param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 0,
    [int]$UiPort = 5173,
    [string]$PreviewId = "default",
    [switch]$Reset,
    [switch]$FreshSession,
    [switch]$BrowserOnly,
    [switch]$EnableSandboxApply,
    [switch]$UseV1
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$versionManifestPath = Join-Path $repoRoot "workbench-version.json"
if (-not (Test-Path -LiteralPath $versionManifestPath -PathType Leaf)) {
    throw "Workbench version manifest was not found at '$versionManifestPath'."
}
$versionManifest = Get-Content -LiteralPath $versionManifestPath -Raw | ConvertFrom-Json
$workbenchVersion = [string]$versionManifest.version
if ($versionManifest.schemaVersion -ne 1 -or $workbenchVersion -notmatch '^\d+\.\d+\.\d+-preview\.\d+$') {
    throw "Workbench version manifest is invalid."
}
. (Join-Path $PSScriptRoot "localtest-seed-contract.ps1")
$seedContract = Get-LocalTestSeedContract -PreviewId $PreviewId
$PreviewId = [string]$seedContract.previewId
$baselineProject = $seedContract.projects | Where-Object key -eq "baseline" | Select-Object -First 1
if ($ProjectId -le 0) {
    $ProjectId = [int]$baselineProject.id
}
$shellRoot = Join-Path $repoRoot "IronDev.TauriShell"
$startupTimestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
$sessionId = [Guid]::NewGuid().ToString("N")
$sessionRoot = Join-Path $env:TEMP ("irondev-localtest-sessions\{0}" -f $sessionId)
New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
$apiOut = Join-Path $sessionRoot "api.stdout.log"
$apiErr = Join-Path $sessionRoot "api.stderr.log"
$apiApplicationLog = Join-Path $sessionRoot "api.application.log"
$uiOut = Join-Path $sessionRoot "ui.stdout.log"
$uiErr = Join-Path $sessionRoot "ui.stderr.log"
$sessionManifestPath = Join-Path $sessionRoot "session-manifest.json"
$previewArgument = if ($PreviewId -eq "default") { "" } else { " -PreviewId $PreviewId" }
$v1Argument = if ($UseV1) { " -UseV1" } else { "" }
$resetCommand = ".\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset$previewArgument$v1Argument"
$sandboxApplyRestartCommand = ".\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply$previewArgument$v1Argument"
$sessionMode = if ($EnableSandboxApply) { "ProjectFeatureWork" } else { "SmokeSimulation" }
$sandboxApplyRequested = [bool]$EnableSandboxApply
$sandboxApplyEnabled = $false
$sandboxApplyRoot = if ($EnableSandboxApply) {
    [System.IO.Path]::GetFullPath([string]$seedContract.paths.workspaceRoot).TrimEnd('\')
} else { $null }
$sessionCapabilities = if ($EnableSandboxApply) {
    @("ProjectFeatureWork", "ControlledSandboxApply")
} else {
    @("WorkflowSmokeSimulation")
}
$configuredDatabaseName = [string]$seedContract.database.name
$apiBuildIdentity = $null
$apiBuildCommit = $null
$repositoryCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryCommit)) {
    throw "Could not resolve the repository commit for the LocalTest session manifest."
}
$apiPort = ([Uri]$ApiBaseUrl).Port

function Stop-Listener {
    param([int]$Port)

    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique

    foreach ($processId in $listeners) {
        if ($processId -and $processId -ne 0) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Wait-HttpOk {
    param(
        [string]$Uri,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 700
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                return
            }
        }
        catch {
        }
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for $Uri."
}

function Get-NodeCommand {
    $bundled = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
    if (Test-Path $bundled) {
        return $bundled
    }

    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $node) {
        throw "node was not found. Install Node.js or use the bundled Codex runtime."
    }

    return $node.Source
}

function New-LocalTestJwtKey {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $rng.Dispose()
    }
}

function Get-ResolvedLocalTestConnectionString {
    $settingsPath = Join-Path $repoRoot "IronDev.Api\appsettings.LocalTest.json"
    if (-not (Test-Path $settingsPath)) {
        throw "LocalTest settings file was not found at $settingsPath."
    }

    $settings = Get-Content -Raw -Path $settingsPath | ConvertFrom-Json
    $connectionString = $settings.ConnectionStrings.IronDeveloperDb
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "LocalTest IronDeveloperDb connection string is missing."
    }

    Add-Type -AssemblyName System.Data
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connectionString)
    if ($builder.DataSource -notmatch '^\(localdb\)\\') {
        throw "LocalTest must use a stable LocalDB instance alias, not '$($builder.DataSource)'."
    }

    $builder["Initial Catalog"] = [string]$seedContract.database.name

    # Never replace the stable LocalDB alias with its ephemeral named pipe. The
    # pipe changes whenever LocalDB restarts while the API process is alive.
    return $builder.ConnectionString
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST")][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body,
        [string]$BearerToken,
        [int]$TimeoutSeconds = 10
    )

    Add-Type -AssemblyName System.Net.Http

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    $request = $null
    try {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Uri)

        if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
            $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $BearerToken)
        }

        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Compress
            $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
        }

        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        if (-not $response.IsSuccessStatusCode) {
            throw "HTTP $([int]$response.StatusCode): $text"
        }

        if ([string]::IsNullOrWhiteSpace($text)) {
            return $null
        }

        return $text | ConvertFrom-Json
    }
    finally {
        if ($null -ne $request) {
            $request.Dispose()
        }
        $client.Dispose()
    }
}

function Test-LocalTestAuthenticationContract {
    param(
        [string]$BaseUrl,
        [int]$TimeoutSeconds = 60
    )

    $login = Invoke-JsonRequest `
        -Method "POST" `
        -Uri "$BaseUrl/api/auth/login" `
        -Body $seedContract.credentials `
        -TimeoutSeconds $TimeoutSeconds

    if ($null -eq $login -or [string]::IsNullOrWhiteSpace($login.token)) {
        throw "LocalTest seeded-login check returned no token."
    }

    return $login
}

function Get-LocalTestPreflight {
    param([string]$BaseUrl)

    return Invoke-JsonRequest -Method "GET" -Uri "$BaseUrl/api/localtest/preflight" -TimeoutSeconds 30
}

function Write-LocalTestSessionManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [int]$ApiProcessId = 0,
        [int]$UiProcessId = 0,
        [string]$UiUrl,
        [string]$PreflightState = "ApiOffline",
        [string]$SeededLoginCheckResult = "NotChecked",
        [string]$Failure
    )

    [ordered]@{
        schemaVersion = 2
        sessionId = $sessionId
        status = $Status
        repositoryCommit = $repositoryCommit
        apiPid = if ($ApiProcessId -gt 0) { $ApiProcessId } else { $null }
        uiPid = if ($UiProcessId -gt 0) { $UiProcessId } else { $null }
        apiBaseUrl = $ApiBaseUrl
        uiUrl = $UiUrl
        databaseName = $configuredDatabaseName
        previewId = $PreviewId
        workbenchVersion = $workbenchVersion
        programmePr = [string]$versionManifest.programmePr
        workbenchMode = if ($UseV1) { "V1" } else { "V2" }
        environment = $seedContract.environment
        sessionMode = $sessionMode
        sandboxApplyRequested = $sandboxApplyRequested
        sandboxApplyEnabled = $sandboxApplyEnabled
        sandboxApplyRoot = $sandboxApplyRoot
        capabilities = $sessionCapabilities
        seedContractVersion = $seedContract.schemaVersion
        apiBuildIdentity = $apiBuildIdentity
        apiBuildCommit = $apiBuildCommit
        preflightState = $PreflightState
        seededLoginCheckResult = $SeededLoginCheckResult
        startupTimestampUtc = $startupTimestampUtc
        updatedTimestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
        logs = [ordered]@{
            apiStdout = $apiOut
            apiStderr = $apiErr
            apiApplication = $apiApplicationLog
            uiStdout = $uiOut
            uiStderr = $uiErr
        }
        failure = if ([string]::IsNullOrWhiteSpace($Failure)) { $null } else { $Failure }
        resetCommand = $resetCommand
        sandboxApplyRestartCommand = $sandboxApplyRestartCommand
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $sessionManifestPath -Encoding UTF8
}

function Assert-SafeSandboxApplyRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Controlled sandbox apply root '$Path' is missing. Use -Reset to recreate the contracted LocalTest sandbox."
    }

    $resolved = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path).TrimEnd('\')
    $contracted = [System.IO.Path]::GetFullPath([string]$seedContract.paths.workspaceRoot).TrimEnd('\')
    if (-not $resolved.Equals($contracted, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing sandbox apply root '$resolved'; expected contracted LocalTest root '$contracted'."
    }

    $driveRoot = [System.IO.Path]::GetPathRoot($resolved).TrimEnd('\')
    $protectedRoots = @(
        $driveRoot,
        [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::System),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        [System.IO.Path]::GetFullPath($_).TrimEnd('\')
    }
    if ($protectedRoots -contains $resolved) {
        throw "Refusing protected sandbox apply root '$resolved'."
    }

    if (((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing sandbox apply root '$resolved' because it is a reparse point."
    }

    return $resolved
}

function Get-LocalTestEnvironment {
    param(
        [string]$BaseUrl,
        [string]$Token
    )

    return Invoke-JsonRequest -Method "GET" -Uri "$BaseUrl/api/environment" -BearerToken $Token
}

function Get-LocalTestUiUrl {
    param(
        [int]$Port,
        [bool]$UseFreshSession
    )

    if ($UseFreshSession) {
        return "http://127.0.0.1:$Port/?freshSession=localtest"
    }

    return "http://127.0.0.1:$Port/"
}

function Start-BrowserShell {
    param([int]$Port)

    Stop-Listener -Port $Port
    $node = Get-NodeCommand

    $process = Start-Process -FilePath $node `
        -ArgumentList @("node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", "$Port", "--mode", "localtest") `
        -WorkingDirectory $shellRoot `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $uiOut `
        -RedirectStandardError $uiErr

    Wait-HttpOk -Uri "http://127.0.0.1:$Port/"
    return $process
}

function Get-TauriDesktopProcesses {
    @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "irondev-tauri-shell.exe" })
}

function Stop-TauriDesktopProcesses {
    $desktopProcesses = Get-TauriDesktopProcesses
    if ($desktopProcesses.Count -eq 0) {
        return
    }

    Write-Host "Stopping existing LocalTest Tauri desktop shell"
    Write-Host "  ProcessId: $($desktopProcesses.ProcessId -join ', ')"
    foreach ($process in $desktopProcesses) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $remaining = Get-TauriDesktopProcesses
        if ($remaining.Count -eq 0) {
            return
        }
    } while ((Get-Date) -lt $deadline)

    throw "Existing Tauri desktop shell is still running. Close it and rerun LocalTest startup."
}

$apiLauncherProcess = $null
$apiRuntimeProcessId = 0
$uiProcess = $null
$uiUrl = Get-LocalTestUiUrl -Port $UiPort -UseFreshSession ([bool]$FreshSession)
$preflightState = "ApiOffline"
$seededLoginCheckResult = "NotChecked"

Write-LocalTestSessionManifest -Status "Starting" -UiUrl $uiUrl

try {
    Stop-Listener -Port $apiPort
    Stop-Listener -Port $UiPort

    if ($Reset) {
        & (Join-Path $PSScriptRoot "reset-localtest-data.ps1") -PreviewId $PreviewId
    }

    if ($EnableSandboxApply) {
        $sandboxApplyRoot = Assert-SafeSandboxApplyRoot -Path $sandboxApplyRoot
        $sandboxApplyEnabled = $true
    }

    $apiConnectionString = Get-ResolvedLocalTestConnectionString
    $configuredDatabaseName = ([System.Data.SqlClient.SqlConnectionStringBuilder]::new($apiConnectionString)).InitialCatalog
    $apiEnvironmentVariables = [ordered]@{
        ASPNETCORE_ENVIRONMENT = "LocalTest"
        ASPNETCORE_URLS = $ApiBaseUrl.TrimEnd('/')
        Cors__AllowedOrigins__0 = "http://127.0.0.1:$UiPort"
        Cors__AllowedOrigins__1 = "http://localhost:$UiPort"
        IRONDEV_JWT_KEY = New-LocalTestJwtKey
        IRONDEV_LOCALTEST_QUALIFICATION_KEY = New-LocalTestJwtKey
        ConnectionStrings__IronDeveloperDb = $apiConnectionString
        IRONDEV_LOCALTEST_SESSION_ID = $sessionId
        IRONDEV_LOCALTEST_PREVIEW_ID = $PreviewId
        IRONDEV_LOCALTEST_REPOSITORY_COMMIT = $repositoryCommit
        IRONDEV_LOCALTEST_API_BASE_URL = $ApiBaseUrl.TrimEnd('/')
        IRONDEV_LOCALTEST_API_LOG_PATH = $apiApplicationLog
        IRONDEV_LOCALTEST_SESSION_MODE = $sessionMode
        IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED = $sandboxApplyRequested.ToString().ToLowerInvariant()
        IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED = $sandboxApplyEnabled.ToString().ToLowerInvariant()
        IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT = if ($sandboxApplyRoot) { $sandboxApplyRoot } else { "" }
        IRONDEV_LOCALTEST_CAPABILITIES = ($sessionCapabilities -join ";")
        LocalTest__WorkspaceRoot = [string]$seedContract.paths.workspaceRoot
        LocalTest__LogsRoot = [string]$seedContract.paths.logsRoot
        LocalTest__WeaviatePrefix = if ($PreviewId -eq "default") { "irondev_test" } else { "irondev_test_$($PreviewId.Replace('-', '_'))" }
        WorkbenchV2__Version = $workbenchVersion
        WorkbenchV2__Enabled = (-not [bool]$UseV1).ToString().ToLowerInvariant()
        WorkbenchV2__V1FallbackEnabled = "true"
        WorkbenchV2__PreviewId = $PreviewId
        SkeletonApply__Enabled = $sandboxApplyEnabled.ToString().ToLowerInvariant()
        SkeletonApply__SandboxRoot = if ($sandboxApplyRoot) { $sandboxApplyRoot } else { "" }
        SkeletonApply__LauncherCapabilityDeclared = $sandboxApplyRequested.ToString().ToLowerInvariant()
        SkeletonApply__LauncherSessionId = $sessionId
    }
    $previousApiEnvironment = @{}
    foreach ($entry in $apiEnvironmentVariables.GetEnumerator()) {
        $previousApiEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
        [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
    }

    try {
        $apiLauncherProcess = Start-Process -FilePath dotnet `
            -ArgumentList @("run", "--no-launch-profile", "--project", "IronDev.Api\IronDev.Api.csproj") `
            -WorkingDirectory $repoRoot `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $apiOut `
            -RedirectStandardError $apiErr
    }
    finally {
        foreach ($entry in $previousApiEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
        }
    }

    Wait-HttpOk -Uri "$ApiBaseUrl/health" -TimeoutSeconds 90
    $preflightState = "ApiConnected"
    Write-LocalTestSessionManifest `
        -Status "Preflight" `
        -ApiProcessId $apiLauncherProcess.Id `
        -UiUrl $uiUrl `
        -PreflightState $preflightState

    $preflight = Get-LocalTestPreflight -BaseUrl $ApiBaseUrl
    $preflightState = [string]$preflight.state
    $apiRuntimeProcessId = [int]$preflight.apiPid
    $apiBuildIdentity = [string]$preflight.apiBuildIdentity
    $apiBuildCommit = [string]$preflight.apiBuildCommit
    if ($preflightState -ne "LocalTestReady") {
        throw "LocalTest preflight returned $preflightState. $($preflight.detail)"
    }
    if ($preflight.apiBaseUrl.TrimEnd('/') -ne $ApiBaseUrl.TrimEnd('/')) {
        throw "LocalTest preflight API identity '$($preflight.apiBaseUrl)' does not match launcher API '$ApiBaseUrl'."
    }

    try {
        $login = Test-LocalTestAuthenticationContract -BaseUrl $ApiBaseUrl
        $seededLoginCheckResult = "Passed"
    }
    catch {
        $seededLoginCheckResult = "Failed"
        throw
    }
    $environment = Get-LocalTestEnvironment -BaseUrl $ApiBaseUrl -Token $login.token

    if ($environment.environment -ne $seedContract.environment -or
        $environment.database -ne $seedContract.database.name -or
        -not $environment.isTestEnvironment) {
        throw "API environment check failed. Refusing to start UI against $($environment.environment)/$($environment.database)."
    }

    $env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl.TrimEnd('/')
    $env:VITE_IRONDEV_PROJECT_ID = if ($FreshSession) { "none" } else { "$ProjectId" }
    $env:IRONDEV_API_PROXY_TARGET = $ApiBaseUrl.TrimEnd('/')
    $env:VITE_IRONDEV_LOCALTEST_SESSION_ID = $sessionId
    $env:VITE_IRONDEV_PREVIEW_ID = $PreviewId
    $env:VITE_IRONDEV_WORKBENCH_VERSION = $workbenchVersion
    $env:VITE_IRONDEV_WORKBENCH_MODE = if ($UseV1) { "V1" } else { "V2" }
    $env:VITE_IRONDEV_LOCALTEST_REPOSITORY_COMMIT = $repositoryCommit
    $env:VITE_IRONDEV_LOCALTEST_API_BASE_URL = $ApiBaseUrl.TrimEnd('/')
    $env:VITE_IRONDEV_LOCALTEST_SESSION_MODE = $sessionMode
    $env:VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED = $sandboxApplyRequested.ToString().ToLowerInvariant()
    $env:VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED = $sandboxApplyEnabled.ToString().ToLowerInvariant()
    $env:VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT = if ($sandboxApplyRoot) { $sandboxApplyRoot } else { "" }
    $env:VITE_IRONDEV_LOCALTEST_CAPABILITIES = ($sessionCapabilities -join ";")

    Write-Host ""
    Write-Host "PASS LocalTest API started"
    Write-Host "  API: $ApiBaseUrl"
    Write-Host "  API PID: $apiRuntimeProcessId"
    Write-Host "  API build: $($preflight.apiBuildIdentity)"
    Write-Host "  Workbench: $($preflight.workbenchVersion) $($preflight.workbenchMode)"
    Write-Host "  Preview: $($preflight.previewId)"
    Write-Host "  Environment: $($environment.environment)"
    Write-Host "  Database: $($environment.database)"
    Write-Host "  Workspace: $($environment.workspaceRoot)"
    Write-Host "PASS LocalTest authentication contract"
    Write-Host "  Login: $($seedContract.credentials.email)"
    if ($FreshSession) {
        Write-Host "PASS LocalTest fresh client session requested"
        Write-Host "  Clears only: irondev.token, irondev.tenantId, irondev.selectedProjectId"
    }
    Write-Host ""

    if ($BrowserOnly) {
        $uiProcess = Start-BrowserShell -Port $UiPort
        Write-LocalTestSessionManifest `
            -Status "Ready" `
            -ApiProcessId $apiRuntimeProcessId `
            -UiProcessId $uiProcess.Id `
            -UiUrl $uiUrl `
            -PreflightState $preflightState `
            -SeededLoginCheckResult $seededLoginCheckResult
        Write-Host "PASS LocalTest browser shell started"
        Write-Host "  UI: $uiUrl"
        Write-Host "  UI PID: $($uiProcess.Id)"
        Write-Host "  Session manifest: $sessionManifestPath"
        Write-Host ""
        Write-Host "Processes are left running. Stop ports $apiPort/$UiPort when finished."
        return
    }

    Write-Host "Starting Tauri desktop shell. Close the desktop window or stop this command when finished."
    $node = Get-NodeCommand
    $tauriCli = Join-Path $shellRoot "node_modules\@tauri-apps\cli\tauri.js"
    if (-not (Test-Path $tauriCli)) {
        throw "Tauri CLI was not found at $tauriCli. Restore IronDev.TauriShell dependencies first."
    }

    $tauriLocalTestConfig = Join-Path $sessionRoot "irondev-tauri-localtest.conf.json"
    @{
        build = @{
            beforeDevCommand = ""
            devUrl = $uiUrl
        }
    } | ConvertTo-Json -Depth 4 | Set-Content -Path $tauriLocalTestConfig -Encoding UTF8

    Stop-TauriDesktopProcesses
    $uiProcess = Start-BrowserShell -Port $UiPort
    Write-LocalTestSessionManifest `
        -Status "Ready" `
        -ApiProcessId $apiRuntimeProcessId `
        -UiProcessId $uiProcess.Id `
        -UiUrl $uiUrl `
        -PreflightState $preflightState `
        -SeededLoginCheckResult $seededLoginCheckResult
    Write-Host "PASS LocalTest browser shell started"
    Write-Host "  UI: $uiUrl"
    Write-Host "  Session manifest: $sessionManifestPath"
    Write-Host ""

    Push-Location $shellRoot
    try {
        $existingDesktopProcessIds = @(Get-TauriDesktopProcesses | Select-Object -ExpandProperty ProcessId)

        $tauriProcess = Start-Process -FilePath $node `
            -ArgumentList @($tauriCli, "dev", "--config", $tauriLocalTestConfig, "--no-dev-server-wait") `
            -WorkingDirectory $shellRoot `
            -NoNewWindow `
            -PassThru `
            -Wait

        if ($tauriProcess.ExitCode -ne 0) {
            $desktopProcess = Get-TauriDesktopProcesses |
                Where-Object { $existingDesktopProcessIds -notcontains $_.ProcessId } |
                Select-Object -First 1

            if ($desktopProcess) {
                Write-Host "PASS LocalTest Tauri desktop shell started"
                Write-Host "  ProcessId: $($desktopProcess.ProcessId)"
                Write-Host "  Tauri CLI exited with code $($tauriProcess.ExitCode) after launching the desktop process."
                Write-Host "  Waiting for the desktop shell to close."
                Wait-Process -Id $desktopProcess.ProcessId
                return
            }

            throw "Tauri desktop shell exited with code $($tauriProcess.ExitCode)."
        }
    }
    finally {
        Pop-Location
        Stop-Listener -Port $UiPort
    }
}
catch {
    $failure = $_.Exception.Message
    if ($null -ne $uiProcess) {
        Stop-Process -Id $uiProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Stop-Listener -Port $UiPort
    Stop-Listener -Port $apiPort
    if ($apiRuntimeProcessId -gt 0) {
        Stop-Process -Id $apiRuntimeProcessId -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $apiLauncherProcess) {
        Stop-Process -Id $apiLauncherProcess.Id -Force -ErrorAction SilentlyContinue
    }

    Write-LocalTestSessionManifest `
        -Status "Failed" `
        -ApiProcessId $(if ($apiRuntimeProcessId -gt 0) { $apiRuntimeProcessId } elseif ($null -ne $apiLauncherProcess) { $apiLauncherProcess.Id } else { 0 }) `
        -UiProcessId $(if ($null -ne $uiProcess) { $uiProcess.Id } else { 0 }) `
        -UiUrl $uiUrl `
        -PreflightState $preflightState `
        -SeededLoginCheckResult $seededLoginCheckResult `
        -Failure $failure

    Write-Host ""
    Write-Host "FAIL LocalTest front-door trust"
    Write-Host "  State: $preflightState"
    Write-Host "  Error: $failure"
    Write-Host "  Safe reset: $resetCommand"
    Write-Host "  Session manifest: $sessionManifestPath"
    Write-Host "  API stdout: $apiOut"
    Write-Host "  API stderr: $apiErr"
    Write-Host "  API error log: $apiApplicationLog"
    throw
}

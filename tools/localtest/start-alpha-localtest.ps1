param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 1,
    [int]$UiPort = 5173,
    [switch]$Reset,
    [switch]$FreshSession,
    [switch]$BrowserOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$shellRoot = Join-Path $repoRoot "IronDev.TauriShell"
$apiOut = Join-Path $env:TEMP "irondev-localtest-api.out.log"
$apiErr = Join-Path $env:TEMP "irondev-localtest-api.err.log"
$uiOut = Join-Path $env:TEMP "irondev-localtest-ui.out.log"
$uiErr = Join-Path $env:TEMP "irondev-localtest-ui.err.log"
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

function Resolve-LocalDbDataSource {
    param([Parameter(Mandatory = $true)][string]$DataSource)

    if ($DataSource -notmatch '^\(localdb\)\\(?<instance>.+)$') {
        return $DataSource
    }

    $instance = $Matches.instance
    $localDb = Get-Command sqllocaldb -ErrorAction SilentlyContinue
    if ($null -ne $localDb) {
        $startExitCode = 0
        $startOutput = @()
        try {
            $startOutput = & $localDb.Source start $instance 2>&1
            $startExitCode = $LASTEXITCODE
        }
        catch {
            $startOutput += $_.Exception.Message
            $startExitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 1 }
        }
        if ($startExitCode -ne 0) {
            Write-Warning "Could not start LocalDB instance '$instance' through sqllocaldb; attempting to resolve an existing named pipe."
            if ($startOutput.Count -gt 0) {
                Write-Warning (($startOutput | Out-String).Trim())
            }
        }

        $info = @()
        try {
            $info = & $localDb.Source info $instance 2>$null
        }
        catch {
            $info = @()
        }
        foreach ($line in $info) {
            $match = [regex]::Match($line, '^\s*Instance pipe name:\s*(?<pipe>.+?)\s*$')
            if ($match.Success -and -not [string]::IsNullOrWhiteSpace($match.Groups['pipe'].Value)) {
                $pipe = $match.Groups['pipe'].Value.Trim()
                if ($pipe.StartsWith("np:", [StringComparison]::OrdinalIgnoreCase)) {
                    return $pipe
                }
                return "np:$pipe"
            }
        }
    }

    $errorLog = Join-Path $env:LOCALAPPDATA "Microsoft\Microsoft SQL Server Local DB\Instances\$instance\error.log"
    if (Test-Path -LiteralPath $errorLog -PathType Leaf) {
        $pipeAnnouncement = Select-String -LiteralPath $errorLog -Pattern 'Server local connection provider is ready to accept connection on' |
            Select-Object -Last 1
        if ($null -ne $pipeAnnouncement) {
            $match = [regex]::Match($pipeAnnouncement.Line, 'Server local connection provider is ready to accept connection on \[(?<pipe>[^\]]+)\]')
            if ($match.Success) {
                $pipe = $match.Groups['pipe'].Value.Trim()
                if ($pipe -like '\\.\pipe\*\tsql\query') {
                    if ($pipe.StartsWith("np:", [StringComparison]::OrdinalIgnoreCase)) {
                        return $pipe
                    }
                    return "np:$pipe"
                }
            }
        }
    }

    throw "Could not resolve LocalDB instance '$instance' to a SQL Server named pipe."
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
    $builder["Data Source"] = Resolve-LocalDbDataSource -DataSource $builder.DataSource
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

    try {
        $login = Invoke-JsonRequest `
            -Method "POST" `
            -Uri "$BaseUrl/api/auth/login" `
            -Body @{ email = "bob@irondev.local"; password = "change-me-local-only" } `
            -TimeoutSeconds $TimeoutSeconds

        if ($null -eq $login -or [string]::IsNullOrWhiteSpace($login.token)) {
            throw "Login returned no token."
        }

        return $login
    }
    catch {
        Stop-Listener -Port $apiPort
        Write-Host ""
        Write-Host "FAIL LocalTest authentication contract"
        Write-Host "Expected seeded account was rejected."
        Write-Host "Run reset-localtest-data.ps1 or inspect the API error log."
        Write-Host "  API log: $apiErr"
        Write-Host "  Error: $($_.Exception.Message)"
        throw "LocalTest authentication contract failed."
    }
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

function Stop-RepoLocalTestProcesses {
    $normalizedShellRoot = [System.IO.Path]::GetFullPath($shellRoot)
    $processes = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $name = $_.Name
        $command = $_.CommandLine
        if ([string]::IsNullOrWhiteSpace($name)) {
            return $false
        }
        if ([string]::IsNullOrWhiteSpace($command)) {
            $command = ""
        }

        $isApiDotnet = $name -eq "dotnet.exe" -and
            $command.IndexOf("IronDev.Api\IronDev.Api.csproj", [StringComparison]::OrdinalIgnoreCase) -ge 0
        $isViteNode = $name -eq "node.exe" -and
            $command.IndexOf("node_modules/vite/bin/vite.js", [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $command.IndexOf($normalizedShellRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0
        $isTauriNode = $name -eq "node.exe" -and
            $command.IndexOf("@tauri-apps\cli\tauri.js", [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $command.IndexOf($normalizedShellRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0
        $isCargo = $name -eq "cargo.exe" -and
            $command.IndexOf((Join-Path $normalizedShellRoot "src-tauri"), [StringComparison]::OrdinalIgnoreCase) -ge 0
        $isDesktop = $name -eq "irondev-tauri-shell.exe"

        return $isApiDotnet -or $isViteNode -or $isTauriNode -or $isCargo -or $isDesktop
    })

    foreach ($process in $processes) {
        if ($process.ProcessId -and $process.ProcessId -ne $PID) {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Start-BrowserShell {
    param([int]$Port)

    Stop-Listener -Port $Port
    $node = Get-NodeCommand

    Start-Process -FilePath $node `
        -ArgumentList @("node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", "$Port", "--mode", "localtest") `
        -WorkingDirectory $shellRoot `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $uiOut `
        -RedirectStandardError $uiErr | Out-Null

    Wait-HttpOk -Uri "http://127.0.0.1:$Port/"
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

Stop-RepoLocalTestProcesses
Stop-Listener -Port $apiPort
Stop-Listener -Port $UiPort

if ($Reset) {
    & (Join-Path $PSScriptRoot "reset-localtest-data.ps1")
}

$apiConnectionString = Get-ResolvedLocalTestConnectionString
$previousJwtKey = $env:IRONDEV_JWT_KEY
$previousConnectionString = $env:ConnectionStrings__IronDeveloperDb
$env:IRONDEV_JWT_KEY = New-LocalTestJwtKey
$env:ConnectionStrings__IronDeveloperDb = $apiConnectionString
try {
    Start-Process -FilePath dotnet `
        -ArgumentList @("run", "--launch-profile", "LocalTest", "--project", "IronDev.Api\IronDev.Api.csproj") `
        -WorkingDirectory $repoRoot `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $apiOut `
        -RedirectStandardError $apiErr | Out-Null
}
finally {
    $env:IRONDEV_JWT_KEY = $previousJwtKey
    $env:ConnectionStrings__IronDeveloperDb = $previousConnectionString
}

Wait-HttpOk -Uri "$ApiBaseUrl/health" -TimeoutSeconds 90
$login = Test-LocalTestAuthenticationContract -BaseUrl $ApiBaseUrl
$environment = Get-LocalTestEnvironment -BaseUrl $ApiBaseUrl -Token $login.token

if ($environment.environment -ne "LocalTest" -or $environment.database -notmatch "Test" -or -not $environment.isTestEnvironment) {
    Stop-Listener -Port $apiPort
    throw "API environment check failed. Refusing to start UI against $($environment.environment)/$($environment.database)."
}

$env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl
$env:VITE_IRONDEV_PROJECT_ID = if ($FreshSession) { "none" } else { "$ProjectId" }
$env:IRONDEV_API_PROXY_TARGET = $ApiBaseUrl
$uiUrl = Get-LocalTestUiUrl -Port $UiPort -UseFreshSession ([bool]$FreshSession)

Write-Host ""
Write-Host "PASS LocalTest API started"
Write-Host "  API: $ApiBaseUrl"
Write-Host "  Environment: $($environment.environment)"
Write-Host "  Database: $($environment.database)"
Write-Host "  Workspace: $($environment.workspaceRoot)"
Write-Host "PASS LocalTest authentication contract"
Write-Host "  Login: bob@irondev.local"
if ($FreshSession) {
    Write-Host "PASS LocalTest fresh client session requested"
    Write-Host "  Clears only: irondev.token, irondev.tenantId, irondev.selectedProjectId"
}
Write-Host ""

if ($BrowserOnly) {
    Start-BrowserShell -Port $UiPort
    Write-Host "PASS LocalTest browser shell started"
    Write-Host "  UI: $uiUrl"
    Write-Host ""
    Write-Host "Processes are left running. Stop ports 5000/$UiPort when finished."
    exit 0
}

Write-Host "Starting Tauri desktop shell. Close the desktop window or stop this command when finished."
$node = Get-NodeCommand
$tauriCli = Join-Path $shellRoot "node_modules\@tauri-apps\cli\tauri.js"
if (-not (Test-Path $tauriCli)) {
    throw "Tauri CLI was not found at $tauriCli. Restore IronDev.TauriShell dependencies first."
}

$tauriLocalTestConfig = Join-Path $env:TEMP "irondev-tauri-localtest.conf.json"
@{
    build = @{
        beforeDevCommand = ""
        devUrl = $uiUrl
    }
} | ConvertTo-Json -Depth 4 | Set-Content -Path $tauriLocalTestConfig -Encoding UTF8

Stop-TauriDesktopProcesses
Start-BrowserShell -Port $UiPort
Write-Host "PASS LocalTest browser shell started"
Write-Host "  UI: $uiUrl"
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

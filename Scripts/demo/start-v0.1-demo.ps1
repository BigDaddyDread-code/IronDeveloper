[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$NoStart,
    [switch]$OpenBrowser,
    [switch]$Json,
    [switch]$Markdown,
    [ValidateSet("Deterministic", "Live")]
    [string]$ModelMode = "Deterministic",
    [string]$Project = "BookSeller",
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$UiBaseUrl = "http://127.0.0.1:5173",
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$DatabaseName = "IronDeveloper_Local",
    [int]$StartTimeoutSeconds = 75,
    [string]$OutputDirectory,
    [switch]$CreateLiveChatTicket
)

$ErrorActionPreference = "Stop"

$BoundaryStatement = "The demo startup script coordinates local checks, optional process start commands, and demo seed invocation. It is evidence only: it does not approve, satisfy policy, continue workflow, apply source by itself, claim live model proof, release readiness, deployment readiness, or authority."
$DeterministicBanner = "Deterministic-only local alpha preview. This is not a live model run."
$LiveUnsupportedBanner = "Live model demo mode is not enabled by this startup path. No silent deterministic fallback is allowed."

$KnownReasonCodes = @(
    "DemoStartupRepoRootNotFound",
    "DemoStartupRootUnsafe",
    "DemoStartupToolMissing",
    "DemoStartupSqlUnavailable",
    "DemoStartupApiBaseUrlNotLocal",
    "DemoStartupApiUnavailable",
    "DemoStartupApiStarted",
    "DemoStartupUiBaseUrlNotLocal",
    "DemoStartupUiUnavailable",
    "DemoStartupUiStarted",
    "DemoStartupSeedUnavailable",
    "DemoStartupSeedPassed",
    "DemoStartupLiveModelUnsupported",
    "DemoStartupOpenSkipped",
    "DemoStartupPassed"
)

$script:Stages = New-Object System.Collections.Generic.List[object]
$script:StartedProcesses = New-Object System.Collections.Generic.List[object]

function Add-Stage {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [ValidateSet("Passed", "Blocked", "Skipped", "Failed")]
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$ReasonCode,
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$NextSafeAction = "",
        [hashtable]$Details = @{}
    )

    $script:Stages.Add([pscustomobject]@{
        stage = $Stage
        status = $Status
        reasonCode = $ReasonCode
        message = $Message
        nextSafeAction = $NextSafeAction
        details = $Details
    }) | Out-Null
}

function Find-RepoRoot {
    $current = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath (Join-Path $current "IronDev.slnx")) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

function Test-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Is-SameOrUnder {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $p = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    $c = [System.IO.Path]::GetFullPath($Child).TrimEnd('\', '/')
    return $c.Equals($p, [System.StringComparison]::OrdinalIgnoreCase) -or
        $c.StartsWith($p + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $c.StartsWith($p + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Has-ReparseAncestor {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = [System.IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $false
}

function Test-SafeDemoOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($full)
    $userProfileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $temp = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')

    if ($full.Equals($pathRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "DriveOrFilesystemRoot"
    }

    if ($full.TrimEnd('\', '/').Equals($userProfileRoot.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
        return "UserHomeRoot"
    }

    if ($full.TrimEnd('\', '/').Equals($temp, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "BroadTempRoot"
    }

    if (Is-SameOrUnder -Parent $RepoRoot -Child $full) {
        return "UnderRepositoryRoot"
    }

    if (Has-ReparseAncestor -Path $full) {
        return "PathContainsSymlinkOrReparsePoint"
    }

    return $null
}

function New-DefaultDemoOutputRoot {
    $base = if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Join-Path $env:LOCALAPPDATA "IronDev\v0.1-demo"
    }
    else {
        Join-Path ([System.IO.Path]::GetTempPath()) "IronDev\v0.1-demo"
    }

    return Join-Path $base $Project
}

function Redact-UserPath {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $result = $Value
    $userHome = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userHome)) {
        $result = $result.Replace($userHome, "<user-home>")
    }

    $temp = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')
    if (-not [string]::IsNullOrWhiteSpace($temp)) {
        $result = $result.Replace($temp, "<temp>")
    }

    return $result
}

function Resolve-PowerShell {
    foreach ($candidate in @("pwsh", "powershell")) {
        if (Test-CommandAvailable $candidate) {
            return $candidate
        }
    }

    return "powershell"
}

function Invoke-ChildScriptQuiet {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $shell = Resolve-PowerShell
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        Push-Location $WorkingDirectory
        try {
            & $shell -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments *> $null
            return $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Test-LoopbackUrl {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "MissingUrl"
    }

    try {
        $uri = [System.Uri]$Value
    }
    catch {
        return "InvalidUrl"
    }

    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        return "UnsupportedScheme"
    }

    if ($uri.IsLoopback) {
        return $null
    }

    return "NonLoopbackHost"
}

function Test-HttpGet {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 2
    )

    try {
        Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec $TimeoutSeconds | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Join-Url {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Path
    )

    return $BaseUrl.TrimEnd('/') + "/" + $Path.TrimStart('/')
}

function Wait-ForEndpoint {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-HttpGet -Url $Url -TimeoutSeconds 2) {
            return $true
        }

        Start-Sleep -Seconds 2
    }

    return $false
}

function Start-ManagedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$LogDirectory
    )

    # DEMO-REHEARSAL-001 finding: a hidden process with no captured output makes
    # "inspect the process output" impossible to follow. Every managed process
    # logs to files the blocked stage can point at.
    New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
    $stdoutPath = Join-Path $LogDirectory "$Name.out.log"
    $stderrPath = Join-Path $LogDirectory "$Name.err.log"

    $shell = Resolve-PowerShell
    $process = Start-Process `
        -FilePath $shell `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $Command) `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    $script:StartedProcesses.Add([pscustomobject]@{
        name = $Name
        processId = $process.Id
        workingDirectory = Redact-UserPath $WorkingDirectory
        stdoutLog = Redact-UserPath $stdoutPath
        stderrLog = Redact-UserPath $stderrPath
    }) | Out-Null
}

function Has-StartupBlocker {
    return $null -ne ($script:Stages |
        Where-Object { $_.status -eq "Blocked" -or $_.status -eq "Failed" } |
        Select-Object -First 1)
}

function Get-FirstStartupBlockerNextSafeAction {
    $candidate = $script:Stages |
        Where-Object { ($_.status -eq "Blocked" -or $_.status -eq "Failed") -and -not [string]::IsNullOrWhiteSpace($_.nextSafeAction) } |
        Select-Object -First 1

    if ($candidate) {
        return $candidate.nextSafeAction
    }

    return "Fix the first blocked startup stage, then rerun Scripts/demo/start-v0.1-demo.ps1 -CheckOnly."
}

function Get-PrimaryNextSafeAction {
    $candidate = $script:Stages |
        Where-Object { ($_.status -eq "Blocked" -or $_.status -eq "Failed") -and -not [string]::IsNullOrWhiteSpace($_.nextSafeAction) } |
        Select-Object -First 1

    if ($candidate) {
        return $candidate.nextSafeAction
    }

    return "Open $UiBaseUrl and sign in with the documented local demo account."
}

function Get-OverallStatus {
    if ($script:Stages | Where-Object { $_.status -eq "Failed" } | Select-Object -First 1) {
        return "Failed"
    }

    if ($script:Stages | Where-Object { $_.status -eq "Blocked" } | Select-Object -First 1) {
        return "Blocked"
    }

    return "Passed"
}

function Convert-ToMarkdown {
    param([Parameter(Mandatory = $true)]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# IronDev v0.1 Demo Startup") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Status: $($Result.status)") | Out-Null
    $lines.Add("- Mode: $($Result.mode)") | Out-Null
    $lines.Add("- Model mode: $($Result.modelMode)") | Out-Null
    $lines.Add("- Banner: $($Result.modelModeBanner)") | Out-Null
    $lines.Add("- App URL: $($Result.appUrl)") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Stages") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| Stage | Status | Reason | Next safe action |") | Out-Null
    $lines.Add("| --- | --- | --- | --- |") | Out-Null
    foreach ($stage in $Result.stages) {
        $lines.Add("| $($stage.stage) | $($stage.status) | $($stage.reasonCode) | $($stage.nextSafeAction) |") | Out-Null
    }
    $lines.Add("") | Out-Null
    $lines.Add("## Next Safe Action") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add('```powershell') | Out-Null
    $lines.Add($Result.nextSafeAction) | Out-Null
    $lines.Add('```') | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("## Boundary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add($Result.boundary) | Out-Null
    return ($lines -join [Environment]::NewLine)
}

function Complete-DemoStartup {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [int]$ExitCode = 0
    )

    $status = Get-OverallStatus
    $nextSafeAction = Get-PrimaryNextSafeAction
    $result = [pscustomobject]@{
        project = $Project
        mode = if ($CheckOnly) { "CheckOnly" } else { "DemoUp" }
        modelMode = $ModelMode
        modelModeBanner = if ($ModelMode -eq "Deterministic") { $DeterministicBanner } else { $LiveUnsupportedBanner }
        liveModelFallbackAllowed = $false
        apiBaseUrl = Redact-UserPath $ApiBaseUrl
        uiBaseUrl = Redact-UserPath $UiBaseUrl
        appUrl = Redact-UserPath $UiBaseUrl
        outputDirectory = Redact-UserPath $OutputRoot
        repoRoot = Redact-UserPath $RepoRoot
        knownReasonCodes = $KnownReasonCodes
        stages = $script:Stages
        startedProcesses = $script:StartedProcesses
        nextSafeAction = $nextSafeAction
        boundary = $BoundaryStatement
        status = $status
    }

    $jsonText = $result | ConvertTo-Json -Depth 30
    $markdownText = Convert-ToMarkdown -Result $result

    if ($Json) {
        $jsonText
    }
    elseif ($Markdown) {
        $markdownText
    }
    else {
        Write-Host "== IronDev v0.1 demo startup =="
        Write-Host "Status: $status"
        Write-Host "Model mode: $($result.modelModeBanner)"
        foreach ($stage in $script:Stages) {
            Write-Host ("{0}: {1} ({2})" -f $stage.stage, $stage.status, $stage.reasonCode)
        }
        Write-Host "App URL: $UiBaseUrl"
        Write-Host "Next safe action: $nextSafeAction"
        Write-Host "Boundary: $BoundaryStatement"
    }

    exit $ExitCode
}

$repoRoot = Find-RepoRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Add-Stage "RepoCheck" "Failed" "DemoStartupRepoRootNotFound" "Could not locate IronDev.slnx." "Run this script from a complete IronDev repository checkout."
    Complete-DemoStartup -RepoRoot "" -OutputRoot "" -ExitCode 1
}

Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = New-DefaultDemoOutputRoot
}
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)

$unsafeOutputReason = Test-SafeDemoOutputRoot -RepoRoot $repoRoot -Path $outputFull
if ($unsafeOutputReason) {
    Add-Stage "RootSafetyCheck" "Blocked" "DemoStartupRootUnsafe" "Planned demo output root is unsafe: $unsafeOutputReason." "Choose an output directory outside the repo, home root, broad temp root, and reparse-point paths." @{
        outputDirectory = Redact-UserPath $outputFull
        unsafeReason = $unsafeOutputReason
    }
}
else {
    Add-Stage "RootSafetyCheck" "Passed" "DemoStartupPassed" "Planned demo output root is safe for local demo artifacts." "" @{
        outputDirectory = Redact-UserPath $outputFull
    }
}

$missingTools = @()
foreach ($tool in @("dotnet", "git", "node", "npm", "sqlcmd")) {
    if (-not (Test-CommandAvailable $tool)) {
        $missingTools += $tool
    }
}

if ($missingTools.Count -gt 0) {
    Add-Stage "ToolchainCheck" "Blocked" "DemoStartupToolMissing" ("Missing tool(s): " + ($missingTools -join ", ")) "Install the missing local toolchain, then rerun Scripts/demo/start-v0.1-demo.ps1 -CheckOnly."
}
else {
    Add-Stage "ToolchainCheck" "Passed" "DemoStartupPassed" "Required startup tools are present." "" @{
        dotnet = $true
        git = $true
        node = $true
        npm = $true
    }
}

if ($ModelMode -eq "Live") {
    Add-Stage "ModelModeCheck" "Blocked" "DemoStartupLiveModelUnsupported" $LiveUnsupportedBanner "Use -ModelMode Deterministic for the v0.1 local alpha demo, or build a separate explicit live-model demo path."
}
else {
    Add-Stage "ModelModeCheck" "Passed" "DemoStartupPassed" $DeterministicBanner
}

$apiUrlIssue = Test-LoopbackUrl -Value $ApiBaseUrl
if ($apiUrlIssue) {
    Add-Stage "ApiUrlCheck" "Blocked" "DemoStartupApiBaseUrlNotLocal" "API base URL is not loopback-local: $apiUrlIssue." "Use a local API URL such as http://127.0.0.1:5000 or http://localhost:5000." @{
        apiBaseUrl = Redact-UserPath $ApiBaseUrl
        urlIssue = $apiUrlIssue
    }
}
else {
    Add-Stage "ApiUrlCheck" "Passed" "DemoStartupPassed" "API base URL is loopback-local." "" @{
        apiBaseUrl = Redact-UserPath $ApiBaseUrl
    }
}

$uiUrlIssue = Test-LoopbackUrl -Value $UiBaseUrl
if ($uiUrlIssue) {
    Add-Stage "UiUrlCheck" "Blocked" "DemoStartupUiBaseUrlNotLocal" "UI base URL is not loopback-local: $uiUrlIssue." "Use a local UI URL such as http://127.0.0.1:5173 or http://localhost:5173." @{
        uiBaseUrl = Redact-UserPath $UiBaseUrl
        urlIssue = $uiUrlIssue
    }
}
else {
    Add-Stage "UiUrlCheck" "Passed" "DemoStartupPassed" "UI base URL is loopback-local." "" @{
        uiBaseUrl = Redact-UserPath $UiBaseUrl
    }
}

$sqlScript = Join-Path $repoRoot "Scripts\local\sql-local.ps1"
if (-not (Test-CommandAvailable "sqlcmd")) {
    Add-Stage "SqlCheck" "Blocked" "DemoStartupSqlUnavailable" "sqlcmd is not available for local SQL readiness checks." "Install SQL Server command-line tools, then rerun Scripts/demo/start-v0.1-demo.ps1 -CheckOnly."
}
elseif (Test-Path -LiteralPath $sqlScript) {
    $sqlExit = Invoke-ChildScriptQuiet -ScriptPath $sqlScript -WorkingDirectory $repoRoot -Arguments @(
        "-CheckOnly",
        "-NonInteractive",
        "-ServerInstance",
        $SqlServer,
        "-DatabaseName",
        $DatabaseName
    )

    if ($sqlExit -eq 0) {
        Add-Stage "SqlCheck" "Passed" "DemoStartupPassed" "Local SQL check-only command accepted the configured local database target." "" @{
            databaseName = $DatabaseName
        }
    }
    else {
        Add-Stage "SqlCheck" "Blocked" "DemoStartupSqlUnavailable" "Local SQL check-only command did not accept or reach the configured target." "Run Scripts/local/sql-local.ps1 -CheckOnly -ServerInstance '$SqlServer' -DatabaseName '$DatabaseName', then fix the first blocker."
    }
}
else {
    Add-Stage "SqlCheck" "Blocked" "DemoStartupSqlUnavailable" "Scripts/local/sql-local.ps1 is missing." "Restore the local SQL helper before running the demo startup script."
}

$apiHealthUrl = Join-Url -BaseUrl $ApiBaseUrl -Path "/health"
if (Has-StartupBlocker) {
    Add-Stage "ApiCheck" "Skipped" "DemoStartupApiUnavailable" "API start/check skipped because an earlier startup blocker exists." (Get-FirstStartupBlockerNextSafeAction)
}
elseif (Test-HttpGet -Url $apiHealthUrl) {
    Add-Stage "ApiCheck" "Passed" "DemoStartupPassed" "API health endpoint is already reachable." "" @{
        apiBaseUrl = Redact-UserPath $ApiBaseUrl
    }
}
elseif ($CheckOnly -or $NoStart) {
    Add-Stage "ApiCheck" "Blocked" "DemoStartupApiUnavailable" "API health endpoint is not reachable and start mode is disabled." "Run Scripts/demo/start-v0.1-demo.ps1 without -CheckOnly/-NoStart, or start the API with: dotnet run --project IronDev.Api/IronDev.Api.csproj --urls $ApiBaseUrl"
}
else {
    # DEMO-REHEARSAL-001 finding: the API refuses to start without a JWT signing
    # key, and the demo previously hid that death. A demo session gets an
    # ephemeral, session-local key unless the operator configured their own —
    # stated openly, never committed, never reused as authority.
    if ([string]::IsNullOrWhiteSpace($env:IRONDEV_JWT_KEY) -and [string]::IsNullOrWhiteSpace($env:Jwt__Key)) {
        $env:IRONDEV_JWT_KEY = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
        Add-Stage "JwtKeyCheck" "Passed" "DemoStartupPassed" "Generated a session-local JWT signing key for this demo process tree only. It is not persisted and grants nothing beyond local demo auth." ""
    }
    else {
        Add-Stage "JwtKeyCheck" "Passed" "DemoStartupPassed" "Using the operator-configured JWT signing key from the environment." ""
    }

    # DEMO-REHEARSAL-001 finding: without an explicit override the API reads
    # appsettings.Development.json and points at the REAL IronDeveloper catalog
    # while the demo advertises -DatabaseName. Pin the demo API to the demo
    # database explicitly — the connection chooses the database, always.
    $env:ConnectionStrings__IronDeveloperDb = "Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
    Add-Stage "ApiDatabasePin" "Passed" "DemoStartupPassed" "Demo API pinned to the demo database target." "" @{
        databaseName = $DatabaseName
    }

    $apiProject = Join-Path $repoRoot "IronDev.Api\IronDev.Api.csproj"
    $apiCommand = "& dotnet run --project `"$apiProject`" --urls `"$ApiBaseUrl`""
    Start-ManagedProcess -Name "IronDev.Api" -Command $apiCommand -WorkingDirectory $repoRoot -LogDirectory $outputFull
    if (Wait-ForEndpoint -Url $apiHealthUrl -TimeoutSeconds $StartTimeoutSeconds) {
        Add-Stage "ApiCheck" "Passed" "DemoStartupApiStarted" "Started API and verified the health endpoint." "" @{
            apiBaseUrl = Redact-UserPath $ApiBaseUrl
        }
    }
    else {
        Add-Stage "ApiCheck" "Blocked" "DemoStartupApiUnavailable" "API did not become reachable before the startup timeout." "Read $(Redact-UserPath (Join-Path $outputFull 'IronDev.Api.err.log')) and $(Redact-UserPath (Join-Path $outputFull 'IronDev.Api.out.log')), fix the first reported blocker, then rerun the demo startup script."
    }
}

if (Has-StartupBlocker) {
    Add-Stage "UiCheck" "Skipped" "DemoStartupUiUnavailable" "UI start/check skipped because an earlier startup blocker exists." (Get-FirstStartupBlockerNextSafeAction)
}
elseif (Test-HttpGet -Url $UiBaseUrl) {
    Add-Stage "UiCheck" "Passed" "DemoStartupPassed" "UI dev server is already reachable." "" @{
        uiBaseUrl = Redact-UserPath $UiBaseUrl
    }
}
elseif ($CheckOnly -or $NoStart) {
    Add-Stage "UiCheck" "Blocked" "DemoStartupUiUnavailable" "UI dev server is not reachable and start mode is disabled." "Run Scripts/demo/start-v0.1-demo.ps1 without -CheckOnly/-NoStart, or start the UI with: cd IronDev.TauriShell; npm run dev"
}
else {
    $uiRoot = Join-Path $repoRoot "IronDev.TauriShell"
    $uiCommand = "`$env:VITE_IRONDEV_API_BASE_URL = `"$ApiBaseUrl`"; npm run dev -- --host 127.0.0.1"
    Start-ManagedProcess -Name "IronDev.TauriShell" -Command $uiCommand -WorkingDirectory $uiRoot -LogDirectory $outputFull
    if (Wait-ForEndpoint -Url $UiBaseUrl -TimeoutSeconds $StartTimeoutSeconds) {
        Add-Stage "UiCheck" "Passed" "DemoStartupUiStarted" "Started UI dev server and verified the app URL." "" @{
            uiBaseUrl = Redact-UserPath $UiBaseUrl
        }
    }
    else {
        Add-Stage "UiCheck" "Blocked" "DemoStartupUiUnavailable" "UI dev server did not become reachable before the startup timeout." "Read $(Redact-UserPath (Join-Path $outputFull 'IronDev.TauriShell.err.log')) and $(Redact-UserPath (Join-Path $outputFull 'IronDev.TauriShell.out.log')), fix the first reported blocker, then rerun the demo startup script."
    }
}

$seedScript = Join-Path $repoRoot "Scripts\demo\demo-seed.ps1"
if (Has-StartupBlocker) {
    Add-Stage "DemoSeedCheck" "Skipped" "DemoStartupSeedUnavailable" "Demo seed was skipped because an earlier startup blocker exists." (Get-FirstStartupBlockerNextSafeAction)
}
elseif (-not (Test-Path -LiteralPath $seedScript)) {
    Add-Stage "DemoSeedCheck" "Blocked" "DemoStartupSeedUnavailable" "Scripts/demo/demo-seed.ps1 is missing." "Restore the demo seed script before starting the local alpha demo."
}
elseif ($CheckOnly) {
    $seedExit = Invoke-ChildScriptQuiet -ScriptPath $seedScript -WorkingDirectory $repoRoot -Arguments @("-CheckOnly", "-Json")
    if ($seedExit -eq 0) {
        Add-Stage "DemoSeedCheck" "Passed" "DemoStartupSeedPassed" "Demo seed check-only path is available." "" @{
            command = "Scripts/demo/demo-seed.ps1 -CheckOnly -Json"
        }
    }
    else {
        Add-Stage "DemoSeedCheck" "Blocked" "DemoStartupSeedUnavailable" "Demo seed check-only path failed." "Run Scripts/demo/demo-seed.ps1 -CheckOnly -Json and fix the first reported blocker."
    }
}
else {
    $seedArguments = @(
        "-Seed",
        "-Project",
        $Project,
        "-ModelMode",
        "Deterministic",
        "-ApiBaseUrl",
        $ApiBaseUrl,
        "-OutputDirectory",
        $outputFull,
        "-Json"
    )
    if ($CreateLiveChatTicket) {
        $seedArguments += "-CreateLiveChatTicket"
    }

    $seedExit = Invoke-ChildScriptQuiet -ScriptPath $seedScript -WorkingDirectory $repoRoot -Arguments $seedArguments
    if ($seedExit -eq 0) {
        Add-Stage "DemoSeedCheck" "Passed" "DemoStartupSeedPassed" "Demo seed completed through the long-lived local API path." "" @{
            command = "Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic"
        }
    }
    else {
        Add-Stage "DemoSeedCheck" "Blocked" "DemoStartupSeedUnavailable" "Demo seed failed or blocked." "Run Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic -ApiBaseUrl $ApiBaseUrl -OutputDirectory '$outputFull' -Json and fix the first reported blocker."
    }
}

if ($OpenBrowser -and -not (Has-StartupBlocker)) {
    Start-Process $UiBaseUrl | Out-Null
    Add-Stage "OpenApp" "Passed" "DemoStartupPassed" "Opened the demo app URL in the default browser." "" @{
        appUrl = Redact-UserPath $UiBaseUrl
    }
}
elseif ($OpenBrowser) {
    Add-Stage "OpenApp" "Skipped" "DemoStartupOpenSkipped" "Browser open skipped because startup is blocked." (Get-FirstStartupBlockerNextSafeAction)
}
else {
    Add-Stage "OpenApp" "Skipped" "DemoStartupOpenSkipped" "Browser open not requested; app URL printed for the operator." "" @{
        appUrl = Redact-UserPath $UiBaseUrl
    }
}

$finalExitCode = if ($CheckOnly) { 0 } elseif ((Get-OverallStatus) -eq "Passed") { 0 } else { 1 }
Complete-DemoStartup -RepoRoot $repoRoot -OutputRoot $outputFull -ExitCode $finalExitCode

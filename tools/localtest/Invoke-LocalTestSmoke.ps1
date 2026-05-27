param(
    [switch]$Reset,
    [switch]$StartServices,
    [switch]$KeepServices,
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$UiBaseUrl = "http://127.0.0.1:5173",
    [int]$UiPort = 5173
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$shellRoot = Join-Path $repoRoot "IronDev.TauriShell"
$reportRoot = Join-Path $PSScriptRoot "reports"
$jsonReportPath = Join-Path $reportRoot "latest-localtest-report.json"
$markdownReportPath = Join-Path $reportRoot "latest-localtest-report.md"
$playwrightJsonPath = Join-Path $shellRoot "reports\playwright-report.json"
$startedPorts = New-Object System.Collections.Generic.List[int]

New-Item -ItemType Directory -Force -Path $reportRoot | Out-Null

$environment = $null
$localTestCounts = $null
$devProbe = $null

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
    $bundled = "C:\Users\bob\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
    if (Test-Path $bundled) {
        return $bundled
    }

    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $node) {
        throw "node was not found. Install Node.js or use the bundled Codex runtime."
    }

    return $node.Source
}

function Get-LocalTestConnectionInfo {
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
    $normalDatabase = if ($builder.InitialCatalog -match "(?i)_Test$") {
        $builder.InitialCatalog -replace "(?i)_Test$", ""
    }
    else {
        "IronDeveloper"
    }

    return [ordered]@{
        server = $builder.DataSource
        localTestDatabase = $builder.InitialCatalog
        normalDatabase = $normalDatabase
        integratedSecurity = $builder.IntegratedSecurity
        userId = $builder.UserID
        password = $builder.Password
    }
}

function Get-SeedCounts {
    param(
        [string]$SqlServer,
        [string]$DatabaseName,
        [bool]$IntegratedSecurity,
        [string]$UserId,
        [string]$Password
    )

    $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($null -eq $sqlcmd) {
        return [ordered]@{ available = $false; reason = "sqlcmd not found" }
    }

    $query = @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM dbo.Projects WHERE Name = 'IronDev Local Test Project') AS Projects,
  (SELECT COUNT(*) FROM dbo.ProjectDocuments WHERE Title IN ('Alpha UI Manual Test Notes','Code Standards Draft','Test Agent Direction')) AS Documents,
  (SELECT COUNT(*) FROM dbo.ProjectTickets WHERE Title IN ('Add Governed Tool Architecture','Wire Start Disposable Run','Improve Ticket Workspace UI')) AS Tickets,
  (SELECT COUNT(*) FROM dbo.RunEvents WHERE RunId = 'localtest-run-ticket-3002') AS RunEvents;
"@

    try {
        $args = @("-S", $SqlServer, "-d", $DatabaseName, "-h", "-1", "-W", "-s", ",", "-Q", $query)
        if ($IntegratedSecurity) {
            $args += "-E"
        }
        else {
            $args += @("-U", $UserId, "-P", $Password)
        }

        $raw = & $sqlcmd.Source @args 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($raw -join ""))) {
            return [ordered]@{ available = $false; reason = "database unavailable" }
        }

        $line = ($raw | Where-Object { $_ -match "," } | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace($line) -or $line -match "^Msg\s+\d+") {
            return [ordered]@{ available = $false; reason = "seed tables unavailable" }
        }

        $parts = $line -split ","
        if ($parts.Count -lt 4) {
            return [ordered]@{ available = $false; reason = "seed count query returned unexpected output" }
        }

        return [ordered]@{
            available = $true
            projects = [int]$parts[0]
            documents = [int]$parts[1]
            tickets = [int]$parts[2]
            runEvents = [int]$parts[3]
        }
    }
    catch {
        return [ordered]@{ available = $false; reason = $_.Exception.Message }
    }
}

function Write-Reports {
    param(
        [hashtable]$Report
    )

    $Report | ConvertTo-Json -Depth 12 | Set-Content -Path $jsonReportPath -Encoding UTF8

    $status = if ($Report.passed) { "PASS" } else { "FAIL" }
    $lines = @(
        "# LocalTest Alpha Cockpit Report",
        "",
        "- Status: $status",
        "- Generated UTC: $($Report.generatedUtc)",
        "- Branch: $($Report.branch)",
        "- Commit: $($Report.commit)",
        "- API: $($Report.apiBaseUrl)",
        "- UI: $($Report.uiBaseUrl)",
        "- Environment: $($Report.environment.environment)",
        "- Database: $($Report.environment.database)",
        "- Is test environment: $($Report.environment.isTestEnvironment)",
        "- Playwright exit code: $($Report.playwright.exitCode)",
        "",
        "## Checks"
    )

    foreach ($check in $Report.checks) {
        $lines += "- $($check.status): $($check.name) - $($check.detail)"
    }

    $lines += @(
        "",
        "## Seed Counts",
        "",
        '```json',
        ($Report.seedCounts | ConvertTo-Json -Depth 8),
        '```',
        "",
        "## Normal Dev DB Probe",
        "",
        '```json',
        ($Report.normalDevDatabaseProbe | ConvertTo-Json -Depth 8),
        '```',
        "",
        "## Playwright",
        "",
        "- JSON: $($Report.playwright.jsonReport)",
        "- HTML: $($Report.playwright.htmlReport)",
        "- LocalTest report JSON: $jsonReportPath"
    )

    $lines | Set-Content -Path $markdownReportPath -Encoding UTF8
}

$checks = New-Object System.Collections.Generic.List[object]
$passed = $false
$playwrightExitCode = 999

try {
    if ($Reset) {
        & (Join-Path $PSScriptRoot "reset-localtest-data.ps1")
        $checks.Add([ordered]@{ name = "Reset LocalTest data"; status = "PASS"; detail = "reset-localtest-data.ps1 completed" })
    }

    if ($StartServices) {
        Stop-Listener -Port 5000
        Stop-Listener -Port $UiPort

        $apiOut = Join-Path $env:TEMP "irondev-localtest-api.out.log"
        $apiErr = Join-Path $env:TEMP "irondev-localtest-api.err.log"
        Start-Process -FilePath dotnet `
            -ArgumentList @("run", "--launch-profile", "LocalTest", "--project", "IronDev.Api\IronDev.Api.csproj") `
            -WorkingDirectory $repoRoot `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $apiOut `
            -RedirectStandardError $apiErr | Out-Null
        $startedPorts.Add(5000)

        Wait-HttpOk -Uri "$ApiBaseUrl/health"

        $node = Get-NodeCommand
        $uiOut = Join-Path $env:TEMP "irondev-localtest-ui.out.log"
        $uiErr = Join-Path $env:TEMP "irondev-localtest-ui.err.log"
        $env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl
        $env:VITE_IRONDEV_PROJECT_ID = "1"
        $env:IRONDEV_API_PROXY_TARGET = $ApiBaseUrl

        Start-Process -FilePath $node `
            -ArgumentList @("node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", "$UiPort", "--mode", "localtest") `
            -WorkingDirectory $shellRoot `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $uiOut `
            -RedirectStandardError $uiErr | Out-Null
        $startedPorts.Add($UiPort)

        Wait-HttpOk -Uri "$UiBaseUrl/"
        $checks.Add([ordered]@{ name = "Start LocalTest API and UI"; status = "PASS"; detail = "API and browser shell are reachable" })
    }
    else {
        Wait-HttpOk -Uri "$ApiBaseUrl/health"
        Wait-HttpOk -Uri "$UiBaseUrl/"
        $checks.Add([ordered]@{ name = "Use existing LocalTest API and UI"; status = "PASS"; detail = "Both endpoints are reachable" })
    }

    $environment = Invoke-RestMethod -Uri "$ApiBaseUrl/api/environment" -TimeoutSec 5
    if ($environment.environment -ne "LocalTest" -or $environment.database -notmatch "Test" -or -not $environment.isTestEnvironment) {
        throw "Environment guard failed: $($environment.environment) / $($environment.database)."
    }
    $checks.Add([ordered]@{ name = "Environment guard"; status = "PASS"; detail = "$($environment.environment) / $($environment.database)" })

    $connectionInfo = Get-LocalTestConnectionInfo
    if ($environment.database -ne $connectionInfo.localTestDatabase) {
        throw "Environment database '$($environment.database)' does not match LocalTest config '$($connectionInfo.localTestDatabase)'."
    }

    $localTestCounts = Get-SeedCounts `
        -SqlServer $connectionInfo.server `
        -DatabaseName $environment.database `
        -IntegratedSecurity $connectionInfo.integratedSecurity `
        -UserId $connectionInfo.userId `
        -Password $connectionInfo.password

    $devProbe = Get-SeedCounts `
        -SqlServer $connectionInfo.server `
        -DatabaseName $connectionInfo.normalDatabase `
        -IntegratedSecurity $connectionInfo.integratedSecurity `
        -UserId $connectionInfo.userId `
        -Password $connectionInfo.password

    if ($devProbe.available -and (($devProbe.projects + $devProbe.documents + $devProbe.tickets + $devProbe.runEvents) -gt 0)) {
        throw "Normal dev database contains LocalTest seed rows."
    }
    $checks.Add([ordered]@{ name = "Normal dev DB untouched"; status = "PASS"; detail = if ($devProbe.available) { "No LocalTest seed rows found" } else { "Normal DB not required: $($devProbe.reason)" } })

    Push-Location $shellRoot
    try {
        $env:IRONDEV_LOCALTEST_LIVE = "1"
        $env:IRONDEV_TAURI_SHELL_BASE_URL = $UiBaseUrl
        $node = Get-NodeCommand
        & $node "node_modules/@playwright/test/cli.js" test tests/localtest-manual-smoke.spec.ts --reporter=list
        $playwrightExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($playwrightExitCode -ne 0) {
        throw "LocalTest Playwright/manual smoke failed with exit code $playwrightExitCode."
    }

    $checks.Add([ordered]@{ name = "Playwright/manual smoke"; status = "PASS"; detail = "Live LocalTest cockpit flow passed" })
    $passed = $true
}
catch {
    $checks.Add([ordered]@{ name = "Failure"; status = "FAIL"; detail = $_.Exception.Message })
    $passed = $false
}
finally {
    $report = @{
        passed = $passed
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("o")
        branch = (git -C $repoRoot branch --show-current)
        commit = (git -C $repoRoot rev-parse --short HEAD)
        apiBaseUrl = $ApiBaseUrl
        uiBaseUrl = $UiBaseUrl
        environment = if ($environment) { $environment } else { @{} }
        seedCounts = if ($localTestCounts) { $localTestCounts } else { @{} }
        normalDevDatabaseProbe = if ($devProbe) { $devProbe } else { @{} }
        checks = $checks
        playwright = @{
            exitCode = $playwrightExitCode
            jsonReport = $playwrightJsonPath
            htmlReport = Join-Path $shellRoot "reports\html\index.html"
        }
    }

    Write-Reports -Report $report

    if ($StartServices -and -not $KeepServices) {
        foreach ($port in $startedPorts) {
            Stop-Listener -Port $port
        }
    }

    if ($passed) {
        Write-Host ""
        Write-Host "PASS LocalTest Alpha cockpit smoke passed"
        Write-Host "Report: $markdownReportPath"
        exit 0
    }

    Write-Host ""
    Write-Host "FAIL LocalTest Alpha cockpit smoke failed"
    Write-Host "Report: $markdownReportPath"
    exit 1
}

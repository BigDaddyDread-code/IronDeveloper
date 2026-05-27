param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 1,
    [int]$UiPort = 5173,
    [switch]$Reset,
    [switch]$BrowserOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$apiOut = Join-Path $env:TEMP "irondev-localtest-api.out.log"
$apiErr = Join-Path $env:TEMP "irondev-localtest-api.err.log"

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

if ($Reset) {
    & (Join-Path $PSScriptRoot "reset-localtest-data.ps1")
}

Stop-Listener -Port 5000
Start-Process -FilePath dotnet `
    -ArgumentList @("run", "--launch-profile", "LocalTest", "--project", "IronDev.Api\IronDev.Api.csproj") `
    -WorkingDirectory $repoRoot `
    -PassThru `
    -WindowStyle Hidden `
    -RedirectStandardOutput $apiOut `
    -RedirectStandardError $apiErr | Out-Null

Wait-HttpOk -Uri "$ApiBaseUrl/health"
$environment = Invoke-RestMethod -Uri "$ApiBaseUrl/api/environment" -TimeoutSec 5

if ($environment.environment -ne "LocalTest" -or $environment.database -notmatch "Test" -or -not $environment.isTestEnvironment) {
    throw "API environment check failed. Refusing to start UI against $($environment.environment)/$($environment.database)."
}

$env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl
$env:VITE_IRONDEV_PROJECT_ID = "$ProjectId"
$env:IRONDEV_API_PROXY_TARGET = $ApiBaseUrl

Write-Host ""
Write-Host "PASS LocalTest API started"
Write-Host "  API: $ApiBaseUrl"
Write-Host "  Environment: $($environment.environment)"
Write-Host "  Database: $($environment.database)"
Write-Host "  Workspace: $($environment.workspaceRoot)"
Write-Host ""

if ($BrowserOnly) {
    Stop-Listener -Port $UiPort
    $node = "C:\Users\bob\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
    $vite = "node_modules/vite/bin/vite.js"
    $shellRoot = Join-Path $repoRoot "IronDev.TauriShell"
    $uiOut = Join-Path $env:TEMP "irondev-localtest-ui.out.log"
    $uiErr = Join-Path $env:TEMP "irondev-localtest-ui.err.log"

    if (Test-Path $node) {
        Start-Process -FilePath $node `
            -ArgumentList @($vite, "--host", "127.0.0.1", "--port", "$UiPort", "--mode", "localtest") `
            -WorkingDirectory $shellRoot `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $uiOut `
            -RedirectStandardError $uiErr | Out-Null
    }
    else {
        Start-Process -FilePath npm `
            -ArgumentList @("run", "dev:localtest", "--", "--port", "$UiPort") `
            -WorkingDirectory $shellRoot `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $uiOut `
            -RedirectStandardError $uiErr | Out-Null
    }

    Wait-HttpOk -Uri "http://127.0.0.1:$UiPort/"
    Write-Host "PASS LocalTest browser shell started"
    Write-Host "  UI: http://127.0.0.1:$UiPort/"
    Write-Host ""
    Write-Host "Processes are left running. Stop ports 5000/$UiPort when finished."
    exit 0
}

Write-Host "Starting Tauri desktop shell. Close the desktop window or stop this command when finished."
Push-Location (Join-Path $repoRoot "IronDev.TauriShell")
try {
    npm run tauri:dev
}
finally {
    Pop-Location
}

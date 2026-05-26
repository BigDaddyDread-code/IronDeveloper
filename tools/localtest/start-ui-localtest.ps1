param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 1,
    [switch]$BrowserOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$shellRoot = Join-Path $repoRoot "IronDev.TauriShell"

if (-not (Test-Path $shellRoot)) {
    throw "Tauri shell folder was not found at '$shellRoot'."
}

$env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl
$env:VITE_IRONDEV_PROJECT_ID = "$ProjectId"

Write-Host "Starting IronDev Tauri shell against LocalTest API $ApiBaseUrl."
Write-Host "Fallback project id: $ProjectId"

Push-Location $shellRoot
try {
    if ($BrowserOnly) {
        npm run dev:localtest
    }
    else {
        npm run tauri:dev
    }
}
finally {
    Pop-Location
}

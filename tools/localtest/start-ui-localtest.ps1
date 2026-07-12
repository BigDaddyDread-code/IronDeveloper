param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 0,
    [switch]$BrowserOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $PSScriptRoot "localtest-seed-contract.ps1")
$seedContract = Get-LocalTestSeedContract
if ($ProjectId -le 0) {
    $ProjectId = [int](($seedContract.projects | Where-Object key -eq "baseline" | Select-Object -First 1).id)
}
$shellRoot = Join-Path $repoRoot "IronDev.TauriShell"

if (-not (Test-Path $shellRoot)) {
    throw "Tauri shell folder was not found at '$shellRoot'."
}

$env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl
$env:VITE_IRONDEV_PROJECT_ID = "$ProjectId"
$env:IRONDEV_API_PROXY_TARGET = $ApiBaseUrl

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

param(
    [string]$ConfigPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $PSScriptRoot "localtest-seed-contract.ps1")
$seedContract = Get-LocalTestSeedContract
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repoRoot "IronDev.Api\appsettings.LocalTest.json"
}

if (-not (Test-Path $ConfigPath)) {
    throw "LocalTest config was not found at '$ConfigPath'."
}

$config = Get-Content -Path $ConfigPath -Raw | ConvertFrom-Json
$database = ([System.Data.SqlClient.SqlConnectionStringBuilder]::new($config.ConnectionStrings.IronDeveloperDb)).InitialCatalog
Assert-LocalTestSeedTarget `
    -Contract $seedContract `
    -DatabaseName $database `
    -WorkspaceRoot $config.LocalTest.WorkspaceRoot `
    -LogsRoot $config.LocalTest.LogsRoot

New-Item -ItemType Directory -Force -Path $config.LocalTest.WorkspaceRoot, $config.LocalTest.LogsRoot | Out-Null

Write-Host "Starting IronDev.Api in LocalTest mode."
Write-Host "Database: $database"
Write-Host "Workspace root: $($config.LocalTest.WorkspaceRoot)"
Write-Host "Logs root: $($config.LocalTest.LogsRoot)"

Set-Location $repoRoot
dotnet run --launch-profile LocalTest --project IronDev.Api\IronDev.Api.csproj

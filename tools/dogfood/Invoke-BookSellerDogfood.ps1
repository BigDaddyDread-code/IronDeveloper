param(
    [string]$RunId = "",

    [string]$BaselinePath = "C:\repo\BookSeller_Baseline",

    [string]$TargetPath = "C:\repo\BookSeller",

    [string]$DatabaseName = "BookSellerDogfood",

    [string]$SqlServer = ".",

    [int]$Reps = 100,

    [switch]$Reset,

    [switch]$ForceReset,

    [switch]$DryRun,

    [switch]$StopIronDev,

    [switch]$StopOnFailure,

    [int]$Seed = 0,

    [string]$RunnerCommand = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = "BookSellerMvp-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")
}

Write-Host "Starting BookSeller dogfood harness"
Write-Host "RunId: $RunId"
Write-Host "Reps: $Reps"
Write-Host "DryRun: $DryRun"

if ($Reset) {
    $resetArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $PSScriptRoot "Reset-BookSellerDogfood.ps1"),
        "-BaselinePath", $BaselinePath,
        "-TargetPath", $TargetPath,
        "-DatabaseName", $DatabaseName,
        "-RunId", $RunId,
        "-SqlServer", $SqlServer
    )

    if ($DryRun) { $resetArgs += "-DryRun" }
    if ($StopIronDev) { $resetArgs += "-StopIronDev" }
    if ($ForceReset) { $resetArgs += "-Force" }

    & powershell @resetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Reset failed with exit code $LASTEXITCODE"
    }
}

$replayArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "Start-BookSellerReplay.ps1"),
    "-RunId", $RunId,
    "-Reps", $Reps
)

if ($DryRun) { $replayArgs += "-DryRun" }
if ($StopOnFailure) { $replayArgs += "-StopOnFailure" }
if ($Seed -ne 0) { $replayArgs += @("-Seed", $Seed) }
if (-not [string]::IsNullOrWhiteSpace($RunnerCommand)) { $replayArgs += @("-RunnerCommand", $RunnerCommand) }

& powershell @replayArgs
if ($LASTEXITCODE -ne 0) {
    throw "Replay planning failed with exit code $LASTEXITCODE"
}

Write-Host "Dogfood harness complete"
Write-Host "Run folder: $(Join-Path $PSScriptRoot "runs\$RunId")"

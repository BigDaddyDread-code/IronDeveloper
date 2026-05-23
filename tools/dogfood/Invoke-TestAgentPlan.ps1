param(
    [Parameter(Mandatory = $true)]
    [string]$PlanPath,

    [string]$RunId,

    [switch]$Json
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$runnerProject = Join-Path $repoRoot "tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj"
$arguments = @(
    "run",
    "--no-build",
    "--project",
    $runnerProject,
    "--",
    "test",
    "run-plan",
    "--plan",
    $PlanPath
)

if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $arguments += @("--run-id", $RunId)
}

if ($Json) {
    $arguments += "--json"
}

& dotnet @arguments
exit $LASTEXITCODE

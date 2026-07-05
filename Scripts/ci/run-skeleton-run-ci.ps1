param(
    [string]$ProjectPath = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj",
    [string]$ArtifactDirectory = "artifacts/ci/skeleton-run"
)

# D-1.1 — the SkeletonRun selection lane. The SkeletonRun category exercises the
# core P0-P2 governed loop (proposal -> blind tests -> disposable build/test ->
# critic package -> gate -> continuation -> apply). Before this lane it ran only
# on a developer's machine; the core loop is not gated if it only runs locally.
#
# The lane is DB-free by construction: every SkeletonRun test wires its own
# in-memory stores and does not inherit IntegrationTestBase, so no SQL server is
# required. It selects TestCategory=SkeletonRun, FAILS if zero tests are selected
# (selection proof is not execution proof), and reports total/passed/failed.

$ErrorActionPreference = "Stop"

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Name)
    Write-Host ""
    Write-Host "== $Name =="
}

function Format-Duration {
    param([TimeSpan]$Duration)
    return "{0:hh\:mm\:ss\.fff}" -f $Duration
}

function Invoke-TimedDotNetCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Section $Name
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & $Command
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    $script:Durations[$Name] = $stopwatch.Elapsed
    Write-Host "$Name duration: $(Format-Duration $stopwatch.Elapsed)"

    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

function Get-TrxCounters {
    param([Parameter(Mandatory = $true)][string]$TestResultsDirectory)

    $trx = Get-ChildItem -LiteralPath $TestResultsDirectory -Filter "*.trx" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $trx) {
        throw "skeleton-run-ci did not produce a TRX result file."
    }

    [xml]$trxXml = Get-Content -LiteralPath $trx.FullName -Raw
    $counters = $trxXml.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters -or [string]::IsNullOrWhiteSpace($counters.total)) {
        throw "skeleton-run-ci could not read the TRX test count."
    }

    return [pscustomobject]@{
        Total   = [int]$counters.total
        Passed  = if ([string]::IsNullOrWhiteSpace($counters.passed)) { 0 } else { [int]$counters.passed }
        Failed  = if ([string]::IsNullOrWhiteSpace($counters.failed)) { 0 } else { [int]$counters.failed }
        Skipped = if ([string]::IsNullOrWhiteSpace($counters.total) -or [string]::IsNullOrWhiteSpace($counters.executed)) { 0 } else { ([int]$counters.total - [int]$counters.executed) }
    }
}

function Write-SkeletonRunSummary {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [int]$Total,
        [int]$Passed,
        [int]$Failed,
        [Parameter(Mandatory = $true)][TimeSpan]$TotalDuration
    )

    $summaryPath = Join-Path $script:ArtifactRoot "skeleton-run-summary.md"
    $timestamp = [DateTimeOffset]::UtcNow.ToString("O")
    $countValue = if ($Total -gt 0) { $Total.ToString() } else { "not-available" }

    $lines = @(
        "# SkeletonRun CI Summary",
        "",
        "Selection proof is not execution proof. This lane selected and RAN the SkeletonRun category.",
        "",
        "| Field | Value |",
        "| --- | --- |",
        "| Workflow name | skeleton-run-ci |",
        "| Lane name | skeleton-run |",
        "| Project | $script:ProjectPath |",
        "| Filter | TestCategory=SkeletonRun |",
        "| Status | $Status |",
        "| Selected/total tests | $countValue |",
        "| Passed | $Passed |",
        "| Failed | $Failed |",
        "| Total duration | $(Format-Duration $TotalDuration) |",
        "| Run id | $env:GITHUB_RUN_ID |",
        "| Run attempt | $env:GITHUB_RUN_ATTEMPT |",
        "| Commit SHA | $env:GITHUB_SHA |",
        "| UTC timestamp | $timestamp |",
        "",
        "skeleton-run-ci proves the core governed-loop category restored, built, and passed on this head.",
        "",
        "This lane gates the SkeletonRun category in CI. It is execution evidence, not approval, policy satisfaction, merge readiness, release readiness, deployment readiness, or source apply authority."
    )

    Set-Content -LiteralPath $summaryPath -Value $lines -Encoding UTF8
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $script:RepoRoot

$script:ProjectPath = $ProjectPath
$resolvedProject = Resolve-Path -LiteralPath $script:ProjectPath
$script:ArtifactRoot = Join-Path $script:RepoRoot $ArtifactDirectory
$testResultsRoot = Join-Path $script:ArtifactRoot "test-results"

if (-not $resolvedProject.Path.EndsWith("IronDev.IntegrationTests.csproj", [StringComparison]::OrdinalIgnoreCase)) {
    throw "skeleton-run-ci must run IronDev.IntegrationTests/IronDev.IntegrationTests.csproj."
}

if (Test-Path -LiteralPath $script:ArtifactRoot) {
    Remove-Item -LiteralPath $script:ArtifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $testResultsRoot -Force | Out-Null
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "skeleton-run-ci" `
    -LaneName "skeleton-run" `
    -CommandCategory "dotnet restore/build/test --filter TestCategory=SkeletonRun" `
    -ResultStatus "Started"

Write-Section "SkeletonRun CI"
Write-Host "GitHub Actions CI reports evidence only."
Write-Host "skeleton-run-ci is not approval, policy satisfaction, merge readiness, release readiness, deployment readiness, or execution permission."

$script:Durations = [ordered]@{}
$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$status = "Failed"
$counters = $null
$failure = $null

try {
    Invoke-TimedDotNetCommand "Restore IronDev.IntegrationTests" {
        dotnet restore $script:ProjectPath --verbosity minimal
    }

    Invoke-TimedDotNetCommand "Build IronDev.IntegrationTests" {
        dotnet build $script:ProjectPath --no-restore --verbosity minimal
    }

    Invoke-TimedDotNetCommand "Test SkeletonRun" {
        dotnet test $script:ProjectPath `
            --no-build `
            --filter "TestCategory=SkeletonRun" `
            --logger "console;verbosity=minimal" `
            --logger "trx;LogFileName=skeleton-run.trx" `
            --results-directory $testResultsRoot
    }

    $counters = Get-TrxCounters -TestResultsDirectory $testResultsRoot
    if ($counters.Total -le 0) {
        throw "skeleton-run-ci selected zero SkeletonRun tests. The core governed loop must be gated, not silently empty."
    }

    $status = "Passed"
    Write-Host "SkeletonRun selected/total: $($counters.Total), passed: $($counters.Passed), failed: $($counters.Failed)"
}
catch {
    $failure = $_
}
finally {
    $totalStopwatch.Stop()
    Write-SkeletonRunSummary `
        -Status $status `
        -Total $(if ($null -ne $counters) { $counters.Total } else { 0 }) `
        -Passed $(if ($null -ne $counters) { $counters.Passed } else { 0 }) `
        -Failed $(if ($null -ne $counters) { $counters.Failed } else { 0 }) `
        -TotalDuration $totalStopwatch.Elapsed

    & (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
        -ArtifactDirectory $script:ArtifactRoot `
        -WorkflowName "skeleton-run-ci" `
        -LaneName "skeleton-run" `
        -CommandCategory "dotnet restore/build/test --filter TestCategory=SkeletonRun" `
        -ResultStatus $status
}

if ($null -ne $failure) {
    throw $failure
}

Write-Section "SkeletonRun CI complete"
Write-Host "The core governed loop is now gated in CI, not only on a developer's machine."

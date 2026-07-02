param(
    [string]$ProjectPath = "IronDev.UnitTests/IronDev.UnitTests.csproj",
    [string]$ArtifactDirectory = "artifacts/ci/fast-unit"
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

function Format-Duration {
    param(
        [TimeSpan]$Duration
    )

    return "{0:hh\:mm\:ss\.fff}" -f $Duration
}

function Invoke-TimedDotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
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

function Get-TrxTestCount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TestResultsDirectory
    )

    $trx = Get-ChildItem -LiteralPath $TestResultsDirectory -Filter "*.trx" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $trx) {
        throw "fast-unit-ci did not produce a TRX result file."
    }

    [xml]$trxXml = Get-Content -LiteralPath $trx.FullName -Raw
    $counters = $trxXml.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters -or [string]::IsNullOrWhiteSpace($counters.total)) {
        throw "fast-unit-ci could not read the TRX test count."
    }

    return [int]$counters.total
}

function Write-FastUnitSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [int]$TestCount,

        [Parameter(Mandatory = $true)]
        [TimeSpan]$TotalDuration
    )

    $summaryPath = Join-Path $script:ArtifactRoot "fast-unit-summary.md"
    $timestamp = [DateTimeOffset]::UtcNow.ToString("O")
    $restoreDuration = if ($script:Durations.Contains("Restore IronDev.UnitTests")) { Format-Duration $script:Durations["Restore IronDev.UnitTests"] } else { "not-completed" }
    $buildDuration = if ($script:Durations.Contains("Build IronDev.UnitTests")) { Format-Duration $script:Durations["Build IronDev.UnitTests"] } else { "not-completed" }
    $testDuration = if ($script:Durations.Contains("Test IronDev.UnitTests")) { Format-Duration $script:Durations["Test IronDev.UnitTests"] } else { "not-completed" }
    $countValue = if ($TestCount -gt 0) { $TestCount.ToString() } else { "not-available" }

    $lines = @(
        "# Fast Unit CI Summary",
        "",
        "A fast lane is not a complete lane.",
        "",
        "| Field | Value |",
        "| --- | --- |",
        "| Workflow name | fast-unit-ci |",
        "| Lane name | fast-unit |",
        "| Project | $script:ProjectPath |",
        "| Status | $Status |",
        "| Test count | $countValue |",
        "| Restore duration | $restoreDuration |",
        "| Build duration | $buildDuration |",
        "| Test duration | $testDuration |",
        "| Total duration | $(Format-Duration $TotalDuration) |",
        "| Run id | $env:GITHUB_RUN_ID |",
        "| Run attempt | $env:GITHUB_RUN_ATTEMPT |",
        "| Commit SHA | $env:GITHUB_SHA |",
        "| UTC timestamp | $timestamp |",
        "",
        "fast-unit-ci proves only that the fast unit project restored, built, and passed its unit tests on this head.",
        "",
        "fast-unit-ci does not prove integration behavior, SQL behavior, API behavior, CLI behavior, frontend behavior, release readiness, merge readiness, deployment readiness, source apply safety, rollback safety, workflow continuation safety, or memory promotion safety.",
        "",
        "Evidence artifact only. Not approval, readiness, authority, or permission."
    )

    Set-Content -LiteralPath $summaryPath -Value $lines -Encoding UTF8
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $script:RepoRoot

$script:ProjectPath = $ProjectPath
$resolvedProject = Resolve-Path -LiteralPath $script:ProjectPath
$script:ArtifactRoot = Join-Path $script:RepoRoot $ArtifactDirectory
$testResultsRoot = Join-Path $script:ArtifactRoot "test-results"

if (-not $resolvedProject.Path.EndsWith("IronDev.UnitTests.csproj", [StringComparison]::OrdinalIgnoreCase)) {
    throw "fast-unit-ci must run IronDev.UnitTests/IronDev.UnitTests.csproj."
}

if (Test-Path -LiteralPath $script:ArtifactRoot) {
    Remove-Item -LiteralPath $script:ArtifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $testResultsRoot -Force | Out-Null
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "fast-unit-ci" `
    -LaneName "fast-unit" `
    -CommandCategory "dotnet restore/build/test" `
    -ResultStatus "Started"

Write-Section "Fast unit CI"
Write-Host "GitHub Actions CI reports evidence only."
Write-Host "fast-unit-ci is not approval, policy satisfaction, merge readiness, release readiness, deployment readiness, or execution permission."

$script:Durations = [ordered]@{}
$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$status = "Failed"
$testCount = 0
$failure = $null

try {
    Invoke-TimedDotNetCommand "Restore IronDev.UnitTests" {
        dotnet restore $script:ProjectPath --verbosity minimal
    }

    Invoke-TimedDotNetCommand "Build IronDev.UnitTests" {
        dotnet build $script:ProjectPath --no-restore --verbosity minimal
    }

    Invoke-TimedDotNetCommand "Test IronDev.UnitTests" {
        dotnet test $script:ProjectPath `
            --no-build `
            --logger "console;verbosity=minimal" `
            --logger "trx;LogFileName=fast-unit.trx" `
            --results-directory $testResultsRoot
    }

    $testCount = Get-TrxTestCount -TestResultsDirectory $testResultsRoot
    if ($testCount -le 0) {
        throw "fast-unit-ci discovered zero tests."
    }

    $status = "Passed"
    Write-Host "Fast unit test count: $testCount"
}
catch {
    $failure = $_
}
finally {
    $totalStopwatch.Stop()
    Write-FastUnitSummary `
        -Status $status `
        -TestCount $testCount `
        -TotalDuration $totalStopwatch.Elapsed

    & (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
        -ArtifactDirectory $script:ArtifactRoot `
        -WorkflowName "fast-unit-ci" `
        -LaneName "fast-unit" `
        -CommandCategory "dotnet restore/build/test" `
        -ResultStatus $status
}

if ($null -ne $failure) {
    throw $failure
}

Write-Section "Fast unit CI complete"
Write-Host "A fast lane is not a complete lane."

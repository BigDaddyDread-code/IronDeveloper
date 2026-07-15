[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}
else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}

$validator = Join-Path $repositoryRoot "tools\dogfood\Test-DogfoodUxAttempt.ps1"
$fixtureRoot = Join-Path $repositoryRoot "tools\dogfood\dogfood-ux\fixtures"
$completedAttemptPath = Join-Path $fixtureRoot "valid-completed.json"
$completedFindingsPath = Join-Path $fixtureRoot "valid-completed.findings.json"
$p1AttemptPath = Join-Path $fixtureRoot "valid-p1-capped.json"
$p1FindingsPath = Join-Path $fixtureRoot "valid-p1-capped.findings.json"
foreach ($requiredPath in @($validator, $completedAttemptPath, $completedFindingsPath, $p1AttemptPath, $p1FindingsPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "DOGFOOD-UX integrity test input is missing: $requiredPath"
    }
}

$tempBase = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "irondev-dogfood-ux-validator"))
$tempDirectory = [IO.Path]::GetFullPath((Join-Path $tempBase ([Guid]::NewGuid().ToString("N"))))
$tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempDirectory.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "DOGFOOD-UX integrity test temp path escaped its allowed root."
}

$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Write-JsonFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 50), $utf8NoBom)
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedMessageFragment,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $rejection = $null
    try {
        & $Action | Out-Null
    }
    catch {
        $rejection = $_
    }

    if ($null -eq $rejection) {
        throw "$Name was accepted; expected rejection containing '$ExpectedMessageFragment'."
    }
    if ($rejection.Exception.Message -notlike "*$ExpectedMessageFragment*") {
        throw "$Name rejected for the wrong reason: $($rejection.Exception.Message)"
    }

    Write-Host "PASS $Name rejected: $($rejection.Exception.Message)"
}

try {
    [void](New-Item -ItemType Directory -Path $tempDirectory -Force:$false)

    & $validator -Path $completedAttemptPath -FindingsPath $completedFindingsPath | Out-Null
    & $validator -Path $p1AttemptPath -FindingsPath $p1FindingsPath | Out-Null
    Write-Host "PASS valid findings and timing fixtures"

    $suppressedP1Path = Join-Path $tempDirectory "suppressed-p1-attempt.json"
    $suppressedP1 = Get-Content -LiteralPath $completedAttemptPath -Raw | ConvertFrom-Json
    Write-JsonFixture -Path $suppressedP1Path -Value $suppressedP1
    Assert-Rejected -Name "P1 severity suppression" -ExpectedMessageFragment "findingCounts.p1" -Action {
        & $validator -Path $suppressedP1Path -FindingsPath $p1FindingsPath
    }

    $shortenedJourneyPath = Join-Path $tempDirectory "shortened-journey-attempt.json"
    $shortenedJourney = Get-Content -LiteralPath $completedAttemptPath -Raw | ConvertFrom-Json
    $shortenedJourney.timing.wallClockElapsedSeconds = 20
    $shortenedJourney.timing.activeJourneySeconds = 20
    $shortenedJourney.timing.pausedSeconds = 0
    $shortenedJourney.timing.productWorkSeconds = 15
    $shortenedJourney.timing.governanceCeremonySeconds = 5
    Write-JsonFixture -Path $shortenedJourneyPath -Value $shortenedJourney
    Assert-Rejected -Name "Timestamp interval understatement" -ExpectedMessageFragment "timing.wallClockElapsedSeconds" -Action {
        & $validator -Path $shortenedJourneyPath -FindingsPath $completedFindingsPath
    }

    $missingFindingsPath = Join-Path $tempDirectory "attempt-without-findings.json"
    $missingFindings = Get-Content -LiteralPath $completedAttemptPath -Raw | ConvertFrom-Json
    $missingFindings.recordKind = "AttemptEvidence"
    Write-JsonFixture -Path $missingFindingsPath -Value $missingFindings
    Assert-Rejected -Name "Missing final findings evidence" -ExpectedMessageFragment "DOGFOOD-UX findings file not found" -Action {
        & $validator -Path $missingFindingsPath
    }

    $malformedFindingsPath = Join-Path $tempDirectory "malformed-findings.json"
    $parsedMalformedFindings = Get-Content -LiteralPath $p1FindingsPath -Raw | ConvertFrom-Json
    $malformedFindings = @($parsedMalformedFindings)
    $malformedFindings[0].PSObject.Properties.Remove("observedBehavior")
    Write-JsonFixture -Path $malformedFindingsPath -Value $malformedFindings
    Assert-Rejected -Name "Malformed findings structure" -ExpectedMessageFragment "observedBehavior" -Action {
        & $validator -Path $p1AttemptPath -FindingsPath $malformedFindingsPath
    }

    $phantomDeviationPath = Join-Path $tempDirectory "phantom-deviation-attempt.json"
    $phantomDeviation = Get-Content -LiteralPath $completedAttemptPath -Raw | ConvertFrom-Json
    $phantomDeviation.outcome = "CompletedWithWorkaround"
    $phantomDeviation.counts.workarounds = 1
    $phantomDeviation.deviations = @(
        [pscustomobject]@{
            kind = "Other"
            occurrences = 1
            documentedBeforeUse = $true
            reason = "Contract fixture for a phantom finding reference."
            findingId = "DUX-MISSING"
        }
    )
    Write-JsonFixture -Path $phantomDeviationPath -Value $phantomDeviation
    Assert-Rejected -Name "Phantom deviation finding" -ExpectedMessageFragment "does not exist in findings.json" -Action {
        & $validator -Path $phantomDeviationPath -FindingsPath $completedFindingsPath
    }
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        $resolvedTemp = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $tempDirectory).Path)
        if (-not $resolvedTemp.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing DOGFOOD-UX integrity test cleanup outside '$tempBase'."
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

Write-Host "PASS DOGFOOD-UX evidence integrity contract."

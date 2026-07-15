[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CampaignId,

    [Parameter(Mandatory = $true)]
    [string]$AttemptId,

    [Parameter(Mandatory = $true)]
    [string]$Project,

    [Parameter(Mandatory = $true)]
    [string]$IronDevCommit
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-SafePathSegment {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "$Name must be one safe path segment containing only letters, digits, dot, underscore, or hyphen."
    }
}

Assert-SafePathSegment -Value $CampaignId -Name "CampaignId"
Assert-SafePathSegment -Value $AttemptId -Name "AttemptId"
Assert-SafePathSegment -Value $Project -Name "Project"
if ($IronDevCommit -notmatch '^[0-9a-fA-F]{7,40}$') {
    throw "IronDevCommit must be a 7-40 character hexadecimal commit ID."
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\dogfood-ux"))
$attemptDirectory = [IO.Path]::GetFullPath((Join-Path $artifactRoot (Join-Path $CampaignId (Join-Path $Project $AttemptId))))
$artifactPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $attemptDirectory.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Attempt output escaped the repository dogfood artifact root."
}
if (Test-Path -LiteralPath $attemptDirectory) {
    throw "DOGFOOD-UX attempt directory already exists and will not be overwritten: $attemptDirectory"
}

$templateRoot = Join-Path $PSScriptRoot "dogfood-ux"
$attemptTemplate = Join-Path $templateRoot "attempt.template.json"
$operatorLogTemplate = Join-Path $templateRoot "operator-log.template.md"
$validator = Join-Path $PSScriptRoot "Test-DogfoodUxAttempt.ps1"
foreach ($requiredFile in @($attemptTemplate, $operatorLogTemplate, $validator)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required DOGFOOD-UX template/tool is missing: $requiredFile"
    }
}

$startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
$utf8NoBom = New-Object Text.UTF8Encoding($false)

try {
    [void](New-Item -ItemType Directory -Path $attemptDirectory -Force:$false)

    $attempt = Get-Content -LiteralPath $attemptTemplate -Raw | ConvertFrom-Json
    $attempt.campaignId = $CampaignId
    $attempt.attemptId = $AttemptId
    $attempt.project = $Project
    $attempt.ironDevCommit = $IronDevCommit.ToLowerInvariant()
    $attempt.startedAtUtc = $startedAtUtc
    $attemptPath = Join-Path $attemptDirectory "attempt.json"
    [IO.File]::WriteAllText($attemptPath, ($attempt | ConvertTo-Json -Depth 30), $utf8NoBom)

    $operatorLog = Get-Content -LiteralPath $operatorLogTemplate -Raw
    $operatorLog = $operatorLog.Replace("**Campaign ID:**", "**Campaign ID:** $CampaignId")
    $operatorLog = $operatorLog.Replace("**Attempt ID:**", "**Attempt ID:** $AttemptId")
    $operatorLog = $operatorLog.Replace("**Project:**", "**Project:** $Project")
    $operatorLog = $operatorLog.Replace("**IronDev commit:**", "**IronDev commit:** $($IronDevCommit.ToLowerInvariant())")
    $operatorLog = $operatorLog.Replace("**Started UTC:**", "**Started UTC:** $startedAtUtc")
    [IO.File]::WriteAllText((Join-Path $attemptDirectory "operator-log.md"), $operatorLog, $utf8NoBom)

    $manifest = [ordered]@{
        schemaVersion = "1.0"
        campaignId = $CampaignId
        attemptId = $AttemptId
        project = $Project
        ironDevCommit = $IronDevCommit.ToLowerInvariant()
        startedAtUtc = $startedAtUtc
        evidenceStatus = "InProgress"
        flowEaseRecord = "attempt.json"
        operatorLog = "operator-log.md"
    }
    [IO.File]::WriteAllText((Join-Path $attemptDirectory "manifest.json"), ($manifest | ConvertTo-Json -Depth 10), $utf8NoBom)

    & $validator -Path $attemptPath -AllowInProgress
}
catch {
    if (Test-Path -LiteralPath $attemptDirectory) {
        $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $attemptDirectory).Path)
        if (-not $resolved.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing cleanup because failed attempt output is outside the dogfood artifact root: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    throw
}

Write-Host "DOGFOOD-UX attempt initialized: $attemptDirectory"
Write-Output $attemptDirectory

param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$WorkflowName,

    [Parameter(Mandatory = $true)]
    [string]$LaneName,

    [Parameter(Mandatory = $true)]
    [string]$CommandCategory,

    [string]$ResultStatus = "Started"
)

$ErrorActionPreference = "Stop"

function Format-SummaryValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "unknown"
    }

    return $Value.Replace("|", "/").Replace("`r", " ").Replace("`n", " ").Trim()
}

$resolvedArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $resolvedArtifactDirectory -Force | Out-Null

$summaryPath = Join-Path $resolvedArtifactDirectory "evidence-summary.md"
$timestamp = [DateTimeOffset]::UtcNow.ToString("O")

$lines = @(
    "# CI Evidence Summary",
    "",
    "| Field | Value |",
    "| --- | --- |",
    "| Workflow name | $(Format-SummaryValue $WorkflowName) |",
    "| Run id | $(Format-SummaryValue $env:GITHUB_RUN_ID) |",
    "| Run attempt | $(Format-SummaryValue $env:GITHUB_RUN_ATTEMPT) |",
    "| Commit SHA | $(Format-SummaryValue $env:GITHUB_SHA) |",
    "| Branch/ref | $(Format-SummaryValue $env:GITHUB_REF) |",
    "| UTC timestamp | $timestamp |",
    "| Lane name | $(Format-SummaryValue $LaneName) |",
    "| Command category | $(Format-SummaryValue $CommandCategory) |",
    "| Result status | $(Format-SummaryValue $ResultStatus) |",
    "",
    "Evidence artifact only. Not approval, readiness, authority, or permission."
)

Set-Content -LiteralPath $summaryPath -Value $lines -Encoding UTF8

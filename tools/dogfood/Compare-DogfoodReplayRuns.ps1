param(
    [Parameter(Mandatory = $true)]
    [string]$LeftRunId,

    [Parameter(Mandatory = $true)]
    [string]$RightRunId
)

$ErrorActionPreference = "Stop"

function Read-Plan {
    param([string]$RunId)

    $path = Join-Path $PSScriptRoot "runs\$RunId\replay\replay-plan.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Replay plan not found for run '$RunId': $path"
    }

    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$left = Read-Plan $LeftRunId
$right = Read-Plan $RightRunId

$leftPrompts = @($left.cases | ForEach-Object { $_.prompt })
$rightPrompts = @($right.cases | ForEach-Object { $_.prompt })
$overlap = @($leftPrompts | Where-Object { $rightPrompts -contains $_ })

$leftWorkspaces = @($left.cases | Group-Object workspace | ForEach-Object { "$($_.Name):$($_.Count)" })
$rightWorkspaces = @($right.cases | Group-Object workspace | ForEach-Object { "$($_.Name):$($_.Count)" })

$result = [ordered]@{
    leftRunId = $LeftRunId
    rightRunId = $RightRunId
    leftSeed = $left.seed
    rightSeed = $right.seed
    leftCases = @($left.cases).Count
    rightCases = @($right.cases).Count
    identicalSeed = $left.seed -eq $right.seed
    promptOverlapCount = $overlap.Count
    promptOverlapRatio = if ($leftPrompts.Count -eq 0) { 0 } else { [Math]::Round($overlap.Count / $leftPrompts.Count, 3) }
    leftWorkspaceMix = $leftWorkspaces
    rightWorkspaceMix = $rightWorkspaces
    comparedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}

$result | ConvertTo-Json -Depth 10

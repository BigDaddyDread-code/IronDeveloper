param(
    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [string]$Scenario = "",

    [int]$Reps = 25,

    [switch]$DryRun,

    [switch]$StopOnFailure,

    [int]$Seed = 0,

    [string]$RunnerCommand = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Scenario)) {
    $Scenario = Join-Path $PSScriptRoot "dogfood-scenarios\BookSellerMvp.json"
}

if (-not (Test-Path -LiteralPath $Scenario)) {
    throw "Scenario file does not exist: $Scenario"
}

if ($Reps -lt 1) {
    throw "Reps must be at least 1."
}

$scenarioJson = Get-Content -LiteralPath $Scenario -Raw | ConvertFrom-Json
$runRoot = Join-Path $PSScriptRoot "runs\$RunId"
$resultsRoot = Join-Path $runRoot "replay"
$planPath = Join-Path $resultsRoot "replay-plan.json"
$summaryPath = Join-Path $resultsRoot "replay-summary.json"

if ($Seed -eq 0) {
    $buffer = New-Object byte[] 4
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($buffer)
    }
    finally {
        $rng.Dispose()
    }
    $Seed = [System.BitConverter]::ToInt32($buffer, 0) -band 0x7fffffff
    if ($Seed -eq 0) {
        $Seed = 1
    }
}

$random = [Random]::new($Seed)

New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

function Pick-Random {
    param([object[]]$Items)

    if ($Items.Count -eq 0) {
        return $null
    }

    return $Items[$random.Next(0, $Items.Count)]
}

function Pick-WeightedVariant {
    param([object[]]$Variants)

    $total = 0
    foreach ($variant in $Variants) {
        $weight = 1
        if ($null -ne $variant.weight) {
            $weight = [Math]::Max(1, [int]$variant.weight)
        }

        $total += $weight
    }

    $ticket = $random.Next(1, $total + 1)
    $cursor = 0

    foreach ($variant in $Variants) {
        $weight = 1
        if ($null -ne $variant.weight) {
            $weight = [Math]::Max(1, [int]$variant.weight)
        }

        $cursor += $weight
        if ($ticket -le $cursor) {
            return $variant
        }
    }

    return $Variants[-1]
}

function New-CaseId {
    param([int]$Index)

    return "{0:D4}-{1}" -f $Index, ([Guid]::NewGuid().ToString("N").Substring(0, 8))
}

$variants = @($scenarioJson.variants)
if ($variants.Count -eq 0) {
    throw "Scenario has no variants: $Scenario"
}

$workspaceNoise = @("Chat", "Discovery", "Tickets", "Documents")
$promptNoisePrefixes = @(
    "",
    "ok ",
    "right ",
    "can you ",
    "quick one, ",
    "I think ",
    "hmm ",
    "not sure but ",
    "maybe "
)
$promptNoiseSuffixes = @(
    "",
    " please",
    " for this",
    " and keep it safe",
    " but don't change code yet",
    " and show me what happened",
    " if that makes sense",
    " you know what I mean",
    " from what we were doing"
)

function Get-ArrayOrDefault {
    param(
        [object]$Value,
        [object[]]$Default
    )

    if ($null -eq $Value) {
        return $Default
    }

    $items = @($Value)
    if ($items.Count -eq 0) {
        return $Default
    }

    return $items
}

$cases = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Reps; $i++) {
    $variant = Pick-WeightedVariant $variants
    $basePrompts = Get-ArrayOrDefault -Value $variant.promptVariants -Default @([string]$variant.prompt)
    $basePrompt = [string](Pick-Random $basePrompts)
    $prefixes = Get-ArrayOrDefault -Value $variant.promptPrefixes -Default $promptNoisePrefixes
    $suffixes = Get-ArrayOrDefault -Value $variant.promptSuffixes -Default $promptNoiseSuffixes
    $prompt = "$(Pick-Random $prefixes)$basePrompt$(Pick-Random $suffixes)".Trim()

    $workspace = if ($variant.randomizeWorkspace -eq $false) {
        [string]$variant.workspace
    } else {
        $workspaceOptions = Get-ArrayOrDefault -Value $variant.workspaceOptions -Default (@($variant.workspace) + $workspaceNoise)
        $choices = @($workspaceOptions)
        [string](Pick-Random $choices)
    }

    $case = [ordered]@{
        dogfoodRunId = $RunId
        caseId = New-CaseId -Index $i
        caseNumber = $i
        scenarioId = $scenarioJson.scenarioId
        seed = $Seed
        dryRun = [bool]$DryRun
        stopOnFailure = [bool]$StopOnFailure
        step = $variant.step
        name = $variant.name
        workspace = $workspace
        prompt = $prompt
        basePrompt = $basePrompt
        expected = $variant.expected
        tags = @($variant.tags)
        ambiguityLevel = if ($null -eq $variant.ambiguityLevel) { "Normal" } else { [string]$variant.ambiguityLevel }
        allowedOutcomes = @($variant.allowedOutcomes)
        status = "Planned"
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }

    $cases.Add($case)
}

$plan = [ordered]@{
    dogfoodRunId = $RunId
    scenarioId = $scenarioJson.scenarioId
    scenarioPath = (Resolve-Path -LiteralPath $Scenario).Path
    seed = $Seed
    reps = $Reps
    dryRun = [bool]$DryRun
    stopOnFailure = [bool]$StopOnFailure
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runnerCommand = $RunnerCommand
    cases = $cases
}

$plan | ConvertTo-Json -Depth 20 | Set-Content -Path $planPath -Encoding UTF8

$summary = [ordered]@{
    dogfoodRunId = $RunId
    scenarioId = $scenarioJson.scenarioId
    seed = $Seed
    reps = $Reps
    status = if ([string]::IsNullOrWhiteSpace($RunnerCommand)) { "PlanOnly" } else { "RunnerRequested" }
    replayPlanPath = $planPath
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}

$workspaceGroups = $cases | Group-Object { $_['workspace'] } | Sort-Object Name | ForEach-Object {
    [ordered]@{
        workspace = $_.Name
        count = $_.Count
    }
}

$stepGroups = $cases | Group-Object { $_['step'] } | Sort-Object Name | ForEach-Object {
    [ordered]@{
        step = [int]$_.Name
        count = $_.Count
    }
}

$reportPath = Join-Path $resultsRoot "replay-report.md"
$report = New-Object System.Text.StringBuilder
[void]($report.AppendLine("# Dogfood Replay Plan"))
[void]($report.AppendLine())
[void]($report.AppendLine("- RunId: $RunId"))
[void]($report.AppendLine("- Scenario: $($scenarioJson.scenarioId)"))
[void]($report.AppendLine("- Seed: $Seed"))
[void]($report.AppendLine("- Cases: $Reps"))
[void]($report.AppendLine("- Dry run: $([bool]$DryRun)"))
[void]($report.AppendLine())
[void]($report.AppendLine("## Workspace Mix"))
[void]($report.AppendLine())
foreach ($group in $workspaceGroups) {
    [void]($report.AppendLine("- $($group['workspace']): $($group['count'])"))
}
[void]($report.AppendLine())
[void]($report.AppendLine("## Step Mix"))
[void]($report.AppendLine())
foreach ($group in $stepGroups) {
    [void]($report.AppendLine("- Step $($group['step']): $($group['count'])"))
}
[void]($report.AppendLine())
[void]($report.AppendLine("## Cases"))
[void]($report.AppendLine())
foreach ($case in $cases) {
    [void]($report.AppendLine("### $($case.caseNumber). $($case.name)"))
    [void]($report.AppendLine())
    [void]($report.AppendLine("- CaseId: $($case.caseId)"))
    [void]($report.AppendLine("- Workspace: $($case.workspace)"))
    [void]($report.AppendLine("- Expected intent: $($case.expected.intent)"))
    [void]($report.AppendLine())
    [void]($report.AppendLine('```text'))
    [void]($report.AppendLine($case.prompt))
    [void]($report.AppendLine('```'))
    [void]($report.AppendLine())
}

$report.ToString() | Set-Content -Path $reportPath -Encoding UTF8
$summary.replayReportPath = $reportPath
$summary.workspaceMix = $workspaceGroups
$summary.stepMix = $stepGroups

if ([string]::IsNullOrWhiteSpace($RunnerCommand)) {
    $summary.message = "Replay plan generated. Wire RunnerCommand to execute cases through IronDev internals."
} else {
    Write-Host "Runner command requested:"
    Write-Host $RunnerCommand
    Write-Host "Passing replay plan path as final argument."

    & powershell -NoProfile -ExecutionPolicy Bypass -Command "$RunnerCommand `"$planPath`""
    $summary.runnerExitCode = $LASTEXITCODE
    $summary.status = if ($LASTEXITCODE -eq 0) { "Completed" } else { "Failed" }
}

$summary | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "Dogfood replay plan ready"
Write-Host "RunId: $RunId"
Write-Host "Seed: $Seed"
Write-Host "Cases: $Reps"
Write-Host "Plan: $planPath"
Write-Host "Summary: $summaryPath"

if ($summary.status -eq "Failed") {
    exit 1
}

param(
    [string]$LoopId = "",

    [int]$Iterations = 1000,

    [string]$Scenario = "",

    [switch]$DryRun,

    [switch]$StopOnFailure,

    [string]$RunnerCommand = "",

    [int]$StartAt = 1,

    [int]$DelayMilliseconds = 0
)

$ErrorActionPreference = "Stop"

if ($Iterations -lt 1) {
    throw "Iterations must be at least 1."
}

if ($StartAt -lt 1) {
    throw "StartAt must be at least 1."
}

if ([string]::IsNullOrWhiteSpace($LoopId)) {
    $LoopId = "BookSellerLoop-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss")
}

if ([string]::IsNullOrWhiteSpace($Scenario)) {
    $Scenario = Join-Path $PSScriptRoot "dogfood-scenarios\BookSellerMvp.json"
}

$loopRoot = Join-Path $PSScriptRoot "runs\$LoopId"
$loopLogPath = Join-Path $loopRoot "iteration-loop.jsonl"
$loopSummaryPath = Join-Path $loopRoot "iteration-loop-summary.json"

New-Item -ItemType Directory -Force -Path $loopRoot | Out-Null

Write-Host "Starting dogfood iteration loop"
Write-Host "LoopId: $LoopId"
Write-Host "Iterations: $Iterations"
Write-Host "DryRun: $DryRun"
Write-Host "Runner: $(if ([string]::IsNullOrWhiteSpace($RunnerCommand)) { 'PlanOnly' } else { $RunnerCommand })"

$completed = 0
$failed = 0
$startedAtUtc = [DateTimeOffset]::UtcNow

for ($i = $StartAt; $i -lt ($StartAt + $Iterations); $i++) {
    $iterationRunId = "{0}-iter-{1:D4}" -f $LoopId, $i
    $iterationStartedUtc = [DateTimeOffset]::UtcNow
    $status = "Completed"
    $errorMessage = ""

    Write-Host ""
    Write-Host "Iteration $i -> $iterationRunId"

    try {
        $args = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $PSScriptRoot "Start-BookSellerReplay.ps1"),
            "-RunId", $iterationRunId,
            "-Scenario", $Scenario,
            "-Reps", "1"
        )

        if ($DryRun) { $args += "-DryRun" }
        if ($StopOnFailure) { $args += "-StopOnFailure" }
        if (-not [string]::IsNullOrWhiteSpace($RunnerCommand)) {
            $args += @("-RunnerCommand", $RunnerCommand)
        }

        & powershell @args
        if ($LASTEXITCODE -ne 0) {
            throw "Iteration runner exited with code $LASTEXITCODE"
        }

        $completed++
    }
    catch {
        $status = "Failed"
        $errorMessage = $_.Exception.Message
        $failed++

        Write-Host "Iteration failed: $errorMessage"

        if ($StopOnFailure) {
            Write-Host "Stopping loop because -StopOnFailure is set."
        }
    }

    $planPath = Join-Path $PSScriptRoot "runs\$iterationRunId\replay\replay-plan.json"
    $summaryPath = Join-Path $PSScriptRoot "runs\$iterationRunId\replay\replay-summary.json"

    $record = [ordered]@{
        loopId = $LoopId
        iteration = $i
        runId = $iterationRunId
        status = $status
        errorMessage = $errorMessage
        dryRun = [bool]$DryRun
        planPath = $planPath
        summaryPath = $summaryPath
        startedAtUtc = $iterationStartedUtc.ToString("o")
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }

    $record | ConvertTo-Json -Compress -Depth 10 | Add-Content -Path $loopLogPath -Encoding UTF8

    if ($status -eq "Failed" -and $StopOnFailure) {
        break
    }

    if ($DelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

$summary = [ordered]@{
    loopId = $LoopId
    requestedIterations = $Iterations
    startAt = $StartAt
    completed = $completed
    failed = $failed
    dryRun = [bool]$DryRun
    runnerMode = if ([string]::IsNullOrWhiteSpace($RunnerCommand)) { "PlanOnly" } else { "RunnerCommand" }
    startedAtUtc = $startedAtUtc.ToString("o")
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    logPath = $loopLogPath
}

$summary | ConvertTo-Json -Depth 10 | Set-Content -Path $loopSummaryPath -Encoding UTF8

Write-Host ""
Write-Host "Dogfood iteration loop complete"
Write-Host "Completed: $completed"
Write-Host "Failed: $failed"
Write-Host "Log: $loopLogPath"
Write-Host "Summary: $loopSummaryPath"

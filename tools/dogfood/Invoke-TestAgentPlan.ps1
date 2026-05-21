param(
    [Parameter(Mandatory = $true)]
    [string]$PlanPath,

    [string]$RunId,

    [switch]$Json
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$runnerProject = Join-Path $repoRoot "tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj"
$scenarioRoot = Join-Path $repoRoot "tools\dogfood\dogfood-scenarios"
$runsRoot = Join-Path $repoRoot "tools\dogfood\runs"
$planFullPath = [System.IO.Path]::GetFullPath($PlanPath)

if (-not (Test-Path -LiteralPath $planFullPath)) {
    throw "Test plan not found: $planFullPath"
}

$plan = Get-Content -LiteralPath $planFullPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = if ([string]::IsNullOrWhiteSpace($plan.test_run_id)) {
        "test-agent-$(Get-Date -Format yyyyMMdd-HHmmss)"
    } else {
        [string]$plan.test_run_id
    }
}

$runRoot = Join-Path $runsRoot $RunId
$logRoot = Join-Path $runRoot "logs"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$buildLogPath = Join-Path $logRoot "runner-build.log"
$buildOutput = & dotnet build $runnerProject -p:UseSharedCompilation=false -nr:false 2>&1
$buildExitCode = $LASTEXITCODE
Set-Content -LiteralPath $buildLogPath -Value ($buildOutput | Out-String) -Encoding UTF8
if ($buildExitCode -ne 0) {
    throw "Runner build failed. See $buildLogPath"
}

$started = Get-Date
$stepResults = New-Object System.Collections.Generic.List[object]
$previousResponses = @{}

function Invoke-CommandCapture {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$StepLogPath
    )

    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String)
    Set-Content -LiteralPath $StepLogPath -Value $text -Encoding UTF8

    return [ordered]@{
        exit_code = $exitCode
        output = $text
    }
}

function Convert-ToBool {
    param($Value, [bool]$Default)
    if ($null -eq $Value) { return $Default }
    return [System.Convert]::ToBoolean($Value)
}

$earlyStop = Convert-ToBool $plan.early_stop_on_failure $true

foreach ($step in $plan.steps) {
    $stepStarted = Get-Date
    $stepNumber = [int]$step.step
    $action = [string]$step.action
    $params = $step.params
    $stepLogPath = Join-Path $logRoot ("step-{0:000}-{1}.log" -f $stepNumber, $action)

    $status = "SUCCESS"
    $summary = ""
    $commandText = ""
    $exitCode = 0
    $parsed = $null

    try {
        switch ($action) {
            "chat_send" {
                $message = [string]$params.message
                $workspace = if ([string]::IsNullOrWhiteSpace($params.workspace)) { "Chat" } else { [string]$params.workspace }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "chat", "send", $message,
                    "--workspace", $workspace,
                    "--dogfood-run-id", $RunId,
                    "--project-id", ([string]$plan.project_id)
                )

                if ($params.previous_from_step) {
                    $previousStep = [int]$params.previous_from_step
                    $previous = $previousResponses[$previousStep]
                    if ($previous) {
                        $arguments += @("--previous-assistant", [string]$previous.assistantResponse)
                        $arguments += @("--previous-user", [string]$previous.userMessage)
                    }
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "chat_send exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $previousResponses[$stepNumber] = $parsed
                $summary = "Intent=$($parsed.intent); Response=$($parsed.assistantResponse)"

                if ($params.expect_intent -and $parsed.intent -ne [string]$params.expect_intent) {
                    $status = "FAILED"
                    $summary = "Expected intent $($params.expect_intent), actual $($parsed.intent)"
                }
            }

            "replay_run" {
                $scenarioName = [string]$params.scenario
                $scenarioPath = Join-Path $scenarioRoot "$scenarioName.json"
                $reps = if ($params.reps) { [int]$params.reps } else { 1 }
                $stopOnFailure = Convert-ToBool $params.stop_on_failure $true
                $dryRun = Convert-ToBool $params.dry_run $true
                $replayRunId = "$RunId-replay-step-$stepNumber"
                $arguments = @(
                    "-NoProfile", "-ExecutionPolicy", "Bypass",
                    "-File", (Join-Path $PSScriptRoot "Start-BookSellerReplay.ps1"),
                    "-RunId", $replayRunId,
                    "-Scenario", $scenarioPath,
                    "-Reps", ([string]$reps),
                    "-RunnerCommand", "dotnet run --no-build --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj --"
                )
                if ($dryRun) { $arguments += "-DryRun" }
                if ($stopOnFailure) { $arguments += "-StopOnFailure" }

                $commandText = "powershell " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "powershell" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                $summaryPath = Join-Path $runsRoot "$replayRunId\replay\runner-summary.json"
                if (Test-Path -LiteralPath $summaryPath) {
                    $parsed = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "replay_run exited with code $exitCode"
                } elseif ($parsed) {
                    $summary = "Replay passed $($parsed.passed)/$($parsed.totalCases)"
                } else {
                    $summary = "Replay completed; runner summary was not found."
                }
            }

            "failure_package" {
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "failure", "latest",
                    "--for-codex",
                    "--runs-root", $runsRoot
                )
                if ($params.run_id) {
                    $arguments += @("--run-id", [string]$params.run_id)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "failure_package exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $summary = "Failure package: $($parsed.markdownPath)"
            }

            default {
                $status = "SKIPPED_UNSUPPORTED"
                $summary = "Unsupported Test Agent action: $action"
                Set-Content -LiteralPath $stepLogPath -Value $summary -Encoding UTF8
            }
        }
    } catch {
        $status = "FAILED"
        $summary = $_.Exception.Message
        Set-Content -LiteralPath $stepLogPath -Value $summary -Encoding UTF8
    }

    $duration = [int]((Get-Date) - $stepStarted).TotalSeconds
    $stepResults.Add([ordered]@{
        step = $stepNumber
        action = $action
        status = $status
        summary = $summary
        command = $commandText
        exit_code = $exitCode
        log_path = $stepLogPath
        duration_seconds = $duration
    }) | Out-Null

    if ($earlyStop -and $status -in @("FAILED", "BLOCKED")) {
        break
    }
}

$passed = @($stepResults | Where-Object { $_.status -eq "SUCCESS" }).Count
$failed = @($stepResults | Where-Object { $_.status -eq "FAILED" }).Count
$skipped = @($stepResults | Where-Object { $_.status -like "SKIPPED*" }).Count
$overall = if ($failed -gt 0) {
    if ($passed -gt 0) { "PARTIAL_SUCCESS" } else { "FAILED" }
} elseif ($skipped -gt 0) {
    "PARTIAL_SUCCESS"
} else {
    "SUCCESS"
}

$report = [ordered]@{
    test_run_id = $RunId
    overall_result = $overall
    summary = "Steps passed: $passed; failed: $failed; skipped: $skipped."
    key_metrics = [ordered]@{
        build_success = $null
        unit_test_pass_rate = $null
        coverage_percent = $null
        api_drive_success_rate = $null
        steps_passed = $passed
        steps_failed = $failed
        steps_skipped = $skipped
    }
    critical_issues = @($stepResults | Where-Object { $_.status -eq "FAILED" } | ForEach-Object { $_.summary })
    full_log_location = $logRoot
    time_taken_seconds = [int]((Get-Date) - $started).TotalSeconds
    next_suggestions = @(
        if ($failed -gt 0) { "Generate a Codex failure package and patch the failing route or command." }
        else { "Promote this test plan into the replay regression pack." }
    )
    steps = $stepResults
}

$reportPath = Join-Path $runRoot "test-agent-report.json"
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding UTF8

if ($Json) {
    $report | ConvertTo-Json -Depth 20
} else {
    Write-Host "Test Agent run complete: $overall"
    Write-Host "Report: $reportPath"
    Write-Host "Logs: $logRoot"
}

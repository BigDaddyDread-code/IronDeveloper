param(
    [Parameter(Mandatory = $true)]
    [string]$PlanPath,

    [string]$RunId,

    [switch]$Json
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$runnerProject = Join-Path $repoRoot "tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj"
$defaultSolution = Join-Path $repoRoot "IronDev.slnx"
$scenarioRoot = Join-Path $repoRoot "tools\dogfood\dogfood-scenarios"
$runsRoot = Join-Path $repoRoot "tools\dogfood\runs"
$schemaPath = Join-Path $repoRoot "tools\dogfood\TestAgentReport.schema.json"
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
$artifactsRoot = Join-Path $runRoot "artifacts"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null

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
$traceGroupId = [Guid]::NewGuid().ToString("N")

function Resolve-TargetPath {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $defaultSolution
    }

    $text = [string]$Value
    if ([System.IO.Path]::IsPathRooted($text)) {
        return $text
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $text))
}

function Invoke-CommandCapture {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$StepLogPath
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

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

function Get-OutputLineValue {
    param(
        [string]$Output,
        [string]$Pattern
    )

    $match = [regex]::Match($Output, $Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success -and $match.Groups.Count -gt 1) {
        return $match.Groups[1].Value
    }

    return $null
}

function Test-ReportShape {
    param($Report)

    $required = @(
        "test_run_id",
        "goal_id",
        "status",
        "summary",
        "commands_run",
        "expected",
        "actual",
        "evidence",
        "time_taken_seconds"
    )

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($name in $required) {
        if ($null -eq $Report[$name]) {
            $missing.Add($name) | Out-Null
        }
    }

    return [ordered]@{
        valid = $missing.Count -eq 0
        missing = $missing
        schema_path = $schemaPath
    }
}

function Test-StringContains {
    param(
        [string]$Value,
        [string]$Expected
    )

    return -not [string]::IsNullOrWhiteSpace($Expected) -and
        $Value.IndexOf($Expected, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Convert-ToRepoRelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    if ($fullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($root.Length).Replace('\', '/')
    }

    return $fullPath.Replace('\', '/')
}

function Get-CodeStandardsAllowlist {
    param($Params)

    $allowlistPath = if ($Params.allowlist) {
        Resolve-TargetPath $Params.allowlist
    } else {
        Join-Path $repoRoot "tools\dogfood\code-standards-allowlist.json"
    }

    if (-not (Test-Path -LiteralPath $allowlistPath)) {
        return [ordered]@{
            path = $allowlistPath
            entries = @()
        }
    }

    $json = Get-Content -LiteralPath $allowlistPath -Raw | ConvertFrom-Json
    return [ordered]@{
        path = $allowlistPath
        entries = @($json.temporaryWarnings)
    }
}

function Find-CodeStandardsAllowlistEntry {
    param(
        [object[]]$Entries,
        [string]$RelativePath,
        [string]$Rule,
        [string]$Method
    )

    foreach ($entry in $Entries) {
        if ([string]$entry.path -ne $RelativePath) { continue }
        if ([string]$entry.rule -ne $Rule) { continue }
        if (-not [string]::IsNullOrWhiteSpace($Method) -and
            -not [string]::IsNullOrWhiteSpace([string]$entry.method) -and
            [string]$entry.method -ne $Method) {
            continue
        }
        return $entry
    }

    return $null
}

function New-CodeStandardsFinding {
    param(
        [string]$Severity,
        [string]$Rule,
        [string]$Path,
        [string]$Message,
        [string]$Recommendation,
        [bool]$Blocking,
        [object]$AllowlistEntry = $null,
        [string]$Method = ""
    )

    $finding = [ordered]@{
        severity = $Severity
        rule = $Rule
        rule_id = $Rule
        file = $Path
        area = $Path
        method = $Method
        message = $Message
        recommendation = $Recommendation
        blocking = $Blocking
        allowlisted = $null -ne $AllowlistEntry
        allowlist_reason = if ($AllowlistEntry) { [string]$AllowlistEntry.reason } else { $null }
        expires_after = if ($AllowlistEntry) { [string]$AllowlistEntry.expiresAfter } else { $null }
    }

    return $finding
}

function Get-CSharpMethodMeasurements {
    param(
        [string]$Path
    )

    $lines = Get-Content -LiteralPath $Path
    $measurements = New-Object System.Collections.Generic.List[object]
    $insideMethod = $false
    $methodName = ""
    $startLine = 0
    $braceDepth = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = [string]$lines[$i]
        if (-not $insideMethod) {
            $match = [regex]::Match(
                $line,
                '^\s*(?:public|private|internal|protected|static|async|\s)+\s*(?:Task(?:<[^>]+>)?|void|int|string|bool|[A-Za-z0-9_<>?]+)\s+([A-Za-z0-9_]+)\s*\(')
            if (-not $match.Success) {
                continue
            }

            $insideMethod = $true
            $methodName = $match.Groups[1].Value
            $startLine = $i + 1
            $braceDepth = 0
        }

        $opens = ([regex]::Matches($line, '\{')).Count
        $closes = ([regex]::Matches($line, '\}')).Count
        $braceDepth += $opens - $closes

        if ($insideMethod -and $braceDepth -le 0 -and $i + 1 -gt $startLine) {
            $measurements.Add([ordered]@{
                method = $methodName
                start_line = $startLine
                end_line = $i + 1
                line_count = ($i + 1) - $startLine + 1
            }) | Out-Null
            $insideMethod = $false
            $methodName = ""
            $startLine = 0
            $braceDepth = 0
        }
    }

    return $measurements.ToArray()
}

function Invoke-CodeStandardsCheck {
    param(
        $Params,
        [string]$StepLogPath
    )

    $targetValues = @()
    if ($Params.targets) {
        foreach ($target in $Params.targets) {
            $targetValues += [string]$target
        }
    } elseif ($Params.target) {
        $targetValues += [string]$Params.target
    } else {
        $targetValues += "tools/IronDev.ReplayRunner/Program.cs"
    }

    $maxFileLines = if ($Params.max_file_lines) { [int]$Params.max_file_lines } else { 700 }
    $maxMethodLines = if ($Params.max_method_lines) { [int]$Params.max_method_lines } else { 120 }
    $failMethodLines = if ($Params.fail_method_lines) { [int]$Params.fail_method_lines } else { 250 }
    $requireProofBoundaryDocs = Convert-ToBool $Params.require_proof_boundary_docs $true
    $requirePlanFiles = Convert-ToBool $Params.require_plan_files $true
    $failOnWarnings = Convert-ToBool $Params.fail_on_warnings $false
    $allowlist = Get-CodeStandardsAllowlist -Params $Params
    $allowlistEntries = @($allowlist.entries)

    $findings = New-Object System.Collections.Generic.List[object]
    $metrics = New-Object System.Collections.Generic.List[object]

    foreach ($target in $targetValues) {
        $path = Resolve-TargetPath $target
        $relativePath = Convert-ToRepoRelativePath $path
        if (-not (Test-Path -LiteralPath $path)) {
            $findings.Add((New-CodeStandardsFinding `
                -Severity "error" `
                -Rule "TargetExists" `
                -Path $target `
                -Message "Code standards target does not exist." `
                -Recommendation "Fix the test plan target path or add the expected file." `
                -Blocking $true)) | Out-Null
            continue
        }

        $lineCount = @(Get-Content -LiteralPath $path).Count
        $metrics.Add([ordered]@{
            path = $path
            relative_path = $relativePath
            line_count = $lineCount
        }) | Out-Null

        if ($lineCount -gt $maxFileLines) {
            $entry = Find-CodeStandardsAllowlistEntry -Entries $allowlistEntries -RelativePath $relativePath -Rule "LargeFile"
            $findings.Add((New-CodeStandardsFinding `
                -Severity "warning" `
                -Rule "LargeFile" `
                -Path $relativePath `
                -Message "File has $lineCount lines; warning threshold is $maxFileLines." `
                -Recommendation "Extract stable dogfood helpers after proof slices stabilise." `
                -Blocking $false `
                -AllowlistEntry $entry)) | Out-Null
        }

        if ([System.IO.Path]::GetExtension($path).Equals(".cs", [System.StringComparison]::OrdinalIgnoreCase)) {
            $methods = Get-CSharpMethodMeasurements -Path $path
            foreach ($method in $methods) {
                if ([int]$method.line_count -gt $maxMethodLines) {
                    $entry = Find-CodeStandardsAllowlistEntry -Entries $allowlistEntries -RelativePath $relativePath -Rule "LargeMethod" -Method ([string]$method.method)
                    $isFailure = [int]$method.line_count -gt $failMethodLines -and $null -eq $entry
                    $findings.Add((New-CodeStandardsFinding `
                        -Severity $(if ($isFailure) { "error" } else { "warning" }) `
                        -Rule "LargeMethod" `
                        -Path $relativePath `
                        -Method ([string]$method.method) `
                        -Message "Method $($method.method) has $($method.line_count) lines; warning threshold is $maxMethodLines and failure threshold is $failMethodLines." `
                        -Recommendation "Extract focused services or command handlers once this smoke path is stable." `
                        -Blocking $isFailure `
                        -AllowlistEntry $entry)) | Out-Null
                }
            }
        }
    }

    if ($requireProofBoundaryDocs) {
        $docsToCheck = @(
            (Join-Path $repoRoot "Docs\CODE_STANDARDS.md"),
            (Join-Path $repoRoot "Docs\CODEX_GOALS.md"),
            (Join-Path $repoRoot "Docs\TEST_AGENT_SPEC.md"),
            (Join-Path $repoRoot "tools\dogfood\README.md")
        )

        foreach ($docPath in $docsToCheck) {
            $text = if (Test-Path -LiteralPath $docPath) { Get-Content -LiteralPath $docPath -Raw } else { "" }
            $hasProofLanguage = $text -match '(?i)proves?|not yet|does not yet|still|boundary|evidence'
            if (-not $hasProofLanguage) {
                $findings.Add((New-CodeStandardsFinding `
                    -Severity "warning" `
                    -Rule "ProofBoundaryDocumentation" `
                    -Path (Convert-ToRepoRelativePath $docPath) `
                    -Message "Document does not clearly state proof boundaries or evidence language." `
                    -Recommendation "Add what this proves and what it does not prove before broadening the memory spine." `
                    -Blocking $false)) | Out-Null
            }
        }
    }

    if ($requirePlanFiles) {
        $planDir = Join-Path $repoRoot "tools\dogfood\test-agent-plans"
        $requiredPlans = @(
            "irondev-code-standards-alpha.json",
            "irondev-memory-spine-smoke.json",
            "irondev-memory-spine-sql-version-smoke.json",
            "irondev-memory-spine-weaviate-sql-version-smoke.json",
            "irondev-memory-spine-cross-project-smoke.json",
            "irondev-memory-spine-ticket-source-link-smoke.json",
            "irondev-memory-spine-builder-context-source-smoke.json",
            "irondev-toolchain-smoke.json"
        )

        foreach ($requiredPlan in $requiredPlans) {
            $requiredPlanPath = Join-Path $planDir $requiredPlan
            if (-not (Test-Path -LiteralPath $requiredPlanPath)) {
                $findings.Add((New-CodeStandardsFinding `
                    -Severity "error" `
                    -Rule "ProofPlanExists" `
                    -Path (Convert-ToRepoRelativePath $requiredPlanPath) `
                    -Message "Required proof plan is missing." `
                    -Recommendation "Add the missing Test Agent plan or remove it from the required proof chain intentionally." `
                    -Blocking $true)) | Out-Null
            }
        }
    }

    $errors = @($findings | Where-Object { $_.severity -eq "error" })
    $warnings = @($findings | Where-Object { $_.severity -eq "warning" })
    $qualityStatus = "passed"
    if ($errors.Count -gt 0) {
        $qualityStatus = "failed"
    } elseif ($warnings.Count -gt 0) {
        $qualityStatus = "warning"
    }

    $result = [ordered]@{
        goal = "code-standards-alpha"
        status = $qualityStatus
        build = "not_run_by_this_step"
        tests = "not_run_by_this_step"
        format = "not_run_by_this_step"
        package_audit = "not_run_by_this_step"
        thresholds = [ordered]@{
            warning_file_lines = $maxFileLines
            warning_method_lines = $maxMethodLines
            failure_method_lines = $failMethodLines
        }
        allowlist_path = $allowlist.path
        metrics = @($metrics.ToArray())
        findings = @($findings.ToArray())
        warning_count = $warnings.Count
        error_count = $errors.Count
    }

    $json = $result | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $StepLogPath -Value $json -Encoding UTF8

    return [ordered]@{
        result = $result
        exit_code = if ($errors.Count -gt 0 -or ($failOnWarnings -and $warnings.Count -gt 0)) { 1 } else { 0 }
    }
}

function Invoke-ChatSendStep {
    param(
        [string]$Message,
        [string]$Workspace,
        [string]$StepLogPath,
        [string]$PreviousAssistant,
        [string]$PreviousUser
    )

    $arguments = @(
        "run", "--no-build", "--project", $runnerProject, "--",
        "chat", "send", $Message,
        "--workspace", $Workspace,
        "--dogfood-run-id", $RunId,
        "--project-id", ([string]$plan.project_id)
    )

    if (-not [string]::IsNullOrWhiteSpace($PreviousAssistant)) {
        $arguments += @("--previous-assistant", $PreviousAssistant)
    }

    if (-not [string]::IsNullOrWhiteSpace($PreviousUser)) {
        $arguments += @("--previous-user", $PreviousUser)
    }

    $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $StepLogPath
    $parsed = $null
    if ($capture.exit_code -eq 0) {
        $parsed = $capture.output | ConvertFrom-Json
    }

    return [ordered]@{
        arguments = $arguments
        command_text = "dotnet " + ($arguments -join " ")
        exit_code = $capture.exit_code
        parsed = $parsed
        output = $capture.output
    }
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
                $previousAssistant = ""
                $previousUser = ""
                if ($params.previous_from_step) {
                    $previousStep = [int]$params.previous_from_step
                    $previous = $previousResponses[$previousStep]
                    if ($previous) {
                        $previousAssistant = [string]$previous.assistantResponse
                        $previousUser = [string]$previous.userMessage
                    }
                }

                $chat = Invoke-ChatSendStep -Message $message -Workspace $workspace -StepLogPath $stepLogPath -PreviousAssistant $previousAssistant -PreviousUser $previousUser
                $commandText = $chat.command_text
                $exitCode = $chat.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "chat_send exited with code $exitCode"
                    break
                }

                $parsed = $chat.parsed
                $previousResponses[$stepNumber] = $parsed
                $summary = "Intent=$($parsed.intent); Response=$($parsed.assistantResponse)"

                if ($params.expect_intent -and $parsed.intent -ne [string]$params.expect_intent) {
                    $status = "FAILED"
                    $summary = "Expected intent $($params.expect_intent), actual $($parsed.intent)"
                }
            }

            "chat_conversation" {
                $workspace = if ([string]::IsNullOrWhiteSpace($params.workspace)) { "Chat" } else { [string]$params.workspace }
                $maxTurns = if ($params.max_turns) { [int]$params.max_turns } elseif ($plan.max_turns) { [int]$plan.max_turns } else { 6 }
                $expectedIntent = [string]$params.expected_outcome.intent
                $facts = @($params.facts_to_reveal)
                $messages = New-Object System.Collections.Generic.List[string]
                if ($params.messages) {
                    foreach ($message in @($params.messages)) {
                        if (-not [string]::IsNullOrWhiteSpace([string]$message)) {
                            $messages.Add([string]$message) | Out-Null
                        }
                    }
                } elseif (-not [string]::IsNullOrWhiteSpace([string]$params.initial_message)) {
                    $messages.Add([string]$params.initial_message) | Out-Null
                }

                if ($messages.Count -eq 0) {
                    $status = "FAILED"
                    $summary = "chat_conversation requires messages or initial_message."
                    break
                }

                $conversationLog = New-Object System.Collections.Generic.List[object]
                $previousAssistant = ""
                $previousUser = ""
                $factIndex = 0
                $lastParsed = $null
                $commandText = "chat_conversation via dotnet run --no-build --project $runnerProject"

                for ($turn = 1; $turn -le $maxTurns; $turn++) {
                    if ($turn -le $messages.Count) {
                        $message = $messages[$turn - 1]
                    } elseif ($factIndex -lt $facts.Count) {
                        $message = [string]$facts[$factIndex]
                        $factIndex++
                    } else {
                        break
                    }

                    $turnLogPath = Join-Path $logRoot ("step-{0:000}-{1}-turn-{2:00}.log" -f $stepNumber, $action, $turn)
                    $chat = Invoke-ChatSendStep -Message $message -Workspace $workspace -StepLogPath $turnLogPath -PreviousAssistant $previousAssistant -PreviousUser $previousUser
                    if ($chat.exit_code -ne 0) {
                        $status = "FAILED"
                        $summary = "chat_conversation turn $turn exited with code $($chat.exit_code)"
                        $exitCode = $chat.exit_code
                        break
                    }

                    $lastParsed = $chat.parsed
                    $conversationLog.Add([ordered]@{
                        turn = $turn
                        user_message = $message
                        intent = $lastParsed.intent
                        assistant_response = $lastParsed.assistantResponse
                        log_path = $turnLogPath
                    }) | Out-Null

                    $previousUser = $message
                    $previousAssistant = [string]$lastParsed.assistantResponse

                    if (-not [string]::IsNullOrWhiteSpace($expectedIntent) -and $lastParsed.intent -eq $expectedIntent) {
                        break
                    }
                }

                $conversationLog | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $stepLogPath -Encoding UTF8
                $parsed = [ordered]@{
                    turns = $conversationLog
                    final = $lastParsed
                }

                if ($status -ne "FAILED") {
                    if ($null -eq $lastParsed) {
                        $status = "FAILED"
                        $summary = "chat_conversation produced no turns."
                    } elseif (-not [string]::IsNullOrWhiteSpace($expectedIntent) -and $lastParsed.intent -ne $expectedIntent) {
                        $status = "FAILED"
                        $summary = "Expected final intent $expectedIntent, actual $($lastParsed.intent)"
                    } else {
                        $summary = "Conversation final intent=$($lastParsed.intent); turns=$($conversationLog.Count)"
                    }
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

            "failure_package_smoke" {
                $failurePlanPath = Resolve-TargetPath $params.failure_plan_path
                $expectedNestedGoalId = [string]$params.expect_nested_goal_id
                $intentionalFailureRunId = "$RunId-intentional-failure"
                $nestedArguments = @(
                    "-NoProfile", "-ExecutionPolicy", "Bypass",
                    "-File", $PSCommandPath,
                    "-PlanPath", $failurePlanPath,
                    "-RunId", $intentionalFailureRunId,
                    "-Json"
                )

                $nestedLogPath = Join-Path $logRoot "step-$($stepNumber.ToString('000'))-intentional-failure.log"
                $nestedCapture = Invoke-CommandCapture -FilePath "powershell" -Arguments $nestedArguments -StepLogPath $nestedLogPath
                if ($nestedCapture.exit_code -ne 0) {
                    $status = "FAILED"
                    $summary = "Intentional failure plan runner exited with code $($nestedCapture.exit_code)"
                    break
                }

                $nestedReportPath = Join-Path $runsRoot "$intentionalFailureRunId\test-agent-report.json"
                if (-not (Test-Path -LiteralPath $nestedReportPath)) {
                    $status = "FAILED"
                    $summary = "Intentional failure plan did not write a Test Agent report."
                    break
                }

                $nestedReport = Get-Content -LiteralPath $nestedReportPath -Raw | ConvertFrom-Json
                $nestedFailures = [int]$nestedReport.actual.steps_failed
                if ($nestedFailures -lt 1) {
                    $status = "FAILED"
                    $summary = "Intentional failure plan unexpectedly passed."
                    break
                }

                if (-not [string]::IsNullOrWhiteSpace($expectedNestedGoalId) -and
                    -not [string]::Equals([string]$nestedReport.goal_id, $expectedNestedGoalId, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $status = "FAILED"
                    $summary = "Expected nested goal '$expectedNestedGoalId', actual '$($nestedReport.goal_id)'."
                    break
                }

                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "failure", "latest",
                    "--for-codex",
                    "--runs-root", $runsRoot,
                    "--run-id", $intentionalFailureRunId
                )

                $commandText = "powershell " + ($nestedArguments -join " ") + "; dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "failure_package_smoke exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $jsonExists = Test-Path -LiteralPath ([string]$parsed.jsonPath)
                $markdownExists = Test-Path -LiteralPath ([string]$parsed.markdownPath)
                $reportExists = Test-Path -LiteralPath ([string]$parsed.reportPath)
                $hasRepro = -not [string]::IsNullOrWhiteSpace([string]$parsed.reproCommand)
                $hasValidation = -not [string]::IsNullOrWhiteSpace([string]$parsed.validationCommand)
                $hasFailure = -not [string]::IsNullOrWhiteSpace([string]$parsed.failureReason)

                if (-not $jsonExists -or -not $markdownExists -or -not $reportExists -or -not $hasRepro -or -not $hasValidation -or -not $hasFailure) {
                    $status = "FAILED"
                    $summary = "Failure package smoke produced incomplete package."
                    break
                }

                $package = Get-Content -LiteralPath ([string]$parsed.jsonPath) -Raw | ConvertFrom-Json
                $packageFailures = New-Object System.Collections.Generic.List[string]
                if (-not [string]::Equals([string]$package.goalId, [string]$nestedReport.goal_id, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $packageFailures.Add("Package goalId '$($package.goalId)' did not match nested report goal '$($nestedReport.goal_id)'.") | Out-Null
                }
                if ([string]::IsNullOrWhiteSpace([string]$package.expectedJson) -or [string]$package.expectedJson -eq "{}") {
                    $packageFailures.Add("Package did not include expectedJson evidence.") | Out-Null
                }
                if ([string]::IsNullOrWhiteSpace([string]$package.actualJson) -or [string]$package.actualJson -eq "{}") {
                    $packageFailures.Add("Package did not include actualJson evidence.") | Out-Null
                }
                if (@($package.evidencePaths).Count -lt 1) {
                    $packageFailures.Add("Package did not include evidence paths.") | Out-Null
                }
                if (@($package.likelyAreas).Count -lt 1) {
                    $packageFailures.Add("Package did not include likely areas.") | Out-Null
                }
                if (@($package.safetyRules).Count -lt 1) {
                    $packageFailures.Add("Package did not include safety rules.") | Out-Null
                }

                if ($packageFailures.Count -gt 0) {
                    $status = "FAILED"
                    $summary = $packageFailures[0]
                } else {
                    $parsed = [ordered]@{
                        intentional_failure_report = $nestedReportPath
                        nested_goal_id = [string]$nestedReport.goal_id
                        package = $package
                        package_command = $parsed
                    }
                    $summary = "Failure package smoke wrote $($package.runRoot)\failure-package.md"
                }
            }

            "agent_list" {
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "list"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "agent_list exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $agents = @($parsed.agents)
                $expectedCount = if ($params.expect_agent_count) { [int]$params.expect_agent_count } else { 8 }
                $tester = $agents | Where-Object { $_.name -eq "TesterAgent" } | Select-Object -First 1

                if ($agents.Count -ne $expectedCount) {
                    $status = "FAILED"
                    $summary = "Expected $expectedCount agents, actual $($agents.Count)."
                } elseif ($null -eq $tester) {
                    $status = "FAILED"
                    $summary = "Expected TesterAgent to be registered."
                } elseif ($tester.defaultModelProfile -ne "cheap-runner") {
                    $status = "FAILED"
                    $summary = "Expected TesterAgent profile cheap-runner, actual $($tester.defaultModelProfile)."
                } else {
                    $summary = "Registered agents=$($agents.Count); TesterAgent profile=$($tester.defaultModelProfile)"
                }
            }

            "agent_profiles" {
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "profiles"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "agent_profiles exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $profiles = @($parsed.profiles)
                $nonOpenAi = @($profiles | Where-Object { $_.Provider -ne "OpenAI" })
                $cheapRunner = $profiles | Where-Object { $_.Name -eq "cheap-runner" } | Select-Object -First 1

                if ($profiles.Count -lt 5) {
                    $status = "FAILED"
                    $summary = "Expected at least 5 model profiles, actual $($profiles.Count)."
                } elseif ($nonOpenAi.Count -gt 0) {
                    $status = "FAILED"
                    $summary = "014 allows OpenAI profiles only; found $($nonOpenAi[0].Provider)."
                } elseif ($null -eq $cheapRunner -or $cheapRunner.Model -ne "gpt-4o-mini") {
                    $status = "FAILED"
                    $summary = "Expected cheap-runner to use gpt-4o-mini."
                } else {
                    $summary = "Model profiles=$($profiles.Count); provider boundary OpenAI-only."
                }
            }

            "agent_tester_run_plan" {
                $planPath = [string]$params.plan_path
                $testerRunId = "$RunId-agent-step-$stepNumber"
                $expectedModelProfile = if ($params.expect_model_profile) { [string]$params.expect_model_profile } else { "cheap-runner" }
                $expectedProvider = if ($params.expect_provider) { [string]$params.expect_provider } else { "OpenAI" }
                $expectedNestedGoalId = [string]$params.expect_nested_goal_id
                $expectedNestedStatus = [string]$params.expect_nested_status
                $expectedNestedOverallResult = [string]$params.expect_nested_overall_result
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "tester", "run-plan",
                    "--plan", $planPath,
                    "--run-id", $testerRunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "agent_tester_run_plan exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                if ($parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected TesterAgent status Succeeded, actual $($parsed.status)."
                } elseif ($parsed.modelProfile -ne $expectedModelProfile) {
                    $status = "FAILED"
                    $summary = "Expected TesterAgent model profile $expectedModelProfile, actual $($parsed.modelProfile)."
                } elseif ($parsed.provider -ne $expectedProvider) {
                    $status = "FAILED"
                    $summary = "Expected TesterAgent provider $expectedProvider, actual $($parsed.provider)."
                } elseif (-not [string]::IsNullOrWhiteSpace($expectedNestedGoalId) -and $parsed.report.goal_id -ne $expectedNestedGoalId) {
                    $status = "FAILED"
                    $summary = "Expected nested report goal_id $expectedNestedGoalId, actual $($parsed.report.goal_id)."
                } elseif (-not [string]::IsNullOrWhiteSpace($expectedNestedStatus) -and $parsed.report.status -ne $expectedNestedStatus) {
                    $status = "FAILED"
                    $summary = "Expected nested report status $expectedNestedStatus, actual $($parsed.report.status)."
                } elseif (-not [string]::IsNullOrWhiteSpace($expectedNestedOverallResult) -and $parsed.report.overall_result -ne $expectedNestedOverallResult) {
                    $status = "FAILED"
                    $summary = "Expected nested report overall_result $expectedNestedOverallResult, actual $($parsed.report.overall_result)."
                } else {
                    $summary = "TesterAgent ran plan with profile=$($parsed.modelProfile); summary=$($parsed.summary)"
                }
            }

            "weaviate_health" {
                $endpoint = if ($params.endpoint) { [string]$params.endpoint } else { "http://localhost:8080" }
                $metaUri = "$endpoint/v1/meta"
                $schemaUri = "$endpoint/v1/schema"
                $commandText = "Invoke-RestMethod $metaUri; Invoke-RestMethod $schemaUri"

                $health = [ordered]@{
                    endpoint = $endpoint
                    meta_uri = $metaUri
                    schema_uri = $schemaUri
                    meta_ok = $false
                    schema_ok = $false
                    version = $null
                    collections = $null
                }

                try {
                    $meta = Invoke-RestMethod -Uri $metaUri -Method Get -TimeoutSec 5
                    $schema = Invoke-RestMethod -Uri $schemaUri -Method Get -TimeoutSec 5
                    $health.meta_ok = $true
                    $health.schema_ok = $true
                    $health.version = $meta.version
                    $health.collections = @($schema.classes).Count
                    $parsed = $health
                    $health | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $stepLogPath -Encoding UTF8
                    $summary = "Weaviate healthy; version=$($health.version); collections=$($health.collections)"
                } catch {
                    $status = "FAILED"
                    $summary = "Weaviate health check failed: $($_.Exception.Message)"
                    $health.error = $_.Exception.Message
                    $parsed = $health
                    $health | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $stepLogPath -Encoding UTF8
                }
            }

            "docs_search" {
                $query = [string]$params.query
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $take = if ($params.take) { [string]$params.take } else { "5" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "docs", "search", $query,
                    "--project", $project,
                    "--take", $take
                )
                if ($params.store_root) {
                    $arguments += @("--store-root", [string]$params.store_root)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "docs_search exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $matches = @($parsed.matches)
                $top = if ($matches.Count -gt 0) { $matches[0] } else { $null }

                if ($null -eq $top) {
                    $status = "FAILED"
                    $summary = "docs_search returned no matches"
                    break
                }

                $expectedTitle = [string]$params.expect_top_title_contains
                $expectedProject = [string]$params.expect_top_project
                $expectedType = [string]$params.expect_top_document_type
                $expectedAuthority = [string]$params.expect_top_authority
                $expectSourcePresent = Convert-ToBool $params.expect_source_present $false
                $mustNotContain = @($params.must_not_primary_title_contain)
                $failures = New-Object System.Collections.Generic.List[string]

                if (-not [string]::IsNullOrWhiteSpace($expectedTitle) -and -not (Test-StringContains -Value ([string]$top.document.title) -Expected $expectedTitle)) {
                    $failures.Add("Expected top title to contain '$expectedTitle', actual '$($top.document.title)'.") | Out-Null
                }

                if (-not [string]::IsNullOrWhiteSpace($expectedProject) -and -not [string]::Equals([string]$top.document.project, $expectedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("Expected top project '$expectedProject', actual '$($top.document.project)'.") | Out-Null
                }

                if (-not [string]::IsNullOrWhiteSpace($expectedType) -and -not [string]::Equals([string]$top.document.documentType, $expectedType, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("Expected top document type '$expectedType', actual '$($top.document.documentType)'.") | Out-Null
                }

                if (-not [string]::IsNullOrWhiteSpace($expectedAuthority) -and -not [string]::Equals([string]$top.document.authority, $expectedAuthority, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("Expected top authority '$expectedAuthority', actual '$($top.document.authority)'.") | Out-Null
                }

                if ($expectSourcePresent -and [string]::IsNullOrWhiteSpace([string]$top.document.source)) {
                    $failures.Add("Expected top document to include a source link.") | Out-Null
                }

                foreach ($forbidden in $mustNotContain) {
                    if (-not [string]::IsNullOrWhiteSpace([string]$forbidden) -and (Test-StringContains -Value ([string]$top.document.title) -Expected ([string]$forbidden))) {
                        $failures.Add("Top title must not contain '$forbidden', actual '$($top.document.title)'.") | Out-Null
                    }
                }

                if ($failures.Count -gt 0) {
                    $status = "FAILED"
                    $summary = $failures[0]
                } else {
                    $summary = "docs_search top match '$($top.document.title)' score=$($top.score)"
                }
            }

            "memory_search" {
                $query = [string]$params.query
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $take = if ($params.take) { [string]$params.take } else { "5" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "search", $query,
                    "--project", $project,
                    "--take", $take,
                    "--json",
                    "--dogfood-run-id", $RunId
                )
                if ($params.store_root) {
                    $arguments += @("--store-root", [string]$params.store_root)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "memory_search exited with code $exitCode"
                    break
                }

                $parsed = $capture.output | ConvertFrom-Json
                $matches = @($parsed.matches)
                $top = if ($matches.Count -gt 0) { $matches[0] } else { $null }

                if ($null -eq $top) {
                    $status = "FAILED"
                    $summary = "memory_search returned no matches"
                    break
                }

                $expectedTitle = [string]$params.expect_top_title_contains
                $expectedProject = [string]$params.expect_project
                $expectSourcePresent = Convert-ToBool $params.expect_source_present $true
                $expectTracePresent = Convert-ToBool $params.expect_semantic_trace_id $true
                $expectRawAndFinalRank = Convert-ToBool $params.expect_raw_and_final_rank $true
                $failures = New-Object System.Collections.Generic.List[string]

                if (-not [string]::IsNullOrWhiteSpace($expectedTitle) -and -not (Test-StringContains -Value ([string]$top.documentTitle) -Expected $expectedTitle)) {
                    $failures.Add("Expected top memory title to contain '$expectedTitle', actual '$($top.documentTitle)'.") | Out-Null
                }

                if (-not [string]::IsNullOrWhiteSpace($expectedProject) -and -not [string]::Equals([string]$parsed.project.name, $expectedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("Expected memory search project '$expectedProject', actual '$($parsed.project.name)'.") | Out-Null
                }

                if ($expectSourcePresent -and ([string]::IsNullOrWhiteSpace([string]$top.documentId) -or [string]::IsNullOrWhiteSpace([string]$top.documentVersionId) -or @($top.sourceLinks).Count -eq 0)) {
                    $failures.Add("Expected top memory match to include document/version/source links.") | Out-Null
                }

                if ($expectTracePresent -and [string]::IsNullOrWhiteSpace([string]$parsed.semanticTraceId)) {
                    $failures.Add("Expected memory search to include semanticTraceId.") | Out-Null
                }

                if ($expectRawAndFinalRank -and ($null -eq $top.rawWeaviateRank -or $null -eq $top.finalIronDevRank)) {
                    $failures.Add("Expected top memory match to include rawWeaviateRank and finalIronDevRank.") | Out-Null
                }

                if ($failures.Count -gt 0) {
                    $status = "FAILED"
                    $summary = $failures[0]
                } else {
                    $summary = "memory_search top match '$($top.documentTitle)' rawRank=$($top.rawWeaviateRank) finalRank=$($top.finalIronDevRank)"
                }
            }

            "sql_document_version_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $query = if ($params.query) { [string]$params.query } else { "current first goal" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "sql-version-smoke",
                    "--project", $project,
                    "--query", $query,
                    "--dogfood-run-id", $RunId
                )
                if ($params.connection_string) {
                    $arguments += @("--connection-string", [string]$params.connection_string)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed -and $parsed.results) {
                        "SQL document version smoke failed; top version=$($parsed.results[0].sourceVersionId), expected=$($parsed.expected.topSourceVersionId)"
                    } else {
                        "sql_document_version_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "SQL document version smoke returned Passed=false"
                } else {
                    $summary = "SQL current document version wins; trace=$($parsed.semanticTraceId)"
                }
            }

            "weaviate_sql_document_version_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $query = if ($params.query) { [string]$params.query } else { "first Codex goal builder output" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "weaviate-sql-version-smoke",
                    "--project", $project,
                    "--query", $query,
                    "--dogfood-run-id", $RunId
                )
                if ($params.connection_string) {
                    $arguments += @("--connection-string", [string]$params.connection_string)
                }
                if ($params.weaviate_endpoint) {
                    $arguments += @("--weaviate-endpoint", [string]$params.weaviate_endpoint)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed -and $parsed.results) {
                        "Weaviate SQL smoke failed; final top version=$($parsed.results[0].sourceVersionId)"
                    } else {
                        "weaviate_sql_document_version_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Weaviate SQL document version smoke returned Passed=false"
                } else {
                    $top = $parsed.results | Select-Object -First 1
                    $summary = "Weaviate raw retrieval corrected by authority ranking; rawRank=$($top.rawWeaviateRank), finalRank=$($top.finalAuthorityRank), trace=$($parsed.semanticTraceId)"
                }
            }

            "cross_project_memory_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $bleedProject = if ($params.bleed_project) { [string]$params.bleed_project } else { "BookSeller" }
                $query = if ($params.query) { [string]$params.query } else { "first Codex goal checkout flow" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "cross-project-smoke",
                    "--project", $project,
                    "--bleed-project", $bleedProject,
                    "--query", $query,
                    "--dogfood-run-id", $RunId
                )
                if ($params.connection_string) {
                    $arguments += @("--connection-string", [string]$params.connection_string)
                }
                if ($params.weaviate_endpoint) {
                    $arguments += @("--weaviate-endpoint", [string]$params.weaviate_endpoint)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed -and $parsed.decisions) {
                        "Cross-project smoke failed; raw top project=$($parsed.decisions[0].projectName)"
                    } else {
                        "cross_project_memory_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Cross-project memory smoke returned Passed=false"
                } else {
                    $rawTop = $parsed.decisions | Select-Object -First 1
                    $accepted = $parsed.decisions | Where-Object { $_.decision -eq "accepted_project_authority" } | Select-Object -First 1
                    $summary = "Cross-project raw retrieval rejected; rawTop=$($rawTop.projectName), accepted=$($accepted.projectName), trace=$($parsed.semanticTraceId)"
                }
            }

            "ticket_source_link_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "ticket-source-link-smoke",
                    "--project", $project,
                    "--dogfood-run-id", $RunId
                )
                if ($params.connection_string) {
                    $arguments += @("--connection-string", [string]$params.connection_string)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed) {
                        "Ticket source-link smoke failed; ticket=$($parsed.ticketId); status=$($parsed.linkResolutionStatus)"
                    } else {
                        "ticket_source_link_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Ticket source-link smoke returned Passed=false"
                } else {
                    $summary = "Ticket $($parsed.ticketId) resolved to ProjectDocumentVersion $($parsed.sourceDocumentVersionId); orphanReported=$($parsed.orphanReportedAsFailure)"
                }
            }

            "builder_context_source_memory_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "builder-context-source-smoke",
                    "--project", $project,
                    "--dogfood-run-id", $RunId
                )
                if ($params.connection_string) {
                    $arguments += @("--connection-string", [string]$params.connection_string)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed) {
                        "Builder context source smoke failed; ticket=$($parsed.ticketId); sourceVersion=$($parsed.sourceDocumentVersionId)"
                    } else {
                        "builder_context_source_memory_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Builder context source smoke returned Passed=false"
                } else {
                    $summary = "Builder context included ticket $($parsed.ticketId), source ProjectDocumentVersion $($parsed.sourceDocumentVersionId), wrongProjectExcluded=$($parsed.builderContext.wrongProjectMemoryExcluded)"
                }
            }

            "builder_proposal_safety_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "builder", "proposal-safety-smoke",
                    "--project", $project,
                    "--dogfood-run-id", $RunId
                )
                if ($params.connection_string) {
                    $arguments += @("--connection-string", [string]$params.connection_string)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed) {
                        "Builder proposal safety smoke failed; ticket=$($parsed.ticketId); fileUnchanged=$($parsed.safety.fileUnchangedAfterPreview)"
                    } else {
                        "builder_proposal_safety_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Builder proposal safety smoke returned Passed=false"
                } else {
                    $summary = "Builder proposal safety passed; ticket=$($parsed.ticketId); proposedFiles=$($parsed.proposal.proposedFileCount); applyBlocked=$($parsed.safety.approvalGateBlockedApply)"
                }
            }

            "dotnet_build" {
                $target = Resolve-TargetPath $params.target
                $arguments = @("build", $target, "-p:UseSharedCompilation=false", "-nr:false")
                if ($params.configuration) {
                    $arguments += @("--configuration", [string]$params.configuration)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                $warningCount = Get-OutputLineValue -Output $capture.output -Pattern "(\d+)\s+Warning\(s\)"
                $errorCount = Get-OutputLineValue -Output $capture.output -Pattern "(\d+)\s+Error\(s\)"
                $parsed = [ordered]@{
                    target = $target
                    warnings = if ($warningCount -ne $null) { [int]$warningCount } else { $null }
                    errors = if ($errorCount -ne $null) { [int]$errorCount } else { $null }
                    log_path = $stepLogPath
                }
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "dotnet build failed with code $exitCode"
                } else {
                    $summary = "dotnet build succeeded; warnings=$($parsed.warnings); errors=$($parsed.errors)"
                }
            }

            "dotnet_test" {
                $target = Resolve-TargetPath $params.target
                $resultsDir = Join-Path $artifactsRoot ("test-results-step-{0:000}" -f $stepNumber)
                New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
                $arguments = @(
                    "test", $target,
                    "--logger", "trx",
                    "--results-directory", $resultsDir,
                    "-p:UseSharedCompilation=false",
                    "-nr:false"
                )
                if ($params.filter) {
                    $arguments += @("--filter", [string]$params.filter)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                $passedCount = Get-OutputLineValue -Output $capture.output -Pattern "Passed:\s+(\d+)"
                $failedCount = Get-OutputLineValue -Output $capture.output -Pattern "Failed:\s+(\d+)"
                $skippedCount = Get-OutputLineValue -Output $capture.output -Pattern "Skipped:\s+(\d+)"
                $parsed = [ordered]@{
                    target = $target
                    results_directory = $resultsDir
                    trx_files = @(Get-ChildItem -Path $resultsDir -Recurse -Filter *.trx -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
                    passed = if ($passedCount -ne $null) { [int]$passedCount } else { $null }
                    failed = if ($failedCount -ne $null) { [int]$failedCount } else { $null }
                    skipped = if ($skippedCount -ne $null) { [int]$skippedCount } else { $null }
                }
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "dotnet test failed with code $exitCode"
                } else {
                    $summary = "dotnet test succeeded; passed=$($parsed.passed); failed=$($parsed.failed); skipped=$($parsed.skipped)"
                }
            }

            "coverage_run" {
                $target = Resolve-TargetPath $params.target
                $coverageDir = Join-Path $artifactsRoot ("coverage-step-{0:000}" -f $stepNumber)
                New-Item -ItemType Directory -Force -Path $coverageDir | Out-Null
                $arguments = @(
                    "test", $target,
                    "--collect:XPlat Code Coverage",
                    "--results-directory", $coverageDir,
                    "-p:UseSharedCompilation=false",
                    "-nr:false"
                )
                if ($params.filter) {
                    $arguments += @("--filter", [string]$params.filter)
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                $coverageFiles = @(Get-ChildItem -Path $coverageDir -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
                $parsed = [ordered]@{
                    target = $target
                    coverage_directory = $coverageDir
                    coverage_files = $coverageFiles
                }
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "coverage test run failed with code $exitCode"
                } elseif ($coverageFiles.Count -eq 0) {
                    $status = "FAILED"
                    $summary = "coverage test run succeeded but no coverage.cobertura.xml was found"
                } else {
                    $summary = "coverage test run succeeded; files=$($coverageFiles.Count)"
                }
            }

            "coverage_report" {
                $reports = if ($params.reports) { [string]$params.reports } else { Join-Path $artifactsRoot "**\coverage.cobertura.xml" }
                $targetDir = if ($params.targetdir) { [string]$params.targetdir } else { Join-Path $artifactsRoot "coverage-report" }
                if (-not [System.IO.Path]::IsPathRooted($targetDir)) {
                    $targetDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $targetDir))
                }

                $toolArgs = @("-reports:$reports", "-targetdir:$targetDir")
                $attempts = New-Object System.Collections.Generic.List[string]
                $localToolCommand = "dotnet tool run reportgenerator -- " + ($toolArgs -join " ")
                $attempts.Add($localToolCommand) | Out-Null
                $toolCapture = Invoke-CommandCapture -FilePath "dotnet" -Arguments (@("tool", "run", "reportgenerator", "--") + $toolArgs) -StepLogPath $stepLogPath
                $exitCode = $toolCapture.exit_code
                $commandText = $localToolCommand

                if ($exitCode -ne 0) {
                    $globalTool = Get-Command "reportgenerator" -ErrorAction SilentlyContinue
                    if ($null -ne $globalTool) {
                        $globalToolCommand = "reportgenerator " + ($toolArgs -join " ")
                        $attempts.Add($globalToolCommand) | Out-Null
                        $toolCapture = Invoke-CommandCapture -FilePath "reportgenerator" -Arguments $toolArgs -StepLogPath $stepLogPath
                        $exitCode = $toolCapture.exit_code
                        $commandText = $globalToolCommand
                    } else {
                        $missingMessage = "ReportGenerator is not installed as a local dotnet tool or global command. Install with: dotnet tool install dotnet-reportgenerator-globaltool --global"
                        Add-Content -LiteralPath $stepLogPath -Value "`n$missingMessage" -Encoding UTF8
                    }
                }

                $parsed = [ordered]@{
                    reports = $reports
                    target_directory = $targetDir
                    index_html = Join-Path $targetDir "index.html"
                    attempted_commands = $attempts
                }
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "ReportGenerator failed or is not installed; see log"
                } else {
                    $summary = "coverage report generated at $targetDir"
                }
            }

            "format_check" {
                $target = Resolve-TargetPath $params.target
                $arguments = @("format", $target, "--verify-no-changes")
                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                $parsed = [ordered]@{
                    target = $target
                    log_path = $stepLogPath
                }
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "dotnet format found formatting/style drift or failed"
                } else {
                    $summary = "dotnet format verify passed"
                }
            }

            "package_audit" {
                $target = Resolve-TargetPath $params.target
                $arguments = @("package", "list", "--project", $target, "--vulnerable", "--include-transitive")
                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                $hasVulnerable = $capture.output -match "(?i)(critical|high|moderate|low)\s+https?://"
                $parsed = [ordered]@{
                    target = $target
                    vulnerabilities_detected = [bool]$hasVulnerable
                    log_path = $stepLogPath
                }
                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "NuGet vulnerability audit command failed with code $exitCode"
                } elseif ($hasVulnerable) {
                    $status = "FAILED"
                    $summary = "NuGet vulnerability audit found vulnerable packages"
                } else {
                    $summary = "NuGet vulnerability audit found no vulnerable packages"
                }
            }

            "code_standards_check" {
                $commandText = "Invoke-CodeStandardsCheck"
                $quality = Invoke-CodeStandardsCheck -Params $params -StepLogPath $stepLogPath
                $exitCode = [int]$quality.exit_code
                $parsed = $quality.result

                if ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = "Code standards gate failed; errors=$($parsed.error_count), warnings=$($parsed.warning_count)"
                } elseif ([int]$parsed.warning_count -gt 0) {
                    $summary = "Code standards gate passed with warnings=$($parsed.warning_count)"
                } else {
                    $summary = "Code standards gate passed with no findings"
                }
            }

            default {
                $status = "SKIPPED_UNSUPPORTED"
                $summary = "Unsupported Test Agent action: $action"
                Set-Content -LiteralPath $stepLogPath -Value $summary -Encoding UTF8
            }
        }
    } catch {
        $status = "FAILED"
        $summary = "$($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
        Set-Content -LiteralPath $stepLogPath -Value ($summary + "`n" + ($_.ScriptStackTrace | Out-String)) -Encoding UTF8
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
        trace = [ordered]@{
            trace_group_id = $traceGroupId
            dogfood_run_id = $RunId
            agent_role = "TestAgent"
            provider = "LocalCli"
            model = "deterministic-headless"
            command = $commandText
        }
        parsed = $parsed
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
    goal_id = if ($plan.goal_id) { [string]$plan.goal_id } else { "ad-hoc" }
    plan_path = $PlanPath
    status = switch ($overall) {
        "SUCCESS" { "passed" }
        "FAILED" { "failed" }
        "PARTIAL_SUCCESS" { "partial" }
        "BLOCKED" { "blocked" }
        default { "skipped" }
    }
    overall_result = $overall
    summary = "Steps passed: $passed; failed: $failed; skipped: $skipped."
    commands_run = @($stepResults | ForEach-Object { $_.command } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    expected = if ($plan.expected) { $plan.expected } else { [ordered]@{} }
    actual = [ordered]@{
        steps_passed = $passed
        steps_failed = $failed
        steps_skipped = $skipped
    }
    evidence = @($stepResults | ForEach-Object {
        [ordered]@{
            type = $_.action
            id = "step-$($_.step)"
            path = $_.log_path
            problem = if ($_.status -eq "FAILED") { $_.summary } else { "" }
        }
    })
    trace = [ordered]@{
        trace_group_id = $traceGroupId
        dogfood_run_id = $RunId
        agent_role = "TestAgent"
        provider = "LocalCli"
        model = "deterministic-headless"
    }
    key_metrics = [ordered]@{
        build_success = $null
        unit_test_pass_rate = $null
        coverage_percent = $null
        api_drive_success_rate = $null
        model_calls = 0
        estimated_cost = 0
        cli_commands_run = @($stepResults | Where-Object { -not [string]::IsNullOrWhiteSpace($_.command) }).Count
        failures_found = $failed
        useful_failures = $failed
        wasted_runs = 0
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

$schemaValidation = Test-ReportShape -Report $report
$report["report_schema_valid"] = $schemaValidation.valid
$report["report_schema_validation"] = $schemaValidation

$reportPath = Join-Path $runRoot "test-agent-report.json"
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding UTF8

if ($Json) {
    $report | ConvertTo-Json -Depth 20
} else {
    Write-Host "Test Agent run complete: $overall"
    Write-Host "Report: $reportPath"
    Write-Host "Logs: $logRoot"
}

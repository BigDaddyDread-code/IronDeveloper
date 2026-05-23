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
                        $validationFailures = [System.Collections.Generic.List[string]]::new()
                        if ($params.expected_outcome.first_turn_intent -and $conversationLog.Count -gt 0) {
                            $firstIntent = [string]$conversationLog[0].intent
                            if ($firstIntent -ne [string]$params.expected_outcome.first_turn_intent) {
                                $validationFailures.Add("Expected first turn intent $($params.expected_outcome.first_turn_intent), actual $firstIntent.") | Out-Null
                            }
                        }

                        if ($null -ne $params.expected_outcome.requires_action) {
                            $expectedRequiresAction = Convert-ToBool $params.expected_outcome.requires_action $false
                            if ([bool]$lastParsed.requiresAction -ne $expectedRequiresAction) {
                                $validationFailures.Add("Expected final requiresAction=$expectedRequiresAction, actual $($lastParsed.requiresAction).") | Out-Null
                            }
                        }

                        if ($null -ne $params.expected_outcome.allows_prose_response) {
                            $expectedAllowsProse = Convert-ToBool $params.expected_outcome.allows_prose_response $true
                            if ([bool]$lastParsed.allowsProseResponse -ne $expectedAllowsProse) {
                                $validationFailures.Add("Expected final allowsProseResponse=$expectedAllowsProse, actual $($lastParsed.allowsProseResponse).") | Out-Null
                            }
                        }

                        if ($params.expected_outcome.min_discussion_documents) {
                            $minDocuments = [int]$params.expected_outcome.min_discussion_documents
                            if ([int]$lastParsed.simulatedDiscussionDocuments -lt $minDocuments) {
                                $validationFailures.Add("Expected at least $minDocuments simulated discussion documents, actual $($lastParsed.simulatedDiscussionDocuments).") | Out-Null
                            }
                        }

                        if ($params.expected_outcome.expect_no_file_writes -and [int]$lastParsed.simulatedFilesChanged -ne 0) {
                            $validationFailures.Add("Expected no file writes, actual $($lastParsed.simulatedFilesChanged).") | Out-Null
                        }

                        if ($validationFailures.Count -gt 0) {
                            $status = "FAILED"
                            $summary = $validationFailures -join " "
                        } else {
                            $summary = "Conversation final intent=$($lastParsed.intent); turns=$($conversationLog.Count)"
                        }
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

            "critic_failure_package_review_smoke" {
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

                $nestedLogPath = Join-Path $logRoot "step-$($stepNumber.ToString('000'))-critic-intentional-failure.log"
                $nestedCapture = Invoke-CommandCapture -FilePath "powershell" -Arguments $nestedArguments -StepLogPath $nestedLogPath
                if ($nestedCapture.exit_code -ne 0) {
                    $status = "FAILED"
                    $summary = "Intentional failure plan runner exited with code $($nestedCapture.exit_code)"
                    break
                }

                $nestedReportPath = Join-Path $runsRoot "$intentionalFailureRunId\test-agent-report.json"
                $nestedReport = Get-Content -LiteralPath $nestedReportPath -Raw | ConvertFrom-Json
                if ([int]$nestedReport.actual.steps_failed -lt 1) {
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

                $packageArguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "failure", "latest",
                    "--for-codex",
                    "--runs-root", $runsRoot,
                    "--run-id", $intentionalFailureRunId
                )
                $packageCapture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $packageArguments -StepLogPath $stepLogPath
                if ($packageCapture.exit_code -ne 0) {
                    $status = "FAILED"
                    $summary = "failure latest exited with code $($packageCapture.exit_code)"
                    break
                }

                $packageCommand = $packageCapture.output | ConvertFrom-Json
                $packagePath = [string]$packageCommand.jsonPath
                $criticArguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "critic", "review-failure",
                    "--package", $packagePath,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "powershell " + ($nestedArguments -join " ") + "; dotnet " + ($packageArguments -join " ") + "; dotnet " + ($criticArguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $criticArguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "CriticAgent review exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected CriticAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected CriticAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } elseif ($params.expect_actionable -and -not [bool]$parsed.review.actionable) {
                    $status = "FAILED"
                    $summary = "Expected CriticAgent review to be actionable."
                } elseif ($params.expect_evidence_sufficient -and -not [bool]$parsed.review.evidenceSufficient) {
                    $status = "FAILED"
                    $summary = "Expected CriticAgent review to find sufficient evidence."
                } elseif ($params.expect_recommendation -and [string]$parsed.review.recommendation -ne [string]$params.expect_recommendation) {
                    $status = "FAILED"
                    $summary = "Expected CriticAgent recommendation '$($params.expect_recommendation)', actual '$($parsed.review.recommendation)'."
                } elseif ($params.expect_boundary_contains -and [string]$parsed.review.boundary -notlike "*$($params.expect_boundary_contains)*") {
                    $status = "FAILED"
                    $summary = "Expected CriticAgent boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.review.boundary)'."
                } else {
                    $summary = "CriticAgent reviewed failure package; recommendation=$($parsed.review.recommendation); actionable=$($parsed.review.actionable)."
                }
            }

            "agent_quality_run_gate" {
                $qualityPlanPath = if ($params.plan_path) { [string]$params.plan_path } else { "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "quality", "run-gate",
                    "--plan", $qualityPlanPath,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "QualityAgent gate exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } elseif ($params.expect_build_succeeded -and -not [bool]$parsed.qualityReport.BuildSucceeded) {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent build check to pass."
                } elseif ($params.expect_tests_succeeded -and -not [bool]$parsed.qualityReport.FocusedTestsSucceeded) {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent focused tests check to pass."
                } elseif ($params.expect_format_succeeded -and -not [bool]$parsed.qualityReport.FormatSucceeded) {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent format check to pass."
                } elseif ($params.expect_package_audit_succeeded -and -not [bool]$parsed.qualityReport.PackageAuditSucceeded) {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent package audit to pass."
                } elseif ($params.expect_code_standards_succeeded -and -not [bool]$parsed.qualityReport.CodeStandardsSucceeded) {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent code standards check to pass."
                } elseif ($params.expect_boundary_contains -and [string]$parsed.qualityReport.Boundary -notlike "*$($params.expect_boundary_contains)*") {
                    $status = "FAILED"
                    $summary = "Expected QualityAgent boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.qualityReport.Boundary)'."
                } else {
                    $summary = "QualityAgent gate status=$($parsed.qualityReport.Status); warnings=$($parsed.qualityReport.WarningCount); errors=$($parsed.qualityReport.ErrorCount)."
                }
            }

            "agent_planner_draft_test_plan" {
                $project = if ($params.project) { [string]$params.project } else { "BookSeller" }
                $goal = [string]$params.goal
                if ([string]::IsNullOrWhiteSpace($goal)) {
                    throw "agent_planner_draft_test_plan requires params.goal."
                }

                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "planner", "draft-test-plan",
                    "--project", $project,
                    "--goal", $goal,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "PlannerAgent draft exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected PlannerAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected PlannerAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } elseif ($params.expect_project -and [string]$parsed.draftPlan.project -ne [string]$params.expect_project) {
                    $status = "FAILED"
                    $summary = "Expected draft project '$($params.expect_project)', actual '$($parsed.draftPlan.project)'."
                } elseif ($params.expect_min_steps -and @($parsed.draftPlan.steps).Count -lt [int]$params.expect_min_steps) {
                    $status = "FAILED"
                    $summary = "Expected draft plan to contain at least $($params.expect_min_steps) steps."
                } elseif ($params.expect_action_contains -and -not (@($parsed.draftPlan.steps).action -contains [string]$params.expect_action_contains)) {
                    $status = "FAILED"
                    $summary = "Expected draft plan to contain action '$($params.expect_action_contains)'."
                } elseif ($params.expect_boundary_contains -and [string]$parsed.draftPlan.planner.boundary -notlike "*$($params.expect_boundary_contains)*") {
                    $status = "FAILED"
                    $summary = "Expected PlannerAgent boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.draftPlan.planner.boundary)'."
                } else {
                    $summary = "PlannerAgent drafted plan goal=$($parsed.draftPlan.goal_id); steps=$(@($parsed.draftPlan.steps).Count)."
                }
            }

            "agent_sentinel_observe" {
                $observedProject = if ($params.observed_project) { [string]$params.observed_project } else { "BookSeller" }
                $affectedProject = if ($params.affected_project) { [string]$params.affected_project } else { $observedProject }
                $findingType = if ($params.finding_type) { [string]$params.finding_type } else { "Observation" }
                $evidenceText = [string]$params.evidence
                if ([string]::IsNullOrWhiteSpace($evidenceText)) {
                    throw "agent_sentinel_observe requires params.evidence."
                }

                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "sentinel", "observe",
                    "--observed-project", $observedProject,
                    "--affected-project", $affectedProject,
                    "--finding-type", $findingType,
                    "--evidence", $evidenceText,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "SentinelAgent observe exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected SentinelAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected SentinelAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } elseif ($params.expect_observed_project -and [string]$parsed.insight.observedProject -ne [string]$params.expect_observed_project) {
                    $status = "FAILED"
                    $summary = "Expected observedProject '$($params.expect_observed_project)', actual '$($parsed.insight.observedProject)'."
                } elseif ($params.expect_affected_project -and [string]$parsed.insight.affectedProject -ne [string]$params.expect_affected_project) {
                    $status = "FAILED"
                    $summary = "Expected affectedProject '$($params.expect_affected_project)', actual '$($parsed.insight.affectedProject)'."
                } elseif ($params.expect_insight_type -and [string]$parsed.insight.insightType -ne [string]$params.expect_insight_type) {
                    $status = "FAILED"
                    $summary = "Expected insightType '$($params.expect_insight_type)', actual '$($parsed.insight.insightType)'."
                } elseif ($params.expect_severity -and [string]$parsed.insight.severity -ne [string]$params.expect_severity) {
                    $status = "FAILED"
                    $summary = "Expected severity '$($params.expect_severity)', actual '$($parsed.insight.severity)'."
                } elseif ($params.expect_boundary_contains -and [string]$parsed.insight.boundary -notlike "*$($params.expect_boundary_contains)*") {
                    $status = "FAILED"
                    $summary = "Expected SentinelAgent boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.insight.boundary)'."
                } else {
                    foreach ($disposition in @($params.expect_recommended_dispositions)) {
                        if ($disposition -and @($parsed.insight.recommendedDispositions | Where-Object { [string]$_ -eq [string]$disposition }).Count -eq 0) {
                            $status = "FAILED"
                            $summary = "Expected SentinelAgent recommended disposition '$disposition'."
                            break
                        }
                    }
                    if ($status -eq "SUCCESS") {
                        $summary = "SentinelAgent observed $($parsed.insight.insightType); observed=$($parsed.insight.observedProject); affected=$($parsed.insight.affectedProject)."
                    }
                }
            }

            "agent_research_package" {
                $project = if ($params.project) { [string]$params.project } else { "BookSeller" }
                $topic = [string]$params.topic
                if ([string]::IsNullOrWhiteSpace($topic)) {
                    throw "agent_research_package requires params.topic."
                }

                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "research", "package",
                    "--project", $project,
                    "--topic", $topic,
                    "--run-id", $RunId,
                    "--json"
                )
                if ($params.source_url) { $arguments += @("--source-url", [string]$params.source_url) }
                if ($params.source_title) { $arguments += @("--source-title", [string]$params.source_title) }
                if ($params.source_type) { $arguments += @("--source-type", [string]$params.source_type) }
                if ($params.snippet) { $arguments += @("--snippet", [string]$params.snippet) }
                if ($params.published_date) { $arguments += @("--published-date", [string]$params.published_date) }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "ResearchAgent package exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected ResearchAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected ResearchAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } elseif ($params.expect_project -and [string]$parsed.researchPackage.project -ne [string]$params.expect_project) {
                    $status = "FAILED"
                    $summary = "Expected research project '$($params.expect_project)', actual '$($parsed.researchPackage.project)'."
                } elseif ($params.expect_type -and [string]$parsed.researchPackage.type -ne [string]$params.expect_type) {
                    $status = "FAILED"
                    $summary = "Expected research package type '$($params.expect_type)', actual '$($parsed.researchPackage.type)'."
                } elseif ($params.expect_authority_warning_contains -and [string]$parsed.researchPackage.authorityWarning -notlike "*$($params.expect_authority_warning_contains)*") {
                    $status = "FAILED"
                    $summary = "Expected authority warning to contain '$($params.expect_authority_warning_contains)', actual '$($parsed.researchPackage.authorityWarning)'."
                } elseif ($params.expect_boundary_contains -and [string]$parsed.researchPackage.boundary -notlike "*$($params.expect_boundary_contains)*") {
                    $status = "FAILED"
                    $summary = "Expected ResearchAgent boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.researchPackage.boundary)'."
                } elseif ($params.expect_source_url -and @($parsed.researchPackage.sources | Where-Object { [string]$_.url -eq [string]$params.expect_source_url }).Count -eq 0) {
                    $status = "FAILED"
                    $summary = "Expected research source '$($params.expect_source_url)'."
                } else {
                    $summary = "ResearchAgent packaged topic='$($parsed.researchPackage.topic)' sources=$(@($parsed.researchPackage.sources).Count); confidence=$($parsed.researchPackage.confidenceScore)."
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
                $nestedPlanPath = [string]$params.plan_path
                $testerRunId = "$RunId-agent-step-$stepNumber"
                $expectedModelProfile = if ($params.expect_model_profile) { [string]$params.expect_model_profile } else { "cheap-runner" }
                $expectedProvider = if ($params.expect_provider) { [string]$params.expect_provider } else { "OpenAI" }
                $expectedNestedGoalId = [string]$params.expect_nested_goal_id
                $expectedNestedStatus = [string]$params.expect_nested_status
                $expectedNestedOverallResult = [string]$params.expect_nested_overall_result
                $compactReport = [bool]$params.compact_report
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "tester", "run-plan",
                    "--plan", $nestedPlanPath,
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
                    if ($compactReport) {
                        $nestedReport = $parsed.report
                        $parsed = [ordered]@{
                            command = $parsed.command
                            agent = $parsed.agent
                            status = $parsed.status
                            summary = $parsed.summary
                            modelProfile = $parsed.modelProfile
                            provider = $parsed.provider
                            model = $parsed.model
                            exitCode = $parsed.exitCode
                            nestedReport = [ordered]@{
                                test_run_id = $nestedReport.test_run_id
                                goal_id = $nestedReport.goal_id
                                status = $nestedReport.status
                                overall_result = $nestedReport.overall_result
                                summary = $nestedReport.summary
                                steps_passed = $nestedReport.actual.steps_passed
                                steps_failed = $nestedReport.actual.steps_failed
                                steps_skipped = $nestedReport.actual.steps_skipped
                                evidence_count = @($nestedReport.evidence).Count
                                full_log_location = $nestedReport.full_log_location
                            }
                        }
                    }
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

            "agent_retriever_search" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $query = [string]$params.query
                if ([string]::IsNullOrWhiteSpace($query)) {
                    throw "agent_retriever_search requires params.query."
                }
                $take = if ($params.take) { [string]$params.take } else { "5" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "retriever", "search",
                    "--project", $project,
                    "--query", [string]$query,
                    "--take", $take,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "agent_retriever_search exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected RetrieverAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected RetrieverAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } elseif ($params.expect_provider -and [string]$parsed.provider -ne [string]$params.expect_provider) {
                    $status = "FAILED"
                    $summary = "Expected RetrieverAgent provider $($params.expect_provider), actual $($parsed.provider)."
                } else {
                    $matches = @($parsed.contextPackage.Matches)
                    $top = $matches | Select-Object -First 1
                    $summary = "RetrieverAgent top match '$($top.DocumentTitle)' finalRank=$($top.FinalIronDevRank)."

                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    if ($params.expect_project -and [string]$parsed.contextPackage.Project.Name -ne [string]$params.expect_project) {
                        $validationFailures.Add("Expected context package project '$($params.expect_project)', actual '$($parsed.contextPackage.Project.Name)'.") | Out-Null
                    }

                    if ($params.expect_top_title_contains -and (-not $top -or [string]$top.DocumentTitle -notlike "*$($params.expect_top_title_contains)*")) {
                        $validationFailures.Add("Expected top context title to contain '$($params.expect_top_title_contains)', actual '$($top.DocumentTitle)'.") | Out-Null
                    }

                    if ($params.expect_source_present -and (-not $top.SourceLinks -or @($top.SourceLinks).Count -eq 0)) {
                        $validationFailures.Add("Expected top context match to include source links.") | Out-Null
                    }

                    if ($params.expect_semantic_trace_id -and -not $parsed.contextPackage.SemanticTraceId) {
                        $validationFailures.Add("Expected context package semantic trace id.") | Out-Null
                    }

                    if ($params.expect_raw_and_final_rank -and ($null -eq $top.RawWeaviateRank -or $null -eq $top.FinalIronDevRank)) {
                        $validationFailures.Add("Expected top context match to include raw and final ranks.") | Out-Null
                    }

                    if ($params.expect_context_bundle -and [string]$parsed.contextPackage.BundleKind -ne "RetrieverContextBundle") {
                        $validationFailures.Add("Expected RetrieverAgent context package BundleKind=RetrieverContextBundle.") | Out-Null
                    }

                    if ($params.expect_guidance_fields) {
                        if (-not $top.Guidance) {
                            $validationFailures.Add("Expected top context match to include guidance.") | Out-Null
                        }
                        if (-not $parsed.contextPackage.UseGuidance) {
                            $validationFailures.Add("Expected context package to include use guidance.") | Out-Null
                        }
                        if ($null -eq $parsed.contextPackage.AcceptedSources) {
                            $validationFailures.Add("Expected context package to include accepted sources.") | Out-Null
                        }
                        if ($null -eq $parsed.contextPackage.DemotedSources) {
                            $validationFailures.Add("Expected context package to include demoted sources.") | Out-Null
                        }
                        if ($null -eq $parsed.contextPackage.HistoricalSources) {
                            $validationFailures.Add("Expected context package to include historical sources.") | Out-Null
                        }
                    }

                    if ($params.expect_top_guidance -and [string]$top.Guidance -ne [string]$params.expect_top_guidance) {
                        $validationFailures.Add("Expected top context guidance '$($params.expect_top_guidance)', actual '$($top.Guidance)'.") | Out-Null
                    }

                    foreach ($term in @($params.expect_no_match_title_contains)) {
                        foreach ($match in $matches) {
                            if ($term -and [string]$match.DocumentTitle -like "*$term*") {
                                $validationFailures.Add("Expected RetrieverAgent matches not to contain title '$term', actual '$($match.DocumentTitle)'.") | Out-Null
                            }
                        }
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
                }
            }

            "agent_supervisor_run_goal" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $query = [string]$params.query
                $nestedPlanPath = [string]$params.plan_path
                if ([string]::IsNullOrWhiteSpace($query)) {
                    throw "agent_supervisor_run_goal requires params.query."
                }
                if ([string]::IsNullOrWhiteSpace($nestedPlanPath)) {
                    throw "agent_supervisor_run_goal requires params.plan_path."
                }

                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "agent", "supervisor", "run-goal",
                    "--project", $project,
                    "--query", $query,
                    "--plan", $nestedPlanPath,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "agent_supervisor_run_goal exited with code $exitCode"
                } elseif ([string]$parsed.status -ne "Succeeded") {
                    $status = "FAILED"
                    $summary = "Expected SupervisorAgent status Succeeded, actual $($parsed.status)."
                } elseif ($params.expect_model_profile -and [string]$parsed.modelProfile -ne [string]$params.expect_model_profile) {
                    $status = "FAILED"
                    $summary = "Expected SupervisorAgent model profile $($params.expect_model_profile), actual $($parsed.modelProfile)."
                } else {
                    $loopReport = $parsed.loopReport
                    $summary = "SupervisorAgent decision '$($loopReport.supervisor.decision)' with tester summary '$($loopReport.tester.summary)'."

                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    if ($params.expect_project -and [string]$loopReport.project -ne [string]$params.expect_project) {
                        $validationFailures.Add("Expected supervisor project '$($params.expect_project)', actual '$($loopReport.project)'.") | Out-Null
                    }

                    if ($params.expect_top_title_contains -and [string]$loopReport.memory.topTitle -notlike "*$($params.expect_top_title_contains)*") {
                        $validationFailures.Add("Expected supervisor memory top title to contain '$($params.expect_top_title_contains)', actual '$($loopReport.memory.topTitle)'.") | Out-Null
                    }

                    if ($params.expect_memory_succeeded -and -not [bool]$loopReport.memory.succeeded) {
                        $validationFailures.Add("Expected supervisor memory step to succeed.") | Out-Null
                    }

                    if ($params.expect_tester_succeeded -and -not [bool]$loopReport.tester.succeeded) {
                        $validationFailures.Add("Expected supervisor tester step to succeed.") | Out-Null
                    }

                    if ($params.expect_codex_handoff -and -not $loopReport.codexHandoff) {
                        $validationFailures.Add("Expected supervisor loop report to include codexHandoff.") | Out-Null
                    }

                    if ($params.expect_supervisor_decision -and [string]$loopReport.supervisor.decision -ne [string]$params.expect_supervisor_decision) {
                        $validationFailures.Add("Expected supervisor decision '$($params.expect_supervisor_decision)', actual '$($loopReport.supervisor.decision)'.") | Out-Null
                    }

                    if ($params.expect_allowed_decisions) {
                        $allowed = @($loopReport.supervisor.allowedDecisions)
                        foreach ($expectedDecision in @($params.expect_allowed_decisions)) {
                            if ($allowed -notcontains [string]$expectedDecision) {
                                $validationFailures.Add("Expected supervisor allowed decisions to include '$expectedDecision'.") | Out-Null
                            }
                        }
                    }

                    if ($params.expect_decision_evidence -and (-not $loopReport.supervisor.decisionEvidence -or @($loopReport.supervisor.decisionEvidence).Count -eq 0)) {
                        $validationFailures.Add("Expected supervisor decision evidence.") | Out-Null
                    }

                    if ($params.expect_boundary_contains -and [string]$loopReport.codexHandoff.boundary -notlike "*$($params.expect_boundary_contains)*") {
                        $validationFailures.Add("Expected handoff boundary to contain '$($params.expect_boundary_contains)', actual '$($loopReport.codexHandoff.boundary)'.") | Out-Null
                    }

                    if ($params.expect_tester_goal_id) {
                        $actualGoalId = [string]$loopReport.tester.report.goal_id
                        if ($actualGoalId -ne [string]$params.expect_tester_goal_id) {
                            $validationFailures.Add("Expected nested tester goal '$($params.expect_tester_goal_id)', actual '$actualGoalId'.") | Out-Null
                        }
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
                }
            }

            "discussion_document_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "BookSeller" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "docs", "discussion-smoke",
                    "--project", $project,
                    "--dogfood-run-id", $RunId
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "discussion_document_smoke exited with code $exitCode"
                } else {
                    $summary = "Discussion document $($parsed.documentId) currentVersion=$($parsed.currentVersionId); trace=$($parsed.semanticTraceId)"
                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    if ($params.expect_project -and [string]$parsed.projectName -ne [string]$params.expect_project) {
                        $validationFailures.Add("Expected project '$($params.expect_project)', actual '$($parsed.projectName)'.") | Out-Null
                    }
                    if ($params.expect_document_type -and [string]$parsed.documentType -ne [string]$params.expect_document_type) {
                        $validationFailures.Add("Expected document type '$($params.expect_document_type)', actual '$($parsed.documentType)'.") | Out-Null
                    }
                    if ($params.expect_current_version_status -and [string]$parsed.currentVersionStatus -ne [string]$params.expect_current_version_status) {
                        $validationFailures.Add("Expected current version status '$($params.expect_current_version_status)', actual '$($parsed.currentVersionStatus)'.") | Out-Null
                    }
                    if ($params.expect_source_link -and ([int]$parsed.currentSourceLinkCount -lt 1 -or [int]$parsed.draftSourceLinkCount -lt 1)) {
                        $validationFailures.Add("Expected source links on draft and current discussion document versions.") | Out-Null
                    }
                    if ($params.expect_semantic_trace_id -and -not $parsed.semanticTraceId) {
                        $validationFailures.Add("Expected semantic trace id.") | Out-Null
                    }
                    if ($params.expect_top_source_version_matches -and [string]$parsed.topSourceVersionId -ne [string]$parsed.currentVersionId) {
                        $validationFailures.Add("Expected top source version '$($parsed.currentVersionId)', actual '$($parsed.topSourceVersionId)'.") | Out-Null
                    }
                    if ($params.expect_boundary_contains -and [string]$parsed.boundary -notlike "*$($params.expect_boundary_contains)*") {
                        $validationFailures.Add("Expected boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.boundary)'.") | Out-Null
                    }
                    if (-not [bool]$parsed.passed) {
                        $validationFailures.Add("Discussion document smoke reported passed=false.") | Out-Null
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
                }
            }

            "document_to_tickets_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "BookSeller" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "tickets", "document-to-tickets-smoke",
                    "--project", $project,
                    "--dogfood-run-id", $RunId
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                if ($exitCode -ne 0 -or -not $parsed) {
                    $status = "FAILED"
                    $summary = "document_to_tickets_smoke exited with code $exitCode"
                } else {
                    $summary = "Document $($parsed.sourceDocumentId) generated $(@($parsed.ticketIds).Count) tickets; links=$($parsed.generatedTicketLinkCount)."
                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    if ($params.expect_project -and [string]$parsed.projectName -ne [string]$params.expect_project) {
                        $validationFailures.Add("Expected project '$($params.expect_project)', actual '$($parsed.projectName)'.") | Out-Null
                    }
                    if ($params.expect_ticket_count -and @($parsed.ticketIds).Count -ne [int]$params.expect_ticket_count) {
                        $validationFailures.Add("Expected $($params.expect_ticket_count) generated tickets, actual $(@($parsed.ticketIds).Count).") | Out-Null
                    }
                    if ($params.expect_all_tickets_linked -and -not [bool]$parsed.allTicketsLinked) {
                        $validationFailures.Add("Expected all generated tickets to preserve SourceDocumentVersionId and artifact source references.") | Out-Null
                    }
                    if ($params.expect_all_tickets_resolve -and -not [bool]$parsed.allTicketsResolve) {
                        $validationFailures.Add("Expected all generated tickets to resolve the exact source document version.") | Out-Null
                    }
                    if ($params.expect_generated_ticket_links -and [int]$parsed.generatedTicketLinkCount -lt [int]$params.expect_generated_ticket_links) {
                        $validationFailures.Add("Expected at least $($params.expect_generated_ticket_links) generated ticket document links, actual $($parsed.generatedTicketLinkCount).") | Out-Null
                    }
                    if ($params.expect_boundary_contains -and [string]$parsed.boundary -notlike "*$($params.expect_boundary_contains)*") {
                        $validationFailures.Add("Expected boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.boundary)'.") | Out-Null
                    }
                    if (-not [bool]$parsed.passed) {
                        $validationFailures.Add("Document-to-tickets smoke reported passed=false.") | Out-Null
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
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
                $mustNotContain = @($params.expect_no_match_title_contains)
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

                foreach ($forbidden in $mustNotContain) {
                    if ([string]::IsNullOrWhiteSpace([string]$forbidden)) {
                        continue
                    }

                    $badMatch = $matches | Where-Object { Test-StringContains -Value ([string]$_.documentTitle) -Expected ([string]$forbidden) } | Select-Object -First 1
                    if ($badMatch) {
                        $failures.Add("Memory search results must not contain title '$forbidden', actual '$($badMatch.documentTitle)'.") | Out-Null
                    }
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

            "memory_reindex_freshness_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $bleedProject = if ($params.bleed_project) { [string]$params.bleed_project } else { "BookSeller" }
                $query = if ($params.query) { [string]$params.query } else { "current reindex freshness rules" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "reindex-freshness-smoke",
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
                    $summary = if ($parsed) {
                        "Reindex freshness smoke failed; duplicateCount=$($parsed.duplicates.duplicateCount); trace=$($parsed.semanticTraceId)"
                    } else {
                        "memory_reindex_freshness_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Reindex freshness smoke returned Passed=false"
                } else {
                    $summary = "Reindex freshness passed; current finalRank=$($parsed.finalRank.newVersionFinalRank); stale rawRank=$($parsed.rawRank.oldVersionRawRank); trace=$($parsed.semanticTraceId)"
                }

                if ($status -eq "SUCCESS" -and $parsed) {
                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    if ($params.expect_project -and [string]$parsed.project -ne [string]$params.expect_project) {
                        $validationFailures.Add("Expected project '$($params.expect_project)', actual '$($parsed.project)'.") | Out-Null
                    }
                    if ($params.expect_current_beats_stale -and -not [bool]$parsed.staleDemotion.currentBeatsStale) {
                        $validationFailures.Add("Expected current version to beat stale version after reindex.") | Out-Null
                    }
                    if ($params.expect_stale_visible -and -not [bool]$parsed.staleDemotion.oldVersionVisible) {
                        $validationFailures.Add("Expected stale version to remain visible as demoted evidence.") | Out-Null
                    }
                    if ($params.expect_no_duplicates -and [int]$parsed.duplicates.duplicateCount -ne 0) {
                        $validationFailures.Add("Expected no duplicate active chunks or artefact source records.") | Out-Null
                    }
                    if ($params.expect_no_duplicates -and [int]$parsed.duplicates.duplicateIndexedCandidates -ne 0) {
                        $validationFailures.Add("Expected no duplicate indexed Weaviate candidates.") | Out-Null
                    }
                    if ($params.expect_wrong_project_rejected -and -not [bool]$parsed.wrongProjectRejection.wrongProjectRejectedFromFinal) {
                        $validationFailures.Add("Expected wrong-project candidate to be rejected from final results.") | Out-Null
                    }
                    if ($params.expect_exact_title_promoted -and -not [bool]$parsed.exactTitlePromotion.promotedAcceptedCurrentVersion) {
                        $validationFailures.Add("Expected exact accepted title query to promote current document.") | Out-Null
                    }
                    if ($params.expect_semantic_trace_id -and [string]::IsNullOrWhiteSpace([string]$parsed.semanticTraceId)) {
                        $validationFailures.Add("Expected semantic trace id.") | Out-Null
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
                }
            }

            "ticket_source_link_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "IronDev" }
                $expectedProject = if ($params.expect_project) { [string]$params.expect_project } else { $project }
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

                if ($status -eq "SUCCESS" -and $parsed) {
                    $validationFailures = [System.Collections.Generic.List[string]]::new()

                    if ($expectedProject -and [string]$parsed.projectName -ne $expectedProject) {
                        $validationFailures.Add("Expected project '$expectedProject', actual '$($parsed.projectName)'.") | Out-Null
                    }

                    if ($params.expect_ticket_has_source_document_version_id -and -not $parsed.ticketSourceDocumentVersionId) {
                        $validationFailures.Add("Expected ticketSourceDocumentVersionId to be present.") | Out-Null
                    }

                    if ($params.expect_source_document_version_resolves -and [string]$parsed.linkResolutionStatus -ne "resolved_exact_project_document_version") {
                        $validationFailures.Add("Expected linkResolutionStatus resolved, actual '$($parsed.linkResolutionStatus)'.") | Out-Null
                    }

                    if ($params.expect_orphan_missing_source_is_reported_as_failure -and -not [bool]$parsed.orphanReportedAsFailure) {
                        $validationFailures.Add("Expected orphan ticket to be reported as failure.") | Out-Null
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
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

                if ($status -eq "SUCCESS" -and $parsed) {
                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    if ($params.expect_project -and [string]$parsed.projectName -ne [string]$params.expect_project) {
                        $validationFailures.Add("Expected project '$($params.expect_project)', actual '$($parsed.projectName)'.") | Out-Null
                    }
                    if ($params.expect_ticket_included -and -not [bool]$parsed.builderContext.ticketIncluded) {
                        $validationFailures.Add("Expected builder context to include ticket.") | Out-Null
                    }
                    if ($params.expect_source_document_included -and -not [bool]$parsed.builderContext.sourceDocumentIncluded) {
                        $validationFailures.Add("Expected builder context to include source document.") | Out-Null
                    }
                    if ($params.expect_source_version_included -and -not [bool]$parsed.builderContext.sourceDocumentVersionIncluded) {
                        $validationFailures.Add("Expected builder context to include source document version.") | Out-Null
                    }
                    if ($params.expect_source_markdown_included -and -not [bool]$parsed.builderContext.sourceMarkdownIncluded) {
                        $validationFailures.Add("Expected builder context to include source markdown excerpt.") | Out-Null
                    }
                    if ($params.expect_wrong_project_excluded -and -not [bool]$parsed.builderContext.wrongProjectMemoryExcluded) {
                        $validationFailures.Add("Expected wrong-project source memory to be excluded.") | Out-Null
                    }
                    if ($params.expect_missing_source_fails -and -not [bool]$parsed.negativeChecks.orphanTicketFailsCleanly) {
                        $validationFailures.Add("Expected missing source document version to fail cleanly.") | Out-Null
                    }
                    if ($params.expect_boundary_contains -and [string]$parsed.boundary -notlike "*$($params.expect_boundary_contains)*") {
                        $validationFailures.Add("Expected boundary to contain '$($params.expect_boundary_contains)', actual '$($parsed.boundary)'.") | Out-Null
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
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
                if ($params.use_requested_project) {
                    $arguments += @("--use-requested-project")
                }
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

                if ($status -eq "SUCCESS" -and $parsed) {
                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    $expectedProject = if ($params.expect_project) { [string]$params.expect_project } else { $null }

                    if ($expectedProject -and [string]$parsed.projectName -ne $expectedProject) {
                        $validationFailures.Add("Expected project '$expectedProject', actual '$($parsed.projectName)'.") | Out-Null
                    }

                    if ($params.expect_no_file_writes -and (-not [bool]$parsed.safety.fileUnchangedAfterPreview -or -not [bool]$parsed.safety.fileUnchangedAfterApplyAttempt -or -not [bool]$parsed.safety.fileUnchangedAfterDirectPatchAttempt)) {
                        $validationFailures.Add("Expected no file writes during preview/apply-block checks.") | Out-Null
                    }

                    if ($params.expect_approval_blocked -and -not [bool]$parsed.safety.approvalGateBlockedApply) {
                        $validationFailures.Add("Expected approval gate to block apply.") | Out-Null
                    }

                    if ($params.expect_source_context_included -and -not [bool]$parsed.safety.sourceContextIncluded) {
                        $validationFailures.Add("Expected source context to be included.") | Out-Null
                    }

                    foreach ($term in @($params.expect_context_not_contains)) {
                        if ($term -and [string]$parsed.evidence.contextSummary -like "*$term*") {
                            $validationFailures.Add("Expected context summary not to contain '$term'.") | Out-Null
                        }
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
                }
            }

            "disposable_workspace_apply_smoke" {
                $project = if ($params.project) { [string]$params.project } else { "BookSeller" }
                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "builder", "disposable-workspace-apply-smoke",
                    "--project", $project,
                    "--dogfood-run-id", $RunId
                )
                if ($params.workspace_root) {
                    $arguments += @("--workspace-root", [string]$params.workspace_root)
                }
                if ($params.proposal_path) {
                    $arguments += @("--proposal", (Resolve-TargetPath $params.proposal_path))
                }

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code

                try {
                    $parsed = $capture.output | ConvertFrom-Json
                } catch {
                    $parsed = $null
                }

                $expectFailure = [bool]$params.expect_failure
                if ($expectFailure -and $parsed -and -not [bool]$parsed.passed) {
                    $summary = "Disposable workspace apply failed closed as expected; project=$($parsed.project); workspace=$($parsed.workspace.workspacePath)"
                } elseif ($exitCode -ne 0) {
                    $status = "FAILED"
                    $summary = if ($parsed) {
                        "Disposable workspace apply smoke failed; project=$($parsed.project); passed=$($parsed.passed); workspace=$($parsed.workspace.workspacePath)"
                    } else {
                        "disposable_workspace_apply_smoke exited with code $exitCode"
                    }
                } elseif ($parsed -and -not [bool]$parsed.passed) {
                    $status = "FAILED"
                    $summary = "Disposable workspace apply smoke returned Passed=false"
                } else {
                    $summary = "Disposable workspace apply passed; project=$($parsed.project); changedFiles=$(@($parsed.apply.changedFiles).Count); recommendation=$($parsed.comparison.recommendation)"
                }

                if ($status -eq "SUCCESS" -and $parsed) {
                    $validationFailures = [System.Collections.Generic.List[string]]::new()
                    $expectedProject = if ($params.expect_project) { [string]$params.expect_project } else { $null }

                    if ($expectedProject -and [string]$parsed.project -ne $expectedProject) {
                        $validationFailures.Add("Expected project '$expectedProject', actual '$($parsed.project)'.") | Out-Null
                    }
                    if ($params.expect_workspace_outside_repo -and -not [bool]$parsed.workspace.isOutsideRealRepo) {
                        $validationFailures.Add("Expected disposable workspace outside the real repo.") | Out-Null
                    }
                    if ($params.expect_real_repo_unchanged -and -not [bool]$parsed.workspace.realRepoUnchanged) {
                        $validationFailures.Add("Expected real repository fixture source to remain unchanged.") | Out-Null
                    }
                    if ($params.expect_patch_applied -and -not [bool]$parsed.apply.patchApplied) {
                        $validationFailures.Add("Expected patch to be applied inside disposable workspace.") | Out-Null
                    }
                    if ($params.expect_patch_not_applied -and [bool]$parsed.apply.patchApplied) {
                        $validationFailures.Add("Expected patch not to be applied.") | Out-Null
                    }
                    if ($params.expect_apply_inside_workspace_only -and -not [bool]$parsed.apply.appliedInsideDisposableWorkspaceOnly) {
                        $validationFailures.Add("Expected apply to be restricted to disposable workspace.") | Out-Null
                    }
                    if ($params.expect_build_success -and [int]$parsed.build.exitCode -ne 0) {
                        $validationFailures.Add("Expected disposable workspace build to succeed.") | Out-Null
                    }
                    if ($params.expect_test_success -and [int]$parsed.test.exitCode -ne 0) {
                        $validationFailures.Add("Expected disposable workspace tests to succeed.") | Out-Null
                    }
                    if ($params.expect_scope_match -and -not [bool]$parsed.comparison.scopeMatch) {
                        $validationFailures.Add("Expected changed files to match proposal scope.") | Out-Null
                    }
                    if ($params.expect_no_unsafe_changes -and [bool]$parsed.comparison.unsafeChangesFound) {
                        $validationFailures.Add("Expected no unsafe changes.") | Out-Null
                    }
                    if ($params.expect_failure_package_path -and -not (Test-Path ([string]$parsed.failurePackage.resultPath))) {
                        $validationFailures.Add("Expected disposable apply result/failure package path to exist.") | Out-Null
                    }
                    if ($params.expect_human_gate_no_real_repo_write -and -not [bool]$parsed.approvalGate.approvalDoesNotMeanRealRepoWrite) {
                        $validationFailures.Add("Expected human approval gate to keep real repo writes blocked.") | Out-Null
                    }
                    if ($params.expect_patch_proposal_id -and [string]$parsed.proposal.patchProposalId -ne [string]$params.expect_patch_proposal_id) {
                        $validationFailures.Add("Expected patch proposal id '$($params.expect_patch_proposal_id)', actual '$($parsed.proposal.patchProposalId)'.") | Out-Null
                    }
                    if ($params.expect_proposal_source_file -and ([string]$parsed.proposal.proposalSourcePath -eq "built-in" -or -not (Test-Path ([string]$parsed.proposal.proposalSourcePath)))) {
                        $validationFailures.Add("Expected proposal source path to be a real file.") | Out-Null
                    }

                    foreach ($source in @($params.expect_included_sources)) {
                        if ($source -and @($parsed.contextBundle.includedSources | Where-Object { [string]$_.source -eq [string]$source }).Count -eq 0) {
                            $validationFailures.Add("Expected weighted context to include source '$source'.") | Out-Null
                        }
                    }
                    foreach ($source in @($params.expect_rejected_sources)) {
                        if ($source -and @($parsed.contextBundle.rejectedSources | Where-Object { [string]$_.source -eq [string]$source -and [bool]$_.rejected }).Count -eq 0) {
                            $validationFailures.Add("Expected weighted context to reject source '$source'.") | Out-Null
                        }
                    }
                    foreach ($failureText in @($params.expect_apply_failure_contains)) {
                        if ($failureText -and @($parsed.apply.failures | Where-Object { ([string]$_).IndexOf([string]$failureText, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -eq 0) {
                            $validationFailures.Add("Expected apply failure containing '$failureText'.") | Out-Null
                        }
                    }
                    foreach ($failureText in @($params.expect_safety_failure_contains)) {
                        if ($failureText -and @($parsed.workspace.safety.failClosedReasons | Where-Object { ([string]$_).IndexOf([string]$failureText, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -eq 0) {
                            $validationFailures.Add("Expected workspace safety failure containing '$failureText'.") | Out-Null
                        }
                    }

                    if ($validationFailures.Count -gt 0) {
                        $status = "FAILED"
                        $summary = $validationFailures -join " "
                    }
                }
            }

            "bookseller_supervised_campaign" {
                $campaignRunId = if ([string]::IsNullOrWhiteSpace([string]$params.campaign_run_id)) {
                    "$RunId-campaign"
                } else {
                    [string]$params.campaign_run_id
                }

                $arguments = @(
                    "-NoProfile", "-ExecutionPolicy", "Bypass",
                    "-File", (Join-Path $PSScriptRoot "Invoke-BookSellerSupervisedCampaign.ps1"),
                    "-RunId", $campaignRunId,
                    "-Json"
                )

                $commandText = "powershell " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "powershell" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if (-not [string]::IsNullOrWhiteSpace($capture.output)) {
                    $parsed = $capture.output | ConvertFrom-Json
                }

                $validationFailures = [System.Collections.Generic.List[string]]::new()
                if ($exitCode -ne 0) {
                    $validationFailures.Add("bookseller_supervised_campaign exited with code $exitCode") | Out-Null
                }
                if ($params.expect_runs -and [int]$parsed.runs -ne [int]$params.expect_runs) {
                    $validationFailures.Add("Expected runs=$($params.expect_runs), actual $($parsed.runs).") | Out-Null
                }
                if ($null -ne $params.expect_real_repo_mutations -and [int]$parsed.realRepoMutations -ne [int]$params.expect_real_repo_mutations) {
                    $validationFailures.Add("Expected realRepoMutations=$($params.expect_real_repo_mutations), actual $($parsed.realRepoMutations).") | Out-Null
                }
                if ($params.expect_blocked_unsafe_min -and [int]$parsed.blockedUnsafe -lt [int]$params.expect_blocked_unsafe_min) {
                    $validationFailures.Add("Expected blockedUnsafe >= $($params.expect_blocked_unsafe_min), actual $($parsed.blockedUnsafe).") | Out-Null
                }
                if ($null -ne $params.expect_parallel_allowed) {
                    $expectedParallel = Convert-ToBool $params.expect_parallel_allowed $false
                    if ([bool]$parsed.parallelExecutionAllowed -ne $expectedParallel) {
                        $validationFailures.Add("Expected parallelExecutionAllowed=$expectedParallel, actual $($parsed.parallelExecutionAllowed).") | Out-Null
                    }
                }
                if ($params.expect_failure_min -and [int]$parsed.failed -lt [int]$params.expect_failure_min) {
                    $validationFailures.Add("Expected failed >= $($params.expect_failure_min), actual $($parsed.failed).") | Out-Null
                }

                if ($validationFailures.Count -gt 0) {
                    $status = "FAILED"
                    $summary = $validationFailures -join " "
                } else {
                    $summary = "BookSeller supervised campaign complete; passed=$($parsed.passed); failed=$($parsed.failed); blockedUnsafe=$($parsed.blockedUnsafe); realRepoMutations=$($parsed.realRepoMutations)"
                }
            }

            "memory_triage" {
                $message = [string]$params.message
                if ([string]::IsNullOrWhiteSpace($message)) {
                    $message = [string]$params.prompt
                }
                $project = if ([string]::IsNullOrWhiteSpace([string]$params.project)) { "IronDev" } else { [string]$params.project }

                $arguments = @(
                    "run", "--no-build", "--project", $runnerProject, "--",
                    "memory", "triage", $message,
                    "--project", $project,
                    "--run-id", $RunId,
                    "--json"
                )

                $commandText = "dotnet " + ($arguments -join " ")
                $capture = Invoke-CommandCapture -FilePath "dotnet" -Arguments $arguments -StepLogPath $stepLogPath
                $exitCode = $capture.exit_code
                if (-not [string]::IsNullOrWhiteSpace($capture.output)) {
                    $parsed = $capture.output | ConvertFrom-Json
                }

                $validationFailures = [System.Collections.Generic.List[string]]::new()
                if ($exitCode -ne 0) {
                    $validationFailures.Add("memory_triage exited with code $exitCode") | Out-Null
                }
                if ($null -ne $params.expect_should_save) {
                    $expectedShouldSave = Convert-ToBool $params.expect_should_save $false
                    if ([bool]$parsed.shouldSave -ne $expectedShouldSave) {
                        $validationFailures.Add("Expected shouldSave=$expectedShouldSave, actual $($parsed.shouldSave).") | Out-Null
                    }
                }
                if ($params.expect_scope -and [string]$parsed.scope -ne [string]$params.expect_scope) {
                    $validationFailures.Add("Expected scope '$($params.expect_scope)', actual '$($parsed.scope)'.") | Out-Null
                }
                if ($params.expect_project -and [string]$parsed.project -ne [string]$params.expect_project) {
                    $validationFailures.Add("Expected project '$($params.expect_project)', actual '$($parsed.project)'.") | Out-Null
                }
                if ($params.expect_memory_type -and [string]$parsed.memoryType -ne [string]$params.expect_memory_type) {
                    $validationFailures.Add("Expected memoryType '$($params.expect_memory_type)', actual '$($parsed.memoryType)'.") | Out-Null
                }
                if ($params.expect_authority -and [string]$parsed.authority -ne [string]$params.expect_authority) {
                    $validationFailures.Add("Expected authority '$($params.expect_authority)', actual '$($parsed.authority)'.") | Out-Null
                }
                foreach ($artifact in @($params.expect_recommended_artifacts)) {
                    if ($artifact -and @($parsed.recommendedArtifacts | Where-Object { [string]$_ -eq [string]$artifact }).Count -eq 0) {
                        $validationFailures.Add("Expected recommended artifact '$artifact'.") | Out-Null
                    }
                }
                foreach ($signal in @($params.expect_evidence)) {
                    if ($signal -and @($parsed.evidence | Where-Object { [string]$_ -eq [string]$signal }).Count -eq 0) {
                        $validationFailures.Add("Expected evidence signal '$signal'.") | Out-Null
                    }
                }

                if ($validationFailures.Count -gt 0) {
                    $status = "FAILED"
                    $summary = $validationFailures -join " "
                } else {
                    $summary = "memory_triage classified scope=$($parsed.scope); type=$($parsed.memoryType); shouldSave=$($parsed.shouldSave); project=$($parsed.project)"
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

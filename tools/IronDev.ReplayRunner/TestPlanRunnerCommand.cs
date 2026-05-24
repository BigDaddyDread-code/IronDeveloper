using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class TestPlanRunnerCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options, string commandName)
    {
        var planPath = ReadOption(args, "--plan");
        if (string.IsNullOrWhiteSpace(planPath))
        {
            Console.Error.WriteLine($"Usage: IronDev.ReplayRunner {commandName} --plan <path> [--run-id id] [--json]");
            return 2;
        }

        var repoRoot = FindRepositoryRoot();
        var fullPlanPath = Path.GetFullPath(Path.IsPathRooted(planPath) ? planPath : Path.Combine(repoRoot, planPath));
        if (!File.Exists(fullPlanPath))
        {
            Console.Error.WriteLine($"Test plan not found: {fullPlanPath}");
            return 2;
        }

        var runner = new TestPlanRunner(repoRoot, options);
        var report = await runner.RunAsync(fullPlanPath, ReadOption(args, "--run-id"), commandName);
        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return string.Equals(report.Status, "passed", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

public sealed class TestPlanRunner
{
    private static readonly HashSet<string> NativeActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "agent_tester_run_plan",
        "buildagent_trace_smoke",
        "builder_repair_loop_smoke",
        "csharp_dogfood_runner_smoke",
        "cli_command_surface_cleanup",
        "memory_search",
        "run_report_viewer_service_smoke",
        "self_improvement_campaign_157",
        "live_governed_agent_execution_158",
        "live_critic_planner_agents_159",
        "live_retriever_sentinel_agents_160",
        "live_remaining_governed_agents_161",
        "governed_tool_loop_162_167",
        "loop_gated_disposable_build_168",
        "promotion_package_169",
        "isolated_promotion_apply_170",
        "controlled_write_policy_173",
        "controlled_write_approval_174",
        "controlled_worktree_dry_run_175",
        "adversarial_memory_agents_183",
        "minesweeper_disposable_build_184",
        "tiny_rest_api_disposable_build_185"
    };

    private readonly string _repoRoot;
    private readonly JsonSerializerOptions _options;
    private readonly string _runnerProject;

    public TestPlanRunner(string repoRoot, JsonSerializerOptions options)
    {
        _repoRoot = repoRoot;
        _options = options;
        _runnerProject = Path.Combine(_repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");
    }

    public async Task<TestPlanRunReport> RunAsync(string planPath, string? requestedRunId, string commandName)
    {
        var started = DateTimeOffset.UtcNow;
        using var planDocument = JsonDocument.Parse(await File.ReadAllTextAsync(planPath));
        var plan = planDocument.RootElement.Clone();
        var runId = string.IsNullOrWhiteSpace(requestedRunId)
            ? ReadString(plan, "test_run_id") ?? $"test-agent-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
            : requestedRunId;

        var goalId = ReadString(plan, "goal_id") ?? "ad-hoc";
        var runRoot = Path.Combine(_repoRoot, "tools", "dogfood", "runs", runId);
        var logRoot = Path.Combine(runRoot, "logs");
        var evidenceRoot = Path.Combine(runRoot, "evidence");
        Directory.CreateDirectory(logRoot);
        Directory.CreateDirectory(evidenceRoot);

        var traceGroupId = Guid.NewGuid().ToString("N");
        var steps = ReadSteps(plan);
        var unsupported = steps.Select(step => step.Action)
            .Where(action => !NativeActions.Contains(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unsupported.Length > 0)
            return await RunLegacyCompatibilityAsync(planPath, runId, commandName, started, unsupported, runRoot, logRoot, traceGroupId);

        var stepReports = new List<TestPlanStepReport>();
        var commandsRun = new List<string>();
        var earlyStop = ReadBool(plan, "early_stop_on_failure", true);

        foreach (var step in steps)
        {
            var stepStarted = DateTimeOffset.UtcNow;
            var logPath = Path.Combine(logRoot, $"step-{step.StepNumber:000}-{step.Action}.log");
            var status = "SUCCESS";
            var summary = "";
            var command = "";
            var exitCode = 0;
            object? parsed = null;

            try
            {
                var result = step.Action.ToLowerInvariant() switch
                {
                    "agent_tester_run_plan" => await RunAgentTesterPlanAsync(step, runId, logPath),
                    "buildagent_trace_smoke" => await RunTraceSmokeAsync(step, runId, logPath),
                    "builder_repair_loop_smoke" => await RunBuilderRepairLoopAsync(step, runId, logPath),
                    "csharp_dogfood_runner_smoke" => await RunCSharpDogfoodRunnerSmokeAsync(runId, logPath),
                    "cli_command_surface_cleanup" => await RunCliCommandSurfaceCleanupAsync(runId, logPath),
                    "memory_search" => await RunMemorySearchAsync(step, runId, logPath),
                    "run_report_viewer_service_smoke" => await RunReportViewerServiceSmokeAsync(runId, logPath),
                    "self_improvement_campaign_157" => await RunSelfImprovementCampaign157Async(runId, logPath),
                    "live_governed_agent_execution_158" => await RunLiveGovernedAgentExecution158Async(runId, logPath),
                    "live_critic_planner_agents_159" => await RunLiveCriticPlannerAgents159Async(runId, logPath),
                    "live_retriever_sentinel_agents_160" => await RunLiveRetrieverSentinelAgents160Async(runId, logPath),
                    "live_remaining_governed_agents_161" => await RunLiveRemainingGovernedAgents161Async(runId, logPath),
                    "governed_tool_loop_162_167" => await RunGovernedToolLoop162167Async(runId, logPath),
                    "loop_gated_disposable_build_168" => await RunLoopGatedDisposableBuild168Async(runId, logPath),
                    "promotion_package_169" => await RunPromotionPackage169Async(runId, logPath),
                    "isolated_promotion_apply_170" => await RunIsolatedPromotionApply170Async(runId, logPath),
                    "controlled_write_policy_173" => await RunControlledWritePolicy173Async(runId, logPath),
                    "controlled_write_approval_174" => await RunControlledWriteApproval174Async(runId, logPath),
                    "controlled_worktree_dry_run_175" => await RunControlledWorktreeDryRun175Async(runId, logPath),
                    "adversarial_memory_agents_183" => await RunAdversarialMemoryAgents183Async(runId, logPath),
                    "minesweeper_disposable_build_184" => await RunMinesweeperDisposableBuild184Async(runId, logPath),
                    "tiny_rest_api_disposable_build_185" => await RunTinyRestApiDisposableBuild185Async(runId, logPath),
                    _ => throw new InvalidOperationException($"Unsupported native action: {step.Action}")
                };

                status = result.Success ? "SUCCESS" : "FAILED";
                summary = result.Summary;
                command = result.Command;
                exitCode = result.ExitCode;
                parsed = result.Parsed;
                if (!string.IsNullOrWhiteSpace(command))
                    commandsRun.Add(command);
            }
            catch (Exception ex)
            {
                status = "FAILED";
                summary = ex.Message;
                await File.WriteAllTextAsync(logPath, ex.ToString(), Encoding.UTF8);
            }

            stepReports.Add(new TestPlanStepReport
            {
                Step = step.StepNumber,
                Action = step.Action,
                Status = status,
                Summary = summary,
                Command = command,
                ExitCode = exitCode,
                LogPath = logPath,
                DurationSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - stepStarted).TotalSeconds),
                Trace = new
                {
                    trace_group_id = traceGroupId,
                    dogfood_run_id = runId,
                    agent_role = "TestAgent",
                    provider = "LocalCli",
                    model = "deterministic-csharp",
                    command
                },
                Parsed = parsed
            });

            if (earlyStop && status == "FAILED")
                break;
        }

        var report = BuildReport(
            commandName,
            runId,
            goalId,
            planPath,
            plan,
            steps,
            stepReports,
            commandsRun,
            started,
            traceGroupId,
            compatibilityMode: false,
            compatibilityWarnings: []);

        await WriteStandardOutputsAsync(report, runRoot);
        return report;
    }

    private async Task<TestPlanRunReport> RunLegacyCompatibilityAsync(
        string planPath,
        string runId,
        string commandName,
        DateTimeOffset started,
        IReadOnlyList<string> unsupported,
        string runRoot,
        string logRoot,
        string traceGroupId)
    {
        var legacyPath = Path.Combine(_repoRoot, "tools", "dogfood", "Invoke-TestAgentPlan.Legacy.ps1");
        var logPath = Path.Combine(logRoot, "legacy-compatibility.log");
        var args = new[]
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            legacyPath,
            "-PlanPath",
            planPath,
            "-RunId",
            runId,
            "-Json"
        };

        var run = await RunProcessAsync("powershell", args, logPath, new Dictionary<string, string>
        {
            ["IRONDEV_SKIP_LEGACY_RUNNER_BUILD"] = "1"
        });
        TestPlanRunReport? legacyReport = null;
        try
        {
            legacyReport = JsonSerializer.Deserialize<TestPlanRunReport>(run.Output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            legacyReport = TryReadLegacyReportShape(run.Output, commandName, planPath, runId, started, logRoot, traceGroupId);
        }

        var warning = $"Compatibility fallback used for unsupported action(s): {string.Join(", ", unsupported)}.";
        if (legacyReport is not null)
        {
            var enriched = legacyReport with
            {
                Command = commandName,
                TraceId = legacyReport.TraceId ?? traceGroupId,
                ReproCommand = $"test run-plan --plan {QuoteIfNeeded(planPath)} --run-id {QuoteIfNeeded(runId)} --json",
                CompatibilityMode = true,
                CompatibilityWarnings = [.. legacyReport.CompatibilityWarnings, warning],
                Boundary = "C# ReplayRunner owns test run-plan execution. Legacy PowerShell was used only as an explicit compatibility fallback for unported actions."
            };
            await WriteStandardOutputsAsync(enriched, runRoot);
            return enriched;
        }

        var step = new TestPlanStepReport
        {
            Step = 1,
            Action = "legacy_compatibility",
            Status = "FAILED",
            Summary = "Legacy compatibility fallback did not return a parseable report.",
            Command = "powershell " + string.Join(" ", args.Select(QuoteIfNeeded)),
            ExitCode = run.ExitCode,
            LogPath = logPath,
            DurationSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - started).TotalSeconds),
            Trace = new { trace_group_id = traceGroupId, dogfood_run_id = runId },
            Parsed = null
        };
        var report = new TestPlanRunReport
        {
            Command = commandName,
            TraceId = traceGroupId,
            ReproCommand = $"test run-plan --plan {QuoteIfNeeded(planPath)} --run-id {QuoteIfNeeded(runId)} --json",
            TestRunId = runId,
            GoalId = "legacy-compatibility",
            PlanPath = planPath,
            Status = "failed",
            OverallResult = "FAILED",
            Summary = "Legacy compatibility fallback failed.",
            CommandsRun = [step.Command],
            Expected = new Dictionary<string, object>(),
            Actual = new TestPlanActual(0, 1, 0),
            Evidence = [new TestPlanEvidence(step.Action, "step-1", logPath, step.Summary)],
            Trace = new { trace_group_id = traceGroupId, dogfood_run_id = runId, agent_role = "TestAgent", provider = "LocalCli", model = "deterministic-csharp" },
            KeyMetrics = new TestPlanMetrics(1, 1, 0, 1),
            CriticalIssues = [step.Summary],
            FullLogLocation = logRoot,
            TimeTakenSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - started).TotalSeconds),
            NextSuggestions = ["Port the failing action to the C# TestPlanRunner or fix the legacy compatibility report."],
            Steps = [step],
            ReportSchemaValid = true,
            ReportSchemaValidation = new { valid = true, missing = Array.Empty<string>(), schema_path = Path.Combine(_repoRoot, "tools", "dogfood", "TestAgentReport.schema.json") },
            CompatibilityMode = true,
            CompatibilityWarnings = [warning],
            Boundary = "C# ReplayRunner owns test run-plan execution. Legacy PowerShell fallback failed."
        };
        await WriteStandardOutputsAsync(report, runRoot);
        return report;
    }

    private TestPlanRunReport? TryReadLegacyReportShape(string output, string commandName, string planPath, string runId, DateTimeOffset started, string logRoot, string traceGroupId)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement.Clone();
            var criticalIssues = ReadStringArray(root, "critical_issues");
            var commandsRun = ReadStringArray(root, "commands_run");
            var actual = ReadElement(root, "actual");
            var metrics = ReadElement(root, "key_metrics");
            var steps = ReadArray(root, "steps")
                .Select((step, index) => new TestPlanStepReport
                {
                    Step = ReadIntProperty(step, "step") == 0 ? index + 1 : ReadIntProperty(step, "step"),
                    Action = ReadProperty(step, "action") ?? "legacy",
                    Status = ReadProperty(step, "status") ?? "UNKNOWN",
                    Summary = ReadProperty(step, "summary") ?? "",
                    Command = ReadProperty(step, "command") ?? "",
                    ExitCode = ReadIntProperty(step, "exit_code"),
                    LogPath = ReadProperty(step, "log_path") ?? logRoot,
                    DurationSeconds = ReadIntProperty(step, "duration_seconds"),
                    Trace = ReadElement(step, "trace"),
                    Parsed = ReadElement(step, "parsed")
                })
                .ToArray();

            return new TestPlanRunReport
            {
                Command = commandName,
                TraceId = traceGroupId,
                ReproCommand = $"{commandName} --plan {QuoteIfNeeded(planPath)} --run-id {QuoteIfNeeded(runId)} --json",
                TestRunId = ReadProperty(root, "test_run_id") ?? runId,
                GoalId = ReadProperty(root, "goal_id") ?? "legacy-compatibility",
                PlanPath = ReadProperty(root, "plan_path") ?? planPath,
                Status = ReadProperty(root, "status") ?? "failed",
                OverallResult = ReadProperty(root, "overall_result") ?? "FAILED",
                Summary = ReadProperty(root, "summary") ?? "Legacy compatibility report parsed through C# compatibility adapter.",
                CommandsRun = commandsRun,
                Expected = ReadElement(root, "expected"),
                Actual = new TestPlanActual(
                    ReadIntProperty(actual, "steps_passed"),
                    ReadIntProperty(actual, "steps_failed"),
                    ReadIntProperty(actual, "steps_skipped")),
                Evidence = ReadArray(root, "evidence")
                    .Select((item, index) => new TestPlanEvidence(
                        ReadProperty(item, "type") ?? "LegacyEvidence",
                        ReadProperty(item, "id") ?? $"legacy-{index + 1}",
                        ReadProperty(item, "path") ?? "",
                        ReadProperty(item, "problem") ?? ""))
                    .ToArray(),
                Trace = ReadElement(root, "trace"),
                KeyMetrics = new TestPlanMetrics(
                    ReadIntProperty(metrics, "cli_commands_run"),
                    ReadIntProperty(metrics, "failures_found"),
                    ReadIntProperty(metrics, "steps_passed"),
                    ReadIntProperty(metrics, "steps_skipped")),
                CriticalIssues = criticalIssues,
                FullLogLocation = ReadProperty(root, "full_log_location") ?? logRoot,
                TimeTakenSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - started).TotalSeconds),
                NextSuggestions = ReadStringArray(root, "next_suggestions"),
                Steps = steps,
                ReportSchemaValid = true,
                ReportSchemaValidation = ReadElement(root, "report_schema_validation"),
                CompatibilityMode = true,
                CompatibilityWarnings = [],
                Boundary = "C# ReplayRunner owns test run-plan execution. Legacy PowerShell was used only as an explicit compatibility fallback for unported actions."
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<NativeActionResult> RunAgentTesterPlanAsync(TestPlanStep step, string runId, string logPath)
    {
        var nestedPlan = RequireParam(step, "plan_path");
        var testerRunId = $"{runId}-agent-step-{step.StepNumber}";
        var args = new[]
        {
            "run", "--no-build", "--project", _runnerProject, "--",
            "agent", "tester", "run-plan",
            "--plan", nestedPlan,
            "--run-id", testerRunId,
            "--json"
        };

        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"agent_tester_run_plan exited with code {run.ExitCode}");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected TesterAgent status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var report = ReadElement(parsed, "report");
        ValidateString(step, failures, report, "goal_id", "expect_nested_goal_id", "nested report goal_id");
        ValidateString(step, failures, report, "status", "expect_nested_status", "nested report status");
        ValidateString(step, failures, report, "overall_result", "expect_nested_overall_result", "nested report overall_result");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"TesterAgent ran plan; summary={ReadProperty(parsed, "summary")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunTraceSmokeAsync(TestPlanStep step, string runId, string logPath)
    {
        var project = ReadParam(step, "project") ?? "Solitaire";
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "trace", "build-smoke", "--project", project, "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = ValidateBuildTraceLike(step, parsed, finalBuildKey: "expect_build_success", finalTestKey: "expect_test_success");
        if (run.ExitCode != 0)
            failures.Add($"trace build-smoke exited with code {run.ExitCode}.");

        return new NativeActionResult(failures.Count == 0, failures.Count == 0 ? $"BuildAgent trace smoke passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures), "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)), run.ExitCode, parsed);
    }

    private async Task<NativeActionResult> RunBuilderRepairLoopAsync(TestPlanStep step, string runId, string logPath)
    {
        var project = ReadParam(step, "project") ?? "Solitaire";
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "build", "disposable", "repair", "--project", project, "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = ValidateBuildTraceLike(step, parsed, finalBuildKey: "expect_final_build_success", finalTestKey: "expect_final_test_success");
        if (run.ExitCode != 0)
            failures.Add($"build disposable repair exited with code {run.ExitCode}.");

        return new NativeActionResult(failures.Count == 0, failures.Count == 0 ? $"Builder repair loop passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures), "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)), run.ExitCode, parsed);
    }

    private async Task<NativeActionResult> RunCliCommandSurfaceCleanupAsync(string runId, string logPath)
    {
        var failures = new List<string>();
        var commands = new[]
        {
            new[] { "run", "--no-build", "--project", _runnerProject, "--", "inventory", "validate", "--run-id", $"{runId}-inventory", "--json" },
            new[] { "run", "--no-build", "--project", _runnerProject, "--", "trace", "build-smoke", "--project", "Solitaire", "--run-id", $"{runId}-trace", "--json" },
            new[] { "run", "--no-build", "--project", _runnerProject, "--", "build", "disposable", "repair", "--project", "Solitaire", "--run-id", $"{runId}-repair", "--json" },
            new[] { "run", "--no-build", "--project", _runnerProject, "--", "test", "run-plan", "--plan", ".\\tools\\dogfood\\test-agent-plans\\irondev-buildagent-traceable-disposable-build-spine-140.json", "--run-id", $"{runId}-test-alias", "--json" }
        };

        var outputs = new List<object>();
        var combined = new StringBuilder();
        foreach (var args in commands)
        {
            var commandLog = logPath + "-" + args.SkipWhile(arg => arg != "--").Skip(1).Take(3).Aggregate("", (current, next) => current + "-" + next);
            var run = await RunProcessAsync("dotnet", args, commandLog);
            combined.AppendLine("dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)));
            combined.AppendLine(run.Output);
            if (run.ExitCode != 0)
                failures.Add($"Command exited with code {run.ExitCode}: dotnet {string.Join(" ", args)}");
            outputs.Add(ParseObject(run.Output));
        }

        await File.WriteAllTextAsync(logPath, combined.ToString(), Encoding.UTF8);
        return new NativeActionResult(failures.Count == 0, failures.Count == 0 ? "CLI command surface cleanup aliases and inventory validation passed." : string.Join(" ", failures), "dotnet <multiple cli alias checks>", failures.Count == 0 ? 0 : 1, outputs);
    }

    private async Task<NativeActionResult> RunCSharpDogfoodRunnerSmokeAsync(string runId, string logPath)
    {
        var plan = ".\\tools\\dogfood\\test-agent-plans\\irondev-buildagent-traceable-disposable-build-spine-140.json";
        var commands = new (string Label, string FileName, string[] Args, bool ExpectAgentEnvelope)[]
        {
            ("test run-plan", "dotnet", ["run", "--no-build", "--project", _runnerProject, "--", "test", "run-plan", "--plan", plan, "--run-id", $"{runId}-test", "--json"], false),
            ("dogfood run-plan", "dotnet", ["run", "--no-build", "--project", _runnerProject, "--", "dogfood", "run-plan", "--plan", plan, "--run-id", $"{runId}-dogfood", "--json"], false),
            ("agent tester run-plan", "dotnet", ["run", "--no-build", "--project", _runnerProject, "--", "agent", "tester", "run-plan", "--plan", plan, "--run-id", $"{runId}-agent", "--json"], true),
            ("Invoke-TestAgentPlan wrapper", "powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\\tools\\dogfood\\Invoke-TestAgentPlan.ps1", "-PlanPath", plan, "-RunId", $"{runId}-wrapper", "-Json"], false)
        };

        var failures = new List<string>();
        var parsedOutputs = new List<object>();
        var combined = new StringBuilder();

        foreach (var command in commands)
        {
            var commandLog = $"{logPath}-{command.Label.Replace(' ', '-')}.log";
            var run = await RunProcessAsync(command.FileName, command.Args, commandLog);
            combined.AppendLine($"{command.FileName} {string.Join(" ", command.Args.Select(QuoteIfNeeded))}");
            combined.AppendLine(run.Output);
            if (run.ExitCode != 0)
                failures.Add($"{command.Label} exited with code {run.ExitCode}.");

            var parsed = ParseObject(run.Output);
            parsedOutputs.Add(new { command = command.Label, output = parsed });

            if (command.ExpectAgentEnvelope)
            {
                if (!StringPropertyEquals(parsed, "status", "Succeeded"))
                    failures.Add($"{command.Label} expected status Succeeded, actual {ReadProperty(parsed, "status")}.");
                var nested = ReadElement(parsed, "report");
                if (!StringPropertyEquals(nested, "overall_result", "SUCCESS"))
                    failures.Add($"{command.Label} expected nested overall_result SUCCESS, actual {ReadProperty(nested, "overall_result")}.");
                if (ReadBoolProperty(nested, "compatibility_mode"))
                    failures.Add($"{command.Label} should not use compatibility mode for the 140 plan.");
            }
            else
            {
                if (!StringPropertyEquals(parsed, "status", "passed"))
                    failures.Add($"{command.Label} expected status passed, actual {ReadProperty(parsed, "status")}.");
                if (!StringPropertyEquals(parsed, "overall_result", "SUCCESS"))
                    failures.Add($"{command.Label} expected overall_result SUCCESS, actual {ReadProperty(parsed, "overall_result")}.");
                if (ReadBoolProperty(parsed, "compatibility_mode"))
                    failures.Add($"{command.Label} should not use compatibility mode for the 140 plan.");
            }
        }

        await File.WriteAllTextAsync(logPath, combined.ToString(), Encoding.UTF8);
        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? "C# test run-plan, dogfood run-plan, agent tester run-plan, and PowerShell compatibility wrapper all route through the C# TestPlanRunner." : string.Join(" ", failures),
            "dotnet/powershell <csharp dogfood runner smoke>",
            failures.Count == 0 ? 0 : 1,
            parsedOutputs);
    }

    private async Task<NativeActionResult> RunMemorySearchAsync(TestPlanStep step, string runId, string logPath)
    {
        var project = ReadParam(step, "project") ?? "IronDev";
        var query = RequireParam(step, "query");
        var take = ReadParam(step, "take") ?? "5";
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "memory", "search", query, "--project", project, "--take", take, "--json", "--dogfood-run-id", runId };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"memory_search exited with code {run.ExitCode}");
        if (!StringPropertyEquals(ReadElement(parsed, "project"), "name", project))
            failures.Add($"Expected project {project}.");

        var matches = ReadArray(parsed, "matches");
        if (matches.Count == 0)
            failures.Add("memory_search returned no matches.");
        else
        {
            var top = matches[0];
            var expectedTitle = ReadParam(step, "expect_top_title_contains");
            if (!string.IsNullOrWhiteSpace(expectedTitle) &&
                !((ReadProperty(top, "documentTitle") ?? "").Contains(expectedTitle, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Expected top title to contain {expectedTitle}, actual {ReadProperty(top, "documentTitle")}.");
            }
        }

        if (ReadBoolParam(step, "expect_semantic_trace_id", false) && string.IsNullOrWhiteSpace(ReadProperty(parsed, "semanticTraceId")))
            failures.Add("Expected semantic trace id.");

        return new NativeActionResult(failures.Count == 0, failures.Count == 0 ? $"memory_search top match '{(matches.Count > 0 ? ReadProperty(matches[0], "documentTitle") : "")}'" : string.Join(" ", failures), "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)), run.ExitCode, parsed);
    }

    private async Task<NativeActionResult> RunReportViewerServiceSmokeAsync(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "run-report", "viewer-smoke", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"run-report viewer-smoke exited with code {run.ExitCode}.");
        if (!ReadBoolProperty(parsed, "Passed"))
            failures.Add("Run report viewer service smoke returned Passed=false.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Run report viewer service smoke passed; trace={ReadProperty(parsed, "TraceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunSelfImprovementCampaign157Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "self-improvement-157", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign self-improvement-157 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var providerSupport = ReadElement(parsed, "providerSupport");
        if (!ReadBoolProperty(providerSupport, "openAi"))
            failures.Add("Expected OpenAI provider support.");
        if (!ReadBoolProperty(providerSupport, "localOpenAi"))
            failures.Add("Expected LocalOpenAI provider support.");
        if (!ReadBoolProperty(providerSupport, "ollama"))
            failures.Add("Expected Ollama provider support.");

        var governance = ReadElement(parsed, "governance");
        if (!ReadBoolProperty(governance, "conscienceRequired"))
            failures.Add("Expected ConscienceAgent to remain required.");
        if (!ReadBoolProperty(governance, "thoughtLedgerRequired"))
            failures.Add("Expected ThoughtLedger to remain required.");
        if (!ReadBoolProperty(governance, "realRepoWritesBlocked"))
            failures.Add("Expected real repo writes to remain blocked.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Self-improvement campaign 157 smoke passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunLiveGovernedAgentExecution158Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "live-governed-agent-158", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign live-governed-agent-158 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var liveProviderHandling = ReadElement(parsed, "liveProviderHandling");
        if (!ReadBoolProperty(liveProviderHandling, "attempted"))
            failures.Add("Expected live provider handling to attempt a model call.");
        if (string.IsNullOrWhiteSpace(ReadProperty(liveProviderHandling, "invocationMode")))
            failures.Add("Expected invocation mode evidence.");

        var governance = ReadElement(parsed, "governance");
        if (!ReadBoolProperty(governance, "realRepoWritesBlocked"))
            failures.Add("Expected real repo writes to remain blocked.");
        if (!ReadBoolProperty(governance, "memoryMutationBlocked"))
            failures.Add("Expected memory mutation to remain blocked.");
        if (!ReadBoolProperty(governance, "ticketCreationBlocked"))
            failures.Add("Expected ticket creation to remain blocked.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Live governed agent execution smoke passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunLiveCriticPlannerAgents159Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "live-critic-planner-159", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign live-critic-planner-159 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var liveProviderHandling = ReadElement(parsed, "liveProviderHandling");
        if (!ReadBoolProperty(liveProviderHandling, "criticAttempted"))
            failures.Add("Expected CriticAgent live provider handling to attempt a model call.");
        if (!ReadBoolProperty(liveProviderHandling, "plannerAttempted"))
            failures.Add("Expected PlannerAgent live provider handling to attempt a model call.");
        if (string.IsNullOrWhiteSpace(ReadProperty(liveProviderHandling, "criticInvocationMode")))
            failures.Add("Expected CriticAgent invocation mode evidence.");
        if (string.IsNullOrWhiteSpace(ReadProperty(liveProviderHandling, "plannerInvocationMode")))
            failures.Add("Expected PlannerAgent invocation mode evidence.");

        var governance = ReadElement(parsed, "governance");
        if (!ReadBoolProperty(governance, "realRepoWritesBlocked"))
            failures.Add("Expected real repo writes to remain blocked.");
        if (!ReadBoolProperty(governance, "memoryMutationBlocked"))
            failures.Add("Expected memory mutation to remain blocked.");
        if (!ReadBoolProperty(governance, "ticketCreationBlocked"))
            failures.Add("Expected ticket creation to remain blocked.");
        if (!ReadBoolProperty(governance, "patchApplyBlocked"))
            failures.Add("Expected patch apply to remain blocked.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Live Critic/Planner agent smoke passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunLiveRetrieverSentinelAgents160Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "live-retriever-sentinel-160", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign live-retriever-sentinel-160 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var liveProviderHandling = ReadElement(parsed, "liveProviderHandling");
        if (!ReadBoolProperty(liveProviderHandling, "retrieverAttempted"))
            failures.Add("Expected RetrieverAgent live provider handling to attempt a model call.");
        if (!ReadBoolProperty(liveProviderHandling, "sentinelAttempted"))
            failures.Add("Expected SentinelAgent live provider handling to attempt a model call.");
        if (string.IsNullOrWhiteSpace(ReadProperty(liveProviderHandling, "retrieverInvocationMode")))
            failures.Add("Expected RetrieverAgent invocation mode evidence.");
        if (string.IsNullOrWhiteSpace(ReadProperty(liveProviderHandling, "sentinelInvocationMode")))
            failures.Add("Expected SentinelAgent invocation mode evidence.");

        var governance = ReadElement(parsed, "governance");
        if (!ReadBoolProperty(governance, "realRepoWritesBlocked"))
            failures.Add("Expected real repo writes to remain blocked.");
        if (!ReadBoolProperty(governance, "memoryMutationBlocked"))
            failures.Add("Expected memory mutation to remain blocked.");
        if (!ReadBoolProperty(governance, "ticketCreationBlocked"))
            failures.Add("Expected ticket creation to remain blocked.");
        if (!ReadBoolProperty(governance, "rankingOverrideBlocked"))
            failures.Add("Expected ranking override to remain blocked.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Live Retriever/Sentinel agent smoke passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunLiveRemainingGovernedAgents161Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "live-remaining-agents-161", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign live-remaining-agents-161 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var liveProviderHandling = ReadElement(parsed, "liveProviderHandling");
        if (!ReadBoolProperty(liveProviderHandling, "researchAttempted"))
            failures.Add("Expected ResearchAgent live provider handling to attempt a model call.");
        if (!ReadBoolProperty(liveProviderHandling, "qualityAttempted"))
            failures.Add("Expected QualityAgent live provider handling to attempt a model call.");
        if (!ReadBoolProperty(liveProviderHandling, "supervisorAttempted"))
            failures.Add("Expected SupervisorAgent live provider handling to attempt a model call.");

        var governance = ReadElement(parsed, "governance");
        if (!ReadBoolProperty(governance, "realRepoWritesBlocked"))
            failures.Add("Expected real repo writes to remain blocked.");
        if (!ReadBoolProperty(governance, "memoryMutationBlocked"))
            failures.Add("Expected memory mutation to remain blocked.");
        if (!ReadBoolProperty(governance, "ticketCreationBlocked"))
            failures.Add("Expected ticket creation to remain blocked.");
        if (!ReadBoolProperty(governance, "deterministicGatesRemainAuthoritative"))
            failures.Add("Expected deterministic quality/governance gates to remain authoritative.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Live remaining governed agents smoke passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunGovernedToolLoop162167Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "governed-tool-loop-162-167", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign governed-tool-loop-162-167 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var data = ReadElement(parsed, "data");
        if (!ReadBoolProperty(data, "toolContractCreated"))
            failures.Add("Expected tool contract to be created.");
        var capabilities = ReadStringArray(data, "registryCapabilities");
        foreach (var capability in new[] { "memory.search", "code.search", "trace.read", "failure.latest", "test.run-plan", "quality.run-gate", "project.build" })
        {
            if (!capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
                failures.Add($"Expected registry capability {capability}.");
        }

        if (!ReadBoolProperty(data, "traceVisualizationAvailable"))
            failures.Add("Expected trace visualization/report markdown to be available.");
        if (!StringPropertyEquals(data, "evidenceValidationStatus", "Passed"))
            failures.Add($"Expected evidence validation Passed, actual {ReadProperty(data, "evidenceValidationStatus")}.");
        if (!StringPropertyEquals(data, "humanEscalationDecision", "HumanReviewRequired"))
            failures.Add($"Expected human escalation gate HumanReviewRequired, actual {ReadProperty(data, "humanEscalationDecision")}.");
        if (!ReadBoolProperty(data, "dotnetProfilePresent") || !ReadBoolProperty(data, "nodeProfilePresent") || !ReadBoolProperty(data, "pythonProfilePresent"))
            failures.Add("Expected dotnet, node, and python runtime profiles.");
        if (!ReadBoolProperty(data, "realRepoWritesBlocked") ||
            !ReadBoolProperty(data, "memoryMutationBlocked") ||
            !ReadBoolProperty(data, "ticketCreationBlocked") ||
            !ReadBoolProperty(data, "patchApplyBlocked") ||
            !ReadBoolProperty(data, "rawCommandExecutionBlockedForAgents"))
        {
            failures.Add("Expected hard governance boundaries to remain blocked.");
        }

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Governed tool loop 162-167 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunLoopGatedDisposableBuild168Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "loop-gated-disposable-build-168", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign loop-gated-disposable-build-168 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");
        if (!StringPropertyEquals(parsed, "project", "Solitaire"))
            failures.Add($"Expected project Solitaire, actual {ReadProperty(parsed, "project")}.");
        if (!StringPropertyEquals(parsed, "goal", "I want build solitaire"))
            failures.Add($"Expected messy goal to be preserved, actual {ReadProperty(parsed, "goal")}.");
        if (!StringPropertyEquals(parsed, "recommendation", "PromoteLater"))
            failures.Add($"Expected recommendation PromoteLater, actual {ReadProperty(parsed, "recommendation")}.");

        var data = ReadElement(parsed, "data");
        if (!ReadBoolProperty(data, "productSpikeCandidate"))
            failures.Add("Expected productSpikeCandidate=true.");
        if (!ReadBoolProperty(data, "generatedSolitaireAppContained"))
            failures.Add("Expected generatedSolitaireAppContained=true.");
        if (!ReadBoolProperty(data, "docsAreRunScoped"))
            failures.Add("Expected docsAreRunScoped=true.");
        if (!ReadBoolProperty(data, "memoryMutationBlocked") || !ReadBoolProperty(data, "ticketAcceptanceBlocked"))
            failures.Add("Expected memory mutation and ticket acceptance to remain blocked.");

        var mutation = ReadElement(parsed, "mutation");
        if (ReadIntProperty(mutation, "realRepoMutationCount") != 0)
            failures.Add($"Expected real repo mutation count zero, actual {ReadIntProperty(mutation, "realRepoMutationCount")}.");
        if (ReadIntProperty(mutation, "disposableFilesChanged") < 17)
            failures.Add($"Expected disposableFilesChanged >= 17, actual {ReadIntProperty(mutation, "disposableFilesChanged")}.");

        var evidence = ReadArray(parsed, "evidence");
        foreach (var expected in new[] { "RunScopedDocument", "PlannerCriticTrace", "BuilderTrace", "BuilderReport", "QualityCommandLog" })
        {
            if (!evidence.Any(item => string.Equals(ReadProperty(item, "type"), expected, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"Expected evidence type {expected}.");
        }

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Loop-gated disposable build 168 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunMinesweeperDisposableBuild184Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "build", "disposable", "run", "--project", "Minesweeper", "--goal", "i want build minesweeper", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"build disposable run for Minesweeper exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected status Succeeded, actual {ReadProperty(parsed, "status")}.");
        if (!StringPropertyEquals(parsed, "project", "Minesweeper"))
            failures.Add($"Expected project Minesweeper, actual {ReadProperty(parsed, "project")}.");
        if (!StringPropertyEquals(parsed, "goal", "i want build minesweeper"))
            failures.Add($"Expected messy goal to be preserved, actual {ReadProperty(parsed, "goal")}.");
        if (!StringPropertyEquals(parsed, "recommendation", "PromoteLater"))
            failures.Add($"Expected recommendation PromoteLater, actual {ReadProperty(parsed, "recommendation")}.");

        var data = ReadElement(parsed, "data");
        if (!ReadBoolProperty(data, "productSpikeCandidate"))
            failures.Add("Expected productSpikeCandidate=true.");
        if (!StringPropertyEquals(data, "normalizedProductName", "Minesweeper"))
            failures.Add($"Expected normalizedProductName Minesweeper, actual {ReadProperty(data, "normalizedProductName")}.");
        if (!ReadBoolProperty(data, "generatedAppContained"))
            failures.Add("Expected generatedAppContained=true.");
        if (ReadBoolProperty(data, "generatedSolitaireAppContained"))
            failures.Add("Expected generatedSolitaireAppContained=false for Minesweeper.");
        if (!ReadBoolProperty(data, "docsAreRunScoped"))
            failures.Add("Expected docsAreRunScoped=true.");
        if (!ReadBoolProperty(data, "memoryMutationBlocked") || !ReadBoolProperty(data, "ticketAcceptanceBlocked"))
            failures.Add("Expected memory mutation and ticket acceptance to remain blocked.");

        var docs = ReadElement(data, "docsCreated");
        var docIds = string.Join(
            '\n',
            ReadProperty(docs, "intakeId") ?? "",
            ReadProperty(docs, "buildBriefId") ?? "",
            ReadProperty(docs, "ticketId") ?? "");
        foreach (var expected in new[] { "MINESWEEPER_PRODUCT_SPIKE_INTAKE_168", "MINESWEEPER_DISPOSABLE_BUILD_BRIEF_168", "MINESWEEPER_DISPOSABLE_BUILD_TICKET_168" })
        {
            if (!docIds.Contains(expected, StringComparison.OrdinalIgnoreCase))
                failures.Add($"Expected run-scoped doc id {expected}.");
        }

        var mutation = ReadElement(parsed, "mutation");
        if (ReadIntProperty(mutation, "realRepoMutationCount") != 0)
            failures.Add($"Expected real repo mutation count zero, actual {ReadIntProperty(mutation, "realRepoMutationCount")}.");
        if (ReadIntProperty(mutation, "disposableFilesChanged") < 13)
            failures.Add($"Expected disposableFilesChanged >= 13, actual {ReadIntProperty(mutation, "disposableFilesChanged")}.");
        if (!(ReadProperty(mutation, "disposableWorkspacePath") ?? "").Contains("Minesweeper", StringComparison.OrdinalIgnoreCase))
            failures.Add("Expected disposable workspace path to contain Minesweeper.");

        var evidence = ReadArray(parsed, "evidence");
        foreach (var expected in new[] { "RunScopedDocument", "PlannerCriticTrace", "BuilderTrace", "BuilderReport", "QualityCommandLog" })
        {
            if (!evidence.Any(item => string.Equals(ReadProperty(item, "type"), expected, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"Expected evidence type {expected}.");
        }

        var evidenceText = string.Join('\n', evidence.Select(item => ReadProperty(item, "path")));
        if (evidenceText.Contains("SOLITAIRE_", StringComparison.OrdinalIgnoreCase))
            failures.Add("Minesweeper run-scoped evidence should not use SOLITAIRE_ document ids.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Minesweeper disposable build 184 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunTinyRestApiDisposableBuild185Async(string runId, string logPath)
    {
        const string project = "TinyRestApi";
        const string goal = "i want build tiny rest api";
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "build", "disposable", "run", "--project", project, "--goal", goal, "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"build disposable run for TinyRestApi exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected status Succeeded, actual {ReadProperty(parsed, "status")}.");
        if (!StringPropertyEquals(parsed, "project", project))
            failures.Add($"Expected project TinyRestApi, actual {ReadProperty(parsed, "project")}.");
        if (!StringPropertyEquals(parsed, "goal", goal))
            failures.Add($"Expected messy goal to be preserved, actual {ReadProperty(parsed, "goal")}.");
        if (!StringPropertyEquals(parsed, "recommendation", "PromoteLater"))
            failures.Add($"Expected recommendation PromoteLater, actual {ReadProperty(parsed, "recommendation")}.");

        var data = ReadElement(parsed, "data");
        if (!ReadBoolProperty(data, "productSpikeCandidate"))
            failures.Add("Expected productSpikeCandidate=true.");
        if (!StringPropertyEquals(data, "normalizedProductName", project))
            failures.Add($"Expected normalizedProductName TinyRestApi, actual {ReadProperty(data, "normalizedProductName")}.");
        if (!ReadBoolProperty(data, "generatedAppContained"))
            failures.Add("Expected generatedAppContained=true.");
        if (ReadBoolProperty(data, "generatedSolitaireAppContained"))
            failures.Add("Expected generatedSolitaireAppContained=false for TinyRestApi.");
        if (!ReadBoolProperty(data, "docsAreRunScoped"))
            failures.Add("Expected docsAreRunScoped=true.");
        if (!ReadBoolProperty(data, "memoryMutationBlocked") || !ReadBoolProperty(data, "ticketAcceptanceBlocked"))
            failures.Add("Expected memory mutation and ticket acceptance to remain blocked.");

        var docs = ReadElement(data, "docsCreated");
        var docIds = string.Join(
            '\n',
            ReadProperty(docs, "intakeId") ?? "",
            ReadProperty(docs, "buildBriefId") ?? "",
            ReadProperty(docs, "ticketId") ?? "");
        foreach (var expected in new[] { "TINYRESTAPI_PRODUCT_SPIKE_INTAKE_168", "TINYRESTAPI_DISPOSABLE_BUILD_BRIEF_168", "TINYRESTAPI_DISPOSABLE_BUILD_TICKET_168" })
        {
            if (!docIds.Contains(expected, StringComparison.OrdinalIgnoreCase))
                failures.Add($"Expected run-scoped doc id {expected}.");
        }

        var mutation = ReadElement(parsed, "mutation");
        if (ReadIntProperty(mutation, "realRepoMutationCount") != 0)
            failures.Add($"Expected real repo mutation count zero, actual {ReadIntProperty(mutation, "realRepoMutationCount")}.");
        if (ReadIntProperty(mutation, "disposableFilesChanged") < 7)
            failures.Add($"Expected disposableFilesChanged >= 7, actual {ReadIntProperty(mutation, "disposableFilesChanged")}.");
        if (!(ReadProperty(mutation, "disposableWorkspacePath") ?? "").Contains(project, StringComparison.OrdinalIgnoreCase))
            failures.Add("Expected disposable workspace path to contain TinyRestApi.");

        var evidence = ReadArray(parsed, "evidence");
        foreach (var expected in new[] { "RunScopedDocument", "PlannerCriticTrace", "BuilderTrace", "BuilderReport", "QualityCommandLog" })
        {
            if (!evidence.Any(item => string.Equals(ReadProperty(item, "type"), expected, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"Expected evidence type {expected}.");
        }

        var evidenceText = string.Join('\n', evidence.Select(item => ReadProperty(item, "path")));
        if (evidenceText.Contains("SOLITAIRE_", StringComparison.OrdinalIgnoreCase) ||
            evidenceText.Contains("MINESWEEPER_", StringComparison.OrdinalIgnoreCase))
            failures.Add("TinyRestApi run-scoped evidence should not use game-specific document ids.");

        var parsedText = run.Output;
        if (parsedText.Contains("TinyRestApi.Wpf", StringComparison.OrdinalIgnoreCase) ||
            parsedText.Contains("Solitaire.Wpf", StringComparison.OrdinalIgnoreCase) ||
            parsedText.Contains("Minesweeper.Wpf", StringComparison.OrdinalIgnoreCase))
            failures.Add("TinyRestApi disposable build output should not reference game/WPF project output.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Tiny REST API disposable build 185 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunPromotionPackage169Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "promotion-package-169", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign promotion-package-169 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");
        if (!StringPropertyEquals(parsed, "project", "Solitaire"))
            failures.Add($"Expected project Solitaire, actual {ReadProperty(parsed, "project")}.");

        var proposedChange = ReadElement(parsed, "proposedChange");
        if (string.IsNullOrWhiteSpace(ReadProperty(proposedChange, "proposedChangeId")))
            failures.Add("Expected ProposedChange id.");
        if (!StringPropertyEquals(proposedChange, "currentStage", "PromotionPackageCreated"))
            failures.Add($"Expected ProposedChange currentStage PromotionPackageCreated, actual {ReadProperty(proposedChange, "currentStage")}.");
        if (!StringPropertyEquals(proposedChange, "approvalState", "NeedsHumanReview"))
            failures.Add($"Expected ProposedChange approvalState NeedsHumanReview, actual {ReadProperty(proposedChange, "approvalState")}.");

        var package = ReadElement(parsed, "promotionPackage");
        var runtime = ReadElement(package, "runtimeProfile");
        if (!StringPropertyEquals(runtime, "runtimeProfileId", "csharp-dotnet"))
            failures.Add($"Expected csharp-dotnet runtime, actual {ReadProperty(runtime, "runtimeProfileId")}.");
        if (!StringPropertyEquals(runtime, "availability", "Executable"))
            failures.Add($"Expected executable runtime profile, actual {ReadProperty(runtime, "availability")}.");
        if (ReadArray(package, "filesToPromote").Count < 10)
            failures.Add("Expected at least 10 promotable files.");
        if (ReadArray(package, "filesBlocked").Count == 0)
            failures.Add("Expected build output files to be blocked from promotion.");
        if (!StringPropertyEquals(package, "approvalState", "NeedsHumanReview"))
            failures.Add($"Expected package approvalState NeedsHumanReview, actual {ReadProperty(package, "approvalState")}.");

        var summary = ReadElement(package, "evidenceSummary");
        if (ReadIntProperty(summary, "realRepoMutationCount") != 0)
            failures.Add($"Expected real repo mutation count zero, actual {ReadIntProperty(summary, "realRepoMutationCount")}.");
        if (ReadIntProperty(summary, "promotableFileCount") < 10)
            failures.Add("Expected evidence summary promotable file count >= 10.");

        var profiles = ReadArray(parsed, "runtimeProfiles");
        foreach (var expected in new[] { "csharp-dotnet", "java-maven", "typescript-node", "python-pytest" })
        {
            if (!profiles.Any(profile => string.Equals(ReadProperty(profile, "runtimeProfileId"), expected, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"Expected runtime profile {expected}.");
        }

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Promotion package 169 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunIsolatedPromotionApply170Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "isolated-promotion-apply-170", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign isolated-promotion-apply-170 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected campaign status Succeeded, actual {ReadProperty(parsed, "status")}.");
        if (!StringPropertyEquals(parsed, "project", "Solitaire"))
            failures.Add($"Expected project Solitaire, actual {ReadProperty(parsed, "project")}.");
        if (!StringPropertyEquals(parsed, "recommendation", "ReviewIsolatedCandidate"))
            failures.Add($"Expected recommendation ReviewIsolatedCandidate, actual {ReadProperty(parsed, "recommendation")}.");
        if (!StringPropertyEquals(parsed, "approvalState", "NeedsHumanReview"))
            failures.Add($"Expected approvalState NeedsHumanReview, actual {ReadProperty(parsed, "approvalState")}.");

        var workspace = ReadProperty(parsed, "isolatedWorkspacePath") ?? "";
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            failures.Add("Expected isolated workspace path to exist.");
        else if (IsUnderDirectory(workspace, _repoRoot))
            failures.Add("Expected isolated workspace outside active repo root.");
        if (string.IsNullOrWhiteSpace(ReadProperty(parsed, "isolatedBranchName")))
            failures.Add("Expected isolated branch name.");

        var runtime = ReadElement(parsed, "runtimeProfile");
        if (!StringPropertyEquals(runtime, "runtimeProfileId", "csharp-dotnet"))
            failures.Add($"Expected csharp-dotnet runtime, actual {ReadProperty(runtime, "runtimeProfileId")}.");
        if (!StringPropertyEquals(runtime, "availability", "Executable"))
            failures.Add($"Expected executable runtime profile, actual {ReadProperty(runtime, "availability")}.");

        if (ReadArray(parsed, "appliedFiles").Count < 10)
            failures.Add("Expected at least 10 applied files.");
        if (ReadArray(parsed, "rejectedBlockedFiles").Count == 0)
            failures.Add("Expected blocked generated files to remain rejected.");

        var build = ReadElement(parsed, "build");
        if (!StringPropertyEquals(build, "status", "Succeeded") || ReadIntProperty(build, "exitCode") != 0)
            failures.Add($"Expected build success, actual {ReadProperty(build, "status")} exit {ReadIntProperty(build, "exitCode")}.");
        var test = ReadElement(parsed, "test");
        if (!StringPropertyEquals(test, "status", "Succeeded") || ReadIntProperty(test, "exitCode") != 0)
            failures.Add($"Expected test success, actual {ReadProperty(test, "status")} exit {ReadIntProperty(test, "exitCode")}.");

        var mutation = ReadElement(parsed, "mutation");
        if (ReadIntProperty(mutation, "activeRepoMutationCount") != 0)
            failures.Add($"Expected active repo mutation count zero, actual {ReadIntProperty(mutation, "activeRepoMutationCount")}.");
        if (ReadArray(mutation, "forbiddenPathsTouched").Count != 0)
            failures.Add("Expected no forbidden paths touched in isolated workspace.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Isolated promotion apply 170 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunControlledWritePolicy173Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "controlled-write-policy-173", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign controlled-write-policy-173 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected policy status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var effective = ReadElement(parsed, "effectiveSettings");
        if (!ReadBoolProperty(effective, "writePathEnabled"))
            failures.Add("Expected write path enabled by explicit handoff key setting.");
        if (!ReadArray(effective, "permittedPromotionModes").Any(mode => string.Equals(mode.ToString(), "ControlledWorktreeDryRun", StringComparison.OrdinalIgnoreCase)))
            failures.Add("Expected ControlledWorktreeDryRun permitted by effective policy.");
        if (ReadArray(parsed, "hardInvariants").Count < 6)
            failures.Add("Expected hard invariants.");
        if (ReadArray(parsed, "hardInvariants").Any(invariant => ReadBoolProperty(invariant, "configurable")))
            failures.Add("Hard invariants must not be configurable.");
        if (ReadArray(parsed, "ignoredInvariantOverrides").Count < 2)
            failures.Add("Expected attempted invariant overrides to be ignored.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Controlled write policy 173 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunControlledWriteApproval174Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "controlled-write-approval-174", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign controlled-write-approval-174 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "approvalState", "ApprovedForControlledWorktreeDryRun"))
            failures.Add($"Expected approval state ApprovedForControlledWorktreeDryRun, actual {ReadProperty(parsed, "approvalState")}.");
        if (!StringPropertyEquals(parsed, "approvalScope", "ControlledWorktreeDryRunOnly"))
            failures.Add($"Expected approval scope ControlledWorktreeDryRunOnly, actual {ReadProperty(parsed, "approvalScope")}.");
        if (!ReadBoolProperty(parsed, "validForControlledWorktreeDryRun"))
            failures.Add("Expected approval valid for controlled worktree dry-run.");
        if (ReadBoolProperty(parsed, "validForRealRepoWrite"))
            failures.Add("Approval must not be valid for real repo write.");
        if (!ReadArray(parsed, "allowedActions").Any(action => string.Equals(action.ToString(), "ControlledWorktreeDryRun", StringComparison.OrdinalIgnoreCase)))
            failures.Add("Expected ControlledWorktreeDryRun allowed action.");
        foreach (var blocked in new[] { "WriteMain", "AutoMerge", "SelfApprove" })
        {
            if (!ReadArray(parsed, "blockedActions").Any(action => string.Equals(action.ToString(), blocked, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"Expected blocked action {blocked}.");
        }

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Controlled write approval 174 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunControlledWorktreeDryRun175Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "controlled-worktree-dry-run-175", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign controlled-worktree-dry-run-175 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected dry-run status Succeeded, actual {ReadProperty(parsed, "status")}.");
        if (!ReadBoolProperty(parsed, "targetPathExplicit"))
            failures.Add("Expected explicit target path.");
        if (!ReadBoolProperty(parsed, "targetOutsideActiveRepo"))
            failures.Add("Expected target outside active repo.");
        if (!ReadBoolProperty(parsed, "targetBranchIsNotMain"))
            failures.Add("Expected target branch not main/master.");
        if (!ReadBoolProperty(parsed, "wouldCreateWorktree"))
            failures.Add("Expected dry-run to say it would create a worktree.");
        if (ReadBoolProperty(parsed, "wouldCopyFiles"))
            failures.Add("Dry-run must not copy files.");
        if (ReadArray(parsed, "filesThatWouldApply").Count < 10)
            failures.Add("Expected at least 10 files that would apply.");
        if (ReadArray(parsed, "blockedFilesRejected").Count == 0)
            failures.Add("Expected blocked files to remain rejected.");

        var approval = ReadElement(parsed, "approvalRecord");
        if (!ReadBoolProperty(approval, "validForControlledWorktreeDryRun") || ReadBoolProperty(approval, "validForRealRepoWrite"))
            failures.Add("Expected scoped approval valid only for dry-run.");
        var mutation = ReadElement(parsed, "mutation");
        if (ReadIntProperty(mutation, "activeRepoMutationCount") != 0)
            failures.Add($"Expected active repo mutation count zero, actual {ReadIntProperty(mutation, "activeRepoMutationCount")}.");
        if (ReadIntProperty(mutation, "isolatedFilesChanged") != 0)
            failures.Add("Expected dry-run isolated files changed count zero.");
        var target = ReadProperty(parsed, "targetWorktreePath") ?? "";
        if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
            failures.Add("Dry-run target worktree should not be created.");

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Controlled worktree dry-run 175 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private async Task<NativeActionResult> RunAdversarialMemoryAgents183Async(string runId, string logPath)
    {
        var args = new[] { "run", "--no-build", "--project", _runnerProject, "--", "campaign", "adversarial-memory-agents-183", "--run-id", runId, "--json" };
        var run = await RunProcessAsync("dotnet", args, logPath);
        var parsed = ParseObject(run.Output);
        var failures = new List<string>();
        if (run.ExitCode != 0)
            failures.Add($"campaign adversarial-memory-agents-183 exited with code {run.ExitCode}.");
        if (!StringPropertyEquals(parsed, "status", "Succeeded"))
            failures.Add($"Expected 183 status Succeeded, actual {ReadProperty(parsed, "status")}.");

        var doubt = ReadElement(parsed, "doubtReview");
        var criticisms = ReadArray(doubt, "criticisms");
        if (criticisms.Count == 0)
            failures.Add("Expected DoubtAgent criticisms.");
        if (!ReadBoolProperty(doubt, "rebuttalRequired"))
            failures.Add("Expected DoubtAgent to require rebuttal.");
        if (!ReadBoolProperty(doubt, "killjoyEscalation"))
            failures.Add("Expected DoubtAgent to escalate to Killjoy.");

        var killjoy = ReadElement(parsed, "killjoyReview");
        if (!ReadBoolProperty(killjoy, "allHighCriticalFindingsAddressed"))
            failures.Add("Expected Killjoy to address high/critical Doubt findings.");

        var memory = ReadElement(parsed, "memoryImprovement");
        var proposals = ReadArray(memory, "proposals");
        if (proposals.Count is < 1 or > 3)
            failures.Add($"Expected one to three memory proposals, actual {proposals.Count}.");
        if (proposals.Any(proposal => string.Equals(ReadProperty(proposal, "memoryAuthorityImpact"), "UpdatesAcceptedMemory", StringComparison.OrdinalIgnoreCase)))
            failures.Add("Memory proposals must not directly update accepted memory.");
        var readiness = ReadElement(memory, "authorityKeyReadiness");
        if (ReadBoolProperty(readiness, "readyForAcceptedMemoryKey"))
            failures.Add("MemoryImprovementAgent must not be ready for accepted-memory keys.");
        if (!StringPropertyEquals(readiness, "currentAuthorityLevel", "Level1ProposalOnly"))
            failures.Add("MemoryImprovementAgent must start at Level1ProposalOnly.");
        var evidenceBundles = ReadArray(memory, "evidenceBundles");
        if (evidenceBundles.Count != proposals.Count)
            failures.Add("Expected every memory proposal to have an evidence bundle.");
        if (evidenceBundles.Any(bundle => ReadArray(bundle, "evidenceRefs").Count == 0))
            failures.Add("Every memory proposal evidence bundle must cite governed evidence.");
        var keyGate = ReadElement(memory, "keyGateReview");
        if (!StringPropertyEquals(keyGate, "decision", "NeedsMoreEvidence"))
            failures.Add("MemoryKeyGate must require more evidence before granting staging-area write keys.");
        if (!StringPropertyEquals(keyGate, "requestedLevelName", "Level2StagingAreaWrite"))
            failures.Add("MemoryKeyGate must evaluate the first key: staging-area write.");
        if (!ReadBoolProperty(parsed, "realRepoMutationBlocked") ||
            !ReadBoolProperty(parsed, "acceptedMemoryMutationBlocked") ||
            !ReadBoolProperty(parsed, "ticketCreationBlocked") ||
            !ReadBoolProperty(parsed, "patchApplyBlocked"))
        {
            failures.Add("Expected all hard mutation boundaries to remain blocked.");
        }

        return new NativeActionResult(
            failures.Count == 0,
            failures.Count == 0 ? $"Adversarial/memory agents 183 passed; trace={ReadProperty(parsed, "traceId")}" : string.Join(" ", failures),
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            run.ExitCode,
            parsed);
    }

    private List<string> ValidateBuildTraceLike(TestPlanStep step, JsonElement parsed, string finalBuildKey, string finalTestKey)
    {
        var failures = new List<string>();
        if (!ReadBoolProperty(parsed, "passed"))
            failures.Add("Command returned Passed=false.");
        if (ReadParam(step, "expect_project") is { } project && !StringPropertyEquals(parsed, "project", project))
            failures.Add($"Expected project {project}, actual {ReadProperty(parsed, "project")}.");
        if (ReadBoolParam(step, "expect_trace_path", false) && !File.Exists(ReadProperty(parsed, "tracePath")))
            failures.Add("Expected trace path to exist.");
        if (ReadBoolParam(step, "expect_report_path", false) && !File.Exists(ReadProperty(parsed, "reportPath")))
            failures.Add("Expected report path to exist.");
        if (ReadBoolParam(step, "expect_markdown_path", false) && !File.Exists(ReadProperty(parsed, "markdownPath")))
            failures.Add("Expected markdown path to exist.");

        var trace = ReadElement(parsed, "trace");
        if (ReadBoolParam(step, "expect_build_failure", false) && !HasStatus(ReadArray(trace, "buildAttempts"), "Failed"))
            failures.Add("Expected failed build attempt.");
        if (ReadBoolParam(step, "expect_test_failure", false) && !HasStatus(ReadArray(trace, "testAttempts"), "Failed"))
            failures.Add("Expected failed test attempt.");
        if (ReadBoolParam(step, finalBuildKey, false) && !HasStatus(ReadArray(trace, "buildAttempts"), "Succeeded"))
            failures.Add("Expected successful build attempt.");
        if (ReadBoolParam(step, finalTestKey, false) && !HasStatus(ReadArray(trace, "testAttempts"), "Succeeded"))
            failures.Add("Expected successful test attempt.");
        if (ReadIntParam(step, "expect_repair_attempts_min") is { } repairs && ReadArray(trace, "repairAttempts").Count < repairs)
            failures.Add($"Expected at least {repairs} repair attempts.");
        if (ReadIntParam(step, "expect_real_repo_mutation_count") is { } repoMutations && ReadIntProperty(trace, "realRepoMutationCount") != repoMutations)
            failures.Add($"Expected realRepoMutationCount={repoMutations}, actual {ReadIntProperty(trace, "realRepoMutationCount")}.");
        if (ReadIntParam(step, "expect_disposable_files_changed_min") is { } changed && ReadIntProperty(trace, "disposableFilesChanged") < changed)
            failures.Add($"Expected disposableFilesChanged >= {changed}, actual {ReadIntProperty(trace, "disposableFilesChanged")}.");
        if (ReadParam(step, "expect_recommendation") is { } recommendation && !StringPropertyEquals(ReadElement(parsed, "report"), "recommendation", recommendation))
            failures.Add($"Expected recommendation {recommendation}.");

        return failures;
    }

    private TestPlanRunReport BuildReport(
        string commandName,
        string runId,
        string goalId,
        string planPath,
        JsonElement plan,
        IReadOnlyList<TestPlanStep> planSteps,
        IReadOnlyList<TestPlanStepReport> stepReports,
        IReadOnlyList<string> commandsRun,
        DateTimeOffset started,
        string traceGroupId,
        bool compatibilityMode,
        IReadOnlyList<string> compatibilityWarnings)
    {
        var passed = stepReports.Count(step => step.Status == "SUCCESS");
        var failed = stepReports.Count(step => step.Status == "FAILED");
        var skipped = Math.Max(0, planSteps.Count - stepReports.Count);
        var overall = failed > 0 ? (passed > 0 ? "PARTIAL_SUCCESS" : "FAILED") : skipped > 0 ? "PARTIAL_SUCCESS" : "SUCCESS";
        var status = overall == "SUCCESS" ? "passed" : overall == "FAILED" ? "failed" : "partial";
        var evidence = stepReports.Select(step => new TestPlanEvidence(step.Action, $"step-{step.Step}", step.LogPath, step.Status == "FAILED" ? step.Summary : "")).ToArray();
        return new TestPlanRunReport
        {
            Command = commandName,
            TraceId = traceGroupId,
            ReproCommand = $"{commandName} --plan {QuoteIfNeeded(planPath)} --run-id {QuoteIfNeeded(runId)} --json",
            TestRunId = runId,
            GoalId = goalId,
            PlanPath = planPath,
            Status = status,
            OverallResult = overall,
            Summary = $"Steps passed: {passed}; failed: {failed}; skipped: {skipped}.",
            CommandsRun = commandsRun,
            Expected = ReadExpected(plan),
            Actual = new TestPlanActual(passed, failed, skipped),
            Evidence = evidence,
            Trace = new { trace_group_id = traceGroupId, dogfood_run_id = runId, agent_role = "TestAgent", provider = "LocalCli", model = "deterministic-csharp" },
            KeyMetrics = new TestPlanMetrics(commandsRun.Count, failed, passed, skipped),
            CriticalIssues = stepReports.Where(step => step.Status == "FAILED").Select(step => step.Summary).ToArray(),
            FullLogLocation = Path.Combine(_repoRoot, "tools", "dogfood", "runs", runId, "logs"),
            TimeTakenSeconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - started).TotalSeconds),
            NextSuggestions = failed > 0 ? ["Generate a Codex failure package and patch the failing route or command."] : ["Promote this test plan into the replay regression pack."],
            Steps = stepReports,
            ReportSchemaValid = true,
            ReportSchemaValidation = new { valid = true, missing = Array.Empty<string>(), schema_path = Path.Combine(_repoRoot, "tools", "dogfood", "TestAgentReport.schema.json") },
            CompatibilityMode = compatibilityMode,
            CompatibilityWarnings = compatibilityWarnings,
            Boundary = compatibilityMode
                ? "C# ReplayRunner owns test run-plan execution. Legacy PowerShell was used only as an explicit compatibility fallback for unported actions."
                : "C# ReplayRunner TestPlanRunner executed this plan. TesterAgent executes and reports only; no repair, memory mutation, or real repo writes are granted."
        };
    }

    private async Task WriteStandardOutputsAsync(TestPlanRunReport report, string runRoot)
    {
        Directory.CreateDirectory(runRoot);
        Directory.CreateDirectory(Path.Combine(runRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(runRoot, "evidence"));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, _options), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "test-agent-report.json"), JsonSerializer.Serialize(report, _options), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "trace.json"), JsonSerializer.Serialize(report.Trace, _options), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdown(report), Encoding.UTF8);
    }

    private static string BuildMarkdown(TestPlanRunReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {report.GoalId}");
        builder.AppendLine();
        builder.AppendLine($"Status: {report.Status}");
        builder.AppendLine($"Run ID: {report.TestRunId}");
        builder.AppendLine($"Summary: {report.Summary}");
        builder.AppendLine($"Compatibility fallback: {report.CompatibilityMode}");
        builder.AppendLine();
        builder.AppendLine("## Steps");
        foreach (var step in report.Steps)
            builder.AppendLine($"- {step.Step}. {step.Action}: {step.Status} - {step.Summary}");
        return builder.ToString();
    }

    private async Task<CommandRunResult> RunProcessAsync(string fileName, IReadOnlyList<string> args, string logPath, IReadOnlyDictionary<string, string>? environment = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        if (environment is not null)
        {
            foreach (var item in environment)
                process.StartInfo.Environment[item.Key] = item.Value;
        }

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllTextAsync(logPath, output, Encoding.UTF8);
        return new CommandRunResult(process.ExitCode, output);
    }

    private IReadOnlyList<TestPlanStep> ReadSteps(JsonElement plan)
    {
        if (!plan.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<TestPlanStep>();
        var index = 0;
        foreach (var step in steps.EnumerateArray())
        {
            index++;
            result.Add(new TestPlanStep(
                ReadIntProperty(step, "step") == 0 ? index : ReadIntProperty(step, "step"),
                ReadString(step, "action") ?? "unknown",
                step.TryGetProperty("params", out var parameters) ? parameters.Clone() : default));
        }

        return result;
    }

    private static Dictionary<string, object> ReadExpected(JsonElement plan)
    {
        if (!plan.TryGetProperty("expected", out var expected) || expected.ValueKind != JsonValueKind.Object)
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, object>>(expected.GetRawText()) ?? [];
    }

    private static bool HasStatus(IReadOnlyList<JsonElement> items, string status) =>
        items.Any(item => StringPropertyEquals(item, "status", status));

    private static void ValidateString(TestPlanStep step, List<string> failures, JsonElement element, string propertyName, string paramName, string label)
    {
        var expected = ReadParam(step, paramName);
        if (!string.IsNullOrWhiteSpace(expected) && !StringPropertyEquals(element, propertyName, expected))
            failures.Add($"Expected {label} {expected}, actual {ReadProperty(element, propertyName)}.");
    }

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadElement(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var child))
            return child.Clone();
        if (element.ValueKind != JsonValueKind.Object)
            return default;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value.Clone();
        }

        return default;
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement element, string propertyName)
    {
        var child = ReadElement(element, propertyName);
        return child.ValueKind == JsonValueKind.Array ? child.EnumerateArray().Select(item => item.Clone()).ToArray() : [];
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        var child = ReadElement(element, propertyName);
        if (child.ValueKind != JsonValueKind.Array)
            return [];

        return child.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string RequireParam(TestPlanStep step, string name) =>
        ReadParam(step, name) ?? throw new InvalidOperationException($"{step.Action} requires params.{name}.");

    private static string? ReadParam(TestPlanStep step, string name) =>
        step.Params.ValueKind == JsonValueKind.Object ? ReadString(step.Params, name) : null;

    private static bool ReadBoolParam(TestPlanStep step, string name, bool defaultValue) =>
        step.Params.ValueKind == JsonValueKind.Object ? ReadBool(step.Params, name, defaultValue) : defaultValue;

    private static int? ReadIntParam(TestPlanStep step, string name)
    {
        if (step.Params.ValueKind != JsonValueKind.Object)
            return null;
        var element = ReadElement(step.Params, name);
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private static string? ReadProperty(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int ReadIntProperty(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    private static bool ReadBoolProperty(JsonElement element, string propertyName)
    {
        var value = ReadElement(element, propertyName);
        return value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }

    private static bool StringPropertyEquals(JsonElement element, string propertyName, string expected) =>
        string.Equals(ReadProperty(element, propertyName), expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), fullDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool defaultValue)
    {
        var value = ReadElement(element, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}

public sealed record TestPlanStep(int StepNumber, string Action, JsonElement Params);

public sealed record NativeActionResult(bool Success, string Summary, string Command, int ExitCode, object? Parsed);

public sealed record CommandRunResult(int ExitCode, string Output);

public sealed record TestPlanActual(
    [property: JsonPropertyName("steps_passed")] int StepsPassed,
    [property: JsonPropertyName("steps_failed")] int StepsFailed,
    [property: JsonPropertyName("steps_skipped")] int StepsSkipped);

public sealed record TestPlanMetrics(
    [property: JsonPropertyName("cli_commands_run")] int CliCommandsRun,
    [property: JsonPropertyName("failures_found")] int FailuresFound,
    [property: JsonPropertyName("steps_passed")] int StepsPassed,
    [property: JsonPropertyName("steps_skipped")] int StepsSkipped)
{
    [JsonPropertyName("build_success")]
    public object? BuildSuccess { get; init; }
    [JsonPropertyName("unit_test_pass_rate")]
    public object? UnitTestPassRate { get; init; }
    [JsonPropertyName("coverage_percent")]
    public object? CoveragePercent { get; init; }
    [JsonPropertyName("api_drive_success_rate")]
    public object? ApiDriveSuccessRate { get; init; }
    [JsonPropertyName("model_calls")]
    public int ModelCalls { get; init; }
    [JsonPropertyName("estimated_cost")]
    public decimal EstimatedCost { get; init; }
    [JsonPropertyName("useful_failures")]
    public int UsefulFailures { get; init; } = FailuresFound;
    [JsonPropertyName("wasted_runs")]
    public int WastedRuns { get; init; }
}

public sealed record TestPlanEvidence(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("problem")] string Problem);

public sealed record TestPlanStepReport
{
    [JsonPropertyName("step")]
    public required int Step { get; init; }
    [JsonPropertyName("action")]
    public required string Action { get; init; }
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
    [JsonPropertyName("command")]
    public required string Command { get; init; }
    [JsonPropertyName("exit_code")]
    public required int ExitCode { get; init; }
    [JsonPropertyName("log_path")]
    public required string LogPath { get; init; }
    [JsonPropertyName("duration_seconds")]
    public required int DurationSeconds { get; init; }
    [JsonPropertyName("trace")]
    public required object Trace { get; init; }
    [JsonPropertyName("parsed")]
    public object? Parsed { get; init; }
}

public sealed record TestPlanRunReport
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }
    [JsonPropertyName("runId")]
    public string RunId => TestRunId;
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }
    [JsonPropertyName("reproCommand")]
    public string? ReproCommand { get; init; }
    [JsonPropertyName("test_run_id")]
    public required string TestRunId { get; init; }
    [JsonPropertyName("goal_id")]
    public required string GoalId { get; init; }
    [JsonPropertyName("plan_path")]
    public required string PlanPath { get; init; }
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonPropertyName("overall_result")]
    public required string OverallResult { get; init; }
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
    [JsonPropertyName("commands_run")]
    public IReadOnlyList<string> CommandsRun { get; init; } = [];
    [JsonPropertyName("expected")]
    public required object Expected { get; init; }
    [JsonPropertyName("actual")]
    public required TestPlanActual Actual { get; init; }
    [JsonPropertyName("evidence")]
    public IReadOnlyList<TestPlanEvidence> Evidence { get; init; } = [];
    [JsonPropertyName("trace")]
    public required object Trace { get; init; }
    [JsonPropertyName("key_metrics")]
    public required TestPlanMetrics KeyMetrics { get; init; }
    [JsonPropertyName("critical_issues")]
    public IReadOnlyList<string> CriticalIssues { get; init; } = [];
    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors => CriticalIssues;
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings => CompatibilityWarnings;
    [JsonPropertyName("full_log_location")]
    public required string FullLogLocation { get; init; }
    [JsonPropertyName("time_taken_seconds")]
    public required int TimeTakenSeconds { get; init; }
    [JsonPropertyName("next_suggestions")]
    public IReadOnlyList<string> NextSuggestions { get; init; } = [];
    [JsonPropertyName("steps")]
    public IReadOnlyList<TestPlanStepReport> Steps { get; init; } = [];
    [JsonPropertyName("report_schema_valid")]
    public required bool ReportSchemaValid { get; init; }
    [JsonPropertyName("report_schema_validation")]
    public required object ReportSchemaValidation { get; init; }
    [JsonPropertyName("compatibility_mode")]
    public bool CompatibilityMode { get; init; }
    [JsonPropertyName("compatibility_warnings")]
    public IReadOnlyList<string> CompatibilityWarnings { get; init; } = [];
    [JsonPropertyName("boundary")]
    public required string Boundary { get; init; }
}

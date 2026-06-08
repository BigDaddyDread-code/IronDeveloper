using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class SupervisorAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly string _repoRoot;
    private readonly IAgentLlmClient? _llmClient;
    private readonly IAgentProcessRunner _processRunner;
    private readonly IRunReportContractReader _runReportContractReader;
    private readonly IWorkspaceApplyReportReader? _workspaceApplyReportReader;
    private readonly IWorkspaceApplyRecommendationService? _workspaceApplyRecommendationService;
    private readonly IWorkspaceApplyActionRequestService? _workspaceApplyActionRequestService;

    public SupervisorAgent(
        AgentDefinition definition,
        IAgentModelResolver modelResolver,
        string repoRoot,
        IRunReportContractReader runReportContractReader,
        IAgentLlmClient? llmClient = null,
        IAgentProcessRunner? processRunner = null,
        IWorkspaceApplyReportReader? workspaceApplyReportReader = null,
        IWorkspaceApplyRecommendationService? workspaceApplyRecommendationService = null,
        IWorkspaceApplyActionRequestService? workspaceApplyActionRequestService = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _repoRoot = repoRoot;
        _llmClient = llmClient;
        _runReportContractReader = runReportContractReader ?? throw new ArgumentNullException(
            nameof(runReportContractReader),
            "SupervisorAgent requires a real run report contract reader.");
        _processRunner = processRunner ?? new AgentProcessRunner();
        _workspaceApplyReportReader = workspaceApplyReportReader;
        _workspaceApplyRecommendationService = workspaceApplyRecommendationService;
        _workspaceApplyActionRequestService = workspaceApplyActionRequestService;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var project = RequireInput(request, "project");
        var query = RequireInput(request, "query");
        var planPath = RequireInput(request, "plan_path");
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"SupervisorAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;
        var autonomyTier = DetermineAutonomyTier(planPath);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var isDisposableApply = string.Equals(autonomyTier, "Tier4DisposableWorkspaceApply", StringComparison.OrdinalIgnoreCase);
        var actionType = isDisposableApply
            ? "execute disposable workspace apply validation plan"
            : "execute bounded autonomous validation plan";
        var safetyBoundaryRefs = isDisposableApply
            ? "disposable workspace|outside real repo|before hash|after hash|no real repository writes|TesterAgent executes only"
            : "No real repository writes|TesterAgent executes only|bounded read/test/report autonomy|no patch apply";

        var memory = await RunDotnetAsync([
            "run",
            "--no-build",
            "--project",
            RunnerProjectPath(),
            "--",
            "agent",
            "retriever",
            "search",
            "--project",
            project,
            "--query",
            query,
            "--take",
            "5",
            "--run-id",
            runId,
            "--json"
        ], ct);

        var memoryJson = TryParse(memory.Stdout);
        var memorySucceeded = memory.ExitCode == 0 && ReadString(memoryJson, "status") == "Succeeded";
        var topMemoryTitle = ReadString(memoryJson, "contextPackage", "Matches", "0", "DocumentTitle");
        var semanticTraceId = ReadString(memoryJson, "contextPackage", "SemanticTraceId");
        var weightedSummary = ReadString(memoryJson, "contextPackage", "WeightedContextBundle", "summaryForAgent");

        var conscience = memorySucceeded
            ? await RunDotnetAsync([
                "run",
                "--no-build",
                "--project",
                RunnerProjectPath(),
                "--",
                "agent",
                "conscience",
                "review",
                "--action-type",
                actionType,
                "--observed-project",
                project,
                "--affected-project",
                project,
                "--evidence",
                JoinEvidence([
                    $"RetrieverAgent succeeded for project {project}",
                    string.IsNullOrWhiteSpace(topMemoryTitle) ? "top memory title missing" : $"top memory title {topMemoryTitle}",
                    string.IsNullOrWhiteSpace(semanticTraceId) ? "semantic trace missing" : $"semantic trace {semanticTraceId}",
                    $"plan path {planPath}"
                ]),
                "--requested-tools",
                "RetrieverAgent|TesterAgent|ThoughtLedger",
                "--memory-authority-refs",
                string.IsNullOrWhiteSpace(topMemoryTitle) ? "WeightedContextBundle" : topMemoryTitle,
                "--safety-boundary-refs",
                safetyBoundaryRefs,
                "--run-id",
                $"{runId}-conscience",
                "--json"
            ], ct)
            : CommandRun.Skipped("ConscienceAgent skipped because RetrieverAgent failed.");

        var conscienceJson = TryParse(conscience.Stdout);
        var conscienceDecision = ReadString(conscienceJson, "review", "decision");
        var conscienceAllows = string.Equals(conscienceDecision, "Allow", StringComparison.OrdinalIgnoreCase);

        var thoughtLedger = await RunDotnetAsync([
            "run",
            "--no-build",
            "--project",
            RunnerProjectPath(),
            "--",
            "agent",
            "thought-ledger",
            "explain",
            "--subject",
            "Supervisor governed autonomous validation loop",
            "--decision",
            string.IsNullOrWhiteSpace(conscienceDecision) ? "NeedsMoreEvidence" : conscienceDecision,
            "--observed-project",
            project,
            "--affected-project",
            project,
            "--evidence",
            JoinEvidence([
                $"memorySucceeded={memorySucceeded}",
                $"conscienceDecision={conscienceDecision}",
                string.IsNullOrWhiteSpace(weightedSummary) ? "weighted summary missing" : weightedSummary
            ]),
            "--known-boundaries",
            isDisposableApply
                ? "No real repository writes|TesterAgent executes only|patch apply only inside explicit disposable workspace|bounded autonomy"
                : "No real repository writes|TesterAgent executes only|no patch apply|bounded autonomy",
            "--uncertainties",
            memorySucceeded ? "" : "retrieval failed",
            "--candidate-actions",
            isDisposableApply
                ? "run disposable workspace Test Agent plan|request more evidence|stop before real repo writes"
                : "run TesterAgent plan|request more evidence|stop before writes",
            "--json"
        ], ct);

        var tests = memorySucceeded && conscienceAllows
            ? await RunDotnetAsync([
                "run",
                "--no-build",
                "--project",
                RunnerProjectPath(),
                "--",
                "agent",
                "tester",
                "run-plan",
                "--plan",
                planPath,
                "--run-id",
                $"{runId}-tester",
                "--json"
            ], ct)
            : CommandRun.Skipped(memorySucceeded
                ? $"TesterAgent skipped because ConscienceAgent decision was {conscienceDecision}."
                : "TesterAgent skipped because RetrieverAgent failed.");

        var testerRunId = $"{runId}-tester";
        var testerReport = await ReadTesterRunReportAsync(tests, testerRunId, ct);
        var workspaceApplyReport = await ReadWorkspaceApplyReportAsync(request, runId, ct);
        var workspaceApplyRecommendation = BuildWorkspaceApplyRecommendation(workspaceApplyReport);
        var workspaceApplyActionRequest = BuildWorkspaceApplyActionRequest(workspaceApplyReport, workspaceApplyRecommendation);
        var testsSucceeded = testerReport is not null && testerReport.Status is "succeeded";
        var testSummary = BuildTesterSummary(testerReport, testerRunId, TryParse(tests.Stdout));
        var status = memorySucceeded && conscienceAllows && testsSucceeded ? AgentRunStatus.Succeeded : AgentRunStatus.Failed;
        var decision = SelectDecision(memorySucceeded, conscienceAllows, testsSucceeded, conscienceDecision);
        var decisionReason = BuildDecisionReason(decision, memorySucceeded, conscienceAllows, testsSucceeded, conscienceDecision);
        var prompt = BuildPrompt(project, query, planPath, autonomyTier, decision, decisionReason, memorySucceeded, conscienceAllows, testsSucceeded);
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);

        var handoff = new
        {
            goalId = request.GoalId,
            dogfoodRunId = runId,
            project,
            query,
            planPath,
            supervisor = new
            {
                agent = AgentName,
                modelProfile = profile.Name,
                provider = profile.Provider,
                model = profile.Model,
                allowedDecisions = new[]
                {
                    "continue",
                    "stop_on_failure",
                    "request_failure_package",
                    "request_retrieval_context",
                    "request_more_evidence",
                    "report_ready"
                },
                decision,
                decisionReason,
                llmIntelligence = new
                {
                    modelProfile = profile.Name,
                    profileProvider = profile.Provider,
                    profileModel = profile.Model,
                    prompt,
                    invocationMode = llmResult.InvocationMode,
                    liveLlmRequested,
                    wasAttempted = llmResult.WasAttempted,
                    wasSuccessful = llmResult.WasSuccessful,
                    durationMs = llmResult.DurationMs,
                    modelSummary = BuildModelSummary(llmResult),
                    error = llmResult.WasSuccessful ? string.Empty : llmResult.ErrorMessage,
                    boundary = "Live SupervisorAgent output is advisory orchestration commentary only. Conscience, ThoughtLedger, and deterministic stop conditions remain authoritative."
                },
                decisionEvidence = new[]
                {
                    $"memorySucceeded={memorySucceeded}",
                    $"conscienceDecision={conscienceDecision}",
                    $"conscienceAllows={conscienceAllows}",
                    $"testerSucceeded={testsSucceeded}",
                    $"testerStatus={testerReport?.Status ?? "unknown"}",
                    string.IsNullOrWhiteSpace(topMemoryTitle) ? "topMemoryTitle=<none>" : $"topMemoryTitle={topMemoryTitle}",
                    string.IsNullOrWhiteSpace(testSummary) ? "testerSummary=<none>" : $"testerSummary={testSummary}"
                }
            },
            memory = new
            {
                succeeded = memorySucceeded,
                topTitle = topMemoryTitle,
                semanticTraceId,
                weightedContextBundle = ReadElement(memoryJson, "contextPackage", "WeightedContextBundle"),
                contextPackage = ReadElement(memoryJson, "contextPackage")
            },
            conscience = new
            {
                succeeded = conscience.ExitCode == 0,
                decision = conscienceDecision,
                review = ReadElement(conscienceJson, "review")
            },
            thoughtLedger = new
            {
                succeeded = thoughtLedger.ExitCode == 0,
                explanation = ReadElement(TryParse(thoughtLedger.Stdout), "thoughtLedger")
            },
            tester = new
            {
                succeeded = testsSucceeded,
                summary = testSummary,
                runId = testerReport?.Data.RunId,
                traceId = testerReport?.TraceId,
                commandStatus = testerReport?.Status,
                runStatus = testerReport?.Data.RunStatus,
                governance = testerReport?.Data.Governance,
                warnings = testerReport?.Warnings ?? [],
                report = ReadElement(TryParse(tests.Stdout), "report")
            },
            workspaceApply = workspaceApplyReport,
            workspaceApplyRecommendation,
            workspaceApplyActionRequest,
            governedAutonomy = new
            {
                tier = autonomyTier,
                autonomousExecutionAllowed = memorySucceeded && conscienceAllows,
                mutationAllowed = false,
                realRepoMutationAllowed = false,
                disposableWorkspaceMutationAllowed = isDisposableApply && memorySucceeded && conscienceAllows && testsSucceeded,
                executedTesterAgent = tests.ExitCode == 0,
                stopReason = status == AgentRunStatus.Succeeded
                    ? ""
                    : decisionReason,
                boundary = isDisposableApply
                    ? "SupervisorAgent may autonomously run disposable workspace apply/build/test plans only when ConscienceAgent allows them and the cage evidence is explicit. Real repository writes, ticket creation, memory mutation, and self-approval remain blocked."
                    : "SupervisorAgent may autonomously run safe read/test/report loops only when ConscienceAgent allows them. It must stop before writes, ticket creation, memory mutation, builder apply, or real repository changes."
            },
            codexHandoff = new
            {
                observedFailure = status == AgentRunStatus.Succeeded ? "" : "Supervisor loop did not complete cleanly.",
                evidence = new[]
                {
                    "RetrieverAgent returned project memory context.",
                    "ConscienceAgent reviewed the proposed autonomous validation action.",
                    "ThoughtLedger explained the visible reasoning summary.",
                    "TesterAgent executed the selected validation plan."
                },
                recommendedNextAction = status == AgentRunStatus.Succeeded
                    ? "Codex may inspect the compact handoff and choose the next scoped improvement."
                    : "Generate a failure package from the failed Test Agent run before patching.",
                boundary = isDisposableApply
                    ? "136 proves a governed autonomous disposable workspace apply loop. It may mutate only the explicit disposable workspace and must not write the real repo, mutate memory, create tickets, approve itself, or apply patches outside the cage."
                    : "135 proves a governed autonomous read/test/report supervisor loop while preserving the tiny memory-to-test supervisor decision loop and memory-to-test orchestration boundary. It does not write files, mutate memory, create tickets, change builder behaviour, or apply code patches."
            }
        };

        var outputJson = JsonSerializer.Serialize(handoff, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return new AgentResult
        {
            AgentName = AgentName,
            Status = status,
            Summary = status == AgentRunStatus.Succeeded
                ? $"SupervisorAgent retrieved '{topMemoryTitle}' and TesterAgent reported: {testSummary}"
                : (memory.ExitCode == -1 || thoughtLedger.ExitCode == -1 || tests.ExitCode == -1)
                    ? "SupervisorAgent loop failed due to a subprocess timeout."
                    : "SupervisorAgent loop failed before producing a clean Codex handoff.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = status == AgentRunStatus.Succeeded ? 0 : 1,
            OutputJson = outputJson,
            CommandsRun = [memory.Command, conscience.Command, thoughtLedger.Command, tests.Command],
            EvidencePaths = ExtractEvidencePaths(testerReport, TryParse(tests.Stdout), workspaceApplyReport),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private WorkspaceApplyActionRequest? BuildWorkspaceApplyActionRequest(
        WorkspaceApplyReportSummary? workspaceApplyReport,
        WorkspaceApplyRecommendation? workspaceApplyRecommendation)
    {
        if (workspaceApplyReport is null || workspaceApplyRecommendation is null)
            return null;

        try
        {
            var service = _workspaceApplyActionRequestService ?? new WorkspaceApplyActionRequestService();
            return service.Create(new WorkspaceApplyActionRequestInput
            {
                Report = workspaceApplyReport,
                Recommendation = workspaceApplyRecommendation
            });
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyActionRequest
            {
                RequestedAction = WorkspaceApplyRequestedActions.CollectMissingEvidence,
                Reason = "Workspace apply evidence is incomplete or inconsistent.",
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                MutatesSourceRepo = false,
                RequiresFreshHumanDecision = true,
                SuggestedCommand = null,
                SuggestedCommandArguments = [],
                Preconditions =
                [
                    "identify missing evidence",
                    "rerun only the appropriate governed command manually",
                    "do not infer success"
                ],
                EvidencePaths = workspaceApplyReport.EvidencePaths,
                RiskNotes = workspaceApplyReport.RiskNotes,
                Warnings = [$"Workspace apply action request could not be produced: {exception.Message}"]
            };
        }
    }

    private WorkspaceApplyRecommendation? BuildWorkspaceApplyRecommendation(WorkspaceApplyReportSummary? workspaceApplyReport)
    {
        if (workspaceApplyReport is null)
            return null;

        try
        {
            var service = _workspaceApplyRecommendationService ?? new WorkspaceApplyRecommendationService();
            return service.Recommend(new WorkspaceApplyRecommendationRequest
            {
                Report = workspaceApplyReport
            });
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyRecommendation
            {
                RecommendedAction = WorkspaceApplyRecommendedActions.CollectMissingEvidence,
                Reason = "Workspace apply evidence is incomplete or inconsistent.",
                HumanReviewRequired = true,
                SafeToRetry = false,
                SafeToCommitAfterReview = false,
                SourceReviewRequiredBeforeRetry = true,
                BlocksAutomaticExecution = true,
                EvidencePaths = workspaceApplyReport.EvidencePaths,
                RiskNotes = workspaceApplyReport.RiskNotes,
                Warnings = [$"Workspace apply recommendation could not be produced: {exception.Message}"]
            };
        }
    }

    private async Task<WorkspaceApplyReportSummary?> ReadWorkspaceApplyReportAsync(
        AgentRequest request,
        string defaultRunId,
        CancellationToken ct)
    {
        var workspacePath = ReadOptionalInput(request, "workspace_apply_workspace_path");
        if (string.IsNullOrWhiteSpace(workspacePath))
            return null;

        var reportRunId = ReadOptionalInput(request, "workspace_apply_run_id");
        if (string.IsNullOrWhiteSpace(reportRunId))
            reportRunId = defaultRunId;

        try
        {
            var reader = _workspaceApplyReportReader ?? new WorkspaceApplyReportReader();
            return await reader.ReadAsync(
                new WorkspaceApplyReportRequest
                {
                    RunId = reportRunId,
                    WorkspacePath = workspacePath
                },
                ct);
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyReportSummary
            {
                RunId = reportRunId,
                WorkspacePath = workspacePath,
                Outcome = "unavailable",
                Warnings = [$"Workspace apply report could not be read: {exception.Message}"]
            };
        }
    }

    private async Task<RunReportContractReadResult?> ReadTesterRunReportAsync(
        CommandRun tests,
        string testerRunId,
        CancellationToken ct)
    {
        if (tests.ExitCode != 0)
            return null;

        try
        {
            return await _runReportContractReader.ReadAsync(testerRunId, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BuildTesterSummary(
        RunReportContractReadResult? testerReport,
        string testerRunId,
        JsonElement? testJson)
    {
        if (testerReport is not null)
        {
            var governance = testerReport.Data.Governance;
            return $"runId={testerReport.Data.RunId}, commandStatus={testerReport.Status}, runStatus={testerReport.Data.RunStatus}, governanceDecision={governance.Decision}";
        }

        if (testJson is null)
            return $"No tester run report available for {testerRunId} and no parsed tester stdout.";

        var status = ReadString(testJson, "status");
        if (!string.IsNullOrWhiteSpace(status))
            return $"TesterAgent returned status {status} without a contract report for {testerRunId}.";

        return $"TesterAgent output could not be interpreted for {testerRunId}; no contract report was available.";
    }

    private async Task<AgentLlmCallResult> ResolveLlmResultAsync(
        ModelProfile profile,
        string prompt,
        bool liveLlmRequested,
        AgentRequest request,
        CancellationToken ct)
    {
        if (request.Inputs.TryGetValue("llm_response", out var providedResponse) &&
            !string.IsNullOrWhiteSpace(providedResponse))
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "provided_llm_response",
                ResponseText = providedResponse
            };
        }

        if (!liveLlmRequested)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "llm_ready_deterministic_fallback",
                ResponseText = "No live model response supplied; deterministic SupervisorAgent orchestration remained in force."
            };
        }

        if (_llmClient is null)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = false,
                InvocationMode = "live_model_requested_without_client_fallback",
                ErrorMessage = "No governed agent LLM client was configured."
            };
        }

        return await _llmClient.CompleteAsync(profile, prompt, ct);
    }

    private string RunnerProjectPath() =>
        Path.Combine(_repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");

    private static string DetermineAutonomyTier(string planPath) =>
        planPath.Contains("disposable-workspace-apply", StringComparison.OrdinalIgnoreCase)
            ? "Tier4DisposableWorkspaceApply"
            : "Tier3ReadTestReport";

    private static string SelectDecision(bool memorySucceeded, bool conscienceAllows, bool testsSucceeded, string conscienceDecision)
    {
        if (!memorySucceeded)
            return "request_retrieval_context";

        if (!conscienceAllows)
            return string.Equals(conscienceDecision, "NeedsMoreEvidence", StringComparison.OrdinalIgnoreCase)
                ? "request_more_evidence"
                : "stop_on_failure";

        if (!testsSucceeded)
            return "request_failure_package";

        return "report_ready";
    }

    private static string BuildDecisionReason(
        string decision,
        bool memorySucceeded,
        bool conscienceAllows,
        bool testsSucceeded,
        string conscienceDecision) =>
        decision switch
        {
            "request_retrieval_context" => "RetrieverAgent did not return usable project memory context.",
            "request_more_evidence" => "ConscienceAgent requested more evidence before autonomous execution.",
            "stop_on_failure" => $"ConscienceAgent blocked autonomous execution with decision '{conscienceDecision}'.",
            "request_failure_package" => "TesterAgent did not return a passing report; Codex needs a failure package before patching.",
            "report_ready" => "RetrieverAgent returned project memory, ConscienceAgent allowed bounded execution, and TesterAgent returned a passing report.",
            _ => $"Supervisor selected {decision}; memorySucceeded={memorySucceeded}; conscienceAllows={conscienceAllows}; testsSucceeded={testsSucceeded}."
        };

    private static string BuildPrompt(
        string project,
        string query,
        string planPath,
        string autonomyTier,
        string decision,
        string decisionReason,
        bool memorySucceeded,
        bool conscienceAllows,
        bool testsSucceeded) =>
        $"""
        You are SupervisorAgent for IronDev/IDA.
        Review this governed orchestration state and return concise JSON with orchestration risks, stop/continue notes, and questions for Codex/human review.
        Do not bypass ConscienceAgent, override ThoughtLedger, create tickets, mutate memory, patch files, approve writes, or rerun actions.
        Project: {project}
        Query: {query}
        PlanPath: {planPath}
        AutonomyTier: {autonomyTier}
        Decision: {decision}
        DecisionReason: {decisionReason}
        MemorySucceeded: {memorySucceeded}
        ConscienceAllows: {conscienceAllows}
        TestsSucceeded: {testsSucceeded}
        """;

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static string ReadOptionalInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) ? value : string.Empty;

    private static string BuildModelSummary(AgentLlmCallResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ResponseText))
            return result.ResponseText;

        return result.WasAttempted
            ? "Live model call did not return usable content; deterministic SupervisorAgent orchestration remained in force."
            : "No live model response supplied; deterministic SupervisorAgent orchestration remained in force.";
    }

    private static int SubprocessTimeoutSeconds =>
        int.TryParse(Environment.GetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS"), out var parsed)
            ? parsed
            : 300;

    private async Task<CommandRun> RunDotnetAsync(string[] arguments, CancellationToken ct)
    {
        var result = await _processRunner.RunAsync("dotnet", arguments, _repoRoot, ct);
        return new CommandRun(result.Command, result.ExitCode, result.Stdout, result.Stderr);
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"SupervisorAgent requires input '{key}'.");
    }

    private static JsonElement? TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ReadElement(JsonElement? root, params string[] path)
    {
        if (root is null)
            return null;

        var current = root.Value;
        foreach (var segment in path)
        {
            if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength())
                    return null;
                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.Clone();
    }

    private static string ReadString(JsonElement? root, params string[] path)
    {
        var value = ReadElement(root, path);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() ?? string.Empty : string.Empty;
    }

    private static IReadOnlyList<string> ExtractEvidencePaths(
        RunReportContractReadResult? testerReport,
        JsonElement? testJson,
        WorkspaceApplyReportSummary? workspaceApplyReport)
    {
        var paths = new List<string>();
        if (testerReport is not null)
        {
            var contractPaths = testerReport.Data.Evidence
                .Select(item => item.Path)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();

            paths.AddRange(contractPaths);
        }

        var report = ReadElement(testJson, "report");
        var evidence = ReadElement(report, "evidence");
        if (evidence is not null && evidence.Value.ValueKind == JsonValueKind.Array)
        {
            paths.AddRange(evidence.Value.EnumerateArray()
                .Select(item => item.TryGetProperty("path", out var path) ? path.GetString() : null)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>());
        }

        if (workspaceApplyReport is not null)
            paths.AddRange(workspaceApplyReport.EvidencePaths);

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string JoinEvidence(IEnumerable<string> evidence) =>
        string.Join('|', evidence.Where(item => !string.IsNullOrWhiteSpace(item)));

    private sealed record CommandRun(string Command, int ExitCode, string Stdout, string Stderr)
    {
        public static CommandRun Skipped(string reason) => new(reason, 1, string.Empty, reason);
    }
}

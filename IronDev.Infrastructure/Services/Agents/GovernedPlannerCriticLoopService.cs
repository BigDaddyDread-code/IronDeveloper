using System.Text.Json;
using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class GovernedPlannerCriticLoopService
{
    private readonly GovernedToolRegistry _toolRegistry;
    private readonly EvidenceValidationService _evidenceValidation;

    public GovernedPlannerCriticLoopService(GovernedToolRegistry toolRegistry, EvidenceValidationService evidenceValidation)
    {
        _toolRegistry = toolRegistry;
        _evidenceValidation = evidenceValidation;
    }

    public async Task<PlannerCriticLoopResult> RunAsync(
        string project,
        string goal,
        string runId,
        string runtime = "dotnet",
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var stages = new List<AgentLoopStageTrace>();
        var requests = BuildPlannerToolRequests(project, goal, runId, runtime);
        var results = new List<AgentToolResult>();
        var runtimeProfiles = _toolRegistry.ListRuntimeProfiles();
        var plannerDraft = BuildPlannerDraft(project, goal, runtime, requests, runtimeProfiles);

        stages.Add(Stage("PlannerDraft", "Succeeded", "PlannerAgent drafted a tool-using plan with evidence requirements."));
        stages.Add(Stage("SupervisorToolReview", "Succeeded", "SupervisorAgent accepted read/test/report tool requests and rejected mutation by default."));

        foreach (var request in requests)
        {
            results.Add(await _toolRegistry.RunAsync(request, ct));
        }

        stages.Add(Stage("ToolExecution", AllSucceeded(results) ? "Succeeded" : "Failed", $"Executed {results.Count} governed tool request(s)."));

        var requiredEvidence = new[] { "memory.search", "code.search", "quality.run-gate" };
        var evidenceValidation = _evidenceValidation.Validate(results, requiredEvidence);
        stages.Add(Stage("EvidenceValidation", evidenceValidation.Status, $"Evidence validation {evidenceValidation.Status}."));

        var criticReview = BuildCriticReview(goal, results, evidenceValidation);
        stages.Add(Stage("CriticReview", "Succeeded", "CriticAgent reviewed evidence sufficiency, blast radius, and fake-confidence risk."));

        var escalation = BuildEscalation(evidenceValidation, requests);
        stages.Add(Stage("HumanEscalationGate", escalation.Decision, escalation.Reason));

        var revisedPlan = BuildRevisedPlan(project, goal, runtime, results, evidenceValidation, escalation);
        stages.Add(Stage("PlannerRevision", "Succeeded", "PlannerAgent revised the plan from tool evidence and CriticAgent review."));

        var trace = new AgentLoopTrace
        {
            TraceId = traceId,
            RunId = runId,
            Project = project,
            Goal = goal,
            Runtime = runtime,
            Stages = stages,
            ToolRequests = requests,
            ToolResults = results,
            EvidenceValidation = evidenceValidation,
            HumanEscalation = escalation,
            RuntimeProfiles = runtimeProfiles
        };

        var status = string.Equals(evidenceValidation.Status, "Passed", StringComparison.OrdinalIgnoreCase)
            ? "Succeeded"
            : "NeedsMoreEvidence";

        return new PlannerCriticLoopResult
        {
            Command = "agent loop plan-review",
            Status = status,
            RunId = runId,
            TraceId = traceId,
            Project = project,
            Goal = goal,
            Summary = status == "Succeeded"
                ? "Planner/Critic governed loop produced an evidence-backed revised plan."
                : "Planner/Critic governed loop stopped for missing evidence.",
            Trace = trace,
            PlannerDraft = plannerDraft,
            CriticReview = criticReview,
            RevisedPlan = revisedPlan,
            EvidenceRefs = results.SelectMany(result => result.EvidenceRefs).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Recommendation = status == "Succeeded" ? "ReadyForHumanReview" : "CollectMissingEvidence"
        };
    }

    public async Task WriteOutputsAsync(PlannerCriticLoopResult result, string runRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(runRoot);
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(runRoot, "agent-loop-trace.json"), JsonSerializer.Serialize(result.Trace, options), ct);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(result, options), ct);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdown(result), ct);
    }

    private static IReadOnlyList<AgentToolRequest> BuildPlannerToolRequests(string project, string goal, string runId, string runtime) =>
    [
        Request("tool-001-memory", "PlannerAgent", "memory.search", project, goal, "Find accepted project memory before planning.", runtime, new Dictionary<string, string> { ["query"] = goal }),
        Request("tool-002-code", "PlannerAgent", "code.search", project, goal, "Find nearby code/docs vocabulary before planning.", runtime, new Dictionary<string, string> { ["query"] = goal }),
        Request("tool-003-trace", "CriticAgent", "trace.read", project, goal, "Check recent run evidence for repeated failures.", runtime, new Dictionary<string, string>()),
        Request("tool-004-failure", "CriticAgent", "failure.latest", project, goal, "Check whether a recent failure package should shape the plan.", runtime, new Dictionary<string, string>()),
        Request("tool-005-quality", "QualityAgent", "quality.run-gate", project, goal, "Run deterministic quality evidence before recommending a branch.", runtime, new Dictionary<string, string>
        {
            ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
            ["run_id"] = $"{runId}-quality-tool"
        }),
        Request("tool-006-test", "TesterAgent", "test.run-plan", project, goal, "Prove the generic Test Agent plan capability remains available through the tool contract.", runtime, new Dictionary<string, string>
        {
            ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-thought-ledger-132.json",
            ["run_id"] = $"{runId}-test-tool"
        }),
        Request("tool-007-runtime", "SupervisorAgent", "project.build", project, goal, "Resolve runtime profile without executing a build.", runtime, new Dictionary<string, string>())
    ];

    private static AgentToolRequest Request(
        string id,
        string agent,
        string tool,
        string project,
        string goal,
        string reason,
        string runtime,
        IReadOnlyDictionary<string, string> parameters) =>
        new()
        {
            RequestId = id,
            RequestedBy = agent,
            ToolName = tool,
            Project = project,
            Goal = goal,
            Reason = reason,
            Runtime = runtime,
            Parameters = parameters
        };

    private static object BuildPlannerDraft(
        string project,
        string goal,
        string runtime,
        IReadOnlyList<AgentToolRequest> requests,
        IReadOnlyList<ProjectRuntimeProfile> runtimeProfiles) =>
        new
        {
            project,
            goal,
            runtime,
            planKind = "ToolUsingEvidencePlan",
            requestedTools = requests.Select(request => new { request.ToolName, request.Reason, request.RequiresMutation }),
            requiredEvidence = new[] { "memory.search", "code.search", "quality.run-gate" },
            languageAgnosticRule = "Agents request capabilities such as project.build; runtime profiles resolve dotnet, node, or python commands.",
            runtimeProfiles = runtimeProfiles.Select(profile => new { profile.Project, profile.Runtime, profile.BuildCommand, profile.TestCommand }),
            boundary = "Planner draft does not execute commands, write files, create tickets, or mutate memory."
        };

    private static object BuildCriticReview(
        string goal,
        IReadOnlyList<AgentToolResult> results,
        EvidenceValidationResult validation) =>
        new
        {
            goal,
            evidenceStatus = validation.Status,
            failedTools = results.Where(result => !string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase)).Select(result => result.ToolName),
            fakeConfidenceRisk = validation.MissingEvidence.Count > 0
                ? "High: plan must not proceed without required evidence."
                : "Low: required evidence was collected, but human review remains required before writes.",
            blastRadiusReview = "Read/test/report-only loop. No files, tickets, memory, or patches were mutated.",
            recommendation = validation.Status == "Passed" ? "revise_plan_with_evidence" : "collect_missing_evidence",
            boundary = "CriticAgent reviews evidence only. It does not patch or approve writes."
        };

    private static object BuildRevisedPlan(
        string project,
        string goal,
        string runtime,
        IReadOnlyList<AgentToolResult> results,
        EvidenceValidationResult validation,
        HumanEscalationGate escalation) =>
        new
        {
            project,
            goal,
            runtime,
            confidence = validation.Status == "Passed" ? 0.78m : 0.42m,
            sourceMemory = Data(results, "memory.search", "topTitle"),
            likelyTouchedAreas = new[] { Data(results, "code.search", "topPath") }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            testsRequired = new[] { "code standards alpha", "main alpha regression pack when branch is ready" },
            acceptanceCriteria = new[]
            {
                "All agent tool requests are traceable.",
                "Evidence validation passes before recommendation.",
                "Human escalation decision is explicit.",
                "Runtime profiles keep dotnet/node/python separated from capability names."
            },
            doNotTouch = new[] { "real repository writes", "memory mutation", "ticket acceptance", "patch apply", "ConscienceAgent bypass" },
            humanEscalation = escalation.Decision,
            nextSafeAction = validation.Status == "Passed" ? "Open human-reviewed implementation PR." : "Collect missing evidence first.",
            boundary = "Revised plan is advisory. It does not execute writes or self-approve."
        };

    private static HumanEscalationGate BuildEscalation(EvidenceValidationResult validation, IReadOnlyList<AgentToolRequest> requests)
    {
        var mutationRequested = requests.Any(request => request.RequiresMutation);
        if (mutationRequested)
        {
            return new HumanEscalationGate
            {
                Decision = "Blocked",
                Reason = "Mutation tool was requested in a read/test/report loop.",
                RequiredApprovals = ["Human approval for disposable workspace mutation"],
                BlockedActions = ["patch apply", "real repository writes", "memory mutation"]
            };
        }

        if (!string.Equals(validation.Status, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            return new HumanEscalationGate
            {
                Decision = "NeedsMoreEvidence",
                Reason = "Required evidence is missing.",
                RequiredApprovals = ["Codex/human review after evidence is collected"],
                BlockedActions = ["implementation", "ticket acceptance", "builder apply"]
            };
        }

        return new HumanEscalationGate
        {
            Decision = "HumanReviewRequired",
            Reason = "Evidence-backed plan is ready for human/Codex review, not automatic execution.",
            RequiredApprovals = ["Human approval before any write path"],
            BlockedActions = ["real repository writes", "memory mutation", "ticket creation without review", "self-approval"]
        };
    }

    private static AgentLoopStageTrace Stage(string name, string status, string summary) =>
        new()
        {
            StageName = name,
            Status = status,
            Summary = summary,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };

    private static bool AllSucceeded(IReadOnlyList<AgentToolResult> results) =>
        results.All(result => string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase));

    private static string Data(IReadOnlyList<AgentToolResult> results, string toolName, string key) =>
        results.FirstOrDefault(result => string.Equals(result.ToolName, toolName, StringComparison.OrdinalIgnoreCase))?.Data.TryGetValue(key, out var value) == true
            ? value
            : string.Empty;

    private static string BuildMarkdown(PlannerCriticLoopResult result) =>
        $"""
        # {result.Goal}

        Status: {result.Status}
        Project: {result.Project}
        Trace: {result.TraceId}
        Recommendation: {result.Recommendation}

        ## Stages
        {string.Join(Environment.NewLine, result.Trace.Stages.Select(stage => $"- {stage.StageName}: {stage.Status} - {stage.Summary}"))}

        ## Tools
        {string.Join(Environment.NewLine, result.Trace.ToolResults.Select(tool => $"- {tool.ToolName}: {tool.Status} - {tool.Summary}"))}

        ## Boundary
        {result.Boundary}
        """;
}

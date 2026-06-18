using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Memory;

namespace IronDev.Cli;

internal static class IronDevCliPlanning
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";
    private const string DefaultMemoryFolderName = "irondev-memory";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsPlanCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "plan", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "plan requires a subcommand: memory-context, context, propose, review, or status.");

        return args[1].ToLowerInvariant() switch
        {
            "memory-context" => await HandleMemoryContextAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "context" => await HandleContextAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "propose" => await HandleProposeAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "review" => await HandleReviewAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => HandleStatus(args, output, error),
            "execute" or "apply" or "approve" or "promote-memory" or "continue" or "release" or "deploy" or "merge" => Usage(error, $"plan {args[1]} is intentionally unsupported; AK is planning evidence only."),
            _ => Usage(error, $"unsupported plan subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleMemoryContextAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseTaskRunCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "plan memory-context", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        Directory.CreateDirectory(runPath);
        var taskSummary = await ReadTaskSummaryAsync(parsed.TaskPath!, cancellationToken).ConfigureAwait(false);
        var request = new AcceptedMemoryRetrievalRequest
        {
            RetrievalRequestId = $"mem_retrieval_req_{Guid.NewGuid():N}",
            RunId = Path.GetFileName(runPath),
            ProjectId = parsed.ProjectId ?? "project-unknown",
            TaskSummary = taskSummary,
            RepoIdentity = parsed.RepoIdentity ?? "repo-unknown",
            AllowedMemoryScopes = [MemoryScope.Project, MemoryScope.PortableEngineering, MemoryScope.Run],
            RequestedBy = "IronDevCli",
            RequestedAtUtc = DateTimeOffset.UtcNow,
            MaxResults = parsed.MaxResults ?? 10,
            Boundary = MemoryPlanningBoundary.ReadOnlyEvidence
        };

        var store = new AcceptedMemoryStore(parsed.MemoryRootPath ?? DefaultMemoryRoot());
        var result = AcceptedMemoryRetriever.Retrieve(store, request);
        var citations = MemoryCitationWriter.CreateBundle(result);

        await WriteJsonAsync(runPath, "accepted-memory-retrieval-request.json", request, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "accepted-memory-retrieval-result.json", result, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "memory-context.json", result.Items, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "memory-context.md"), RenderMemoryContext(result), cancellationToken).ConfigureAwait(false);
        foreach (var citation in citations.Citations)
            await AppendJsonLineAsync(Path.Combine(runPath, "memory-citations.jsonl"), citation, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "memory-citation-bundle.json", citations, cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.AcceptedMemoryRetrievalRequested, request.RetrievalRequestId, "Accepted memory retrieval was requested for planning.", ["accepted-memory-retrieval-request.json"]);
        RecordEvent(runPath, GovernanceKernelEventKind.AcceptedMemoryRetrieved, result.RetrievalResultId, "Accepted memory was retrieved as planning evidence.", ["accepted-memory-retrieval-result.json", "memory-context.json"]);
        RecordEvent(runPath, GovernanceKernelEventKind.MemoryCitationBundleCreated, citations.MemoryCitationBundleId, "Memory citation bundle was created.", ["memory-citation-bundle.json", "memory-citations.jsonl"]);

        if (parsed.Json)
            WriteJson(output, "plan memory-context", "succeeded", new { runPath, result, citations }, []);
        else
        {
            output.WriteLine($"Accepted memory context: {result.Items.Length} item(s)");
            output.WriteLine($"Run path: {runPath}");
            output.WriteLine("Boundary: memory informs planning only; memory did not approve, execute, promote, or continue workflow.");
        }

        return 0;
    }

    private static async Task<int> HandleContextAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseTaskRunCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "plan context", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var retrieval = ReadJson<AcceptedMemoryRetrievalResult>(Path.Combine(runPath, "accepted-memory-retrieval-result.json"));
        var citations = ReadJson<MemoryCitationBundle>(Path.Combine(runPath, "memory-citation-bundle.json"));
        if (retrieval is null || citations is null)
            return Failure(output, error, parsed.Json, "plan context", "memory context artifacts are missing; run 'irondev plan memory-context' first.");

        var taskSummary = await ReadTaskSummaryAsync(parsed.TaskPath!, cancellationToken).ConfigureAwait(false);
        var context = PlannerContextBuilder.Build(new PlannerContextBuildRequest
        {
            RunId = Path.GetFileName(runPath),
            TaskSummary = taskSummary,
            RepoIdentity = parsed.RepoIdentity ?? "repo-unknown",
            BaseCommit = parsed.BaseCommit ?? "base-unknown",
            RelevantFiles = parsed.RelevantFiles.ToArray()
        }, retrieval, citations);

        await WriteJsonAsync(runPath, "planner-context-bundle.json", context, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "planner-context.md"), RenderPlannerContext(context), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.PlannerContextBundleCreated, context.PlannerContextBundleId, "Planner context bundle was created from cited memory.", ["planner-context-bundle.json", "planner-context.md"]);

        if (parsed.Json)
            WriteJson(output, "plan context", "succeeded", new { runPath, context }, []);
        else
        {
            output.WriteLine($"Planner context: {context.PlannerContextBundleId}");
            output.WriteLine("Boundary: planner context is not authority.");
        }

        return 0;
    }

    private static async Task<int> HandleProposeAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "plan propose", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var context = ReadJson<PlannerContextBundle>(Path.Combine(runPath, "planner-context-bundle.json"));
        var citations = ReadJson<MemoryCitationBundle>(Path.Combine(runPath, "memory-citation-bundle.json"));
        if (context is null || citations is null)
            return Failure(output, error, parsed.Json, "plan propose", "planner context artifacts are missing; run 'irondev plan context' first.");

        var plan = MemoryInformedPlanProposalBuilder.Build(context, citations);
        var risk = MemoryPlanReviewBuilder.BuildRiskReport(plan, context);
        var testProfile = MemoryPlanReviewBuilder.BuildSuggestedTestProfile(plan);
        var boundary = MemoryPlanReviewBuilder.BuildBoundaryReport(plan, citations);

        await WriteJsonAsync(runPath, "plan-proposal.json", plan, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "plan-proposal.md"), RenderPlan(plan), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "plan-risks.md"), RenderRiskReport(risk), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "suggested-test-profile.json", testProfile, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(runPath, "planner-boundary-report.json", boundary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "planner-boundary-report.md"), RenderBoundaryReport(boundary), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.MemoryInformedPlanProposed, plan.PlanProposalId, "Memory-informed plan was proposed as review evidence.", ["plan-proposal.json", "plan-proposal.md"]);
        RecordEvent(runPath, GovernanceKernelEventKind.PlanRiskReportCreated, risk.PlanRiskReportId, "Plan risk report was created.", ["plan-risks.md"]);
        RecordEvent(runPath, GovernanceKernelEventKind.SuggestedTestProfileCreated, testProfile.SuggestedTestProfileId, "Suggested test profile was created.", ["suggested-test-profile.json"]);
        RecordEvent(runPath, GovernanceKernelEventKind.PlanningBoundaryReportCreated, boundary.PlanningBoundaryReportId, "Planning boundary report was created.", ["planner-boundary-report.json", "planner-boundary-report.md"]);

        if (parsed.Json)
            WriteJson(output, "plan propose", "succeeded", new { runPath, plan, risk, testProfile, boundary }, []);
        else
        {
            output.WriteLine($"Plan proposal: {plan.PlanProposalId}");
            output.WriteLine($"Status: {plan.PlanStatus}");
            output.WriteLine("Boundary: plan proposal is not approval, execution, source apply, memory promotion, workflow continuation, release, merge, or deployment.");
        }

        return 0;
    }

    private static async Task<int> HandleReviewAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "plan review", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var plan = ReadJson<MemoryInformedPlanProposal>(Path.Combine(runPath, "plan-proposal.json"));
        var citations = ReadJson<MemoryCitationBundle>(Path.Combine(runPath, "memory-citation-bundle.json"));
        if (plan is null || citations is null)
            return Failure(output, error, parsed.Json, "plan review", "plan proposal artifacts are missing; run 'irondev plan propose' first.");

        var review = MemoryPlanReviewBuilder.Review(plan, citations);
        await WriteJsonAsync(runPath, "killjoy-plan-review.json", review, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runPath, "killjoy-plan-review.md"), RenderKilljoyReview(review), cancellationToken).ConfigureAwait(false);
        RecordEvent(runPath, GovernanceKernelEventKind.KilljoyPlanReviewCreated, review.KilljoyPlanReviewId, "Killjoy plan review was created.", ["killjoy-plan-review.json", "killjoy-plan-review.md"]);

        if (parsed.Json)
            WriteJson(output, "plan review", "succeeded", new { runPath, review }, []);
        else
        {
            output.WriteLine($"Killjoy plan review: {review.Severity}");
            output.WriteLine("Boundary: Killjoy can complain. Killjoy cannot approve.");
        }

        return 0;
    }

    private static int HandleStatus(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "plan status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!);
        var artifacts = new[]
        {
            "accepted-memory-retrieval-result.json",
            "memory-citation-bundle.json",
            "planner-context-bundle.json",
            "plan-proposal.json",
            "planner-boundary-report.json",
            "killjoy-plan-review.json",
            "governance-events.jsonl"
        }.Select(name => new { name, exists = File.Exists(Path.Combine(runPath, name)) }).ToArray();

        if (parsed.Json)
            WriteJson(output, "plan status", "succeeded", new { runPath, artifacts, boundary = Boundary() }, []);
        else
        {
            output.WriteLine($"Plan artifacts: {runPath}");
            foreach (var artifact in artifacts)
                output.WriteLine($"- {artifact.name}: {(artifact.exists ? "present" : "missing")}");
            output.WriteLine("Boundary: status is read-only.");
        }

        return 0;
    }

    private static ParsedTaskRunCommand ParseTaskRunCommand(string[] args)
    {
        var parsed = ParseRunOnlyCommand(args);
        if (parsed.Error is not null)
            return new ParsedTaskRunCommand(parsed.Run, null, null, null, null, null, [], null, parsed.Json, parsed.Error);

        string? task = null;
        string? projectId = null;
        string? memoryRoot = null;
        string? repoIdentity = null;
        string? baseCommit = null;
        int? maxResults = null;
        var relevantFiles = new List<string>();
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    index++;
                    break;
                case "--task":
                    if (!TryRead(args, ref index, out task)) return ParsedTaskRunCommand.Fail(parsed.Json, "--task requires a value.");
                    break;
                case "--project-id":
                    if (!TryRead(args, ref index, out projectId)) return ParsedTaskRunCommand.Fail(parsed.Json, "--project-id requires a value.");
                    break;
                case "--memory-root":
                    if (!TryRead(args, ref index, out memoryRoot)) return ParsedTaskRunCommand.Fail(parsed.Json, "--memory-root requires a value.");
                    break;
                case "--repo-identity":
                    if (!TryRead(args, ref index, out repoIdentity)) return ParsedTaskRunCommand.Fail(parsed.Json, "--repo-identity requires a value.");
                    break;
                case "--base-commit":
                    if (!TryRead(args, ref index, out baseCommit)) return ParsedTaskRunCommand.Fail(parsed.Json, "--base-commit requires a value.");
                    break;
                case "--relevant-file":
                    if (!TryRead(args, ref index, out var relevantFile)) return ParsedTaskRunCommand.Fail(parsed.Json, "--relevant-file requires a value.");
                    relevantFiles.Add(relevantFile!);
                    break;
                case "--max-results":
                    if (!TryRead(args, ref index, out var maxText) || !int.TryParse(maxText, out var parsedMax)) return ParsedTaskRunCommand.Fail(parsed.Json, "--max-results requires an integer value.");
                    maxResults = parsedMax;
                    break;
                case "--json":
                    break;
                default:
                    return ParsedTaskRunCommand.Fail(parsed.Json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(task)
            ? ParsedTaskRunCommand.Fail(parsed.Json, "--task is required.")
            : new ParsedTaskRunCommand(parsed.Run, task, projectId, memoryRoot, repoIdentity, baseCommit, relevantFiles, maxResults, parsed.Json, null);
    }

    private static ParsedRunOnlyCommand ParseRunOnlyCommand(string[] args)
    {
        string? run = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    if (!TryRead(args, ref index, out run)) return ParsedRunOnlyCommand.Fail(json, "--run requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedRunOnlyCommand.Fail(json, "--run is required.") : new(run, json, null);
    }

    private static async Task<string> ReadTaskSummaryAsync(string taskPath, CancellationToken cancellationToken)
    {
        var text = File.Exists(taskPath)
            ? await File.ReadAllTextAsync(taskPath, cancellationToken).ConfigureAwait(false)
            : taskPath;
        return MemoryContentSafety.SanitiseSummary(text);
    }

    private static void RecordEvent(string runPath, GovernanceKernelEventKind kind, string subjectId, string summary, string[] evidenceRefs) =>
        new FileBackedGovernanceEventStore(runPath).Append(
            runId: Path.GetFileName(runPath),
            actionId: subjectId,
            eventKind: kind,
            subjectKind: "MemoryInformedPlanning",
            subjectId: subjectId,
            summary: summary,
            evidenceRefs: evidenceRefs);

    private static string RenderMemoryContext(AcceptedMemoryRetrievalResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Accepted Memory Context");
        builder.AppendLine();
        builder.AppendLine("Boundary: accepted memory is planning evidence only. It is not approval, execution, memory promotion, source apply, workflow continuation, release, merge, or deployment.");
        builder.AppendLine();
        foreach (var item in result.Items)
            builder.AppendLine($"- `{item.MemoryId}` / `{item.MemoryVersionId}` / `{item.MemoryScope}`: {item.ContentSummary}");
        foreach (var warning in result.Warnings)
            builder.AppendLine($"- Warning: `{warning}`");
        return builder.ToString();
    }

    private static string RenderPlannerContext(PlannerContextBundle context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Planner Context Bundle");
        builder.AppendLine();
        builder.AppendLine($"Run: `{context.RunId}`");
        builder.AppendLine($"Task: {context.TaskSummary}");
        builder.AppendLine($"Memory citations: `{context.AcceptedMemoryRefs.Length}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: context is fuel. It is not the driver.");
        return builder.ToString();
    }

    private static string RenderPlan(MemoryInformedPlanProposal plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Memory-Informed Plan Proposal");
        builder.AppendLine();
        builder.AppendLine($"Plan: `{plan.PlanProposalId}`");
        builder.AppendLine($"Status: `{plan.PlanStatus}`");
        builder.AppendLine();
        foreach (var step in plan.PlanSteps)
            builder.AppendLine($"- `{step.StepKind}`: {step.Description}");
        builder.AppendLine();
        builder.AppendLine("Boundary: a plan is a map, not motion.");
        return builder.ToString();
    }

    private static string RenderRiskReport(PlanRiskReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Plan Risk Report");
        builder.AppendLine();
        foreach (var risk in report.RiskItems)
            builder.AppendLine($"- {risk}");
        foreach (var concern in report.AuthorityBoundaryConcerns)
            builder.AppendLine($"- Boundary: {concern}");
        return builder.ToString();
    }

    private static string RenderBoundaryReport(PlanningBoundaryReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Planning Boundary Report");
        builder.AppendLine();
        foreach (var check in report.BoundaryChecks)
            builder.AppendLine($"- {check}");
        foreach (var claim in report.ForbiddenClaimsFound)
            builder.AppendLine($"- Forbidden claim: `{claim}`");
        return builder.ToString();
    }

    private static string RenderKilljoyReview(KilljoyPlanReview review)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Killjoy Plan Review");
        builder.AppendLine();
        builder.AppendLine($"Severity: `{review.Severity}`");
        foreach (var finding in review.Findings)
            builder.AppendLine($"- {finding}");
        builder.AppendLine();
        builder.AppendLine("Boundary: Killjoy can complain. Killjoy cannot approve.");
        return builder.ToString();
    }

    private static async Task WriteJsonAsync<T>(string runPath, string artifactName, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runPath);
        await File.WriteAllTextAsync(Path.Combine(runPath, artifactName), JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(value, JsonLineOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static T? ReadJson<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;

    private static string ResolveRunPath(string run)
    {
        var candidate = Path.GetFullPath(run.Trim());
        if (Path.IsPathRooted(run) || Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")))
            return candidate;
        return Path.Combine(Path.GetTempPath(), DefaultRunsFolderName, run.Trim());
    }

    private static string DefaultMemoryRoot() => Path.Combine(Path.GetTempPath(), DefaultMemoryFolderName);

    private static bool TryRead(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            return false;
        value = args[++index];
        return true;
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev plan memory-context --run <run-id-or-path> --task <task.md> [--project-id <id>] [--memory-root <path>] [--max-results <n>] [--json]");
        error.WriteLine("  irondev plan context --run <run-id-or-path> --task <task.md> [--repo-identity <id>] [--base-commit <sha>] [--relevant-file <path>] [--json]");
        error.WriteLine("  irondev plan propose --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev plan review --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev plan status --run <run-id-or-path> [--json]");
        return 2;
    }

    private static int Failure(TextWriter output, TextWriter error, bool json, string command, string message)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message]);
        else
            error.WriteLine(message);
        return 1;
    }

    private static void WriteJson(TextWriter output, string command, string status, object? data, string[] errors) =>
        output.WriteLine(JsonSerializer.Serialize(new { ok = errors.Length == 0, command, status, data, errors, boundary = Boundary() }, JsonOptions));

    private static object Boundary() => new
    {
        planningEvidenceOnly = true,
        memoryApproves = false,
        planApproves = false,
        killjoyApproves = false,
        executesPlan = false,
        runsTools = false,
        mutatesWorkspace = false,
        appliesSource = false,
        rollsBackSource = false,
        promotesMemory = false,
        mutatesAcceptedMemory = false,
        continuesWorkflow = false,
        satisfiesPolicy = false,
        releases = false,
        deploys = false,
        merges = false
    };

    private sealed record ParsedRunOnlyCommand(string? Run, bool Json, string? Error)
    {
        public static ParsedRunOnlyCommand Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record ParsedTaskRunCommand(
        string? Run,
        string? TaskPath,
        string? ProjectId,
        string? MemoryRootPath,
        string? RepoIdentity,
        string? BaseCommit,
        List<string> RelevantFiles,
        int? MaxResults,
        bool Json,
        string? Error)
    {
        public static ParsedTaskRunCommand Fail(bool json, string error) => new(null, null, null, null, null, null, [], null, json, error);
    }
}

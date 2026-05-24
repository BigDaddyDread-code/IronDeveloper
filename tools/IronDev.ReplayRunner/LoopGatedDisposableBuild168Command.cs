using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class LoopGatedDisposableBuild168Command
{
    private const string DefaultGoal = "I want build solitaire";
    private const string Boundary = "Loop-gated disposable build only. Docs are run-scoped artefacts; generated app files stay inside the disposable workspace; real repo writes, memory mutation, ticket acceptance, and self-approval remain blocked.";

    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"LoopGatedDisposableBuild168-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var project = ReadOption(args, "--project") ?? "Solitaire";
        var goal = ReadOption(args, "--goal") ?? ReadPositionalText(args, 3);
        if (string.IsNullOrWhiteSpace(goal))
            goal = DefaultGoal;

        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        var evidenceRoot = Path.Combine(runRoot, "evidence");
        Directory.CreateDirectory(evidenceRoot);

        var repoStatusBefore = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var docs = await WriteRunScopedDocsAsync(runRoot, project, goal);
        var plan = await RunPlannerCriticLoopAsync(repoRoot, project, goal, $"{runId}-plan-review");
        var build = await RunChildCommandAsync(
            repoRoot,
            runRoot,
            "builder-repair-loop",
            ["agent", "builder", "repair-loop", "--project", project, "--run-id", $"{runId}-builder", "--json"]);
        var quality = await RunChildCommandAsync(
            repoRoot,
            runRoot,
            "quality-gate",
            ["agent", "quality", "run-gate", "--run-id", $"{runId}-quality", "--json"]);
        var repoStatusAfter = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);

        var result = BuildResult(
            runId,
            project,
            goal,
            docs,
            plan,
            build,
            quality,
            repoStatusBefore,
            repoStatusAfter);

        await WriteReportsAsync(runRoot, result, options);
        Console.WriteLine(JsonSerializer.Serialize(result, options));

        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    public static Task<int> HandleCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var rebased = new List<string> { "build", "disposable", "run" };
        rebased.AddRange(args.Skip(2));
        if (!ContainsOption(rebased, "--project"))
            rebased.AddRange(["--project", "Solitaire"]);
        if (!ContainsOption(rebased, "--goal"))
            rebased.AddRange(["--goal", DefaultGoal]);
        return HandleAsync(rebased.ToArray(), options);
    }

    private static async Task<LoopPlannerResult> RunPlannerCriticLoopAsync(string repoRoot, string project, string goal, string runId)
    {
        var registry = new GovernedToolRegistry(repoRoot);
        var service = new GovernedPlannerCriticLoopService(registry, new EvidenceValidationService());
        var result = await service.RunAsync("IronDev", $"Build disposable {project} from messy prompt: {goal}", runId, "dotnet");
        var outputRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        await service.WriteOutputsAsync(result, outputRoot);
        return new LoopPlannerResult(
            result.Status,
            result.RunId,
            result.TraceId,
            result.Summary,
            result.Recommendation,
            result.Trace.HumanEscalation?.Decision ?? "",
            result.Trace.EvidenceValidation?.Status ?? "",
            outputRoot,
            result.EvidenceRefs.ToArray());
    }

    private static async Task<RunScopedDocs> WriteRunScopedDocsAsync(string runRoot, string project, string goal)
    {
        var docsRoot = Path.Combine(runRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var token = ToDocumentToken(project);
        var intakeId = $"{token}_PRODUCT_SPIKE_INTAKE_168";
        var briefId = $"{token}_DISPOSABLE_BUILD_BRIEF_168";
        var ticketId = $"{token}_DISPOSABLE_BUILD_TICKET_168";
        var intakePath = Path.Combine(docsRoot, $"{intakeId}.md");
        var briefPath = Path.Combine(docsRoot, $"{briefId}.md");
        var ticketPath = Path.Combine(docsRoot, $"{ticketId}.md");

        await File.WriteAllTextAsync(intakePath, BuildIntakeDoc(intakeId, project, goal), Encoding.UTF8);
        await File.WriteAllTextAsync(briefPath, BuildBriefDoc(briefId, project, goal), Encoding.UTF8);
        await File.WriteAllTextAsync(ticketPath, BuildTicketDoc(ticketId, project), Encoding.UTF8);

        return new RunScopedDocs(
            intakeId,
            intakePath,
            briefId,
            briefPath,
            ticketId,
            ticketPath);
    }

    private static LoopGatedBuildResult BuildResult(
        string runId,
        string project,
        string goal,
        RunScopedDocs docs,
        LoopPlannerResult plan,
        ChildCommandResult build,
        ChildCommandResult quality,
        string repoStatusBefore,
        string repoStatusAfter)
    {
        var buildRoot = build.Json;
        var buildTrace = ReadNode(buildRoot, "trace");
        var buildReport = ReadNode(buildRoot, "report");
        var qualityRoot = quality.Json;
        var realRepoMutationCount = repoStatusBefore == repoStatusAfter ? 0 : 1;
        var disposableFilesChanged = ReadInt(buildTrace, "disposableFilesChanged");
        var buildPassed = build.ExitCode == 0 && ReadBool(buildRoot, "passed");
        var qualityPassed = quality.ExitCode == 0 && StringEquals(qualityRoot, "status", "Succeeded");
        var planPassed = StringEquals(plan.Status, "Succeeded");
        var status = planPassed && buildPassed && qualityPassed && realRepoMutationCount == 0
            ? "Succeeded"
            : "Failed";
        var traceId = ReadString(buildRoot, "traceId");

        return new LoopGatedBuildResult
        {
            Command = "build disposable run",
            Status = status,
            RunId = runId,
            TraceId = string.IsNullOrWhiteSpace(traceId) ? plan.TraceId : traceId,
            Project = project,
            Goal = goal,
            Summary = status == "Succeeded"
                ? $"Messy prompt produced run-scoped docs, governed plan evidence, caged {project} repair build, quality evidence, and zero real repo mutation."
                : "Loop-gated disposable build did not complete all gates.",
            Data = new
            {
                route = "ProjectPlanningDiscussion",
                productSpikeCandidate = true,
                normalizedProductName = project,
                targetProduct = project,
                docsCreated = docs,
                plannerCriticLoop = plan,
                builderRun = new
                {
                    runId = ReadString(buildRoot, "runId"),
                    traceId = traceId,
                    passed = buildPassed,
                    reportPath = ReadString(buildRoot, "reportPath"),
                    markdownPath = ReadString(buildRoot, "markdownPath"),
                    recommendation = ReadString(buildReport, "recommendation")
                },
                qualityGate = new
                {
                    passed = qualityPassed,
                    status = ReadString(qualityRoot, "status"),
                    summary = ReadString(qualityRoot, "summary")
                },
                generatedAppContained = disposableFilesChanged > 0 && realRepoMutationCount == 0,
                generatedSolitaireAppContained = string.Equals(project, "Solitaire", StringComparison.OrdinalIgnoreCase) && disposableFilesChanged > 0 && realRepoMutationCount == 0,
                docsAreRunScoped = true,
                memoryMutationBlocked = true,
                ticketAcceptanceBlocked = true
            },
            Mutation = new MutationReport
            {
                RealRepoMutationAllowed = false,
                RealRepoMutationCount = realRepoMutationCount,
                DeveloperWorkingTreeMutationAllowed = false,
                DisposableWorkspaceMutationAllowed = true,
                DisposableWorkspacePath = ReadString(ReadNode(buildTrace, "workspaceMutation"), "workspacePath"),
                DisposableFilesChanged = disposableFilesChanged,
                ForbiddenPathsTouched = []
            },
            Evidence = BuildEvidence(docs, plan, build, quality),
            Warnings = BuildWarnings(plan, build, quality, realRepoMutationCount),
            Errors = status == "Succeeded" ? [] : ["One or more loop-gated build stages failed."],
            Boundary = Boundary,
            ReproCommand = $"build disposable run --project {project} --goal \"{goal}\" --run-id {runId} --json",
            Recommendation = status == "Succeeded" ? "PromoteLater" : "Retry"
        };
    }

    private static List<EvidenceRef> BuildEvidence(
        RunScopedDocs docs,
        LoopPlannerResult plan,
        ChildCommandResult build,
        ChildCommandResult quality) =>
    [
        new("RunScopedDocument", docs.IntakePath, "Messy prompt intake document created for this run."),
        new("RunScopedDocument", docs.BuildBriefPath, "Build brief document created for this run."),
        new("RunScopedDocument", docs.TicketPath, "Draft disposable build ticket document created for this run."),
        new("PlannerCriticTrace", Path.Combine(plan.OutputRoot, "agent-loop-trace.json"), "Planner/Critic loop trace."),
        new("PlannerCriticReport", Path.Combine(plan.OutputRoot, "report.json"), "Planner/Critic loop report."),
        new("BuilderCommandLog", build.LogPath, "Caged builder repair-loop command log."),
        new("QualityCommandLog", quality.LogPath, "Quality/Killjoy command log."),
        new("BuilderTrace", ReadString(build.Json, "tracePath"), "Trace-backed builder repair-loop JSON."),
        new("BuilderReport", ReadString(build.Json, "reportPath"), "Trace-backed builder repair-loop report."),
        new("BuilderMarkdown", ReadString(build.Json, "markdownPath"), "Human-readable builder repair-loop report.")
    ];

    private static List<string> BuildWarnings(
        LoopPlannerResult plan,
        ChildCommandResult build,
        ChildCommandResult quality,
        int realRepoMutationCount)
    {
        var warnings = new List<string>();
        if (!StringEquals(plan.Status, "Succeeded"))
            warnings.Add($"Planner/Critic loop status was {plan.Status}.");
        if (build.ExitCode != 0)
            warnings.Add($"Builder repair loop exited {build.ExitCode}.");
        if (quality.ExitCode != 0)
            warnings.Add($"Quality gate exited {quality.ExitCode}.");
        if (realRepoMutationCount != 0)
            warnings.Add("Repository status changed during the run.");
        return warnings;
    }

    private static async Task WriteReportsAsync(string runRoot, LoopGatedBuildResult result, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(runRoot);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "loop-gated-disposable-build-168-report.json"), JsonSerializer.Serialize(result, options), Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "loop-gated-disposable-build-168-report.md"), ToMarkdown(result), Encoding.UTF8);
    }

    private static string BuildIntakeDoc(string id, string project, string goal) =>
        $"""
        ---
        id: {id}
        project: {project}
        document_type: RunScopedIntake
        authority: Draft
        status: Current
        ---

        # {project} Product Spike Intake 168

        User goal:

        ```text
        {goal}
        ```

        Normalized project: `{project}`.

        Boundary: run-scoped artefact only. This does not mutate accepted project memory.
        """;

    private static string BuildBriefDoc(string id, string project, string goal) =>
        $"""
        ---
        id: {id}
        project: {project}
        document_type: RunScopedBuildBrief
        authority: Draft
        status: Current
        ---

        # {project} Disposable Build Brief 168

        Build a caged {project} vertical slice from the messy prompt `{goal}`.

        The build must pass through the governed Planner/Critic loop and write generated app files only inside the explicit disposable workspace.
        """;

    private static string BuildTicketDoc(string id, string project) =>
        $"""
        ---
        id: {id}
        project: {project}
        document_type: RunScopedTicketDraft
        authority: Draft
        status: Current
        ---

        # {project} Disposable Build Ticket 168

        Acceptance criteria:

        - Create generated {project} files only inside the disposable workspace.
        - Run the trace-backed builder repair loop.
        - Run deterministic quality evidence.
        - Report real repo mutation count zero.
        - Return a final recommendation without self-approval.
        """;

    private static string ToMarkdown(LoopGatedBuildResult result)
    {
        var evidence = string.Join(Environment.NewLine, result.Evidence.Select(item => $"- `{item.Type}`: `{item.Path}`"));
        return $"""
        # Loop-Gated Disposable Build 168

        Status: `{result.Status}`
        Project: `{result.Project}`
        Goal: `{result.Goal}`
        Recommendation: `{result.Recommendation}`

        ## Summary

        {result.Summary}

        ## Mutation

        - Real repo mutation count: `{result.Mutation.RealRepoMutationCount}`
        - Disposable files changed: `{result.Mutation.DisposableFilesChanged}`
        - Disposable workspace: `{result.Mutation.DisposableWorkspacePath}`

        ## Evidence

        {evidence}

        ## Boundary

        {result.Boundary}
        """;
    }

    private static async Task<ChildCommandResult> RunChildCommandAsync(
        string repoRoot,
        string runRoot,
        string logName,
        string[] commandArgs)
    {
        var runnerProject = Path.Combine(repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");
        var logRoot = Path.Combine(runRoot, "logs");
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(logRoot, $"{logName}.log");
        var args = new List<string> { "run", "--no-build", "--project", runnerProject, "--" };
        args.AddRange(commandArgs);

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.WorkingDirectory = repoRoot;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        var started = DateTimeOffset.UtcNow;
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var completed = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(logPath, stdout + Environment.NewLine + stderr, Encoding.UTF8);

        return new ChildCommandResult(
            process.ExitCode,
            "dotnet " + string.Join(" ", args.Select(QuoteIfNeeded)),
            logPath,
            ParseObject(stdout),
            started,
            completed);
    }

    private static JsonObject ParseObject(string output)
    {
        try
        {
            var start = output.IndexOf('{');
            var end = output.LastIndexOf('}');
            if (start < 0 || end <= start)
                return [];
            return JsonNode.Parse(output[start..(end + 1)])?.AsObject() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonObject ReadNode(JsonObject node, string name)
    {
        if (TryGetNode(node, name, out var value) && value is JsonObject obj)
            return obj;
        return [];
    }

    private static string ReadString(JsonObject node, string name) =>
        TryGetNode(node, name, out var value) ? value?.ToString() ?? "" : "";

    private static int ReadInt(JsonObject node, string name) =>
        int.TryParse(ReadString(node, name), out var value) ? value : 0;

    private static bool ReadBool(JsonObject node, string name) =>
        bool.TryParse(ReadString(node, name), out var value) && value;

    private static bool StringEquals(JsonObject node, string name, string expected) =>
        string.Equals(ReadString(node, name), expected, StringComparison.OrdinalIgnoreCase);

    private static bool StringEquals(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static bool ContainsOption(IEnumerable<string> args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetNode(JsonObject node, string name, out JsonNode? value)
    {
        if (node.TryGetPropertyValue(name, out value))
            return true;

        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string ReadPositionalText(string[] args, int startIndex)
    {
        var tokens = new List<string>();
        for (var i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            tokens.Add(args[i]);
        }

        return string.Join(' ', tokens);
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static string ToDocumentToken(string project)
    {
        var builder = new StringBuilder();
        foreach (var character in project.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }

        return builder.Length == 0 ? "PROJECT" : builder.ToString().Trim('_');
    }

    private sealed record RunScopedDocs(
        string IntakeId,
        string IntakePath,
        string BuildBriefId,
        string BuildBriefPath,
        string TicketId,
        string TicketPath);

    private sealed record LoopPlannerResult(
        string Status,
        string RunId,
        string TraceId,
        string Summary,
        string Recommendation,
        string HumanEscalationDecision,
        string EvidenceValidationStatus,
        string OutputRoot,
        IReadOnlyList<string> EvidenceRefs);

    private sealed record ChildCommandResult(
        int ExitCode,
        string Command,
        string LogPath,
        JsonObject Json,
        DateTimeOffset StartedUtc,
        DateTimeOffset CompletedUtc);

    private sealed class LoopGatedBuildResult
    {
        public string Command { get; init; } = "";
        public string Status { get; init; } = "";
        public string RunId { get; init; } = "";
        public string TraceId { get; init; } = "";
        public string Project { get; init; } = "";
        public string Goal { get; init; } = "";
        public string Summary { get; init; } = "";
        public object Data { get; init; } = new();
        public MutationReport Mutation { get; init; } = new();
        public List<EvidenceRef> Evidence { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
        public List<string> Errors { get; init; } = [];
        public string Boundary { get; init; } = "";
        public string ReproCommand { get; init; } = "";
        public string Recommendation { get; init; } = "";
    }

    private sealed class MutationReport
    {
        public bool RealRepoMutationAllowed { get; init; }
        public int RealRepoMutationCount { get; init; }
        public bool DeveloperWorkingTreeMutationAllowed { get; init; }
        public bool DisposableWorkspaceMutationAllowed { get; init; }
        public string DisposableWorkspacePath { get; init; } = "";
        public int DisposableFilesChanged { get; init; }
        public List<string> ForbiddenPathsTouched { get; init; } = [];
    }

    private sealed record EvidenceRef(string Type, string Path, string Summary);
}

using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using IronDev.Core.Tools;
using WorkspaceToolRequest = IronDev.Core.Tools.ToolRequest;

namespace IronDev.Cli;

public static partial class IronDevCliPatchProposal
{
    private const string ToolRequestsArtifactName = "tool-requests.jsonl";
    private const string ToolGateDecisionsArtifactName = "tool-gate-decisions.jsonl";
    private const string ToolResultsArtifactName = "tool-results.jsonl";
    private const string ToolOutputFolderName = "tool-output";

    private static readonly JsonSerializerOptions ToolJsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<int> HandlePatchToolsAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePatchTools(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch tools", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var run = await LoadRunAsync(runPath, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return WriteFailure(output, error, parsed.Json, "patch tools", $"run metadata was not found: {Path.Combine(runPath, "run.json")}");

        var requests = await ReadJsonLinesAsync<WorkspaceToolRequest>(Path.Combine(run.RunPath, ToolRequestsArtifactName), cancellationToken).ConfigureAwait(false);
        var gates = await ReadJsonLinesAsync<WorkspaceToolGateDecision>(Path.Combine(run.RunPath, ToolGateDecisionsArtifactName), cancellationToken).ConfigureAwait(false);
        var results = await ReadJsonLinesAsync<ToolExecutionResult>(Path.Combine(run.RunPath, ToolResultsArtifactName), cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
        {
            WriteJsonEnvelope(output, "patch tools", "succeeded", new
            {
                run.RunId,
                Requests = requests,
                GateDecisions = gates,
                Results = results,
                Artifacts = ToolArtifacts(run),
                boundary = Boundary()
            }, []);
            return 0;
        }

        output.WriteLine($"Patch tool evidence: {run.RunId}");
        output.WriteLine($"Requests: {requests.Length}");
        output.WriteLine($"Gate decisions: {gates.Length}");
        output.WriteLine($"Results: {results.Length}");
        foreach (var result in results)
            output.WriteLine($"- {result.ToolResultId}: executed={result.WasExecuted}; exit={result.ExitCode?.ToString() ?? "n/a"}; command={result.Command}");

        return 0;
    }

    private static async Task<WorkspaceToolRunOutcome> RunWorkspaceToolCommandAsync(
        PatchProposalRunDocument run,
        ToolRequestKind requestKind,
        string command,
        string? profileName,
        CancellationToken cancellationToken)
    {
        var request = CreateToolRequest(run, requestKind, command, profileName);
        await AppendToolJsonLineAsync(run, ToolRequestsArtifactName, request, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.WorkspaceToolRequestCreated,
            "Workspace tool request was created for a patch run.",
            [ToolRequestsArtifactName, "run.json"],
            cancellationToken).ConfigureAwait(false);

        var gate = WorkspaceToolGateEvaluator.Evaluate(request);
        await AppendToolJsonLineAsync(run, ToolGateDecisionsArtifactName, gate, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.WorkspaceToolGateEvaluated,
            gate.Decision == WorkspaceToolGateDecisionOutcome.Allow
                ? "Workspace tool gate allowed the command for disposable workspace execution."
                : "Workspace tool gate blocked the command before execution.",
            [ToolRequestsArtifactName, ToolGateDecisionsArtifactName],
            cancellationToken).ConfigureAwait(false);

        if (gate.Decision == WorkspaceToolGateDecisionOutcome.Block)
        {
            var now = DateTimeOffset.UtcNow;
            var blocked = new ToolExecutionResult
            {
                ToolResultId = $"tool_result_{Guid.NewGuid():N}",
                ToolRequestId = request.ToolRequestId,
                ToolGateDecisionId = gate.ToolGateDecisionId,
                RunId = run.RunId,
                Command = request.ResolvedCommand,
                WorkingDirectory = request.WorkingDirectory,
                StartedAtUtc = now,
                FinishedAtUtc = now,
                ExitCode = null,
                StdoutPath = null,
                StderrPath = null,
                CombinedOutputPath = null,
                SummaryPath = null,
                WasExecuted = false,
                Boundary = ToolCommandBoundary.None
            };

            await AppendToolJsonLineAsync(run, ToolResultsArtifactName, blocked, cancellationToken).ConfigureAwait(false);
            await RecordGovernanceEventAsync(
                run,
                GovernedActionKind.WorkspaceToolResultRecorded,
                "Workspace tool result recorded a blocked command; no command was executed.",
                [ToolRequestsArtifactName, ToolGateDecisionsArtifactName, ToolResultsArtifactName],
                cancellationToken).ConfigureAwait(false);

            return new WorkspaceToolRunOutcome(request, gate, blocked, null);
        }

        var started = DateTimeOffset.UtcNow;
        var processResult = await RunShellAsync(request.ResolvedCommand, request.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var finished = DateTimeOffset.UtcNow;
        var resultId = $"tool_result_{Guid.NewGuid():N}";
        var outputFolder = Path.Combine(run.RunPath, ToolOutputFolderName);
        Directory.CreateDirectory(outputFolder);

        var stdoutRelative = Path.Combine(ToolOutputFolderName, $"{resultId}.stdout.txt");
        var stderrRelative = Path.Combine(ToolOutputFolderName, $"{resultId}.stderr.txt");
        var combinedRelative = Path.Combine(ToolOutputFolderName, $"{resultId}.combined.txt");
        var summaryRelative = Path.Combine(ToolOutputFolderName, $"{resultId}.summary.md");

        await File.WriteAllTextAsync(Path.Combine(run.RunPath, stdoutRelative), processResult.Stdout, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, stderrRelative), processResult.Stderr, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, combinedRelative), RenderCombinedToolOutput(processResult), cancellationToken).ConfigureAwait(false);

        var result = new ToolExecutionResult
        {
            ToolResultId = resultId,
            ToolRequestId = request.ToolRequestId,
            ToolGateDecisionId = gate.ToolGateDecisionId,
            RunId = run.RunId,
            Command = request.ResolvedCommand,
            WorkingDirectory = request.WorkingDirectory,
            StartedAtUtc = started,
            FinishedAtUtc = finished,
            ExitCode = processResult.ExitCode,
            StdoutPath = stdoutRelative,
            StderrPath = stderrRelative,
            CombinedOutputPath = combinedRelative,
            SummaryPath = summaryRelative,
            WasExecuted = true,
            Boundary = ToolCommandBoundary.None
        };

        await File.WriteAllTextAsync(Path.Combine(run.RunPath, summaryRelative), RenderToolResultSummary(request, gate, result), cancellationToken).ConfigureAwait(false);
        await AppendToolJsonLineAsync(run, ToolResultsArtifactName, result, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.WorkspaceCommandExecuted,
            $"Workspace command executed with exit code {processResult.ExitCode}.",
            [ToolRequestsArtifactName, ToolGateDecisionsArtifactName, combinedRelative],
            cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.WorkspaceToolResultRecorded,
            "Workspace tool result evidence was recorded.",
            [ToolResultsArtifactName, summaryRelative],
            cancellationToken).ConfigureAwait(false);

        run.Artifacts = MergeArtifacts(run.Artifacts, ToolArtifacts(run));
        return new WorkspaceToolRunOutcome(request, gate, result, processResult);
    }

    private static WorkspaceToolRequest CreateToolRequest(PatchProposalRunDocument run, ToolRequestKind requestKind, string command, string? profileName)
    {
        var normalizedCommand = command.Trim();
        return new WorkspaceToolRequest
        {
            ToolRequestId = $"tool_req_{Guid.NewGuid():N}",
            RunId = run.RunId,
            RequestKind = requestKind,
            ToolName = "workspace-shell",
            ToolKind = ToolCommandRiskClassifier.DetectKind(normalizedCommand),
            Command = normalizedCommand,
            ResolvedCommand = normalizedCommand,
            WorkingDirectory = NormalizeFullPath(run.WorkspacePath),
            WorkspacePath = NormalizeFullPath(run.WorkspacePath),
            SourceRepoPath = NormalizeFullPath(run.SourceRepoPath),
            RequestedBy = "IronDevCli",
            SourceComponent = "IronDev.Cli.patch",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RiskClassification = ToolCommandRiskClassifier.Classify(normalizedCommand),
            EvidenceRefs =
            [
                new ToolEvidenceRef
                {
                    RefId = "run.json",
                    EvidenceKind = "PatchProposalRun",
                    SafeSummary = string.IsNullOrWhiteSpace(profileName)
                        ? "Patch run command request."
                        : $"Patch run command request for profile {profileName}."
                }
            ],
            Boundary = ToolCommandBoundary.None
        };
    }

    private static void WriteBlockedTestArtifacts(PatchProposalRunDocument run, string testCommand, string? profileName, WorkspaceToolGateDecision gate)
    {
        File.WriteAllText(Path.Combine(run.RunPath, "test-results.txt"), RenderBlockedTestResults(testCommand, profileName, gate));
        File.WriteAllText(Path.Combine(run.RunPath, "test-output-summary.md"), RenderBlockedTestOutputSummary(testCommand, profileName, run.WorkspacePath, gate));
    }

    private static string RenderBlockedTestResults(string testCommand, string? profileName, WorkspaceToolGateDecision gate)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test Results");
        builder.AppendLine();
        builder.AppendLine("Boundary: tests were blocked by the workspace tool gate before command execution. The source repository was not modified.");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(profileName))
            builder.AppendLine($"Profile: `{profileName}`");
        builder.AppendLine($"Command: `{testCommand}`");
        builder.AppendLine("Status: blocked by workspace tool gate");
        builder.AppendLine($"Gate decision: `{gate.ToolGateDecisionId}`");
        builder.AppendLine();
        builder.AppendLine("## Block reasons");
        foreach (var reason in gate.Reasons)
            builder.AppendLine($"- `{reason}`");
        return builder.ToString();
    }

    private static string RenderBlockedTestOutputSummary(string testCommand, string? profileName, string workingDirectory, WorkspaceToolGateDecision gate)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test Output Summary");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(profileName))
            builder.AppendLine($"Profile: `{profileName}`");
        builder.AppendLine($"Command: `{testCommand}`");
        builder.AppendLine($"Working directory: `{workingDirectory}`");
        builder.AppendLine("Status: blocked by workspace tool gate");
        builder.AppendLine($"Gate decision: `{gate.ToolGateDecisionId}`");
        builder.AppendLine();
        builder.AppendLine("No command output exists because the command was not executed.");
        builder.AppendLine();
        builder.AppendLine("Boundary: this summary is diagnostic evidence only. It does not approve, apply, or retry anything.");
        return builder.ToString();
    }

    private static string RenderCombinedToolOutput(ProcessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## stdout");
        builder.AppendLine(result.Stdout.TrimEnd());
        builder.AppendLine();
        builder.AppendLine("## stderr");
        builder.AppendLine(result.Stderr.TrimEnd());
        return builder.ToString();
    }

    private static string RenderToolResultSummary(WorkspaceToolRequest request, WorkspaceToolGateDecision gate, ToolExecutionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Workspace Tool Result");
        builder.AppendLine();
        builder.AppendLine($"Tool request: `{request.ToolRequestId}`");
        builder.AppendLine($"Gate decision: `{gate.ToolGateDecisionId}`");
        builder.AppendLine($"Command: `{request.ResolvedCommand}`");
        builder.AppendLine($"Working directory: `{request.WorkingDirectory}`");
        builder.AppendLine($"Was executed: `{result.WasExecuted}`");
        builder.AppendLine($"Exit code: `{result.ExitCode?.ToString() ?? "n/a"}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this is workspace command evidence only. It does not approve the result, apply source, promote memory, continue workflow, or approve release.");
        return builder.ToString();
    }

    private static async Task AppendToolJsonLineAsync<T>(PatchProposalRunDocument run, string artifactName, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(run.RunPath);
        await File.AppendAllTextAsync(Path.Combine(run.RunPath, artifactName), JsonSerializer.Serialize(value, ToolJsonOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, ToolArtifacts(run));
    }

    private static async Task<T[]> ReadJsonLinesAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, ToolJsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private static string[] ToolArtifacts(PatchProposalRunDocument run) =>
    [
        ToolRequestsArtifactName,
        ToolGateDecisionsArtifactName,
        ToolResultsArtifactName,
        ToolOutputFolderName
    ];

    private static ParsedPatchToolsCommand ParsePatchTools(string[] args)
    {
        string? run = null;
        string? runsRootPath = null;
        var json = HasJson(args);

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out run))
                        return ParsedPatchToolsCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedPatchToolsCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--json":
                    break;
                default:
                    return ParsedPatchToolsCommand.Fail(json, $"unsupported patch tools option: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
            return ParsedPatchToolsCommand.Fail(json, "--run is required.");

        return new ParsedPatchToolsCommand(run, runsRootPath, json, null);
    }

    private sealed record WorkspaceToolRunOutcome(
        WorkspaceToolRequest Request,
        WorkspaceToolGateDecision GateDecision,
        ToolExecutionResult Result,
        ProcessResult? ProcessResult);

    private sealed record ParsedPatchToolsCommand(string? Run, string? RunsRootPath, bool Json, string? Error)
    {
        public static ParsedPatchToolsCommand Fail(bool json, string error) => new(null, null, json, error);
    }
}

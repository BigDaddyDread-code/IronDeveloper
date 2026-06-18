using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static partial class IronDevCliPatchProposal
{
    private const string GovernanceEventsArtifactName = "governance-events.jsonl";
    private static readonly JsonSerializerOptions GovernanceJsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<int> HandlePatchGovernanceAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePatchGovernance(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch governance", parsed.Error);

        if (parsed.Inventory)
        {
            if (parsed.Json)
            {
                WriteJsonEnvelope(output, "patch governance", "succeeded", new { Entries = AuthorityActionInventory.All, Boundary = Boundary() }, []);
                return 0;
            }

            output.WriteLine("Patch governance inventory");
            foreach (var entry in AuthorityActionInventory.All)
                output.WriteLine($"- {entry.ActionKind}: {entry.Classification}; allowed={entry.AllowedInCurrentBlock}; requiresConscience={entry.RequiresConscience}; requiresThoughtLedger={entry.RequiresThoughtLedger}; status={entry.CurrentImplementationStatus}");

            return 0;
        }

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var eventsPath = Path.Combine(runPath, GovernanceEventsArtifactName);
        if (!File.Exists(eventsPath))
            return WriteFailure(output, error, parsed.Json, "patch governance", $"governance event artifact was not found: {eventsPath}");

        var lines = await File.ReadAllLinesAsync(eventsPath, cancellationToken).ConfigureAwait(false);
        if (parsed.Json)
        {
            var events = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JsonSerializer.Deserialize<RunScopedGovernanceEvent>(line, GovernanceJsonOptions)).Where(item => item is not null).ToArray();
            WriteJsonEnvelope(output, "patch governance", "succeeded", new { Run = parsed.Run, Events = events, Boundary = Boundary() }, []);
            return 0;
        }

        output.WriteLine($"Governance events: {eventsPath}");
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var governanceEvent = JsonSerializer.Deserialize<RunScopedGovernanceEvent>(line, GovernanceJsonOptions);
            if (governanceEvent is not null)
                output.WriteLine($"- {governanceEvent.ActionKind}: {governanceEvent.Message}");
        }

        return 0;
    }

    private static async Task RecordPatchRunStartedGovernanceEventAsync(PatchProposalRunDocument run, CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.PatchProposalRunStarted,
            "Patch proposal run was started.",
            ["run.json", "task.md"],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordDisposableWorkspaceCreatedGovernanceEventAsync(PatchProposalRunDocument run, CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.DisposableWorkspaceCreated,
            "Disposable patch workspace was created.",
            ["run.json"],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordPatchPackageGovernanceEventsAsync(
        PatchProposalRunDocument run,
        ScopeEvaluationResult scopeResult,
        bool skipTest,
        bool testsBlockedByToolGate,
        ProcessResult? testResult,
        CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(run, GovernedActionKind.ChangedFilesDetected, "Changed files were detected for review.", ["changed-files.txt", "file-scope-result.md"], cancellationToken).ConfigureAwait(false);
        var testMessage = skipTest
            ? "Workspace tests were explicitly skipped for review."
            : scopeResult.BlockedFiles.Length > 0
                ? "Workspace tests were blocked by file scope before execution."
                : testsBlockedByToolGate
                    ? "Workspace tests were blocked by workspace tool gate before execution."
                    : $"Workspace tests executed with exit code {testResult?.ExitCode ?? -1}.";
        await RecordGovernanceEventAsync(run, GovernedActionKind.WorkspaceTestsExecuted, testMessage, ["test-output-summary.md"], cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(run, GovernedActionKind.PatchArtifactExported, "Patch artifact was exported from the disposable workspace.", ["patch.diff", "changed-files.txt"], cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(run, GovernedActionKind.ReviewPackageCreated, $"Patch review package was created with {scopeResult.ChangedFiles.Length} changed file(s).", ["review-summary.md", "known-risks.md", "patch-risk-summary.md"], cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordWorkspaceTestsExecutedGovernanceEventAsync(PatchProposalRunDocument run, ProcessResult result, CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.WorkspaceTestsExecuted,
            $"Workspace tests executed with exit code {result.ExitCode}.",
            ["test-output-summary.md"],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordPatchRunStatusReadGovernanceEventAsync(PatchProposalRunDocument run, CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.PatchRunStatusRead,
            "Patch run status was inspected.",
            ["run.json"],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordPatchWorkspaceCleanedGovernanceEventAsync(PatchProposalRunDocument run, bool workspaceExisted, CancellationToken cancellationToken)
    {
        await RecordGovernanceEventAsync(
            run,
            GovernedActionKind.PatchWorkspaceCleaned,
            workspaceExisted ? "Disposable patch workspace was cleaned." : "Disposable patch workspace was already missing during cleanup.",
            ["cleanup-summary.md"],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RecordGovernanceEventAsync(
        PatchProposalRunDocument run,
        GovernedActionKind actionKind,
        string message,
        string[] evidenceRefs,
        CancellationToken cancellationToken)
    {
        var action = GovernedAction.Create(
            actionKind,
            subjectKind: "PatchProposalRun",
            subjectId: run.RunId,
            requestedBy: "IronDevCli",
            sourceComponent: "IronDev.Cli.patch",
            runId: run.RunId,
            evidenceRefs: evidenceRefs);

        var governanceEvent = RunScopedGovernanceEvent.FromAction(action, eventType: "ActionRecorded", message);
        Directory.CreateDirectory(run.RunPath);
        var json = JsonSerializer.Serialize(governanceEvent, GovernanceJsonOptions);
        await File.AppendAllTextAsync(Path.Combine(run.RunPath, GovernanceEventsArtifactName), json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, [GovernanceEventsArtifactName]);
    }

    private static ParsedPatchGovernanceCommand ParsePatchGovernance(string[] args)
    {
        string? run = null;
        string? runsRoot = null;
        var inventory = false;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (++index >= args.Length)
                        return ParsedPatchGovernanceCommand.Fail(json, "--run requires a value.");
                    run = args[index];
                    break;
                case "--runs-root":
                    if (++index >= args.Length)
                        return ParsedPatchGovernanceCommand.Fail(json, "--runs-root requires a value.");
                    runsRoot = args[index];
                    break;
                case "--inventory":
                    inventory = true;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedPatchGovernanceCommand.Fail(json, $"unsupported patch governance option: {arg}");
            }
        }

        if (!inventory && string.IsNullOrWhiteSpace(run))
            return ParsedPatchGovernanceCommand.Fail(json, "patch governance requires --run <run-id-or-path> or --inventory.");

        return new ParsedPatchGovernanceCommand(run, runsRoot, inventory, json, null);
    }

    private sealed record ParsedPatchGovernanceCommand(string? Run, string? RunsRootPath, bool Inventory, bool Json, string? Error)
    {
        public static ParsedPatchGovernanceCommand Fail(bool json, string error) => new(null, null, false, json, error);
    }
}

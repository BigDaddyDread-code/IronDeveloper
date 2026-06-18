using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliGovernanceInspection
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsGovernanceCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "governance", StringComparison.OrdinalIgnoreCase);

    public static Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (args.Length < 2)
            return Task.FromResult(WriteUsage(error));

        return args[1].ToLowerInvariant() switch
        {
            "inventory" => Task.FromResult(HandleInventory(args, output, error)),
            "actions" => Task.FromResult(HandleInventory(args, output, error)),
            "classify" => Task.FromResult(HandleClassify(args, output, error)),
            "action-envelope" => Task.FromResult(HandleActionEnvelope(args, output, error)),
            "verify" => Task.FromResult(HandleVerify(args, output, error)),
            "events" => Task.FromResult(HandleEvents(args, output, error)),
            "allow" or "approve" or "execute" or "release" or "deploy" or "merge" => Task.FromResult(WriteFailure(error, $"governance {args[1]} is intentionally unsupported; AJ is inspection/envelope/verification only.")),
            _ => Task.FromResult(WriteFailure(error, $"unsupported governance command: {args[1]}"))
        };
    }

    private static int HandleInventory(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasJson(args);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                command = "governance actions",
                status = "succeeded",
                entries = AuthorityActionInventory.All,
                boundary = Boundary()
            }, JsonOptions));
            return 0;
        }

        output.WriteLine("Governed action inventory");
        output.WriteLine();
        foreach (var entry in AuthorityActionInventory.All)
            output.WriteLine($"- {entry.ActionKind}: {entry.Classification}; allowedInCurrentBlock={entry.AllowedInCurrentBlock}; requiresConscience={entry.RequiresConscience}; requiresThoughtLedger={entry.RequiresThoughtLedger}; status={entry.CurrentImplementationStatus}");

        output.WriteLine();
        output.WriteLine("Authority-bearing actions require the governed-action kernel. Inventory is not permission.");
        return 0;
    }

    private static int HandleClassify(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasJson(args);
        var action = GetOption(args, "--action");
        if (string.IsNullOrWhiteSpace(action))
            return WriteFailure(error, "missing required option: --action <action-kind>");

        var entry = AuthorityActionInventory.Get(action);
        var data = new
        {
            command = "governance classify",
            status = "succeeded",
            requestedActionKind = action,
            registeredActionKind = entry.ActionKind,
            entry.Classification,
            entry.AllowedInCurrentBlock,
            entry.RequiresConscience,
            entry.RequiresThoughtLedger,
            entry.CurrentImplementationStatus,
            executableInCurrentBlock = entry.AllowedInCurrentBlock,
            boundary = Boundary()
        };

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
            return 0;
        }

        output.WriteLine($"{entry.ActionKind}: {entry.Classification}");
        output.WriteLine($"Allowed in current block: {entry.AllowedInCurrentBlock}");
        output.WriteLine($"Requires Conscience: {entry.RequiresConscience}");
        output.WriteLine($"Requires ThoughtLedger: {entry.RequiresThoughtLedger}");
        output.WriteLine($"Implementation status: {entry.CurrentImplementationStatus}");
        output.WriteLine($"Executable in current block: {entry.AllowedInCurrentBlock}");
        return 0;
    }

    private static int HandleActionEnvelope(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasJson(args);
        var kindText = GetOption(args, "--kind");
        var subject = GetOption(args, "--subject");
        var run = GetOption(args, "--run");
        var subjectKind = GetOption(args, "--subject-kind") ?? "GovernedSubject";
        var requestedBy = GetOption(args, "--requested-by") ?? "IronDevCli";
        var inputsRef = GetOption(args, "--inputs-ref") ?? "existing-artifact";

        if (!Enum.TryParse<GovernedActionKind>(kindText, ignoreCase: true, out var kind) || kind == GovernedActionKind.Unknown)
            return WriteFailure(error, "missing or invalid required option: --kind <governed-action-kind>");
        if (string.IsNullOrWhiteSpace(subject))
            return WriteFailure(error, "missing required option: --subject <subject-id>");
        if (string.IsNullOrWhiteSpace(run))
            return WriteFailure(error, "missing required option: --run <run-id-or-path>");

        var envelope = GovernedActionEnvelope.FromInventory(kind, subjectKind, subject, requestedBy, inputsRef);
        var runPath = ResolveRunPath(run);
        GovernanceKernelArtifactWriter.AppendJsonLine(runPath, "governed-actions.jsonl", envelope);
        var evt = new FileBackedGovernanceEventStore(runPath).Append(
            runId: Path.GetFileName(runPath),
            actionId: envelope.ActionId,
            eventKind: GovernanceKernelEventKind.ActionRequested,
            subjectKind: envelope.Subject.SubjectKind,
            subjectId: envelope.Subject.SubjectId,
            summary: $"Governed action envelope created for {kind}.",
            evidenceRefs: envelope.EvidenceRefs.Select(item => item.EvidenceRefId));

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                command = "governance action-envelope",
                status = "succeeded",
                runPath,
                envelope,
                eventRecorded = evt.EventId,
                boundary = Boundary()
            }, JsonOptions));
            return 0;
        }

        output.WriteLine($"Created governed action envelope: {envelope.ActionId}");
        output.WriteLine($"Run path: {runPath}");
        output.WriteLine("Boundary: envelope creation is not approval, permission, execution, release, merge, deployment, source apply, rollback, workflow continuation, or memory promotion.");
        return 0;
    }

    private static int HandleVerify(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasJson(args);
        var run = GetOption(args, "--run");
        if (string.IsNullOrWhiteSpace(run))
            return WriteFailure(error, "missing required option: --run <run-id-or-path>");

        var runPath = ResolveRunPath(run);
        var result = GovernanceKernelArtifactWriter.VerifyAndWrite(runPath);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                command = "governance verify",
                status = result.Passed ? "succeeded" : "blocked",
                runPath,
                result,
                boundary = Boundary()
            }, JsonOptions));
            return result.Passed ? 0 : 1;
        }

        output.WriteLine($"Governance kernel verification: {(result.Passed ? "passed" : "blocked")}");
        output.WriteLine($"Events: {result.EventCount}; Actions: {result.ActionCount}; Gate evidence: {result.GateEvidenceCount}; Conscience: {result.ConscienceDecisionCount}; ThoughtLedger: {result.ThoughtLedgerCount}");
        foreach (var issue in result.Issues)
            output.WriteLine($"- {issue}");
        return result.Passed ? 0 : 1;
    }

    private static int HandleEvents(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasJson(args);
        var run = GetOption(args, "--run");
        if (string.IsNullOrWhiteSpace(run))
            return WriteFailure(error, "missing required option: --run <run-id-or-path>");

        var runPath = ResolveRunPath(run);
        var events = new FileBackedGovernanceEventStore(runPath).ReadAll();
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                command = "governance events",
                status = "succeeded",
                runPath,
                events,
                boundary = Boundary()
            }, JsonOptions));
            return 0;
        }

        output.WriteLine($"Governance events: {runPath}");
        foreach (var evt in events)
            output.WriteLine($"- {evt.EventKind}: {evt.Summary}");
        return 0;
    }

    private static int WriteUsage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  irondev governance actions [--json]");
        error.WriteLine("  irondev governance inventory [--json]");
        error.WriteLine("  irondev governance classify --action <action-kind> [--json]");
        error.WriteLine("  irondev governance action-envelope --kind <kind> --subject <subject-id> --run <run-id-or-path> [--subject-kind <kind>] [--requested-by <actor>] [--inputs-ref <artifact>] [--json]");
        error.WriteLine("  irondev governance verify --run <run-id-or-path> [--json]");
        error.WriteLine("  irondev governance events --run <run-id-or-path> [--json]");
        return 2;
    }

    private static int WriteFailure(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 2;
    }

    private static bool HasJson(string[] args) =>
        HasFlag(args, "--json") || HasOutputJson(args);

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool HasOutputJson(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[index + 1], "json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static string ResolveRunPath(string run)
    {
        var candidate = Path.GetFullPath(run.Trim());
        if (Path.IsPathRooted(run) || Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")))
            return candidate;

        return Path.Combine(Path.GetTempPath(), DefaultRunsFolderName, run.Trim());
    }

    private static object Boundary() => new
    {
        inspectionOnly = true,
        envelopeOnly = true,
        grantsAuthority = false,
        approvesAction = false,
        executesAction = false,
        mutatesSource = false,
        rollsBackSource = false,
        promotesMemory = false,
        continuesWorkflow = false,
        releasesSoftware = false,
        deploysSoftware = false,
        mergesSource = false
    };
}

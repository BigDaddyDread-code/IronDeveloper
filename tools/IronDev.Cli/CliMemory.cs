using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Memory;

namespace IronDev.Cli;

public static class IronDevCliMemory
{
    private const string DefaultRunsFolderName = "irondev-patch-runs";
    private const string DefaultMemoryFolderName = "irondev-memory";
    private const string MemoryProposalsArtifactName = "memory-proposals.jsonl";
    private const string MemoryProposalSummaryArtifactName = "memory-proposal-summary.md";
    private const string MemoryKeyGateResultsArtifactName = "memory-key-gate-results.jsonl";
    private const string MemoryPromotionRequestsArtifactName = "memory-promotion-requests.jsonl";
    private const string MemoryPromotionReceiptsArtifactName = "memory-promotion-receipts.jsonl";
    private const string AcceptedMemoryReceiptsArtifactName = "accepted-memory-receipts.jsonl";
    private const string GovernanceEventsArtifactName = "governance-events.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsMemoryCommand(string[] args) =>
        args.Length >= 1 && string.Equals(args[0], "memory", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "memory requires a subcommand: propose, proposals, promote, list, or show.");

        return args[1].ToLowerInvariant() switch
        {
            "propose" => await HandleProposeAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "proposals" => await HandleProposalsAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "promote" => await HandlePromoteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "list" => HandleList(args, output, error),
            "show" => HandleShow(args, output, error),
            _ => Usage(error, $"unsupported memory subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleProposeAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "memory propose", parsed.Error);

        var loaded = LoadRun(parsed.Run!, parsed.RunsRootPath);
        if (loaded.Run is null)
            return Failure(output, error, parsed.Json, "memory propose", $"run metadata was not found: {Path.Combine(loaded.RunPath, "run.json")}");

        var artifacts = ReadPatchRunArtifacts(loaded.RunPath);
        var hashes = artifacts.ToDictionary(item => item.Key, item => Sha256Hex(Encoding.UTF8.GetBytes(item.Value)), StringComparer.OrdinalIgnoreCase);
        var source = new PatchRunMemorySource
        {
            RunId = loaded.Run.RunId,
            SourceProjectId = ProjectIdFor(loaded.Run.SourceRepoIdentity, loaded.Run.SourceRepoPath),
            SourceRepoPath = loaded.Run.SourceRepoPath,
            SourceRepoIdentity = loaded.Run.SourceRepoIdentity,
            RunPath = loaded.RunPath,
            CreatedBy = "IronDevCli"
        };

        var proposal = MemoryProposalBuilder.BuildFromPatchRun(source, artifacts, hashes);
        var gate = MemoryKeyGate.Evaluate(proposal);

        await AppendJsonLineAsync(Path.Combine(loaded.RunPath, MemoryProposalsArtifactName), proposal, cancellationToken).ConfigureAwait(false);
        await AppendJsonLineAsync(Path.Combine(loaded.RunPath, MemoryKeyGateResultsArtifactName), gate, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(loaded.RunPath, MemoryProposalSummaryArtifactName), RenderProposalSummary(proposal, gate), cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(loaded.RunPath, loaded.Run.RunId, GovernedActionKind.MemoryProposalCreated, proposal.MemoryProposalId, "Memory proposal was created from patch-run evidence.", [MemoryProposalsArtifactName, MemoryProposalSummaryArtifactName], cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(loaded.RunPath, loaded.Run.RunId, GovernedActionKind.MemoryKeyGateEvaluated, proposal.MemoryProposalId, $"Memory key gate returned {gate.Decision}.", [MemoryKeyGateResultsArtifactName], cancellationToken).ConfigureAwait(false);

        var data = new { proposal.MemoryProposalId, proposal.RunId, proposal.ProposedScope, proposal.ProposedKey, gate.Decision, gate.Reasons, artifacts = new[] { MemoryProposalsArtifactName, MemoryProposalSummaryArtifactName, MemoryKeyGateResultsArtifactName } };
        if (parsed.Json)
            WriteJson(output, "memory propose", "succeeded", data, []);
        else
        {
            output.WriteLine($"Memory proposal: {proposal.MemoryProposalId}");
            output.WriteLine($"Key: {proposal.ProposedKey}");
            output.WriteLine($"Gate: {gate.Decision}");
            output.WriteLine("Boundary: proposal evidence only; accepted memory was not written.");
        }

        return 0;
    }

    private static async Task<int> HandleProposalsAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRunCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "memory proposals", parsed.Error);

        var loaded = LoadRun(parsed.Run!, parsed.RunsRootPath);
        if (loaded.Run is null)
            return Failure(output, error, parsed.Json, "memory proposals", $"run metadata was not found: {Path.Combine(loaded.RunPath, "run.json")}");

        var proposals = ReadJsonLines<MemoryProposal>(Path.Combine(loaded.RunPath, MemoryProposalsArtifactName));
        await RecordGovernanceEventAsync(loaded.RunPath, loaded.Run.RunId, GovernedActionKind.MemoryProposalInspected, loaded.Run.RunId, "Memory proposals were inspected.", [MemoryProposalsArtifactName], cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
            WriteJson(output, "memory proposals", "succeeded", new { loaded.Run.RunId, proposals }, []);
        else
        {
            output.WriteLine($"Memory proposals for run {loaded.Run.RunId}: {proposals.Length}");
            foreach (var proposal in proposals)
                output.WriteLine($"- {proposal.MemoryProposalId} {proposal.ProposedKey} ({proposal.ProposedScope})");
            output.WriteLine("Boundary: inspection only; no memory was accepted.");
        }

        return 0;
    }

    private static async Task<int> HandlePromoteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePromoteCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "memory promote", parsed.Error);

        var resolved = ResolveProposal(parsed.Proposal!, parsed.RunsRootPath);
        if (resolved.Proposal is null)
            return Failure(output, error, parsed.Json, "memory promote", $"memory proposal was not found: {parsed.Proposal}");

        var proposal = ApplyScopeOverride(resolved.Proposal, parsed.ScopeOverride);
        var request = new MemoryPromotionRequest
        {
            MemoryPromotionRequestId = $"mem_promo_req_{Guid.NewGuid():N}",
            MemoryProposalId = proposal.MemoryProposalId,
            ProposedScope = proposal.ProposedScope,
            ProposedKey = proposal.ProposedKey,
            RequestedBy = "IronDevCli",
            ConscienceDecisionRef = parsed.ConscienceDecisionPath,
            ThoughtLedgerRef = parsed.ThoughtLedgerRef,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryBoundary.None
        };

        ConscienceDecision? decision = null;
        if (!string.IsNullOrWhiteSpace(parsed.ConscienceDecisionPath) && File.Exists(parsed.ConscienceDecisionPath))
            decision = JsonSerializer.Deserialize<ConscienceDecision>(await File.ReadAllTextAsync(parsed.ConscienceDecisionPath, cancellationToken).ConfigureAwait(false), JsonOptions);

        var memoryRoot = Path.GetFullPath(parsed.MemoryRootPath ?? DefaultMemoryRoot());
        var store = new AcceptedMemoryStore(memoryRoot);

        await AppendJsonLineAsync(Path.Combine(resolved.RunPath, MemoryPromotionRequestsArtifactName), request, cancellationToken).ConfigureAwait(false);
        await RecordGovernanceEventAsync(resolved.RunPath, proposal.RunId, GovernedActionKind.MemoryPromotionRequested, proposal.MemoryProposalId, "Memory promotion was requested for governed review.", [MemoryPromotionRequestsArtifactName, MemoryProposalsArtifactName], cancellationToken).ConfigureAwait(false);

        var result = MemoryPromotionEvaluator.EvaluateAndPromote(proposal, request, decision, store);
        await AppendJsonLineAsync(Path.Combine(resolved.RunPath, MemoryPromotionReceiptsArtifactName), result.Receipt, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(memoryRoot);
        await AppendJsonLineAsync(Path.Combine(memoryRoot, AcceptedMemoryReceiptsArtifactName), result.Receipt, cancellationToken).ConfigureAwait(false);

        if (result.Receipt.Decision == MemoryPromotionDecision.Accepted)
        {
            await RecordGovernanceEventAsync(resolved.RunPath, proposal.RunId, GovernedActionKind.MemoryPromotionAccepted, proposal.MemoryProposalId, "Memory promotion was accepted after key gate, Conscience, and ThoughtLedger evidence.", [MemoryPromotionReceiptsArtifactName], cancellationToken, new GovernedActionBoundary { MemoryPromoted = true }).ConfigureAwait(false);
            await RecordGovernanceEventAsync(resolved.RunPath, proposal.RunId, GovernedActionKind.AcceptedMemoryVersionAppended, result.Record!.MemoryId, "Accepted memory version was appended.", [MemoryPromotionReceiptsArtifactName], cancellationToken, new GovernedActionBoundary { MemoryPromoted = true, AcceptedMemoryVersionAppended = true }).ConfigureAwait(false);
        }
        else
        {
            await RecordGovernanceEventAsync(resolved.RunPath, proposal.RunId, GovernedActionKind.MemoryPromotionBlocked, proposal.MemoryProposalId, "Memory promotion was blocked before accepted-memory append.", [MemoryPromotionReceiptsArtifactName], cancellationToken).ConfigureAwait(false);
        }

        var data = new { result.Receipt, result.Record, result.Version, memoryRoot };
        if (parsed.Json)
            WriteJson(output, "memory promote", result.Receipt.Decision == MemoryPromotionDecision.Accepted ? "succeeded" : "blocked", data, []);
        else
        {
            output.WriteLine($"Memory promotion: {result.Receipt.Decision}");
            if (result.Receipt.Reasons.Length > 0)
                output.WriteLine($"Reasons: {string.Join(", ", result.Receipt.Reasons)}");
            if (result.Record is not null)
                output.WriteLine($"Accepted memory: {result.Record.MemoryId} v{result.Record.CurrentVersion}");
            output.WriteLine("Boundary: accepted memory is evidence only, not approval or policy satisfaction.");
        }

        return result.Receipt.Decision == MemoryPromotionDecision.Accepted ? 0 : 1;
    }

    private static int HandleList(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseMemoryRootCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "memory list", parsed.Error);

        var store = new AcceptedMemoryStore(parsed.MemoryRootPath ?? DefaultMemoryRoot());
        var records = store.List();
        if (parsed.Json)
            WriteJson(output, "memory list", "succeeded", new { records }, []);
        else
        {
            output.WriteLine($"Accepted memory records: {records.Length}");
            foreach (var record in records)
                output.WriteLine($"- {record.Key} {record.MemoryId} v{record.CurrentVersion}");
            output.WriteLine("Boundary: read-only; accepted memory is not authority.");
        }

        return 0;
    }

    private static int HandleShow(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseShowCommand(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "memory show", parsed.Error);

        var store = new AcceptedMemoryStore(parsed.MemoryRootPath ?? DefaultMemoryRoot());
        var record = store.GetByKey(parsed.Key!);
        var versions = record is null ? [] : store.Versions(record.MemoryId);
        if (record is null)
            return Failure(output, error, parsed.Json, "memory show", $"accepted memory key was not found: {parsed.Key}");

        if (parsed.Json)
            WriteJson(output, "memory show", "succeeded", new { record, versions }, []);
        else
        {
            output.WriteLine($"Accepted memory: {record.Key}");
            output.WriteLine($"Current version: {record.CurrentVersion}");
            foreach (var version in versions)
                output.WriteLine($"- v{version.Version}: {version.SanitisedContent}");
            output.WriteLine("Boundary: read-only; this does not approve action.");
        }

        return 0;
    }

    private static MemoryProposal ApplyScopeOverride(MemoryProposal proposal, MemoryScope? scope)
    {
        if (scope is null || scope == proposal.ProposedScope)
            return proposal;

        var source = new PatchRunMemorySource
        {
            RunId = proposal.RunId,
            SourceProjectId = proposal.SourceProjectId,
            SourceRepoPath = proposal.SourceRepoPath,
            SourceRepoIdentity = proposal.SourceRepoIdentity,
            RunPath = proposal.SourceRunPath ?? string.Empty,
            CreatedBy = proposal.CreatedBy
        };

        var key = scope == MemoryScope.PortableEngineering
            ? MemoryKeyNormalizer.BuildPortableEngineeringKey(proposal.Title)
            : scope == MemoryScope.Run
                ? MemoryKeyNormalizer.BuildRunKey(proposal.RunId, proposal.Title)
                : MemoryKeyNormalizer.BuildProjectKey(proposal.SourceProjectId, proposal.Title);

        return proposal with
        {
            ProposedScope = scope.Value,
            ProposedKey = key,
            SafetyFlags = MemoryContentSafety.Flags(proposal.Content, scope.Value, source)
        };
    }

    private static (PatchRunForMemory? Run, string RunPath) LoadRun(string run, string? runsRoot)
    {
        var runPath = ResolveRunPath(run, runsRoot);
        var path = Path.Combine(runPath, "run.json");
        if (!File.Exists(path))
            return (null, runPath);

        return (JsonSerializer.Deserialize<PatchRunForMemory>(File.ReadAllText(path), JsonOptions), runPath);
    }

    private static Dictionary<string, string> ReadPatchRunArtifacts(string runPath)
    {
        var names = new[]
        {
            "run.json",
            "review-summary.md",
            "known-risks.md",
            "test-output-summary.md",
            "tool-results.jsonl",
            "governance-events.jsonl",
            "ai-assist-summary.md",
            "ai-review.md",
            "model-responses.jsonl",
            "changed-files.txt",
            "patch.diff",
            "manual-apply-instructions.md"
        };

        var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var path = Path.Combine(runPath, name);
            if (File.Exists(path))
                artifacts[name] = File.ReadAllText(path);
        }

        return artifacts;
    }

    private static (MemoryProposal? Proposal, string RunPath) ResolveProposal(string proposal, string? runsRoot)
    {
        if (File.Exists(proposal))
        {
            var item = JsonSerializer.Deserialize<MemoryProposal>(File.ReadAllText(proposal), JsonOptions);
            return (item, Path.GetDirectoryName(Path.GetFullPath(proposal)) ?? Directory.GetCurrentDirectory());
        }

        if (Directory.Exists(proposal))
        {
            var file = Path.Combine(proposal, MemoryProposalsArtifactName);
            return (ReadJsonLines<MemoryProposal>(file).LastOrDefault(), Path.GetFullPath(proposal));
        }

        var root = Path.GetFullPath(runsRoot ?? DefaultRunsRoot());
        if (!Directory.Exists(root))
            return (null, root);

        foreach (var file in Directory.EnumerateFiles(root, MemoryProposalsArtifactName, SearchOption.AllDirectories))
        {
            foreach (var item in ReadJsonLines<MemoryProposal>(file))
            {
                if (string.Equals(item.MemoryProposalId, proposal, StringComparison.OrdinalIgnoreCase))
                    return (item, Path.GetDirectoryName(file) ?? root);
            }
        }

        return (null, root);
    }

    private static async Task RecordGovernanceEventAsync(string runPath, string runId, GovernedActionKind actionKind, string subjectId, string message, string[] evidenceRefs, CancellationToken cancellationToken, GovernedActionBoundary? boundary = null)
    {
        var action = GovernedAction.Create(actionKind, "Memory", subjectId, "IronDevCli", "IronDev.Cli.memory", runId, evidenceRefs: evidenceRefs) with
        {
            Boundary = boundary ?? GovernedActionBoundary.None
        };
        var evt = RunScopedGovernanceEvent.FromAction(action, "ActionRecorded", message);
        await AppendJsonLineAsync(Path.Combine(runPath, GovernanceEventsArtifactName), evt, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderProposalSummary(MemoryProposal proposal, MemoryKeyGateResult gate)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Memory Proposal Summary");
        builder.AppendLine();
        builder.AppendLine($"Proposal: `{proposal.MemoryProposalId}`");
        builder.AppendLine($"Run: `{proposal.RunId}`");
        builder.AppendLine($"Scope: `{proposal.ProposedScope}`");
        builder.AppendLine($"Key: `{proposal.ProposedKey}`");
        builder.AppendLine($"Gate: `{gate.Decision}`");
        if (gate.Reasons.Length > 0)
            builder.AppendLine($"Gate reasons: {string.Join(", ", gate.Reasons)}");
        builder.AppendLine();
        builder.AppendLine(proposal.Summary);
        builder.AppendLine();
        builder.AppendLine("Boundary: memory proposal is evidence only. It is not accepted memory, approval, policy satisfaction, workflow continuation, release readiness, source apply authority, or memory promotion.");
        return builder.ToString();
    }

    private static ParsedRunCommand ParseRunCommand(string[] args)
    {
        string? run = null;
        string? runsRoot = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    if (!TryRead(args, ref index, out run)) return ParsedRunCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryRead(args, ref index, out runsRoot)) return ParsedRunCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedRunCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(run) ? ParsedRunCommand.Fail(json, "--run is required.") : new(run, runsRoot, json, null);
    }

    private static ParsedPromoteCommand ParsePromoteCommand(string[] args)
    {
        string? proposal = null;
        string? conscience = null;
        string? thoughtLedger = null;
        string? memoryRoot = null;
        string? runsRoot = null;
        MemoryScope? scope = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--proposal":
                    if (!TryRead(args, ref index, out proposal)) return ParsedPromoteCommand.Fail(json, "--proposal requires a value.");
                    break;
                case "--conscience-decision":
                    if (!TryRead(args, ref index, out conscience)) return ParsedPromoteCommand.Fail(json, "--conscience-decision requires a value.");
                    break;
                case "--thought-ledger-ref":
                    if (!TryRead(args, ref index, out thoughtLedger)) return ParsedPromoteCommand.Fail(json, "--thought-ledger-ref requires a value.");
                    break;
                case "--memory-root":
                    if (!TryRead(args, ref index, out memoryRoot)) return ParsedPromoteCommand.Fail(json, "--memory-root requires a value.");
                    break;
                case "--runs-root":
                    if (!TryRead(args, ref index, out runsRoot)) return ParsedPromoteCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--scope":
                    if (!TryRead(args, ref index, out var scopeText)) return ParsedPromoteCommand.Fail(json, "--scope requires a value.");
                    scope = ParseScope(scopeText);
                    if (scope is null) return ParsedPromoteCommand.Fail(json, "--scope must be run, project, or portable.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedPromoteCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(proposal) ? ParsedPromoteCommand.Fail(json, "--proposal is required.") : new(proposal, conscience, thoughtLedger, memoryRoot, runsRoot, scope, json, null);
    }

    private static ParsedMemoryRootCommand ParseMemoryRootCommand(string[] args)
    {
        string? memoryRoot = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--memory-root":
                    if (!TryRead(args, ref index, out memoryRoot)) return ParsedMemoryRootCommand.Fail(json, "--memory-root requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedMemoryRootCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return new(memoryRoot, json, null);
    }

    private static ParsedShowCommand ParseShowCommand(string[] args)
    {
        string? key = null;
        string? memoryRoot = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--key":
                    if (!TryRead(args, ref index, out key)) return ParsedShowCommand.Fail(json, "--key requires a value.");
                    break;
                case "--memory-root":
                    if (!TryRead(args, ref index, out memoryRoot)) return ParsedShowCommand.Fail(json, "--memory-root requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedShowCommand.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(key) ? ParsedShowCommand.Fail(json, "--key is required.") : new(key, memoryRoot, json, null);
    }

    private static MemoryScope? ParseScope(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "run" => MemoryScope.Run,
        "project" => MemoryScope.Project,
        "portable" or "portableengineering" or "portable-engineering" => MemoryScope.PortableEngineering,
        _ => null
    };

    private static bool TryRead(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            return false;
        value = args[++index];
        return true;
    }

    private static T[] ReadJsonLines<T>(string path)
    {
        if (!File.Exists(path))
            return [];

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private static async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory());
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(value, JsonLineOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveRunPath(string run, string? runsRootPath)
    {
        var candidate = Path.GetFullPath(run);
        if (Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")) || Path.IsPathRooted(run))
            return candidate;

        return Path.Combine(Path.GetFullPath(runsRootPath ?? DefaultRunsRoot()), run.Trim());
    }

    private static string DefaultRunsRoot() => Path.Combine(Path.GetTempPath(), DefaultRunsFolderName);

    private static string DefaultMemoryRoot() => Path.Combine(Path.GetTempPath(), DefaultMemoryFolderName);

    private static string ProjectIdFor(string identity, string repoPath) => $"project-{Sha256Hex(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(identity) ? repoPath : identity))[..12]}";

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev memory propose --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev memory proposals --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev memory promote --proposal <proposal-id> --conscience-decision <decision.json> --thought-ledger-ref <ref> [--memory-root <path>] [--runs-root <path>] [--json]");
        error.WriteLine("  irondev memory list [--memory-root <path>] [--json]");
        error.WriteLine("  irondev memory show --key <memory-key> [--memory-root <path>] [--json]");
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
        output.WriteLine(JsonSerializer.Serialize(new { ok = errors.Length == 0, command, status, data, errors }, JsonOptions));

    private sealed record ParsedRunCommand(string? Run, string? RunsRootPath, bool Json, string? Error)
    {
        public static ParsedRunCommand Fail(bool json, string error) => new(null, null, json, error);
    }

    private sealed record ParsedPromoteCommand(string? Proposal, string? ConscienceDecisionPath, string? ThoughtLedgerRef, string? MemoryRootPath, string? RunsRootPath, MemoryScope? ScopeOverride, bool Json, string? Error)
    {
        public static ParsedPromoteCommand Fail(bool json, string error) => new(null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedMemoryRootCommand(string? MemoryRootPath, bool Json, string? Error)
    {
        public static ParsedMemoryRootCommand Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record ParsedShowCommand(string? Key, string? MemoryRootPath, bool Json, string? Error)
    {
        public static ParsedShowCommand Fail(bool json, string error) => new(null, null, json, error);
    }

    private sealed class PatchRunForMemory
    {
        public string RunId { get; set; } = string.Empty;
        public string SourceRepoPath { get; set; } = string.Empty;
        public string SourceRepoIdentity { get; set; } = string.Empty;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliFeedbackPatch
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "apply",
        "commit",
        "push",
        "update-pr",
        "ready",
        "request-reviewers",
        "approve",
        "merge",
        "release",
        "deploy",
        "continue",
        "continue-workflow"
    ];

    public static bool IsFeedbackPatchCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "feedback-patch", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "feedback-patch requires a subcommand: propose, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"feedback-patch {args[1]} is intentionally unsupported; Block AQ only writes patch proposal evidence.");

        return subcommand switch
        {
            "propose" => await HandleProposeAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported feedback-patch subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleProposeAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePropose(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "feedback-patch propose", parsed.Error);

        var packagePath = Path.GetFullPath(parsed.PackagePath!);
        if (!File.Exists(packagePath))
            return Failure(output, error, parsed.Json, "feedback-patch propose", $"feedback remediation package not found: {packagePath}");

        var package = ReadJson<FeedbackRemediationPackage>(packagePath);
        if (package is null)
            return Failure(output, error, parsed.Json, "feedback-patch propose", "feedback remediation package is invalid.");

        var artifacts = FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput
        {
            Package = package,
            ExpectedPrNumber = parsed.PullRequestNumber,
            ExpectedHeadSha = parsed.HeadSha,
            BaseSha = parsed.BaseSha,
            SelectedRemediationIds = parsed.SelectedRemediationIds.ToArray()
        });
        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Proposal);

        if (parsed.Json)
            WriteJson(output, "feedback-patch propose", artifacts.Proposal.Verdict == FeedbackPatchProposalVerdict.Rejected ? "rejected" : "succeeded", new { outDirectory, artifacts.Proposal, artifacts.Receipt }, artifacts.Proposal.CannotApplyReasons);
        else
        {
            output.WriteLine($"Feedback patch proposal: {artifacts.Proposal.PatchProposalId}");
            output.WriteLine($"Verdict: {artifacts.Proposal.Verdict}");
            output.WriteLine("Boundary: proposal evidence does not apply source, commit, push, update PRs, or continue workflow.");
        }

        return artifacts.Proposal.Verdict == FeedbackPatchProposalVerdict.Rejected ? 1 : 0;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"feedback-patch {mode}", parsed.Error);

        var proposal = ReadJson<FeedbackPatchProposal>(ResolveProposalPath(parsed.ProposalPath!));
        if (proposal is null)
            return Failure(output, error, parsed.Json, $"feedback-patch {mode}", "feedback patch proposal is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { proposal.ProposedFiles, proposal.ProposedHunks, proposal.CannotApplyReasons, proposal.IncompleteReasons }
                : new { proposal.PatchProposalId, proposal.SourcePackageId, proposal.Verdict, proposal.TargetPrNumber, proposal.TargetHeadSha, proposal.ExpectedChangedFiles, proposal.ExpectedValidationLanes, proposal.Boundary };
            WriteJson(output, $"feedback-patch {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            foreach (var file in proposal.ProposedFiles)
                output.WriteLine($"{file.FilePath}: {string.Join(", ", file.RemediationCandidateIds)}");
        }
        else
        {
            output.WriteLine($"Feedback patch proposal: {proposal.PatchProposalId}");
            output.WriteLine($"Verdict: {proposal.Verdict}");
            output.WriteLine($"Expected files: {string.Join(", ", proposal.ExpectedChangedFiles.DefaultIfEmpty("none"))}");
            output.WriteLine("Boundary: proposal evidence grants no source or PR mutation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, FeedbackPatchProposalArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "feedback-patch-proposal.json"), JsonSerializer.Serialize(artifacts.Proposal, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "feedback-patch-proposal-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "feedback-patch-proposal-notes.md"), FeedbackPatchProposalBuilder.RenderManualReviewProposal(artifacts.Proposal), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "feedback-patch-proposal-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "feedback-patch-proposal-hunks.jsonl"), artifacts.Proposal.ProposedHunks.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(FeedbackPatchProposalArtifacts artifacts) => $"""
        # Feedback Patch Proposal

        Verdict: `{artifacts.Proposal.Verdict}`
        Source package: `{artifacts.Proposal.SourcePackageId}`
        Proposed files:
        {RenderBullets(artifacts.Proposal.ProposedFiles.Select(item => $"{item.FilePath}: {string.Join(", ", item.RemediationCandidateIds)}"))}
        Cannot apply reasons:
        {RenderBullets(artifacts.Proposal.CannotApplyReasons)}
        Incomplete reasons:
        {RenderBullets(artifacts.Proposal.IncompleteReasons)}

        Boundary: AQ produces manual-review patch proposal evidence only. It does not write fake diff artifacts, apply source changes, commit, push, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, FeedbackPatchProposal proposal) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            proposal.SourcePackageId,
            proposal.PatchProposalId,
            GovernanceKernelEventKind.FeedbackPatchProposalCreated,
            "FeedbackPatchProposal",
            proposal.PatchProposalId,
            "Feedback patch proposal was created.",
            ["feedback-patch-proposal.json", "feedback-patch-proposal-receipt.json"]);

    private static ParsedPropose ParsePropose(string[] args)
    {
        string? package = null;
        string? outPath = null;
        string? head = null;
        string? baseSha = null;
        int? pr = null;
        var selected = new List<string>();
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--package": if (!TryRead(args, ref index, out package)) return ParsedPropose.Fail(json, "--package requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPropose.Fail(json, "--out requires a value."); break;
                case "--head": if (!TryRead(args, ref index, out head)) return ParsedPropose.Fail(json, "--head requires a value."); break;
                case "--base": if (!TryRead(args, ref index, out baseSha)) return ParsedPropose.Fail(json, "--base requires a value."); break;
                case "--candidate": if (!TryRead(args, ref index, out var candidate)) return ParsedPropose.Fail(json, "--candidate requires a value."); selected.Add(candidate); break;
                case "--pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedPropose.Fail(json, "--pr requires a number.");
                    pr = parsedPr;
                    break;
                case "--json": json = true; break;
                default: return ParsedPropose.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package)) return ParsedPropose.Fail(json, "Missing required option: --package <feedback-package.json>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPropose.Fail(json, "Missing required option: --out <path>.");
        return new ParsedPropose(package, outPath, pr, head, baseSha, selected, json, null);
    }

    private static ParsedRead ParseRead(string[] args)
    {
        string? proposal = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--proposal": if (!TryRead(args, ref index, out proposal)) return ParsedRead.Fail(json, "--proposal requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(proposal)
            ? ParsedRead.Fail(json, "Missing required option: --proposal <feedback-patch-proposal.json>.")
            : new ParsedRead(proposal, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ResolveOutputDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.HasExtension(fullPath) && string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory()
            : fullPath;
    }

    private static string ResolveProposalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "feedback-patch-proposal.json")
            : fullPath;
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;
        }
        catch
        {
            return default;
        }
    }

    private static string RenderBullets(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "- none" : string.Join(Environment.NewLine, items.Select(item => $"- {item}"));
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev feedback-patch propose --package <feedback-package.json> --out <path> [--pr <number>] [--head <sha>] [--base <sha>] [--candidate <id>] [--json]");
        error.WriteLine("  irondev feedback-patch inspect --proposal <proposal.json> [--json]");
        error.WriteLine("  irondev feedback-patch status --proposal <proposal.json> [--json]");
        error.WriteLine("  irondev feedback-patch records --proposal <proposal.json> [--json]");
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

    private static void WriteJson(TextWriter output, string command, string status, object? data, string[] errors)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = errors.Length == 0 && status != "failed" && status != "rejected",
            command,
            status,
            data,
            errors,
            boundary = FeedbackPatchProposalBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPropose(string? PackagePath, string? OutPath, int? PullRequestNumber, string? HeadSha, string? BaseSha, List<string> SelectedRemediationIds, bool Json, string? Error)
    {
        public static ParsedPropose Fail(bool json, string error) => new(null, null, null, null, null, [], json, error);
    }

    private sealed record ParsedRead(string? ProposalPath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}

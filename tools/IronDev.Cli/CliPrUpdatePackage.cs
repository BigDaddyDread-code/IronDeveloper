using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static class IronDevCliPrUpdatePackage
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
        "execute",
        "update-branch",
        "ready",
        "mark-ready",
        "request-reviewers",
        "approve",
        "merge",
        "release",
        "deploy",
        "tag",
        "publish",
        "continue",
        "continue-workflow"
    ];

    public static bool IsPrUpdateCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "pr-update", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "pr-update requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"pr-update {args[1]} is intentionally unsupported; Block AR only writes PR update package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported pr-update subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pr-update package", parsed.Error);

        var proposal = ReadJson<FeedbackPatchProposal>(Path.GetFullPath(parsed.ProposalPath!));
        if (proposal is null)
            return Failure(output, error, parsed.Json, "pr-update package", "feedback patch proposal is missing or invalid.");

        var validationReceipts = new List<ValidationRunReceipt>();
        foreach (var validationPath in parsed.ValidationPaths)
        {
            var receipt = ReadJson<ValidationRunReceipt>(Path.GetFullPath(validationPath));
            if (receipt is null)
                return Failure(output, error, parsed.Json, "pr-update package", $"validation receipt is missing or invalid: {validationPath}");
            validationReceipts.Add(receipt);
        }

        PrUpdateSourceApplyEvidence? sourceApplyEvidence = null;
        if (!string.IsNullOrWhiteSpace(parsed.SourceApplyPath))
        {
            var sourceApply = ReadJson<SourceApplyReceipt>(Path.GetFullPath(parsed.SourceApplyPath));
            if (sourceApply is null)
                return Failure(output, error, parsed.Json, "pr-update package", $"source apply receipt is missing or invalid: {parsed.SourceApplyPath}");
            sourceApplyEvidence = ControlledPrUpdatePackageBuilder.FromSourceApplyReceipt(sourceApply);
        }

        var target = new PrUpdateTarget
        {
            Repository = parsed.Repository!,
            PrNumber = parsed.PullRequestNumber!.Value,
            PrUrl = parsed.PullRequestUrl!,
            PrState = parsed.PullRequestState!,
            PrDraftState = parsed.PullRequestDraft!.Value,
            TargetBranch = parsed.TargetBranch!,
            ExpectedCurrentHeadSha = parsed.ExpectedHeadSha!,
            BaseBranch = parsed.BaseBranch!,
            BaseSha = parsed.BaseSha!,
            PackageCreatedAtUtc = DateTimeOffset.UtcNow,
            PackageCreatedBy = parsed.CreatedBy ?? Environment.UserName
        };
        var artifacts = ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = validationReceipts.ToArray(),
            SourceApplyEvidence = sourceApplyEvidence,
            SourceApplyPending = sourceApplyEvidence is null,
            ExpectedPostUpdateHeadSha = parsed.ExpectedPostUpdateHeadSha,
            ExpectedCommitMessage = parsed.CommitMessage,
            ExpectedCommitBody = parsed.CommitBody
        });
        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(output, "pr-update package", artifacts.Package.Verdict is PrUpdatePackageVerdict.PackageRejected or PrUpdatePackageVerdict.PackageBlocked ? "blocked" : "succeeded", new { outDirectory, artifacts.Package, artifacts.Receipt }, artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"PR update package: {artifacts.Package.PrUpdatePackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.Verdict}");
            output.WriteLine($"Execution eligibility: {artifacts.Package.ExecutionEligibility}");
            output.WriteLine("Boundary: package evidence does not apply patches, commit, push, update PRs, or continue workflow.");
        }

        return artifacts.Package.Verdict is PrUpdatePackageVerdict.PackageRejected or PrUpdatePackageVerdict.PackageBlocked ? 1 : 0;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"pr-update {mode}", parsed.Error);

        var package = ReadJson<ControlledPrUpdatePackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"pr-update {mode}", "PR update package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.ValidationEvidence, package.RollbackPlan, package.PackageIssues, package.MissingValidationFamilies }
                : new { package.PrUpdatePackageId, package.PatchProposalId, package.Target, package.ExpectedState, package.Verdict, package.ExecutionEligibility, package.CanExecuteBranchUpdate, package.Boundary };
            WriteJson(output, $"pr-update {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Target: {package.Target.Repository}#{package.Target.PrNumber} {package.Target.TargetBranch}");
            output.WriteLine($"Validation receipts: {package.ValidationEvidence.Length}");
            output.WriteLine($"Rollback available: {package.RollbackPlan.RollbackAvailable}");
        }
        else
        {
            output.WriteLine($"PR update package: {package.PrUpdatePackageId}");
            output.WriteLine($"Verdict: {package.Verdict}");
            output.WriteLine($"Execution eligibility: {package.ExecutionEligibility}");
            output.WriteLine("Boundary: package evidence grants no source or PR mutation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, ControlledPrUpdatePackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "pr-update-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "pr-update-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "pr-update-package-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "pr-update-validation-records.jsonl"), artifacts.Package.ValidationEvidence.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(ControlledPrUpdatePackageArtifacts artifacts) => $"""
        # Controlled PR Update Package

        Verdict: `{artifacts.Package.Verdict}`
        Execution eligibility: `{artifacts.Package.ExecutionEligibility}`
        PR: `{artifacts.Package.Target.Repository}#{artifacts.Package.Target.PrNumber}`
        Target branch: `{artifacts.Package.Target.TargetBranch}`
        Expected current head: `{artifacts.Package.Target.ExpectedCurrentHeadSha}`
        Source apply pending: `{artifacts.Package.ExpectedState.SourceApplyPending.ToString().ToLowerInvariant()}`
        Expected changed files:
        {RenderBullets(artifacts.Package.ExpectedState.ExpectedChangedFiles)}
        Missing validation families:
        {RenderBullets(artifacts.Package.MissingValidationFamilies.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        Boundary: AR produces PR update package evidence only. It does not apply patches, mutate source, stage files, commit, push, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, ControlledPrUpdatePackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.PatchProposalId,
            package.PrUpdatePackageId,
            GovernanceKernelEventKind.PrUpdatePackageCreated,
            "ControlledPrUpdatePackage",
            package.PrUpdatePackageId,
            "Controlled PR update package was created.",
            ["pr-update-package.json", "pr-update-package-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? proposal = null;
        string? outPath = null;
        string? repo = null;
        string? prUrl = null;
        string? state = null;
        string? targetBranch = null;
        string? expectedHead = null;
        string? baseBranch = null;
        string? baseSha = null;
        string? createdBy = null;
        string? sourceApply = null;
        string? expectedPostUpdateHead = null;
        string? commitMessage = null;
        string? commitBody = null;
        int? pr = null;
        bool? draft = null;
        var validation = new List<string>();
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--proposal": if (!TryRead(args, ref index, out proposal)) return ParsedPackage.Fail(json, "--proposal requires a value."); break;
                case "--validation": if (!TryRead(args, ref index, out var receipt)) return ParsedPackage.Fail(json, "--validation requires a value."); validation.Add(receipt); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--pr-url": if (!TryRead(args, ref index, out prUrl)) return ParsedPackage.Fail(json, "--pr-url requires a value."); break;
                case "--state": if (!TryRead(args, ref index, out state)) return ParsedPackage.Fail(json, "--state requires a value."); break;
                case "--target-branch": if (!TryRead(args, ref index, out targetBranch)) return ParsedPackage.Fail(json, "--target-branch requires a value."); break;
                case "--expected-head": if (!TryRead(args, ref index, out expectedHead)) return ParsedPackage.Fail(json, "--expected-head requires a value."); break;
                case "--base-branch": if (!TryRead(args, ref index, out baseBranch)) return ParsedPackage.Fail(json, "--base-branch requires a value."); break;
                case "--base-sha": if (!TryRead(args, ref index, out baseSha)) return ParsedPackage.Fail(json, "--base-sha requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--source-apply": if (!TryRead(args, ref index, out sourceApply)) return ParsedPackage.Fail(json, "--source-apply requires a value."); break;
                case "--expected-post-update-head": if (!TryRead(args, ref index, out expectedPostUpdateHead)) return ParsedPackage.Fail(json, "--expected-post-update-head requires a value."); break;
                case "--commit-message": if (!TryRead(args, ref index, out commitMessage)) return ParsedPackage.Fail(json, "--commit-message requires a value."); break;
                case "--commit-body": if (!TryRead(args, ref index, out commitBody)) return ParsedPackage.Fail(json, "--commit-body requires a value."); break;
                case "--pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedPackage.Fail(json, "--pr requires a number.");
                    pr = parsedPr;
                    break;
                case "--draft":
                    if (!TryRead(args, ref index, out var draftValue) || !bool.TryParse(draftValue, out var parsedDraft)) return ParsedPackage.Fail(json, "--draft requires true or false.");
                    draft = parsedDraft;
                    break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(proposal)) return ParsedPackage.Fail(json, "Missing required option: --proposal <feedback-patch-proposal.json>.");
        if (validation.Count == 0) return ParsedPackage.Fail(json, "Missing required option: --validation <validation-receipt.json>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (pr is null) return ParsedPackage.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(prUrl)) return ParsedPackage.Fail(json, "Missing required option: --pr-url <url>.");
        if (string.IsNullOrWhiteSpace(state)) return ParsedPackage.Fail(json, "Missing required option: --state <open|closed>.");
        if (draft is null) return ParsedPackage.Fail(json, "Missing required option: --draft <true|false>.");
        if (string.IsNullOrWhiteSpace(targetBranch)) return ParsedPackage.Fail(json, "Missing required option: --target-branch <branch>.");
        if (string.IsNullOrWhiteSpace(expectedHead)) return ParsedPackage.Fail(json, "Missing required option: --expected-head <sha>.");
        if (string.IsNullOrWhiteSpace(baseBranch)) return ParsedPackage.Fail(json, "Missing required option: --base-branch <branch>.");
        if (string.IsNullOrWhiteSpace(baseSha)) return ParsedPackage.Fail(json, "Missing required option: --base-sha <sha>.");

        return new ParsedPackage(proposal, validation, outPath, repo, pr, prUrl, state, draft, targetBranch, expectedHead, baseBranch, baseSha, createdBy, sourceApply, expectedPostUpdateHead, commitMessage, commitBody, json, null);
    }

    private static ParsedRead ParseRead(string[] args)
    {
        string? package = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--package": if (!TryRead(args, ref index, out package)) return ParsedRead.Fail(json, "--package requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(package)
            ? ParsedRead.Fail(json, "Missing required option: --package <pr-update-package.json>.")
            : new ParsedRead(package, json, null);
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

    private static string ResolvePackagePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "pr-update-package.json")
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
        error.WriteLine("  irondev pr-update package --pr <number> --proposal <proposal.json> --validation <receipt.json> --out <path> --repo <owner/name> --pr-url <url> --state <open|closed> --draft <true|false> --target-branch <branch> --expected-head <sha> --base-branch <branch> --base-sha <sha> [--source-apply <receipt.json>] [--expected-post-update-head <sha>] [--json]");
        error.WriteLine("  irondev pr-update inspect --package <package.json> [--json]");
        error.WriteLine("  irondev pr-update status --package <package.json> [--json]");
        error.WriteLine("  irondev pr-update records --package <package.json> [--json]");
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
            ok = errors.Length == 0 && status != "failed" && status != "blocked",
            command,
            status,
            data,
            errors,
            boundary = PrUpdateBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? ProposalPath,
        List<string> ValidationPaths,
        string? OutPath,
        string? Repository,
        int? PullRequestNumber,
        string? PullRequestUrl,
        string? PullRequestState,
        bool? PullRequestDraft,
        string? TargetBranch,
        string? ExpectedHeadSha,
        string? BaseBranch,
        string? BaseSha,
        string? CreatedBy,
        string? SourceApplyPath,
        string? ExpectedPostUpdateHeadSha,
        string? CommitMessage,
        string? CommitBody,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, [], null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}

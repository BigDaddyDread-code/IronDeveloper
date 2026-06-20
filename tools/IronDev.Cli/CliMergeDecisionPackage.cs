using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static class IronDevCliMergeDecisionPackage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "execute",
        "merge",
        "auto-merge",
        "approve",
        "review",
        "resolve-comments",
        "reply",
        "request-reviewers",
        "ready",
        "release",
        "deploy",
        "tag",
        "publish",
        "promote-memory",
        "continue",
        "continue-workflow"
    ];

    public static bool IsMergeDecisionCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "merge-decision", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "merge-decision requires a subcommand: package, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"merge-decision {args[1]} is intentionally unsupported; Block AX only writes merge decision package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported merge-decision subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge-decision package", parsed.Error);

        var reviewerReceipt = ReadJson<ReviewerRequestExecutionReceipt>(Path.GetFullPath(parsed.ReviewerRequestReceiptPath!));
        if (reviewerReceipt is null)
            return Failure(output, error, parsed.Json, "merge-decision package", $"reviewer request execution receipt is missing or invalid: {parsed.ReviewerRequestReceiptPath}");

        var reviewEvidence = ReadJson<MergeReviewEvidence>(Path.GetFullPath(parsed.ReviewEvidencePath!));
        if (reviewEvidence is null)
            return Failure(output, error, parsed.Json, "merge-decision package", $"review evidence is missing or invalid: {parsed.ReviewEvidencePath}");

        var validationReceipt = ReadJson<ValidationRunReceipt>(Path.GetFullPath(parsed.ValidationReceiptPath!));
        if (validationReceipt is null)
            return Failure(output, error, parsed.Json, "merge-decision package", $"validation receipt is missing or invalid: {parsed.ValidationReceiptPath}");

        var validationEvidence = MergeDecisionPackageBuilder.FromValidationReceipt(validationReceipt);
        var observed = new MergeDecisionObservedPrState
        {
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber!.Value,
            PullRequestUrl = parsed.PullRequestUrl ?? $"https://github.com/{parsed.Repository}/pull/{parsed.PullRequestNumber}",
            PullRequestState = parsed.PullRequestState!,
            PullRequestDraft = parsed.PullRequestDraft!.Value,
            HeadBranch = parsed.HeadBranch!,
            HeadSha = parsed.ObservedHeadSha!,
            BaseBranch = parsed.BaseBranch!,
            BaseSha = parsed.BaseSha,
            Author = parsed.Author!,
            Mergeable = parsed.Mergeable!.Value,
            MergeStateStatus = parsed.MergeStateStatus!,
            IsBehindBase = parsed.IsBehindBase!.Value,
            HasConflicts = parsed.HasConflicts!.Value,
            ObservedAtUtc = parsed.ObservedAtUtc ?? DateTimeOffset.UtcNow,
            ObservationSource = parsed.ObservationSource ?? "cli-input"
        };
        var decision = new MergeDecisionRecord
        {
            MergeDecisionId = $"merge_decision_{Guid.NewGuid():N}",
            Decision = ParseDecision(parsed.Decision!),
            DecisionMadeBy = parsed.DecisionBy!,
            DecisionMadeAtUtc = parsed.DecisionMadeAtUtc ?? DateTimeOffset.UtcNow,
            DecisionRationale = parsed.DecisionRationale!,
            ExpectedRepository = parsed.Repository!,
            ExpectedPullRequestNumber = parsed.PullRequestNumber.Value,
            ExpectedHeadSha = parsed.ExpectedHeadSha!,
            ExpectedBaseBranch = parsed.BaseBranch!,
            ExpectedMergeStrategy = parsed.MergeStrategy!,
            ReviewEvidenceReceiptId = reviewEvidence.ReviewEvidenceReceiptId,
            ValidationEvidenceReceiptId = validationEvidence.ValidationEvidenceReceiptId
        };

        var artifacts = MergeDecisionPackageBuilder.Build(new MergeDecisionPackageInput
        {
            ReviewerRequestExecutionReceipt = reviewerReceipt,
            ObservedPullRequest = observed,
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber.Value,
            ExpectedHeadBranch = parsed.HeadBranch!,
            ExpectedHeadSha = parsed.ExpectedHeadSha!,
            ExpectedBaseBranch = parsed.BaseBranch!,
            ExpectedBaseSha = parsed.BaseSha,
            ReviewEvidence = reviewEvidence,
            ValidationEvidence = validationEvidence,
            MergeDecisionRecord = decision,
            CreatedBy = parsed.CreatedBy!,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(output, "merge-decision package", artifacts.Package.PackageVerdict == MergeDecisionPackageVerdict.PackageReadyForMergeExecutor ? "succeeded" : "blocked", new { outDirectory, artifacts.Package, artifacts.Receipt }, artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Merge decision package: {artifacts.Package.MergeDecisionPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Can merge for future executor: {artifacts.Package.CanMergeForExecutor}");
            output.WriteLine("Boundary: package evidence does not merge, approve, release, deploy, or continue workflow.");
        }

        return artifacts.Package.PackageVerdict is MergeDecisionPackageVerdict.PackageBlocked or MergeDecisionPackageVerdict.PackageRejected ? 1 : 0;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"merge-decision {mode}", parsed.Error);

        var package = ReadJson<MergeDecisionPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"merge-decision {mode}", "merge decision package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.ReviewEvidence, package.ValidationEvidence, package.MergeDecisionRecord, package.BlockReasons, package.PackageIssues }
                : new { package.MergeDecisionPackageId, package.Repository, package.PullRequestNumber, package.PackageVerdict, package.CanMergeForExecutor, package.Boundary };
            WriteJson(output, $"merge-decision {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Decision: {package.MergeDecisionRecord?.Decision}");
            output.WriteLine($"Decision maker: {package.MergeDecisionRecord?.DecisionMadeBy ?? "none"}");
            output.WriteLine($"Selected merge strategy: {package.SelectedMergeStrategy ?? "none"}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(package.PackageIssues)}");
        }
        else
        {
            output.WriteLine($"Merge decision package: {package.MergeDecisionPackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Can merge for future executor: {package.CanMergeForExecutor}");
            output.WriteLine("Boundary: package evidence grants no merge, approval, release, deploy, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, MergeDecisionPackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "merge-decision-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "merge-decision-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "merge-decision-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var records = new[]
        {
            JsonSerializer.Serialize(artifacts.Package.ReviewEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.ValidationEvidence, JsonOptions),
            JsonSerializer.Serialize(artifacts.Package.MergeDecisionRecord, JsonOptions),
            JsonSerializer.Serialize(artifacts.Receipt, JsonOptions)
        };
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "merge-decision-evidence.jsonl"), records, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(MergeDecisionPackageArtifacts artifacts) => $"""
        # Merge Decision Package

        Verdict: `{artifacts.Package.PackageVerdict}`
        Can merge for future executor: `{artifacts.Package.CanMergeForExecutor.ToString().ToLowerInvariant()}`
        PR: `{artifacts.Package.Repository}#{artifacts.Package.PullRequestNumber}`
        Head branch: `{artifacts.Package.HeadBranch}`
        Expected head: `{artifacts.Package.ExpectedHeadSha}`
        Selected merge strategy: `{artifacts.Package.SelectedMergeStrategy ?? "none"}`
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        Approval is not merge decision.
        Merge decision package is not merge execution.
        Merge execution is not release.
        Release is not deployment.
        Validation evidence is not approval.
        No self-approval.
        No hidden mutation.

        Boundary: AX produces merge decision package evidence only. It does not merge, enable auto-merge, approve, submit reviews, resolve review threads, release, deploy, tag, publish, promote memory, commit, push, mutate source, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, MergeDecisionPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.MergeDecisionPackageId,
            package.MergeDecisionPackageId,
            GovernanceKernelEventKind.MergeDecisionPackageCreated,
            "MergeDecisionPackage",
            package.MergeDecisionPackageId,
            "Merge decision package was created.",
            ["merge-decision-package.json", "merge-decision-package-receipt.json"]);

    private static ParsedPackage ParsePackage(string[] args)
    {
        string? outPath = null;
        string? repo = null;
        string? prUrl = null;
        string? state = null;
        string? headBranch = null;
        string? expectedHead = null;
        string? observedHead = null;
        string? baseBranch = null;
        string? baseSha = null;
        string? author = null;
        string? mergeStateStatus = null;
        string? reviewerRequestReceipt = null;
        string? reviewEvidence = null;
        string? validationReceipt = null;
        string? decision = null;
        string? decisionBy = null;
        string? decisionRationale = null;
        string? mergeStrategy = null;
        string? createdBy = null;
        string? observationSource = null;
        DateTimeOffset? observedAt = null;
        DateTimeOffset? decisionMadeAt = null;
        int? pr = null;
        bool? draft = null;
        bool? mergeable = null;
        bool? isBehindBase = null;
        bool? hasConflicts = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedPackage.Fail(json, "--out requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedPackage.Fail(json, "--repo requires a value."); break;
                case "--pr-url": if (!TryRead(args, ref index, out prUrl)) return ParsedPackage.Fail(json, "--pr-url requires a value."); break;
                case "--state": if (!TryRead(args, ref index, out state)) return ParsedPackage.Fail(json, "--state requires a value."); break;
                case "--branch":
                case "--target-branch":
                    if (!TryRead(args, ref index, out headBranch)) return ParsedPackage.Fail(json, $"{args[index]} requires a value.");
                    break;
                case "--head":
                case "--expected-head":
                    if (!TryRead(args, ref index, out expectedHead)) return ParsedPackage.Fail(json, $"{args[index]} requires a value.");
                    break;
                case "--observed-head": if (!TryRead(args, ref index, out observedHead)) return ParsedPackage.Fail(json, "--observed-head requires a value."); break;
                case "--base":
                case "--base-branch":
                    if (!TryRead(args, ref index, out baseBranch)) return ParsedPackage.Fail(json, $"{args[index]} requires a value.");
                    break;
                case "--base-sha": if (!TryRead(args, ref index, out baseSha)) return ParsedPackage.Fail(json, "--base-sha requires a value."); break;
                case "--author": if (!TryRead(args, ref index, out author)) return ParsedPackage.Fail(json, "--author requires a value."); break;
                case "--merge-state-status": if (!TryRead(args, ref index, out mergeStateStatus)) return ParsedPackage.Fail(json, "--merge-state-status requires a value."); break;
                case "--reviewer-request-receipt": if (!TryRead(args, ref index, out reviewerRequestReceipt)) return ParsedPackage.Fail(json, "--reviewer-request-receipt requires a value."); break;
                case "--review-evidence": if (!TryRead(args, ref index, out reviewEvidence)) return ParsedPackage.Fail(json, "--review-evidence requires a value."); break;
                case "--validation": if (!TryRead(args, ref index, out validationReceipt)) return ParsedPackage.Fail(json, "--validation requires a value."); break;
                case "--decision": if (!TryRead(args, ref index, out decision)) return ParsedPackage.Fail(json, "--decision requires a value."); break;
                case "--decision-by": if (!TryRead(args, ref index, out decisionBy)) return ParsedPackage.Fail(json, "--decision-by requires a value."); break;
                case "--decision-rationale": if (!TryRead(args, ref index, out decisionRationale)) return ParsedPackage.Fail(json, "--decision-rationale requires a value."); break;
                case "--merge-strategy": if (!TryRead(args, ref index, out mergeStrategy)) return ParsedPackage.Fail(json, "--merge-strategy requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--observation-source": if (!TryRead(args, ref index, out observationSource)) return ParsedPackage.Fail(json, "--observation-source requires a value."); break;
                case "--observed-at":
                    if (!TryRead(args, ref index, out var observedAtValue) || !DateTimeOffset.TryParse(observedAtValue, out var parsedObservedAt)) return ParsedPackage.Fail(json, "--observed-at requires a timestamp.");
                    observedAt = parsedObservedAt;
                    break;
                case "--decision-made-at":
                    if (!TryRead(args, ref index, out var decisionAtValue) || !DateTimeOffset.TryParse(decisionAtValue, out var parsedDecisionAt)) return ParsedPackage.Fail(json, "--decision-made-at requires a timestamp.");
                    decisionMadeAt = parsedDecisionAt;
                    break;
                case "--pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedPackage.Fail(json, "--pr requires a number.");
                    pr = parsedPr;
                    break;
                case "--draft":
                    if (!TryRead(args, ref index, out var draftValue) || !bool.TryParse(draftValue, out var parsedDraft)) return ParsedPackage.Fail(json, "--draft requires true or false.");
                    draft = parsedDraft;
                    break;
                case "--mergeable":
                    if (!TryRead(args, ref index, out var mergeableValue) || !bool.TryParse(mergeableValue, out var parsedMergeable)) return ParsedPackage.Fail(json, "--mergeable requires true or false.");
                    mergeable = parsedMergeable;
                    break;
                case "--is-behind-base":
                    if (!TryRead(args, ref index, out var behindValue) || !bool.TryParse(behindValue, out var parsedBehind)) return ParsedPackage.Fail(json, "--is-behind-base requires true or false.");
                    isBehindBase = parsedBehind;
                    break;
                case "--has-conflicts":
                    if (!TryRead(args, ref index, out var conflictsValue) || !bool.TryParse(conflictsValue, out var parsedConflicts)) return ParsedPackage.Fail(json, "--has-conflicts requires true or false.");
                    hasConflicts = parsedConflicts;
                    break;
                case "--json": json = true; break;
                default: return ParsedPackage.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (pr is null) return ParsedPackage.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(state)) return ParsedPackage.Fail(json, "Missing required option: --state <open|closed>.");
        if (draft is null) return ParsedPackage.Fail(json, "Missing required option: --draft <true|false>.");
        if (string.IsNullOrWhiteSpace(headBranch)) return ParsedPackage.Fail(json, "Missing required option: --branch <head-branch>.");
        if (string.IsNullOrWhiteSpace(expectedHead)) return ParsedPackage.Fail(json, "Missing required option: --head <sha>.");
        if (string.IsNullOrWhiteSpace(observedHead)) return ParsedPackage.Fail(json, "Missing required option: --observed-head <sha>.");
        if (string.IsNullOrWhiteSpace(baseBranch)) return ParsedPackage.Fail(json, "Missing required option: --base <base-branch>.");
        if (string.IsNullOrWhiteSpace(baseSha)) return ParsedPackage.Fail(json, "Missing required option: --base-sha <sha>.");
        if (string.IsNullOrWhiteSpace(author)) return ParsedPackage.Fail(json, "Missing required option: --author <login>.");
        if (mergeable is null) return ParsedPackage.Fail(json, "Missing required option: --mergeable <true|false>.");
        if (string.IsNullOrWhiteSpace(mergeStateStatus)) return ParsedPackage.Fail(json, "Missing required option: --merge-state-status <status>.");
        if (isBehindBase is null) return ParsedPackage.Fail(json, "Missing required option: --is-behind-base <true|false>.");
        if (hasConflicts is null) return ParsedPackage.Fail(json, "Missing required option: --has-conflicts <true|false>.");
        if (string.IsNullOrWhiteSpace(reviewerRequestReceipt)) return ParsedPackage.Fail(json, "Missing required option: --reviewer-request-receipt <receipt.json>.");
        if (string.IsNullOrWhiteSpace(reviewEvidence)) return ParsedPackage.Fail(json, "Missing required option: --review-evidence <review-evidence.json>.");
        if (string.IsNullOrWhiteSpace(validationReceipt)) return ParsedPackage.Fail(json, "Missing required option: --validation <validation-run-receipt.json>.");
        if (string.IsNullOrWhiteSpace(decision)) return ParsedPackage.Fail(json, "Missing required option: --decision <approved-for-merge-executor|blocked|rejected>.");
        if (!TryParseDecision(decision, out _)) return ParsedPackage.Fail(json, "Unsupported --decision. Use approved-for-merge-executor, blocked, or rejected.");
        if (string.IsNullOrWhiteSpace(decisionBy)) return ParsedPackage.Fail(json, "Missing required option: --decision-by <github-login>.");
        if (string.IsNullOrWhiteSpace(decisionRationale)) return ParsedPackage.Fail(json, "Missing required option: --decision-rationale <text>.");
        if (string.IsNullOrWhiteSpace(mergeStrategy)) return ParsedPackage.Fail(json, "Missing required option: --merge-strategy <merge-commit|squash|rebase>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");

        return new ParsedPackage(outPath, repo, pr, prUrl, state, draft, headBranch, expectedHead, observedHead, baseBranch, baseSha, author, mergeable, mergeStateStatus, isBehindBase, hasConflicts, reviewerRequestReceipt, reviewEvidence, validationReceipt, decision, decisionBy, decisionRationale, mergeStrategy, createdBy, observedAt, decisionMadeAt, observationSource, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <merge-decision-package.json>.")
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

    private static MergeDecision ParseDecision(string value) =>
        TryParseDecision(value, out var decision) ? decision : MergeDecision.Blocked;

    private static bool TryParseDecision(string value, out MergeDecision decision)
    {
        var normalized = value.Trim().Replace("_", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        switch (normalized)
        {
            case "approved-for-merge-executor":
            case "approvedformergeexecutor":
                decision = MergeDecision.ApprovedForMergeExecutor;
                return true;
            case "blocked":
                decision = MergeDecision.Blocked;
                return true;
            case "rejected":
                decision = MergeDecision.Rejected;
                return true;
            default:
                decision = MergeDecision.Blocked;
                return false;
        }
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
            ? Path.Combine(fullPath, "merge-decision-package.json")
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

    private static string RenderInline(IEnumerable<string> values)
    {
        var items = values.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return items.Length == 0 ? "none" : string.Join(", ", items);
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev merge-decision package --repo <owner/name> --pr <number> --state open --draft false --branch <head-branch> --head <sha> --observed-head <sha> --base <base-branch> --base-sha <sha> --author <login> --mergeable <true|false> --merge-state-status <status> --is-behind-base <true|false> --has-conflicts <true|false> --reviewer-request-receipt <receipt.json> --review-evidence <review-evidence.json> --validation <validation-run-receipt.json> --decision <approved-for-merge-executor|blocked|rejected> --decision-by <github-login> --decision-rationale <text> --merge-strategy <merge-commit|squash|rebase> --created-by <github-login> --out <path> [--json]");
        error.WriteLine("  irondev merge-decision inspect --package <merge-decision-package.json> [--json]");
        error.WriteLine("  irondev merge-decision status --package <merge-decision-package.json> [--json]");
        error.WriteLine("  irondev merge-decision records --package <merge-decision-package.json> [--json]");
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
            boundary = MergeDecisionPackageBoundary.Evidence
        }, JsonOptions));
    }

    private sealed record ParsedPackage(
        string? OutPath,
        string? Repository,
        int? PullRequestNumber,
        string? PullRequestUrl,
        string? PullRequestState,
        bool? PullRequestDraft,
        string? HeadBranch,
        string? ExpectedHeadSha,
        string? ObservedHeadSha,
        string? BaseBranch,
        string? BaseSha,
        string? Author,
        bool? Mergeable,
        string? MergeStateStatus,
        bool? IsBehindBase,
        bool? HasConflicts,
        string? ReviewerRequestReceiptPath,
        string? ReviewEvidencePath,
        string? ValidationReceiptPath,
        string? Decision,
        string? DecisionBy,
        string? DecisionRationale,
        string? MergeStrategy,
        string? CreatedBy,
        DateTimeOffset? ObservedAtUtc,
        DateTimeOffset? DecisionMadeAtUtc,
        string? ObservationSource,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}

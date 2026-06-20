using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static partial class IronDevCliReadyForReview
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "mark-ready",
        "execute-request-reviewers",
        "request-reviewers",
        "approve",
        "review",
        "resolve-comments",
        "merge",
        "auto-merge",
        "release",
        "deploy",
        "tag",
        "publish",
        "promote-memory",
        "continue",
        "continue-workflow"
    ];

    public static bool IsReadyCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "ready", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "ready requires a subcommand: package, inspect, status, records, execute, execution-status, or execution-records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"ready {args[1]} is intentionally unsupported; Block AT only writes ready-for-review eligibility package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "execute" => await HandleExecuteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            "execution-status" => HandleExecutionRead(args, output, error, "execution-status"),
            "execution-records" => HandleExecutionRead(args, output, error, "execution-records"),
            _ => Usage(error, $"unsupported ready subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "ready package", parsed.Error);

        PrBranchUpdateExecutionReceipt? asReceipt = null;
        if (!string.IsNullOrWhiteSpace(parsed.AsReceiptPath))
        {
            asReceipt = ReadJson<PrBranchUpdateExecutionReceipt>(Path.GetFullPath(parsed.AsReceiptPath));
            if (asReceipt is null)
                return Failure(output, error, parsed.Json, "ready package", $"AS receipt is missing or invalid: {parsed.AsReceiptPath}");
        }

        ExplicitNoBranchUpdateRequiredEvidence? noUpdateEvidence = null;
        if (!string.IsNullOrWhiteSpace(parsed.NoBranchUpdateRequiredPath))
        {
            noUpdateEvidence = ReadJson<ExplicitNoBranchUpdateRequiredEvidence>(Path.GetFullPath(parsed.NoBranchUpdateRequiredPath));
            if (noUpdateEvidence is null)
                return Failure(output, error, parsed.Json, "ready package", $"no-branch-update evidence is missing or invalid: {parsed.NoBranchUpdateRequiredPath}");
        }

        var validationReceipts = new List<ValidationRunReceipt>();
        foreach (var validationPath in parsed.ValidationPaths)
        {
            var receipt = ReadJson<ValidationRunReceipt>(Path.GetFullPath(validationPath));
            if (receipt is null)
                return Failure(output, error, parsed.Json, "ready package", $"validation receipt is missing or invalid: {validationPath}");
            validationReceipts.Add(receipt);
        }

        var phaseReceiptPath = Path.GetFullPath(parsed.PhaseReceiptPath!);
        if (!File.Exists(phaseReceiptPath))
            return Failure(output, error, parsed.Json, "ready package", $"phase authority receipt is missing: {parsed.PhaseReceiptPath}");

        var artifacts = ReadyForReviewSeparationBuilder.Build(new ReadyForReviewSeparationInput
        {
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber!.Value,
            PullRequestUrl = parsed.PullRequestUrl,
            PullRequestState = parsed.PullRequestState!,
            PullRequestDraft = parsed.PullRequestDraft!.Value,
            HeadBranch = parsed.HeadBranch!,
            ExpectedHeadSha = parsed.ExpectedHeadSha!,
            ObservedHeadSha = parsed.ObservedHeadSha,
            BaseBranch = parsed.BaseBranch!,
            BaseSha = parsed.BaseSha!,
            ExpectedBaseBranch = parsed.ExpectedBaseBranch,
            ExpectedBaseSha = parsed.ExpectedBaseSha,
            BranchUpdateReceipt = asReceipt,
            NoBranchUpdateRequiredEvidence = noUpdateEvidence,
            ValidationReceipts = validationReceipts.ToArray(),
            PhaseAuthorityReceiptId = Path.GetFileNameWithoutExtension(phaseReceiptPath),
            PhaseAuthorityReceiptText = File.ReadAllText(phaseReceiptPath),
            PackageCreatedBy = parsed.CreatedBy ?? Environment.UserName,
            PackageCreatedAtUtc = DateTimeOffset.UtcNow
        });

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(output, "ready package", artifacts.Package.Verdict is ReadyForReviewEligibilityVerdict.Blocked or ReadyForReviewEligibilityVerdict.Rejected ? "blocked" : "succeeded", new { outDirectory, artifacts.Package, artifacts.Receipt }, artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Ready-for-review package: {artifacts.Package.ReadyForReviewPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.Verdict}");
            output.WriteLine($"Can mark ready for future executor: {artifacts.Package.CanMarkReadyForReview}");
            output.WriteLine("Boundary: package evidence does not mark ready, request reviewers, approve, merge, release, deploy, or continue workflow.");
        }

        return artifacts.Package.Verdict is ReadyForReviewEligibilityVerdict.Blocked or ReadyForReviewEligibilityVerdict.Rejected ? 1 : 0;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"ready {mode}", parsed.Error);

        var package = ReadJson<ReadyForReviewEligibilityPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"ready {mode}", "ready-for-review package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.BranchUpdateEvidence, package.ValidationEvidence, package.PhaseAuthorityReceiptId, package.BlockReasons, package.PackageIssues }
                : new { package.ReadyForReviewPackageId, package.Target, package.Verdict, package.CanMarkReadyForReview, package.Boundary };
            WriteJson(output, $"ready {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Target: {package.Target.Repository}#{package.Target.PullRequestNumber} {package.Target.HeadBranch}");
            output.WriteLine($"Validation receipts: {package.ValidationEvidence.Length}");
            output.WriteLine($"Phase authority receipt: {package.PhaseAuthorityReceiptId}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
        }
        else
        {
            output.WriteLine($"Ready-for-review package: {package.ReadyForReviewPackageId}");
            output.WriteLine($"Verdict: {package.Verdict}");
            output.WriteLine($"Can mark ready for future executor: {package.CanMarkReadyForReview}");
            output.WriteLine("Boundary: package evidence grants no reviewer, approval, merge, release, deploy, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, ReadyForReviewSeparationArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "ready-for-review-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "ready-for-review-separation-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "ready-for-review-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "ready-for-review-validation-records.jsonl"), artifacts.Package.ValidationEvidence.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(ReadyForReviewSeparationArtifacts artifacts) => $"""
        # Ready-for-Review Eligibility Package

        Verdict: `{artifacts.Package.Verdict}`
        Can mark ready for future executor: `{artifacts.Package.CanMarkReadyForReview.ToString().ToLowerInvariant()}`
        PR: `{artifacts.Package.Target.Repository}#{artifacts.Package.Target.PullRequestNumber}`
        Head branch: `{artifacts.Package.Target.HeadBranch}`
        Expected head: `{artifacts.Package.Target.ExpectedHeadSha}`
        Missing validation families:
        {RenderBullets(artifacts.Package.MissingValidationFamilies.Select(item => item.ToString()))}
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        Boundary: AT produces ready-for-review eligibility evidence only. It does not mark ready, request reviewers, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, ReadyForReviewEligibilityPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.ReadyForReviewPackageId,
            package.ReadyForReviewPackageId,
            GovernanceKernelEventKind.ReadyForReviewPackageCreated,
            "ReadyForReviewEligibilityPackage",
            package.ReadyForReviewPackageId,
            "Ready-for-review eligibility package was created.",
            ["ready-for-review-package.json", "ready-for-review-separation-receipt.json"]);

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
        string? expectedBaseBranch = null;
        string? expectedBaseSha = null;
        string? asReceipt = null;
        string? noBranchUpdateRequired = null;
        string? phaseReceipt = null;
        string? createdBy = null;
        int? pr = null;
        bool? draft = null;
        var validation = new List<string>();
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
                case "--expected-base": if (!TryRead(args, ref index, out expectedBaseBranch)) return ParsedPackage.Fail(json, "--expected-base requires a value."); break;
                case "--expected-base-sha": if (!TryRead(args, ref index, out expectedBaseSha)) return ParsedPackage.Fail(json, "--expected-base-sha requires a value."); break;
                case "--as-receipt": if (!TryRead(args, ref index, out asReceipt)) return ParsedPackage.Fail(json, "--as-receipt requires a value."); break;
                case "--no-branch-update-required": if (!TryRead(args, ref index, out noBranchUpdateRequired)) return ParsedPackage.Fail(json, "--no-branch-update-required requires a value."); break;
                case "--validation": if (!TryRead(args, ref index, out var receipt)) return ParsedPackage.Fail(json, "--validation requires a value."); validation.Add(receipt); break;
                case "--phase-receipt": if (!TryRead(args, ref index, out phaseReceipt)) return ParsedPackage.Fail(json, "--phase-receipt requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
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

        if (string.IsNullOrWhiteSpace(outPath)) return ParsedPackage.Fail(json, "Missing required option: --out <path>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedPackage.Fail(json, "Missing required option: --repo <owner/name>.");
        if (pr is null) return ParsedPackage.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(headBranch)) return ParsedPackage.Fail(json, "Missing required option: --branch <branch>.");
        if (string.IsNullOrWhiteSpace(expectedHead)) return ParsedPackage.Fail(json, "Missing required option: --head <sha>.");
        if (string.IsNullOrWhiteSpace(observedHead)) return ParsedPackage.Fail(json, "Missing required option: --observed-head <sha>.");
        if (string.IsNullOrWhiteSpace(baseBranch)) return ParsedPackage.Fail(json, "Missing required option: --base <branch>.");
        if (string.IsNullOrWhiteSpace(baseSha)) return ParsedPackage.Fail(json, "Missing required option: --base-sha <sha>.");
        if (string.IsNullOrWhiteSpace(state)) return ParsedPackage.Fail(json, "Missing required option: --state <open|closed>.");
        if (draft is null) return ParsedPackage.Fail(json, "Missing required option: --draft <true|false>.");
        if (validation.Count == 0) return ParsedPackage.Fail(json, "Missing required option: --validation <validation-receipt.json>.");
        if (string.IsNullOrWhiteSpace(phaseReceipt)) return ParsedPackage.Fail(json, "Missing required option: --phase-receipt <receipt.md>.");
        if (string.IsNullOrWhiteSpace(asReceipt) && string.IsNullOrWhiteSpace(noBranchUpdateRequired)) return ParsedPackage.Fail(json, "Missing required option: --as-receipt <receipt.json> or --no-branch-update-required <evidence.json>.");
        if (!string.IsNullOrWhiteSpace(asReceipt) && !string.IsNullOrWhiteSpace(noBranchUpdateRequired)) return ParsedPackage.Fail(json, "Use either --as-receipt or --no-branch-update-required, not both.");

        return new ParsedPackage(outPath, repo, pr, prUrl, state, draft, headBranch, expectedHead, observedHead, baseBranch, baseSha, expectedBaseBranch, expectedBaseSha, asReceipt, noBranchUpdateRequired, validation, phaseReceipt, createdBy, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <ready-for-review-package.json>.")
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
            ? Path.Combine(fullPath, "ready-for-review-package.json")
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
        error.WriteLine("  irondev ready package --pr <number> --repo <owner/name> --state <open|closed> --draft <true|false> --head <sha> --observed-head <sha> --base <branch> --base-sha <sha> --branch <branch> --as-receipt <receipt.json> --validation <receipt.json> --phase-receipt <receipt.md> --out <path> [--pr-url <url>] [--created-by <name>] [--json]");
        error.WriteLine("  irondev ready package --pr <number> --repo <owner/name> --state <open|closed> --draft <true|false> --head <sha> --observed-head <sha> --base <branch> --base-sha <sha> --branch <branch> --no-branch-update-required <evidence.json> --validation <receipt.json> --phase-receipt <receipt.md> --out <path> [--json]");
        error.WriteLine("  irondev ready inspect --package <ready-package.json> [--json]");
        error.WriteLine("  irondev ready status --package <ready-package.json> [--json]");
        error.WriteLine("  irondev ready records --package <ready-package.json> [--json]");
        error.WriteLine("  irondev ready execute --package <ready-for-review-package.json> --repo <owner/name> --pr <number> --observed-head <sha> --out <path> [--json]");
        error.WriteLine("  irondev ready execution-status --receipt <ready-for-review-execution-receipt.json> [--json]");
        error.WriteLine("  irondev ready execution-records --receipt <ready-for-review-execution-receipt.json> [--json]");
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
            boundary = ReadyForReviewBoundary.Evidence
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
        string? ExpectedBaseBranch,
        string? ExpectedBaseSha,
        string? AsReceiptPath,
        string? NoBranchUpdateRequiredPath,
        List<string> ValidationPaths,
        string? PhaseReceiptPath,
        string? CreatedBy,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, [], null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }

}

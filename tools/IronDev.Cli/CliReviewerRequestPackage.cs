using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static partial class IronDevCliReviewerRequestPackage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "request",
        "request-reviewers",
        "remove-reviewers",
        "ready",
        "approve",
        "review",
        "resolve-comments",
        "reply",
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

    public static bool IsReviewerRequestCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "reviewer-request", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "reviewer-request requires a subcommand: package, inspect, status, records, execute, execution-status, or execution-records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"reviewer-request {args[1]} is intentionally unsupported; Block AV only writes reviewer request package evidence.");

        return subcommand switch
        {
            "package" => await HandlePackageAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "execute" => await HandleExecuteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleRead(args, output, error, "inspect"),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            "execution-status" => HandleExecutionRead(args, output, error, "execution-status"),
            "execution-records" => HandleExecutionRead(args, output, error, "execution-records"),
            _ => Usage(error, $"unsupported reviewer-request subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePackageAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParsePackage(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "reviewer-request package", parsed.Error);

        var readyReceipt = ReadJson<ReadyForReviewExecutionReceipt>(Path.GetFullPath(parsed.ReadyReceiptPath!));
        if (readyReceipt is null)
            return Failure(output, error, parsed.Json, "reviewer-request package", $"ready execution receipt is missing or invalid: {parsed.ReadyReceiptPath}");

        var observed = new ReviewerRequestObservedPrState
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
            ExistingRequestedReviewers = parsed.AlreadyRequestedReviewers.ToArray(),
            ExistingRequestedTeams = parsed.AlreadyRequestedTeams.ToArray(),
            Author = parsed.Author!,
            ObservedAtUtc = parsed.ObservedAtUtc ?? DateTimeOffset.UtcNow,
            ObservationSource = parsed.ObservationSource ?? "cli-input"
        };

        var artifacts = ReviewerRequestPackageBuilder.Build(new ReviewerRequestPackageInput
        {
            ReadyExecutionReceipt = readyReceipt,
            ObservedPullRequest = observed,
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber.Value,
            ExpectedHeadBranch = parsed.HeadBranch!,
            ExpectedHeadSha = parsed.ExpectedHeadSha!,
            ExpectedBaseBranch = parsed.BaseBranch!,
            ExpectedBaseSha = parsed.BaseSha,
            RequestedReviewers = parsed.RequestedReviewers.ToArray(),
            RequestedTeams = parsed.RequestedTeams.ToArray(),
            RequestRationale = parsed.Rationale!,
            RequestedBy = parsed.CreatedBy!,
            PackageCreatedAtUtc = DateTimeOffset.UtcNow
        });

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Package);

        if (parsed.Json)
            WriteJson(output, "reviewer-request package", artifacts.Package.PackageVerdict == ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor ? "succeeded" : "blocked", new { outDirectory, artifacts.Package, artifacts.Receipt }, artifacts.Package.PackageIssues);
        else
        {
            output.WriteLine($"Reviewer request package: {artifacts.Package.ReviewerRequestPackageId}");
            output.WriteLine($"Verdict: {artifacts.Package.PackageVerdict}");
            output.WriteLine($"Can request reviewers for future executor: {artifacts.Package.CanRequestReviewersForExecutor}");
            output.WriteLine("Boundary: package evidence does not request reviewers, approve, merge, release, deploy, or continue workflow.");
        }

        return artifacts.Package.PackageVerdict is ReviewerRequestPackageVerdict.PackageBlocked or ReviewerRequestPackageVerdict.PackageRejected ? 1 : 0;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"reviewer-request {mode}", parsed.Error);

        var package = ReadJson<ReviewerRequestPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, $"reviewer-request {mode}", "reviewer request package is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { package.RequestedReviewers, package.RequestedTeams, package.AlreadyRequestedReviewers, package.AlreadyRequestedTeams, package.SkippedReviewerTargets, package.BlockReasons, package.PackageIssues }
                : new { package.ReviewerRequestPackageId, package.Repository, package.PullRequestNumber, package.PackageVerdict, package.CanRequestReviewersForExecutor, package.Boundary };
            WriteJson(output, $"reviewer-request {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Requested reviewers: {RenderInline(package.RequestedReviewers.Select(item => item.SlugOrLogin))}");
            output.WriteLine($"Requested teams: {RenderInline(package.RequestedTeams.Select(item => item.SlugOrLogin))}");
            output.WriteLine($"Already requested reviewers: {RenderInline(package.AlreadyRequestedReviewers)}");
            output.WriteLine($"Already requested teams: {RenderInline(package.AlreadyRequestedTeams)}");
            output.WriteLine($"Block reasons: {RenderInline(package.BlockReasons.Select(item => item.ToString()))}");
        }
        else
        {
            output.WriteLine($"Reviewer request package: {package.ReviewerRequestPackageId}");
            output.WriteLine($"Verdict: {package.PackageVerdict}");
            output.WriteLine($"Can request reviewers for future executor: {package.CanRequestReviewersForExecutor}");
            output.WriteLine("Boundary: package evidence grants no reviewer request, approval, merge, release, deploy, or continuation authority.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(string outDirectory, ReviewerRequestPackageArtifacts artifacts, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "reviewer-request-package.json"), JsonSerializer.Serialize(artifacts.Package, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "reviewer-request-package-receipt.json"), JsonSerializer.Serialize(artifacts.Receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "reviewer-request-summary.md"), RenderSummary(artifacts), cancellationToken).ConfigureAwait(false);
        var targets = artifacts.Package.RequestedReviewers
            .Concat(artifacts.Package.RequestedTeams)
            .Concat(artifacts.Package.SkippedReviewerTargets)
            .Select(target => JsonSerializer.Serialize(target, JsonOptions));
        await File.WriteAllLinesAsync(Path.Combine(outDirectory, "reviewer-request-targets.jsonl"), targets, cancellationToken).ConfigureAwait(false);
    }

    private static string RenderSummary(ReviewerRequestPackageArtifacts artifacts) => $"""
        # Reviewer Request Package

        Verdict: `{artifacts.Package.PackageVerdict}`
        Can request reviewers for future executor: `{artifacts.Package.CanRequestReviewersForExecutor.ToString().ToLowerInvariant()}`
        PR: `{artifacts.Package.Repository}#{artifacts.Package.PullRequestNumber}`
        Head branch: `{artifacts.Package.HeadBranch}`
        Expected head: `{artifacts.Package.ExpectedHeadSha}`
        Requested reviewers:
        {RenderBullets(artifacts.Package.RequestedReviewers.Select(item => item.SlugOrLogin))}
        Requested teams:
        {RenderBullets(artifacts.Package.RequestedTeams.Select(item => item.SlugOrLogin))}
        Block reasons:
        {RenderBullets(artifacts.Package.BlockReasons.Select(item => item.ToString()))}
        Package issues:
        {RenderBullets(artifacts.Package.PackageIssues)}

        Boundary: AV produces reviewer request package evidence only. It does not request reviewers, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, ReviewerRequestPackage package) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            package.ReviewerRequestPackageId,
            package.ReviewerRequestPackageId,
            GovernanceKernelEventKind.ReviewerRequestPackageCreated,
            "ReviewerRequestPackage",
            package.ReviewerRequestPackageId,
            "Reviewer request package was created.",
            ["reviewer-request-package.json", "reviewer-request-package-receipt.json"]);

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
        string? readyReceipt = null;
        string? rationale = null;
        string? createdBy = null;
        string? observationSource = null;
        DateTimeOffset? observedAt = null;
        int? pr = null;
        bool? draft = null;
        var reviewers = new List<string>();
        var teams = new List<string>();
        var alreadyReviewers = new List<string>();
        var alreadyTeams = new List<string>();
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
                case "--ready-receipt": if (!TryRead(args, ref index, out readyReceipt)) return ParsedPackage.Fail(json, "--ready-receipt requires a value."); break;
                case "--reviewer": if (!TryRead(args, ref index, out var reviewer)) return ParsedPackage.Fail(json, "--reviewer requires a value."); reviewers.Add(reviewer); break;
                case "--team": if (!TryRead(args, ref index, out var team)) return ParsedPackage.Fail(json, "--team requires a value."); teams.Add(team); break;
                case "--already-requested-reviewer": if (!TryRead(args, ref index, out var alreadyReviewer)) return ParsedPackage.Fail(json, "--already-requested-reviewer requires a value."); alreadyReviewers.Add(alreadyReviewer); break;
                case "--already-requested-team": if (!TryRead(args, ref index, out var alreadyTeam)) return ParsedPackage.Fail(json, "--already-requested-team requires a value."); alreadyTeams.Add(alreadyTeam); break;
                case "--rationale": if (!TryRead(args, ref index, out rationale)) return ParsedPackage.Fail(json, "--rationale requires a value."); break;
                case "--created-by": if (!TryRead(args, ref index, out createdBy)) return ParsedPackage.Fail(json, "--created-by requires a value."); break;
                case "--observation-source": if (!TryRead(args, ref index, out observationSource)) return ParsedPackage.Fail(json, "--observation-source requires a value."); break;
                case "--observed-at":
                    if (!TryRead(args, ref index, out var observedAtValue) || !DateTimeOffset.TryParse(observedAtValue, out var parsedObservedAt)) return ParsedPackage.Fail(json, "--observed-at requires a timestamp.");
                    observedAt = parsedObservedAt;
                    break;
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
        if (string.IsNullOrWhiteSpace(state)) return ParsedPackage.Fail(json, "Missing required option: --state <open|closed>.");
        if (draft is null) return ParsedPackage.Fail(json, "Missing required option: --draft <true|false>.");
        if (string.IsNullOrWhiteSpace(headBranch)) return ParsedPackage.Fail(json, "Missing required option: --branch <branch>.");
        if (string.IsNullOrWhiteSpace(expectedHead)) return ParsedPackage.Fail(json, "Missing required option: --head <sha>.");
        if (string.IsNullOrWhiteSpace(observedHead)) return ParsedPackage.Fail(json, "Missing required option: --observed-head <sha>.");
        if (string.IsNullOrWhiteSpace(baseBranch)) return ParsedPackage.Fail(json, "Missing required option: --base <branch>.");
        if (string.IsNullOrWhiteSpace(baseSha)) return ParsedPackage.Fail(json, "Missing required option: --base-sha <sha>.");
        if (string.IsNullOrWhiteSpace(author)) return ParsedPackage.Fail(json, "Missing required option: --author <login>.");
        if (string.IsNullOrWhiteSpace(readyReceipt)) return ParsedPackage.Fail(json, "Missing required option: --ready-receipt <ready-for-review-execution-receipt.json>.");
        if (string.IsNullOrWhiteSpace(rationale)) return ParsedPackage.Fail(json, "Missing required option: --rationale <text>.");
        if (string.IsNullOrWhiteSpace(createdBy)) return ParsedPackage.Fail(json, "Missing required option: --created-by <github-login>.");

        return new ParsedPackage(outPath, repo, pr, prUrl, state, draft, headBranch, expectedHead, observedHead, baseBranch, baseSha, author, readyReceipt, reviewers, teams, alreadyReviewers, alreadyTeams, rationale, createdBy, observedAt, observationSource, json, null);
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
            ? ParsedRead.Fail(json, "Missing required option: --package <reviewer-request-package.json>.")
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
            ? Path.Combine(fullPath, "reviewer-request-package.json")
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
        error.WriteLine("  irondev reviewer-request package --repo <owner/name> --pr <number> --state open --draft false --branch <head-branch> --head <sha> --observed-head <sha> --base <base-branch> --base-sha <sha> --author <login> --ready-receipt <receipt.json> --reviewer <login> --team <team-slug> --rationale <text> --created-by <login> --out <path> [--already-requested-reviewer <login>] [--already-requested-team <team-slug>] [--json]");
        error.WriteLine("  irondev reviewer-request inspect --package <reviewer-request-package.json> [--json]");
        error.WriteLine("  irondev reviewer-request status --package <reviewer-request-package.json> [--json]");
        error.WriteLine("  irondev reviewer-request records --package <reviewer-request-package.json> [--json]");
        error.WriteLine("  irondev reviewer-request execute --package <reviewer-request-package.json> --repo <owner/name> --pr <number> --observed-head <sha> --out <path> [--requested-by <login>] [--json]");
        error.WriteLine("  irondev reviewer-request execution-status --receipt <reviewer-request-execution-receipt.json> [--json]");
        error.WriteLine("  irondev reviewer-request execution-records --receipt <reviewer-request-execution-receipt.json> [--json]");
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
            boundary = ReviewerRequestPackageBoundary.Evidence
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
        string? ReadyReceiptPath,
        List<string> RequestedReviewers,
        List<string> RequestedTeams,
        List<string> AlreadyRequestedReviewers,
        List<string> AlreadyRequestedTeams,
        string? Rationale,
        string? CreatedBy,
        DateTimeOffset? ObservedAtUtc,
        string? ObservationSource,
        bool Json,
        string? Error)
    {
        public static ParsedPackage Fail(bool json, string error) => new(null, null, null, null, null, null, null, null, null, null, null, null, null, [], [], [], [], null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? PackagePath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}

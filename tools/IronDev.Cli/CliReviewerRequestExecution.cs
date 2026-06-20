using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static partial class IronDevCliReviewerRequestPackage
{
    private static async Task<int> HandleExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "reviewer-request execute", parsed.Error);

        var package = ReadJson<ReviewerRequestPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, "reviewer-request execute", "reviewer request package is missing or invalid.");

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        var request = new ReviewerRequestExecutionRequest
        {
            Package = package,
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber!.Value,
            ExpectedHeadBranch = package.HeadBranch,
            ExpectedHeadSha = parsed.ObservedHeadSha!,
            ExpectedBaseBranch = package.BaseBranch,
            ExpectedBaseSha = package.BaseSha,
            OutputDirectory = outDirectory,
            RequestedBy = parsed.RequestedBy ?? package.CreatedBy,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        var result = await ReviewerRequestExecutor.ExecuteAsync(
            request,
            new GitHubCliReviewerRequestCommandClient(parsed.Repository!, parsed.PullRequestNumber.Value),
            cancellationToken).ConfigureAwait(false);
        if (result.Receipt is not null)
        {
            await WriteExecutionReceiptAsync(outDirectory, result.Receipt, cancellationToken).ConfigureAwait(false);
            RecordExecutionEvent(outDirectory, result.Receipt);
        }

        if (parsed.Json)
            WriteJson(output, "reviewer-request execute", result.Verdict == ReviewerRequestExecutionVerdict.Executed ? "succeeded" : "blocked", new { outDirectory, receipt = result.Receipt }, result.Issues);
        else if (result.Verdict == ReviewerRequestExecutionVerdict.Executed && result.Receipt is not null)
        {
            output.WriteLine("Reviewers requested.");
            output.WriteLine($"PR: {result.Receipt.Repository}#{result.Receipt.PullRequestNumber}");
            output.WriteLine("Boundary: reviewer request execution did not approve, resolve comments, merge, release, deploy, or continue workflow.");
        }
        else
        {
            error.WriteLine($"Reviewer request execution blocked: {string.Join(", ", result.Issues)}");
        }

        return result.Verdict == ReviewerRequestExecutionVerdict.Executed ? 0 : 1;
    }

    private static int HandleExecutionRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseExecutionRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"reviewer-request {mode}", parsed.Error);

        var receipt = ReadJson<ReviewerRequestExecutionReceipt>(ResolveExecutionReceiptPath(parsed.ReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, $"reviewer-request {mode}", "reviewer request execution receipt is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "execution-records"
                ? new { receipt.PreState, receipt.PostState, receipt.ReviewerRequestAttempted, receipt.ReviewerRequestAccepted, receipt.PostStateVerified, receipt.Issues, receipt.Boundary }
                : new { receipt.ReviewerRequestExecutionId, receipt.ReviewerRequestPackageId, receipt.Repository, receipt.PullRequestNumber, receipt.ExecutionVerdict, receipt.FailureClassification, receipt.PostStateVerified, receipt.Boundary };
            WriteJson(output, $"reviewer-request {mode}", "succeeded", data, []);
        }
        else if (mode == "execution-records")
        {
            output.WriteLine($"Reviewer request attempted: {receipt.ReviewerRequestAttempted}");
            output.WriteLine($"Reviewer request accepted: {receipt.ReviewerRequestAccepted}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine($"Issues: {RenderInline(receipt.Issues)}");
        }
        else
        {
            output.WriteLine($"Reviewer request execution: {receipt.ExecutionVerdict}");
            output.WriteLine($"PR: {receipt.Repository}#{receipt.PullRequestNumber}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine("Boundary: status is read-only and does not approve, merge, release, deploy, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteExecutionReceiptAsync(string outDirectory, ReviewerRequestExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "reviewer-request-execution-receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "reviewer-request-execution-receipt.md"), RenderExecutionReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderExecutionReceipt(ReviewerRequestExecutionReceipt receipt) => $"""
        # Reviewer Request Execution Receipt

        Verdict: `{receipt.ExecutionVerdict}`
        Failure classification: `{receipt.FailureClassification}`
        Package: `{receipt.ReviewerRequestPackageId}`
        PR: `{receipt.Repository}#{receipt.PullRequestNumber}`
        Expected head branch: `{receipt.ExpectedHeadBranch}`
        Expected head SHA: `{receipt.ExpectedHeadSha}`
        Requested reviewers:
        {RenderBullets(receipt.RequestedReviewers)}
        Requested teams:
        {RenderBullets(receipt.RequestedTeams)}
        Reviewer request attempted: `{receipt.ReviewerRequestAttempted.ToString().ToLowerInvariant()}`
        Reviewer request accepted: `{receipt.ReviewerRequestAccepted.ToString().ToLowerInvariant()}`
        Post-state verified: `{receipt.PostStateVerified.ToString().ToLowerInvariant()}`
        Issues:
        {RenderBullets(receipt.Issues)}

        Boundary: Reviewer request execution is not approval. Reviewer request execution is not review completion. Reviewer request execution is not merge readiness. Reviewer request execution is not release readiness. Reviewer request execution does not resolve review threads.
        """;

    private static void RecordExecutionEvent(string outDirectory, ReviewerRequestExecutionReceipt receipt)
    {
        var kind = receipt.ExecutionVerdict == ReviewerRequestExecutionVerdict.Executed
            ? GovernanceKernelEventKind.ReviewerRequestExecuted
            : GovernanceKernelEventKind.ReceiptCreated;
        new FileBackedGovernanceEventStore(outDirectory).Append(
            receipt.ReviewerRequestPackageId,
            receipt.ReviewerRequestExecutionId,
            kind,
            "ReviewerRequestExecutionReceipt",
            receipt.ReviewerRequestExecutionId,
            $"Reviewer request execution returned {receipt.ExecutionVerdict}.",
            ["reviewer-request-execution-receipt.json"]);
    }

    private static ParsedExecute ParseExecute(string[] args)
    {
        string? package = null;
        string? outPath = null;
        string? repo = null;
        string? observedHead = null;
        string? requestedBy = null;
        int? pr = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--package": if (!TryRead(args, ref index, out package)) return ParsedExecute.Fail(json, "--package requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedExecute.Fail(json, "--out requires a value."); break;
                case "--repo": if (!TryRead(args, ref index, out repo)) return ParsedExecute.Fail(json, "--repo requires a value."); break;
                case "--observed-head": if (!TryRead(args, ref index, out observedHead)) return ParsedExecute.Fail(json, "--observed-head requires a value."); break;
                case "--requested-by": if (!TryRead(args, ref index, out requestedBy)) return ParsedExecute.Fail(json, "--requested-by requires a value."); break;
                case "--pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedExecute.Fail(json, "--pr requires a number.");
                    pr = parsedPr;
                    break;
                case "--json": json = true; break;
                default: return ParsedExecute.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package)) return ParsedExecute.Fail(json, "Missing required option: --package <reviewer-request-package.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedExecute.Fail(json, "Missing required option: --repo <owner/name>.");
        if (pr is null) return ParsedExecute.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(observedHead)) return ParsedExecute.Fail(json, "Missing required option: --observed-head <sha>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedExecute.Fail(json, "Missing required option: --out <path>.");
        return new ParsedExecute(package, outPath, repo, pr, observedHead, requestedBy, json, null);
    }

    private static ParsedExecutionRead ParseExecutionRead(string[] args)
    {
        string? receipt = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--receipt": if (!TryRead(args, ref index, out receipt)) return ParsedExecutionRead.Fail(json, "--receipt requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedExecutionRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(receipt)
            ? ParsedExecutionRead.Fail(json, "Missing required option: --receipt <reviewer-request-execution-receipt.json>.")
            : new ParsedExecutionRead(receipt, json, null);
    }

    private static string ResolveExecutionReceiptPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "reviewer-request-execution-receipt.json")
            : fullPath;
    }

    private sealed record ParsedExecute(
        string? PackagePath,
        string? OutPath,
        string? Repository,
        int? PullRequestNumber,
        string? ObservedHeadSha,
        string? RequestedBy,
        bool Json,
        string? Error)
    {
        public static ParsedExecute Fail(bool json, string error) => new(null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedExecutionRead(string? ReceiptPath, bool Json, string? Error)
    {
        public static ParsedExecutionRead Fail(bool json, string error) => new(null, json, error);
    }

    private sealed class GitHubCliReviewerRequestCommandClient(string repository, int pullRequestNumber) : IReviewerRequestCommandClient
    {
        private readonly string _repository = repository;
        private readonly int _pullRequestNumber = pullRequestNumber;

        public async Task<ReviewerRequestExecutionObservedPrState> ObserveAsync(ReviewerRequestExecutionRequest request, CancellationToken cancellationToken)
        {
            var observedAt = DateTimeOffset.UtcNow;
            var result = await RunProcessAsync(
                "gh",
                ["pr", "view", _pullRequestNumber.ToString(), "--repo", _repository, "--json", "number,url,state,isDraft,headRefName,headRefOid,baseRefName,baseRefOid,author,reviewRequests"],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return FailedObservation(observedAt, string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr);

            try
            {
                using var document = JsonDocument.Parse(result.Stdout);
                var root = document.RootElement;
                var requests = root.TryGetProperty("reviewRequests", out var reviewRequests) && reviewRequests.ValueKind == JsonValueKind.Array
                    ? ParseReviewRequests(reviewRequests)
                    : (Reviewers: Array.Empty<string>(), Teams: Array.Empty<string>());
                return new ReviewerRequestExecutionObservedPrState
                {
                    Repository = _repository,
                    PullRequestNumber = TryGetInt(root, "number") ?? _pullRequestNumber,
                    PullRequestUrl = TryGetString(root, "url") ?? string.Empty,
                    PullRequestState = TryGetString(root, "state") ?? string.Empty,
                    PullRequestDraft = TryGetBool(root, "isDraft"),
                    HeadBranch = TryGetString(root, "headRefName") ?? string.Empty,
                    HeadSha = TryGetString(root, "headRefOid") ?? string.Empty,
                    BaseBranch = TryGetString(root, "baseRefName") ?? string.Empty,
                    BaseSha = TryGetString(root, "baseRefOid"),
                    Author = TryGetNestedString(root, "author", "login") ?? string.Empty,
                    RequestedReviewers = requests.Reviewers,
                    RequestedTeams = requests.Teams,
                    ObservedAtUtc = observedAt,
                    ObservationSucceeded = true,
                    ObservationError = null
                };
            }
            catch (JsonException exception)
            {
                return FailedObservation(observedAt, exception.Message);
            }
        }

        public async Task<ReviewerRequestMutationResult> RequestReviewersAsync(ReviewerRequestExecutionRequest request, CancellationToken cancellationToken)
        {
            var reviewers = request.Package?.RequestedReviewers
                .Where(IsExecutableCliTarget)
                .Select(target => target.SlugOrLogin)
                .ToArray() ?? [];
            var teams = request.Package?.RequestedTeams
                .Where(IsExecutableCliTarget)
                .Select(target => target.SlugOrLogin)
                .ToArray() ?? [];
            var arguments = new List<string>
            {
                "api",
                "--method",
                "POST",
                $"repos/{_repository}/pulls/{_pullRequestNumber}/requested_reviewers"
            };
            foreach (var reviewer in reviewers)
            {
                arguments.Add("-f");
                arguments.Add($"reviewers[]={reviewer}");
            }

            foreach (var team in teams)
            {
                arguments.Add("-f");
                arguments.Add($"team_reviewers[]={team}");
            }

            var result = await RunProcessAsync("gh", arguments, Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
            return new ReviewerRequestMutationResult
            {
                Attempted = true,
                Accepted = result.ExitCode == 0,
                Provider = "GitHubCli",
                CommandOrMutationName = "gh api requested_reviewers",
                RequestedReviewers = reviewers,
                RequestedTeams = teams,
                Message = result.Stdout,
                Error = result.ExitCode == 0 ? null : result.Stderr,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private ReviewerRequestExecutionObservedPrState FailedObservation(DateTimeOffset observedAt, string error) => new()
        {
            Repository = _repository,
            PullRequestNumber = _pullRequestNumber,
            PullRequestUrl = string.Empty,
            PullRequestState = string.Empty,
            PullRequestDraft = false,
            HeadBranch = string.Empty,
            HeadSha = string.Empty,
            BaseBranch = string.Empty,
            BaseSha = null,
            Author = string.Empty,
            RequestedReviewers = [],
            RequestedTeams = [],
            ObservedAtUtc = observedAt,
            ObservationSucceeded = false,
            ObservationError = error
        };
    }

    private static bool IsExecutableCliTarget(ReviewerRequestTarget target) =>
        !target.AlreadyRequested &&
        !target.Duplicate &&
        !target.SelfRequest &&
        !target.PullRequestAuthorRequest &&
        string.IsNullOrWhiteSpace(target.BlockedReason);

    private static (string[] Reviewers, string[] Teams) ParseReviewRequests(JsonElement reviewRequests)
    {
        var reviewers = new List<string>();
        var teams = new List<string>();
        foreach (var item in reviewRequests.EnumerateArray())
        {
            var login = TryGetString(item, "login") ?? TryGetString(item, "slug") ?? TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(login))
                continue;
            var type = TryGetString(item, "__typename") ?? TryGetString(item, "type") ?? string.Empty;
            if (type.Contains("team", StringComparison.OrdinalIgnoreCase))
                teams.Add(login);
            else
                reviewers.Add(login);
        }

        return (reviewers.ToArray(), teams.ToArray());
    }

    private static async Task<ReviewerRequestCommandProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ReviewerRequestCommandProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static string? TryGetNestedString(JsonElement element, string propertyName, string childPropertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? TryGetString(value, childPropertyName)
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static bool TryGetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private sealed record ReviewerRequestCommandProcessResult(int ExitCode, string Stdout, string Stderr);
}

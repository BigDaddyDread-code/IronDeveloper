using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static partial class IronDevCliReadyForReview
{
    private static async Task<int> HandleExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "ready execute", parsed.Error);

        var package = ReadJson<ReadyForReviewEligibilityPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, "ready execute", "ready-for-review package is missing or invalid.");

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        var request = new ReadyForReviewExecutionRequest
        {
            Package = package,
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber!.Value,
            ExpectedHeadBranch = package.Target.HeadBranch,
            ExpectedHeadSha = parsed.ObservedHeadSha!,
            ExpectedBaseBranch = package.Target.BaseBranch,
            ExpectedBaseSha = package.Target.BaseSha,
            OutputDirectory = outDirectory,
            RequestedBy = parsed.RequestedBy ?? Environment.UserName,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        var result = await ReadyForReviewExecutor.ExecuteAsync(
            request,
            new GitHubCliReadyForReviewCommandClient(parsed.Repository!, parsed.PullRequestNumber.Value),
            cancellationToken).ConfigureAwait(false);
        if (result.Receipt is not null)
        {
            await WriteExecutionReceiptAsync(outDirectory, result.Receipt, cancellationToken).ConfigureAwait(false);
            RecordExecutionEvent(outDirectory, result.Receipt);
        }

        if (parsed.Json)
            WriteJson(output, "ready execute", result.Verdict == ReadyForReviewExecutionVerdict.Executed ? "succeeded" : "blocked", new { outDirectory, receipt = result.Receipt }, result.Issues);
        else if (result.Verdict == ReadyForReviewExecutionVerdict.Executed && result.Receipt is not null)
        {
            output.WriteLine("PR marked ready for review.");
            output.WriteLine($"PR: {result.Receipt.Repository}#{result.Receipt.PullRequestNumber}");
            output.WriteLine("Boundary: ready execution did not request reviewers, approve, merge, release, deploy, or continue workflow.");
        }
        else
        {
            error.WriteLine($"Ready-for-review execution blocked: {string.Join(", ", result.Issues)}");
        }

        return result.Verdict == ReadyForReviewExecutionVerdict.Executed ? 0 : 1;
    }

    private static int HandleExecutionRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseExecutionRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"ready {mode}", parsed.Error);

        var receipt = ReadJson<ReadyForReviewExecutionReceipt>(ResolveExecutionReceiptPath(parsed.ReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, $"ready {mode}", "ready-for-review execution receipt is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "execution-records"
                ? new { receipt.PreState, receipt.PostState, receipt.ReadyTransitionAttempted, receipt.ReadyTransitionAccepted, receipt.PostStateVerified, receipt.Issues, receipt.Boundary }
                : new { receipt.ReadyForReviewExecutionId, receipt.ReadyForReviewPackageId, receipt.Repository, receipt.PullRequestNumber, receipt.ExecutionVerdict, receipt.FailureClassification, receipt.PostStateVerified, receipt.Boundary };
            WriteJson(output, $"ready {mode}", "succeeded", data, []);
        }
        else if (mode == "execution-records")
        {
            output.WriteLine($"Ready transition attempted: {receipt.ReadyTransitionAttempted}");
            output.WriteLine($"Ready transition accepted: {receipt.ReadyTransitionAccepted}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine($"Issues: {RenderInline(receipt.Issues)}");
        }
        else
        {
            output.WriteLine($"Ready-for-review execution: {receipt.ExecutionVerdict}");
            output.WriteLine($"PR: {receipt.Repository}#{receipt.PullRequestNumber}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine("Boundary: status is read-only and does not request reviewers, approve, merge, release, deploy, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteExecutionReceiptAsync(string outDirectory, ReadyForReviewExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "ready-for-review-execution-receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "ready-for-review-execution-receipt.md"), RenderExecutionReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderExecutionReceipt(ReadyForReviewExecutionReceipt receipt) => $"""
        # Ready-for-Review Execution Receipt

        Verdict: `{receipt.ExecutionVerdict}`
        Failure classification: `{receipt.FailureClassification}`
        Package: `{receipt.ReadyForReviewPackageId}`
        PR: `{receipt.Repository}#{receipt.PullRequestNumber}`
        Expected head branch: `{receipt.ExpectedHeadBranch}`
        Expected head SHA: `{receipt.ExpectedHeadSha}`
        Ready transition attempted: `{receipt.ReadyTransitionAttempted.ToString().ToLowerInvariant()}`
        Ready transition accepted: `{receipt.ReadyTransitionAccepted.ToString().ToLowerInvariant()}`
        Post-state verified: `{receipt.PostStateVerified.ToString().ToLowerInvariant()}`
        Issues:
        {RenderBullets(receipt.Issues)}

        Boundary: Ready-for-review execution is not reviewer request. Ready-for-review execution is not approval. Ready-for-review execution is not merge readiness. Ready-for-review execution is not release readiness.
        """;

    private static void RecordExecutionEvent(string outDirectory, ReadyForReviewExecutionReceipt receipt)
    {
        var kind = receipt.ExecutionVerdict == ReadyForReviewExecutionVerdict.Executed
            ? GovernanceKernelEventKind.ReadyForReviewExecuted
            : GovernanceKernelEventKind.ReceiptCreated;
        new FileBackedGovernanceEventStore(outDirectory).Append(
            receipt.ReadyForReviewPackageId,
            receipt.ReadyForReviewExecutionId,
            kind,
            "ReadyForReviewExecutionReceipt",
            receipt.ReadyForReviewExecutionId,
            $"Ready-for-review execution returned {receipt.ExecutionVerdict}.",
            ["ready-for-review-execution-receipt.json"]);
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

        if (string.IsNullOrWhiteSpace(package)) return ParsedExecute.Fail(json, "Missing required option: --package <ready-for-review-package.json>.");
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
            ? ParsedExecutionRead.Fail(json, "Missing required option: --receipt <ready-for-review-execution-receipt.json>.")
            : new ParsedExecutionRead(receipt, json, null);
    }

    private static string ResolveExecutionReceiptPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "ready-for-review-execution-receipt.json")
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

    private sealed class GitHubCliReadyForReviewCommandClient(string repository, int pullRequestNumber) : IReadyForReviewCommandClient
    {
        private readonly string _repository = repository;
        private readonly int _pullRequestNumber = pullRequestNumber;

        public async Task<ReadyForReviewObservedPrState> ObserveAsync(ReadyForReviewExecutionRequest request, CancellationToken cancellationToken)
        {
            var observedAt = DateTimeOffset.UtcNow;
            var result = await RunProcessAsync(
                "gh",
                ["pr", "view", _pullRequestNumber.ToString(), "--repo", _repository, "--json", "number,url,state,isDraft,headRefName,headRefOid,baseRefName,baseRefOid"],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return new ReadyForReviewObservedPrState
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
                    ObservedAtUtc = observedAt,
                    ObservationSucceeded = false,
                    ObservationError = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr
                };
            }

            try
            {
                using var document = JsonDocument.Parse(result.Stdout);
                var root = document.RootElement;
                return new ReadyForReviewObservedPrState
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
                    ObservedAtUtc = observedAt,
                    ObservationSucceeded = true,
                    ObservationError = null
                };
            }
            catch (JsonException exception)
            {
                return new ReadyForReviewObservedPrState
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
                    ObservedAtUtc = observedAt,
                    ObservationSucceeded = false,
                    ObservationError = exception.Message
                };
            }
        }

        public async Task<ReadyForReviewMutationResult> MarkReadyAsync(ReadyForReviewExecutionRequest request, CancellationToken cancellationToken)
        {
            var result = await RunProcessAsync(
                "gh",
                ["pr", "ready", _pullRequestNumber.ToString(), "--repo", _repository],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            return new ReadyForReviewMutationResult
            {
                Attempted = true,
                Accepted = result.ExitCode == 0,
                Provider = "GitHubCli",
                CommandOrMutationName = "gh pr ready",
                Message = result.Stdout,
                Error = result.ExitCode == 0 ? null : result.Stderr,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private static async Task<ReadyCommandProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
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
        return new ReadyCommandProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static bool TryGetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private sealed record ReadyCommandProcessResult(int ExitCode, string Stdout, string Stderr);
}

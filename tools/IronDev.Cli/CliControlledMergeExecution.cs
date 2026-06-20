using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliControlledMergeExecution
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
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
        "continue-workflow",
        "push",
        "commit"
    ];

    public static bool IsMergeCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "merge", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "merge requires a subcommand: execute, execution-status, or execution-records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"merge {args[1]} is intentionally unsupported; Block AY only executes the package-selected PR merge.");

        return subcommand switch
        {
            "execute" => await HandleExecuteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "execution-status" => HandleExecutionRead(args, output, error, "execution-status"),
            "execution-records" => HandleExecutionRead(args, output, error, "execution-records"),
            _ => Usage(error, $"unsupported merge subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "merge execute", parsed.Error);

        var package = ReadJson<MergeDecisionPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, "merge execute", "merge decision package is missing or invalid.");

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        var request = new MergeExecutionRequest
        {
            Package = package,
            Repository = parsed.Repository!,
            PullRequestNumber = parsed.PullRequestNumber!.Value,
            ExpectedHeadBranch = package.HeadBranch,
            ExpectedHeadSha = parsed.ObservedHeadSha!,
            ExpectedBaseBranch = package.BaseBranch,
            ExpectedBaseSha = package.BaseSha,
            RequestedBy = parsed.RequestedBy ?? package.CreatedBy,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            OutputDirectory = outDirectory,
            DryRun = parsed.DryRun
        };
        var result = await ControlledMergeExecutor.ExecuteAsync(
            request,
            new GitHubCliControlledMergeCommandClient(parsed.Repository!, parsed.PullRequestNumber.Value),
            cancellationToken).ConfigureAwait(false);

        if (result.Receipt is not null)
        {
            await WriteExecutionReceiptAsync(outDirectory, result.Receipt, cancellationToken).ConfigureAwait(false);
            RecordExecutionEvent(outDirectory, result.Receipt);
        }

        if (parsed.Json)
            WriteJson(output, "merge execute", result.Verdict == MergeExecutionVerdict.Executed ? "succeeded" : "blocked", new { outDirectory, receipt = result.Receipt }, result.Issues);
        else if (result.Verdict == MergeExecutionVerdict.Executed && result.Receipt is not null)
        {
            output.WriteLine("Pull request merged.");
            output.WriteLine($"PR: {result.Receipt.Repository}#{result.Receipt.PullRequestNumber}");
            output.WriteLine($"Merge commit: {result.Receipt.MergeCommitSha ?? "unknown"}");
            output.WriteLine("Boundary: merge execution did not release, deploy, tag, publish, promote memory, or continue workflow.");
        }
        else
        {
            error.WriteLine($"Merge execution blocked: {string.Join(", ", result.Issues)}");
        }

        return result.Verdict == MergeExecutionVerdict.Executed ? 0 : 1;
    }

    private static int HandleExecutionRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseExecutionRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"merge {mode}", parsed.Error);

        var receipt = ReadJson<MergeExecutionReceipt>(ResolveReceiptPath(parsed.ReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, $"merge {mode}", "merge execution receipt is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "execution-records"
                ? new { receipt.PreState, receipt.PostState, receipt.MergeAttempted, receipt.MergeAccepted, receipt.PostStateVerified, receipt.Issues, receipt.Boundary }
                : new { receipt.MergeExecutionId, receipt.MergeDecisionPackageId, receipt.Repository, receipt.PullRequestNumber, receipt.ExecutionVerdict, receipt.FailureClassification, receipt.PostStateVerified, receipt.MergeCommitSha, receipt.Boundary };
            WriteJson(output, $"merge {mode}", "succeeded", data, []);
        }
        else if (mode == "execution-records")
        {
            output.WriteLine($"Merge attempted: {receipt.MergeAttempted}");
            output.WriteLine($"Merge accepted: {receipt.MergeAccepted}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine($"Issues: {RenderInline(receipt.Issues)}");
        }
        else
        {
            output.WriteLine($"Merge execution: {receipt.ExecutionVerdict}");
            output.WriteLine($"PR: {receipt.Repository}#{receipt.PullRequestNumber}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine("Boundary: status is read-only and does not release, deploy, tag, publish, promote memory, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteExecutionReceiptAsync(string outDirectory, MergeExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "merge-execution-receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "merge-execution-receipt.md"), RenderExecutionReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderExecutionReceipt(MergeExecutionReceipt receipt) => $"""
        # Merge Execution Receipt

        Verdict: `{receipt.ExecutionVerdict}`
        Failure classification: `{receipt.FailureClassification}`
        Package: `{receipt.MergeDecisionPackageId}`
        PR: `{receipt.Repository}#{receipt.PullRequestNumber}`
        Expected head branch: `{receipt.ExpectedHeadBranch}`
        Expected head SHA: `{receipt.ExpectedHeadSha}`
        Expected base branch: `{receipt.ExpectedBaseBranch}`
        Selected merge strategy: `{receipt.SelectedMergeStrategy}`
        Merge commit: `{receipt.MergeCommitSha ?? "none"}`
        Merge attempted: `{receipt.MergeAttempted.ToString().ToLowerInvariant()}`
        Merge accepted: `{receipt.MergeAccepted.ToString().ToLowerInvariant()}`
        Post-state verified: `{receipt.PostStateVerified.ToString().ToLowerInvariant()}`
        Issues:
        {RenderBullets(receipt.Issues)}

        Merge decision package is not merge execution.
        Merge execution is not release.
        Merge execution is not deployment.
        Merge execution is not tag creation.
        Merge execution is not publishing.
        Merge execution is not memory promotion.
        Merge execution is not workflow continuation.
        """;

    private static void RecordExecutionEvent(string outDirectory, MergeExecutionReceipt receipt)
    {
        var kind = receipt.ExecutionVerdict == MergeExecutionVerdict.Executed
            ? GovernanceKernelEventKind.MergeExecuted
            : GovernanceKernelEventKind.ReceiptCreated;
        new FileBackedGovernanceEventStore(outDirectory).Append(
            receipt.MergeDecisionPackageId,
            receipt.MergeExecutionId,
            kind,
            "MergeExecutionReceipt",
            receipt.MergeExecutionId,
            $"Merge execution returned {receipt.ExecutionVerdict}.",
            ["merge-execution-receipt.json"]);
    }

    private static ParsedExecute ParseExecute(string[] args)
    {
        string? package = null;
        string? outPath = null;
        string? repo = null;
        string? observedHead = null;
        string? requestedBy = null;
        int? pr = null;
        var dryRun = false;
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
                case "--dry-run": dryRun = true; break;
                case "--json": json = true; break;
                default: return ParsedExecute.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package)) return ParsedExecute.Fail(json, "Missing required option: --package <merge-decision-package.json>.");
        if (string.IsNullOrWhiteSpace(repo)) return ParsedExecute.Fail(json, "Missing required option: --repo <owner/name>.");
        if (pr is null) return ParsedExecute.Fail(json, "Missing required option: --pr <number>.");
        if (string.IsNullOrWhiteSpace(observedHead)) return ParsedExecute.Fail(json, "Missing required option: --observed-head <sha>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedExecute.Fail(json, "Missing required option: --out <path>.");
        return new ParsedExecute(package, outPath, repo, pr, observedHead, requestedBy, dryRun, json, null);
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
            ? ParsedExecutionRead.Fail(json, "Missing required option: --receipt <merge-execution-receipt.json>.")
            : new ParsedExecutionRead(receipt, json, null);
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
            ? Path.Combine(fullPath, "merge-decision-package.json")
            : fullPath;
    }

    private static string ResolveReceiptPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "merge-execution-receipt.json")
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
        error.WriteLine("  irondev merge execute --package <merge-decision-package.json> --repo <owner/name> --pr <number> --observed-head <sha> --out <path> [--requested-by <login>] [--dry-run] [--json]");
        error.WriteLine("  irondev merge execution-status --receipt <merge-execution-receipt.json> [--json]");
        error.WriteLine("  irondev merge execution-records --receipt <merge-execution-receipt.json> [--json]");
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
            boundary = MergeExecutionBoundary.Executor
        }, JsonOptions));
    }

    private sealed record ParsedExecute(
        string? PackagePath,
        string? OutPath,
        string? Repository,
        int? PullRequestNumber,
        string? ObservedHeadSha,
        string? RequestedBy,
        bool DryRun,
        bool Json,
        string? Error)
    {
        public static ParsedExecute Fail(bool json, string error) => new(null, null, null, null, null, null, false, json, error);
    }

    private sealed record ParsedExecutionRead(string? ReceiptPath, bool Json, string? Error)
    {
        public static ParsedExecutionRead Fail(bool json, string error) => new(null, json, error);
    }

    private sealed class GitHubCliControlledMergeCommandClient(string repository, int pullRequestNumber) : IControlledMergeCommandClient
    {
        private readonly string _repository = repository;
        private readonly int _pullRequestNumber = pullRequestNumber;

        public async Task<MergeExecutionObservedPrState> ObserveAsync(MergeExecutionRequest request, CancellationToken cancellationToken)
        {
            var observedAt = DateTimeOffset.UtcNow;
            var result = await RunProcessAsync(
                "gh",
                ["pr", "view", _pullRequestNumber.ToString(), "--repo", _repository, "--json", "number,url,state,isDraft,headRefName,headRefOid,baseRefName,baseRefOid,author,mergeable,mergeStateStatus,merged,mergeCommit"],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return FailedObservation(observedAt, string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr);

            try
            {
                using var document = JsonDocument.Parse(result.Stdout);
                var root = document.RootElement;
                var state = TryGetString(root, "state") ?? string.Empty;
                var mergeableText = TryGetRawString(root, "mergeable") ?? string.Empty;
                var mergeStateStatus = TryGetString(root, "mergeStateStatus") ?? string.Empty;
                var merged = TryGetBool(root, "merged") || state.Equals("merged", StringComparison.OrdinalIgnoreCase);
                return new MergeExecutionObservedPrState
                {
                    Repository = _repository,
                    PullRequestNumber = TryGetInt(root, "number") ?? _pullRequestNumber,
                    PullRequestUrl = TryGetString(root, "url") ?? string.Empty,
                    PullRequestState = state,
                    PullRequestDraft = TryGetBool(root, "isDraft"),
                    HeadBranch = TryGetString(root, "headRefName") ?? string.Empty,
                    HeadSha = TryGetString(root, "headRefOid") ?? string.Empty,
                    BaseBranch = TryGetString(root, "baseRefName") ?? string.Empty,
                    BaseSha = TryGetString(root, "baseRefOid"),
                    Author = TryGetNestedString(root, "author", "login") ?? string.Empty,
                    Mergeable = ParseMergeable(mergeableText),
                    MergeStateStatus = mergeStateStatus,
                    IsBehindBase = mergeStateStatus.Contains("behind", StringComparison.OrdinalIgnoreCase),
                    HasConflicts = mergeStateStatus.Contains("dirty", StringComparison.OrdinalIgnoreCase) ||
                        mergeStateStatus.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
                        mergeableText.Contains("conflict", StringComparison.OrdinalIgnoreCase),
                    Merged = merged,
                    MergeCommitSha = TryGetNestedString(root, "mergeCommit", "oid"),
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

        public async Task<MergeMutationResult> MergeAsync(MergeExecutionRequest request, CancellationToken cancellationToken)
        {
            var method = ControlledMergeExecutor.MergeMethodForStrategy(request.Package?.SelectedMergeStrategy);
            var arguments = new List<string>
            {
                "api",
                "--method",
                "PUT",
                $"repos/{_repository}/pulls/{_pullRequestNumber}/merge",
                "-f",
                $"sha={request.ExpectedHeadSha}",
                "-f",
                $"merge_method={method}"
            };
            var result = await RunProcessAsync("gh", arguments, Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
            var mergeCommitSha = TryReadMergeSha(result.Stdout);
            return new MergeMutationResult
            {
                Attempted = true,
                Accepted = result.ExitCode == 0 && !ResponseSaysNotMerged(result.Stdout),
                Provider = "GitHubCli",
                CommandOrMutationName = "GitHub REST PR merge",
                MergeStrategy = request.Package?.SelectedMergeStrategy ?? string.Empty,
                ExpectedHeadSha = request.ExpectedHeadSha,
                MergeCommitSha = mergeCommitSha,
                Message = result.Stdout,
                Error = result.ExitCode == 0 ? null : result.Stderr,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private MergeExecutionObservedPrState FailedObservation(DateTimeOffset observedAt, string error) => new()
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
            Mergeable = false,
            MergeStateStatus = string.Empty,
            IsBehindBase = false,
            HasConflicts = false,
            Merged = false,
            MergeCommitSha = null,
            ObservedAtUtc = observedAt,
            ObservationSucceeded = false,
            ObservationError = error
        };
    }

    private static async Task<MergeCommandProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
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
        return new MergeCommandProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string? TryReadMergeSha(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            return TryGetString(document.RootElement, "sha");
        }
        catch
        {
            return null;
        }
    }

    private static bool ResponseSaysNotMerged(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            return document.RootElement.TryGetProperty("merged", out var merged) &&
                merged.ValueKind == JsonValueKind.False;
        }
        catch
        {
            return false;
        }
    }

    private static bool ParseMergeable(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("mergeable", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static string? TryGetRawString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

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

    private sealed record MergeCommandProcessResult(int ExitCode, string Stdout, string Stderr);
}

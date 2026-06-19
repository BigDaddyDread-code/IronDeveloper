using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliPrBranchUpdate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "approve",
        "ready",
        "mark-ready",
        "request-reviewers",
        "reviewers",
        "resolve-comments",
        "resolve",
        "reply",
        "auto-merge",
        "enable-auto-merge",
        "merge",
        "release",
        "deploy",
        "tag",
        "publish",
        "promote-memory",
        "continue",
        "continue-workflow"
    ];

    public static bool IsPrBranchUpdateCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "pr-branch-update", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "pr-branch-update requires a subcommand: execute, status, records, or rollback-plan.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"pr-branch-update {args[1]} is intentionally unsupported; Block AS updates only the expected PR branch.");

        return subcommand switch
        {
            "execute" => await HandleExecuteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => HandleRead(args, output, error, "status"),
            "records" => HandleRead(args, output, error, "records"),
            "rollback-plan" => HandleRead(args, output, error, "rollback-plan"),
            _ => Usage(error, $"unsupported pr-branch-update subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "pr-branch-update execute", parsed.Error);

        var package = ReadJson<ControlledPrUpdatePackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, "pr-branch-update execute", "PR update package is missing or invalid.");

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        var request = new PrBranchUpdateExecutionRequest
        {
            Package = package,
            ExpectedPullRequestNumber = parsed.ExpectedPullRequestNumber,
            WorkspacePath = parsed.WorkspacePath,
            TargetRemote = parsed.Remote,
            RequestedBy = parsed.RequestedBy ?? Environment.UserName,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        var result = await PrBranchUpdateExecutor.ExecuteAsync(request, new GitPrBranchUpdateCommandClient(parsed.WorkspacePath!, package.BranchUpdateConstraints.TargetRemote), cancellationToken).ConfigureAwait(false);
        if (result.Receipt is not null)
        {
            await WriteReceiptAsync(outDirectory, result.Receipt, cancellationToken).ConfigureAwait(false);
            RecordEvent(outDirectory, result.Receipt);
        }

        if (parsed.Json)
            WriteJson(output, "pr-branch-update execute", result.Verdict == PrBranchUpdateExecutionVerdict.Executed ? "succeeded" : "blocked", new { outDirectory, receipt = result.Receipt }, result.Issues);
        else if (result.Verdict == PrBranchUpdateExecutionVerdict.Executed && result.Receipt is not null)
        {
            output.WriteLine("PR branch updated.");
            output.WriteLine($"Commit: {result.Receipt.CommitSha}");
            output.WriteLine("Boundary: branch update did not mark ready, request reviewers, merge, release, deploy, or continue workflow.");
        }
        else
        {
            error.WriteLine($"PR branch update blocked: {string.Join(", ", result.Issues)}");
        }

        return result.Verdict == PrBranchUpdateExecutionVerdict.Executed ? 0 : 1;
    }

    private static int HandleRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"pr-branch-update {mode}", parsed.Error);

        var receipt = ReadJson<PrBranchUpdateExecutionReceipt>(ResolveReceiptPath(parsed.ReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, $"pr-branch-update {mode}", "PR branch update receipt is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode switch
            {
                "records" => new { receipt.SourceApplyReceipt, receipt.ValidationReceipts, receipt.ExpectedFilesChanged, receipt.ActualFilesChanged, receipt.Issues, receipt.Boundary },
                "rollback-plan" => new { receipt.RollbackAvailable, receipt.RollbackInstructions, receipt.PreExecutionHeadSha, receipt.PostExecutionHeadSha, receipt.Boundary },
                _ => new { receipt.ExecutionId, receipt.PackageId, receipt.Repository, receipt.PrNumber, receipt.Branch, receipt.ExecutionVerdict, receipt.FailureClassification, receipt.Pushed, receipt.Boundary }
            };
            WriteJson(output, $"pr-branch-update {mode}", "succeeded", data, []);
        }
        else if (mode == "rollback-plan")
        {
            output.WriteLine($"Rollback available: {receipt.RollbackAvailable}");
            output.WriteLine(receipt.RollbackInstructions);
            output.WriteLine("Boundary: rollback-plan is read-only; no rollback was executed.");
        }
        else if (mode == "records")
        {
            output.WriteLine($"Source apply receipt: {receipt.SourceApplyReceipt ?? "missing"}");
            output.WriteLine($"Validation receipts: {receipt.ValidationReceipts.Length}");
            output.WriteLine($"Expected files: {string.Join(", ", receipt.ExpectedFilesChanged.DefaultIfEmpty("none"))}");
            output.WriteLine($"Actual files: {string.Join(", ", receipt.ActualFilesChanged.DefaultIfEmpty("none"))}");
        }
        else
        {
            output.WriteLine($"PR branch update: {receipt.ExecutionVerdict}");
            output.WriteLine($"Branch: {receipt.Branch}");
            output.WriteLine($"Commit: {receipt.CommitSha}");
            output.WriteLine("Boundary: status is read-only and does not mark ready, request reviewers, merge, release, deploy, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteReceiptAsync(string outDirectory, PrBranchUpdateExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "pr-branch-update-execution-receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "pr-branch-update-execution-receipt.md"), RenderReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderReceipt(PrBranchUpdateExecutionReceipt receipt) => $"""
        # PR Branch Update Execution Receipt

        Verdict: `{receipt.ExecutionVerdict}`
        Package: `{receipt.PackageId}`
        PR: `{receipt.Repository}#{receipt.PrNumber}`
        Branch: `{receipt.Branch}`
        Pre-execution head: `{receipt.PreExecutionHeadSha}`
        Post-execution head: `{receipt.PostExecutionHeadSha}`
        Commit: `{receipt.CommitSha}`
        Pushed: `{receipt.Pushed.ToString().ToLowerInvariant()}`
        Expected files:
        {RenderBullets(receipt.ExpectedFilesChanged)}
        Actual files:
        {RenderBullets(receipt.ActualFilesChanged)}
        Issues:
        {RenderBullets(receipt.Issues)}

        Boundary: AS updates only the expected PR branch. It does not approve, mark ready, request reviewers, resolve review threads, merge, release, deploy, tag, publish, promote memory, or continue workflow.
        """;

    private static void RecordEvent(string outDirectory, PrBranchUpdateExecutionReceipt receipt) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            receipt.PackageId,
            receipt.ExecutionId,
            GovernanceKernelEventKind.PrBranchUpdateExecuted,
            "PrBranchUpdateExecutionReceipt",
            receipt.ExecutionId,
            $"PR branch update execution returned {receipt.ExecutionVerdict}.",
            ["pr-branch-update-execution-receipt.json"]);

    private static ParsedExecute ParseExecute(string[] args)
    {
        string? package = null;
        string? workspace = null;
        string? outPath = null;
        string? remote = null;
        string? requestedBy = null;
        int? expectedPr = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--package": if (!TryRead(args, ref index, out package)) return ParsedExecute.Fail(json, "--package requires a value."); break;
                case "--workspace": if (!TryRead(args, ref index, out workspace)) return ParsedExecute.Fail(json, "--workspace requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedExecute.Fail(json, "--out requires a value."); break;
                case "--remote": if (!TryRead(args, ref index, out remote)) return ParsedExecute.Fail(json, "--remote requires a value."); break;
                case "--requested-by": if (!TryRead(args, ref index, out requestedBy)) return ParsedExecute.Fail(json, "--requested-by requires a value."); break;
                case "--expected-pr":
                    if (!TryRead(args, ref index, out var prValue) || !int.TryParse(prValue, out var parsedPr)) return ParsedExecute.Fail(json, "--expected-pr requires a number.");
                    expectedPr = parsedPr;
                    break;
                case "--json": json = true; break;
                default: return ParsedExecute.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package)) return ParsedExecute.Fail(json, "Missing required option: --package <pr-update-package.json>.");
        if (string.IsNullOrWhiteSpace(workspace)) return ParsedExecute.Fail(json, "Missing required option: --workspace <repo-path>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedExecute.Fail(json, "Missing required option: --out <path>.");
        return new ParsedExecute(package, Path.GetFullPath(workspace), outPath, remote, expectedPr, requestedBy, json, null);
    }

    private static ParsedRead ParseRead(string[] args)
    {
        string? receipt = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--receipt": if (!TryRead(args, ref index, out receipt)) return ParsedRead.Fail(json, "--receipt requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(receipt)
            ? ParsedRead.Fail(json, "Missing required option: --receipt <pr-branch-update-execution-receipt.json>.")
            : new ParsedRead(receipt, json, null);
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

    private static string ResolveReceiptPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "pr-branch-update-execution-receipt.json")
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
        error.WriteLine("  irondev pr-branch-update execute --package <package.json> --workspace <repo-path> --out <path> [--remote origin] [--expected-pr <number>] [--json]");
        error.WriteLine("  irondev pr-branch-update status --receipt <receipt.json> [--json]");
        error.WriteLine("  irondev pr-branch-update records --receipt <receipt.json> [--json]");
        error.WriteLine("  irondev pr-branch-update rollback-plan --receipt <receipt.json> [--json]");
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
            boundary = PrBranchUpdateBoundaryText.Boundary
        }, JsonOptions));
    }

    private sealed record ParsedExecute(
        string? PackagePath,
        string? WorkspacePath,
        string? OutPath,
        string? Remote,
        int? ExpectedPullRequestNumber,
        string? RequestedBy,
        bool Json,
        string? Error)
    {
        public static ParsedExecute Fail(bool json, string error) => new(null, null, null, null, null, null, json, error);
    }

    private sealed record ParsedRead(string? ReceiptPath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }

    private sealed class GitPrBranchUpdateCommandClient(string workspacePath, string remote) : IPrBranchUpdateCommandClient
    {
        private readonly string _workspacePath = Path.GetFullPath(workspacePath);
        private readonly string _remote = remote;

        public async Task<PrBranchUpdateObservedState> ObserveAsync(PrBranchUpdateExecutionRequest request, CancellationToken cancellationToken)
        {
            var package = request.Package;
            var repositoryAvailable = Directory.Exists(_workspacePath) && Directory.Exists(Path.Combine(_workspacePath, ".git"));
            if (!repositoryAvailable || package is null)
            {
                return new PrBranchUpdateObservedState
                {
                    RepositoryAvailable = false,
                    Repository = package?.Target.Repository ?? string.Empty,
                    Branch = string.Empty,
                    HeadSha = string.Empty,
                    RemoteHeadSha = string.Empty,
                    DiffHash = string.Empty
                };
            }

            var branch = await GitValueAsync(["branch", "--show-current"], cancellationToken).ConfigureAwait(false);
            var head = await GitValueAsync(["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
            var statusResult = await RunGitAsync(["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
            var status = statusResult.ExitCode == 0 ? statusResult.Stdout : string.Empty;
            var statusEntries = ParseStatus(status);
            var staged = statusEntries.Where(item => item.Staged).Select(item => item.Path).ToArray();
            var dirty = statusEntries.Select(item => item.Path).ToArray();
            var generated = dirty.Where(PrBranchUpdateExecutor.IsGeneratedRestoreArtifact).ToArray();
            var diffResult = await RunGitAsync(["diff", "--binary", "--", .. package.ExpectedState.ExpectedChangedFiles], cancellationToken).ConfigureAwait(false);
            var diff = diffResult.ExitCode == 0 ? diffResult.Stdout : string.Empty;
            var remoteHead = await RemoteHeadAsync(package.Target.TargetBranch, cancellationToken).ConfigureAwait(false);

            return new PrBranchUpdateObservedState
            {
                RepositoryAvailable = true,
                Repository = package.Target.Repository,
                Branch = branch,
                HeadSha = head,
                RemoteHeadSha = remoteHead,
                DirtyFiles = dirty,
                StagedFiles = staged,
                GeneratedRestoreArtifacts = generated,
                DiffHash = PrBranchUpdateExecutor.ComputeDiffHash(diff),
                ForcePushRequired = false
            };
        }

        public async Task<PrBranchUpdateCommandResult> StageAsync(ControlledPrUpdatePackage package, string[] expectedFiles, CancellationToken cancellationToken)
        {
            var result = await RunGitAsync(["add", "--", .. expectedFiles], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new PrBranchUpdateCommandResult { Succeeded = false, Messages = [result.Stderr, result.Stdout] };
            var staged = await GitValueAsync(["diff", "--cached", "--name-only"], cancellationToken).ConfigureAwait(false);
            return new PrBranchUpdateCommandResult
            {
                Succeeded = true,
                StagedFiles = staged.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim().Replace('\\', '/')).ToArray()
            };
        }

        public async Task<PrBranchUpdateCommandResult> CommitAsync(ControlledPrUpdatePackage package, CancellationToken cancellationToken)
        {
            var message = package.ExpectedState.ExpectedCommitMessage;
            var body = string.IsNullOrWhiteSpace(package.ExpectedState.ExpectedCommitBody)
                ? "Controlled PR branch update."
                : package.ExpectedState.ExpectedCommitBody;
            var result = await RunGitAsync(["commit", "-m", message, "-m", body], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return new PrBranchUpdateCommandResult { Succeeded = false, Messages = [result.Stderr, result.Stdout] };
            var head = await GitValueAsync(["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
            var changedFiles = await GitValueAsync(["diff-tree", "--no-commit-id", "--name-only", "-r", head], cancellationToken).ConfigureAwait(false);
            return new PrBranchUpdateCommandResult
            {
                Succeeded = true,
                CommitSha = head,
                PostHeadSha = head,
                ChangedFiles = changedFiles.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim().Replace('\\', '/')).ToArray(),
                Messages = [result.Stdout]
            };
        }

        public async Task<PrBranchUpdateCommandResult> PushAsync(ControlledPrUpdatePackage package, string remote, string branch, CancellationToken cancellationToken)
        {
            var result = await RunGitAsync(["push", remote, $"HEAD:refs/heads/{branch}"], cancellationToken).ConfigureAwait(false);
            var nonFastForward = result.Stderr.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
                                 result.Stdout.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
                                 result.Stderr.Contains("fetch first", StringComparison.OrdinalIgnoreCase);
            if (result.ExitCode != 0)
                return new PrBranchUpdateCommandResult { Succeeded = false, NonFastForward = nonFastForward, Messages = [result.Stderr, result.Stdout] };
            var remoteHead = await RemoteHeadAsync(branch, cancellationToken).ConfigureAwait(false);
            return new PrBranchUpdateCommandResult { Succeeded = true, RemoteHeadSha = remoteHead, Messages = [result.Stdout, result.Stderr] };
        }

        private async Task<string> RemoteHeadAsync(string branch, CancellationToken cancellationToken)
        {
            var result = await RunGitAsync(["ls-remote", _remote, $"refs/heads/{branch}"], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return string.Empty;
            return result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split('\t').FirstOrDefault()?.Trim() ?? string.Empty;
        }

        private async Task<string> GitValueAsync(string[] arguments, CancellationToken cancellationToken)
        {
            var result = await RunGitAsync(arguments, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.Stdout.Trim() : string.Empty;
        }

        private async Task<ProcessRunResult> RunGitAsync(string[] arguments, CancellationToken cancellationToken)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _workspacePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ProcessRunResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }

        private static StatusEntry[] ParseStatus(string status)
        {
            return status.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseLine)
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .ToArray();
        }

        private static StatusEntry ParseLine(string line)
        {
            if (line.Length < 4)
                return new StatusEntry(string.Empty, false);
            var path = line[3..].Trim().Replace('\\', '/');
            var marker = " -> ";
            var markerIndex = path.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
                path = path[(markerIndex + marker.Length)..];
            return new StatusEntry(path.Trim('"'), line[0] != ' ' && line[0] != '?');
        }

        private sealed record StatusEntry(string Path, bool Staged);
        private sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr);
    }
}

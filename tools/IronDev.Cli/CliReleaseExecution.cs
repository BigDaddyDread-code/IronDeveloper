using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliReleaseExecution
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "deploy",
        "publish-package",
        "publish",
        "promote-memory",
        "continue",
        "continue-workflow",
        "commit",
        "push",
        "merge",
        "source-apply",
        "rollback",
        "rollback-execute",
        "approve",
        "ready",
        "request-reviewers"
    ];

    public static bool IsReleaseExecutionCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "release-execution", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "release-execution requires a subcommand: execute, status, records, or inspect.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"release-execution {args[1]} is intentionally unsupported; Block BB only creates the expected tag, GitHub release, and release artifacts.");

        return subcommand switch
        {
            "execute" => await HandleExecuteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => HandleReceiptRead(args, output, error, "status"),
            "records" => HandleReceiptRead(args, output, error, "records"),
            "inspect" => HandleReceiptRead(args, output, error, "inspect"),
            _ => Usage(error, $"unsupported release-execution subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, "release-execution execute", parsed.Error);

        var package = ReadJson<ReleaseReadinessDecisionPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, "release-execution execute", "release readiness decision package is missing or invalid.");

        var requestPath = Path.GetFullPath(parsed.RequestPath!);
        var request = ReadJson<ReleaseExecutionRequest>(requestPath);
        if (request is null)
            return Failure(output, error, parsed.Json, "release-execution execute", "release execution request is missing or invalid.");

        request = NormalizeRequestPaths(request, requestPath);
        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        request = request with { OutputDirectory = outDirectory };

        var result = await ControlledReleaseExecutor.ExecuteAsync(
            package,
            request,
            new GitHubCliReleaseExecutionGateway(),
            cancellationToken).ConfigureAwait(false);

        if (result.Receipt is not null)
        {
            await WriteExecutionReceiptAsync(outDirectory, result.Receipt, cancellationToken).ConfigureAwait(false);
            RecordExecutionEvent(outDirectory, result.Receipt);
        }

        if (parsed.Json)
            WriteJson(output, "release-execution execute", result.Verdict == ReleaseExecutionVerdict.ExecutedAndVerified ? "succeeded" : "blocked", new { outDirectory, receipt = result.Receipt }, result.Issues);
        else if (result.Verdict == ReleaseExecutionVerdict.ExecutedAndVerified && result.Receipt is not null)
        {
            output.WriteLine("Release execution completed and verified.");
            output.WriteLine($"Repository: {result.Receipt.Repository}");
            output.WriteLine($"Tag: {result.Receipt.CandidateTagName}");
            output.WriteLine($"Release: {result.Receipt.GitHubReleaseUrl ?? result.Receipt.GitHubReleaseId ?? "unknown"}");
            output.WriteLine("Boundary: release execution did not deploy, publish packages, promote memory, or continue workflow.");
        }
        else
        {
            error.WriteLine($"Release execution did not complete: {string.Join(", ", result.Issues)}");
        }

        return result.Verdict == ReleaseExecutionVerdict.ExecutedAndVerified ? 0 : 1;
    }

    private static int HandleReceiptRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseReceiptRead(args);
        if (parsed.Error is not null)
            return Failure(output, error, parsed.Json, $"release-execution {mode}", parsed.Error);

        var receipt = ReadJson<ReleaseExecutionReceipt>(ResolveReceiptPath(parsed.ReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, $"release-execution {mode}", "release execution receipt is missing or invalid.");

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { receipt.PreState, receipt.PostState, receipt.MutationResults, receipt.Issues, receipt.Boundary }
                : new { receipt.ReleaseExecutionId, receipt.ReleaseReadinessDecisionPackageId, receipt.Repository, receipt.CandidateTagName, receipt.ExecutionVerdict, receipt.FailureClassification, receipt.PostStateVerified, receipt.Boundary };
            WriteJson(output, $"release-execution {mode}", "succeeded", data, []);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Pre-state verified: {receipt.PreStateVerified}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine($"Completed actions: {RenderInline(receipt.CompletedActions.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(receipt.Issues)}");
        }
        else
        {
            output.WriteLine($"Release execution: {receipt.ExecutionVerdict}");
            output.WriteLine($"Repository: {receipt.Repository}");
            output.WriteLine($"Tag: {receipt.CandidateTagName}");
            output.WriteLine($"Post-state verified: {receipt.PostStateVerified}");
            output.WriteLine("Boundary: status is read-only and does not deploy, publish packages, promote memory, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteExecutionReceiptAsync(string outDirectory, ReleaseExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-execution-receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "release-execution-receipt.md"), RenderExecutionReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderExecutionReceipt(ReleaseExecutionReceipt receipt) => $"""
        # Release Execution Receipt

        Verdict: `{receipt.ExecutionVerdict}`
        Failure classification: `{receipt.FailureClassification}`
        Package: `{receipt.ReleaseReadinessDecisionPackageId}`
        Request: `{receipt.ReleaseExecutionRequestId}`
        Repository: `{receipt.Repository}`
        Source branch: `{receipt.ReleaseSourceBranch}`
        Candidate commit: `{receipt.CandidateCommitSha}`
        Version: `{receipt.CandidateVersion}`
        Tag: `{receipt.CandidateTagName}`
        Channel: `{receipt.ReleaseChannel}`
        Completed actions: `{RenderInline(receipt.CompletedActions.Select(item => item.ToString()))}`
        Tag created: `{receipt.TagCreated.ToString().ToLowerInvariant()}`
        GitHub release created: `{receipt.GitHubReleaseCreated.ToString().ToLowerInvariant()}`
        Artifacts uploaded: `{receipt.ReleaseArtifactsUploaded.ToString().ToLowerInvariant()}`
        Post-state verified: `{receipt.PostStateVerified.ToString().ToLowerInvariant()}`
        Issues:
        {RenderBullets(receipt.Issues)}

        Release readiness decision package is not release execution.
        Release execution is not deployment.
        Release execution is not package publication.
        Release execution receipt is not deployment authority.
        Release execution receipt is not package publication authority.
        Release execution receipt is not workflow continuation authority.
        No implicit tag creation through release creation.
        No hidden deployment.
        No hidden package publication.
        No hidden memory promotion.
        No hidden workflow continuation.
        """;

    private static void RecordExecutionEvent(string outDirectory, ReleaseExecutionReceipt receipt)
    {
        var kind = receipt.ExecutionVerdict == ReleaseExecutionVerdict.ExecutedAndVerified
            ? GovernanceKernelEventKind.ReleaseExecuted
            : GovernanceKernelEventKind.ReceiptCreated;
        new FileBackedGovernanceEventStore(outDirectory).Append(
            receipt.ReleaseReadinessDecisionPackageId,
            receipt.ReleaseExecutionId,
            kind,
            "ReleaseExecutionReceipt",
            receipt.ReleaseExecutionId,
            $"Release execution returned {receipt.ExecutionVerdict}.",
            ["release-execution-receipt.json"]);
    }

    private static ParsedExecute ParseExecute(string[] args)
    {
        string? package = null;
        string? request = null;
        string? outPath = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--release-readiness-package": if (!TryRead(args, ref index, out package)) return ParsedExecute.Fail(json, "--release-readiness-package requires a value."); break;
                case "--request": if (!TryRead(args, ref index, out request)) return ParsedExecute.Fail(json, "--request requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedExecute.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedExecute.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package)) return ParsedExecute.Fail(json, "Missing required option: --release-readiness-package <release-readiness-decision-package.json>.");
        if (string.IsNullOrWhiteSpace(request)) return ParsedExecute.Fail(json, "Missing required option: --request <release-execution-request.json>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedExecute.Fail(json, "Missing required option: --out <path>.");
        return new ParsedExecute(package, request, outPath, json, null);
    }

    private static ParsedReceiptRead ParseReceiptRead(string[] args)
    {
        string? receipt = null;
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--receipt": if (!TryRead(args, ref index, out receipt)) return ParsedReceiptRead.Fail(json, "--receipt requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedReceiptRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(receipt)
            ? ParsedReceiptRead.Fail(json, "Missing required option: --receipt <release-execution-receipt.json>.")
            : new ParsedReceiptRead(receipt, json, null);
    }

    private static ReleaseExecutionRequest NormalizeRequestPaths(ReleaseExecutionRequest request, string requestPath)
    {
        var directory = Path.GetDirectoryName(requestPath) ?? Directory.GetCurrentDirectory();
        string? normalize(string? path) =>
            string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path)
                ? path
                : Path.GetFullPath(Path.Combine(directory, path));
        return request with
        {
            ReleaseNotesPath = normalize(request.ReleaseNotesPath),
            Artifacts = request.Artifacts.Select(item => item with { Path = normalize(item.Path) ?? item.Path }).ToArray()
        };
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
            ? Path.Combine(fullPath, "release-readiness-decision-package.json")
            : fullPath;
    }

    private static string ResolveReceiptPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "release-execution-receipt.json")
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

    private static string ReadReleaseNotes(ReleaseExecutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ReleaseNotesBody))
            return request.ReleaseNotesBody;
        return !string.IsNullOrWhiteSpace(request.ReleaseNotesPath) && File.Exists(request.ReleaseNotesPath)
            ? File.ReadAllText(request.ReleaseNotesPath)
            : string.Empty;
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
        error.WriteLine("  irondev release-execution execute --release-readiness-package <release-readiness-decision-package.json> --request <release-execution-request.json> --out <path> [--json]");
        error.WriteLine("  irondev release-execution status --receipt <release-execution-receipt.json> [--json]");
        error.WriteLine("  irondev release-execution records --receipt <release-execution-receipt.json> [--json]");
        error.WriteLine("  irondev release-execution inspect --receipt <release-execution-receipt.json> [--json]");
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
            boundary = ReleaseExecutionBoundary.Executor
        }, JsonOptions));
    }

    private sealed record ParsedExecute(
        string? PackagePath,
        string? RequestPath,
        string? OutPath,
        bool Json,
        string? Error)
    {
        public static ParsedExecute Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ParsedReceiptRead(string? ReceiptPath, bool Json, string? Error)
    {
        public static ParsedReceiptRead Fail(bool json, string error) => new(null, json, error);
    }

    private sealed class GitHubCliReleaseExecutionGateway : IReleaseExecutionGateway
    {
        public async Task<ReleaseExecutionObservedState> ObserveAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var observedAt = DateTimeOffset.UtcNow;
            var branch = await RunProcessAsync(
                "gh",
                ["api", $"repos/{request.Repository}/branches/{request.ReleaseSourceBranch}"],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            if (branch.ExitCode != 0)
                return FailedObservation(request, observedAt, branch.StderrOrStdout());

            try
            {
                var branchSha = ReadBranchSha(branch.Stdout);
                var tag = await RunProcessAsync(
                    "gh",
                    ["api", $"repos/{request.Repository}/git/ref/tags/{request.CandidateTagName}"],
                    Directory.GetCurrentDirectory(),
                    cancellationToken).ConfigureAwait(false);
                var release = await RunProcessAsync(
                    "gh",
                    ["api", $"repos/{request.Repository}/releases/tags/{request.CandidateTagName}"],
                    Directory.GetCurrentDirectory(),
                    cancellationToken).ConfigureAwait(false);
                var tagSha = tag.ExitCode == 0 ? ReadNestedString(tag.Stdout, "object", "sha") : null;
                var releaseInfo = release.ExitCode == 0 ? ReadReleaseInfo(release.Stdout) : (false, null, null, Array.Empty<string>());
                return new ReleaseExecutionObservedState
                {
                    Repository = request.Repository,
                    ReleaseSourceBranch = request.ReleaseSourceBranch,
                    ReleaseSourceHeadSha = branchSha,
                    CandidateCommitSha = request.CandidateCommitSha,
                    CommitPresentOnReleaseSource = string.Equals(branchSha, request.CandidateCommitSha, StringComparison.OrdinalIgnoreCase),
                    CandidateTagName = request.CandidateTagName,
                    ExistingTagFound = tag.ExitCode == 0,
                    ExistingTagSha = tagSha,
                    ExistingReleaseFound = releaseInfo.Item1,
                    ExistingReleaseId = releaseInfo.Item2,
                    ExistingReleaseUrl = releaseInfo.Item3,
                    ExistingReleaseArtifactNames = releaseInfo.Item4,
                    ObservedAtUtc = observedAt,
                    ObservationSource = "GitHubCli",
                    ObservationSucceeded = true
                };
            }
            catch (JsonException exception)
            {
                return FailedObservation(request, observedAt, exception.Message);
            }
        }

        public async Task<ReleaseExecutionMutationResult> CreateTagAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var result = await RunProcessAsync(
                "gh",
                ["api", "--method", "POST", $"repos/{request.Repository}/git/refs", "-f", $"ref=refs/tags/{request.CandidateTagName}", "-f", $"sha={request.CandidateCommitSha}"],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            return new ReleaseExecutionMutationResult
            {
                Action = ReleaseExecutionAction.CreateTag,
                Attempted = true,
                Accepted = result.ExitCode == 0,
                Provider = "GitHubCli",
                CommandOrMutationName = "GitHub REST create git ref",
                Target = request.CandidateTagName,
                ResourceId = result.ExitCode == 0 ? request.CandidateCommitSha : null,
                Message = result.Stdout,
                Error = result.ExitCode == 0 ? null : result.StderrOrStdout(),
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        public async Task<ReleaseExecutionMutationResult> CreateGitHubReleaseAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var result = await RunProcessAsync(
                "gh",
                ["api", "--method", "POST", $"repos/{request.Repository}/releases", "-f", $"tag_name={request.CandidateTagName}", "-f", $"target_commitish={request.CandidateCommitSha}", "-f", $"name={request.ReleaseName ?? request.CandidateTagName}", "-f", $"body={ReadReleaseNotes(request)}"],
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
            var info = result.ExitCode == 0 ? ReadReleaseInfo(result.Stdout) : (false, null, null, Array.Empty<string>());
            return new ReleaseExecutionMutationResult
            {
                Action = ReleaseExecutionAction.CreateGitHubRelease,
                Attempted = true,
                Accepted = result.ExitCode == 0,
                Provider = "GitHubCli",
                CommandOrMutationName = "GitHub REST create release",
                Target = request.CandidateTagName,
                ResourceId = info.Item2,
                ResourceUrl = info.Item3,
                Message = result.Stdout,
                Error = result.ExitCode == 0 ? null : result.StderrOrStdout(),
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        public async Task<ReleaseExecutionMutationResult> UploadReleaseArtifactsAsync(
            ReleaseReadinessDecisionPackage package,
            ReleaseExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var uploaded = new List<string>();
            var errors = new List<string>();
            foreach (var artifact in request.Artifacts)
            {
                var result = await RunProcessAsync(
                    "gh",
                    ["release", "upload", request.CandidateTagName, artifact.Path, "--repo", request.Repository],
                    Directory.GetCurrentDirectory(),
                    cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                    uploaded.Add(artifact.Name);
                else
                    errors.Add($"{artifact.Name}:{result.StderrOrStdout()}");
            }

            return new ReleaseExecutionMutationResult
            {
                Action = ReleaseExecutionAction.UploadReleaseArtifacts,
                Attempted = true,
                Accepted = errors.Count == 0,
                Provider = "GitHubCli",
                CommandOrMutationName = "GitHub CLI release asset upload",
                Target = request.CandidateTagName,
                UploadedArtifacts = uploaded.ToArray(),
                Message = uploaded.Count == 0 ? null : $"Uploaded {uploaded.Count} artifact(s).",
                Error = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private static ReleaseExecutionObservedState FailedObservation(ReleaseExecutionRequest request, DateTimeOffset observedAt, string error) => new()
        {
            Repository = request.Repository,
            ReleaseSourceBranch = request.ReleaseSourceBranch,
            ReleaseSourceHeadSha = string.Empty,
            CandidateCommitSha = request.CandidateCommitSha,
            CommitPresentOnReleaseSource = false,
            CandidateTagName = request.CandidateTagName,
            ObservedAtUtc = observedAt,
            ObservationSource = "GitHubCli",
            ObservationSucceeded = false,
            ObservationError = error
        };
    }

    private static async Task<ReleaseCommandProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
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
        return new ReleaseCommandProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string ReadBranchSha(string stdout)
    {
        using var document = JsonDocument.Parse(stdout);
        return ReadNestedString(document.RootElement, "commit", "sha") ?? string.Empty;
    }

    private static (bool, string?, string?, string[]) ReadReleaseInfo(string stdout)
    {
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.ToString() : null;
        var url = TryGetString(root, "html_url") ?? TryGetString(root, "url");
        var assets = root.TryGetProperty("assets", out var assetElement) && assetElement.ValueKind == JsonValueKind.Array
            ? assetElement.EnumerateArray()
                .Select(asset => TryGetString(asset, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToArray()
            : [];
        return (true, id, url, assets);
    }

    private static string? ReadNestedString(string stdout, string propertyName, string childPropertyName)
    {
        using var document = JsonDocument.Parse(stdout);
        return ReadNestedString(document.RootElement, propertyName, childPropertyName);
    }

    private static string? ReadNestedString(JsonElement element, string propertyName, string childPropertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? TryGetString(value, childPropertyName)
            : null;

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private sealed record ReleaseCommandProcessResult(int ExitCode, string Stdout, string Stderr)
    {
        public string StderrOrStdout() => string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;
    }
}

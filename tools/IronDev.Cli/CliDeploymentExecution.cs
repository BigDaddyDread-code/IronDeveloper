using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static class IronDevCliDeploymentExecution
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "publish",
        "publish-package",
        "promote-memory",
        "continue",
        "continue-workflow",
        "commit",
        "push",
        "merge",
        "source-apply",
        "tag",
        "release",
        "rollback",
        "rollback-execute",
        "dispatch",
        "trigger-pipeline"
    ];

    public static bool IsDeploymentExecutionCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "deployment-execution", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "deployment-execution requires a subcommand: execute, inspect, status, or records.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"deployment-execution {args[1]} is intentionally unsupported; Block BE only deploys the approved artifact to the approved target/environment.");

        return subcommand switch
        {
            "execute" => await HandleExecuteAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleReceiptRead(args, output, error, "inspect"),
            "status" => HandleReceiptRead(args, output, error, "status"),
            "records" => HandleReceiptRead(args, output, error, "records"),
            _ => Usage(error, $"unsupported deployment-execution subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleExecuteAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseExecute(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var package = ReadJson<DeploymentReadinessDecisionPackage>(ResolvePackagePath(parsed.PackagePath!));
        if (package is null)
            return Failure(output, error, parsed.Json, "deployment-execution execute", "deployment readiness decision package is missing or invalid.", DeploymentExecutionBoundary.ReadOnly);

        var request = ReadJson<DeploymentExecutionRequest>(Path.GetFullPath(parsed.RequestPath!));
        if (request is null)
            return Failure(output, error, parsed.Json, "deployment-execution execute", "deployment execution request is missing or invalid.", DeploymentExecutionBoundary.ReadOnly);

        var outDirectory = ResolveOutputDirectory(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        var result = await ControlledDeploymentExecutor.ExecuteAsync(
            package,
            request,
            new FileBackedDeploymentExecutionGateway(outDirectory),
            cancellationToken).ConfigureAwait(false);

        if (result.Receipt is not null)
        {
            await WriteExecutionReceiptAsync(outDirectory, result.Receipt, cancellationToken).ConfigureAwait(false);
            RecordExecutionEvent(outDirectory, result.Receipt);
        }

        var boundary = result.Receipt?.Boundary ?? DeploymentExecutionBoundary.Blocked;
        if (parsed.Json)
            WriteJson(output, "deployment-execution execute", result.Verdict == DeploymentExecutionVerdict.ExecutedAndVerified ? "succeeded" : "blocked", new { outDirectory, receipt = result.Receipt }, result.Issues, boundary);
        else if (result.Verdict == DeploymentExecutionVerdict.ExecutedAndVerified && result.Receipt is not null)
        {
            output.WriteLine("Deployment execution completed and verified.");
            output.WriteLine($"Target: {result.Receipt.DeploymentTarget}");
            output.WriteLine($"Environment: {result.Receipt.DeploymentEnvironment}");
            output.WriteLine($"Artifact: {result.Receipt.DeployedArtifactName}");
            output.WriteLine("Boundary: deployment execution did not publish packages, promote memory, continue workflow, mutate source, or execute rollback.");
        }
        else
        {
            error.WriteLine($"Deployment execution did not complete: {string.Join(", ", result.Issues)}");
        }

        return result.Verdict == DeploymentExecutionVerdict.ExecutedAndVerified ? 0 : 1;
    }

    private static int HandleReceiptRead(string[] args, TextWriter output, TextWriter error, string mode)
    {
        var parsed = ParseReceiptRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var receipt = ReadJson<DeploymentExecutionReceipt>(ResolveReceiptPath(parsed.ReceiptPath!));
        if (receipt is null)
            return Failure(output, error, parsed.Json, $"deployment-execution {mode}", "deployment execution receipt is missing or invalid.", DeploymentExecutionBoundary.ReadOnly);

        if (parsed.Json)
        {
            object data = mode == "records"
                ? new { receipt.PreDeploymentState, receipt.PostDeploymentState, receipt.MutationResults, receipt.Issues, boundary = DeploymentExecutionBoundary.ReadOnly }
                : new { receipt.DeploymentExecutionReceiptId, receipt.DeploymentReadinessDecisionPackageId, receipt.Repository, receipt.DeploymentTarget, receipt.DeploymentEnvironment, receipt.ExecutionVerdict, receipt.FailureClassification, receipt.PostDeploymentStateVerified, boundary = DeploymentExecutionBoundary.ReadOnly };
            WriteJson(output, $"deployment-execution {mode}", "succeeded", data, [], DeploymentExecutionBoundary.ReadOnly);
        }
        else if (mode == "records")
        {
            output.WriteLine($"Pre-state verified: {receipt.PreDeploymentStateVerified}");
            output.WriteLine($"Post-state verified: {receipt.PostDeploymentStateVerified}");
            output.WriteLine($"Completed actions: {RenderInline(receipt.CompletedActions.Select(item => item.ToString()))}");
            output.WriteLine($"Issues: {RenderInline(receipt.Issues)}");
        }
        else
        {
            output.WriteLine($"Deployment execution: {receipt.ExecutionVerdict}");
            output.WriteLine($"Target: {receipt.DeploymentTarget}");
            output.WriteLine($"Environment: {receipt.DeploymentEnvironment}");
            output.WriteLine($"Post-state verified: {receipt.PostDeploymentStateVerified}");
            output.WriteLine("Boundary: status is read-only and does not deploy, publish packages, promote memory, continue workflow, mutate source, or execute rollback.");
        }

        return 0;
    }

    private static async Task WriteExecutionReceiptAsync(
        string outDirectory,
        DeploymentExecutionReceipt receipt,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-execution-receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "deployment-execution-receipt.md"), RenderExecutionReceipt(receipt), cancellationToken).ConfigureAwait(false);
    }

    private static string RenderExecutionReceipt(DeploymentExecutionReceipt receipt) => $"""
        # Deployment Execution Receipt

        Verdict: `{receipt.ExecutionVerdict}`
        Failure classification: `{receipt.FailureClassification}`
        Package: `{receipt.DeploymentReadinessDecisionPackageId}`
        Request: `{receipt.DeploymentExecutionRequestId}`
        Repository: `{receipt.Repository}`
        Candidate commit: `{receipt.CandidateCommitSha}`
        Version: `{receipt.CandidateVersion}`
        Tag: `{receipt.CandidateTagName}`
        Channel: `{receipt.ReleaseChannel}`
        Target: `{receipt.DeploymentTarget}`
        Environment: `{receipt.DeploymentEnvironment}`
        Artifact: `{receipt.DeployedArtifactName}`
        Completed actions: `{RenderInline(receipt.CompletedActions.Select(item => item.ToString()))}`
        Deployment attempted: `{receipt.DeploymentAttempted.ToString().ToLowerInvariant()}`
        Deployment accepted: `{receipt.DeploymentAccepted.ToString().ToLowerInvariant()}`
        Post-state verified: `{receipt.PostDeploymentStateVerified.ToString().ToLowerInvariant()}`
        Issues:
        {RenderBullets(receipt.Issues)}

        BE consumes BD deployment-readiness decision package.
        BC package is not deployment execution authority.
        Release execution receipt is not deployment execution authority.
        Deployment readiness decision package is not deployment execution by itself.
        Deployment execution is not package publication.
        Deployment execution is not workflow continuation.
        Deployment execution is not memory promotion.
        Deployment execution is not rollback execution.
        BE deploys only the approved artifact to the approved target/environment.
        BE does not publish packages.
        BE does not mutate source.
        BE does not commit.
        BE does not push.
        BE does not merge.
        BE does not promote memory.
        BE does not continue workflow.
        BE does not execute rollback.
        Partial deployment is non-success.
        Post-deployment verification failure is non-success.
        """;

    private static void RecordExecutionEvent(string outDirectory, DeploymentExecutionReceipt receipt)
    {
        var kind = receipt.ExecutionVerdict == DeploymentExecutionVerdict.ExecutedAndVerified
            ? GovernanceKernelEventKind.DeploymentExecuted
            : GovernanceKernelEventKind.ReceiptCreated;
        new FileBackedGovernanceEventStore(outDirectory).Append(
            receipt.DeploymentReadinessDecisionPackageId,
            receipt.DeploymentExecutionReceiptId,
            kind,
            "DeploymentExecutionReceipt",
            receipt.DeploymentExecutionReceiptId,
            $"Deployment execution returned {receipt.ExecutionVerdict}.",
            ["deployment-execution-receipt.json"]);
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
                case "--deployment-readiness-decision-package": if (!TryRead(args, ref index, out package)) return ParsedExecute.Fail(json, "--deployment-readiness-decision-package requires a value."); break;
                case "--request": if (!TryRead(args, ref index, out request)) return ParsedExecute.Fail(json, "--request requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedExecute.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedExecute.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(package)) return ParsedExecute.Fail(json, "Missing required option: --deployment-readiness-decision-package <deployment-readiness-decision-package.json>.");
        if (string.IsNullOrWhiteSpace(request)) return ParsedExecute.Fail(json, "Missing required option: --request <deployment-execution-request.json>.");
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
            ? ParsedReceiptRead.Fail(json, "Missing required option: --receipt <deployment-execution-receipt.json>.")
            : new ParsedReceiptRead(receipt, json, null);
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
            ? Path.Combine(fullPath, "deployment-readiness-decision-package.json")
            : fullPath;
    }

    private static string ResolveReceiptPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath) || !Path.HasExtension(fullPath)
            ? Path.Combine(fullPath, "deployment-execution-receipt.json")
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
        error.WriteLine("  irondev deployment-execution execute --deployment-readiness-decision-package <deployment-readiness-decision-package.json> --request <deployment-execution-request.json> --out <path> [--json]");
        error.WriteLine("  irondev deployment-execution inspect --receipt <deployment-execution-receipt.json> [--json]");
        error.WriteLine("  irondev deployment-execution status --receipt <deployment-execution-receipt.json> [--json]");
        error.WriteLine("  irondev deployment-execution records --receipt <deployment-execution-receipt.json> [--json]");
        return 2;
    }

    private static int Failure(
        TextWriter output,
        TextWriter error,
        bool json,
        string command,
        string message,
        DeploymentExecutionBoundary boundary)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message], boundary);
        else
            error.WriteLine(message);
        return 1;
    }

    private static void WriteJson(
        TextWriter output,
        string command,
        string status,
        object? data,
        string[] errors,
        DeploymentExecutionBoundary boundary)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = errors.Length == 0 && status != "failed" && status != "blocked",
            command,
            status,
            data,
            errors,
            boundary
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

    public sealed class FileBackedDeploymentExecutionGateway : IDeploymentExecutionGateway
    {
        private readonly string _statePath;

        public FileBackedDeploymentExecutionGateway(string outDirectory)
        {
            _statePath = Path.Combine(outDirectory, "deployment-target-state.json");
        }

        public Task<DeploymentTargetObservedState> ObserveAsync(
            DeploymentReadinessDecisionPackage package,
            DeploymentExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var observedAt = DateTimeOffset.UtcNow;
            if (!File.Exists(_statePath))
                return Task.FromResult(EmptyObservedState(request, observedAt));

            try
            {
                var state = JsonSerializer.Deserialize<DeploymentTargetObservedState>(File.ReadAllText(_statePath), JsonOptions);
                if (state is null)
                    return Task.FromResult(FailedObservation(request, observedAt, "deployment target state file was empty."));

                return Task.FromResult(state with
                {
                    ObservedAtUtc = observedAt,
                    ObservationSource = "FileBackedDeploymentExecutionGateway",
                    ObservationSucceeded = true,
                    ObservationError = null
                });
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                return Task.FromResult(FailedObservation(request, observedAt, exception.Message));
            }
        }

        public Task<DeploymentExecutionMutationResult> DeployApprovedArtifactAsync(
            DeploymentReadinessDecisionPackage package,
            DeploymentExecutionRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var completedAt = DateTimeOffset.UtcNow;
                var state = new DeploymentTargetObservedState
                {
                    DeploymentTarget = request.DeploymentTarget,
                    DeploymentEnvironment = request.DeploymentEnvironment,
                    CurrentlyDeployedVersion = request.CandidateVersion,
                    CurrentlyDeployedCommitSha = request.CandidateCommitSha,
                    CurrentlyDeployedArtifactSha256 = request.DeploymentArtifactSha256,
                    DeploymentInProgress = false,
                    DeploymentTargetLocked = false,
                    ObservedAtUtc = completedAt,
                    ObservationSource = "FileBackedDeploymentExecutionGateway",
                    ObservationSucceeded = true
                };
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath) ?? Directory.GetCurrentDirectory());
                File.WriteAllText(_statePath, JsonSerializer.Serialize(state, JsonOptions));
                return Task.FromResult(new DeploymentExecutionMutationResult
                {
                    Action = DeploymentExecutionAction.DeployApprovedArtifact,
                    Attempted = true,
                    Accepted = true,
                    Provider = "FileBackedDeploymentExecutionGateway",
                    MutationName = "WriteDeploymentTargetState",
                    DeploymentTarget = request.DeploymentTarget,
                    DeploymentEnvironment = request.DeploymentEnvironment,
                    DeploymentId = $"deployment_{Guid.NewGuid():N}",
                    Message = "Approved artifact recorded as deployed in the file-backed deployment target.",
                    CompletedAtUtc = completedAt
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(new DeploymentExecutionMutationResult
                {
                    Action = DeploymentExecutionAction.DeployApprovedArtifact,
                    Attempted = true,
                    Accepted = false,
                    Provider = "FileBackedDeploymentExecutionGateway",
                    MutationName = "WriteDeploymentTargetState",
                    DeploymentTarget = request.DeploymentTarget,
                    DeploymentEnvironment = request.DeploymentEnvironment,
                    Error = exception.Message,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        private static DeploymentTargetObservedState EmptyObservedState(
            DeploymentExecutionRequest request,
            DateTimeOffset observedAt) => new()
            {
                DeploymentTarget = request.DeploymentTarget,
                DeploymentEnvironment = request.DeploymentEnvironment,
                DeploymentInProgress = false,
                DeploymentTargetLocked = false,
                ObservedAtUtc = observedAt,
                ObservationSource = "FileBackedDeploymentExecutionGateway",
                ObservationSucceeded = true
            };

        private static DeploymentTargetObservedState FailedObservation(
            DeploymentExecutionRequest request,
            DateTimeOffset observedAt,
            string error) => new()
            {
                DeploymentTarget = request.DeploymentTarget,
                DeploymentEnvironment = request.DeploymentEnvironment,
                DeploymentInProgress = false,
                DeploymentTargetLocked = false,
                ObservedAtUtc = observedAt,
                ObservationSource = "FileBackedDeploymentExecutionGateway",
                ObservationSucceeded = false,
                ObservationError = error
            };
    }
}

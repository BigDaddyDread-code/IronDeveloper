using System.Security.Cryptography;
using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceApplyVerifyService : IDisposableWorkspaceApplyVerifyService
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceApplyVerifyResult> VerifyAsync(
        DisposableWorkspaceApplyVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var applyCopyPath = Path.Combine(runDirectory, "apply-copy.json");
        var applyDryRunPath = Path.Combine(runDirectory, "apply-dry-run.json");
        var applyPreflightPath = Path.Combine(runDirectory, "apply-preflight.json");
        var promotionApprovalPath = Path.Combine(runDirectory, "promotion-approval.json");
        var promotionPackagePath = Path.Combine(runDirectory, "promotion-package.json");
        var diffMetadataPath = Path.Combine(runDirectory, "diff.json");
        var applyVerifyPath = Path.Combine(runDirectory, "apply-verify.json");
        var blockers = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;

        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        RequireFile(workspaceMetadataPath, "workspace metadata", blockers);
        RequireFile(applyCopyPath, "apply copy evidence", blockers);
        RequireFile(applyDryRunPath, "apply dry run evidence", blockers);
        RequireFile(applyPreflightPath, "apply preflight evidence", blockers);
        RequireFile(promotionApprovalPath, "promotion approval evidence", blockers);
        RequireFile(promotionPackagePath, "promotion package", blockers);
        RequireFile(diffMetadataPath, "diff evidence", blockers);

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, verified: false, sourceMatchesWorkspace: false, operations: [], workspaceMetadataPath, applyCopyPath: null, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath: null, blockers, warnings);

        VerifyFacts facts;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            using var applyCopy = await ReadJsonAsync(applyCopyPath, cancellationToken).ConfigureAwait(false);
            using var applyDryRun = await ReadJsonAsync(applyDryRunPath, cancellationToken).ConfigureAwait(false);
            using var applyPreflight = await ReadJsonAsync(applyPreflightPath, cancellationToken).ConfigureAwait(false);
            using var promotionApproval = await ReadJsonAsync(promotionApprovalPath, cancellationToken).ConfigureAwait(false);
            using var promotionPackage = await ReadJsonAsync(promotionPackagePath, cancellationToken).ConfigureAwait(false);
            using var diffEvidence = await ReadJsonAsync(diffMetadataPath, cancellationToken).ConfigureAwait(false);

            facts = ReadFacts(
                request,
                workspacePath,
                workspaceMetadata.RootElement,
                applyCopy.RootElement,
                applyDryRun.RootElement,
                applyPreflight.RootElement,
                promotionApproval.RootElement,
                promotionPackage.RootElement,
                diffEvidence.RootElement,
                blockers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply verification could not read required evidence: {exception.Message}" };
            return Failed(request, workspacePath, string.Empty, verified: false, sourceMatchesWorkspace: false, operations: [], workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath: null, errors, warnings);
        }

        if (!string.IsNullOrWhiteSpace(facts.SourceRepo) && !Directory.Exists(facts.SourceRepo))
            blockers.Add("Source repository does not exist.");

        if (!string.IsNullOrWhiteSpace(facts.SourceRepo) &&
            (PathsEqual(workspacePath, facts.SourceRepo) ||
             IsSameOrInside(facts.SourceRepo, workspacePath) ||
             IsSameOrInside(workspacePath, facts.SourceRepo)))
        {
            blockers.Add("Workspace path and source repository must be isolated from each other.");
        }

        if (!facts.Applied)
            blockers.Add("Apply copy evidence must report applied true.");
        if (!facts.SourceRepoMutated)
            blockers.Add("Apply copy evidence must report sourceRepoMutated true.");
        if (facts.DeleteCount != 0)
            blockers.Add("Apply verification does not support delete operations.");
        if (facts.Operations.Count == 0)
            blockers.Add("Apply copy evidence contains no operations.");
        if (facts.Operations.Any(operation => string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase)))
            blockers.Add("Delete operations cannot be verified by workspace apply-verify.");
        if (facts.Operations.Any(operation =>
                !string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("Apply copy evidence contains an unsupported operation.");
        }

        IReadOnlyList<DisposableWorkspaceApplyVerifyOperation> operations = [];
        if (blockers.Count == 0)
        {
            try
            {
                operations = await BuildVerificationOperationsAsync(facts, workspacePath, blockers, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var errors = new List<string> { $"Workspace apply verification could not inspect operation files: {exception.Message}" };
                return Failed(request, workspacePath, facts.SourceRepo, verified: false, sourceMatchesWorkspace: false, operations: [], workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath: null, errors, warnings);
            }
        }

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, facts.SourceRepo, verified: false, sourceMatchesWorkspace: false, operations, workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath: null, blockers, warnings);

        var verified = operations.All(operation => operation.Verified);
        var sourceMatchesWorkspace = operations.All(operation =>
            !string.IsNullOrWhiteSpace(operation.ActualSourceSha256After) &&
            string.Equals(operation.ActualSourceSha256After, operation.ActualWorkspaceSha256, StringComparison.OrdinalIgnoreCase));

        if (!verified || !sourceMatchesWorkspace)
        {
            blockers.Add("One or more apply-copy operations did not verify.");
            return Blocked(request, workspacePath, facts.SourceRepo, verified: false, sourceMatchesWorkspace, operations, workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath: null, blockers, warnings);
        }

        var evidencePaths = new[]
        {
            workspaceMetadataPath,
            applyCopyPath,
            applyDryRunPath,
            applyPreflightPath,
            promotionApprovalPath,
            promotionPackagePath,
            diffMetadataPath,
            applyVerifyPath
        };
        var data = BuildData(request, workspacePath, facts.SourceRepo, verified: true, sourceMatchesWorkspace: true, operations, workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath, evidencePaths, blockers, warnings, errors: []);

        try
        {
            Directory.CreateDirectory(runDirectory);
            await WriteEvidenceAsync(data, createdUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply verification could not write evidence: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, verified: false, sourceMatchesWorkspace: true, operations, workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath: null, errors, warnings);
        }

        return new DisposableWorkspaceApplyVerifyResult
        {
            Status = "succeeded",
            Summary = "Workspace apply verification completed.",
            ExitCode = 0,
            Data = data,
            Errors = [],
            Warnings = warnings
        };
    }

    private static void RequireFile(string path, string label, List<string> blockers)
    {
        if (!File.Exists(path))
            blockers.Add($"Required {label} was not found.");
    }

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static VerifyFacts ReadFacts(
        DisposableWorkspaceApplyVerifyRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        JsonElement applyCopy,
        JsonElement applyDryRun,
        JsonElement applyPreflight,
        JsonElement promotionApproval,
        JsonElement promotionPackage,
        JsonElement diffEvidence,
        List<string> blockers)
    {
        RequireMatch("workspace metadata runId", request.RunId, GetString(workspaceMetadata, "runId"), blockers);
        RequireMatch("apply copy runId", request.RunId, GetString(applyCopy, "runId"), blockers);
        RequireMatch("apply dry run runId", request.RunId, GetString(applyDryRun, "runId"), blockers);
        RequireMatch("apply preflight runId", request.RunId, GetString(applyPreflight, "runId"), blockers);
        RequireMatch("promotion approval runId", request.RunId, GetString(promotionApproval, "runId"), blockers);
        RequireMatch("promotion package runId", request.RunId, GetString(promotionPackage, "runId"), blockers);
        RequireMatch("diff runId", request.RunId, GetString(diffEvidence, "runId"), blockers);

        RequirePathMatch("workspace metadata workspacePath", workspacePath, NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath")), blockers);
        RequirePathMatch("apply copy workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyCopy, "workspacePath")), blockers);
        RequirePathMatch("apply dry run workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyDryRun, "workspacePath")), blockers);
        RequirePathMatch("apply preflight workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyPreflight, "workspacePath")), blockers);
        RequirePathMatch("promotion approval workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionApproval, "workspacePath")), blockers);
        RequirePathMatch("promotion package workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionPackage, "workspacePath")), blockers);
        RequirePathMatch("diff workspacePath", workspacePath, NormalizeOptionalPath(GetString(diffEvidence, "workspacePath")), blockers);

        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));
        RequireSourceMatch("apply copy sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyCopy, "sourceRepo")), blockers);
        RequireSourceMatch("apply dry run sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyDryRun, "sourceRepo")), blockers);
        RequireSourceMatch("apply preflight sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyPreflight, "sourceRepo")), blockers);
        RequireSourceMatch("promotion package sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(promotionPackage, "sourceRepo")), blockers);
        RequireSourceMatch("diff sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(diffEvidence, "sourceRepo")), blockers);
        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");

        return new VerifyFacts(
            SourceRepo: sourceRepo,
            Applied: GetBool(applyCopy, "applied") ?? false,
            SourceRepoMutated: GetBool(applyCopy, "sourceRepoMutated") ?? false,
            DeleteCount: GetInt(applyCopy, "deleteCount") ?? -1,
            Operations: GetOperations(applyCopy));
    }

    private static IReadOnlyList<CopyOperation> GetOperations(JsonElement applyCopy)
    {
        if (applyCopy.ValueKind != JsonValueKind.Object ||
            !applyCopy.TryGetProperty("operations", out var operations) ||
            operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return operations.EnumerateArray()
            .Where(operation => operation.ValueKind == JsonValueKind.Object)
            .Select(operation => new CopyOperation(
                Operation: GetString(operation, "operation") ?? string.Empty,
                RelativePath: GetString(operation, "relativePath") ?? string.Empty,
                SourcePath: NormalizeOptionalPath(GetString(operation, "sourcePath")),
                WorkspacePath: NormalizeOptionalPath(GetString(operation, "workspacePath")),
                ActualSourceSha256After: GetString(operation, "actualSourceSha256After") ?? string.Empty,
                Applied: GetBool(operation, "applied") ?? false))
            .ToArray();
    }

    private static async Task<IReadOnlyList<DisposableWorkspaceApplyVerifyOperation>> BuildVerificationOperationsAsync(
        VerifyFacts facts,
        string workspaceRoot,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var operations = new List<DisposableWorkspaceApplyVerifyOperation>();
        foreach (var operation in facts.Operations)
            operations.Add(await BuildVerificationOperationAsync(operation, facts.SourceRepo, workspaceRoot, blockers, cancellationToken).ConfigureAwait(false));
        return operations;
    }

    private static async Task<DisposableWorkspaceApplyVerifyOperation> BuildVerificationOperationAsync(
        CopyOperation operation,
        string sourceRepo,
        string workspaceRoot,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var operationBlockers = new List<string>();
        var safeRelativePath = NormalizeRelativePath(operation.RelativePath, operationBlockers);
        var sourcePath = Path.GetFullPath(Path.Combine(sourceRepo, safeRelativePath));
        var workspacePath = Path.GetFullPath(Path.Combine(workspaceRoot, safeRelativePath));

        if (!IsSameOrInside(sourceRepo, sourcePath) || !IsSameOrInside(workspaceRoot, workspacePath))
            operationBlockers.Add($"Apply verification path escapes expected roots: {operation.RelativePath}");
        if (!string.IsNullOrWhiteSpace(operation.SourcePath) && !PathsEqual(sourcePath, operation.SourcePath))
            operationBlockers.Add($"Apply verification source path mismatch for {operation.RelativePath}.");
        if (!string.IsNullOrWhiteSpace(operation.WorkspacePath) && !PathsEqual(workspacePath, operation.WorkspacePath))
            operationBlockers.Add($"Apply verification workspace path mismatch for {operation.RelativePath}.");
        if (!string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase))
        {
            operationBlockers.Add($"Apply verification only supports add/modify operations: {operation.RelativePath}");
        }
        if (!operation.Applied)
            operationBlockers.Add($"Apply copy operation was not applied: {operation.RelativePath}");

        var sourceExists = File.Exists(sourcePath);
        var workspaceExists = File.Exists(workspacePath);
        if (!sourceExists)
            operationBlockers.Add($"Source file does not exist after apply-copy: {operation.RelativePath}");
        if (!workspaceExists)
            operationBlockers.Add($"Workspace file does not exist for apply verification: {operation.RelativePath}");
        if (Directory.Exists(sourcePath))
            operationBlockers.Add($"Source path is a directory, not a file: {operation.RelativePath}");
        if (Directory.Exists(workspacePath))
            operationBlockers.Add($"Workspace path is a directory, not a file: {operation.RelativePath}");

        string? actualSourceSha = null;
        string? actualWorkspaceSha = null;
        if (sourceExists)
            actualSourceSha = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
        if (workspaceExists)
            actualWorkspaceSha = await ComputeSha256Async(workspacePath, cancellationToken).ConfigureAwait(false);

        if (sourceExists && workspaceExists && !string.Equals(actualSourceSha, actualWorkspaceSha, StringComparison.OrdinalIgnoreCase))
            operationBlockers.Add($"Source hash does not match workspace hash after apply-copy: {operation.RelativePath}");
        if (sourceExists && !string.Equals(actualSourceSha, operation.ActualSourceSha256After, StringComparison.OrdinalIgnoreCase))
            operationBlockers.Add($"Source hash does not match apply-copy evidence after apply-copy: {operation.RelativePath}");

        blockers.AddRange(operationBlockers);
        return new DisposableWorkspaceApplyVerifyOperation
        {
            Operation = operation.Operation,
            RelativePath = string.IsNullOrWhiteSpace(safeRelativePath) ? operation.RelativePath : safeRelativePath,
            SourcePath = sourcePath,
            WorkspacePath = workspacePath,
            SourceExists = sourceExists,
            WorkspaceExists = workspaceExists,
            ExpectedSourceSha256After = operation.ActualSourceSha256After,
            ActualSourceSha256After = actualSourceSha,
            ActualWorkspaceSha256 = actualWorkspaceSha,
            Verified = operationBlockers.Count == 0
        };
    }

    private static string NormalizeRelativePath(string relativePath, List<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            blockers.Add("Apply verification path is empty.");
            return string.Empty;
        }

        if (Path.IsPathRooted(relativePath))
            blockers.Add($"Apply verification path must be relative: {relativePath}");

        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            blockers.Add($"Apply verification path must not contain parent traversal: {relativePath}");
        if (segments.Any(segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, ".irondev", StringComparison.OrdinalIgnoreCase)))
            blockers.Add($"Apply verification path must not target reserved workspace metadata: {relativePath}");

        return segments.Length == 0 ? string.Empty : Path.Combine(segments);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteEvidenceAsync(
        DisposableWorkspaceApplyVerifyData data,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            data.ApplyVerifyPath!,
            JsonSerializer.Serialize(
                new
                {
                    runId = data.RunId,
                    workspacePath = data.WorkspacePath,
                    sourceRepo = data.SourceRepo,
                    createdUtc,
                    verified = data.Verified,
                    sourceMatchesWorkspace = data.SourceMatchesWorkspace,
                    operations = data.Operations,
                    verifiedCount = data.VerifiedCount,
                    failedCount = data.FailedCount,
                    evidencePaths = data.EvidencePaths,
                    blockers = data.Blockers,
                    warnings = data.Warnings,
                    errors = data.Errors
                },
                EvidenceJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static DisposableWorkspaceApplyVerifyResult Blocked(
        DisposableWorkspaceApplyVerifyRequest request,
        string workspacePath,
        string sourceRepo,
        bool verified,
        bool sourceMatchesWorkspace,
        IReadOnlyList<DisposableWorkspaceApplyVerifyOperation> operations,
        string? workspaceMetadataPath,
        string? applyCopyPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? applyVerifyPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace apply verification was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, verified, sourceMatchesWorkspace, operations, workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath, ExistingEvidencePaths(workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath), blockers, warnings, errors: blockers),
            Errors = blockers,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyVerifyResult Failed(
        DisposableWorkspaceApplyVerifyRequest request,
        string workspacePath,
        string sourceRepo,
        bool verified,
        bool sourceMatchesWorkspace,
        IReadOnlyList<DisposableWorkspaceApplyVerifyOperation> operations,
        string? workspaceMetadataPath,
        string? applyCopyPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? applyVerifyPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace apply verification failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, verified, sourceMatchesWorkspace, operations, workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath, ExistingEvidencePaths(workspaceMetadataPath, applyCopyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyVerifyPath), blockers: [], warnings, errors),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyVerifyData BuildData(
        DisposableWorkspaceApplyVerifyRequest request,
        string workspacePath,
        string sourceRepo,
        bool verified,
        bool sourceMatchesWorkspace,
        IReadOnlyList<DisposableWorkspaceApplyVerifyOperation> operations,
        string? workspaceMetadataPath,
        string? applyCopyPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? applyVerifyPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            Verified = verified,
            SourceMatchesWorkspace = sourceMatchesWorkspace,
            Operations = operations,
            VerifiedCount = operations.Count(operation => operation.Verified),
            FailedCount = operations.Count(operation => !operation.Verified),
            WorkspaceMetadataPath = workspaceMetadataPath,
            ApplyCopyPath = applyCopyPath,
            ApplyDryRunPath = applyDryRunPath,
            ApplyPreflightPath = applyPreflightPath,
            PromotionApprovalPath = promotionApprovalPath,
            PromotionPackagePath = promotionPackagePath,
            DiffMetadataPath = diffMetadataPath,
            ApplyVerifyPath = applyVerifyPath,
            EvidencePaths = evidencePaths,
            Blockers = blockers,
            Warnings = warnings,
            Errors = errors
        };

    private static IReadOnlyList<string> ExistingEvidencePaths(params string?[] paths) =>
        paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Select(path => path!).ToArray();

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static void RequireMatch(string label, string expected, string? actual, List<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(actual))
            blockers.Add($"{label} is missing.");
        else if (!string.Equals(expected, actual, StringComparison.Ordinal))
            blockers.Add($"{label} mismatch.");
    }

    private static void RequirePathMatch(string label, string expected, string? actual, List<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(actual))
            blockers.Add($"{label} is missing.");
        else if (!PathsEqual(expected, actual))
            blockers.Add($"{label} mismatch.");
    }

    private static void RequireSourceMatch(string label, string expected, string? actual, List<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(actual))
            blockers.Add($"{label} is missing.");
        else if (!PathsEqual(expected, actual))
            blockers.Add($"{label} mismatch.");
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static string NormalizeOptionalPath(string? path) => string.IsNullOrWhiteSpace(path) ? string.Empty : NormalizePath(path);

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;
        return string.Equals(NormalizePath(first), NormalizePath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsSameOrInside(string parent, string candidate)
    {
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(candidate))
            return false;

        var normalizedParent = NormalizePath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = NormalizePath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedCandidate.StartsWith(normalizedParent, comparison);
    }

    private sealed record VerifyFacts(
        string SourceRepo,
        bool Applied,
        bool SourceRepoMutated,
        int DeleteCount,
        IReadOnlyList<CopyOperation> Operations);

    private sealed record CopyOperation(
        string Operation,
        string RelativePath,
        string SourcePath,
        string WorkspacePath,
        string ActualSourceSha256After,
        bool Applied);
}

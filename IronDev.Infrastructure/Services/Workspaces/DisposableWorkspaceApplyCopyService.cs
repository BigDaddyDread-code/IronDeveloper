using System.Security.Cryptography;
using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceApplyCopyService : IDisposableWorkspaceApplyCopyService
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceApplyCopyResult> ApplyAsync(
        DisposableWorkspaceApplyCopyRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var applyDryRunPath = Path.Combine(runDirectory, "apply-dry-run.json");
        var applyPreflightPath = Path.Combine(runDirectory, "apply-preflight.json");
        var promotionApprovalPath = Path.Combine(runDirectory, "promotion-approval.json");
        var promotionPackagePath = Path.Combine(runDirectory, "promotion-package.json");
        var diffMetadataPath = Path.Combine(runDirectory, "diff.json");
        var applyCopyPath = Path.Combine(runDirectory, "apply-copy.json");
        var blockers = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;

        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        RequireFile(workspaceMetadataPath, "workspace metadata", blockers);
        RequireFile(applyDryRunPath, "apply dry run evidence", blockers);
        RequireFile(applyPreflightPath, "apply preflight evidence", blockers);
        RequireFile(promotionApprovalPath, "promotion approval evidence", blockers);
        RequireFile(promotionPackagePath, "promotion package", blockers);
        RequireFile(diffMetadataPath, "diff evidence", blockers);

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, applied: false, sourceRepoMutated: false, operations: [], workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath: null, blockers, warnings);

        ApplyFacts facts;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            using var applyDryRun = await ReadJsonAsync(applyDryRunPath, cancellationToken).ConfigureAwait(false);
            using var applyPreflight = await ReadJsonAsync(applyPreflightPath, cancellationToken).ConfigureAwait(false);
            using var promotionApproval = await ReadJsonAsync(promotionApprovalPath, cancellationToken).ConfigureAwait(false);
            using var promotionPackage = await ReadJsonAsync(promotionPackagePath, cancellationToken).ConfigureAwait(false);
            using var diffEvidence = await ReadJsonAsync(diffMetadataPath, cancellationToken).ConfigureAwait(false);

            facts = ReadFacts(
                request,
                workspacePath,
                workspaceMetadata.RootElement,
                applyDryRun.RootElement,
                applyPreflight.RootElement,
                promotionApproval.RootElement,
                promotionPackage.RootElement,
                diffEvidence.RootElement,
                blockers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply copy could not read required evidence: {exception.Message}" };
            return Failed(request, workspacePath, string.Empty, applied: false, sourceRepoMutated: false, operations: [], workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath: null, errors, warnings);
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

        if (!facts.ReadyForApply)
            blockers.Add("Apply dry run readyForApply must be true.");
        if (facts.CanApplyNow)
            blockers.Add("Apply dry run canApplyNow must be false.");
        if (!facts.RequiresSeparateApplyCommand)
            blockers.Add("Apply dry run must require a separate apply command.");
        if (!string.Equals(facts.Recommendation, "ready_for_separate_apply_command", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Apply dry run recommendation must be ready_for_separate_apply_command.");
        if (facts.Operations.Count == 0)
            blockers.Add("Apply dry run contains no operations.");
        if (facts.Operations.Any(operation => string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase)))
            blockers.Add("Delete operations are not supported by workspace apply-copy.");
        if (facts.Operations.Any(operation =>
                !string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase)))
            blockers.Add("Apply dry run contains an unsupported operation.");
        ValidateNoDuplicateOperations(facts.Operations, blockers);

        IReadOnlyList<DisposableWorkspaceAppliedCopyOperation> plannedOperations = [];
        if (blockers.Count == 0)
        {
            try
            {
                plannedOperations = await BuildPlannedOperationsAsync(facts, workspacePath, blockers, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var errors = new List<string> { $"Workspace apply copy could not inspect operation files: {exception.Message}" };
                return Failed(request, workspacePath, facts.SourceRepo, applied: false, sourceRepoMutated: false, operations: [], workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath: null, errors, warnings);
            }
        }

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, facts.SourceRepo, applied: false, sourceRepoMutated: false, plannedOperations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath: null, blockers, warnings);

        var appliedOperations = new List<DisposableWorkspaceAppliedCopyOperation>();
        var sourceRepoMutated = false;
        try
        {
            foreach (var operation in plannedOperations)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(operation.SourcePath)!);
                File.Copy(operation.WorkspacePath, operation.SourcePath, overwrite: operation.Operation == "modify");
                sourceRepoMutated = true;
                var sourceShaAfter = await ComputeSha256Async(operation.SourcePath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(sourceShaAfter, operation.ExpectedWorkspaceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Source hash after copy did not match workspace hash for {operation.RelativePath}.");

                appliedOperations.Add(operation with
                {
                    ActualSourceSha256After = sourceShaAfter,
                    Applied = true
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failedOperations = appliedOperations
                .Concat(plannedOperations.Skip(appliedOperations.Count))
                .ToArray();
            var errors = new List<string>
            {
                $"Workspace apply copy failed after mutation may have started: {exception.Message}",
                "Source repository may be partially mutated; inspect apply-copy evidence and source files before retrying."
            };
            await TryWriteEvidenceAsync(request, workspacePath, facts.SourceRepo, createdUtc, applied: false, sourceRepoMutated, failedOperations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath, blockers: [], warnings, errors, cancellationToken).ConfigureAwait(false);
            return Failed(request, workspacePath, facts.SourceRepo, applied: false, sourceRepoMutated, failedOperations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, sourceRepoMutated ? applyCopyPath : null, errors, warnings);
        }

        var evidencePaths = new[]
        {
            workspaceMetadataPath,
            applyDryRunPath,
            applyPreflightPath,
            promotionApprovalPath,
            promotionPackagePath,
            diffMetadataPath,
            applyCopyPath
        };

        var data = BuildData(request, workspacePath, facts.SourceRepo, applied: true, sourceRepoMutated, appliedOperations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath, evidencePaths, blockers, warnings, errors: []);
        try
        {
            Directory.CreateDirectory(runDirectory);
            await WriteEvidenceAsync(data, createdUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply copy could not write evidence after source mutation: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, applied: false, sourceRepoMutated, appliedOperations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath: null, errors, warnings);
        }

        return new DisposableWorkspaceApplyCopyResult
        {
            Status = "succeeded",
            Summary = "Workspace apply copy completed.",
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

    private static ApplyFacts ReadFacts(
        DisposableWorkspaceApplyCopyRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        JsonElement applyDryRun,
        JsonElement applyPreflight,
        JsonElement promotionApproval,
        JsonElement promotionPackage,
        JsonElement diffEvidence,
        List<string> blockers)
    {
        RequireMatch("workspace metadata runId", request.RunId, GetString(workspaceMetadata, "runId"), blockers);
        RequireMatch("apply dry run runId", request.RunId, GetString(applyDryRun, "runId"), blockers);
        RequireMatch("apply preflight runId", request.RunId, GetString(applyPreflight, "runId"), blockers);
        RequireMatch("promotion approval runId", request.RunId, GetString(promotionApproval, "runId"), blockers);
        RequireMatch("promotion package runId", request.RunId, GetString(promotionPackage, "runId"), blockers);
        RequireMatch("diff runId", request.RunId, GetString(diffEvidence, "runId"), blockers);

        RequirePathMatch("workspace metadata workspacePath", workspacePath, NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath")), blockers);
        RequirePathMatch("apply dry run workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyDryRun, "workspacePath")), blockers);
        RequirePathMatch("apply preflight workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyPreflight, "workspacePath")), blockers);
        RequirePathMatch("promotion approval workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionApproval, "workspacePath")), blockers);
        RequirePathMatch("promotion package workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionPackage, "workspacePath")), blockers);
        RequirePathMatch("diff workspacePath", workspacePath, NormalizeOptionalPath(GetString(diffEvidence, "workspacePath")), blockers);

        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));
        RequireSourceMatch("apply dry run sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyDryRun, "sourceRepo")), blockers);
        RequireSourceMatch("apply preflight sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyPreflight, "sourceRepo")), blockers);
        RequireSourceMatch("promotion package sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(promotionPackage, "sourceRepo")), blockers);
        RequireSourceMatch("diff sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(diffEvidence, "sourceRepo")), blockers);
        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");

        return new ApplyFacts(
            SourceRepo: sourceRepo,
            ReadyForApply: GetBool(applyDryRun, "readyForApply") ?? false,
            CanApplyNow: GetBool(applyDryRun, "canApplyNow") ?? true,
            RequiresSeparateApplyCommand: GetBool(applyDryRun, "requiresSeparateApplyCommand") ?? false,
            Recommendation: GetString(applyDryRun, "recommendation") ?? string.Empty,
            Operations: GetOperations(applyDryRun));
    }

    private static IReadOnlyList<DryRunOperation> GetOperations(JsonElement applyDryRun)
    {
        if (applyDryRun.ValueKind != JsonValueKind.Object ||
            !applyDryRun.TryGetProperty("operations", out var operations) ||
            operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return operations.EnumerateArray()
            .Where(operation => operation.ValueKind == JsonValueKind.Object)
            .Select(operation => new DryRunOperation(
                Operation: GetString(operation, "operation") ?? string.Empty,
                RelativePath: GetString(operation, "relativePath") ?? string.Empty,
                SourcePath: NormalizeOptionalPath(GetString(operation, "sourcePath")),
                WorkspacePath: NormalizeOptionalPath(GetString(operation, "workspacePath")),
                SourceSha256: GetString(operation, "sourceSha256") ?? string.Empty,
                WorkspaceSha256: GetString(operation, "workspaceSha256") ?? string.Empty))
            .ToArray();
    }

    private static void ValidateNoDuplicateOperations(IReadOnlyList<DryRunOperation> operations, List<string> blockers)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (string.IsNullOrWhiteSpace(operation.RelativePath))
            {
                blockers.Add("Apply dry run operation path is empty.");
                continue;
            }

            if (!seen.Add(operation.RelativePath))
                blockers.Add($"Apply dry run contains duplicate operation for path: {operation.RelativePath}");
        }
    }

    private static async Task<IReadOnlyList<DisposableWorkspaceAppliedCopyOperation>> BuildPlannedOperationsAsync(
        ApplyFacts facts,
        string workspaceRoot,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var operations = new List<DisposableWorkspaceAppliedCopyOperation>();
        foreach (var operation in facts.Operations)
            operations.Add(await BuildPlannedOperationAsync(operation, facts.SourceRepo, workspaceRoot, blockers, cancellationToken).ConfigureAwait(false));
        return operations;
    }

    private static async Task<DisposableWorkspaceAppliedCopyOperation> BuildPlannedOperationAsync(
        DryRunOperation operation,
        string sourceRepo,
        string workspaceRoot,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var safeRelativePath = NormalizeRelativePath(operation.RelativePath, blockers);
        var sourcePath = Path.GetFullPath(Path.Combine(sourceRepo, safeRelativePath));
        var workspacePath = Path.GetFullPath(Path.Combine(workspaceRoot, safeRelativePath));

        if (!IsSameOrInside(sourceRepo, sourcePath) || !IsSameOrInside(workspaceRoot, workspacePath))
            blockers.Add($"Apply copy path escapes expected roots: {operation.RelativePath}");
        if (!string.IsNullOrWhiteSpace(operation.SourcePath) && !PathsEqual(sourcePath, operation.SourcePath))
            blockers.Add($"Apply copy source path mismatch for {operation.RelativePath}.");
        if (!string.IsNullOrWhiteSpace(operation.WorkspacePath) && !PathsEqual(workspacePath, operation.WorkspacePath))
            blockers.Add($"Apply copy workspace path mismatch for {operation.RelativePath}.");

        var sourceFileExists = File.Exists(sourcePath);
        var workspaceFileExists = File.Exists(workspacePath);
        var sourceDirectoryExists = Directory.Exists(sourcePath);
        var workspaceDirectoryExists = Directory.Exists(workspacePath);

        if (string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceFileExists)
                blockers.Add($"Add operation source file already exists: {operation.RelativePath}");
            if (sourceDirectoryExists)
                blockers.Add($"Add operation would conflict with an existing source directory: {operation.RelativePath}");
            if (!workspaceFileExists)
                blockers.Add($"Add operation workspace file does not exist: {operation.RelativePath}");
            if (workspaceDirectoryExists)
                blockers.Add($"Add operation workspace path is a directory, not a file: {operation.RelativePath}");
        }
        else if (string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase))
        {
            if (!sourceFileExists)
                blockers.Add($"Modify operation source file does not exist: {operation.RelativePath}");
            if (sourceDirectoryExists)
                blockers.Add($"Modify operation source path is a directory, not a file: {operation.RelativePath}");
            if (!workspaceFileExists)
                blockers.Add($"Modify operation workspace file does not exist: {operation.RelativePath}");
            if (workspaceDirectoryExists)
                blockers.Add($"Modify operation workspace path is a directory, not a file: {operation.RelativePath}");
        }

        var actualSourceSha = sourceFileExists ? await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false) : null;
        var actualWorkspaceSha = workspaceFileExists ? await ComputeSha256Async(workspacePath, cancellationToken).ConfigureAwait(false) : null;

        if (string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actualSourceSha, operation.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add($"Source hash mismatch for modify operation: {operation.RelativePath}");
        }

        if ((string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase)) &&
            !string.Equals(actualWorkspaceSha, operation.WorkspaceSha256, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add($"Workspace hash mismatch for {operation.Operation} operation: {operation.RelativePath}");
        }

        return new DisposableWorkspaceAppliedCopyOperation
        {
            Operation = operation.Operation,
            RelativePath = safeRelativePath,
            SourcePath = sourcePath,
            WorkspacePath = workspacePath,
            ExpectedSourceSha256 = operation.SourceSha256,
            ExpectedWorkspaceSha256 = operation.WorkspaceSha256,
            ActualSourceSha256Before = actualSourceSha,
            ActualWorkspaceSha256Before = actualWorkspaceSha,
            ActualSourceSha256After = null,
            Applied = false
        };
    }

    private static string NormalizeRelativePath(string relativePath, List<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            blockers.Add("Apply copy path is empty.");
            return string.Empty;
        }

        if (Path.IsPathRooted(relativePath))
            blockers.Add($"Apply copy path must be relative: {relativePath}");

        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            blockers.Add($"Apply copy path must not contain parent traversal: {relativePath}");
        if (segments.Any(segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, ".irondev", StringComparison.OrdinalIgnoreCase)))
            blockers.Add($"Apply copy path must not target reserved workspace metadata: {relativePath}");

        return Path.Combine(segments);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task TryWriteEvidenceAsync(
        DisposableWorkspaceApplyCopyRequest request,
        string workspacePath,
        string sourceRepo,
        DateTimeOffset createdUtc,
        bool applied,
        bool sourceRepoMutated,
        IReadOnlyList<DisposableWorkspaceAppliedCopyOperation> operations,
        string? workspaceMetadataPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string applyCopyPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = BuildData(request, workspacePath, sourceRepo, applied, sourceRepoMutated, operations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath, ExistingEvidencePaths(workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath), blockers, warnings, errors);
            Directory.CreateDirectory(Path.GetDirectoryName(applyCopyPath)!);
            await WriteEvidenceAsync(data, createdUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task WriteEvidenceAsync(
        DisposableWorkspaceApplyCopyData data,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            data.ApplyCopyPath!,
            JsonSerializer.Serialize(
                new
                {
                    runId = data.RunId,
                    workspacePath = data.WorkspacePath,
                    sourceRepo = data.SourceRepo,
                    createdUtc,
                    applied = data.Applied,
                    sourceRepoMutated = data.SourceRepoMutated,
                    operations = data.Operations,
                    addCount = data.AddCount,
                    modifyCount = data.ModifyCount,
                    deleteCount = data.DeleteCount,
                    evidencePaths = data.EvidencePaths,
                    blockers = data.Blockers,
                    warnings = data.Warnings,
                    errors = data.Errors
                },
                EvidenceJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static DisposableWorkspaceApplyCopyResult Blocked(
        DisposableWorkspaceApplyCopyRequest request,
        string workspacePath,
        string sourceRepo,
        bool applied,
        bool sourceRepoMutated,
        IReadOnlyList<DisposableWorkspaceAppliedCopyOperation> operations,
        string? workspaceMetadataPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? applyCopyPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace apply copy was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, applied, sourceRepoMutated, operations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath, ExistingEvidencePaths(workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath), blockers, warnings, errors: blockers),
            Errors = blockers,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyCopyResult Failed(
        DisposableWorkspaceApplyCopyRequest request,
        string workspacePath,
        string sourceRepo,
        bool applied,
        bool sourceRepoMutated,
        IReadOnlyList<DisposableWorkspaceAppliedCopyOperation> operations,
        string? workspaceMetadataPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? applyCopyPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace apply copy failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, applied, sourceRepoMutated, operations, workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath, ExistingEvidencePaths(workspaceMetadataPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, applyCopyPath), blockers: [], warnings, errors),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyCopyData BuildData(
        DisposableWorkspaceApplyCopyRequest request,
        string workspacePath,
        string sourceRepo,
        bool applied,
        bool sourceRepoMutated,
        IReadOnlyList<DisposableWorkspaceAppliedCopyOperation> operations,
        string? workspaceMetadataPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? applyCopyPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            Applied = applied,
            SourceRepoMutated = sourceRepoMutated,
            Operations = operations,
            AddCount = operations.Count(operation => string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase)),
            ModifyCount = operations.Count(operation => string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase)),
            DeleteCount = operations.Count(operation => string.Equals(operation.Operation, "delete", StringComparison.OrdinalIgnoreCase)),
            WorkspaceMetadataPath = workspaceMetadataPath,
            ApplyDryRunPath = applyDryRunPath,
            ApplyPreflightPath = applyPreflightPath,
            PromotionApprovalPath = promotionApprovalPath,
            PromotionPackagePath = promotionPackagePath,
            DiffMetadataPath = diffMetadataPath,
            ApplyCopyPath = applyCopyPath,
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

    private sealed record ApplyFacts(
        string SourceRepo,
        bool ReadyForApply,
        bool CanApplyNow,
        bool RequiresSeparateApplyCommand,
        string Recommendation,
        IReadOnlyList<DryRunOperation> Operations);

    private sealed record DryRunOperation(
        string Operation,
        string RelativePath,
        string SourcePath,
        string WorkspacePath,
        string SourceSha256,
        string WorkspaceSha256);
}

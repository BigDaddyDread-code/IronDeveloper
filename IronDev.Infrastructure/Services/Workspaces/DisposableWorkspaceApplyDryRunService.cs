using System.Security.Cryptography;
using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceApplyDryRunService : IDisposableWorkspaceApplyDryRunService
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceApplyDryRunResult> CheckAsync(
        DisposableWorkspaceApplyDryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var diffMetadataPath = Path.Combine(runDirectory, "diff.json");
        var promotionPackagePath = Path.Combine(runDirectory, "promotion-package.json");
        var approvalEvidencePath = Path.Combine(runDirectory, "promotion-approval.json");
        var applyPreflightPath = Path.Combine(runDirectory, "apply-preflight.json");
        var applyDryRunPath = Path.Combine(runDirectory, "apply-dry-run.json");
        var blockers = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;

        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        RequireFile(workspaceMetadataPath, "workspace metadata", blockers);
        RequireFile(diffMetadataPath, "diff evidence", blockers);
        RequireFile(promotionPackagePath, "promotion package", blockers);
        RequireFile(approvalEvidencePath, "promotion approval evidence", blockers);
        RequireFile(applyPreflightPath, "apply preflight evidence", blockers);

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, readyForApply: false, canApplyNow: false, requiresSeparateApplyCommand: false, recommendation: "not_ready_missing_evidence", operations: [], workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath: null, blockers, warnings);

        ArtifactFacts facts;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            using var diffEvidence = await ReadJsonAsync(diffMetadataPath, cancellationToken).ConfigureAwait(false);
            using var promotionPackage = await ReadJsonAsync(promotionPackagePath, cancellationToken).ConfigureAwait(false);
            using var approvalEvidence = await ReadJsonAsync(approvalEvidencePath, cancellationToken).ConfigureAwait(false);
            using var applyPreflight = await ReadJsonAsync(applyPreflightPath, cancellationToken).ConfigureAwait(false);

            facts = ReadFacts(
                request,
                workspacePath,
                workspaceMetadata.RootElement,
                diffEvidence.RootElement,
                promotionPackage.RootElement,
                approvalEvidence.RootElement,
                applyPreflight.RootElement,
                blockers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply dry run could not read required evidence: {exception.Message}" };
            return Failed(request, workspacePath, string.Empty, readyForApply: false, canApplyNow: false, requiresSeparateApplyCommand: false, recommendation: "not_ready_missing_evidence", operations: [], workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath: null, errors, warnings);
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
            blockers.Add("Apply preflight readyForApply must be true.");
        if (facts.CanApplyNow)
            blockers.Add("Apply preflight canApplyNow must be false for dry run.");
        if (!facts.RequiresSeparateApplyCommand)
            blockers.Add("Apply preflight must require a separate apply command.");
        if (!string.Equals(facts.Recommendation, "ready_for_separate_apply_command", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Apply preflight recommendation must be ready_for_separate_apply_command.");

        var changedCount = facts.AddedFiles.Count + facts.ModifiedFiles.Count + facts.DeletedFiles.Count;
        if (changedCount == 0)
            blockers.Add("Diff evidence has no changed files.");
        ValidateNoDuplicateOperations(facts, blockers);

        IReadOnlyList<DisposableWorkspaceApplyOperation> operations = [];
        if (blockers.Count == 0)
        {
            try
            {
                operations = await BuildOperationsAsync(facts, workspacePath, blockers, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var errors = new List<string> { $"Workspace apply dry run could not inspect operation files: {exception.Message}" };
                return Failed(request, workspacePath, facts.SourceRepo, facts.ReadyForApply, facts.CanApplyNow, facts.RequiresSeparateApplyCommand, facts.Recommendation, [], workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath: null, errors, warnings);
            }
        }

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, facts.SourceRepo, facts.ReadyForApply, facts.CanApplyNow, facts.RequiresSeparateApplyCommand, facts.Recommendation, operations, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath: null, blockers, warnings);

        var evidencePaths = new[]
        {
            workspaceMetadataPath,
            diffMetadataPath,
            promotionPackagePath,
            approvalEvidencePath,
            applyPreflightPath,
            applyDryRunPath
        };

        var data = BuildData(
            request,
            workspacePath,
            facts.SourceRepo,
            facts.ReadyForApply,
            facts.CanApplyNow,
            facts.RequiresSeparateApplyCommand,
            facts.Recommendation,
            operations,
            workspaceMetadataPath,
            diffMetadataPath,
            promotionPackagePath,
            approvalEvidencePath,
            applyPreflightPath,
            applyDryRunPath,
            evidencePaths,
            blockers,
            warnings,
            errors: []);

        try
        {
            Directory.CreateDirectory(runDirectory);
            await File.WriteAllTextAsync(
                applyDryRunPath,
                JsonSerializer.Serialize(
                    new
                    {
                        runId = data.RunId,
                        workspacePath = data.WorkspacePath,
                        sourceRepo = data.SourceRepo,
                        createdUtc,
                        readyForApply = data.ReadyForApply,
                        canApplyNow = data.CanApplyNow,
                        requiresSeparateApplyCommand = data.RequiresSeparateApplyCommand,
                        recommendation = data.Recommendation,
                        operations = data.Operations,
                        addCount = data.AddCount,
                        modifyCount = data.ModifyCount,
                        deleteCount = data.DeleteCount,
                        evidencePaths = data.EvidencePaths
                    },
                    EvidenceJsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply dry run could not write evidence: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, facts.ReadyForApply, facts.CanApplyNow, facts.RequiresSeparateApplyCommand, facts.Recommendation, operations, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath: null, errors, warnings);
        }

        return new DisposableWorkspaceApplyDryRunResult
        {
            Status = "succeeded",
            Summary = "Workspace apply dry run completed.",
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

    private static ArtifactFacts ReadFacts(
        DisposableWorkspaceApplyDryRunRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        JsonElement diffEvidence,
        JsonElement promotionPackage,
        JsonElement approvalEvidence,
        JsonElement applyPreflight,
        List<string> blockers)
    {
        RequireMatch("workspace metadata runId", request.RunId, GetString(workspaceMetadata, "runId"), blockers);
        RequireMatch("diff runId", request.RunId, GetString(diffEvidence, "runId"), blockers);
        RequireMatch("promotion package runId", request.RunId, GetString(promotionPackage, "runId"), blockers);
        RequireMatch("approval evidence runId", request.RunId, GetString(approvalEvidence, "runId"), blockers);
        RequireMatch("apply preflight runId", request.RunId, GetString(applyPreflight, "runId"), blockers);

        RequirePathMatch("workspace metadata workspacePath", workspacePath, NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath")), blockers);
        RequirePathMatch("diff workspacePath", workspacePath, NormalizeOptionalPath(GetString(diffEvidence, "workspacePath")), blockers);
        RequirePathMatch("promotion package workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionPackage, "workspacePath")), blockers);
        RequirePathMatch("approval evidence workspacePath", workspacePath, NormalizeOptionalPath(GetString(approvalEvidence, "workspacePath")), blockers);
        RequirePathMatch("apply preflight workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyPreflight, "workspacePath")), blockers);

        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));
        RequireSourceMatch("diff sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(diffEvidence, "sourceRepo")), blockers);
        RequireSourceMatch("promotion package sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(promotionPackage, "sourceRepo")), blockers);
        RequireSourceMatch("apply preflight sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyPreflight, "sourceRepo")), blockers);
        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");

        var preflight = GetObjectOrSelf(applyPreflight, "preflight");

        return new ArtifactFacts(
            SourceRepo: sourceRepo,
            ReadyForApply: GetBool(preflight, "readyForApply") ?? GetBool(applyPreflight, "readyForApply") ?? false,
            CanApplyNow: GetBool(preflight, "canApplyNow") ?? GetBool(applyPreflight, "canApplyNow") ?? true,
            RequiresSeparateApplyCommand: GetBool(preflight, "requiresSeparateApplyCommand") ?? GetBool(applyPreflight, "requiresSeparateApplyCommand") ?? false,
            Recommendation: GetString(preflight, "recommendation") ?? GetString(applyPreflight, "recommendation") ?? string.Empty,
            AddedFiles: GetStringArray(diffEvidence, "addedFiles"),
            ModifiedFiles: GetStringArray(diffEvidence, "modifiedFiles"),
            DeletedFiles: GetStringArray(diffEvidence, "deletedFiles"));
    }

    private static JsonElement GetObjectOrSelf(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object
            ? nested
            : element;

    private static async Task<IReadOnlyList<DisposableWorkspaceApplyOperation>> BuildOperationsAsync(
        ArtifactFacts facts,
        string workspacePath,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var operations = new List<DisposableWorkspaceApplyOperation>();
        foreach (var path in facts.AddedFiles)
            operations.Add(await BuildOperationAsync("add", path, facts.SourceRepo, workspacePath, blockers, cancellationToken).ConfigureAwait(false));
        foreach (var path in facts.ModifiedFiles)
            operations.Add(await BuildOperationAsync("modify", path, facts.SourceRepo, workspacePath, blockers, cancellationToken).ConfigureAwait(false));
        foreach (var path in facts.DeletedFiles)
            operations.Add(await BuildOperationAsync("delete", path, facts.SourceRepo, workspacePath, blockers, cancellationToken).ConfigureAwait(false));
        return operations;
    }

    private static void ValidateNoDuplicateOperations(ArtifactFacts facts, List<string> blockers)
    {
        var seen = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        ValidateOperationPaths("add", facts.AddedFiles, seen, blockers);
        ValidateOperationPaths("modify", facts.ModifiedFiles, seen, blockers);
        ValidateOperationPaths("delete", facts.DeletedFiles, seen, blockers);
    }

    private static void ValidateOperationPaths(
        string operation,
        IReadOnlyList<string> paths,
        Dictionary<string, string> seen,
        List<string> blockers)
    {
        var operationSeen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                blockers.Add($"Diff evidence contains an empty {operation} path.");
                continue;
            }

            if (!operationSeen.Add(path))
                blockers.Add($"Diff evidence contains duplicate {operation} operation for path: {path}");

            if (seen.TryGetValue(path, out var existingOperation))
            {
                blockers.Add($"Diff evidence contains conflicting operations for path '{path}': {existingOperation} and {operation}.");
                continue;
            }

            seen[path] = operation;
        }
    }

    private static async Task<DisposableWorkspaceApplyOperation> BuildOperationAsync(
        string operation,
        string relativePath,
        string sourceRepo,
        string workspaceRoot,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var safeRelativePath = NormalizeRelativePath(relativePath, blockers);
        var sourcePath = Path.GetFullPath(Path.Combine(sourceRepo, safeRelativePath));
        var workspacePath = Path.GetFullPath(Path.Combine(workspaceRoot, safeRelativePath));

        if (!IsSameOrInside(sourceRepo, sourcePath) || !IsSameOrInside(workspaceRoot, workspacePath))
            blockers.Add($"Apply dry run path escapes expected roots: {relativePath}");

        var sourceExists = File.Exists(sourcePath);
        var workspaceExists = File.Exists(workspacePath);
        var sourceDirectoryExists = Directory.Exists(sourcePath);
        var workspaceDirectoryExists = Directory.Exists(workspacePath);
        if (operation == "add" && sourceDirectoryExists)
            blockers.Add($"Add operation would conflict with an existing source directory: {relativePath}");
        if ((operation == "add" || operation == "modify") && workspaceDirectoryExists)
            blockers.Add($"Workspace path is a directory, not a file: {relativePath}");
        if ((operation == "modify" || operation == "delete") && sourceDirectoryExists)
            blockers.Add($"Source path is a directory, not a file: {relativePath}");
        if (operation == "add" && (!workspaceExists || sourceExists || sourceDirectoryExists || workspaceDirectoryExists))
            blockers.Add($"Add operation filesystem state does not match diff: {relativePath}");
        if (operation == "modify" && (!workspaceExists || !sourceExists || sourceDirectoryExists || workspaceDirectoryExists))
            blockers.Add($"Modify operation filesystem state does not match diff: {relativePath}");
        if (operation == "delete" && (!sourceExists || workspaceExists || sourceDirectoryExists || workspaceDirectoryExists))
            blockers.Add($"Delete operation filesystem state does not match diff: {relativePath}");

        var sourceSha = sourceExists ? await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false) : null;
        var workspaceSha = workspaceExists ? await ComputeSha256Async(workspacePath, cancellationToken).ConfigureAwait(false) : null;
        if (operation == "modify" && sourceSha is not null && workspaceSha is not null && string.Equals(sourceSha, workspaceSha, StringComparison.OrdinalIgnoreCase))
            blockers.Add($"Modify operation hashes do not differ: {relativePath}");

        return new DisposableWorkspaceApplyOperation
        {
            Operation = operation,
            RelativePath = safeRelativePath,
            SourcePath = sourcePath,
            WorkspacePath = workspacePath,
            SourceExists = sourceExists,
            WorkspaceExists = workspaceExists,
            SourceDirectoryExists = sourceDirectoryExists,
            WorkspaceDirectoryExists = workspaceDirectoryExists,
            SourceSha256 = sourceSha,
            WorkspaceSha256 = workspaceSha
        };
    }

    private static string NormalizeRelativePath(string relativePath, List<string> blockers)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            blockers.Add("Diff path is empty.");
            return string.Empty;
        }

        if (Path.IsPathRooted(relativePath))
            blockers.Add($"Diff path must be relative: {relativePath}");

        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            blockers.Add($"Diff path must not contain parent traversal: {relativePath}");
        if (segments.Any(segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, ".irondev", StringComparison.OrdinalIgnoreCase)))
            blockers.Add($"Diff path must not target reserved workspace metadata: {relativePath}");

        return Path.Combine(segments);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DisposableWorkspaceApplyDryRunResult Blocked(
        DisposableWorkspaceApplyDryRunRequest request,
        string workspacePath,
        string sourceRepo,
        bool readyForApply,
        bool canApplyNow,
        bool requiresSeparateApplyCommand,
        string recommendation,
        IReadOnlyList<DisposableWorkspaceApplyOperation> operations,
        string? workspaceMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        string? approvalEvidencePath,
        string? applyPreflightPath,
        string? applyDryRunPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace apply dry run was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, readyForApply, canApplyNow, requiresSeparateApplyCommand, recommendation, operations, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath, ExistingEvidencePaths(workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath), blockers, warnings, errors: blockers),
            Errors = blockers,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyDryRunResult Failed(
        DisposableWorkspaceApplyDryRunRequest request,
        string workspacePath,
        string sourceRepo,
        bool readyForApply,
        bool canApplyNow,
        bool requiresSeparateApplyCommand,
        string recommendation,
        IReadOnlyList<DisposableWorkspaceApplyOperation> operations,
        string? workspaceMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        string? approvalEvidencePath,
        string? applyPreflightPath,
        string? applyDryRunPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace apply dry run failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, readyForApply, canApplyNow, requiresSeparateApplyCommand, recommendation, operations, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath, applyDryRunPath, ExistingEvidencePaths(workspaceMetadataPath, diffMetadataPath, promotionPackagePath, approvalEvidencePath, applyPreflightPath), blockers: [], warnings, errors),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyDryRunData BuildData(
        DisposableWorkspaceApplyDryRunRequest request,
        string workspacePath,
        string sourceRepo,
        bool readyForApply,
        bool canApplyNow,
        bool requiresSeparateApplyCommand,
        string recommendation,
        IReadOnlyList<DisposableWorkspaceApplyOperation> operations,
        string? workspaceMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        string? approvalEvidencePath,
        string? applyPreflightPath,
        string? applyDryRunPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            ReadyForApply = readyForApply,
            CanApplyNow = canApplyNow,
            RequiresSeparateApplyCommand = requiresSeparateApplyCommand,
            Recommendation = recommendation,
            Operations = operations,
            AddCount = operations.Count(operation => operation.Operation == "add"),
            ModifyCount = operations.Count(operation => operation.Operation == "modify"),
            DeleteCount = operations.Count(operation => operation.Operation == "delete"),
            WorkspaceMetadataPath = workspaceMetadataPath,
            DiffMetadataPath = diffMetadataPath,
            PromotionPackagePath = promotionPackagePath,
            ApprovalEvidencePath = approvalEvidencePath,
            ApplyPreflightPath = applyPreflightPath,
            ApplyDryRunPath = applyDryRunPath,
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

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
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

    private sealed record ArtifactFacts(
        string SourceRepo,
        bool ReadyForApply,
        bool CanApplyNow,
        bool RequiresSeparateApplyCommand,
        string Recommendation,
        IReadOnlyList<string> AddedFiles,
        IReadOnlyList<string> ModifiedFiles,
        IReadOnlyList<string> DeletedFiles);
}

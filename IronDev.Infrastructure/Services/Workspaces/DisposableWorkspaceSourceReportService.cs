using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceSourceReportService : IDisposableWorkspaceSourceReportService
{
    private const string Recommendation = "ready_for_human_review_or_commit";

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceSourceReportResult> CreateAsync(
        DisposableWorkspaceSourceReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var diffMetadataPath = Path.Combine(runDirectory, "diff.json");
        var promotionPackagePath = Path.Combine(runDirectory, "promotion-package.json");
        var promotionApprovalPath = Path.Combine(runDirectory, "promotion-approval.json");
        var applyPreflightPath = Path.Combine(runDirectory, "apply-preflight.json");
        var applyDryRunPath = Path.Combine(runDirectory, "apply-dry-run.json");
        var applyCopyPath = Path.Combine(runDirectory, "apply-copy.json");
        var applyVerifyPath = Path.Combine(runDirectory, "apply-verify.json");
        var postApplyValidationPath = Path.Combine(runDirectory, "post-apply-validation.json");
        var sourceReportPath = Path.Combine(runDirectory, "source-report.json");
        var blockers = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;

        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        RequireFile(workspaceMetadataPath, "workspace metadata", blockers);
        RequireFile(diffMetadataPath, "diff evidence", blockers);
        RequireFile(promotionPackagePath, "promotion package", blockers);
        RequireFile(promotionApprovalPath, "promotion approval evidence", blockers);
        RequireFile(applyPreflightPath, "apply preflight evidence", blockers);
        RequireFile(applyDryRunPath, "apply dry run evidence", blockers);
        RequireFile(applyCopyPath, "apply copy evidence", blockers);
        RequireFile(applyVerifyPath, "apply verification evidence", blockers);
        RequireFile(postApplyValidationPath, "post-apply validation evidence", blockers);

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, false, false, false, false, "blocked", [], 0, [], workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, File.Exists(applyCopyPath) ? applyCopyPath : null, File.Exists(applyVerifyPath) ? applyVerifyPath : null, File.Exists(postApplyValidationPath) ? postApplyValidationPath : null, null, blockers, warnings);

        SourceReportFacts facts;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            using var diffEvidence = await ReadJsonAsync(diffMetadataPath, cancellationToken).ConfigureAwait(false);
            using var promotionPackage = await ReadJsonAsync(promotionPackagePath, cancellationToken).ConfigureAwait(false);
            using var promotionApproval = await ReadJsonAsync(promotionApprovalPath, cancellationToken).ConfigureAwait(false);
            using var applyPreflight = await ReadJsonAsync(applyPreflightPath, cancellationToken).ConfigureAwait(false);
            using var applyDryRun = await ReadJsonAsync(applyDryRunPath, cancellationToken).ConfigureAwait(false);
            using var applyCopy = await ReadJsonAsync(applyCopyPath, cancellationToken).ConfigureAwait(false);
            using var applyVerify = await ReadJsonAsync(applyVerifyPath, cancellationToken).ConfigureAwait(false);
            using var postApplyValidation = await ReadJsonAsync(postApplyValidationPath, cancellationToken).ConfigureAwait(false);

            facts = ReadFacts(
                request,
                workspacePath,
                workspaceMetadata.RootElement,
                diffEvidence.RootElement,
                promotionPackage.RootElement,
                promotionApproval.RootElement,
                applyPreflight.RootElement,
                applyDryRun.RootElement,
                applyCopy.RootElement,
                applyVerify.RootElement,
                postApplyValidation.RootElement,
                blockers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace source report could not read required evidence: {exception.Message}" };
            return Failed(request, workspacePath, string.Empty, false, false, false, false, "failed", [], 0, [], workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, null, errors, warnings);
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

        if (!facts.ApplyCopyApplied)
            blockers.Add("Apply copy evidence must report applied true.");
        if (!facts.ApplyCopySourceRepoMutated)
            blockers.Add("Apply copy evidence must report sourceRepoMutated true.");
        if (facts.ApplyCopyDeleteCount != 0)
            blockers.Add("Apply copy evidence must report deleteCount 0.");
        if (!facts.ApplyVerified)
            blockers.Add("Apply verification evidence must report verified true.");
        if (!facts.SourceMatchesWorkspace)
            blockers.Add("Apply verification evidence must report sourceMatchesWorkspace true.");
        if (facts.ApplyVerifyFailedCount != 0)
            blockers.Add("Apply verification evidence must report failedCount 0.");
        if (!facts.PostApplyValidationSucceeded)
            blockers.Add("Post-apply validation evidence must report validationSucceeded true.");
        if (!string.Equals(facts.PostApplyValidationStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Post-apply validation evidence must report validationStatus succeeded.");

        var files = BuildFileReport(facts, blockers);
        var riskNotes = BuildRiskNotes(files);

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, facts.SourceRepo, facts.ApplyCopySourceRepoMutated, facts.ApplyVerified, facts.SourceMatchesWorkspace, facts.PostApplyValidationSucceeded, facts.PostApplyValidationStatus, files, facts.ApplyCopyDeleteCount, riskNotes, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, null, blockers, warnings);

        var evidencePaths = new[]
        {
            workspaceMetadataPath,
            diffMetadataPath,
            promotionPackagePath,
            promotionApprovalPath,
            applyPreflightPath,
            applyDryRunPath,
            applyCopyPath,
            applyVerifyPath,
            postApplyValidationPath,
            sourceReportPath
        };
        var data = BuildData(request, workspacePath, facts.SourceRepo, facts.ApplyCopySourceRepoMutated, facts.ApplyVerified, facts.SourceMatchesWorkspace, facts.PostApplyValidationSucceeded, facts.PostApplyValidationStatus, files, facts.ApplyCopyDeleteCount, riskNotes, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, sourceReportPath, evidencePaths, blockers, warnings, errors: []);

        try
        {
            Directory.CreateDirectory(runDirectory);
            await WriteEvidenceAsync(data, createdUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace source report could not write evidence: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, facts.ApplyCopySourceRepoMutated, facts.ApplyVerified, facts.SourceMatchesWorkspace, facts.PostApplyValidationSucceeded, facts.PostApplyValidationStatus, files, facts.ApplyCopyDeleteCount, riskNotes, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, null, errors, warnings);
        }

        return new DisposableWorkspaceSourceReportResult
        {
            Status = "succeeded",
            Summary = "Workspace source change report created.",
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

    private static SourceReportFacts ReadFacts(
        DisposableWorkspaceSourceReportRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        JsonElement diffEvidence,
        JsonElement promotionPackage,
        JsonElement promotionApproval,
        JsonElement applyPreflight,
        JsonElement applyDryRun,
        JsonElement applyCopy,
        JsonElement applyVerify,
        JsonElement postApplyValidation,
        List<string> blockers)
    {
        RequireMatch("workspace metadata runId", request.RunId, GetString(workspaceMetadata, "runId"), blockers);
        RequireMatch("diff runId", request.RunId, GetString(diffEvidence, "runId"), blockers);
        RequireMatch("promotion package runId", request.RunId, GetString(promotionPackage, "runId"), blockers);
        RequireMatch("promotion approval runId", request.RunId, GetString(promotionApproval, "runId"), blockers);
        RequireMatch("apply preflight runId", request.RunId, GetString(applyPreflight, "runId"), blockers);
        RequireMatch("apply dry run runId", request.RunId, GetString(applyDryRun, "runId"), blockers);
        RequireMatch("apply copy runId", request.RunId, GetString(applyCopy, "runId"), blockers);
        RequireMatch("apply verify runId", request.RunId, GetString(applyVerify, "runId"), blockers);
        RequireMatch("post-apply validation runId", request.RunId, GetString(postApplyValidation, "runId"), blockers);

        RequirePathMatch("workspace metadata workspacePath", workspacePath, NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath")), blockers);
        RequirePathMatch("diff workspacePath", workspacePath, NormalizeOptionalPath(GetString(diffEvidence, "workspacePath")), blockers);
        RequirePathMatch("promotion package workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionPackage, "workspacePath")), blockers);
        RequirePathMatch("promotion approval workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionApproval, "workspacePath")), blockers);
        RequirePathMatch("apply preflight workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyPreflight, "workspacePath")), blockers);
        RequirePathMatch("apply dry run workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyDryRun, "workspacePath")), blockers);
        RequirePathMatch("apply copy workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyCopy, "workspacePath")), blockers);
        RequirePathMatch("apply verify workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyVerify, "workspacePath")), blockers);
        RequirePathMatch("post-apply validation workspacePath", workspacePath, NormalizeOptionalPath(GetString(postApplyValidation, "workspacePath")), blockers);

        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));
        RequireSourceMatch("diff sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(diffEvidence, "sourceRepo")), blockers);
        RequireSourceMatch("promotion package sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(promotionPackage, "sourceRepo")), blockers);
        RequireSourceMatch("apply preflight sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyPreflight, "sourceRepo")), blockers);
        RequireSourceMatch("apply dry run sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyDryRun, "sourceRepo")), blockers);
        RequireSourceMatch("apply copy sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyCopy, "sourceRepo")), blockers);
        RequireSourceMatch("apply verify sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyVerify, "sourceRepo")), blockers);
        RequireSourceMatch("post-apply validation sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(postApplyValidation, "sourceRepo")), blockers);
        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");

        return new SourceReportFacts(
            SourceRepo: sourceRepo,
            ApplyCopyApplied: GetBool(applyCopy, "applied") ?? false,
            ApplyCopySourceRepoMutated: GetBool(applyCopy, "sourceRepoMutated") ?? false,
            ApplyCopyDeleteCount: GetInt(applyCopy, "deleteCount") ?? -1,
            ApplyVerified: GetBool(applyVerify, "verified") ?? false,
            SourceMatchesWorkspace: GetBool(applyVerify, "sourceMatchesWorkspace") ?? false,
            ApplyVerifyFailedCount: GetInt(applyVerify, "failedCount") ?? -1,
            PostApplyValidationSucceeded: GetBool(postApplyValidation, "validationSucceeded") ?? false,
            PostApplyValidationStatus: GetString(postApplyValidation, "validationStatus") ?? string.Empty,
            ApplyCopyOperations: GetCopyOperations(applyCopy),
            ApplyVerifyOperations: GetVerifyOperations(applyVerify));
    }

    private static IReadOnlyList<CopyOperation> GetCopyOperations(JsonElement applyCopy)
    {
        if (!TryGetOperations(applyCopy, out var operations))
            return [];

        return operations.EnumerateArray()
            .Where(operation => operation.ValueKind == JsonValueKind.Object)
            .Select(operation => new CopyOperation(
                Operation: GetString(operation, "operation") ?? string.Empty,
                RelativePath: GetString(operation, "relativePath") ?? string.Empty,
                SourcePath: NormalizeOptionalPath(GetString(operation, "sourcePath")),
                WorkspacePath: NormalizeOptionalPath(GetString(operation, "workspacePath")),
                ActualSourceSha256Before: GetString(operation, "actualSourceSha256Before"),
                ActualWorkspaceSha256Before: GetString(operation, "actualWorkspaceSha256Before"),
                ActualSourceSha256After: GetString(operation, "actualSourceSha256After"),
                Applied: GetBool(operation, "applied") ?? false))
            .ToArray();
    }

    private static IReadOnlyList<VerifyOperation> GetVerifyOperations(JsonElement applyVerify)
    {
        if (!TryGetOperations(applyVerify, out var operations))
            return [];

        return operations.EnumerateArray()
            .Where(operation => operation.ValueKind == JsonValueKind.Object)
            .Select(operation => new VerifyOperation(
                Operation: GetString(operation, "operation") ?? string.Empty,
                RelativePath: GetString(operation, "relativePath") ?? string.Empty,
                Verified: GetBool(operation, "verified") ?? false))
            .ToArray();
    }

    private static bool TryGetOperations(JsonElement evidence, out JsonElement operations)
    {
        operations = default;
        return evidence.ValueKind == JsonValueKind.Object &&
            evidence.TryGetProperty("operations", out operations) &&
            operations.ValueKind == JsonValueKind.Array;
    }

    private static IReadOnlyList<DisposableWorkspaceSourceReportFile> BuildFileReport(
        SourceReportFacts facts,
        List<string> blockers)
    {
        var files = new List<DisposableWorkspaceSourceReportFile>();
        var copyByPath = new Dictionary<string, CopyOperation>(PathComparer);
        foreach (var operation in facts.ApplyCopyOperations)
        {
            var normalizedPath = NormalizeRelativePath(operation.RelativePath, blockers, "Apply copy");
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            if (!string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"Apply copy operation is unsupported: {operation.RelativePath}");
                continue;
            }

            if (!operation.Applied)
                blockers.Add($"Apply copy operation was not applied: {operation.RelativePath}");
            if (string.IsNullOrWhiteSpace(operation.SourcePath))
                blockers.Add($"Apply copy operation is missing sourcePath: {operation.RelativePath}");
            if (string.IsNullOrWhiteSpace(operation.WorkspacePath))
                blockers.Add($"Apply copy operation is missing workspacePath: {operation.RelativePath}");
            if (string.IsNullOrWhiteSpace(operation.ActualSourceSha256After))
                blockers.Add($"Apply copy operation is missing actualSourceSha256After: {operation.RelativePath}");

            if (copyByPath.ContainsKey(normalizedPath))
                blockers.Add($"Apply copy contains duplicate operation for normalized path: {normalizedPath}");
            else
                copyByPath[normalizedPath] = operation;
        }

        var verifyByPath = new Dictionary<string, VerifyOperation>(PathComparer);
        foreach (var operation in facts.ApplyVerifyOperations)
        {
            var normalizedPath = NormalizeRelativePath(operation.RelativePath, blockers, "Apply verification");
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            if (!string.Equals(operation.Operation, "add", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.Operation, "modify", StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"Apply verification operation is unsupported: {operation.RelativePath}");
                continue;
            }

            if (verifyByPath.ContainsKey(normalizedPath))
                blockers.Add($"Apply verification contains duplicate operation for normalized path: {normalizedPath}");
            else
                verifyByPath[normalizedPath] = operation;
        }

        foreach (var (normalizedPath, copyOperation) in copyByPath)
        {
            if (!verifyByPath.TryGetValue(normalizedPath, out var verifyOperation))
            {
                blockers.Add($"Apply/verify operation mismatch: missing verification for {normalizedPath}.");
                continue;
            }

            if (!string.Equals(copyOperation.Operation, verifyOperation.Operation, StringComparison.OrdinalIgnoreCase))
                blockers.Add($"Apply/verify operation mismatch for {normalizedPath}.");
            if (!verifyOperation.Verified)
                blockers.Add($"Apply verification operation was not verified: {normalizedPath}");

            files.Add(new DisposableWorkspaceSourceReportFile
            {
                Operation = copyOperation.Operation,
                RelativePath = normalizedPath,
                SourcePath = copyOperation.SourcePath,
                WorkspacePath = copyOperation.WorkspacePath,
                SourceSha256Before = copyOperation.ActualSourceSha256Before,
                WorkspaceSha256 = copyOperation.ActualWorkspaceSha256Before,
                SourceSha256After = copyOperation.ActualSourceSha256After,
                Applied = copyOperation.Applied,
                Verified = verifyOperation.Verified
            });
        }

        foreach (var normalizedPath in verifyByPath.Keys)
        {
            if (!copyByPath.ContainsKey(normalizedPath))
                blockers.Add($"Apply/verify operation mismatch: verification contains unapplied path {normalizedPath}.");
        }

        return files;
    }

    private static IReadOnlyList<string> BuildRiskNotes(IReadOnlyList<DisposableWorkspaceSourceReportFile> files)
    {
        var notes = new List<string>
        {
            "Source repository was mutated by apply-copy.",
            "Report is advisory and does not create a commit.",
            "Human should review changed files before commit/PR.",
            "Delete operations are not supported in this apply path."
        };

        if (files.Any(file => string.Equals(file.Operation, "modify", StringComparison.OrdinalIgnoreCase)))
            notes.Add("Modified files should be reviewed carefully.");
        if (files.Any(file => string.Equals(file.Operation, "add", StringComparison.OrdinalIgnoreCase)))
            notes.Add("Added files should be checked for naming, location, and ownership.");

        return notes;
    }

    private static async Task WriteEvidenceAsync(
        DisposableWorkspaceSourceReportData data,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            data.SourceReportPath!,
            JsonSerializer.Serialize(
                new
                {
                    runId = data.RunId,
                    workspacePath = data.WorkspacePath,
                    sourceRepo = data.SourceRepo,
                    createdUtc,
                    sourceRepoMutated = data.SourceRepoMutated,
                    applyVerified = data.ApplyVerified,
                    sourceMatchesWorkspace = data.SourceMatchesWorkspace,
                    postApplyValidationSucceeded = data.PostApplyValidationSucceeded,
                    postApplyValidationStatus = data.PostApplyValidationStatus,
                    recommendation = data.Recommendation,
                    files = data.Files,
                    addCount = data.AddCount,
                    modifyCount = data.ModifyCount,
                    deleteCount = data.DeleteCount,
                    riskNotes = data.RiskNotes,
                    evidencePaths = data.EvidencePaths,
                    blockers = data.Blockers,
                    warnings = data.Warnings,
                    errors = data.Errors
                },
                EvidenceJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static DisposableWorkspaceSourceReportResult Blocked(
        DisposableWorkspaceSourceReportRequest request,
        string workspacePath,
        string sourceRepo,
        bool sourceRepoMutated,
        bool applyVerified,
        bool sourceMatchesWorkspace,
        bool postApplyValidationSucceeded,
        string postApplyValidationStatus,
        IReadOnlyList<DisposableWorkspaceSourceReportFile> files,
        int deleteCount,
        IReadOnlyList<string> riskNotes,
        string? workspaceMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        string? promotionApprovalPath,
        string? applyPreflightPath,
        string? applyDryRunPath,
        string? applyCopyPath,
        string? applyVerifyPath,
        string? postApplyValidationPath,
        string? sourceReportPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace source change report was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, sourceRepoMutated, applyVerified, sourceMatchesWorkspace, postApplyValidationSucceeded, postApplyValidationStatus, files, deleteCount, riskNotes, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, sourceReportPath, ExistingEvidencePaths(workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath), blockers, warnings, errors: blockers),
            Errors = blockers,
            Warnings = warnings
        };

    private static DisposableWorkspaceSourceReportResult Failed(
        DisposableWorkspaceSourceReportRequest request,
        string workspacePath,
        string sourceRepo,
        bool sourceRepoMutated,
        bool applyVerified,
        bool sourceMatchesWorkspace,
        bool postApplyValidationSucceeded,
        string postApplyValidationStatus,
        IReadOnlyList<DisposableWorkspaceSourceReportFile> files,
        int deleteCount,
        IReadOnlyList<string> riskNotes,
        string? workspaceMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        string? promotionApprovalPath,
        string? applyPreflightPath,
        string? applyDryRunPath,
        string? applyCopyPath,
        string? applyVerifyPath,
        string? postApplyValidationPath,
        string? sourceReportPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace source change report failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, sourceRepoMutated, applyVerified, sourceMatchesWorkspace, postApplyValidationSucceeded, postApplyValidationStatus, files, deleteCount, riskNotes, workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, sourceReportPath, ExistingEvidencePaths(workspaceMetadataPath, diffMetadataPath, promotionPackagePath, promotionApprovalPath, applyPreflightPath, applyDryRunPath, applyCopyPath, applyVerifyPath, postApplyValidationPath, sourceReportPath), blockers: [], warnings, errors),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceSourceReportData BuildData(
        DisposableWorkspaceSourceReportRequest request,
        string workspacePath,
        string sourceRepo,
        bool sourceRepoMutated,
        bool applyVerified,
        bool sourceMatchesWorkspace,
        bool postApplyValidationSucceeded,
        string postApplyValidationStatus,
        IReadOnlyList<DisposableWorkspaceSourceReportFile> files,
        int deleteCount,
        IReadOnlyList<string> riskNotes,
        string? workspaceMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        string? promotionApprovalPath,
        string? applyPreflightPath,
        string? applyDryRunPath,
        string? applyCopyPath,
        string? applyVerifyPath,
        string? postApplyValidationPath,
        string? sourceReportPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            SourceRepoMutated = sourceRepoMutated,
            ApplyVerified = applyVerified,
            SourceMatchesWorkspace = sourceMatchesWorkspace,
            PostApplyValidationSucceeded = postApplyValidationSucceeded,
            PostApplyValidationStatus = postApplyValidationStatus,
            Recommendation = blockers.Count == 0 ? Recommendation : "blocked",
            Files = files,
            AddCount = files.Count(file => string.Equals(file.Operation, "add", StringComparison.OrdinalIgnoreCase)),
            ModifyCount = files.Count(file => string.Equals(file.Operation, "modify", StringComparison.OrdinalIgnoreCase)),
            DeleteCount = deleteCount,
            WorkspaceMetadataPath = workspaceMetadataPath,
            DiffMetadataPath = diffMetadataPath,
            PromotionPackagePath = promotionPackagePath,
            PromotionApprovalPath = promotionApprovalPath,
            ApplyPreflightPath = applyPreflightPath,
            ApplyDryRunPath = applyDryRunPath,
            ApplyCopyPath = applyCopyPath,
            ApplyVerifyPath = applyVerifyPath,
            PostApplyValidationPath = postApplyValidationPath,
            SourceReportPath = sourceReportPath,
            EvidencePaths = evidencePaths,
            RiskNotes = riskNotes,
            Blockers = blockers,
            Warnings = warnings,
            Errors = errors
        };

    private static IReadOnlyList<string> ExistingEvidencePaths(params string?[] paths) =>
        paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Select(path => path!).ToArray();

    private static string NormalizeRelativePath(string relativePath, List<string> blockers, string label)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            blockers.Add($"{label} operation relativePath is missing.");
            return string.Empty;
        }

        if (Path.IsPathRooted(relativePath))
            blockers.Add($"{label} operation path must be relative: {relativePath}");

        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            blockers.Add($"{label} operation path must not contain parent traversal: {relativePath}");
        if (segments.Any(segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, ".irondev", StringComparison.OrdinalIgnoreCase)))
            blockers.Add($"{label} operation path must not target reserved workspace metadata: {relativePath}");

        return segments.Length == 0 ? string.Empty : Path.Combine(segments);
    }

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

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record SourceReportFacts(
        string SourceRepo,
        bool ApplyCopyApplied,
        bool ApplyCopySourceRepoMutated,
        int ApplyCopyDeleteCount,
        bool ApplyVerified,
        bool SourceMatchesWorkspace,
        int ApplyVerifyFailedCount,
        bool PostApplyValidationSucceeded,
        string PostApplyValidationStatus,
        IReadOnlyList<CopyOperation> ApplyCopyOperations,
        IReadOnlyList<VerifyOperation> ApplyVerifyOperations);

    private sealed record CopyOperation(
        string Operation,
        string RelativePath,
        string SourcePath,
        string WorkspacePath,
        string? ActualSourceSha256Before,
        string? ActualWorkspaceSha256Before,
        string? ActualSourceSha256After,
        bool Applied);

    private sealed record VerifyOperation(
        string Operation,
        string RelativePath,
        bool Verified);
}

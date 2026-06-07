using System.Security.Cryptography;
using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceApplyPreflightService : IDisposableWorkspaceApplyPreflightService
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceApplyPreflightResult> CheckAsync(
        DisposableWorkspaceApplyPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var promotionPackagePath = Path.Combine(runDirectory, "promotion-package.json");
        var approvalEvidencePath = Path.Combine(runDirectory, "promotion-approval.json");
        var diffMetadataPath = Path.Combine(runDirectory, "diff.json");
        var applyPreflightPath = Path.Combine(runDirectory, "apply-preflight.json");
        var createdUtc = DateTimeOffset.UtcNow;
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        RequireFile(workspaceMetadataPath, "workspace metadata", blockers);
        RequireFile(promotionPackagePath, "promotion package", blockers);
        RequireFile(approvalEvidencePath, "promotion approval evidence", blockers);
        RequireFile(diffMetadataPath, "diff evidence", blockers);

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, promotionPackagePath, string.Empty, string.Empty, false, "not_ready_missing_evidence", false, false, [], [], [], workspaceMetadataPath, approvalEvidencePath, diffMetadataPath, applyPreflightPath: null, blockers, warnings);

        ArtifactFacts facts;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            using var promotionPackage = await ReadJsonAsync(promotionPackagePath, cancellationToken).ConfigureAwait(false);
            using var approvalEvidence = await ReadJsonAsync(approvalEvidencePath, cancellationToken).ConfigureAwait(false);
            using var diffEvidence = await ReadJsonAsync(diffMetadataPath, cancellationToken).ConfigureAwait(false);

            facts = ReadFacts(
                request,
                workspacePath,
                workspaceMetadata.RootElement,
                promotionPackage.RootElement,
                approvalEvidence.RootElement,
                diffEvidence.RootElement,
                promotionPackagePath,
                diffMetadataPath,
                blockers,
                warnings);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply preflight could not read required evidence: {exception.Message}" };
            return Failed(request, workspacePath, string.Empty, promotionPackagePath, string.Empty, string.Empty, false, "not_ready_missing_evidence", false, false, [], [], [], workspaceMetadataPath, approvalEvidencePath, diffMetadataPath, applyPreflightPath: null, errors, warnings);
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

        string currentPackageHash;
        try
        {
            currentPackageHash = await ComputeSha256Async(promotionPackagePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply preflight could not hash promotion package: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, promotionPackagePath, string.Empty, facts.ApprovalPromotionPackageSha256, false, "not_ready_missing_evidence", facts.ValidationSucceeded, facts.DiffChanged, facts.AddedFiles, facts.ModifiedFiles, facts.DeletedFiles, workspaceMetadataPath, approvalEvidencePath, diffMetadataPath, applyPreflightPath: null, errors, warnings, facts);
        }

        var hashMatchesApproval = string.Equals(currentPackageHash, facts.ApprovalPromotionPackageSha256, StringComparison.OrdinalIgnoreCase);
        if (!hashMatchesApproval)
            blockers.Add("Promotion package hash does not match approval evidence hash.");
        if (!string.Equals(facts.ApprovalDecision, "approved", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Promotion approval decision must be approved.");
        if (facts.ApprovalAllowsApply)
            blockers.Add("Promotion approval evidence is unsafe because allowsApply is true.");
        if (!facts.ApprovalRequiresSeparateApplyCommand)
            blockers.Add("Promotion approval evidence must require a separate apply command.");
        if (!facts.RequiresHumanApproval)
            blockers.Add("Promotion package must require human approval.");
        if (facts.CanApplyToSourceRepo)
            blockers.Add("Promotion package must not allow direct source repository apply.");
        if (facts.AutoPromotionAllowed)
            blockers.Add("Promotion package must not allow automatic promotion.");
        if (!string.Equals(facts.PackageRecommendation, "ready_for_human_review", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Promotion package recommendation is not ready_for_human_review.");
        if (!facts.ValidationSucceeded)
            blockers.Add("Validation did not succeed.");
        if (!facts.DiffChanged)
            blockers.Add("Diff has no changed files.");

        var recommendation = ResolveRecommendation(facts, hashMatchesApproval, blockers);
        if (blockers.Count > 0)
            return Blocked(request, workspacePath, facts.SourceRepo, promotionPackagePath, currentPackageHash, facts.ApprovalPromotionPackageSha256, hashMatchesApproval, recommendation, facts.ValidationSucceeded, facts.DiffChanged, facts.AddedFiles, facts.ModifiedFiles, facts.DeletedFiles, workspaceMetadataPath, approvalEvidencePath, diffMetadataPath, applyPreflightPath: null, blockers, warnings, facts);

        var evidencePaths = new[]
        {
            workspaceMetadataPath,
            promotionPackagePath,
            approvalEvidencePath,
            diffMetadataPath,
            applyPreflightPath
        };

        var data = BuildData(
            request,
            workspacePath,
            facts.SourceRepo,
            facts.ApprovalDecision,
            facts.ApprovedBy,
            facts.ApprovalReason,
            promotionPackagePath,
            currentPackageHash,
            facts.ApprovalPromotionPackageSha256,
            hashMatchesApproval,
            recommendation: "ready_for_separate_apply_command",
            validationSucceeded: facts.ValidationSucceeded,
            diffChanged: facts.DiffChanged,
            facts.AddedFiles,
            facts.ModifiedFiles,
            facts.DeletedFiles,
            readyForApply: true,
            canApplyNow: false,
            requiresSeparateApplyCommand: true,
            workspaceMetadataPath,
            facts.PromotionPackagePathFromEvidence,
            approvalEvidencePath,
            diffMetadataPath,
            applyPreflightPath,
            evidencePaths,
            blockers,
            warnings,
            errors: []);

        try
        {
            Directory.CreateDirectory(runDirectory);
            await File.WriteAllTextAsync(
                applyPreflightPath,
                JsonSerializer.Serialize(
                    new
                    {
                        runId = data.RunId,
                        workspacePath = data.WorkspacePath,
                        sourceRepo = data.SourceRepo,
                        createdUtc,
                        approval = new
                        {
                            decision = data.ApprovalDecision,
                            approvedBy = data.ApprovedBy,
                            reason = data.ApprovalReason,
                            evidencePath = data.ApprovalEvidencePath,
                            promotionPackageSha256 = data.ApprovalPromotionPackageSha256
                        },
                        promotionPackage = new
                        {
                            path = data.PromotionPackagePath,
                            currentSha256 = data.PromotionPackageSha256,
                            hashMatchesApproval = data.PromotionPackageHashMatchesApproval,
                            recommendation = facts.PackageRecommendation
                        },
                        diff = new
                        {
                            changed = data.DiffChanged,
                            addedFiles = data.AddedFiles,
                            modifiedFiles = data.ModifiedFiles,
                            deletedFiles = data.DeletedFiles
                        },
                        preflight = new
                        {
                            readyForApply = data.ReadyForApply,
                            canApplyNow = data.CanApplyNow,
                            requiresSeparateApplyCommand = data.RequiresSeparateApplyCommand,
                            recommendation = data.Recommendation,
                            blockers = data.Blockers
                        },
                        evidencePaths = data.EvidencePaths
                    },
                    EvidenceJsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace apply preflight could not write evidence: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, promotionPackagePath, currentPackageHash, facts.ApprovalPromotionPackageSha256, hashMatchesApproval, recommendation, facts.ValidationSucceeded, facts.DiffChanged, facts.AddedFiles, facts.ModifiedFiles, facts.DeletedFiles, workspaceMetadataPath, approvalEvidencePath, diffMetadataPath, applyPreflightPath: null, errors, warnings, facts);
        }

        return new DisposableWorkspaceApplyPreflightResult
        {
            Status = "succeeded",
            Summary = "Workspace apply preflight completed.",
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
        DisposableWorkspaceApplyPreflightRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        JsonElement promotionPackage,
        JsonElement approvalEvidence,
        JsonElement diffEvidence,
        string promotionPackagePath,
        string diffMetadataPath,
        List<string> blockers,
        List<string> warnings)
    {
        RequireMatch("workspace metadata runId", request.RunId, GetString(workspaceMetadata, "runId"), blockers);
        RequireMatch("promotion package runId", request.RunId, GetString(promotionPackage, "runId"), blockers);
        RequireMatch("approval evidence runId", request.RunId, GetString(approvalEvidence, "runId"), blockers);
        RequireMatch("diff evidence runId", request.RunId, GetString(diffEvidence, "runId"), blockers);

        RequirePathMatch("workspace metadata workspacePath", workspacePath, NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath")), blockers);
        RequirePathMatch("promotion package workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionPackage, "workspacePath")), blockers);
        RequirePathMatch("approval evidence workspacePath", workspacePath, NormalizeOptionalPath(GetString(approvalEvidence, "workspacePath")), blockers);
        RequirePathMatch("diff evidence workspacePath", workspacePath, NormalizeOptionalPath(GetString(diffEvidence, "workspacePath")), blockers);

        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));
        var packageSourceRepo = NormalizeOptionalPath(GetString(promotionPackage, "sourceRepo"));
        var diffSourceRepo = NormalizeOptionalPath(GetString(diffEvidence, "sourceRepo"));
        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");
        if (!string.IsNullOrWhiteSpace(packageSourceRepo) && !PathsEqual(sourceRepo, packageSourceRepo))
            blockers.Add("Promotion package sourceRepo does not match workspace metadata.");
        if (!string.IsNullOrWhiteSpace(diffSourceRepo) && !PathsEqual(sourceRepo, diffSourceRepo))
            blockers.Add("Diff evidence sourceRepo does not match workspace metadata.");

        var packagePathFromApproval = NormalizeOptionalPath(GetString(approvalEvidence, "promotionPackagePath"));
        if (!string.IsNullOrWhiteSpace(packagePathFromApproval) && !PathsEqual(packagePathFromApproval, promotionPackagePath))
            blockers.Add("Approval evidence promotion package path does not match current promotion package path.");

        var approval = GetObjectOrSelf(promotionPackage, "approval");
        var validation = GetObjectOrSelf(promotionPackage, "validation");
        var packageDiff = GetObjectOrSelf(promotionPackage, "diff");
        var requiresHumanApproval = GetBool(approval, "requiresHumanApproval") ?? GetBool(promotionPackage, "requiresHumanApproval") ?? false;
        var canApplyToSourceRepo = GetBool(approval, "canApplyToSourceRepo") ?? GetBool(promotionPackage, "canApplyToSourceRepo") ?? true;
        var autoPromotionAllowed = GetBool(approval, "autoPromotionAllowed") ?? GetBool(promotionPackage, "autoPromotionAllowed") ?? true;
        var validationSucceeded = GetBool(validation, "succeeded") ?? GetBool(promotionPackage, "validationSucceeded") ?? false;
        var packageRecommendation = GetString(promotionPackage, "recommendation") ?? string.Empty;
        var diffChanged = GetBool(diffEvidence, "changed") ?? GetBool(packageDiff, "changed") ?? GetBool(promotionPackage, "diffChanged") ?? false;

        var addedFiles = FirstNonEmpty(GetStringArray(diffEvidence, "addedFiles"), GetStringArray(packageDiff, "addedFiles"));
        var modifiedFiles = FirstNonEmpty(GetStringArray(diffEvidence, "modifiedFiles"), GetStringArray(packageDiff, "modifiedFiles"));
        var deletedFiles = FirstNonEmpty(GetStringArray(diffEvidence, "deletedFiles"), GetStringArray(packageDiff, "deletedFiles"));

        if (GetString(diffEvidence, "diffMetadataPath") is { Length: > 0 } diffPathFromEvidence &&
            !PathsEqual(NormalizeOptionalPath(diffPathFromEvidence), diffMetadataPath))
        {
            warnings.Add("Diff evidence path field does not match the expected diff path; using the required artifact path.");
        }

        return new ArtifactFacts(
            SourceRepo: sourceRepo,
            ApprovalDecision: GetString(approvalEvidence, "decision") ?? string.Empty,
            ApprovedBy: GetString(approvalEvidence, "approvedBy") ?? string.Empty,
            ApprovalReason: GetString(approvalEvidence, "reason") ?? string.Empty,
            ApprovalPromotionPackageSha256: GetString(approvalEvidence, "promotionPackageSha256") ?? string.Empty,
            ApprovalAllowsApply: GetBool(approvalEvidence, "allowsApply") ?? true,
            ApprovalRequiresSeparateApplyCommand: GetBool(approvalEvidence, "requiresSeparateApplyCommand") ?? false,
            PromotionPackagePathFromEvidence: packagePathFromApproval,
            RequiresHumanApproval: requiresHumanApproval,
            CanApplyToSourceRepo: canApplyToSourceRepo,
            AutoPromotionAllowed: autoPromotionAllowed,
            PackageRecommendation: packageRecommendation,
            ValidationSucceeded: validationSucceeded,
            DiffChanged: diffChanged,
            AddedFiles: addedFiles,
            ModifiedFiles: modifiedFiles,
            DeletedFiles: deletedFiles);
    }

    private static IReadOnlyList<string> FirstNonEmpty(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Count > 0 ? first : second;

    private static JsonElement GetObjectOrSelf(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object
            ? nested
            : element;

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

    private static string ResolveRecommendation(ArtifactFacts facts, bool hashMatchesApproval, IReadOnlyCollection<string> blockers)
    {
        if (blockers.Any(blocker => blocker.Contains("not found", StringComparison.OrdinalIgnoreCase) || blocker.Contains("missing", StringComparison.OrdinalIgnoreCase)))
            return "not_ready_missing_evidence";
        if (!string.Equals(facts.ApprovalDecision, "approved", StringComparison.OrdinalIgnoreCase))
            return "not_ready_rejected";
        if (!hashMatchesApproval)
            return "not_ready_stale_approval";
        if (!facts.ValidationSucceeded || !string.Equals(facts.PackageRecommendation, "ready_for_human_review", StringComparison.OrdinalIgnoreCase))
            return "not_ready_validation_failed";
        if (!facts.DiffChanged)
            return "not_ready_no_changes";
        return blockers.Count > 0 ? "not_ready_blocked" : "ready_for_separate_apply_command";
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DisposableWorkspaceApplyPreflightResult Blocked(
        DisposableWorkspaceApplyPreflightRequest request,
        string workspacePath,
        string sourceRepo,
        string promotionPackagePath,
        string promotionPackageSha256,
        string approvalPromotionPackageSha256,
        bool hashMatchesApproval,
        string recommendation,
        bool validationSucceeded,
        bool diffChanged,
        IReadOnlyList<string> addedFiles,
        IReadOnlyList<string> modifiedFiles,
        IReadOnlyList<string> deletedFiles,
        string? workspaceMetadataPath,
        string? approvalEvidencePath,
        string? diffMetadataPath,
        string? applyPreflightPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        ArtifactFacts? facts = null) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace apply preflight was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, facts?.ApprovalDecision ?? string.Empty, facts?.ApprovedBy ?? string.Empty, facts?.ApprovalReason ?? string.Empty, promotionPackagePath, promotionPackageSha256, approvalPromotionPackageSha256, hashMatchesApproval, recommendation, validationSucceeded, diffChanged, addedFiles, modifiedFiles, deletedFiles, readyForApply: false, canApplyNow: false, requiresSeparateApplyCommand: false, workspaceMetadataPath, facts?.PromotionPackagePathFromEvidence, approvalEvidencePath, diffMetadataPath, applyPreflightPath, ExistingEvidencePaths(workspaceMetadataPath, promotionPackagePath, approvalEvidencePath, diffMetadataPath), blockers, warnings, errors: blockers),
            Errors = blockers,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyPreflightResult Failed(
        DisposableWorkspaceApplyPreflightRequest request,
        string workspacePath,
        string sourceRepo,
        string promotionPackagePath,
        string promotionPackageSha256,
        string approvalPromotionPackageSha256,
        bool hashMatchesApproval,
        string recommendation,
        bool validationSucceeded,
        bool diffChanged,
        IReadOnlyList<string> addedFiles,
        IReadOnlyList<string> modifiedFiles,
        IReadOnlyList<string> deletedFiles,
        string? workspaceMetadataPath,
        string? approvalEvidencePath,
        string? diffMetadataPath,
        string? applyPreflightPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        ArtifactFacts? facts = null) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace apply preflight failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, facts?.ApprovalDecision ?? string.Empty, facts?.ApprovedBy ?? string.Empty, facts?.ApprovalReason ?? string.Empty, promotionPackagePath, promotionPackageSha256, approvalPromotionPackageSha256, hashMatchesApproval, recommendation, validationSucceeded, diffChanged, addedFiles, modifiedFiles, deletedFiles, readyForApply: false, canApplyNow: false, requiresSeparateApplyCommand: false, workspaceMetadataPath, facts?.PromotionPackagePathFromEvidence, approvalEvidencePath, diffMetadataPath, applyPreflightPath, ExistingEvidencePaths(workspaceMetadataPath, promotionPackagePath, approvalEvidencePath, diffMetadataPath), blockers: [], warnings, errors),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceApplyPreflightData BuildData(
        DisposableWorkspaceApplyPreflightRequest request,
        string workspacePath,
        string sourceRepo,
        string approvalDecision,
        string approvedBy,
        string approvalReason,
        string promotionPackagePath,
        string promotionPackageSha256,
        string approvalPromotionPackageSha256,
        bool hashMatchesApproval,
        string recommendation,
        bool validationSucceeded,
        bool diffChanged,
        IReadOnlyList<string> addedFiles,
        IReadOnlyList<string> modifiedFiles,
        IReadOnlyList<string> deletedFiles,
        bool readyForApply,
        bool canApplyNow,
        bool requiresSeparateApplyCommand,
        string? workspaceMetadataPath,
        string? promotionPackagePathFromEvidence,
        string? approvalEvidencePath,
        string? diffMetadataPath,
        string? applyPreflightPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            ApprovalDecision = approvalDecision,
            ApprovedBy = approvedBy,
            ApprovalReason = approvalReason,
            PromotionPackagePath = promotionPackagePath,
            PromotionPackageSha256 = promotionPackageSha256,
            ApprovalPromotionPackageSha256 = approvalPromotionPackageSha256,
            PromotionPackageHashMatchesApproval = hashMatchesApproval,
            Recommendation = recommendation,
            ValidationSucceeded = validationSucceeded,
            DiffChanged = diffChanged,
            AddedFiles = addedFiles,
            ModifiedFiles = modifiedFiles,
            DeletedFiles = deletedFiles,
            ReadyForApply = readyForApply,
            CanApplyNow = canApplyNow,
            RequiresSeparateApplyCommand = requiresSeparateApplyCommand,
            WorkspaceMetadataPath = workspaceMetadataPath,
            PromotionPackagePathFromEvidence = promotionPackagePathFromEvidence,
            ApprovalEvidencePath = approvalEvidencePath,
            DiffMetadataPath = diffMetadataPath,
            ApplyPreflightPath = applyPreflightPath,
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
        string ApprovalDecision,
        string ApprovedBy,
        string ApprovalReason,
        string ApprovalPromotionPackageSha256,
        bool ApprovalAllowsApply,
        bool ApprovalRequiresSeparateApplyCommand,
        string PromotionPackagePathFromEvidence,
        bool RequiresHumanApproval,
        bool CanApplyToSourceRepo,
        bool AutoPromotionAllowed,
        string PackageRecommendation,
        bool ValidationSucceeded,
        bool DiffChanged,
        IReadOnlyList<string> AddedFiles,
        IReadOnlyList<string> ModifiedFiles,
        IReadOnlyList<string> DeletedFiles);
}

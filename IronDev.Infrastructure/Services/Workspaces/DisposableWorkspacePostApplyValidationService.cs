using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspacePostApplyValidationService : IDisposableWorkspacePostApplyValidationService
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlySet<string> AllowedProfiles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dotnet-build-test"
        };

    private readonly IDisposableWorkspacePrepareService _prepareService;
    private readonly IDisposableWorkspaceValidationService _validationService;

    public DisposableWorkspacePostApplyValidationService(
        IDisposableWorkspacePrepareService prepareService,
        IDisposableWorkspaceValidationService validationService)
    {
        _prepareService = prepareService;
        _validationService = validationService;
    }

    public async Task<DisposableWorkspacePostApplyValidationResult> ValidateAsync(
        DisposableWorkspacePostApplyValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var validationWorkspacePath = NormalizePath($"{workspacePath}-post-apply-validation");
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var applyCopyPath = Path.Combine(runDirectory, "apply-copy.json");
        var applyVerifyPath = Path.Combine(runDirectory, "apply-verify.json");
        var applyDryRunPath = Path.Combine(runDirectory, "apply-dry-run.json");
        var applyPreflightPath = Path.Combine(runDirectory, "apply-preflight.json");
        var promotionApprovalPath = Path.Combine(runDirectory, "promotion-approval.json");
        var promotionPackagePath = Path.Combine(runDirectory, "promotion-package.json");
        var diffMetadataPath = Path.Combine(runDirectory, "diff.json");
        var postApplyValidationPath = Path.Combine(runDirectory, "post-apply-validation.json");
        var blockers = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;

        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        RequireFile(workspaceMetadataPath, "workspace metadata", blockers);
        RequireFile(applyCopyPath, "apply copy evidence", blockers);
        RequireFile(applyVerifyPath, "apply verification evidence", blockers);
        RequireFile(applyDryRunPath, "apply dry run evidence", blockers);
        RequireFile(applyPreflightPath, "apply preflight evidence", blockers);
        RequireFile(promotionApprovalPath, "promotion approval evidence", blockers);
        RequireFile(promotionPackagePath, "promotion package", blockers);
        RequireFile(diffMetadataPath, "diff evidence", blockers);

        if (!AllowedProfiles.Contains(request.ProfileId))
            blockers.Add($"Workspace post-apply validation profile '{request.ProfileId}' is not allowlisted.");

        if (Directory.Exists(validationWorkspacePath) &&
            Directory.EnumerateFileSystemEntries(validationWorkspacePath).Any())
        {
            blockers.Add("Post-apply validation workspace already exists and is not empty.");
        }

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, validationWorkspacePath, validationWorkspacePrepared: false, validationStatus: "blocked", validationSucceeded: false, steps: [], workspaceMetadataPath, applyCopyPath: File.Exists(applyCopyPath) ? applyCopyPath : null, applyVerifyPath: File.Exists(applyVerifyPath) ? applyVerifyPath : null, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, blockers, warnings);

        PostApplyFacts facts;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            using var applyCopy = await ReadJsonAsync(applyCopyPath, cancellationToken).ConfigureAwait(false);
            using var applyVerify = await ReadJsonAsync(applyVerifyPath, cancellationToken).ConfigureAwait(false);
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
                applyVerify.RootElement,
                applyDryRun.RootElement,
                applyPreflight.RootElement,
                promotionApproval.RootElement,
                promotionPackage.RootElement,
                diffEvidence.RootElement,
                blockers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace post-apply validation could not read required evidence: {exception.Message}" };
            return Failed(request, workspacePath, string.Empty, validationWorkspacePath, validationWorkspacePrepared: false, validationStatus: "failed", validationSucceeded: false, steps: [], workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, errors, warnings);
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
            blockers.Add("Apply copy evidence must not contain delete operations.");
        if (!facts.ApplyVerifyVerified)
            blockers.Add("Apply verification evidence must report verified true.");
        if (!facts.ApplyVerifySourceMatchesWorkspace)
            blockers.Add("Apply verification evidence must report sourceMatchesWorkspace true.");
        if (facts.ApplyVerifyFailedCount != 0)
            blockers.Add("Apply verification evidence must report failedCount 0.");
        if (facts.ApplyVerifyOperations.Any(operation =>
                !string.Equals(operation, "add", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation, "modify", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("Apply verification evidence must contain only add/modify operations.");
        }

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, facts.SourceRepo, validationWorkspacePath, validationWorkspacePrepared: false, validationStatus: "blocked", validationSucceeded: false, steps: [], workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, blockers, warnings);

        var workspaceRoot = Path.GetDirectoryName(validationWorkspacePath);
        var validationRunId = Path.GetFileName(validationWorkspacePath);
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(validationRunId))
        {
            blockers.Add("Post-apply validation workspace path could not be derived.");
            return Blocked(request, workspacePath, facts.SourceRepo, validationWorkspacePath, validationWorkspacePrepared: false, validationStatus: "blocked", validationSucceeded: false, steps: [], workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, blockers, warnings);
        }

        var prepareResult = await _prepareService.PrepareAsync(
            new DisposableWorkspacePrepareRequest
            {
                RunId = validationRunId,
                SourceRepo = facts.SourceRepo,
                WorkspaceRoot = workspaceRoot,
                AllowDirtySourceRepo = true
            },
            cancellationToken).ConfigureAwait(false);
        warnings.AddRange(prepareResult.Warnings);

        if (!string.Equals(prepareResult.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var errors = prepareResult.Errors.ToList();
            var status = string.Equals(prepareResult.Status, "blocked", StringComparison.OrdinalIgnoreCase) ? "blocked" : "failed";
            return status == "blocked"
                ? Blocked(request, workspacePath, facts.SourceRepo, validationWorkspacePath, validationWorkspacePrepared: false, validationStatus: "blocked", validationSucceeded: false, steps: [], workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, errors, warnings)
                : Failed(request, workspacePath, facts.SourceRepo, validationWorkspacePath, validationWorkspacePrepared: false, validationStatus: "failed", validationSucceeded: false, steps: [], workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, errors, warnings);
        }

        var validationResult = await _validationService.ValidateAsync(
            new DisposableWorkspaceValidationRequest
            {
                RunId = request.RunId,
                WorkspacePath = validationWorkspacePath,
                ProfileId = request.ProfileId
            },
            cancellationToken).ConfigureAwait(false);

        warnings.AddRange(validationResult.Warnings);
        var steps = validationResult.Data.Steps
            .Select(step => new DisposableWorkspacePostApplyValidationStep
            {
                CommandId = step.CommandId,
                Status = step.Status,
                ExitCode = step.ExitCode,
                Succeeded = step.Succeeded,
                EvidencePaths = step.EvidencePaths,
                Errors = step.Errors,
                Warnings = step.Warnings
            })
            .ToArray();

        var evidencePaths = ExistingEvidencePaths(
                workspaceMetadataPath,
                applyCopyPath,
                applyVerifyPath,
                applyDryRunPath,
                applyPreflightPath,
                promotionApprovalPath,
                promotionPackagePath,
                diffMetadataPath)
            .Concat(validationResult.Data.EvidencePaths)
            .Concat([postApplyValidationPath])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var validationSucceeded = string.Equals(validationResult.Status, "succeeded", StringComparison.OrdinalIgnoreCase) &&
            validationResult.Data.Succeeded;
        var data = BuildData(request, workspacePath, facts.SourceRepo, validationWorkspacePath, validationWorkspacePrepared: true, validationResult.Status, validationSucceeded, steps, workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath, evidencePaths, blockers: [], warnings, errors: validationResult.Errors);

        try
        {
            Directory.CreateDirectory(runDirectory);
            await WriteEvidenceAsync(data, createdUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace post-apply validation could not write evidence: {exception.Message}" };
            return Failed(request, workspacePath, facts.SourceRepo, validationWorkspacePath, validationWorkspacePrepared: true, validationResult.Status, validationSucceeded, steps, workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath: null, errors, warnings);
        }

        if (validationSucceeded)
        {
            return new DisposableWorkspacePostApplyValidationResult
            {
                Status = "succeeded",
                Summary = "Workspace post-apply validation completed.",
                ExitCode = 0,
                Data = data,
                Errors = [],
                Warnings = warnings
            };
        }

        if (string.Equals(validationResult.Status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return new DisposableWorkspacePostApplyValidationResult
            {
                Status = "blocked",
                Summary = "Workspace post-apply validation was blocked.",
                ExitCode = 1,
                Data = data with { Blockers = validationResult.Errors, Errors = validationResult.Errors },
                Errors = validationResult.Errors,
                Warnings = warnings
            };
        }

        return new DisposableWorkspacePostApplyValidationResult
        {
            Status = "failed",
            Summary = "Workspace post-apply validation failed.",
            ExitCode = 1,
            Data = data,
            Errors = validationResult.Errors,
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

    private static PostApplyFacts ReadFacts(
        DisposableWorkspacePostApplyValidationRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        JsonElement applyCopy,
        JsonElement applyVerify,
        JsonElement applyDryRun,
        JsonElement applyPreflight,
        JsonElement promotionApproval,
        JsonElement promotionPackage,
        JsonElement diffEvidence,
        List<string> blockers)
    {
        RequireMatch("workspace metadata runId", request.RunId, GetString(workspaceMetadata, "runId"), blockers);
        RequireMatch("apply copy runId", request.RunId, GetString(applyCopy, "runId"), blockers);
        RequireMatch("apply verify runId", request.RunId, GetString(applyVerify, "runId"), blockers);
        RequireMatch("apply dry run runId", request.RunId, GetString(applyDryRun, "runId"), blockers);
        RequireMatch("apply preflight runId", request.RunId, GetString(applyPreflight, "runId"), blockers);
        RequireMatch("promotion approval runId", request.RunId, GetString(promotionApproval, "runId"), blockers);
        RequireMatch("promotion package runId", request.RunId, GetString(promotionPackage, "runId"), blockers);
        RequireMatch("diff runId", request.RunId, GetString(diffEvidence, "runId"), blockers);

        RequirePathMatch("workspace metadata workspacePath", workspacePath, NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath")), blockers);
        RequirePathMatch("apply copy workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyCopy, "workspacePath")), blockers);
        RequirePathMatch("apply verify workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyVerify, "workspacePath")), blockers);
        RequirePathMatch("apply dry run workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyDryRun, "workspacePath")), blockers);
        RequirePathMatch("apply preflight workspacePath", workspacePath, NormalizeOptionalPath(GetString(applyPreflight, "workspacePath")), blockers);
        RequirePathMatch("promotion approval workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionApproval, "workspacePath")), blockers);
        RequirePathMatch("promotion package workspacePath", workspacePath, NormalizeOptionalPath(GetString(promotionPackage, "workspacePath")), blockers);
        RequirePathMatch("diff workspacePath", workspacePath, NormalizeOptionalPath(GetString(diffEvidence, "workspacePath")), blockers);

        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));
        RequireSourceMatch("apply copy sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyCopy, "sourceRepo")), blockers);
        RequireSourceMatch("apply verify sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyVerify, "sourceRepo")), blockers);
        RequireSourceMatch("apply dry run sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyDryRun, "sourceRepo")), blockers);
        RequireSourceMatch("apply preflight sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(applyPreflight, "sourceRepo")), blockers);
        RequireSourceMatch("promotion package sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(promotionPackage, "sourceRepo")), blockers);
        RequireSourceMatch("diff sourceRepo", sourceRepo, NormalizeOptionalPath(GetString(diffEvidence, "sourceRepo")), blockers);
        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");

        return new PostApplyFacts(
            SourceRepo: sourceRepo,
            ApplyCopyApplied: GetBool(applyCopy, "applied") ?? false,
            ApplyCopySourceRepoMutated: GetBool(applyCopy, "sourceRepoMutated") ?? false,
            ApplyCopyDeleteCount: GetInt(applyCopy, "deleteCount") ?? -1,
            ApplyVerifyVerified: GetBool(applyVerify, "verified") ?? false,
            ApplyVerifySourceMatchesWorkspace: GetBool(applyVerify, "sourceMatchesWorkspace") ?? false,
            ApplyVerifyFailedCount: GetInt(applyVerify, "failedCount") ?? -1,
            ApplyVerifyOperations: GetOperationNames(applyVerify));
    }

    private static IReadOnlyList<string> GetOperationNames(JsonElement evidence)
    {
        if (evidence.ValueKind != JsonValueKind.Object ||
            !evidence.TryGetProperty("operations", out var operations) ||
            operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return operations.EnumerateArray()
            .Where(operation => operation.ValueKind == JsonValueKind.Object)
            .Select(operation => GetString(operation, "operation") ?? string.Empty)
            .ToArray();
    }

    private static async Task WriteEvidenceAsync(
        DisposableWorkspacePostApplyValidationData data,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            data.PostApplyValidationPath!,
            JsonSerializer.Serialize(
                new
                {
                    runId = data.RunId,
                    workspacePath = data.WorkspacePath,
                    sourceRepo = data.SourceRepo,
                    createdUtc,
                    profileId = data.ProfileId,
                    validationWorkspacePath = data.ValidationWorkspacePath,
                    validationWorkspacePrepared = data.ValidationWorkspacePrepared,
                    validationStatus = data.ValidationStatus,
                    validationSucceeded = data.ValidationSucceeded,
                    steps = data.Steps,
                    evidencePaths = data.EvidencePaths,
                    blockers = data.Blockers,
                    warnings = data.Warnings,
                    errors = data.Errors
                },
                EvidenceJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static DisposableWorkspacePostApplyValidationResult Blocked(
        DisposableWorkspacePostApplyValidationRequest request,
        string workspacePath,
        string sourceRepo,
        string validationWorkspacePath,
        bool validationWorkspacePrepared,
        string validationStatus,
        bool validationSucceeded,
        IReadOnlyList<DisposableWorkspacePostApplyValidationStep> steps,
        string? workspaceMetadataPath,
        string? applyCopyPath,
        string? applyVerifyPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? postApplyValidationPath,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace post-apply validation was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, validationWorkspacePath, validationWorkspacePrepared, validationStatus, validationSucceeded, steps, workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath, ExistingEvidencePaths(workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath), blockers, warnings, errors: blockers),
            Errors = blockers,
            Warnings = warnings
        };

    private static DisposableWorkspacePostApplyValidationResult Failed(
        DisposableWorkspacePostApplyValidationRequest request,
        string workspacePath,
        string sourceRepo,
        string validationWorkspacePath,
        bool validationWorkspacePrepared,
        string validationStatus,
        bool validationSucceeded,
        IReadOnlyList<DisposableWorkspacePostApplyValidationStep> steps,
        string? workspaceMetadataPath,
        string? applyCopyPath,
        string? applyVerifyPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? postApplyValidationPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace post-apply validation failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, validationWorkspacePath, validationWorkspacePrepared, validationStatus, validationSucceeded, steps, workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath, ExistingEvidencePaths(workspaceMetadataPath, applyCopyPath, applyVerifyPath, applyDryRunPath, applyPreflightPath, promotionApprovalPath, promotionPackagePath, diffMetadataPath, postApplyValidationPath), blockers: [], warnings, errors),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspacePostApplyValidationData BuildData(
        DisposableWorkspacePostApplyValidationRequest request,
        string workspacePath,
        string sourceRepo,
        string validationWorkspacePath,
        bool validationWorkspacePrepared,
        string validationStatus,
        bool validationSucceeded,
        IReadOnlyList<DisposableWorkspacePostApplyValidationStep> steps,
        string? workspaceMetadataPath,
        string? applyCopyPath,
        string? applyVerifyPath,
        string? applyDryRunPath,
        string? applyPreflightPath,
        string? promotionApprovalPath,
        string? promotionPackagePath,
        string? diffMetadataPath,
        string? postApplyValidationPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            ProfileId = request.ProfileId,
            ValidationWorkspacePath = validationWorkspacePath,
            ValidationWorkspacePrepared = validationWorkspacePrepared,
            ValidationStatus = validationStatus,
            ValidationSucceeded = validationSucceeded,
            Steps = steps,
            WorkspaceMetadataPath = workspaceMetadataPath,
            ApplyCopyPath = applyCopyPath,
            ApplyVerifyPath = applyVerifyPath,
            ApplyDryRunPath = applyDryRunPath,
            ApplyPreflightPath = applyPreflightPath,
            PromotionApprovalPath = promotionApprovalPath,
            PromotionPackagePath = promotionPackagePath,
            DiffMetadataPath = diffMetadataPath,
            PostApplyValidationPath = postApplyValidationPath,
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

    private sealed record PostApplyFacts(
        string SourceRepo,
        bool ApplyCopyApplied,
        bool ApplyCopySourceRepoMutated,
        int ApplyCopyDeleteCount,
        bool ApplyVerifyVerified,
        bool ApplyVerifySourceMatchesWorkspace,
        int ApplyVerifyFailedCount,
        IReadOnlyList<string> ApplyVerifyOperations);
}

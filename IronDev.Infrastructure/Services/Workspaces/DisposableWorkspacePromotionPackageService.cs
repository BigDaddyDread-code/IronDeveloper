using System.Text.Json;

using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspacePromotionPackageService : IDisposableWorkspacePromotionPackageService
{
    private static readonly JsonSerializerOptions PromotionJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspacePromotionPackageResult> CreateAsync(
        DisposableWorkspacePromotionPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var errors = new List<string>();
        var warnings = new List<string>();
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var validationMetadataPath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "validation.json");
        var diffMetadataPath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "diff.json");
        var promotionPackagePath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "promotion-package.json");

        if (!Directory.Exists(workspacePath))
        {
            errors.Add($"Workspace path does not exist: {workspacePath}");
            return Failed(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!File.Exists(workspaceMetadataPath))
        {
            errors.Add("Workspace preparation metadata was not found. Run 'irondev workspace prepare' before creating a promotion package.");
            return Blocked(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        WorkspaceMetadata? workspaceMetadata;
        try
        {
            workspaceMetadata = await ReadJsonAsync<WorkspaceMetadata>(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add($"Workspace preparation metadata could not be read: {ex.Message}");
            return Failed(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (workspaceMetadata is null)
        {
            errors.Add("Workspace preparation metadata was empty.");
            return Blocked(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(workspaceMetadata.RunId))
        {
            errors.Add("Workspace metadata is missing runId.");
            return Blocked(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!string.Equals(workspaceMetadata.RunId, request.RunId, StringComparison.Ordinal))
        {
            errors.Add($"Workspace runId mismatch. Metadata runId '{workspaceMetadata.RunId}' does not match requested runId '{request.RunId}'.");
            return Blocked(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(workspaceMetadata.SourceRepo))
        {
            errors.Add("Workspace metadata is missing sourceRepo.");
            return Blocked(request, workspacePath, string.Empty, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(workspaceMetadata.WorkspacePath))
        {
            errors.Add("Workspace metadata is missing workspacePath.");
            return Blocked(request, workspacePath, workspaceMetadata.SourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        var sourceRepo = NormalizePath(workspaceMetadata.SourceRepo);
        var metadataWorkspacePath = NormalizePath(workspaceMetadata.WorkspacePath);
        if (!PathsEqual(workspacePath, metadataWorkspacePath))
        {
            errors.Add("Workspace path does not match the prepared workspace metadata.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!Directory.Exists(sourceRepo))
        {
            errors.Add($"Source repository from workspace metadata does not exist: {sourceRepo}");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (PathContainsSegment(workspacePath, ".git") ||
            PathsEqual(workspacePath, sourceRepo) ||
            IsSameOrInside(sourceRepo, workspacePath) ||
            IsSameOrInside(workspacePath, sourceRepo))
        {
            errors.Add("Workspace path and source repository must be isolated from each other.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!File.Exists(validationMetadataPath))
        {
            errors.Add("Workspace validation package was not found. Run 'irondev workspace validate' before creating a promotion package.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!File.Exists(diffMetadataPath))
        {
            errors.Add("Workspace diff package was not found. Run 'irondev workspace diff' before creating a promotion package.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        ValidationMetadata? validationMetadata;
        DiffMetadata? diffMetadata;
        try
        {
            validationMetadata = await ReadJsonAsync<ValidationMetadata>(validationMetadataPath, cancellationToken).ConfigureAwait(false);
            diffMetadata = await ReadJsonAsync<DiffMetadata>(diffMetadataPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add($"Promotion package evidence could not be read: {ex.Message}");
            return Failed(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (validationMetadata is null)
        {
            errors.Add("Workspace validation package was empty.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (diffMetadata is null)
        {
            errors.Add("Workspace diff package was empty.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!string.Equals(validationMetadata.RunId, request.RunId, StringComparison.Ordinal))
        {
            errors.Add("Workspace validation package runId does not match the requested runId.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(validationMetadata.WorkspacePath) ||
            !PathsEqual(validationMetadata.WorkspacePath, workspacePath))
        {
            errors.Add("Workspace validation package workspacePath does not match the prepared workspace.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (!string.Equals(diffMetadata.RunId, request.RunId, StringComparison.Ordinal))
        {
            errors.Add("Workspace diff package runId does not match the requested runId.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(diffMetadata.WorkspacePath) ||
            !PathsEqual(diffMetadata.WorkspacePath, workspacePath))
        {
            errors.Add("Workspace diff package workspacePath does not match the prepared workspace.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(diffMetadata.SourceRepo) ||
            !PathsEqual(diffMetadata.SourceRepo, sourceRepo))
        {
            errors.Add("Workspace diff package sourceRepo does not match the prepared workspace metadata.");
            return Blocked(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        var validationSucceeded = string.Equals(validationMetadata.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
        var recommendation = BuildRecommendation(validationSucceeded, diffMetadata.Changed);
        var riskNotes = BuildRiskNotes(validationSucceeded, diffMetadata.Changed, diffMetadata.DeletedFiles);
        var evidencePaths = BuildEvidencePaths(
            workspaceMetadataPath,
            validationMetadataPath,
            diffMetadataPath,
            promotionPackagePath,
            validationMetadata.Steps.SelectMany(step => step.EvidencePaths));

        var data = new DisposableWorkspacePromotionPackageData
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            ValidationStatus = validationMetadata.Status,
            ValidationSucceeded = validationSucceeded,
            DiffChanged = diffMetadata.Changed,
            AddedFiles = diffMetadata.AddedFiles,
            ModifiedFiles = diffMetadata.ModifiedFiles,
            DeletedFiles = diffMetadata.DeletedFiles,
            RequiresHumanApproval = true,
            CanApplyToSourceRepo = false,
            AutoPromotionAllowed = false,
            Recommendation = recommendation,
            RiskNotes = riskNotes,
            WorkspaceMetadataPath = workspaceMetadataPath,
            ValidationMetadataPath = validationMetadataPath,
            DiffMetadataPath = diffMetadataPath,
            PromotionPackagePath = promotionPackagePath,
            EvidencePaths = evidencePaths,
            Errors = errors,
            Warnings = warnings
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(promotionPackagePath)!);
            await File.WriteAllTextAsync(
                promotionPackagePath,
                JsonSerializer.Serialize(
                    new PromotionPackageMetadata
                    {
                        RunId = data.RunId,
                        WorkspacePath = data.WorkspacePath,
                        SourceRepo = data.SourceRepo,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Validation = new PromotionValidationMetadata
                        {
                            Status = data.ValidationStatus,
                            Succeeded = data.ValidationSucceeded,
                            MetadataPath = validationMetadataPath
                        },
                        Diff = new PromotionDiffMetadata
                        {
                            Changed = data.DiffChanged,
                            AddedFiles = data.AddedFiles,
                            ModifiedFiles = data.ModifiedFiles,
                            DeletedFiles = data.DeletedFiles,
                            MetadataPath = diffMetadataPath
                        },
                        Approval = new PromotionApprovalMetadata
                        {
                            RequiresHumanApproval = true,
                            CanApplyToSourceRepo = false,
                            AutoPromotionAllowed = false
                        },
                        Recommendation = data.Recommendation,
                        RiskNotes = data.RiskNotes,
                        EvidencePaths = data.EvidencePaths
                    },
                    PromotionJsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"Workspace promotion package could not be written: {ex.Message}");
            return Failed(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, errors, warnings);
        }

        return new DisposableWorkspacePromotionPackageResult
        {
            Status = "succeeded",
            Summary = "Workspace promotion package created.",
            ExitCode = 0,
            Data = data,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, PromotionJsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static DisposableWorkspacePromotionPackageResult Blocked(
        DisposableWorkspacePromotionPackageRequest request,
        string workspacePath,
        string sourceRepo,
        string? workspaceMetadataPath,
        string? validationMetadataPath,
        string? diffMetadataPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace promotion package was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, promotionPackagePath: null, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspacePromotionPackageResult Failed(
        DisposableWorkspacePromotionPackageRequest request,
        string workspacePath,
        string sourceRepo,
        string? workspaceMetadataPath,
        string? validationMetadataPath,
        string? diffMetadataPath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace promotion package failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, workspaceMetadataPath, validationMetadataPath, diffMetadataPath, promotionPackagePath: null, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspacePromotionPackageData BuildData(
        DisposableWorkspacePromotionPackageRequest request,
        string workspacePath,
        string sourceRepo,
        string? workspaceMetadataPath,
        string? validationMetadataPath,
        string? diffMetadataPath,
        string? promotionPackagePath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            ValidationStatus = "not_available",
            ValidationSucceeded = false,
            DiffChanged = false,
            AddedFiles = [],
            ModifiedFiles = [],
            DeletedFiles = [],
            RequiresHumanApproval = true,
            CanApplyToSourceRepo = false,
            AutoPromotionAllowed = false,
            Recommendation = "not_ready_missing_evidence",
            RiskNotes = ["Promotion package evidence is missing or unsafe."],
            WorkspaceMetadataPath = workspaceMetadataPath,
            ValidationMetadataPath = validationMetadataPath,
            DiffMetadataPath = diffMetadataPath,
            PromotionPackagePath = promotionPackagePath,
            EvidencePaths = [],
            Errors = errors,
            Warnings = warnings
        };

    private static string BuildRecommendation(bool validationSucceeded, bool diffChanged)
    {
        if (!validationSucceeded)
            return "not_ready_validation_failed";

        return diffChanged ? "ready_for_human_review" : "not_ready_no_changes";
    }

    private static IReadOnlyList<string> BuildRiskNotes(
        bool validationSucceeded,
        bool diffChanged,
        IReadOnlyList<string> deletedFiles)
    {
        var riskNotes = new List<string>();
        if (!validationSucceeded)
            riskNotes.Add("Validation did not succeed.");
        if (!diffChanged)
            riskNotes.Add("No changed files were detected.");
        if (diffChanged)
            riskNotes.Add("Changed files require human review before promotion.");
        if (deletedFiles.Count > 0)
            riskNotes.Add("Deleted files require careful review.");

        riskNotes.Add("Promotion package is advisory only and cannot apply changes.");
        return riskNotes;
    }

    private static IReadOnlyList<string> BuildEvidencePaths(
        string workspaceMetadataPath,
        string validationMetadataPath,
        string diffMetadataPath,
        string promotionPackagePath,
        IEnumerable<string> validationEvidencePaths)
    {
        return validationEvidencePaths
            .Append(workspaceMetadataPath)
            .Append(validationMetadataPath)
            .Append(diffMetadataPath)
            .Append(promotionPackagePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimEndingDirectorySeparator(NormalizePath(left)),
            TrimEndingDirectorySeparator(NormalizePath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string parent, string candidate)
    {
        var normalizedParent = TrimEndingDirectorySeparator(NormalizePath(parent));
        var normalizedCandidate = TrimEndingDirectorySeparator(NormalizePath(candidate));

        return string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathContainsSegment(string path, string segment)
    {
        return NormalizePath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimEndingDirectorySeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record WorkspaceMetadata
    {
        public string? RunId { get; init; }
        public string? SourceRepo { get; init; }
        public string? WorkspacePath { get; init; }
    }

    private sealed record ValidationMetadata
    {
        public string? RunId { get; init; }
        public string? WorkspacePath { get; init; }
        public string Status { get; init; } = "not_available";
        public IReadOnlyList<ValidationStepMetadata> Steps { get; init; } = [];
    }

    private sealed record ValidationStepMetadata
    {
        public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    }

    private sealed record DiffMetadata
    {
        public string? RunId { get; init; }
        public string? WorkspacePath { get; init; }
        public string? SourceRepo { get; init; }
        public bool Changed { get; init; }
        public IReadOnlyList<string> AddedFiles { get; init; } = [];
        public IReadOnlyList<string> ModifiedFiles { get; init; } = [];
        public IReadOnlyList<string> DeletedFiles { get; init; } = [];
    }

    private sealed record PromotionPackageMetadata
    {
        public required string RunId { get; init; }
        public required string WorkspacePath { get; init; }
        public required string SourceRepo { get; init; }
        public required DateTimeOffset CreatedUtc { get; init; }
        public required PromotionValidationMetadata Validation { get; init; }
        public required PromotionDiffMetadata Diff { get; init; }
        public required PromotionApprovalMetadata Approval { get; init; }
        public required string Recommendation { get; init; }
        public IReadOnlyList<string> RiskNotes { get; init; } = [];
        public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    }

    private sealed record PromotionValidationMetadata
    {
        public required string Status { get; init; }
        public required bool Succeeded { get; init; }
        public required string MetadataPath { get; init; }
    }

    private sealed record PromotionDiffMetadata
    {
        public required bool Changed { get; init; }
        public IReadOnlyList<string> AddedFiles { get; init; } = [];
        public IReadOnlyList<string> ModifiedFiles { get; init; } = [];
        public IReadOnlyList<string> DeletedFiles { get; init; } = [];
        public required string MetadataPath { get; init; }
    }

    private sealed record PromotionApprovalMetadata
    {
        public required bool RequiresHumanApproval { get; init; }
        public required bool CanApplyToSourceRepo { get; init; }
        public required bool AutoPromotionAllowed { get; init; }
    }
}

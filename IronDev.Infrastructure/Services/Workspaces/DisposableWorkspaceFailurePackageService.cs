using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceFailurePackageService : IDisposableWorkspaceFailurePackageService
{
    private static readonly string[] AllowedFailedStages =
    [
        "validate",
        "diff",
        "promotion-package",
        "promotion-approval",
        "apply-preflight",
        "apply-dry-run",
        "apply-copy",
        "apply-verify",
        "post-apply-validate",
        "source-report"
    ];

    private static readonly EvidenceDefinition[] KnownEvidence =
    [
        new("validation", "validation.json"),
        new("diff", "diff.json"),
        new("promotion-package", "promotion-package.json"),
        new("promotion-approval", "promotion-approval.json"),
        new("apply-preflight", "apply-preflight.json"),
        new("apply-dry-run", "apply-dry-run.json"),
        new("apply-copy", "apply-copy.json"),
        new("apply-verify", "apply-verify.json"),
        new("post-apply-validation", "post-apply-validation.json"),
        new("source-report", "source-report.json")
    ];

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceFailurePackageResult> CreateAsync(
        DisposableWorkspaceFailurePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var failedStage = NormalizeStage(request.FailedStage);
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId);
        var workspaceMetadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var failurePackagePath = Path.Combine(runDirectory, "failure-package.json");
        var blockers = new List<string>();
        var warnings = new List<string>();
        var createdUtc = DateTimeOffset.UtcNow;

        if (!AllowedFailedStages.Contains(failedStage, StringComparer.OrdinalIgnoreCase))
            blockers.Add($"Failed stage is not allowlisted: {request.FailedStage}");
        if (!Directory.Exists(workspacePath))
            blockers.Add("Workspace path does not exist.");
        if (!File.Exists(workspaceMetadataPath))
            blockers.Add("Workspace metadata was not found.");

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, string.Empty, failedStage, workspaceMetadataPath, null, [], [], [], [], false, false, false, false, false, "blocked", "inspect_evidence_before_retry", [], blockers, warnings);

        WorkspaceMetadata metadata;
        try
        {
            using var workspaceMetadata = await ReadJsonAsync(workspaceMetadataPath, cancellationToken).ConfigureAwait(false);
            metadata = ReadWorkspaceMetadata(request, workspacePath, workspaceMetadata.RootElement, blockers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            blockers.Add($"Workspace metadata could not be read: {exception.Message}");
            return Blocked(request, workspacePath, string.Empty, failedStage, workspaceMetadataPath, null, [], [], [], [], false, false, false, false, false, "blocked", "inspect_evidence_before_retry", [], blockers, warnings);
        }

        if (!string.IsNullOrWhiteSpace(metadata.SourceRepo) && !Directory.Exists(metadata.SourceRepo))
            blockers.Add("Source repository does not exist.");
        if (!string.IsNullOrWhiteSpace(metadata.SourceRepo) &&
            (PathsEqual(workspacePath, metadata.SourceRepo) ||
             IsSameOrInside(metadata.SourceRepo, workspacePath) ||
             IsSameOrInside(workspacePath, metadata.SourceRepo)))
        {
            blockers.Add("Workspace path and source repository must be isolated from each other.");
        }

        if (blockers.Count > 0)
            return Blocked(request, workspacePath, metadata.SourceRepo, failedStage, workspaceMetadataPath, null, [], [], [], [], false, false, false, false, false, "blocked", "inspect_evidence_before_retry", [], blockers, warnings);

        var evidenceRead = await ReadEvidenceFilesAsync(runDirectory, warnings, cancellationToken).ConfigureAwait(false);
        var evidenceByName = evidenceRead.Facts.ToDictionary(fact => fact.Name, StringComparer.OrdinalIgnoreCase);
        var applyCopyAttempted = evidenceByName.ContainsKey("apply-copy");
        var applyCopySucceeded = TryGetBool(evidenceByName, "apply-copy", "applied");
        var sourceRepoMutated = TryGetBool(evidenceByName, "apply-copy", "sourceRepoMutated");
        var applyVerified = TryGetBool(evidenceByName, "apply-verify", "verified");
        var postApplyValidationSucceeded = TryGetBool(evidenceByName, "post-apply-validation", "validationSucceeded");
        var failureSeverity = DetermineFailureSeverity(failedStage, sourceRepoMutated, applyVerified, postApplyValidationSucceeded);
        var recommendedNextAction = DetermineRecommendedNextAction(sourceRepoMutated, applyVerified, postApplyValidationSucceeded, evidenceRead.HasUnreadableEvidence);
        var riskNotes = BuildRiskNotes(sourceRepoMutated, applyVerified, postApplyValidationSucceeded);
        var existingEvidencePaths = evidenceRead.EvidenceFiles
            .Where(file => file.Exists)
            .Select(file => file.Path)
            .ToArray();
        var evidencePaths = new[] { workspaceMetadataPath }
            .Concat(existingEvidencePaths)
            .Append(failurePackagePath)
            .ToArray();

        var data = BuildData(
            request,
            workspacePath,
            metadata.SourceRepo,
            failedStage,
            sourceRepoMutated,
            applyCopyAttempted,
            applyCopySucceeded,
            applyVerified,
            postApplyValidationSucceeded,
            failureSeverity,
            recommendedNextAction,
            evidenceRead.MissingEvidence,
            existingEvidencePaths,
            evidenceRead.EvidenceFiles,
            evidenceRead.AggregatedErrors,
            evidenceRead.AggregatedWarnings,
            evidenceRead.AggregatedBlockers,
            riskNotes,
            workspaceMetadataPath,
            failurePackagePath,
            evidencePaths,
            errors: [],
            warnings);

        try
        {
            Directory.CreateDirectory(runDirectory);
            await WriteEvidenceAsync(data, createdUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var errors = new List<string> { $"Workspace failure package could not be written: {exception.Message}" };
            return Failed(request, workspacePath, metadata.SourceRepo, failedStage, workspaceMetadataPath, null, evidenceRead.MissingEvidence, existingEvidencePaths, evidenceRead.EvidenceFiles, riskNotes, sourceRepoMutated, applyCopyAttempted, applyCopySucceeded, applyVerified, postApplyValidationSucceeded, failureSeverity, recommendedNextAction, evidenceRead.AggregatedErrors, evidenceRead.AggregatedWarnings, evidenceRead.AggregatedBlockers, errors, warnings);
        }

        return new DisposableWorkspaceFailurePackageResult
        {
            Status = "succeeded",
            Summary = "Workspace failure package created.",
            ExitCode = 0,
            Data = data,
            Errors = [],
            Warnings = warnings
        };
    }

    private static async Task<EvidenceReadResult> ReadEvidenceFilesAsync(
        string runDirectory,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var evidenceFiles = new List<DisposableWorkspaceFailureEvidenceFile>();
        var facts = new List<EvidenceFacts>();
        var missingEvidence = new List<string>();
        var aggregatedErrors = new List<string>();
        var aggregatedWarnings = new List<string>();
        var aggregatedBlockers = new List<string>();
        var hasUnreadableEvidence = false;

        foreach (var evidence in KnownEvidence)
        {
            var path = Path.Combine(runDirectory, evidence.FileName);
            if (!File.Exists(path))
            {
                missingEvidence.Add(evidence.FileName);
                evidenceFiles.Add(new DisposableWorkspaceFailureEvidenceFile
                {
                    Name = evidence.Name,
                    Path = path,
                    Exists = false
                });
                continue;
            }

            try
            {
                using var document = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                var errors = ReadStringArray(root, "errors").Concat(ReadDataStringArray(root, "errors")).Distinct().ToArray();
                var evidenceWarnings = ReadStringArray(root, "warnings").Concat(ReadDataStringArray(root, "warnings")).Distinct().ToArray();
                var blockers = ReadStringArray(root, "blockers").Concat(ReadDataStringArray(root, "blockers")).Distinct().ToArray();
                var status = GetRootOrDataString(root, "status") ?? GetRootOrDataString(root, "validationStatus") ?? GetRootOrDataString(root, "recommendation");
                var succeeded = GetSucceeded(root);

                aggregatedErrors.AddRange(errors);
                aggregatedWarnings.AddRange(evidenceWarnings);
                aggregatedBlockers.AddRange(blockers);
                evidenceFiles.Add(new DisposableWorkspaceFailureEvidenceFile
                {
                    Name = evidence.Name,
                    Path = path,
                    Exists = true,
                    Status = status,
                    Succeeded = succeeded,
                    Errors = errors,
                    Warnings = evidenceWarnings,
                    Blockers = blockers
                });
                facts.Add(new EvidenceFacts(evidence.Name, root.Clone()));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                hasUnreadableEvidence = true;
                var message = $"Evidence file could not be parsed: {evidence.FileName}: {exception.Message}";
                warnings.Add(message);
                aggregatedWarnings.Add(message);
                evidenceFiles.Add(new DisposableWorkspaceFailureEvidenceFile
                {
                    Name = evidence.Name,
                    Path = path,
                    Exists = true,
                    Warnings = [message]
                });
            }
        }

        return new EvidenceReadResult(
            MissingEvidence: missingEvidence,
            EvidenceFiles: evidenceFiles,
            Facts: facts,
            AggregatedErrors: aggregatedErrors.Distinct().ToArray(),
            AggregatedWarnings: aggregatedWarnings.Distinct().ToArray(),
            AggregatedBlockers: aggregatedBlockers.Distinct().ToArray(),
            HasUnreadableEvidence: hasUnreadableEvidence);
    }

    private static WorkspaceMetadata ReadWorkspaceMetadata(
        DisposableWorkspaceFailurePackageRequest request,
        string workspacePath,
        JsonElement workspaceMetadata,
        List<string> blockers)
    {
        var runId = GetString(workspaceMetadata, "runId");
        var metadataWorkspacePath = NormalizeOptionalPath(GetString(workspaceMetadata, "workspacePath"));
        var sourceRepo = NormalizeOptionalPath(GetString(workspaceMetadata, "sourceRepo"));

        if (string.IsNullOrWhiteSpace(runId))
            blockers.Add("Workspace metadata is missing runId.");
        else if (!string.Equals(request.RunId, runId, StringComparison.Ordinal))
            blockers.Add("Workspace metadata runId mismatch.");

        if (string.IsNullOrWhiteSpace(metadataWorkspacePath))
            blockers.Add("Workspace metadata is missing workspacePath.");
        else if (!PathsEqual(workspacePath, metadataWorkspacePath))
            blockers.Add("Workspace metadata workspacePath mismatch.");

        if (string.IsNullOrWhiteSpace(sourceRepo))
            blockers.Add("Workspace metadata is missing sourceRepo.");

        return new WorkspaceMetadata(sourceRepo);
    }

    private static bool TryGetBool(
        IReadOnlyDictionary<string, EvidenceFacts> evidenceByName,
        string name,
        string propertyName) =>
        evidenceByName.TryGetValue(name, out var evidence) &&
        (GetRootOrDataBool(evidence.Root, propertyName) ?? false);

    private static bool? GetSucceeded(JsonElement root) =>
        GetRootOrDataBool(root, "succeeded") ??
        GetRootOrDataBool(root, "validationSucceeded") ??
        GetRootOrDataBool(root, "ready") ??
        GetRootOrDataBool(root, "readyForApply") ??
        GetRootOrDataBool(root, "applied") ??
        GetRootOrDataBool(root, "verified");

    private static string DetermineFailureSeverity(
        string failedStage,
        bool sourceRepoMutated,
        bool applyVerified,
        bool postApplyValidationSucceeded)
    {
        if (sourceRepoMutated && !applyVerified)
            return "critical";
        if (sourceRepoMutated && applyVerified && !postApplyValidationSucceeded)
            return "high";
        if (!sourceRepoMutated && IsApplyStage(failedStage))
            return "warning";
        if (!sourceRepoMutated && IsBeforeApplyStage(failedStage))
            return "info";

        return "high";
    }

    private static string DetermineRecommendedNextAction(
        bool sourceRepoMutated,
        bool applyVerified,
        bool postApplyValidationSucceeded,
        bool hasUnreadableEvidence)
    {
        if (sourceRepoMutated && !applyVerified)
            return "do_not_retry_until_source_reviewed";
        if (sourceRepoMutated && applyVerified && !postApplyValidationSucceeded)
            return "fix_validation_failure";
        if (hasUnreadableEvidence)
            return "inspect_evidence_before_retry";

        return "safe_to_retry_after_fixing_blockers";
    }

    private static bool IsApplyStage(string failedStage) =>
        string.Equals(failedStage, "apply-preflight", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(failedStage, "apply-dry-run", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(failedStage, "apply-copy", StringComparison.OrdinalIgnoreCase);

    private static bool IsBeforeApplyStage(string failedStage) =>
        string.Equals(failedStage, "validate", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(failedStage, "diff", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(failedStage, "promotion-package", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(failedStage, "promotion-approval", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildRiskNotes(
        bool sourceRepoMutated,
        bool applyVerified,
        bool postApplyValidationSucceeded)
    {
        var notes = new List<string>
        {
            "Failure package is advisory and does not repair or roll back changes.",
            "No source files were modified by failure-package.",
            "Human review is required before retrying a failed apply flow."
        };

        if (sourceRepoMutated)
        {
            notes.Add("Source repository may contain applied changes.");
            notes.Add("Do not rerun apply-copy blindly.");
            notes.Add("Inspect source-report/apply-copy/apply-verify evidence before retrying.");
        }

        if (!applyVerified)
            notes.Add("Applied source state has not been verified.");
        if (!postApplyValidationSucceeded)
            notes.Add("Post-apply validation did not succeed.");

        return notes;
    }

    private static async Task WriteEvidenceAsync(
        DisposableWorkspaceFailurePackageData data,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            data.FailurePackagePath!,
            JsonSerializer.Serialize(
                new
                {
                    runId = data.RunId,
                    workspacePath = data.WorkspacePath,
                    sourceRepo = data.SourceRepo,
                    failedStage = data.FailedStage,
                    createdUtc,
                    sourceRepoMutated = data.SourceRepoMutated,
                    applyCopyAttempted = data.ApplyCopyAttempted,
                    applyCopySucceeded = data.ApplyCopySucceeded,
                    applyVerified = data.ApplyVerified,
                    postApplyValidationSucceeded = data.PostApplyValidationSucceeded,
                    failureSeverity = data.FailureSeverity,
                    recommendedNextAction = data.RecommendedNextAction,
                    missingEvidence = data.MissingEvidence,
                    existingEvidencePaths = data.ExistingEvidencePaths,
                    evidenceFiles = data.EvidenceFiles,
                    aggregatedErrors = data.AggregatedErrors,
                    aggregatedWarnings = data.AggregatedWarnings,
                    aggregatedBlockers = data.AggregatedBlockers,
                    riskNotes = data.RiskNotes,
                    evidencePaths = data.EvidencePaths,
                    errors = data.Errors,
                    warnings = data.Warnings
                },
                EvidenceJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static DisposableWorkspaceFailurePackageResult Blocked(
        DisposableWorkspaceFailurePackageRequest request,
        string workspacePath,
        string sourceRepo,
        string failedStage,
        string? workspaceMetadataPath,
        string? failurePackagePath,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> existingEvidencePaths,
        IReadOnlyList<DisposableWorkspaceFailureEvidenceFile> evidenceFiles,
        IReadOnlyList<string> riskNotes,
        bool sourceRepoMutated,
        bool applyCopyAttempted,
        bool applyCopySucceeded,
        bool applyVerified,
        bool postApplyValidationSucceeded,
        string failureSeverity,
        string recommendedNextAction,
        IReadOnlyList<string> aggregatedErrors,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "blocked",
            Summary = "Workspace failure package was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, failedStage, sourceRepoMutated, applyCopyAttempted, applyCopySucceeded, applyVerified, postApplyValidationSucceeded, failureSeverity, recommendedNextAction, missingEvidence, existingEvidencePaths, evidenceFiles, aggregatedErrors, warnings, errors, riskNotes, workspaceMetadataPath, failurePackagePath, existingEvidencePaths, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceFailurePackageResult Failed(
        DisposableWorkspaceFailurePackageRequest request,
        string workspacePath,
        string sourceRepo,
        string failedStage,
        string? workspaceMetadataPath,
        string? failurePackagePath,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> existingEvidencePaths,
        IReadOnlyList<DisposableWorkspaceFailureEvidenceFile> evidenceFiles,
        IReadOnlyList<string> riskNotes,
        bool sourceRepoMutated,
        bool applyCopyAttempted,
        bool applyCopySucceeded,
        bool applyVerified,
        bool postApplyValidationSucceeded,
        string failureSeverity,
        string recommendedNextAction,
        IReadOnlyList<string> aggregatedErrors,
        IReadOnlyList<string> aggregatedWarnings,
        IReadOnlyList<string> aggregatedBlockers,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Status = "failed",
            Summary = "Workspace failure package failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, failedStage, sourceRepoMutated, applyCopyAttempted, applyCopySucceeded, applyVerified, postApplyValidationSucceeded, failureSeverity, recommendedNextAction, missingEvidence, existingEvidencePaths, evidenceFiles, aggregatedErrors, aggregatedWarnings, aggregatedBlockers, riskNotes, workspaceMetadataPath, failurePackagePath, existingEvidencePaths, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };

    private static DisposableWorkspaceFailurePackageData BuildData(
        DisposableWorkspaceFailurePackageRequest request,
        string workspacePath,
        string sourceRepo,
        string failedStage,
        bool sourceRepoMutated,
        bool applyCopyAttempted,
        bool applyCopySucceeded,
        bool applyVerified,
        bool postApplyValidationSucceeded,
        string failureSeverity,
        string recommendedNextAction,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> existingEvidencePaths,
        IReadOnlyList<DisposableWorkspaceFailureEvidenceFile> evidenceFiles,
        IReadOnlyList<string> aggregatedErrors,
        IReadOnlyList<string> aggregatedWarnings,
        IReadOnlyList<string> aggregatedBlockers,
        IReadOnlyList<string> riskNotes,
        string? workspaceMetadataPath,
        string? failurePackagePath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) =>
        new()
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            FailedStage = failedStage,
            SourceRepoMutated = sourceRepoMutated,
            ApplyCopyAttempted = applyCopyAttempted,
            ApplyCopySucceeded = applyCopySucceeded,
            ApplyVerified = applyVerified,
            PostApplyValidationSucceeded = postApplyValidationSucceeded,
            FailureSeverity = failureSeverity,
            RecommendedNextAction = recommendedNextAction,
            MissingEvidence = missingEvidence,
            ExistingEvidencePaths = existingEvidencePaths,
            EvidenceFiles = evidenceFiles,
            AggregatedErrors = aggregatedErrors,
            AggregatedWarnings = aggregatedWarnings,
            AggregatedBlockers = aggregatedBlockers,
            RiskNotes = riskNotes,
            WorkspaceMetadataPath = workspaceMetadataPath,
            FailurePackagePath = failurePackagePath,
            EvidencePaths = evidencePaths,
            Errors = errors,
            Warnings = warnings
        };

    private static async Task<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ReadDataStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return ReadStringArray(data, propertyName);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static string? GetRootOrDataString(JsonElement root, string propertyName) =>
        GetString(root, propertyName) ?? GetDataString(root, propertyName);

    private static string? GetDataString(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(data, propertyName);
    }

    private static bool? GetRootOrDataBool(JsonElement root, string propertyName) =>
        GetBool(root, propertyName) ?? GetDataBool(root, propertyName);

    private static bool? GetDataBool(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetBool(data, propertyName);
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

    private static string NormalizeStage(string stage) => stage.Trim().ToLowerInvariant();

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

    private sealed record EvidenceDefinition(string Name, string FileName);

    private sealed record WorkspaceMetadata(string SourceRepo);

    private sealed record EvidenceFacts(string Name, JsonElement Root);

    private sealed record EvidenceReadResult(
        IReadOnlyList<string> MissingEvidence,
        IReadOnlyList<DisposableWorkspaceFailureEvidenceFile> EvidenceFiles,
        IReadOnlyList<EvidenceFacts> Facts,
        IReadOnlyList<string> AggregatedErrors,
        IReadOnlyList<string> AggregatedWarnings,
        IReadOnlyList<string> AggregatedBlockers,
        bool HasUnreadableEvidence);
}

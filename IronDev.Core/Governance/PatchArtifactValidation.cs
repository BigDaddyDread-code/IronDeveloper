namespace IronDev.Core.Governance;

public sealed record PatchArtifactValidationIssue(string Code, string Field, string Message);

public sealed record PatchArtifactValidationResult(IReadOnlyList<PatchArtifactValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class PatchArtifactValidation
{
    private static readonly string[] AllowedChangeKinds = ["Create", "Modify", "Delete", "Rename"];

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "source applied",
        "applies source",
        "rollback executed",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready"
    ];

    public static PatchArtifactValidationResult Validate(PatchArtifact? artifact)
    {
        var issues = new List<PatchArtifactValidationIssue>();

        if (artifact is null)
        {
            Add(issues, "PATCH_ARTIFACT_REQUIRED", "artifact", "Patch artifact is required.");
            return new PatchArtifactValidationResult(issues);
        }

        if (artifact.PatchArtifactId == Guid.Empty)
        {
            Add(issues, "PATCH_ARTIFACT_ID_REQUIRED", nameof(artifact.PatchArtifactId), "Patch artifact ID is required.");
        }

        if (artifact.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(artifact.ProjectId), "Project ID is required.");
        }

        if (artifact.ControlledDryRunRequestId == Guid.Empty)
        {
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", nameof(artifact.ControlledDryRunRequestId), "Controlled dry-run request ID is required.");
        }

        if (artifact.DryRunExecutionAuditId == Guid.Empty)
        {
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", nameof(artifact.DryRunExecutionAuditId), "Dry-run execution audit ID is required.");
        }

        if (artifact.PolicySatisfactionId == Guid.Empty)
        {
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(artifact.PolicySatisfactionId), "Policy satisfaction ID is required.");
        }

        ValidateText(issues, artifact.PatchArtifactKind, nameof(artifact.PatchArtifactKind), "PATCH_ARTIFACT_KIND_REQUIRED", "Patch artifact kind is required.");
        ValidateText(issues, artifact.DryRunAuditHash, nameof(artifact.DryRunAuditHash), "DRY_RUN_AUDIT_HASH_REQUIRED", "Dry-run audit hash is required.");
        ValidateText(issues, artifact.DryRunReceiptHash, nameof(artifact.DryRunReceiptHash), "DRY_RUN_RECEIPT_HASH_REQUIRED", "Dry-run receipt hash is required.");
        ValidateText(issues, artifact.PolicySatisfactionHash, nameof(artifact.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        ValidateText(issues, artifact.SubjectKind, nameof(artifact.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, artifact.SubjectId, nameof(artifact.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, artifact.SubjectHash, nameof(artifact.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, artifact.SourceSnapshotReference, nameof(artifact.SourceSnapshotReference), "SOURCE_SNAPSHOT_REFERENCE_REQUIRED", "Source snapshot reference is required.");
        ValidateText(issues, artifact.SourceBaselineHash, nameof(artifact.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        ValidateText(issues, artifact.WorkspaceBoundaryHash, nameof(artifact.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        ValidateText(issues, artifact.ValidationPlanId, nameof(artifact.ValidationPlanId), "VALIDATION_PLAN_ID_REQUIRED", "Validation plan ID is required.");
        ValidateText(issues, artifact.ValidationPlanHash, nameof(artifact.ValidationPlanHash), "VALIDATION_PLAN_HASH_REQUIRED", "Validation plan hash is required.");
        ValidateText(issues, artifact.PatchHash, nameof(artifact.PatchHash), "PATCH_HASH_REQUIRED", "Patch hash is required.");
        ValidateText(issues, artifact.ChangeSetHash, nameof(artifact.ChangeSetHash), "CHANGE_SET_HASH_REQUIRED", "Change set hash is required.");
        ValidateText(issues, artifact.Boundary, nameof(artifact.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");

        if (artifact.CreatedAtUtc == default)
        {
            Add(issues, "CREATED_AT_UTC_REQUIRED", nameof(artifact.CreatedAtUtc), "Created timestamp is required.");
        }

        if (artifact.ExpiresAtUtc.HasValue && artifact.ExpiresAtUtc.Value <= artifact.CreatedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(artifact.ExpiresAtUtc), "Expiry timestamp must be after created timestamp.");
        }

        ValidateRequiredList(issues, artifact.EvidenceReferences, nameof(artifact.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, artifact.BoundaryMaxims, nameof(artifact.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        if (artifact.FileChanges is null || artifact.FileChanges.Count == 0)
        {
            Add(issues, "FILE_CHANGES_REQUIRED", nameof(artifact.FileChanges), "At least one file change is required.");
        }
        else
        {
            for (var index = 0; index < artifact.FileChanges.Count; index++)
            {
                ValidateFileChange(issues, artifact.FileChanges[index], $"{nameof(artifact.FileChanges)}[{index}]");
            }
        }

        ValidateSafeText(issues, artifact.PatchArtifactKind, nameof(artifact.PatchArtifactKind));
        ValidateSafeText(issues, artifact.DryRunAuditHash, nameof(artifact.DryRunAuditHash));
        ValidateSafeText(issues, artifact.DryRunReceiptHash, nameof(artifact.DryRunReceiptHash));
        ValidateSafeText(issues, artifact.PolicySatisfactionHash, nameof(artifact.PolicySatisfactionHash));
        ValidateSafeText(issues, artifact.SubjectKind, nameof(artifact.SubjectKind));
        ValidateSafeText(issues, artifact.SubjectId, nameof(artifact.SubjectId));
        ValidateSafeText(issues, artifact.SubjectHash, nameof(artifact.SubjectHash));
        ValidateSafeText(issues, artifact.SourceSnapshotReference, nameof(artifact.SourceSnapshotReference));
        ValidateSafeText(issues, artifact.SourceBaselineHash, nameof(artifact.SourceBaselineHash));
        ValidateSafeText(issues, artifact.WorkspaceBoundaryHash, nameof(artifact.WorkspaceBoundaryHash));
        ValidateSafeText(issues, artifact.ValidationPlanId, nameof(artifact.ValidationPlanId));
        ValidateSafeText(issues, artifact.ValidationPlanHash, nameof(artifact.ValidationPlanHash));
        ValidateSafeText(issues, artifact.PatchHash, nameof(artifact.PatchHash));
        ValidateSafeText(issues, artifact.ChangeSetHash, nameof(artifact.ChangeSetHash));
        ValidateSafeText(issues, artifact.Boundary, nameof(artifact.Boundary));
        ValidateSafeList(issues, artifact.EvidenceReferences, nameof(artifact.EvidenceReferences));
        ValidateSafeList(issues, artifact.BoundaryMaxims, nameof(artifact.BoundaryMaxims));

        return new PatchArtifactValidationResult(issues);
    }

    private static void ValidateFileChange(List<PatchArtifactValidationIssue> issues, PatchArtifactFileChange change, string fieldPrefix)
    {
        ValidateText(issues, change.Path, $"{fieldPrefix}.{nameof(change.Path)}", "FILE_CHANGE_PATH_REQUIRED", "File change path is required.");
        ValidateText(issues, change.ChangeKind, $"{fieldPrefix}.{nameof(change.ChangeKind)}", "FILE_CHANGE_KIND_REQUIRED", "File change kind is required.");
        ValidateText(issues, change.DiffHash, $"{fieldPrefix}.{nameof(change.DiffHash)}", "FILE_CHANGE_DIFF_HASH_REQUIRED", "File change diff hash is required.");
        ValidateText(issues, change.NormalizedDiff, $"{fieldPrefix}.{nameof(change.NormalizedDiff)}", "FILE_CHANGE_NORMALIZED_DIFF_REQUIRED", "File change normalized diff is required.");

        ValidatePath(issues, change.Path, $"{fieldPrefix}.{nameof(change.Path)}");

        if (!string.IsNullOrWhiteSpace(change.PreviousPath))
        {
            ValidatePath(issues, change.PreviousPath, $"{fieldPrefix}.{nameof(change.PreviousPath)}");
        }

        if (!AllowedChangeKinds.Contains(change.ChangeKind, StringComparer.Ordinal))
        {
            Add(issues, "FILE_CHANGE_KIND_INVALID", $"{fieldPrefix}.{nameof(change.ChangeKind)}", "File change kind must be Create, Modify, Delete, or Rename.");
        }
        else
        {
            ValidateChangeKindHashes(issues, change, fieldPrefix);
        }

        ValidateSafeText(issues, change.Path, $"{fieldPrefix}.{nameof(change.Path)}");
        ValidateSafeText(issues, change.PreviousPath, $"{fieldPrefix}.{nameof(change.PreviousPath)}");
        ValidateSafeText(issues, change.ChangeKind, $"{fieldPrefix}.{nameof(change.ChangeKind)}");
        ValidateSafeText(issues, change.BeforeContentHash, $"{fieldPrefix}.{nameof(change.BeforeContentHash)}");
        ValidateSafeText(issues, change.AfterContentHash, $"{fieldPrefix}.{nameof(change.AfterContentHash)}");
        ValidateSafeText(issues, change.DiffHash, $"{fieldPrefix}.{nameof(change.DiffHash)}");
        ValidateSafeText(issues, change.NormalizedDiff, $"{fieldPrefix}.{nameof(change.NormalizedDiff)}");
    }

    private static void ValidateChangeKindHashes(List<PatchArtifactValidationIssue> issues, PatchArtifactFileChange change, string fieldPrefix)
    {
        switch (change.ChangeKind)
        {
            case "Create":
                RequireBlank(issues, change.BeforeContentHash, $"{fieldPrefix}.{nameof(change.BeforeContentHash)}", "CREATE_BEFORE_CONTENT_HASH_FORBIDDEN", "Create change must not include before content hash.");
                RequireText(issues, change.AfterContentHash, $"{fieldPrefix}.{nameof(change.AfterContentHash)}", "CREATE_AFTER_CONTENT_HASH_REQUIRED", "Create change requires after content hash.");
                break;
            case "Modify":
                RequireText(issues, change.BeforeContentHash, $"{fieldPrefix}.{nameof(change.BeforeContentHash)}", "MODIFY_BEFORE_CONTENT_HASH_REQUIRED", "Modify change requires before content hash.");
                RequireText(issues, change.AfterContentHash, $"{fieldPrefix}.{nameof(change.AfterContentHash)}", "MODIFY_AFTER_CONTENT_HASH_REQUIRED", "Modify change requires after content hash.");
                break;
            case "Delete":
                RequireText(issues, change.BeforeContentHash, $"{fieldPrefix}.{nameof(change.BeforeContentHash)}", "DELETE_BEFORE_CONTENT_HASH_REQUIRED", "Delete change requires before content hash.");
                RequireBlank(issues, change.AfterContentHash, $"{fieldPrefix}.{nameof(change.AfterContentHash)}", "DELETE_AFTER_CONTENT_HASH_FORBIDDEN", "Delete change must not include after content hash.");
                break;
            case "Rename":
                RequireText(issues, change.PreviousPath, $"{fieldPrefix}.{nameof(change.PreviousPath)}", "RENAME_PREVIOUS_PATH_REQUIRED", "Rename change requires previous path.");
                RequireText(issues, change.BeforeContentHash, $"{fieldPrefix}.{nameof(change.BeforeContentHash)}", "RENAME_BEFORE_CONTENT_HASH_REQUIRED", "Rename change requires before content hash.");
                RequireText(issues, change.AfterContentHash, $"{fieldPrefix}.{nameof(change.AfterContentHash)}", "RENAME_AFTER_CONTENT_HASH_REQUIRED", "Rename change requires after content hash.");
                break;
        }
    }

    private static void ValidatePath(List<PatchArtifactValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var path = value.Trim();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var unsafePath =
            path == "/" ||
            path.EndsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Contains('\\', StringComparison.Ordinal) ||
            (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') ||
            segments.Any(segment => segment == ".." || segment == ".git");

        if (unsafePath)
        {
            Add(issues, "FILE_CHANGE_PATH_UNSAFE", field, "Patch artifact file paths must be safe relative repository paths.");
        }
    }

    private static void ValidateText(
        List<PatchArtifactValidationIssue> issues,
        string? value,
        string field,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(
        List<PatchArtifactValidationIssue> issues,
        string? value,
        string field,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireBlank(
        List<PatchArtifactValidationIssue> issues,
        string? value,
        string field,
        string code,
        string message)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateRequiredList(
        List<PatchArtifactValidationIssue> issues,
        IReadOnlyList<string>? values,
        string field,
        string code,
        string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<PatchArtifactValidationIssue> issues, IReadOnlyList<string>? values, string field)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ValidateSafeText(issues, value, field);
        }
    }

    private static void ValidateSafeText(List<PatchArtifactValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Patch artifact text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Patch artifact text must not claim authority: {marker}.");
            }
        }
    }

    private static void Add(List<PatchArtifactValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new PatchArtifactValidationIssue(code, field, message));
}

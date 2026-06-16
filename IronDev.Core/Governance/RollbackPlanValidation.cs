namespace IronDev.Core.Governance;

public sealed record RollbackPlanValidationIssue(string Code, string Field, string Message);

public sealed record RollbackPlanValidationResult(IReadOnlyList<RollbackPlanValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class RollbackPlanValidation
{
    private static readonly string[] AllowedActionKinds =
    [
        "RestoreModifiedFile",
        "DeleteCreatedFile",
        "RecreateDeletedFile",
        "RenameBack",
        "Noop"
    ];

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
        "rollback succeeded",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready"
    ];

    public static RollbackPlanValidationResult Validate(RollbackPlan? plan)
    {
        var issues = new List<RollbackPlanValidationIssue>();

        if (plan is null)
        {
            Add(issues, "ROLLBACK_PLAN_REQUIRED", "plan", "Rollback plan is required.");
            return new RollbackPlanValidationResult(issues);
        }

        if (plan.RollbackPlanId == Guid.Empty)
        {
            Add(issues, "ROLLBACK_PLAN_ID_REQUIRED", nameof(plan.RollbackPlanId), "Rollback plan ID is required.");
        }

        if (plan.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(plan.ProjectId), "Project ID is required.");
        }

        if (plan.PatchArtifactId == Guid.Empty)
        {
            Add(issues, "PATCH_ARTIFACT_ID_REQUIRED", nameof(plan.PatchArtifactId), "Patch artifact ID is required.");
        }

        if (plan.ControlledDryRunRequestId == Guid.Empty)
        {
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", nameof(plan.ControlledDryRunRequestId), "Controlled dry-run request ID is required.");
        }

        if (plan.DryRunExecutionAuditId == Guid.Empty)
        {
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", nameof(plan.DryRunExecutionAuditId), "Dry-run execution audit ID is required.");
        }

        if (plan.PolicySatisfactionId == Guid.Empty)
        {
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(plan.PolicySatisfactionId), "Policy satisfaction ID is required.");
        }

        ValidateText(issues, plan.RollbackPlanKind, nameof(plan.RollbackPlanKind), "ROLLBACK_PLAN_KIND_REQUIRED", "Rollback plan kind is required.");
        ValidateText(issues, plan.PatchHash, nameof(plan.PatchHash), "PATCH_HASH_REQUIRED", "Patch hash is required.");
        ValidateText(issues, plan.ChangeSetHash, nameof(plan.ChangeSetHash), "CHANGE_SET_HASH_REQUIRED", "Change-set hash is required.");
        ValidateText(issues, plan.DryRunAuditHash, nameof(plan.DryRunAuditHash), "DRY_RUN_AUDIT_HASH_REQUIRED", "Dry-run audit hash is required.");
        ValidateText(issues, plan.DryRunReceiptHash, nameof(plan.DryRunReceiptHash), "DRY_RUN_RECEIPT_HASH_REQUIRED", "Dry-run receipt hash is required.");
        ValidateText(issues, plan.PolicySatisfactionHash, nameof(plan.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        ValidateText(issues, plan.SubjectKind, nameof(plan.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, plan.SubjectId, nameof(plan.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, plan.SubjectHash, nameof(plan.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, plan.SourceSnapshotReference, nameof(plan.SourceSnapshotReference), "SOURCE_SNAPSHOT_REFERENCE_REQUIRED", "Source snapshot reference is required.");
        ValidateText(issues, plan.SourceBaselineHash, nameof(plan.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        ValidateText(issues, plan.WorkspaceBoundaryHash, nameof(plan.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        ValidateText(issues, plan.ExpectedBranch, nameof(plan.ExpectedBranch), "EXPECTED_BRANCH_REQUIRED", "Expected branch is required.");
        ValidateText(issues, plan.ExpectedCleanWorktreeHash, nameof(plan.ExpectedCleanWorktreeHash), "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED", "Expected clean worktree hash is required.");
        ValidateText(issues, plan.RollbackPlanHash, nameof(plan.RollbackPlanHash), "ROLLBACK_PLAN_HASH_REQUIRED", "Rollback plan hash is required.");
        ValidateText(issues, plan.Boundary, nameof(plan.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");

        if (plan.CreatedAtUtc == default)
        {
            Add(issues, "CREATED_AT_UTC_REQUIRED", nameof(plan.CreatedAtUtc), "Created timestamp is required.");
        }

        if (plan.ExpiresAtUtc.HasValue && plan.ExpiresAtUtc.Value <= plan.CreatedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(plan.ExpiresAtUtc), "Expiry timestamp must be after created timestamp.");
        }

        ValidateRequiredList(issues, plan.EvidenceReferences, nameof(plan.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, plan.BoundaryMaxims, nameof(plan.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        if (plan.FileActions is null || plan.FileActions.Count == 0)
        {
            Add(issues, "FILE_ACTIONS_REQUIRED", nameof(plan.FileActions), "At least one rollback file action is required.");
        }
        else
        {
            for (var index = 0; index < plan.FileActions.Count; index++)
            {
                ValidateFileAction(issues, plan.FileActions[index], $"{nameof(plan.FileActions)}[{index}]");
            }
        }

        ValidateSafeText(issues, plan.ExpectedBranch, nameof(plan.ExpectedBranch));
        ValidateSafeText(issues, plan.SourceSnapshotReference, nameof(plan.SourceSnapshotReference));
        ValidateSafeList(issues, plan.EvidenceReferences, nameof(plan.EvidenceReferences));
        ValidateSafeList(issues, plan.BoundaryMaxims, nameof(plan.BoundaryMaxims));
        ValidateSafeText(issues, plan.Boundary, nameof(plan.Boundary));

        return new RollbackPlanValidationResult(issues);
    }

    private static void ValidateFileAction(List<RollbackPlanValidationIssue> issues, RollbackPlanFileAction action, string fieldPrefix)
    {
        ValidateText(issues, action.Path, $"{fieldPrefix}.{nameof(action.Path)}", "ROLLBACK_FILE_ACTION_PATH_REQUIRED", "Rollback file action path is required.");
        ValidateText(issues, action.PlannedActionKind, $"{fieldPrefix}.{nameof(action.PlannedActionKind)}", "ROLLBACK_FILE_ACTION_KIND_REQUIRED", "Rollback file action kind is required.");
        ValidateText(issues, action.ExpectedCurrentContentHash, $"{fieldPrefix}.{nameof(action.ExpectedCurrentContentHash)}", "ROLLBACK_FILE_ACTION_EXPECTED_CURRENT_CONTENT_HASH_REQUIRED", "Rollback file action expected current content hash is required.");
        ValidateText(issues, action.RollbackActionHash, $"{fieldPrefix}.{nameof(action.RollbackActionHash)}", "ROLLBACK_FILE_ACTION_HASH_REQUIRED", "Rollback file action hash is required.");

        ValidatePath(issues, action.Path, $"{fieldPrefix}.{nameof(action.Path)}");
        if (!string.IsNullOrWhiteSpace(action.PreviousPath))
        {
            ValidatePath(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}");
        }

        if (!AllowedActionKinds.Contains(action.PlannedActionKind, StringComparer.Ordinal))
        {
            Add(issues, "ROLLBACK_FILE_ACTION_KIND_INVALID", $"{fieldPrefix}.{nameof(action.PlannedActionKind)}", "Rollback file action kind is invalid.");
        }
        else
        {
            ValidateActionKindHashes(issues, action, fieldPrefix);
        }

        ValidateSafeText(issues, action.Path, $"{fieldPrefix}.{nameof(action.Path)}");
        ValidateSafeText(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}");
    }

    private static void ValidateActionKindHashes(List<RollbackPlanValidationIssue> issues, RollbackPlanFileAction action, string fieldPrefix)
    {
        switch (action.PlannedActionKind)
        {
            case "RestoreModifiedFile":
                RequireText(issues, action.RestoreContentHash, $"{fieldPrefix}.{nameof(action.RestoreContentHash)}", "RESTORE_CONTENT_HASH_REQUIRED", "RestoreModifiedFile requires restore content hash.");
                RequireBlank(issues, action.DeleteContentHash, $"{fieldPrefix}.{nameof(action.DeleteContentHash)}", "DELETE_CONTENT_HASH_FORBIDDEN", "RestoreModifiedFile must not include delete content hash.");
                RequireBlank(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}", "PREVIOUS_PATH_FORBIDDEN", "RestoreModifiedFile must not include previous path.");
                break;
            case "DeleteCreatedFile":
                RequireText(issues, action.DeleteContentHash, $"{fieldPrefix}.{nameof(action.DeleteContentHash)}", "DELETE_CONTENT_HASH_REQUIRED", "DeleteCreatedFile requires delete content hash.");
                RequireBlank(issues, action.RestoreContentHash, $"{fieldPrefix}.{nameof(action.RestoreContentHash)}", "RESTORE_CONTENT_HASH_FORBIDDEN", "DeleteCreatedFile must not include restore content hash.");
                RequireBlank(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}", "PREVIOUS_PATH_FORBIDDEN", "DeleteCreatedFile must not include previous path.");
                break;
            case "RecreateDeletedFile":
                RequireText(issues, action.RestoreContentHash, $"{fieldPrefix}.{nameof(action.RestoreContentHash)}", "RESTORE_CONTENT_HASH_REQUIRED", "RecreateDeletedFile requires restore content hash.");
                RequireBlank(issues, action.DeleteContentHash, $"{fieldPrefix}.{nameof(action.DeleteContentHash)}", "DELETE_CONTENT_HASH_FORBIDDEN", "RecreateDeletedFile must not include delete content hash.");
                RequireBlank(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}", "PREVIOUS_PATH_FORBIDDEN", "RecreateDeletedFile must not include previous path.");
                break;
            case "RenameBack":
                RequireText(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}", "PREVIOUS_PATH_REQUIRED", "RenameBack requires previous path.");
                RequireText(issues, action.RestoreContentHash, $"{fieldPrefix}.{nameof(action.RestoreContentHash)}", "RESTORE_CONTENT_HASH_REQUIRED", "RenameBack requires restore content hash.");
                RequireBlank(issues, action.DeleteContentHash, $"{fieldPrefix}.{nameof(action.DeleteContentHash)}", "DELETE_CONTENT_HASH_FORBIDDEN", "RenameBack must not include delete content hash.");
                break;
            case "Noop":
                RequireBlank(issues, action.RestoreContentHash, $"{fieldPrefix}.{nameof(action.RestoreContentHash)}", "RESTORE_CONTENT_HASH_FORBIDDEN", "Noop must not include restore content hash.");
                RequireBlank(issues, action.DeleteContentHash, $"{fieldPrefix}.{nameof(action.DeleteContentHash)}", "DELETE_CONTENT_HASH_FORBIDDEN", "Noop must not include delete content hash.");
                RequireBlank(issues, action.PreviousPath, $"{fieldPrefix}.{nameof(action.PreviousPath)}", "PREVIOUS_PATH_FORBIDDEN", "Noop must not include previous path.");
                break;
        }
    }

    private static void ValidatePath(List<RollbackPlanValidationIssue> issues, string? value, string field)
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
            Add(issues, "ROLLBACK_FILE_ACTION_PATH_UNSAFE", field, "Rollback file action paths must be safe relative repository paths.");
        }
    }

    private static void ValidateText(List<RollbackPlanValidationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(List<RollbackPlanValidationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireBlank(List<RollbackPlanValidationIssue> issues, string? value, string field, string code, string message)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateRequiredList(List<RollbackPlanValidationIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<RollbackPlanValidationIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<RollbackPlanValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Rollback plan text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Rollback plan text must not claim authority: {marker}.");
            }
        }
    }

    private static void Add(List<RollbackPlanValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new RollbackPlanValidationIssue(code, field, message));
}

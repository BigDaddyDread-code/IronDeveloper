namespace IronDev.Core.Governance;

public sealed record RollbackSupportReceiptValidationIssue(string Code, string Field, string Message);

public sealed record RollbackSupportReceiptValidationResult(IReadOnlyList<RollbackSupportReceiptValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class RollbackSupportReceiptValidation
{
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

    public static RollbackSupportReceiptValidationResult Validate(RollbackSupportReceipt? receipt)
    {
        var issues = new List<RollbackSupportReceiptValidationIssue>();

        if (receipt is null)
        {
            Add(issues, "ROLLBACK_SUPPORT_RECEIPT_REQUIRED", "receipt", "Rollback support receipt is required.");
            return new RollbackSupportReceiptValidationResult(issues);
        }

        if (receipt.RollbackSupportReceiptId == Guid.Empty)
            Add(issues, "ROLLBACK_SUPPORT_RECEIPT_ID_REQUIRED", nameof(receipt.RollbackSupportReceiptId), "Rollback support receipt ID is required.");
        if (receipt.ProjectId == Guid.Empty)
            Add(issues, "PROJECT_ID_REQUIRED", nameof(receipt.ProjectId), "Project ID is required.");
        if (receipt.RollbackPlanId == Guid.Empty)
            Add(issues, "ROLLBACK_PLAN_ID_REQUIRED", nameof(receipt.RollbackPlanId), "Rollback plan ID is required.");
        if (receipt.PatchArtifactId == Guid.Empty)
            Add(issues, "PATCH_ARTIFACT_ID_REQUIRED", nameof(receipt.PatchArtifactId), "Patch artifact ID is required.");
        if (receipt.ControlledDryRunRequestId == Guid.Empty)
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", nameof(receipt.ControlledDryRunRequestId), "Controlled dry-run request ID is required.");
        if (receipt.DryRunExecutionAuditId == Guid.Empty)
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", nameof(receipt.DryRunExecutionAuditId), "Dry-run execution audit ID is required.");
        if (receipt.PolicySatisfactionId == Guid.Empty)
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(receipt.PolicySatisfactionId), "Policy satisfaction ID is required.");
        if (!receipt.RollbackGateSatisfied)
            Add(issues, "ROLLBACK_GATE_NOT_SATISFIED", nameof(receipt.RollbackGateSatisfied), "Only satisfied rollback gate evaluations can be stored as rollback support receipts.");

        ValidateText(issues, receipt.RollbackPlanHash, nameof(receipt.RollbackPlanHash), "ROLLBACK_PLAN_HASH_REQUIRED", "Rollback plan hash is required.");
        ValidateText(issues, receipt.RollbackGateEvaluationHash, nameof(receipt.RollbackGateEvaluationHash), "ROLLBACK_GATE_EVALUATION_HASH_REQUIRED", "Rollback gate evaluation hash is required.");
        ValidateText(issues, receipt.PatchHash, nameof(receipt.PatchHash), "PATCH_HASH_REQUIRED", "Patch hash is required.");
        ValidateText(issues, receipt.ChangeSetHash, nameof(receipt.ChangeSetHash), "CHANGE_SET_HASH_REQUIRED", "Change-set hash is required.");
        ValidateText(issues, receipt.DryRunAuditHash, nameof(receipt.DryRunAuditHash), "DRY_RUN_AUDIT_HASH_REQUIRED", "Dry-run audit hash is required.");
        ValidateText(issues, receipt.DryRunReceiptHash, nameof(receipt.DryRunReceiptHash), "DRY_RUN_RECEIPT_HASH_REQUIRED", "Dry-run receipt hash is required.");
        ValidateText(issues, receipt.PolicySatisfactionHash, nameof(receipt.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        ValidateText(issues, receipt.SubjectKind, nameof(receipt.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, receipt.SubjectId, nameof(receipt.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, receipt.SubjectHash, nameof(receipt.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, receipt.SourceSnapshotReference, nameof(receipt.SourceSnapshotReference), "SOURCE_SNAPSHOT_REFERENCE_REQUIRED", "Source snapshot reference is required.");
        ValidateText(issues, receipt.SourceBaselineHash, nameof(receipt.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        ValidateText(issues, receipt.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        ValidateText(issues, receipt.ExpectedBranch, nameof(receipt.ExpectedBranch), "EXPECTED_BRANCH_REQUIRED", "Expected branch is required.");
        ValidateText(issues, receipt.ExpectedCleanWorktreeHash, nameof(receipt.ExpectedCleanWorktreeHash), "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED", "Expected clean worktree hash is required.");
        ValidateText(issues, receipt.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash), "ROLLBACK_SUPPORT_RECEIPT_HASH_REQUIRED", "Rollback support receipt hash is required.");
        ValidateText(issues, receipt.Boundary, nameof(receipt.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");

        if (receipt.CreatedAtUtc == default)
            Add(issues, "CREATED_AT_UTC_REQUIRED", nameof(receipt.CreatedAtUtc), "Created timestamp is required.");
        if (receipt.ExpiresAtUtc.HasValue && receipt.ExpiresAtUtc.Value <= receipt.CreatedAtUtc)
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(receipt.ExpiresAtUtc), "Expiry timestamp must be after created timestamp.");

        ValidateRequiredList(issues, receipt.EvidenceReferences, nameof(receipt.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, receipt.BoundaryMaxims, nameof(receipt.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        ValidateSafeText(issues, receipt.RollbackPlanHash, nameof(receipt.RollbackPlanHash));
        ValidateSafeText(issues, receipt.RollbackGateEvaluationHash, nameof(receipt.RollbackGateEvaluationHash));
        ValidateSafeText(issues, receipt.PatchHash, nameof(receipt.PatchHash));
        ValidateSafeText(issues, receipt.ChangeSetHash, nameof(receipt.ChangeSetHash));
        ValidateSafeText(issues, receipt.DryRunAuditHash, nameof(receipt.DryRunAuditHash));
        ValidateSafeText(issues, receipt.DryRunReceiptHash, nameof(receipt.DryRunReceiptHash));
        ValidateSafeText(issues, receipt.PolicySatisfactionHash, nameof(receipt.PolicySatisfactionHash));
        ValidateSafeText(issues, receipt.SubjectKind, nameof(receipt.SubjectKind));
        ValidateSafeText(issues, receipt.SubjectId, nameof(receipt.SubjectId));
        ValidateSafeText(issues, receipt.SubjectHash, nameof(receipt.SubjectHash));
        ValidateSafeText(issues, receipt.SourceSnapshotReference, nameof(receipt.SourceSnapshotReference));
        ValidateSafeText(issues, receipt.SourceBaselineHash, nameof(receipt.SourceBaselineHash));
        ValidateSafeText(issues, receipt.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash));
        ValidateSafeText(issues, receipt.ExpectedBranch, nameof(receipt.ExpectedBranch));
        ValidateSafeText(issues, receipt.ExpectedCleanWorktreeHash, nameof(receipt.ExpectedCleanWorktreeHash));
        ValidateSafeText(issues, receipt.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash));
        ValidateSafeText(issues, receipt.Boundary, nameof(receipt.Boundary));
        ValidateSafeList(issues, receipt.EvidenceReferences, nameof(receipt.EvidenceReferences));
        ValidateSafeList(issues, receipt.BoundaryMaxims, nameof(receipt.BoundaryMaxims));

        return new RollbackSupportReceiptValidationResult(issues);
    }

    private static void ValidateText(List<RollbackSupportReceiptValidationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(issues, code, field, message);
    }

    private static void ValidateRequiredList(List<RollbackSupportReceiptValidationIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
            Add(issues, code, field, message);
    }

    private static void ValidateSafeList(List<RollbackSupportReceiptValidationIssue> issues, IReadOnlyList<string>? values, string field)
    {
        if (values is null)
            return;

        foreach (var value in values)
            ValidateSafeText(issues, value, field);
    }

    private static void ValidateSafeText(List<RollbackSupportReceiptValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in PrivateMaterialMarkers)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Rollback support receipt text must not contain private or raw material: {marker}.");

        foreach (var marker in AuthorityClaimMarkers)
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Rollback support receipt text must not claim authority: {marker}.");
    }

    private static void Add(List<RollbackSupportReceiptValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new RollbackSupportReceiptValidationIssue(code, field, message));
}

namespace IronDev.Core.Governance;

public static class PatchBaseHashValidation
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
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready"
    ];

    public static PatchBaseHashValidationResult Validate(PatchBaseHashValidationContext? context)
    {
        var issues = new List<PatchBaseHashValidationIssue>();

        if (context is null)
        {
            Add(issues, "CONTEXT_REQUIRED", "context", "Patch base/hash validation context is required.");
            return new PatchBaseHashValidationResult(issues, null, null);
        }

        var artifactValidation = PatchArtifactValidation.Validate(context.PatchArtifact);
        if (!artifactValidation.IsValid)
        {
            Add(issues, "PATCH_ARTIFACT_INVALID", nameof(context.PatchArtifact), "Patch artifact must satisfy the patch artifact contract before base/hash validation.");
            foreach (var issue in artifactValidation.Issues)
            {
                Add(issues, issue.Code, $"{nameof(context.PatchArtifact)}.{issue.Field}", issue.Message);
            }
        }

        ValidateContextShape(issues, context);
        ValidateContextSafety(issues, context);

        if (context.PatchArtifact is null)
        {
            return new PatchBaseHashValidationResult(issues, null, null);
        }

        ValidateBinding(issues, context);

        var computedChangeSetHash = context.PatchArtifact.FileChanges is null
            ? null
            : PatchArtifactHashing.ComputeChangeSetHash(context.PatchArtifact.FileChanges);
        var computedPatchHash = computedChangeSetHash is null
            ? null
            : PatchArtifactHashing.ComputePatchHash(context.PatchArtifact, computedChangeSetHash);

        if (!string.Equals(Normalize(context.PatchArtifact.ChangeSetHash), Normalize(computedChangeSetHash), StringComparison.Ordinal))
        {
            Add(issues, "CHANGE_SET_HASH_MISMATCH", nameof(context.PatchArtifact.ChangeSetHash), "Patch artifact change-set hash does not match the computed change-set hash.");
        }

        if (!string.Equals(Normalize(context.PatchArtifact.PatchHash), Normalize(computedPatchHash), StringComparison.Ordinal))
        {
            Add(issues, "PATCH_HASH_MISMATCH", nameof(context.PatchArtifact.PatchHash), "Patch artifact hash does not match the computed patch hash.");
        }

        return new PatchBaseHashValidationResult(issues, computedChangeSetHash, computedPatchHash);
    }

    private static void ValidateContextShape(List<PatchBaseHashValidationIssue> issues, PatchBaseHashValidationContext context)
    {
        if (context.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(context.ProjectId), "Project ID is required.");
        }

        if (context.ControlledDryRunRequestId == Guid.Empty)
        {
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", nameof(context.ControlledDryRunRequestId), "Controlled dry-run request ID is required.");
        }

        if (context.DryRunExecutionAuditId == Guid.Empty)
        {
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", nameof(context.DryRunExecutionAuditId), "Dry-run execution audit ID is required.");
        }

        if (context.PolicySatisfactionId == Guid.Empty)
        {
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(context.PolicySatisfactionId), "Policy satisfaction ID is required.");
        }

        RequireText(issues, context.DryRunAuditHash, nameof(context.DryRunAuditHash), "DRY_RUN_AUDIT_HASH_REQUIRED", "Dry-run audit hash is required.");
        RequireText(issues, context.DryRunReceiptHash, nameof(context.DryRunReceiptHash), "DRY_RUN_RECEIPT_HASH_REQUIRED", "Dry-run receipt hash is required.");
        RequireText(issues, context.PolicySatisfactionHash, nameof(context.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        RequireText(issues, context.SubjectKind, nameof(context.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        RequireText(issues, context.SubjectId, nameof(context.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        RequireText(issues, context.SubjectHash, nameof(context.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        RequireText(issues, context.SourceSnapshotReference, nameof(context.SourceSnapshotReference), "SOURCE_SNAPSHOT_REFERENCE_REQUIRED", "Source snapshot reference is required.");
        RequireText(issues, context.SourceBaselineHash, nameof(context.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        RequireText(issues, context.WorkspaceBoundaryHash, nameof(context.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        RequireText(issues, context.ValidationPlanId, nameof(context.ValidationPlanId), "VALIDATION_PLAN_ID_REQUIRED", "Validation plan ID is required.");
        RequireText(issues, context.ValidationPlanHash, nameof(context.ValidationPlanHash), "VALIDATION_PLAN_HASH_REQUIRED", "Validation plan hash is required.");

        if (context.EvidenceReferences is null || context.EvidenceReferences.Count == 0 || context.EvidenceReferences.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "EVIDENCE_REFERENCES_REQUIRED", nameof(context.EvidenceReferences), "At least one evidence reference is required.");
        }

        if (context.BoundaryMaxims is null || context.BoundaryMaxims.Count == 0 || context.BoundaryMaxims.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "BOUNDARY_MAXIMS_REQUIRED", nameof(context.BoundaryMaxims), "At least one boundary maxim is required.");
        }
    }

    private static void ValidateContextSafety(List<PatchBaseHashValidationIssue> issues, PatchBaseHashValidationContext context)
    {
        ValidateSafeText(issues, context.DryRunAuditHash, nameof(context.DryRunAuditHash));
        ValidateSafeText(issues, context.DryRunReceiptHash, nameof(context.DryRunReceiptHash));
        ValidateSafeText(issues, context.PolicySatisfactionHash, nameof(context.PolicySatisfactionHash));
        ValidateSafeText(issues, context.SubjectKind, nameof(context.SubjectKind));
        ValidateSafeText(issues, context.SubjectId, nameof(context.SubjectId));
        ValidateSafeText(issues, context.SubjectHash, nameof(context.SubjectHash));
        ValidateSafeText(issues, context.SourceSnapshotReference, nameof(context.SourceSnapshotReference));
        ValidateSafeText(issues, context.SourceBaselineHash, nameof(context.SourceBaselineHash));
        ValidateSafeText(issues, context.WorkspaceBoundaryHash, nameof(context.WorkspaceBoundaryHash));
        ValidateSafeText(issues, context.ValidationPlanId, nameof(context.ValidationPlanId));
        ValidateSafeText(issues, context.ValidationPlanHash, nameof(context.ValidationPlanHash));
        ValidateSafeList(issues, context.EvidenceReferences, nameof(context.EvidenceReferences));
        ValidateSafeList(issues, context.BoundaryMaxims, nameof(context.BoundaryMaxims));
    }

    private static void ValidateBinding(List<PatchBaseHashValidationIssue> issues, PatchBaseHashValidationContext context)
    {
        Compare(issues, context.PatchArtifact.ProjectId, context.ProjectId, "PROJECT_ID_MISMATCH", nameof(context.ProjectId), "Project ID does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.ControlledDryRunRequestId, context.ControlledDryRunRequestId, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH", nameof(context.ControlledDryRunRequestId), "Controlled dry-run request ID does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.DryRunExecutionAuditId, context.DryRunExecutionAuditId, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH", nameof(context.DryRunExecutionAuditId), "Dry-run execution audit ID does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.DryRunAuditHash, context.DryRunAuditHash, "DRY_RUN_AUDIT_HASH_MISMATCH", nameof(context.DryRunAuditHash), "Dry-run audit hash does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.DryRunReceiptHash, context.DryRunReceiptHash, "DRY_RUN_RECEIPT_HASH_MISMATCH", nameof(context.DryRunReceiptHash), "Dry-run receipt hash does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.PolicySatisfactionId, context.PolicySatisfactionId, "POLICY_SATISFACTION_ID_MISMATCH", nameof(context.PolicySatisfactionId), "Policy satisfaction ID does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.PolicySatisfactionHash, context.PolicySatisfactionHash, "POLICY_SATISFACTION_HASH_MISMATCH", nameof(context.PolicySatisfactionHash), "Policy satisfaction hash does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.SubjectKind, context.SubjectKind, "SUBJECT_KIND_MISMATCH", nameof(context.SubjectKind), "Subject kind does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.SubjectId, context.SubjectId, "SUBJECT_ID_MISMATCH", nameof(context.SubjectId), "Subject ID does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.SubjectHash, context.SubjectHash, "SUBJECT_HASH_MISMATCH", nameof(context.SubjectHash), "Subject hash does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.SourceSnapshotReference, context.SourceSnapshotReference, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH", nameof(context.SourceSnapshotReference), "Source snapshot reference does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.SourceBaselineHash, context.SourceBaselineHash, "SOURCE_BASELINE_HASH_MISMATCH", nameof(context.SourceBaselineHash), "Source baseline hash does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.WorkspaceBoundaryHash, context.WorkspaceBoundaryHash, "WORKSPACE_BOUNDARY_HASH_MISMATCH", nameof(context.WorkspaceBoundaryHash), "Workspace boundary hash does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.ValidationPlanId, context.ValidationPlanId, "VALIDATION_PLAN_ID_MISMATCH", nameof(context.ValidationPlanId), "Validation plan ID does not match the patch artifact.");
        Compare(issues, context.PatchArtifact.ValidationPlanHash, context.ValidationPlanHash, "VALIDATION_PLAN_HASH_MISMATCH", nameof(context.ValidationPlanHash), "Validation plan hash does not match the patch artifact.");
    }

    private static void Compare(List<PatchBaseHashValidationIssue> issues, Guid actual, Guid expected, string code, string field, string message)
    {
        if (actual != expected)
        {
            Add(issues, code, field, message);
        }
    }

    private static void Compare(List<PatchBaseHashValidationIssue> issues, string actual, string expected, string code, string field, string message)
    {
        if (!string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(
        List<PatchBaseHashValidationIssue> issues,
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

    private static void ValidateSafeList(List<PatchBaseHashValidationIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<PatchBaseHashValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Patch base/hash validation text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Patch base/hash validation text must not claim authority: {marker}.");
            }
        }
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static void Add(List<PatchBaseHashValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new PatchBaseHashValidationIssue(code, field, message));
}

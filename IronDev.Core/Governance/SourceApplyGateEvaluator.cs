namespace IronDev.Core.Governance;

public static class SourceApplyGateEvaluator
{
    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
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
        "source apply succeeded",
        "source mutated",
        "git applied",
        "rollback executed",
        "rollback succeeded",
        "workflow continued",
        "release approved",
        "release ready"
    ];

    public static SourceApplyGateEvaluationResult Evaluate(SourceApplyGateEvaluationRequest? request)
    {
        var issues = new List<SourceApplyGateEvaluationIssue>();

        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Source apply gate evaluation request is required.");
            return BuildResult(null, issues);
        }

        ValidateRequestShape(request, issues);
        ValidateEvidenceShape(request, issues);
        ValidateExpiry(request, issues);
        ValidateSafety(request, issues);
        ValidateBindings(request, issues);

        return BuildResult(request, issues);
    }

    private static void ValidateRequestShape(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        RequireGuid(issues, request.ProjectId, nameof(request.ProjectId), "PROJECT_ID_REQUIRED", "Project ID is required.");
        RequireGuid(issues, request.AcceptedApprovalId, nameof(request.AcceptedApprovalId), "ACCEPTED_APPROVAL_ID_REQUIRED", "Accepted approval ID is required.");
        RequireText(issues, request.AcceptedApprovalHash, nameof(request.AcceptedApprovalHash), "ACCEPTED_APPROVAL_HASH_REQUIRED", "Accepted approval hash is required.");
        RequireGuid(issues, request.PolicySatisfactionId, nameof(request.PolicySatisfactionId), "POLICY_SATISFACTION_ID_REQUIRED", "Policy satisfaction ID is required.");
        RequireText(issues, request.PolicySatisfactionHash, nameof(request.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        RequireGuid(issues, request.ControlledDryRunRequestId, nameof(request.ControlledDryRunRequestId), "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", "Controlled dry-run request ID is required.");
        RequireGuid(issues, request.DryRunExecutionAuditId, nameof(request.DryRunExecutionAuditId), "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", "Dry-run execution audit ID is required.");
        RequireText(issues, request.DryRunAuditHash, nameof(request.DryRunAuditHash), "DRY_RUN_AUDIT_HASH_REQUIRED", "Dry-run audit hash is required.");
        RequireText(issues, request.DryRunReceiptHash, nameof(request.DryRunReceiptHash), "DRY_RUN_RECEIPT_HASH_REQUIRED", "Dry-run receipt hash is required.");
        RequireGuid(issues, request.PatchArtifactId, nameof(request.PatchArtifactId), "PATCH_ARTIFACT_ID_REQUIRED", "Patch artifact ID is required.");
        RequireText(issues, request.PatchHash, nameof(request.PatchHash), "PATCH_HASH_REQUIRED", "Patch hash is required.");
        RequireText(issues, request.ChangeSetHash, nameof(request.ChangeSetHash), "CHANGE_SET_HASH_REQUIRED", "Change-set hash is required.");
        RequireGuid(issues, request.RollbackSupportReceiptId, nameof(request.RollbackSupportReceiptId), "ROLLBACK_SUPPORT_RECEIPT_ID_REQUIRED", "Rollback support receipt ID is required.");
        RequireText(issues, request.RollbackSupportReceiptHash, nameof(request.RollbackSupportReceiptHash), "ROLLBACK_SUPPORT_RECEIPT_HASH_REQUIRED", "Rollback support receipt hash is required.");
        RequireGuid(issues, request.RollbackPlanId, nameof(request.RollbackPlanId), "ROLLBACK_PLAN_ID_REQUIRED", "Rollback plan ID is required.");
        RequireText(issues, request.RollbackPlanHash, nameof(request.RollbackPlanHash), "ROLLBACK_PLAN_HASH_REQUIRED", "Rollback plan hash is required.");
        RequireText(issues, request.RollbackGateEvaluationHash, nameof(request.RollbackGateEvaluationHash), "ROLLBACK_GATE_EVALUATION_HASH_REQUIRED", "Rollback gate evaluation hash is required.");
        RequireText(issues, request.SubjectKind, nameof(request.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        RequireText(issues, request.SubjectId, nameof(request.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        RequireText(issues, request.SubjectHash, nameof(request.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        RequireText(issues, request.SourceSnapshotReference, nameof(request.SourceSnapshotReference), "SOURCE_SNAPSHOT_REFERENCE_REQUIRED", "Source snapshot reference is required.");
        RequireText(issues, request.SourceBaselineHash, nameof(request.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        RequireText(issues, request.WorkspaceBoundaryHash, nameof(request.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        RequireText(issues, request.ExpectedBranch, nameof(request.ExpectedBranch), "EXPECTED_BRANCH_REQUIRED", "Expected branch is required.");
        RequireText(issues, request.ExpectedCleanWorktreeHash, nameof(request.ExpectedCleanWorktreeHash), "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED", "Expected clean worktree hash is required.");
        RequireList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        RequireList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");
        RequireText(issues, request.Boundary, nameof(request.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");
    }

    private static void ValidateEvidenceShape(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        if (request.AcceptedApproval is null)
        {
            Add(issues, "ACCEPTED_APPROVAL_EVIDENCE_REQUIRED", nameof(request.AcceptedApproval), "Accepted approval evidence is required.");
        }

        if (request.PolicySatisfaction is null)
        {
            Add(issues, "POLICY_SATISFACTION_EVIDENCE_REQUIRED", nameof(request.PolicySatisfaction), "Policy satisfaction evidence is required.");
        }

        if (request.ControlledDryRun is null)
        {
            Add(issues, "CONTROLLED_DRY_RUN_EVIDENCE_REQUIRED", nameof(request.ControlledDryRun), "Controlled dry-run evidence is required.");
        }

        if (request.PatchArtifact is null)
        {
            Add(issues, "PATCH_ARTIFACT_EVIDENCE_REQUIRED", nameof(request.PatchArtifact), "Patch artifact evidence is required.");
        }

        if (request.RollbackSupport is null)
        {
            Add(issues, "ROLLBACK_SUPPORT_EVIDENCE_REQUIRED", nameof(request.RollbackSupport), "Rollback support receipt evidence is required.");
        }

        if (request.RollbackSupport is not null && !request.RollbackSupport.RollbackGateSatisfied)
        {
            Add(issues, "ROLLBACK_GATE_NOT_SATISFIED", nameof(request.RollbackSupport.RollbackGateSatisfied), "Rollback support receipt must carry a satisfied rollback gate evaluation.");
        }
    }

    private static void ValidateExpiry(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        var evaluatedAtUtc = request.EvaluatedAtUtc ?? DateTimeOffset.UtcNow;
        ValidateExpiry(issues, request.ExpiresAtUtc, evaluatedAtUtc, nameof(request.ExpiresAtUtc), "SOURCE_APPLY_GATE_EVIDENCE_EXPIRED");
        ValidateExpiry(issues, request.AcceptedApproval?.ExpiresAtUtc, evaluatedAtUtc, nameof(request.AcceptedApproval), "ACCEPTED_APPROVAL_EXPIRED");
        ValidateExpiry(issues, request.PolicySatisfaction?.ExpiresAtUtc, evaluatedAtUtc, nameof(request.PolicySatisfaction), "POLICY_SATISFACTION_EXPIRED");
        ValidateExpiry(issues, request.ControlledDryRun?.ExpiresAtUtc, evaluatedAtUtc, nameof(request.ControlledDryRun), "CONTROLLED_DRY_RUN_EXPIRED");
        ValidateExpiry(issues, request.PatchArtifact?.ExpiresAtUtc, evaluatedAtUtc, nameof(request.PatchArtifact), "PATCH_ARTIFACT_EXPIRED");
        ValidateExpiry(issues, request.RollbackSupport?.ExpiresAtUtc, evaluatedAtUtc, nameof(request.RollbackSupport), "ROLLBACK_SUPPORT_EXPIRED");
    }

    private static void ValidateSafety(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        ValidateSafeText(issues, request.AcceptedApprovalHash, nameof(request.AcceptedApprovalHash));
        ValidateSafeText(issues, request.PolicySatisfactionHash, nameof(request.PolicySatisfactionHash));
        ValidateSafeText(issues, request.DryRunAuditHash, nameof(request.DryRunAuditHash));
        ValidateSafeText(issues, request.DryRunReceiptHash, nameof(request.DryRunReceiptHash));
        ValidateSafeText(issues, request.PatchHash, nameof(request.PatchHash));
        ValidateSafeText(issues, request.ChangeSetHash, nameof(request.ChangeSetHash));
        ValidateSafeText(issues, request.RollbackSupportReceiptHash, nameof(request.RollbackSupportReceiptHash));
        ValidateSafeText(issues, request.RollbackPlanHash, nameof(request.RollbackPlanHash));
        ValidateSafeText(issues, request.RollbackGateEvaluationHash, nameof(request.RollbackGateEvaluationHash));
        ValidateSafeText(issues, request.SubjectKind, nameof(request.SubjectKind));
        ValidateSafeText(issues, request.SubjectId, nameof(request.SubjectId));
        ValidateSafeText(issues, request.SubjectHash, nameof(request.SubjectHash));
        ValidateSafeText(issues, request.SourceSnapshotReference, nameof(request.SourceSnapshotReference));
        ValidateSafeText(issues, request.SourceBaselineHash, nameof(request.SourceBaselineHash));
        ValidateSafeText(issues, request.WorkspaceBoundaryHash, nameof(request.WorkspaceBoundaryHash));
        ValidateSafeText(issues, request.ExpectedBranch, nameof(request.ExpectedBranch));
        ValidateSafeText(issues, request.ExpectedCleanWorktreeHash, nameof(request.ExpectedCleanWorktreeHash));
        ValidateSafeText(issues, request.Boundary, nameof(request.Boundary));
        ValidateSafeList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences));
        ValidateSafeList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims));
        ValidateAcceptedApprovalSafety(request.AcceptedApproval, issues);
        ValidatePolicySatisfactionSafety(request.PolicySatisfaction, issues);
        ValidateDryRunSafety(request.ControlledDryRun, issues);
        ValidatePatchArtifactSafety(request.PatchArtifact, issues);
        ValidateRollbackSupportSafety(request.RollbackSupport, issues);
    }

    private static void ValidateBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        ValidateProjectBindings(request, issues);
        ValidateAcceptedApprovalBindings(request, issues);
        ValidatePolicySatisfactionBindings(request, issues);
        ValidateDryRunBindings(request, issues);
        ValidatePatchArtifactBindings(request, issues);
        ValidateRollbackSupportBindings(request, issues);
        ValidateSubjectBindings(request, issues);
        ValidateSourceBindings(request, issues);
    }

    private static void ValidateProjectBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddGuidMismatch(issues, request.ProjectId, request.AcceptedApproval?.ProjectId, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Accepted approval evidence project must match the source apply gate project.");
        AddGuidMismatch(issues, request.ProjectId, request.PolicySatisfaction?.ProjectId, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Policy satisfaction evidence project must match the source apply gate project.");
        AddGuidMismatch(issues, request.ProjectId, request.ControlledDryRun?.ProjectId, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Controlled dry-run evidence project must match the source apply gate project.");
        AddGuidMismatch(issues, request.ProjectId, request.PatchArtifact?.ProjectId, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Patch artifact evidence project must match the source apply gate project.");
        AddGuidMismatch(issues, request.ProjectId, request.RollbackSupport?.ProjectId, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Rollback support evidence project must match the source apply gate project.");
    }

    private static void ValidateAcceptedApprovalBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddGuidMismatch(issues, request.AcceptedApprovalId, request.AcceptedApproval?.AcceptedApprovalId, "ACCEPTED_APPROVAL_ID_MISMATCH", nameof(request.AcceptedApprovalId), "Accepted approval ID must match accepted approval evidence.");
        AddTextMismatch(issues, request.AcceptedApprovalHash, request.AcceptedApproval?.AcceptedApprovalHash, "ACCEPTED_APPROVAL_HASH_MISMATCH", nameof(request.AcceptedApprovalHash), "Accepted approval hash must match accepted approval evidence.");
        AddGuidMismatch(issues, request.AcceptedApprovalId, request.PolicySatisfaction?.AcceptedApprovalId, "ACCEPTED_APPROVAL_ID_MISMATCH", nameof(SourceApplyGatePolicySatisfactionEvidence.AcceptedApprovalId), "Policy satisfaction evidence must reference the accepted approval ID.");
        AddTextMismatch(issues, request.AcceptedApprovalHash, request.PolicySatisfaction?.AcceptedApprovalHash, "ACCEPTED_APPROVAL_HASH_MISMATCH", nameof(SourceApplyGatePolicySatisfactionEvidence.AcceptedApprovalHash), "Policy satisfaction evidence must reference the accepted approval hash.");
    }

    private static void ValidatePolicySatisfactionBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddGuidMismatch(issues, request.PolicySatisfactionId, request.PolicySatisfaction?.PolicySatisfactionId, "POLICY_SATISFACTION_ID_MISMATCH", nameof(request.PolicySatisfactionId), "Policy satisfaction ID must match policy satisfaction evidence.");
        AddTextMismatch(issues, request.PolicySatisfactionHash, request.PolicySatisfaction?.PolicySatisfactionHash, "POLICY_SATISFACTION_HASH_MISMATCH", nameof(request.PolicySatisfactionHash), "Policy satisfaction hash must match policy satisfaction evidence.");
        AddGuidMismatch(issues, request.PolicySatisfactionId, request.ControlledDryRun?.PolicySatisfactionId, "POLICY_SATISFACTION_ID_MISMATCH", nameof(SourceApplyGateDryRunEvidence.PolicySatisfactionId), "Controlled dry-run evidence must reference the policy satisfaction ID.");
        AddTextMismatch(issues, request.PolicySatisfactionHash, request.ControlledDryRun?.PolicySatisfactionHash, "POLICY_SATISFACTION_HASH_MISMATCH", nameof(SourceApplyGateDryRunEvidence.PolicySatisfactionHash), "Controlled dry-run evidence must reference the policy satisfaction hash.");
        AddGuidMismatch(issues, request.PolicySatisfactionId, request.PatchArtifact?.PolicySatisfactionId, "POLICY_SATISFACTION_ID_MISMATCH", nameof(SourceApplyGatePatchArtifactEvidence.PolicySatisfactionId), "Patch artifact evidence must reference the policy satisfaction ID.");
        AddTextMismatch(issues, request.PolicySatisfactionHash, request.PatchArtifact?.PolicySatisfactionHash, "POLICY_SATISFACTION_HASH_MISMATCH", nameof(SourceApplyGatePatchArtifactEvidence.PolicySatisfactionHash), "Patch artifact evidence must reference the policy satisfaction hash.");
    }

    private static void ValidateDryRunBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddGuidMismatch(issues, request.ControlledDryRunRequestId, request.ControlledDryRun?.ControlledDryRunRequestId, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH", nameof(request.ControlledDryRunRequestId), "Controlled dry-run request ID must match dry-run evidence.");
        AddGuidMismatch(issues, request.DryRunExecutionAuditId, request.ControlledDryRun?.DryRunExecutionAuditId, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH", nameof(request.DryRunExecutionAuditId), "Dry-run execution audit ID must match dry-run evidence.");
        AddTextMismatch(issues, request.DryRunAuditHash, request.ControlledDryRun?.DryRunAuditHash, "DRY_RUN_AUDIT_HASH_MISMATCH", nameof(request.DryRunAuditHash), "Dry-run audit hash must match dry-run evidence.");
        AddTextMismatch(issues, request.DryRunReceiptHash, request.ControlledDryRun?.DryRunReceiptHash, "DRY_RUN_RECEIPT_HASH_MISMATCH", nameof(request.DryRunReceiptHash), "Dry-run receipt hash must match dry-run evidence.");
        AddGuidMismatch(issues, request.ControlledDryRunRequestId, request.PatchArtifact?.ControlledDryRunRequestId, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH", nameof(SourceApplyGatePatchArtifactEvidence.ControlledDryRunRequestId), "Patch artifact evidence must reference the controlled dry-run request ID.");
        AddGuidMismatch(issues, request.DryRunExecutionAuditId, request.PatchArtifact?.DryRunExecutionAuditId, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH", nameof(SourceApplyGatePatchArtifactEvidence.DryRunExecutionAuditId), "Patch artifact evidence must reference the dry-run execution audit ID.");
        AddTextMismatch(issues, request.DryRunAuditHash, request.PatchArtifact?.DryRunAuditHash, "DRY_RUN_AUDIT_HASH_MISMATCH", nameof(SourceApplyGatePatchArtifactEvidence.DryRunAuditHash), "Patch artifact evidence must reference the dry-run audit hash.");
        AddTextMismatch(issues, request.DryRunReceiptHash, request.PatchArtifact?.DryRunReceiptHash, "DRY_RUN_RECEIPT_HASH_MISMATCH", nameof(SourceApplyGatePatchArtifactEvidence.DryRunReceiptHash), "Patch artifact evidence must reference the dry-run receipt hash.");
    }

    private static void ValidatePatchArtifactBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddGuidMismatch(issues, request.PatchArtifactId, request.PatchArtifact?.PatchArtifactId, "PATCH_ARTIFACT_ID_MISMATCH", nameof(request.PatchArtifactId), "Patch artifact ID must match patch artifact evidence.");
        AddTextMismatch(issues, request.PatchHash, request.PatchArtifact?.PatchHash, "PATCH_HASH_MISMATCH", nameof(request.PatchHash), "Patch hash must match patch artifact evidence.");
        AddTextMismatch(issues, request.ChangeSetHash, request.PatchArtifact?.ChangeSetHash, "CHANGE_SET_HASH_MISMATCH", nameof(request.ChangeSetHash), "Change-set hash must match patch artifact evidence.");
        AddGuidMismatch(issues, request.PatchArtifactId, request.RollbackSupport?.PatchArtifactId, "PATCH_ARTIFACT_ID_MISMATCH", nameof(SourceApplyGateRollbackSupportEvidence.PatchArtifactId), "Rollback support evidence must reference the patch artifact ID.");
        AddTextMismatch(issues, request.PatchHash, request.RollbackSupport?.PatchHash, "PATCH_HASH_MISMATCH", nameof(SourceApplyGateRollbackSupportEvidence.PatchHash), "Rollback support evidence must reference the patch hash.");
        AddTextMismatch(issues, request.ChangeSetHash, request.RollbackSupport?.ChangeSetHash, "CHANGE_SET_HASH_MISMATCH", nameof(SourceApplyGateRollbackSupportEvidence.ChangeSetHash), "Rollback support evidence must reference the change-set hash.");
    }

    private static void ValidateRollbackSupportBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddGuidMismatch(issues, request.RollbackSupportReceiptId, request.RollbackSupport?.RollbackSupportReceiptId, "ROLLBACK_SUPPORT_RECEIPT_ID_MISMATCH", nameof(request.RollbackSupportReceiptId), "Rollback support receipt ID must match rollback support evidence.");
        AddTextMismatch(issues, request.RollbackSupportReceiptHash, request.RollbackSupport?.RollbackSupportReceiptHash, "ROLLBACK_SUPPORT_RECEIPT_HASH_MISMATCH", nameof(request.RollbackSupportReceiptHash), "Rollback support receipt hash must match rollback support evidence.");
        AddGuidMismatch(issues, request.RollbackPlanId, request.RollbackSupport?.RollbackPlanId, "ROLLBACK_PLAN_ID_MISMATCH", nameof(request.RollbackPlanId), "Rollback plan ID must match rollback support evidence.");
        AddTextMismatch(issues, request.RollbackPlanHash, request.RollbackSupport?.RollbackPlanHash, "ROLLBACK_PLAN_HASH_MISMATCH", nameof(request.RollbackPlanHash), "Rollback plan hash must match rollback support evidence.");
        AddTextMismatch(issues, request.RollbackGateEvaluationHash, request.RollbackSupport?.RollbackGateEvaluationHash, "ROLLBACK_GATE_EVALUATION_HASH_MISMATCH", nameof(request.RollbackGateEvaluationHash), "Rollback gate evaluation hash must match rollback support evidence.");
    }

    private static void ValidateSubjectBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddSubjectMismatch(issues, request, request.AcceptedApproval?.SubjectKind, request.AcceptedApproval?.SubjectId, request.AcceptedApproval?.SubjectHash);
        AddSubjectMismatch(issues, request, request.PolicySatisfaction?.SubjectKind, request.PolicySatisfaction?.SubjectId, request.PolicySatisfaction?.SubjectHash);
        AddSubjectMismatch(issues, request, request.ControlledDryRun?.SubjectKind, request.ControlledDryRun?.SubjectId, request.ControlledDryRun?.SubjectHash);
        AddSubjectMismatch(issues, request, request.PatchArtifact?.SubjectKind, request.PatchArtifact?.SubjectId, request.PatchArtifact?.SubjectHash);
        AddSubjectMismatch(issues, request, request.RollbackSupport?.SubjectKind, request.RollbackSupport?.SubjectId, request.RollbackSupport?.SubjectHash);
    }

    private static void ValidateSourceBindings(SourceApplyGateEvaluationRequest request, List<SourceApplyGateEvaluationIssue> issues)
    {
        AddSourceMismatch(issues, request, request.ControlledDryRun?.SourceSnapshotReference, request.ControlledDryRun?.SourceBaselineHash, request.ControlledDryRun?.WorkspaceBoundaryHash, request.ControlledDryRun?.ExpectedBranch, request.ControlledDryRun?.ExpectedCleanWorktreeHash);
        AddSourceMismatch(issues, request, request.PatchArtifact?.SourceSnapshotReference, request.PatchArtifact?.SourceBaselineHash, request.PatchArtifact?.WorkspaceBoundaryHash, request.ExpectedBranch, request.ExpectedCleanWorktreeHash);
        AddSourceMismatch(issues, request, request.RollbackSupport?.SourceSnapshotReference, request.RollbackSupport?.SourceBaselineHash, request.RollbackSupport?.WorkspaceBoundaryHash, request.RollbackSupport?.ExpectedBranch, request.RollbackSupport?.ExpectedCleanWorktreeHash);
    }

    private static SourceApplyGateEvaluationResult BuildResult(SourceApplyGateEvaluationRequest? request, IReadOnlyList<SourceApplyGateEvaluationIssue> issues)
    {
        var boundary = SafeScalar(string.IsNullOrWhiteSpace(request?.Boundary) ? SourceApplyGateBoundaryText.Boundary : request.Boundary);

        return new SourceApplyGateEvaluationResult
        {
            Satisfied = issues.Count == 0,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            PatchArtifactId = request?.PatchArtifactId ?? Guid.Empty,
            PatchHash = SafeScalar(request?.PatchHash),
            ChangeSetHash = SafeScalar(request?.ChangeSetHash),
            RollbackSupportReceiptId = request?.RollbackSupportReceiptId ?? Guid.Empty,
            SourceBaselineHash = SafeScalar(request?.SourceBaselineHash),
            ExpectedBranch = SafeScalar(request?.ExpectedBranch),
            ExpectedCleanWorktreeHash = SafeScalar(request?.ExpectedCleanWorktreeHash),
            Issues = issues.ToArray(),
            EvidenceReferences = SafeList(request?.EvidenceReferences),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            Boundary = string.IsNullOrWhiteSpace(boundary) ? SourceApplyGateBoundaryText.Boundary : boundary
        };
    }

    private static void ValidateAcceptedApprovalSafety(SourceApplyGateAcceptedApprovalEvidence? evidence, List<SourceApplyGateEvaluationIssue> issues)
    {
        if (evidence is null)
        {
            return;
        }

        ValidateSafeText(issues, evidence.AcceptedApprovalHash, nameof(evidence.AcceptedApprovalHash));
        ValidateSafeText(issues, evidence.SubjectKind, nameof(evidence.SubjectKind));
        ValidateSafeText(issues, evidence.SubjectId, nameof(evidence.SubjectId));
        ValidateSafeText(issues, evidence.SubjectHash, nameof(evidence.SubjectHash));
    }

    private static void ValidatePolicySatisfactionSafety(SourceApplyGatePolicySatisfactionEvidence? evidence, List<SourceApplyGateEvaluationIssue> issues)
    {
        if (evidence is null)
        {
            return;
        }

        ValidateSafeText(issues, evidence.PolicySatisfactionHash, nameof(evidence.PolicySatisfactionHash));
        ValidateSafeText(issues, evidence.AcceptedApprovalHash, nameof(evidence.AcceptedApprovalHash));
        ValidateSafeText(issues, evidence.SubjectKind, nameof(evidence.SubjectKind));
        ValidateSafeText(issues, evidence.SubjectId, nameof(evidence.SubjectId));
        ValidateSafeText(issues, evidence.SubjectHash, nameof(evidence.SubjectHash));
    }

    private static void ValidateDryRunSafety(SourceApplyGateDryRunEvidence? evidence, List<SourceApplyGateEvaluationIssue> issues)
    {
        if (evidence is null)
        {
            return;
        }

        ValidateSafeText(issues, evidence.DryRunAuditHash, nameof(evidence.DryRunAuditHash));
        ValidateSafeText(issues, evidence.DryRunReceiptHash, nameof(evidence.DryRunReceiptHash));
        ValidateSafeText(issues, evidence.PolicySatisfactionHash, nameof(evidence.PolicySatisfactionHash));
        ValidateSafeText(issues, evidence.SubjectKind, nameof(evidence.SubjectKind));
        ValidateSafeText(issues, evidence.SubjectId, nameof(evidence.SubjectId));
        ValidateSafeText(issues, evidence.SubjectHash, nameof(evidence.SubjectHash));
        ValidateSafeText(issues, evidence.SourceSnapshotReference, nameof(evidence.SourceSnapshotReference));
        ValidateSafeText(issues, evidence.SourceBaselineHash, nameof(evidence.SourceBaselineHash));
        ValidateSafeText(issues, evidence.WorkspaceBoundaryHash, nameof(evidence.WorkspaceBoundaryHash));
        ValidateSafeText(issues, evidence.ExpectedBranch, nameof(evidence.ExpectedBranch));
        ValidateSafeText(issues, evidence.ExpectedCleanWorktreeHash, nameof(evidence.ExpectedCleanWorktreeHash));
    }

    private static void ValidatePatchArtifactSafety(SourceApplyGatePatchArtifactEvidence? evidence, List<SourceApplyGateEvaluationIssue> issues)
    {
        if (evidence is null)
        {
            return;
        }

        ValidateSafeText(issues, evidence.PatchHash, nameof(evidence.PatchHash));
        ValidateSafeText(issues, evidence.ChangeSetHash, nameof(evidence.ChangeSetHash));
        ValidateSafeText(issues, evidence.DryRunAuditHash, nameof(evidence.DryRunAuditHash));
        ValidateSafeText(issues, evidence.DryRunReceiptHash, nameof(evidence.DryRunReceiptHash));
        ValidateSafeText(issues, evidence.PolicySatisfactionHash, nameof(evidence.PolicySatisfactionHash));
        ValidateSafeText(issues, evidence.SubjectKind, nameof(evidence.SubjectKind));
        ValidateSafeText(issues, evidence.SubjectId, nameof(evidence.SubjectId));
        ValidateSafeText(issues, evidence.SubjectHash, nameof(evidence.SubjectHash));
        ValidateSafeText(issues, evidence.SourceSnapshotReference, nameof(evidence.SourceSnapshotReference));
        ValidateSafeText(issues, evidence.SourceBaselineHash, nameof(evidence.SourceBaselineHash));
        ValidateSafeText(issues, evidence.WorkspaceBoundaryHash, nameof(evidence.WorkspaceBoundaryHash));
    }

    private static void ValidateRollbackSupportSafety(SourceApplyGateRollbackSupportEvidence? evidence, List<SourceApplyGateEvaluationIssue> issues)
    {
        if (evidence is null)
        {
            return;
        }

        ValidateSafeText(issues, evidence.RollbackSupportReceiptHash, nameof(evidence.RollbackSupportReceiptHash));
        ValidateSafeText(issues, evidence.RollbackPlanHash, nameof(evidence.RollbackPlanHash));
        ValidateSafeText(issues, evidence.RollbackGateEvaluationHash, nameof(evidence.RollbackGateEvaluationHash));
        ValidateSafeText(issues, evidence.PatchHash, nameof(evidence.PatchHash));
        ValidateSafeText(issues, evidence.ChangeSetHash, nameof(evidence.ChangeSetHash));
        ValidateSafeText(issues, evidence.SubjectKind, nameof(evidence.SubjectKind));
        ValidateSafeText(issues, evidence.SubjectId, nameof(evidence.SubjectId));
        ValidateSafeText(issues, evidence.SubjectHash, nameof(evidence.SubjectHash));
        ValidateSafeText(issues, evidence.SourceSnapshotReference, nameof(evidence.SourceSnapshotReference));
        ValidateSafeText(issues, evidence.SourceBaselineHash, nameof(evidence.SourceBaselineHash));
        ValidateSafeText(issues, evidence.WorkspaceBoundaryHash, nameof(evidence.WorkspaceBoundaryHash));
        ValidateSafeText(issues, evidence.ExpectedBranch, nameof(evidence.ExpectedBranch));
        ValidateSafeText(issues, evidence.ExpectedCleanWorktreeHash, nameof(evidence.ExpectedCleanWorktreeHash));
    }

    private static void AddSubjectMismatch(List<SourceApplyGateEvaluationIssue> issues, SourceApplyGateEvaluationRequest request, string? actualKind, string? actualId, string? actualHash)
    {
        AddTextMismatch(issues, request.SubjectKind, actualKind, "SUBJECT_KIND_MISMATCH", nameof(request.SubjectKind), "Evidence subject kind must match the source apply gate subject kind.");
        AddTextMismatch(issues, request.SubjectId, actualId, "SUBJECT_ID_MISMATCH", nameof(request.SubjectId), "Evidence subject ID must match the source apply gate subject ID.");
        AddTextMismatch(issues, request.SubjectHash, actualHash, "SUBJECT_HASH_MISMATCH", nameof(request.SubjectHash), "Evidence subject hash must match the source apply gate subject hash.");
    }

    private static void AddSourceMismatch(List<SourceApplyGateEvaluationIssue> issues, SourceApplyGateEvaluationRequest request, string? sourceSnapshotReference, string? sourceBaselineHash, string? workspaceBoundaryHash, string? expectedBranch, string? expectedCleanWorktreeHash)
    {
        AddTextMismatch(issues, request.SourceSnapshotReference, sourceSnapshotReference, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH", nameof(request.SourceSnapshotReference), "Evidence source snapshot reference must match the source apply gate source snapshot reference.");
        AddTextMismatch(issues, request.SourceBaselineHash, sourceBaselineHash, "SOURCE_BASELINE_HASH_MISMATCH", nameof(request.SourceBaselineHash), "Evidence source baseline hash must match the source apply gate source baseline hash.");
        AddTextMismatch(issues, request.WorkspaceBoundaryHash, workspaceBoundaryHash, "WORKSPACE_BOUNDARY_HASH_MISMATCH", nameof(request.WorkspaceBoundaryHash), "Evidence workspace boundary hash must match the source apply gate workspace boundary hash.");
        AddTextMismatch(issues, request.ExpectedBranch, expectedBranch, "EXPECTED_BRANCH_MISMATCH", nameof(request.ExpectedBranch), "Evidence expected branch must match the source apply gate expected branch.");
        AddTextMismatch(issues, request.ExpectedCleanWorktreeHash, expectedCleanWorktreeHash, "EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH", nameof(request.ExpectedCleanWorktreeHash), "Evidence expected clean worktree hash must match the source apply gate expected clean worktree hash.");
    }

    private static void ValidateExpiry(List<SourceApplyGateEvaluationIssue> issues, DateTimeOffset? expiresAtUtc, DateTimeOffset evaluatedAtUtc, string field, string code)
    {
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= evaluatedAtUtc)
        {
            Add(issues, code, field, "Source apply gate upstream evidence has expired.");
        }
    }

    private static void RequireGuid(List<SourceApplyGateEvaluationIssue> issues, Guid value, string field, string code, string message)
    {
        if (value == Guid.Empty)
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(List<SourceApplyGateEvaluationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireList(List<SourceApplyGateEvaluationIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<SourceApplyGateEvaluationIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<SourceApplyGateEvaluationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Source apply gate evaluation text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Source apply gate evaluation text must not claim authority: {marker}.");
            }
        }
    }

    private static void AddGuidMismatch(List<SourceApplyGateEvaluationIssue> issues, Guid expected, Guid? actual, string code, string field, string message)
    {
        if (actual.HasValue && expected != actual.Value)
        {
            Add(issues, code, field, message);
        }
    }

    private static void AddTextMismatch(List<SourceApplyGateEvaluationIssue> issues, string? expected, string? actual, string code, string field, string message)
    {
        if (actual is not null && !StringEquals(expected, actual))
        {
            Add(issues, code, field, message);
        }
    }

    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value) && !ContainsUnsafeMarker(value))
            .Select(value => value.Trim())
            .ToArray();
    }

    private static string SafeScalar(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static bool ContainsUnsafeMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return PrivateMaterialMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            AuthorityClaimMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<SourceApplyGateEvaluationIssue> issues, string code, string field, string message) =>
        issues.Add(new SourceApplyGateEvaluationIssue(code, field, message));
}

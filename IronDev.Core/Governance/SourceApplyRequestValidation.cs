namespace IronDev.Core.Governance;

public static class SourceApplyRequestValidation
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
        "patch applied",
        "files written",
        "rollback executed",
        "rollback succeeded",
        "workflow continued",
        "release approved",
        "release ready"
    ];

    public static SourceApplyRequestValidationResult Validate(SourceApplyRequest? request)
    {
        var issues = new List<SourceApplyRequestValidationIssue>();

        if (request is null)
        {
            Add(issues, "SOURCE_APPLY_REQUEST_REQUIRED", "request", "Source apply request is required.");
            return new SourceApplyRequestValidationResult(issues);
        }

        ValidateRequiredFields(request, issues);
        ValidateGateEvidence(request, issues);
        ValidateFileOperations(request, issues);
        ValidateExpiry(request, issues);
        ValidateTextSafety(request, issues);

        return new SourceApplyRequestValidationResult(issues);
    }

    private static void ValidateRequiredFields(SourceApplyRequest request, List<SourceApplyRequestValidationIssue> issues)
    {
        RequireGuid(issues, request.SourceApplyRequestId, nameof(request.SourceApplyRequestId), "SOURCE_APPLY_REQUEST_ID_REQUIRED", "Source apply request ID is required.");
        RequireGuid(issues, request.ProjectId, nameof(request.ProjectId), "PROJECT_ID_REQUIRED", "Project ID is required.");
        RequireGuid(issues, request.SourceApplyGateEvaluationId, nameof(request.SourceApplyGateEvaluationId), "SOURCE_APPLY_GATE_EVALUATION_ID_REQUIRED", "Source apply gate evaluation ID is required.");
        RequireText(issues, request.SourceApplyGateEvaluationHash, nameof(request.SourceApplyGateEvaluationHash), "SOURCE_APPLY_GATE_EVALUATION_HASH_REQUIRED", "Source apply gate evaluation hash is required.");
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
        RequireText(issues, request.SourceApplyRequestHash, nameof(request.SourceApplyRequestHash), "SOURCE_APPLY_REQUEST_HASH_REQUIRED", "Source apply request hash is required.");
        RequireList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        RequireList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");
        RequireText(issues, request.Boundary, nameof(request.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");

        if (request.RequestedAtUtc == default)
        {
            Add(issues, "REQUESTED_AT_UTC_REQUIRED", nameof(request.RequestedAtUtc), "Requested timestamp is required.");
        }

        if (!request.SourceApplyGateSatisfied)
        {
            Add(issues, "SOURCE_APPLY_GATE_UNSATISFIED", nameof(request.SourceApplyGateSatisfied), "Source apply request requires a satisfied source-apply gate evaluation.");
        }
    }

    private static void ValidateGateEvidence(SourceApplyRequest request, List<SourceApplyRequestValidationIssue> issues)
    {
        var gate = request.SourceApplyGateEvaluation;
        if (gate is null)
        {
            Add(issues, "SOURCE_APPLY_GATE_EVIDENCE_REQUIRED", nameof(request.SourceApplyGateEvaluation), "Source apply gate evaluation evidence is required.");
            return;
        }

        RequireGuid(issues, gate.SourceApplyGateEvaluationId, nameof(gate.SourceApplyGateEvaluationId), "SOURCE_APPLY_GATE_EVALUATION_ID_REQUIRED", "Source apply gate evaluation evidence ID is required.");
        RequireText(issues, gate.SourceApplyGateEvaluationHash, nameof(gate.SourceApplyGateEvaluationHash), "SOURCE_APPLY_GATE_EVALUATION_HASH_REQUIRED", "Source apply gate evaluation evidence hash is required.");
        RequireList(issues, gate.EvidenceReferences, nameof(gate.EvidenceReferences), "SOURCE_APPLY_GATE_EVIDENCE_REFERENCES_REQUIRED", "Source apply gate evaluation evidence references are required.");
        RequireList(issues, gate.BoundaryMaxims, nameof(gate.BoundaryMaxims), "SOURCE_APPLY_GATE_BOUNDARY_MAXIMS_REQUIRED", "Source apply gate evaluation boundary maxims are required.");

        if (!gate.Satisfied)
        {
            Add(issues, "SOURCE_APPLY_GATE_EVIDENCE_UNSATISFIED", nameof(gate.Satisfied), "Source apply gate evidence must be satisfied.");
        }

        AddGuidMismatch(issues, request.ProjectId, gate.ProjectId, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Source apply gate evidence project must match request project.");
        AddGuidMismatch(issues, request.SourceApplyGateEvaluationId, gate.SourceApplyGateEvaluationId, "SOURCE_APPLY_GATE_EVALUATION_ID_MISMATCH", nameof(request.SourceApplyGateEvaluationId), "Source apply gate evaluation ID must match gate evidence.");
        AddTextMismatch(issues, request.SourceApplyGateEvaluationHash, gate.SourceApplyGateEvaluationHash, "SOURCE_APPLY_GATE_EVALUATION_HASH_MISMATCH", nameof(request.SourceApplyGateEvaluationHash), "Source apply gate evaluation hash must match gate evidence.");
        AddGuidMismatch(issues, request.AcceptedApprovalId, gate.AcceptedApprovalId, "ACCEPTED_APPROVAL_ID_MISMATCH", nameof(request.AcceptedApprovalId), "Source apply gate evidence accepted approval ID must match request accepted approval ID.");
        AddTextMismatch(issues, request.AcceptedApprovalHash, gate.AcceptedApprovalHash, "ACCEPTED_APPROVAL_HASH_MISMATCH", nameof(request.AcceptedApprovalHash), "Source apply gate evidence accepted approval hash must match request accepted approval hash.");
        AddGuidMismatch(issues, request.PolicySatisfactionId, gate.PolicySatisfactionId, "POLICY_SATISFACTION_ID_MISMATCH", nameof(request.PolicySatisfactionId), "Source apply gate evidence policy satisfaction ID must match request policy satisfaction ID.");
        AddTextMismatch(issues, request.PolicySatisfactionHash, gate.PolicySatisfactionHash, "POLICY_SATISFACTION_HASH_MISMATCH", nameof(request.PolicySatisfactionHash), "Source apply gate evidence policy satisfaction hash must match request policy satisfaction hash.");
        AddGuidMismatch(issues, request.ControlledDryRunRequestId, gate.ControlledDryRunRequestId, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH", nameof(request.ControlledDryRunRequestId), "Source apply gate evidence controlled dry-run request ID must match request controlled dry-run request ID.");
        AddGuidMismatch(issues, request.DryRunExecutionAuditId, gate.DryRunExecutionAuditId, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH", nameof(request.DryRunExecutionAuditId), "Source apply gate evidence dry-run execution audit ID must match request dry-run execution audit ID.");
        AddTextMismatch(issues, request.DryRunAuditHash, gate.DryRunAuditHash, "DRY_RUN_AUDIT_HASH_MISMATCH", nameof(request.DryRunAuditHash), "Source apply gate evidence dry-run audit hash must match request dry-run audit hash.");
        AddTextMismatch(issues, request.DryRunReceiptHash, gate.DryRunReceiptHash, "DRY_RUN_RECEIPT_HASH_MISMATCH", nameof(request.DryRunReceiptHash), "Source apply gate evidence dry-run receipt hash must match request dry-run receipt hash.");
        AddGuidMismatch(issues, request.PatchArtifactId, gate.PatchArtifactId, "PATCH_ARTIFACT_ID_MISMATCH", nameof(request.PatchArtifactId), "Source apply gate evidence patch artifact ID must match request patch artifact ID.");
        AddTextMismatch(issues, request.PatchHash, gate.PatchHash, "PATCH_HASH_MISMATCH", nameof(request.PatchHash), "Source apply gate evidence patch hash must match request patch hash.");
        AddTextMismatch(issues, request.ChangeSetHash, gate.ChangeSetHash, "CHANGE_SET_HASH_MISMATCH", nameof(request.ChangeSetHash), "Source apply gate evidence change-set hash must match request change-set hash.");
        AddGuidMismatch(issues, request.RollbackSupportReceiptId, gate.RollbackSupportReceiptId, "ROLLBACK_SUPPORT_RECEIPT_ID_MISMATCH", nameof(request.RollbackSupportReceiptId), "Source apply gate evidence rollback support receipt ID must match request rollback support receipt ID.");
        AddTextMismatch(issues, request.RollbackSupportReceiptHash, gate.RollbackSupportReceiptHash, "ROLLBACK_SUPPORT_RECEIPT_HASH_MISMATCH", nameof(request.RollbackSupportReceiptHash), "Source apply gate evidence rollback support receipt hash must match request rollback support receipt hash.");
        AddGuidMismatch(issues, request.RollbackPlanId, gate.RollbackPlanId, "ROLLBACK_PLAN_ID_MISMATCH", nameof(request.RollbackPlanId), "Source apply gate evidence rollback plan ID must match request rollback plan ID.");
        AddTextMismatch(issues, request.RollbackPlanHash, gate.RollbackPlanHash, "ROLLBACK_PLAN_HASH_MISMATCH", nameof(request.RollbackPlanHash), "Source apply gate evidence rollback plan hash must match request rollback plan hash.");
        AddTextMismatch(issues, request.RollbackGateEvaluationHash, gate.RollbackGateEvaluationHash, "ROLLBACK_GATE_EVALUATION_HASH_MISMATCH", nameof(request.RollbackGateEvaluationHash), "Source apply gate evidence rollback gate evaluation hash must match request rollback gate evaluation hash.");
        AddTextMismatch(issues, request.SubjectKind, gate.SubjectKind, "SUBJECT_KIND_MISMATCH", nameof(request.SubjectKind), "Source apply gate evidence subject kind must match request subject kind.");
        AddTextMismatch(issues, request.SubjectId, gate.SubjectId, "SUBJECT_ID_MISMATCH", nameof(request.SubjectId), "Source apply gate evidence subject ID must match request subject ID.");
        AddTextMismatch(issues, request.SubjectHash, gate.SubjectHash, "SUBJECT_HASH_MISMATCH", nameof(request.SubjectHash), "Source apply gate evidence subject hash must match request subject hash.");
        AddTextMismatch(issues, request.SourceSnapshotReference, gate.SourceSnapshotReference, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH", nameof(request.SourceSnapshotReference), "Source apply gate evidence source snapshot reference must match request source snapshot reference.");
        AddTextMismatch(issues, request.SourceBaselineHash, gate.SourceBaselineHash, "SOURCE_BASELINE_HASH_MISMATCH", nameof(request.SourceBaselineHash), "Source apply gate evidence source baseline hash must match request source baseline hash.");
        AddTextMismatch(issues, request.WorkspaceBoundaryHash, gate.WorkspaceBoundaryHash, "WORKSPACE_BOUNDARY_HASH_MISMATCH", nameof(request.WorkspaceBoundaryHash), "Source apply gate evidence workspace boundary hash must match request workspace boundary hash.");
        AddTextMismatch(issues, request.ExpectedBranch, gate.ExpectedBranch, "EXPECTED_BRANCH_MISMATCH", nameof(request.ExpectedBranch), "Source apply gate evidence expected branch must match request expected branch.");
        AddTextMismatch(issues, request.ExpectedCleanWorktreeHash, gate.ExpectedCleanWorktreeHash, "EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH", nameof(request.ExpectedCleanWorktreeHash), "Source apply gate evidence expected clean worktree hash must match request expected clean worktree hash.");
    }

    private static void ValidateFileOperations(SourceApplyRequest request, List<SourceApplyRequestValidationIssue> issues)
    {
        if (request.FileOperations is null || request.FileOperations.Count == 0)
        {
            Add(issues, "FILE_OPERATIONS_REQUIRED", nameof(request.FileOperations), "At least one source apply request file operation is required.");
            return;
        }

        foreach (var group in request.FileOperations.GroupBy(operation => (Path: Normalize(operation.Path), ChangeHash: Normalize(operation.PatchArtifactChangeHash))))
        {
            if (group.Count() > 1)
            {
                Add(issues, "FILE_OPERATION_DUPLICATE", nameof(request.FileOperations), "Source apply request file operations must not duplicate the same path and patch artifact change hash.");
            }
        }

        for (var index = 0; index < request.FileOperations.Count; index++)
        {
            ValidateFileOperation(request.FileOperations[index], $"{nameof(request.FileOperations)}[{index}]", issues);
        }
    }

    private static void ValidateFileOperation(SourceApplyRequestFileOperation operation, string fieldPrefix, List<SourceApplyRequestValidationIssue> issues)
    {
        RequireText(issues, operation.Path, $"{fieldPrefix}.{nameof(operation.Path)}", "FILE_OPERATION_PATH_REQUIRED", "File operation path is required.");
        RequireText(issues, operation.OperationKind, $"{fieldPrefix}.{nameof(operation.OperationKind)}", "FILE_OPERATION_KIND_REQUIRED", "File operation kind is required.");
        RequireText(issues, operation.PatchArtifactChangeHash, $"{fieldPrefix}.{nameof(operation.PatchArtifactChangeHash)}", "PATCH_ARTIFACT_CHANGE_HASH_REQUIRED", "Patch artifact change hash is required.");
        RequireText(issues, operation.OperationHash, $"{fieldPrefix}.{nameof(operation.OperationHash)}", "FILE_OPERATION_HASH_REQUIRED", "File operation hash is required.");
        ValidatePath(issues, operation.Path, $"{fieldPrefix}.{nameof(operation.Path)}");

        if (!string.IsNullOrWhiteSpace(operation.PreviousPath))
        {
            ValidatePath(issues, operation.PreviousPath, $"{fieldPrefix}.{nameof(operation.PreviousPath)}");
        }

        if (!SourceApplyRequestFileOperationKinds.Known.Contains(operation.OperationKind, StringComparer.Ordinal))
        {
            Add(issues, "FILE_OPERATION_KIND_INVALID", $"{fieldPrefix}.{nameof(operation.OperationKind)}", "File operation kind must be CreateFile, ModifyFile, DeleteFile, RenameFile, or Noop.");
        }

        ValidateSafeText(issues, operation.Path, $"{fieldPrefix}.{nameof(operation.Path)}");
        ValidateSafeText(issues, operation.OperationKind, $"{fieldPrefix}.{nameof(operation.OperationKind)}");
        ValidateSafeText(issues, operation.PreviousPath, $"{fieldPrefix}.{nameof(operation.PreviousPath)}");
        ValidateSafeText(issues, operation.BeforeContentHash, $"{fieldPrefix}.{nameof(operation.BeforeContentHash)}");
        ValidateSafeText(issues, operation.AfterContentHash, $"{fieldPrefix}.{nameof(operation.AfterContentHash)}");
        ValidateSafeText(issues, operation.DiffHash, $"{fieldPrefix}.{nameof(operation.DiffHash)}");
        ValidateSafeText(issues, operation.PatchArtifactChangeHash, $"{fieldPrefix}.{nameof(operation.PatchArtifactChangeHash)}");
        ValidateSafeText(issues, operation.OperationHash, $"{fieldPrefix}.{nameof(operation.OperationHash)}");
    }

    private static void ValidateExpiry(SourceApplyRequest request, List<SourceApplyRequestValidationIssue> issues)
    {
        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= request.RequestedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(request.ExpiresAtUtc), "Expiry timestamp must be after requested timestamp.");
        }

        if (request.SourceApplyGateEvaluation?.ExpiresAtUtc.HasValue == true && request.SourceApplyGateEvaluation.ExpiresAtUtc.Value <= request.RequestedAtUtc)
        {
            Add(issues, "SOURCE_APPLY_GATE_EVIDENCE_EXPIRED", nameof(request.SourceApplyGateEvaluation), "Source apply gate evidence has expired.");
        }
    }

    private static void ValidateTextSafety(SourceApplyRequest request, List<SourceApplyRequestValidationIssue> issues)
    {
        ValidateRequestText(issues, request);

        var gate = request.SourceApplyGateEvaluation;
        if (gate is null)
        {
            return;
        }

        ValidateSafeText(issues, gate.SourceApplyGateEvaluationHash, nameof(gate.SourceApplyGateEvaluationHash));
        ValidateSafeText(issues, gate.AcceptedApprovalHash, nameof(gate.AcceptedApprovalHash));
        ValidateSafeText(issues, gate.PolicySatisfactionHash, nameof(gate.PolicySatisfactionHash));
        ValidateSafeText(issues, gate.DryRunAuditHash, nameof(gate.DryRunAuditHash));
        ValidateSafeText(issues, gate.DryRunReceiptHash, nameof(gate.DryRunReceiptHash));
        ValidateSafeText(issues, gate.PatchHash, nameof(gate.PatchHash));
        ValidateSafeText(issues, gate.ChangeSetHash, nameof(gate.ChangeSetHash));
        ValidateSafeText(issues, gate.RollbackSupportReceiptHash, nameof(gate.RollbackSupportReceiptHash));
        ValidateSafeText(issues, gate.RollbackPlanHash, nameof(gate.RollbackPlanHash));
        ValidateSafeText(issues, gate.RollbackGateEvaluationHash, nameof(gate.RollbackGateEvaluationHash));
        ValidateSafeText(issues, gate.SubjectKind, nameof(gate.SubjectKind));
        ValidateSafeText(issues, gate.SubjectId, nameof(gate.SubjectId));
        ValidateSafeText(issues, gate.SubjectHash, nameof(gate.SubjectHash));
        ValidateSafeText(issues, gate.SourceSnapshotReference, nameof(gate.SourceSnapshotReference));
        ValidateSafeText(issues, gate.SourceBaselineHash, nameof(gate.SourceBaselineHash));
        ValidateSafeText(issues, gate.WorkspaceBoundaryHash, nameof(gate.WorkspaceBoundaryHash));
        ValidateSafeText(issues, gate.ExpectedBranch, nameof(gate.ExpectedBranch));
        ValidateSafeText(issues, gate.ExpectedCleanWorktreeHash, nameof(gate.ExpectedCleanWorktreeHash));
        ValidateSafeText(issues, gate.Boundary, nameof(gate.Boundary));
        ValidateSafeList(issues, gate.EvidenceReferences, nameof(gate.EvidenceReferences));
        ValidateSafeList(issues, gate.BoundaryMaxims, nameof(gate.BoundaryMaxims));
    }

    private static void ValidateRequestText(List<SourceApplyRequestValidationIssue> issues, SourceApplyRequest request)
    {
        ValidateSafeText(issues, request.SourceApplyGateEvaluationHash, nameof(request.SourceApplyGateEvaluationHash));
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
        ValidateSafeText(issues, request.SourceApplyRequestHash, nameof(request.SourceApplyRequestHash));
        ValidateSafeText(issues, request.Boundary, nameof(request.Boundary));
        ValidateSafeList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences));
        ValidateSafeList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims));
    }

    private static void ValidatePath(List<SourceApplyRequestValidationIssue> issues, string? value, string field)
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
            path.Any(char.IsControl) ||
            (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') ||
            segments.Any(segment => segment == ".." || segment == ".git");

        if (unsafePath)
        {
            Add(issues, "FILE_OPERATION_PATH_UNSAFE", field, "Source apply request file paths must be safe relative repository paths.");
        }
    }

    private static void RequireGuid(List<SourceApplyRequestValidationIssue> issues, Guid value, string field, string code, string message)
    {
        if (value == Guid.Empty)
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(List<SourceApplyRequestValidationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireList(List<SourceApplyRequestValidationIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<SourceApplyRequestValidationIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<SourceApplyRequestValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Source apply request text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Source apply request text must not claim authority: {marker}.");
            }
        }
    }

    private static void AddGuidMismatch(List<SourceApplyRequestValidationIssue> issues, Guid expected, Guid actual, string code, string field, string message)
    {
        if (expected != actual)
        {
            Add(issues, code, field, message);
        }
    }

    private static void AddTextMismatch(List<SourceApplyRequestValidationIssue> issues, string? expected, string? actual, string code, string field, string message)
    {
        if (!StringEquals(expected, actual))
        {
            Add(issues, code, field, message);
        }
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<SourceApplyRequestValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new SourceApplyRequestValidationIssue(code, field, message));
}

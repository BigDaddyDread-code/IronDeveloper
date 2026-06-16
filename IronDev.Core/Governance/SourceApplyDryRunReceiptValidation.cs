namespace IronDev.Core.Governance;

public static class SourceApplyDryRunReceiptValidation
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

    public static SourceApplyDryRunReceiptValidationResult Validate(SourceApplyDryRunReceipt? receipt)
    {
        var issues = new List<SourceApplyDryRunReceiptValidationIssue>();

        if (receipt is null)
        {
            Add(issues, "SOURCE_APPLY_DRY_RUN_RECEIPT_REQUIRED", "receipt", "Source apply dry-run receipt is required.");
            return new SourceApplyDryRunReceiptValidationResult(issues);
        }

        ValidateRequiredFields(receipt, issues);
        ValidateFileResults(receipt, issues);
        ValidateTextSafety(receipt, issues);

        return new SourceApplyDryRunReceiptValidationResult(issues);
    }

    private static void ValidateRequiredFields(SourceApplyDryRunReceipt receipt, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        RequireGuid(issues, receipt.SourceApplyDryRunReceiptId, nameof(receipt.SourceApplyDryRunReceiptId), "SOURCE_APPLY_DRY_RUN_RECEIPT_ID_REQUIRED", "Source apply dry-run receipt ID is required.");
        RequireGuid(issues, receipt.ProjectId, nameof(receipt.ProjectId), "PROJECT_ID_REQUIRED", "Project ID is required.");
        RequireGuid(issues, receipt.SourceApplyDryRunRequestId, nameof(receipt.SourceApplyDryRunRequestId), "SOURCE_APPLY_DRY_RUN_REQUEST_ID_REQUIRED", "Source apply dry-run request ID is required.");
        RequireText(issues, receipt.SourceApplyDryRunRequestHash, nameof(receipt.SourceApplyDryRunRequestHash), "SOURCE_APPLY_DRY_RUN_REQUEST_HASH_REQUIRED", "Source apply dry-run request hash is required.");
        RequireText(issues, receipt.DryRunResultHash, nameof(receipt.DryRunResultHash), "DRY_RUN_RESULT_HASH_REQUIRED", "Dry-run result hash is required.");
        RequireGuid(issues, receipt.SourceApplyRequestId, nameof(receipt.SourceApplyRequestId), "SOURCE_APPLY_REQUEST_ID_REQUIRED", "Source apply request ID is required.");
        RequireText(issues, receipt.SourceApplyRequestHash, nameof(receipt.SourceApplyRequestHash), "SOURCE_APPLY_REQUEST_HASH_REQUIRED", "Source apply request hash is required.");
        RequireGuid(issues, receipt.SourceApplyGateEvaluationId, nameof(receipt.SourceApplyGateEvaluationId), "SOURCE_APPLY_GATE_EVALUATION_ID_REQUIRED", "Source apply gate evaluation ID is required.");
        RequireText(issues, receipt.SourceApplyGateEvaluationHash, nameof(receipt.SourceApplyGateEvaluationHash), "SOURCE_APPLY_GATE_EVALUATION_HASH_REQUIRED", "Source apply gate evaluation hash is required.");
        RequireGuid(issues, receipt.PatchArtifactId, nameof(receipt.PatchArtifactId), "PATCH_ARTIFACT_ID_REQUIRED", "Patch artifact ID is required.");
        RequireText(issues, receipt.PatchHash, nameof(receipt.PatchHash), "PATCH_HASH_REQUIRED", "Patch hash is required.");
        RequireText(issues, receipt.ChangeSetHash, nameof(receipt.ChangeSetHash), "CHANGE_SET_HASH_REQUIRED", "Change-set hash is required.");
        RequireGuid(issues, receipt.RollbackSupportReceiptId, nameof(receipt.RollbackSupportReceiptId), "ROLLBACK_SUPPORT_RECEIPT_ID_REQUIRED", "Rollback support receipt ID is required.");
        RequireText(issues, receipt.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash), "ROLLBACK_SUPPORT_RECEIPT_HASH_REQUIRED", "Rollback support receipt hash is required.");
        RequireText(issues, receipt.SourceBaselineHash, nameof(receipt.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        RequireText(issues, receipt.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        RequireText(issues, receipt.ExpectedBranch, nameof(receipt.ExpectedBranch), "EXPECTED_BRANCH_REQUIRED", "Expected branch is required.");
        RequireText(issues, receipt.ExpectedCleanWorktreeHash, nameof(receipt.ExpectedCleanWorktreeHash), "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED", "Expected clean worktree hash is required.");
        RequireText(issues, receipt.SourceApplyDryRunReceiptHash, nameof(receipt.SourceApplyDryRunReceiptHash), "SOURCE_APPLY_DRY_RUN_RECEIPT_HASH_REQUIRED", "Source apply dry-run receipt hash is required.");
        RequireList(issues, receipt.EvidenceReferences, nameof(receipt.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        RequireList(issues, receipt.BoundaryMaxims, nameof(receipt.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");
        RequireText(issues, receipt.Boundary, nameof(receipt.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");

        if (receipt.CreatedAtUtc == default)
        {
            Add(issues, "CREATED_AT_UTC_REQUIRED", nameof(receipt.CreatedAtUtc), "Created timestamp is required.");
        }

        if (receipt.ExpiresAtUtc.HasValue && receipt.ExpiresAtUtc.Value <= receipt.CreatedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(receipt.ExpiresAtUtc), "Expiry timestamp must be after created timestamp.");
        }
    }

    private static void ValidateFileResults(SourceApplyDryRunReceipt receipt, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        if (receipt.FileResults is null || receipt.FileResults.Count == 0)
        {
            Add(issues, "FILE_RESULTS_REQUIRED", nameof(receipt.FileResults), "At least one source apply dry-run receipt file result is required.");
            return;
        }

        foreach (var group in receipt.FileResults.GroupBy(result => (Path: Normalize(result.Path), ChangeHash: Normalize(result.PatchArtifactChangeHash))))
        {
            if (group.Count() > 1)
            {
                Add(issues, "FILE_RESULT_DUPLICATE", nameof(receipt.FileResults), "Source apply dry-run receipt file results must not duplicate the same path and patch artifact change hash.");
            }
        }

        for (var index = 0; index < receipt.FileResults.Count; index++)
        {
            ValidateFileResult(receipt.FileResults[index], $"{nameof(receipt.FileResults)}[{index}]", issues);
        }
    }

    private static void ValidateFileResult(SourceApplyDryRunReceiptFileResult result, string fieldPrefix, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        RequireText(issues, result.Path, $"{fieldPrefix}.{nameof(result.Path)}", "FILE_RESULT_PATH_REQUIRED", "File result path is required.");
        RequireText(issues, result.OperationKind, $"{fieldPrefix}.{nameof(result.OperationKind)}", "FILE_OPERATION_KIND_REQUIRED", "File operation kind is required.");
        RequireText(issues, result.PatchArtifactChangeHash, $"{fieldPrefix}.{nameof(result.PatchArtifactChangeHash)}", "PATCH_ARTIFACT_CHANGE_HASH_REQUIRED", "Patch artifact change hash is required.");
        RequireText(issues, result.OperationHash, $"{fieldPrefix}.{nameof(result.OperationHash)}", "FILE_OPERATION_HASH_REQUIRED", "File operation hash is required.");
        RequireText(issues, result.FileResultHash, $"{fieldPrefix}.{nameof(result.FileResultHash)}", "FILE_RESULT_HASH_REQUIRED", "File result hash is required.");
        ValidatePath(issues, result.Path, $"{fieldPrefix}.{nameof(result.Path)}");

        if (!string.IsNullOrWhiteSpace(result.PreviousPath))
        {
            ValidatePath(issues, result.PreviousPath, $"{fieldPrefix}.{nameof(result.PreviousPath)}");
        }

        if (!SourceApplyRequestFileOperationKinds.Known.Contains(result.OperationKind, StringComparer.Ordinal))
        {
            Add(issues, "FILE_OPERATION_KIND_INVALID", $"{fieldPrefix}.{nameof(result.OperationKind)}", "File operation kind must be CreateFile, ModifyFile, DeleteFile, RenameFile, or Noop.");
        }

        ValidateOperationSpecificHashes(result, fieldPrefix, issues);
        ValidateWouldFlags(result, fieldPrefix, issues);
        ValidateSafeFileResultText(result, fieldPrefix, issues);
    }

    private static void ValidateOperationSpecificHashes(SourceApplyDryRunReceiptFileResult result, string fieldPrefix, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        switch (result.OperationKind)
        {
            case SourceApplyRequestFileOperationKinds.CreateFile:
                RequireText(issues, result.ExpectedAfterContentHash, $"{fieldPrefix}.{nameof(result.ExpectedAfterContentHash)}", "AFTER_CONTENT_HASH_REQUIRED", "CreateFile receipt result requires an after-content hash.");
                break;
            case SourceApplyRequestFileOperationKinds.ModifyFile:
                RequireText(issues, result.ExpectedBeforeContentHash, $"{fieldPrefix}.{nameof(result.ExpectedBeforeContentHash)}", "BEFORE_CONTENT_HASH_REQUIRED", "ModifyFile receipt result requires a before-content hash.");
                RequireText(issues, result.ExpectedAfterContentHash, $"{fieldPrefix}.{nameof(result.ExpectedAfterContentHash)}", "AFTER_CONTENT_HASH_REQUIRED", "ModifyFile receipt result requires an after-content hash.");
                break;
            case SourceApplyRequestFileOperationKinds.DeleteFile:
                RequireText(issues, result.ExpectedBeforeContentHash, $"{fieldPrefix}.{nameof(result.ExpectedBeforeContentHash)}", "BEFORE_CONTENT_HASH_REQUIRED", "DeleteFile receipt result requires a before-content hash.");
                break;
            case SourceApplyRequestFileOperationKinds.RenameFile:
                RequireText(issues, result.PreviousPath, $"{fieldPrefix}.{nameof(result.PreviousPath)}", "PREVIOUS_PATH_REQUIRED", "RenameFile receipt result requires a previous path.");
                break;
        }
    }

    private static void ValidateWouldFlags(SourceApplyDryRunReceiptFileResult result, string fieldPrefix, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        var wouldCount = new[] { result.WouldCreate, result.WouldModify, result.WouldDelete, result.WouldRename, result.WouldNoop }.Count(value => value);
        var issueCount = result.IssueCodes?.Count ?? 0;

        if (wouldCount > 1)
        {
            Add(issues, "WOULD_FLAGS_INCONSISTENT", fieldPrefix, "At most one dry-run would flag may be true for a file result.");
        }

        if (wouldCount == 0 && issueCount == 0)
        {
            Add(issues, "WOULD_FLAGS_MISSING", fieldPrefix, "A file result with no dry-run would flag must include issue codes explaining why.");
        }

        if (!FlagMatchesOperation(result))
        {
            Add(issues, "WOULD_FLAG_OPERATION_MISMATCH", fieldPrefix, "Dry-run would flag must match the file operation kind.");
        }

        if (!result.PreconditionsSatisfied && issueCount == 0)
        {
            Add(issues, "PRECONDITION_FAILURE_REQUIRES_ISSUE_CODES", $"{fieldPrefix}.{nameof(result.IssueCodes)}", "A file result with failed preconditions must include issue codes.");
        }

        if (result.PreconditionsSatisfied && issueCount > 0)
        {
            Add(issues, "PRECONDITION_SUCCESS_MUST_NOT_HAVE_ISSUE_CODES", $"{fieldPrefix}.{nameof(result.IssueCodes)}", "A file result with satisfied preconditions must not include issue codes.");
        }
    }

    private static bool FlagMatchesOperation(SourceApplyDryRunReceiptFileResult result) =>
        result.OperationKind switch
        {
            SourceApplyRequestFileOperationKinds.CreateFile => result.WouldCreate && !result.WouldModify && !result.WouldDelete && !result.WouldRename && !result.WouldNoop,
            SourceApplyRequestFileOperationKinds.ModifyFile => !result.WouldCreate && result.WouldModify && !result.WouldDelete && !result.WouldRename && !result.WouldNoop,
            SourceApplyRequestFileOperationKinds.DeleteFile => !result.WouldCreate && !result.WouldModify && result.WouldDelete && !result.WouldRename && !result.WouldNoop,
            SourceApplyRequestFileOperationKinds.RenameFile => !result.WouldCreate && !result.WouldModify && !result.WouldDelete && result.WouldRename && !result.WouldNoop,
            SourceApplyRequestFileOperationKinds.Noop => !result.WouldCreate && !result.WouldModify && !result.WouldDelete && !result.WouldRename && result.WouldNoop,
            _ => false
        };

    private static void ValidateTextSafety(SourceApplyDryRunReceipt receipt, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        ValidateSafeText(issues, receipt.SourceApplyDryRunRequestHash, nameof(receipt.SourceApplyDryRunRequestHash));
        ValidateSafeText(issues, receipt.DryRunResultHash, nameof(receipt.DryRunResultHash));
        ValidateSafeText(issues, receipt.SourceApplyRequestHash, nameof(receipt.SourceApplyRequestHash));
        ValidateSafeText(issues, receipt.SourceApplyGateEvaluationHash, nameof(receipt.SourceApplyGateEvaluationHash));
        ValidateSafeText(issues, receipt.PatchHash, nameof(receipt.PatchHash));
        ValidateSafeText(issues, receipt.ChangeSetHash, nameof(receipt.ChangeSetHash));
        ValidateSafeText(issues, receipt.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash));
        ValidateSafeText(issues, receipt.SourceBaselineHash, nameof(receipt.SourceBaselineHash));
        ValidateSafeText(issues, receipt.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash));
        ValidateSafeText(issues, receipt.ExpectedBranch, nameof(receipt.ExpectedBranch));
        ValidateSafeText(issues, receipt.ExpectedCleanWorktreeHash, nameof(receipt.ExpectedCleanWorktreeHash));
        ValidateSafeText(issues, receipt.SourceApplyDryRunReceiptHash, nameof(receipt.SourceApplyDryRunReceiptHash));
        ValidateSafeText(issues, receipt.Boundary, nameof(receipt.Boundary));
        ValidateSafeList(issues, receipt.EvidenceReferences, nameof(receipt.EvidenceReferences));
        ValidateSafeList(issues, receipt.BoundaryMaxims, nameof(receipt.BoundaryMaxims));
    }

    private static void ValidateSafeFileResultText(SourceApplyDryRunReceiptFileResult result, string fieldPrefix, List<SourceApplyDryRunReceiptValidationIssue> issues)
    {
        ValidateSafeText(issues, result.Path, $"{fieldPrefix}.{nameof(result.Path)}");
        ValidateSafeText(issues, result.PreviousPath, $"{fieldPrefix}.{nameof(result.PreviousPath)}");
        ValidateSafeText(issues, result.OperationKind, $"{fieldPrefix}.{nameof(result.OperationKind)}");
        ValidateSafeText(issues, result.PatchArtifactChangeHash, $"{fieldPrefix}.{nameof(result.PatchArtifactChangeHash)}");
        ValidateSafeText(issues, result.OperationHash, $"{fieldPrefix}.{nameof(result.OperationHash)}");
        ValidateSafeText(issues, result.ExpectedBeforeContentHash, $"{fieldPrefix}.{nameof(result.ExpectedBeforeContentHash)}");
        ValidateSafeText(issues, result.ExpectedAfterContentHash, $"{fieldPrefix}.{nameof(result.ExpectedAfterContentHash)}");
        ValidateSafeText(issues, result.ObservedCurrentContentHash, $"{fieldPrefix}.{nameof(result.ObservedCurrentContentHash)}");
        ValidateSafeText(issues, result.FileResultHash, $"{fieldPrefix}.{nameof(result.FileResultHash)}");
        ValidateSafeList(issues, result.IssueCodes, $"{fieldPrefix}.{nameof(result.IssueCodes)}");
    }

    private static void ValidatePath(List<SourceApplyDryRunReceiptValidationIssue> issues, string? value, string field)
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
            Add(issues, "FILE_RESULT_PATH_UNSAFE", field, "Source apply dry-run receipt file paths must be safe relative repository paths.");
        }
    }

    private static void RequireGuid(List<SourceApplyDryRunReceiptValidationIssue> issues, Guid value, string field, string code, string message)
    {
        if (value == Guid.Empty)
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(List<SourceApplyDryRunReceiptValidationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireList(List<SourceApplyDryRunReceiptValidationIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<SourceApplyDryRunReceiptValidationIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<SourceApplyDryRunReceiptValidationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Source apply dry-run receipt text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Source apply dry-run receipt text must not claim authority: {marker}.");
            }
        }
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<SourceApplyDryRunReceiptValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new SourceApplyDryRunReceiptValidationIssue(code, field, message));
}

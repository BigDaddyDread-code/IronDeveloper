namespace IronDev.Core.Governance;

public static class SourceApplyDryRunExecutor
{
    private const string RejectedText = "[rejected]";

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

    public static SourceApplyDryRunResult Execute(SourceApplyDryRunRequest? request)
    {
        var issues = new List<SourceApplyDryRunIssue>();
        var fileResults = new List<SourceApplyDryRunFileResult>();

        if (request is null)
        {
            Add(issues, "DRY_RUN_REQUEST_REQUIRED", "request", "Source apply dry-run request is required.");
            return BuildResult(null, null, issues, fileResults);
        }

        ValidateDryRunRequestShape(request, issues);
        ValidateTextSafety(request, issues);

        var sourceRequest = request.SourceApplyRequest;
        if (sourceRequest is null)
        {
            Add(issues, "SOURCE_APPLY_REQUEST_REQUIRED", nameof(request.SourceApplyRequest), "Source apply request is required.");
            return BuildResult(request, null, issues, fileResults);
        }

        var sourceValidation = SourceApplyRequestValidation.Validate(sourceRequest);
        foreach (var issue in sourceValidation.Issues)
        {
            Add(issues, "SOURCE_APPLY_REQUEST_INVALID", issue.Field, "Source apply request failed validation before dry-run evaluation.");
        }

        if (request.ProjectId != sourceRequest.ProjectId)
        {
            Add(issues, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Dry-run request project must match source apply request project.");
        }

        var snapshotFiles = ValidateWorkspaceSnapshot(request, sourceRequest, issues);

        if (sourceRequest.FileOperations is null || sourceRequest.FileOperations.Count == 0)
        {
            Add(issues, "FILE_OPERATIONS_REQUIRED", nameof(sourceRequest.FileOperations), "Source apply dry-run requires at least one file operation.");
        }
        else
        {
            foreach (var operation in sourceRequest.FileOperations)
            {
                var fileResult = EvaluateFileOperation(operation, snapshotFiles, sourceRequest.FileOperations);
                fileResults.Add(fileResult);
                issues.AddRange(fileResult.Issues);
            }
        }

        return BuildResult(request, sourceRequest, issues, fileResults);
    }

    private static void ValidateDryRunRequestShape(SourceApplyDryRunRequest request, List<SourceApplyDryRunIssue> issues)
    {
        RequireGuid(issues, request.SourceApplyDryRunRequestId, nameof(request.SourceApplyDryRunRequestId), "DRY_RUN_REQUEST_ID_REQUIRED", "Source apply dry-run request ID is required.");
        RequireGuid(issues, request.ProjectId, nameof(request.ProjectId), "PROJECT_ID_REQUIRED", "Project ID is required.");
        RequireList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "Dry-run evidence references are required.");
        RequireList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "Dry-run boundary maxims are required.");
        RequireText(issues, request.Boundary, nameof(request.Boundary), "BOUNDARY_REQUIRED", "Dry-run boundary text is required.");

        if (request.RequestedAtUtc == default)
        {
            Add(issues, "REQUESTED_AT_UTC_REQUIRED", nameof(request.RequestedAtUtc), "Dry-run request timestamp is required.");
        }
    }

    private static IReadOnlyDictionary<string, SourceApplyDryRunWorkspaceFile>? ValidateWorkspaceSnapshot(
        SourceApplyDryRunRequest request,
        SourceApplyRequest sourceRequest,
        List<SourceApplyDryRunIssue> issues)
    {
        var snapshot = request.WorkspaceSnapshot;
        if (snapshot is null)
        {
            return null;
        }

        RequireText(issues, snapshot.SourceBaselineHash, nameof(snapshot.SourceBaselineHash), "SNAPSHOT_SOURCE_BASELINE_HASH_REQUIRED", "Workspace snapshot source baseline hash is required.");
        RequireText(issues, snapshot.WorkspaceBoundaryHash, nameof(snapshot.WorkspaceBoundaryHash), "SNAPSHOT_WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace snapshot boundary hash is required.");
        RequireText(issues, snapshot.ExpectedBranch, nameof(snapshot.ExpectedBranch), "SNAPSHOT_EXPECTED_BRANCH_REQUIRED", "Workspace snapshot expected branch is required.");
        RequireText(issues, snapshot.ExpectedCleanWorktreeHash, nameof(snapshot.ExpectedCleanWorktreeHash), "SNAPSHOT_EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED", "Workspace snapshot expected clean worktree hash is required.");

        AddTextMismatch(issues, sourceRequest.SourceBaselineHash, snapshot.SourceBaselineHash, "SNAPSHOT_SOURCE_BASELINE_MISMATCH", nameof(snapshot.SourceBaselineHash), "Workspace snapshot source baseline hash must match source apply request evidence.");
        AddTextMismatch(issues, sourceRequest.WorkspaceBoundaryHash, snapshot.WorkspaceBoundaryHash, "SNAPSHOT_WORKSPACE_BOUNDARY_MISMATCH", nameof(snapshot.WorkspaceBoundaryHash), "Workspace snapshot boundary hash must match source apply request evidence.");
        AddTextMismatch(issues, sourceRequest.ExpectedBranch, snapshot.ExpectedBranch, "SNAPSHOT_EXPECTED_BRANCH_MISMATCH", nameof(snapshot.ExpectedBranch), "Workspace snapshot expected branch must match source apply request evidence.");
        AddTextMismatch(issues, sourceRequest.ExpectedCleanWorktreeHash, snapshot.ExpectedCleanWorktreeHash, "SNAPSHOT_EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH", nameof(snapshot.ExpectedCleanWorktreeHash), "Workspace snapshot expected clean worktree hash must match source apply request evidence.");

        ValidateSafeText(issues, snapshot.SourceBaselineHash, nameof(snapshot.SourceBaselineHash));
        ValidateSafeText(issues, snapshot.WorkspaceBoundaryHash, nameof(snapshot.WorkspaceBoundaryHash));
        ValidateSafeText(issues, snapshot.ExpectedBranch, nameof(snapshot.ExpectedBranch));
        ValidateSafeText(issues, snapshot.ExpectedCleanWorktreeHash, nameof(snapshot.ExpectedCleanWorktreeHash));

        if (snapshot.Files is null)
        {
            Add(issues, "SNAPSHOT_FILES_REQUIRED", nameof(snapshot.Files), "Workspace snapshot file list is required when a snapshot is supplied.");
            return new Dictionary<string, SourceApplyDryRunWorkspaceFile>(StringComparer.Ordinal);
        }

        var files = new Dictionary<string, SourceApplyDryRunWorkspaceFile>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Files.Count; index++)
        {
            var file = snapshot.Files[index];
            var prefix = $"{nameof(snapshot.Files)}[{index}]";
            RequireText(issues, file.Path, $"{prefix}.{nameof(file.Path)}", "SNAPSHOT_FILE_PATH_REQUIRED", "Workspace snapshot file path is required.");
            RequireText(issues, file.CurrentContentHash, $"{prefix}.{nameof(file.CurrentContentHash)}", "SNAPSHOT_FILE_HASH_REQUIRED", "Workspace snapshot file content hash is required.");
            ValidatePath(issues, file.Path, $"{prefix}.{nameof(file.Path)}");
            ValidateSafeText(issues, file.Path, $"{prefix}.{nameof(file.Path)}");
            ValidateSafeText(issues, file.CurrentContentHash, $"{prefix}.{nameof(file.CurrentContentHash)}");

            var key = Normalize(file.Path);
            if (!string.IsNullOrWhiteSpace(key) && !files.TryAdd(key, file))
            {
                Add(issues, "SNAPSHOT_FILE_DUPLICATE", nameof(snapshot.Files), "Workspace snapshot must not duplicate file paths.");
            }
        }

        return files;
    }

    private static SourceApplyDryRunFileResult EvaluateFileOperation(
        SourceApplyRequestFileOperation operation,
        IReadOnlyDictionary<string, SourceApplyDryRunWorkspaceFile>? snapshotFiles,
        IReadOnlyList<SourceApplyRequestFileOperation> allOperations)
    {
        var issues = new List<SourceApplyDryRunIssue>();
        var fieldPrefix = nameof(SourceApplyRequest.FileOperations);

        RequireText(issues, operation.Path, $"{fieldPrefix}.{nameof(operation.Path)}", "FILE_OPERATION_PATH_REQUIRED", "File operation path is required.");
        RequireText(issues, operation.OperationKind, $"{fieldPrefix}.{nameof(operation.OperationKind)}", "FILE_OPERATION_KIND_REQUIRED", "File operation kind is required.");
        RequireText(issues, operation.OperationHash, $"{fieldPrefix}.{nameof(operation.OperationHash)}", "FILE_OPERATION_HASH_REQUIRED", "File operation hash is required.");
        RequireText(issues, operation.PatchArtifactChangeHash, $"{fieldPrefix}.{nameof(operation.PatchArtifactChangeHash)}", "PATCH_ARTIFACT_CHANGE_HASH_REQUIRED", "Patch artifact change hash is required.");
        ValidatePath(issues, operation.Path, $"{fieldPrefix}.{nameof(operation.Path)}");
        ValidateSafeFileOperationText(operation, issues, fieldPrefix);

        if (!SourceApplyRequestFileOperationKinds.Known.Contains(operation.OperationKind, StringComparer.Ordinal))
        {
            Add(issues, "FILE_OPERATION_KIND_INVALID", $"{fieldPrefix}.{nameof(operation.OperationKind)}", "File operation kind must be CreateFile, ModifyFile, DeleteFile, RenameFile, or Noop.");
        }

        if (allOperations.Count(candidate => StringEquals(candidate.Path, operation.Path) && StringEquals(candidate.PatchArtifactChangeHash, operation.PatchArtifactChangeHash)) > 1)
        {
            Add(issues, "FILE_OPERATION_DUPLICATE", fieldPrefix, "Dry-run file operations must not duplicate the same path and patch artifact change hash.");
        }

        var targetFile = TryGetSnapshotFile(snapshotFiles, operation.Path);
        var previousFile = TryGetSnapshotFile(snapshotFiles, operation.PreviousPath);

        switch (operation.OperationKind)
        {
            case SourceApplyRequestFileOperationKinds.CreateFile:
                RequireText(issues, operation.AfterContentHash, $"{fieldPrefix}.{nameof(operation.AfterContentHash)}", "AFTER_CONTENT_HASH_REQUIRED", "CreateFile dry-run requires an after-content hash.");
                if (targetFile?.Exists == true)
                {
                    Add(issues, "CREATE_TARGET_ALREADY_EXISTS", nameof(operation.Path), "CreateFile dry-run requires the target file to be absent in the supplied snapshot.");
                }
                break;

            case SourceApplyRequestFileOperationKinds.ModifyFile:
                RequireText(issues, operation.BeforeContentHash, $"{fieldPrefix}.{nameof(operation.BeforeContentHash)}", "BEFORE_CONTENT_HASH_REQUIRED", "ModifyFile dry-run requires a before-content hash.");
                RequireText(issues, operation.AfterContentHash, $"{fieldPrefix}.{nameof(operation.AfterContentHash)}", "AFTER_CONTENT_HASH_REQUIRED", "ModifyFile dry-run requires an after-content hash.");
                if (snapshotFiles is not null)
                {
                    if (targetFile is null || !targetFile.Exists)
                    {
                        Add(issues, "MODIFY_TARGET_MISSING", nameof(operation.Path), "ModifyFile dry-run requires the target file to exist in the supplied snapshot.");
                    }
                    else if (!StringEquals(targetFile.CurrentContentHash, operation.BeforeContentHash))
                    {
                        Add(issues, "CURRENT_FILE_HASH_MISMATCH", nameof(operation.BeforeContentHash), "ModifyFile dry-run requires current content hash to match the before-content hash.");
                    }
                }
                break;

            case SourceApplyRequestFileOperationKinds.DeleteFile:
                RequireText(issues, operation.BeforeContentHash, $"{fieldPrefix}.{nameof(operation.BeforeContentHash)}", "BEFORE_CONTENT_HASH_REQUIRED", "DeleteFile dry-run requires a before-content hash.");
                if (snapshotFiles is not null)
                {
                    if (targetFile is null || !targetFile.Exists)
                    {
                        Add(issues, "DELETE_TARGET_MISSING", nameof(operation.Path), "DeleteFile dry-run requires the target file to exist in the supplied snapshot.");
                    }
                    else if (!StringEquals(targetFile.CurrentContentHash, operation.BeforeContentHash))
                    {
                        Add(issues, "CURRENT_FILE_HASH_MISMATCH", nameof(operation.BeforeContentHash), "DeleteFile dry-run requires current content hash to match the before-content hash.");
                    }
                }
                break;

            case SourceApplyRequestFileOperationKinds.RenameFile:
                RequireText(issues, operation.PreviousPath, $"{fieldPrefix}.{nameof(operation.PreviousPath)}", "PREVIOUS_PATH_REQUIRED", "RenameFile dry-run requires a previous path.");
                ValidatePath(issues, operation.PreviousPath, $"{fieldPrefix}.{nameof(operation.PreviousPath)}");
                if (snapshotFiles is not null)
                {
                    if (previousFile is null || !previousFile.Exists)
                    {
                        Add(issues, "RENAME_SOURCE_MISSING", nameof(operation.PreviousPath), "RenameFile dry-run requires the previous file to exist in the supplied snapshot.");
                    }

                    if (targetFile?.Exists == true)
                    {
                        Add(issues, "RENAME_TARGET_ALREADY_EXISTS", nameof(operation.Path), "RenameFile dry-run requires the target file to be absent in the supplied snapshot.");
                    }
                }
                break;

            case SourceApplyRequestFileOperationKinds.Noop:
                break;
        }

        return new SourceApplyDryRunFileResult
        {
            Path = SafeOutputText(operation.Path) ?? string.Empty,
            PreviousPath = SafeOutputText(operation.PreviousPath),
            OperationKind = SafeOutputText(operation.OperationKind) ?? string.Empty,
            PatchArtifactChangeHash = SafeOutputText(operation.PatchArtifactChangeHash) ?? string.Empty,
            OperationHash = SafeOutputText(operation.OperationHash) ?? string.Empty,
            ExpectedBeforeContentHash = SafeOutputText(operation.BeforeContentHash),
            ExpectedAfterContentHash = SafeOutputText(operation.AfterContentHash),
            ObservedCurrentContentHash = SafeOutputText((targetFile ?? previousFile)?.CurrentContentHash),
            PreconditionsSatisfied = issues.Count == 0,
            WouldCreate = operation.OperationKind == SourceApplyRequestFileOperationKinds.CreateFile,
            WouldModify = operation.OperationKind == SourceApplyRequestFileOperationKinds.ModifyFile,
            WouldDelete = operation.OperationKind == SourceApplyRequestFileOperationKinds.DeleteFile,
            WouldRename = operation.OperationKind == SourceApplyRequestFileOperationKinds.RenameFile,
            WouldNoop = operation.OperationKind == SourceApplyRequestFileOperationKinds.Noop,
            Issues = issues
        };
    }

    private static SourceApplyDryRunWorkspaceFile? TryGetSnapshotFile(IReadOnlyDictionary<string, SourceApplyDryRunWorkspaceFile>? snapshotFiles, string? path)
    {
        if (snapshotFiles is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return snapshotFiles.TryGetValue(Normalize(path), out var file) ? file : null;
    }

    private static SourceApplyDryRunResult BuildResult(
        SourceApplyDryRunRequest? request,
        SourceApplyRequest? sourceRequest,
        List<SourceApplyDryRunIssue> issues,
        List<SourceApplyDryRunFileResult> fileResults)
    {
        var safeIssues = issues.Select(issue => issue with { Message = SafeIssueMessage(issue.Message) }).ToArray();
        var safeFileResults = fileResults.Select(result => result with
        {
            Issues = result.Issues.Select(issue => issue with { Message = SafeIssueMessage(issue.Message) }).ToArray()
        }).ToArray();

        return new SourceApplyDryRunResult
        {
            Satisfied = safeIssues.Length == 0 && safeFileResults.All(result => result.PreconditionsSatisfied),
            SourceApplyDryRunRequestId = request?.SourceApplyDryRunRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? sourceRequest?.ProjectId ?? Guid.Empty,
            SourceApplyRequestId = sourceRequest?.SourceApplyRequestId ?? Guid.Empty,
            SourceApplyRequestHash = SafeOutputText(sourceRequest?.SourceApplyRequestHash) ?? string.Empty,
            SourceApplyGateEvaluationId = sourceRequest?.SourceApplyGateEvaluationId ?? Guid.Empty,
            SourceApplyGateEvaluationHash = SafeOutputText(sourceRequest?.SourceApplyGateEvaluationHash) ?? string.Empty,
            PatchArtifactId = sourceRequest?.PatchArtifactId ?? Guid.Empty,
            PatchHash = SafeOutputText(sourceRequest?.PatchHash) ?? string.Empty,
            ChangeSetHash = SafeOutputText(sourceRequest?.ChangeSetHash) ?? string.Empty,
            RollbackSupportReceiptId = sourceRequest?.RollbackSupportReceiptId ?? Guid.Empty,
            RollbackSupportReceiptHash = SafeOutputText(sourceRequest?.RollbackSupportReceiptHash) ?? string.Empty,
            SourceBaselineHash = SafeOutputText(sourceRequest?.SourceBaselineHash) ?? string.Empty,
            WorkspaceBoundaryHash = SafeOutputText(sourceRequest?.WorkspaceBoundaryHash) ?? string.Empty,
            ExpectedBranch = SafeOutputText(sourceRequest?.ExpectedBranch) ?? string.Empty,
            ExpectedCleanWorktreeHash = SafeOutputText(sourceRequest?.ExpectedCleanWorktreeHash) ?? string.Empty,
            FileResults = safeFileResults,
            Issues = safeIssues,
            EvidenceReferences = MergeSafeLists(request?.EvidenceReferences, sourceRequest?.EvidenceReferences, sourceRequest?.SourceApplyGateEvaluation?.EvidenceReferences),
            BoundaryMaxims = MergeSafeLists(request?.BoundaryMaxims, sourceRequest?.BoundaryMaxims, sourceRequest?.SourceApplyGateEvaluation?.BoundaryMaxims),
            Boundary = SourceApplyDryRunBoundaryText.Boundary
        };
    }

    private static IReadOnlyList<string> MergeSafeLists(params IReadOnlyList<string>?[] lists) =>
        lists
            .Where(list => list is not null)
            .SelectMany(list => list!)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => SafeOutputText(value) ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != RejectedText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void ValidateTextSafety(SourceApplyDryRunRequest request, List<SourceApplyDryRunIssue> issues)
    {
        ValidateSafeText(issues, request.Boundary, nameof(request.Boundary));
        ValidateSafeList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences));
        ValidateSafeList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims));
    }

    private static void ValidateSafeFileOperationText(SourceApplyRequestFileOperation operation, List<SourceApplyDryRunIssue> issues, string fieldPrefix)
    {
        ValidateSafeText(issues, operation.Path, $"{fieldPrefix}.{nameof(operation.Path)}");
        ValidateSafeText(issues, operation.OperationKind, $"{fieldPrefix}.{nameof(operation.OperationKind)}");
        ValidateSafeText(issues, operation.PreviousPath, $"{fieldPrefix}.{nameof(operation.PreviousPath)}");
        ValidateSafeText(issues, operation.BeforeContentHash, $"{fieldPrefix}.{nameof(operation.BeforeContentHash)}");
        ValidateSafeText(issues, operation.AfterContentHash, $"{fieldPrefix}.{nameof(operation.AfterContentHash)}");
        ValidateSafeText(issues, operation.DiffHash, $"{fieldPrefix}.{nameof(operation.DiffHash)}");
        ValidateSafeText(issues, operation.PatchArtifactChangeHash, $"{fieldPrefix}.{nameof(operation.PatchArtifactChangeHash)}");
        ValidateSafeText(issues, operation.OperationHash, $"{fieldPrefix}.{nameof(operation.OperationHash)}");
    }

    private static void ValidatePath(List<SourceApplyDryRunIssue> issues, string? value, string field)
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
            Add(issues, "FILE_OPERATION_PATH_UNSAFE", field, "Dry-run file paths must be safe relative repository paths.");
        }
    }

    private static void RequireGuid(List<SourceApplyDryRunIssue> issues, Guid value, string field, string code, string message)
    {
        if (value == Guid.Empty)
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireText(List<SourceApplyDryRunIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireList(List<SourceApplyDryRunIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<SourceApplyDryRunIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<SourceApplyDryRunIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (ContainsPrivateOrRawMaterial(value))
        {
            Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, "Dry-run text must not contain private or raw material.");
        }

        if (ContainsAuthorityClaim(value))
        {
            Add(issues, "AUTHORITY_CLAIM_REJECTED", field, "Dry-run text must not claim source apply, rollback, workflow, or release authority.");
        }
    }

    private static string SafeIssueMessage(string value) =>
        ContainsPrivateOrRawMaterial(value) || ContainsAuthorityClaim(value)
            ? "Dry-run issue contains rejected unsafe material."
            : value;

    private static string? SafeOutputText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : ContainsPrivateOrRawMaterial(value) || ContainsAuthorityClaim(value)
                ? RejectedText
                : value.Trim();

    private static bool ContainsPrivateOrRawMaterial(string value) =>
        PrivateMaterialMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAuthorityClaim(string value) =>
        AuthorityClaimMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void AddTextMismatch(List<SourceApplyDryRunIssue> issues, string? expected, string? actual, string code, string field, string message)
    {
        if (!StringEquals(expected, actual))
        {
            Add(issues, code, field, message);
        }
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<SourceApplyDryRunIssue> issues, string code, string field, string message) =>
        issues.Add(new SourceApplyDryRunIssue(code, field, message));
}

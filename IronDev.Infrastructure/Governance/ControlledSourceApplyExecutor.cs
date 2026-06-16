using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ControlledSourceApplyExecutor : IControlledSourceApplyExecutor
{
    private readonly ISourceApplyReceiptStore _receiptStore;

    public ControlledSourceApplyExecutor(ISourceApplyReceiptStore receiptStore) =>
        _receiptStore = receiptStore;

    public async Task<ControlledSourceApplyResult> ApplyAsync(ControlledSourceApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = ValidateEvidenceChain(request);
        var preflight = PreflightWorkspace(request, issues);
        if (issues.Count > 0)
        {
            return Rejected(issues, preflight.FileResults);
        }

        var fileResults = new List<SourceApplyReceiptFileResult>();
        var mutationOccurred = false;
        var partial = false;
        var issueCodes = new List<string>();

        try
        {
            foreach (var operation in OrderedOperations(request.SourceApplyRequest.FileOperations))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var planned = preflight.PlannedOperations.Single(plan => ReferenceEquals(plan.Operation, operation));
                var result = await ApplyOperationAsync(planned, cancellationToken);
                fileResults.Add(result);
                mutationOccurred |= result.MutationApplied;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            partial = mutationOccurred;
            issueCodes.Add("ApplyFailedAfterMutationStarted");
            issues.Add(new("ApplyFailed", nameof(ControlledSourceApplyRequest.WorkspaceRoot), ex.Message));
        }

        var receipt = BuildReceipt(
            request,
            mutationOccurred,
            applySucceeded: issues.Count == 0,
            partialApplyOccurred: partial,
            fileResults.Count == 0 ? preflight.FileResults : fileResults,
            issueCodes.Concat(issues.Select(issue => issue.Code)).Distinct(StringComparer.Ordinal).ToArray());

        await _receiptStore.SaveAsync(receipt, cancellationToken);

        return new ControlledSourceApplyResult
        {
            Status = receipt.ApplySucceeded ? ControlledSourceApplyStatuses.Applied : ControlledSourceApplyStatuses.PartialFailure,
            Succeeded = receipt.ApplySucceeded,
            MutationOccurred = receipt.MutationOccurred,
            PartialApplyOccurred = receipt.PartialApplyOccurred,
            Issues = issues,
            FileResults = receipt.FileResults,
            Receipt = receipt
        };
    }

    private static List<ControlledSourceApplyIssue> ValidateEvidenceChain(ControlledSourceApplyRequest request)
    {
        var issues = new List<ControlledSourceApplyIssue>();

        AddValidationIssues(SourceApplyRequestValidation.Validate(request.SourceApplyRequest).Issues, issues, "SourceApplyRequest");
        AddValidationIssues(SourceApplyDryRunReceiptValidation.Validate(request.SourceApplyDryRunReceipt).Issues, issues, "SourceApplyDryRunReceipt");
        AddValidationIssues(PatchArtifactValidation.Validate(request.PatchArtifact).Issues, issues, "PatchArtifact");
        AddValidationIssues(RollbackSupportReceiptValidation.Validate(request.RollbackSupportReceipt).Issues, issues, "RollbackSupportReceipt");

        RequireSame(request.ProjectId, request.SourceApplyRequest.ProjectId, nameof(request.SourceApplyRequest.ProjectId), issues);
        RequireSame(request.ProjectId, request.SourceApplyDryRunReceipt.ProjectId, nameof(request.SourceApplyDryRunReceipt.ProjectId), issues);
        RequireSame(request.ProjectId, request.PatchArtifact.ProjectId, nameof(request.PatchArtifact.ProjectId), issues);
        RequireSame(request.ProjectId, request.RollbackSupportReceipt.ProjectId, nameof(request.RollbackSupportReceipt.ProjectId), issues);

        if (!request.SourceApplyRequest.SourceApplyGateSatisfied)
        {
            issues.Add(new("SourceApplyGateNotSatisfied", nameof(request.SourceApplyRequest.SourceApplyGateSatisfied), "Source apply gate must be satisfied before real apply."));
        }

        if (!request.SourceApplyDryRunReceipt.DryRunSatisfied)
        {
            issues.Add(new("DryRunNotSatisfied", nameof(request.SourceApplyDryRunReceipt.DryRunSatisfied), "Source apply dry-run receipt must be satisfied before real apply."));
        }

        if (request.SourceApplyDryRunReceipt.ExpiresAtUtc is { } dryRunExpiry && dryRunExpiry <= request.RequestedAtUtc)
        {
            issues.Add(new("DryRunReceiptExpired", nameof(request.SourceApplyDryRunReceipt.ExpiresAtUtc), "Source apply dry-run receipt is expired."));
        }

        if (request.RollbackSupportReceipt.ExpiresAtUtc is { } rollbackExpiry && rollbackExpiry <= request.RequestedAtUtc)
        {
            issues.Add(new("RollbackSupportExpired", nameof(request.RollbackSupportReceipt.ExpiresAtUtc), "Rollback support receipt is expired."));
        }

        if (request.PatchArtifact.ExpiresAtUtc is { } patchExpiry && patchExpiry <= request.RequestedAtUtc)
        {
            issues.Add(new("PatchArtifactExpired", nameof(request.PatchArtifact.ExpiresAtUtc), "Patch artifact is expired."));
        }

        RequireEqual(request.SourceApplyRequest.SourceApplyRequestId, request.SourceApplyDryRunReceipt.SourceApplyRequestId, "SourceApplyRequestId", issues);
        RequireEqual(request.SourceApplyRequest.SourceApplyRequestHash, request.SourceApplyDryRunReceipt.SourceApplyRequestHash, "SourceApplyRequestHash", issues);
        RequireEqual(request.SourceApplyRequest.SourceApplyGateEvaluationId, request.SourceApplyDryRunReceipt.SourceApplyGateEvaluationId, "SourceApplyGateEvaluationId", issues);
        RequireEqual(request.SourceApplyRequest.SourceApplyGateEvaluationHash, request.SourceApplyDryRunReceipt.SourceApplyGateEvaluationHash, "SourceApplyGateEvaluationHash", issues);
        RequireEqual(request.SourceApplyRequest.PatchArtifactId, request.PatchArtifact.PatchArtifactId, "PatchArtifactId", issues);
        RequireEqual(request.SourceApplyRequest.PatchHash, request.PatchArtifact.PatchHash, "PatchHash", issues);
        RequireEqual(request.SourceApplyRequest.ChangeSetHash, request.PatchArtifact.ChangeSetHash, "ChangeSetHash", issues);
        RequireEqual(request.SourceApplyRequest.RollbackSupportReceiptId, request.RollbackSupportReceipt.RollbackSupportReceiptId, "RollbackSupportReceiptId", issues);
        RequireEqual(request.SourceApplyRequest.RollbackSupportReceiptHash, request.RollbackSupportReceipt.RollbackSupportReceiptHash, "RollbackSupportReceiptHash", issues);
        RequireEqual(request.SourceApplyRequest.SourceBaselineHash, request.SourceApplyDryRunReceipt.SourceBaselineHash, "SourceBaselineHash", issues);
        RequireEqual(request.SourceApplyRequest.SourceBaselineHash, request.PatchArtifact.SourceBaselineHash, "PatchSourceBaselineHash", issues);
        RequireEqual(request.SourceApplyRequest.SourceBaselineHash, request.RollbackSupportReceipt.SourceBaselineHash, "RollbackSourceBaselineHash", issues);
        RequireEqual(request.SourceApplyRequest.WorkspaceBoundaryHash, request.ApprovedWorkspaceBoundaryHash, "ApprovedWorkspaceBoundaryHash", issues);
        RequireEqual(request.SourceApplyRequest.WorkspaceBoundaryHash, request.SourceApplyDryRunReceipt.WorkspaceBoundaryHash, "DryRunWorkspaceBoundaryHash", issues);
        RequireEqual(request.SourceApplyRequest.WorkspaceBoundaryHash, request.PatchArtifact.WorkspaceBoundaryHash, "PatchWorkspaceBoundaryHash", issues);
        RequireEqual(request.SourceApplyRequest.WorkspaceBoundaryHash, request.RollbackSupportReceipt.WorkspaceBoundaryHash, "RollbackWorkspaceBoundaryHash", issues);
        RequireEqual(request.SourceApplyRequest.ExpectedBranch, request.ObservedBranch, "ObservedBranch", issues);
        RequireEqual(request.SourceApplyRequest.ExpectedCleanWorktreeHash, request.ObservedCleanWorktreeHashBeforeApply, "ObservedCleanWorktreeHashBeforeApply", issues);
        RequireEqual(request.SourceApplyRequest.SourceBaselineHash, request.ObservedSourceBaselineHash, "ObservedSourceBaselineHash", issues);
        RequireEqual(request.SourceApplyDryRunReceipt.SourceApplyDryRunReceiptHash, request.PatchArtifact.DryRunReceiptHash, "PatchDryRunReceiptHash", issues);
        RequireEqual(request.SourceApplyDryRunReceipt.SourceApplyDryRunReceiptHash, request.RollbackSupportReceipt.DryRunReceiptHash, "RollbackDryRunReceiptHash", issues);
        RequireEqual(request.SourceApplyRequest.PolicySatisfactionId, request.PatchArtifact.PolicySatisfactionId, "PolicySatisfactionId", issues);
        RequireEqual(request.SourceApplyRequest.PolicySatisfactionHash, request.PatchArtifact.PolicySatisfactionHash, "PolicySatisfactionHash", issues);

        return issues;
    }

    private static PreflightResult PreflightWorkspace(ControlledSourceApplyRequest request, List<ControlledSourceApplyIssue> issues)
    {
        var fileResults = new List<SourceApplyReceiptFileResult>();
        var planned = new List<PlannedSourceApplyOperation>();
        var root = NormalizeRoot(request.WorkspaceRoot, issues);
        if (root is null)
        {
            return new(fileResults, planned);
        }

        var contentByPath = request.ApprovedContents
            .GroupBy(content => NormalizePathKey(content.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var operation in OrderedOperations(request.SourceApplyRequest.FileOperations))
        {
            var operationIssues = new List<string>();
            var target = ResolvePath(root, operation.Path, operationIssues);
            var previous = string.IsNullOrWhiteSpace(operation.PreviousPath)
                ? null
                : ResolvePath(root, operation.PreviousPath!, operationIssues);

            var patchChange = FindPatchChange(request.PatchArtifact, operation);
            if (patchChange is null && !IsNoop(operation))
            {
                operationIssues.Add("PatchArtifactChangeMissing");
            }

            if (patchChange is { IsBinary: true })
            {
                operationIssues.Add("BinaryPatchNotSupported");
            }

            var dryRun = FindDryRunResult(request.SourceApplyDryRunReceipt, operation);
            if (dryRun is null)
            {
                operationIssues.Add("DryRunFileResultMissing");
            }
            else if (!dryRun.PreconditionsSatisfied)
            {
                operationIssues.Add("DryRunPreconditionsNotSatisfied");
            }

            if (patchChange is not null)
            {
                CompareNullable(operation.BeforeContentHash, patchChange.BeforeContentHash, operationIssues, "PatchBeforeHashMismatch");
                CompareNullable(operation.AfterContentHash, patchChange.AfterContentHash, operationIssues, "PatchAfterHashMismatch");
                CompareNullable(operation.DiffHash, patchChange.DiffHash, operationIssues, "PatchDiffHashMismatch");
            }

            var beforeHash = target is not null && File.Exists(target) ? HashFile(target) : null;
            ControlledSourceApplyContent? content = null;

            switch (operation.OperationKind)
            {
                case SourceApplyRequestFileOperationKinds.CreateFile:
                    if (target is not null && File.Exists(target)) operationIssues.Add("TargetAlreadyExists");
                    content = RequireContent(operation, contentByPath, operationIssues);
                    break;
                case SourceApplyRequestFileOperationKinds.ModifyFile:
                    if (target is null || !File.Exists(target)) operationIssues.Add("TargetMissing");
                    CompareNullable(operation.BeforeContentHash, beforeHash, operationIssues, "BeforeHashMismatch");
                    content = RequireContent(operation, contentByPath, operationIssues);
                    break;
                case SourceApplyRequestFileOperationKinds.DeleteFile:
                    if (target is null || !File.Exists(target)) operationIssues.Add("TargetMissing");
                    CompareNullable(operation.BeforeContentHash, beforeHash, operationIssues, "BeforeHashMismatch");
                    break;
                case SourceApplyRequestFileOperationKinds.RenameFile:
                    if (previous is null || !File.Exists(previous)) operationIssues.Add("PreviousPathMissing");
                    if (target is not null && File.Exists(target)) operationIssues.Add("TargetAlreadyExists");
                    var previousHash = previous is not null && File.Exists(previous) ? HashFile(previous) : null;
                    CompareNullable(operation.BeforeContentHash, previousHash, operationIssues, "BeforeHashMismatch");
                    CompareNullable(operation.AfterContentHash, previousHash, operationIssues, "AfterHashMismatch");
                    beforeHash = previousHash;
                    break;
                case SourceApplyRequestFileOperationKinds.Noop:
                    break;
                default:
                    operationIssues.Add("UnsupportedOperationKind");
                    break;
            }

            var afterHash = content?.AfterContentHash ?? operation.AfterContentHash ?? beforeHash;
            var result = BuildFileResult(operation, beforeHash, afterHash, preconditionsSatisfied: operationIssues.Count == 0, mutationApplied: false, operationIssues);
            fileResults.Add(result);
            planned.Add(new(operation, target, previous, content, result));

            foreach (var issue in operationIssues)
            {
                issues.Add(new(issue, operation.Path, $"Source apply operation failed preflight: {issue}."));
            }
        }

        return new(fileResults, planned);
    }

    private static async Task<SourceApplyReceiptFileResult> ApplyOperationAsync(PlannedSourceApplyOperation plan, CancellationToken cancellationToken)
    {
        var operation = plan.Operation;
        var issues = new List<string>();
        string? afterHash = plan.PreflightResult.AfterContentHash;
        var mutationApplied = false;
        var created = false;
        var modified = false;
        var deleted = false;
        var renamed = false;
        var noop = false;

        switch (operation.OperationKind)
        {
            case SourceApplyRequestFileOperationKinds.CreateFile:
                Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath!)!);
                await File.WriteAllTextAsync(plan.TargetPath!, plan.Content!.Content, Encoding.UTF8, cancellationToken);
                afterHash = HashFile(plan.TargetPath!);
                mutationApplied = created = true;
                break;
            case SourceApplyRequestFileOperationKinds.ModifyFile:
                await File.WriteAllTextAsync(plan.TargetPath!, plan.Content!.Content, Encoding.UTF8, cancellationToken);
                afterHash = HashFile(plan.TargetPath!);
                mutationApplied = modified = true;
                break;
            case SourceApplyRequestFileOperationKinds.DeleteFile:
                File.Delete(plan.TargetPath!);
                afterHash = null;
                mutationApplied = deleted = true;
                break;
            case SourceApplyRequestFileOperationKinds.RenameFile:
                Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath!)!);
                File.Move(plan.PreviousPath!, plan.TargetPath!);
                afterHash = HashFile(plan.TargetPath!);
                mutationApplied = renamed = true;
                break;
            case SourceApplyRequestFileOperationKinds.Noop:
                noop = true;
                break;
        }

        var result = plan.PreflightResult with
        {
            AfterContentHash = afterHash,
            PreconditionsSatisfied = true,
            MutationApplied = mutationApplied,
            Created = created,
            Modified = modified,
            Deleted = deleted,
            Renamed = renamed,
            Noop = noop,
            IssueCodes = issues
        };

        return result with { FileResultHash = SourceApplyReceiptHashing.ComputeFileResultHash(result) };
    }

    private static SourceApplyReceipt BuildReceipt(
        ControlledSourceApplyRequest request,
        bool mutationOccurred,
        bool applySucceeded,
        bool partialApplyOccurred,
        IReadOnlyList<SourceApplyReceiptFileResult> fileResults,
        IReadOnlyList<string> issueCodes)
    {
        var appliedAt = DateTimeOffset.UtcNow;
        var afterHash = mutationOccurred
            ? SourceApplyReceiptHashing.ComputeContentHash(string.Join("\n", fileResults.Select(result => result.FileResultHash).Order(StringComparer.Ordinal)))
            : request.ObservedCleanWorktreeHashBeforeApply;

        var receipt = new SourceApplyReceipt
        {
            SourceApplyReceiptId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            ControlledSourceApplyRequestId = request.ControlledSourceApplyRequestId,
            SourceApplyRequestId = request.SourceApplyRequest.SourceApplyRequestId,
            SourceApplyRequestHash = request.SourceApplyRequest.SourceApplyRequestHash,
            SourceApplyDryRunReceiptId = request.SourceApplyDryRunReceipt.SourceApplyDryRunReceiptId,
            SourceApplyDryRunReceiptHash = request.SourceApplyDryRunReceipt.SourceApplyDryRunReceiptHash,
            SourceApplyGateEvaluationId = request.SourceApplyRequest.SourceApplyGateEvaluationId,
            SourceApplyGateEvaluationHash = request.SourceApplyRequest.SourceApplyGateEvaluationHash,
            PatchArtifactId = request.PatchArtifact.PatchArtifactId,
            PatchHash = request.PatchArtifact.PatchHash,
            ChangeSetHash = request.PatchArtifact.ChangeSetHash,
            RollbackSupportReceiptId = request.RollbackSupportReceipt.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = request.RollbackSupportReceipt.RollbackSupportReceiptHash,
            SourceBaselineHash = request.SourceApplyRequest.SourceBaselineHash,
            WorkspaceBoundaryHash = request.SourceApplyRequest.WorkspaceBoundaryHash,
            ExpectedBranch = request.SourceApplyRequest.ExpectedBranch,
            ExpectedCleanWorktreeHash = request.SourceApplyRequest.ExpectedCleanWorktreeHash,
            ObservedBranch = request.ObservedBranch,
            ObservedCleanWorktreeHashBeforeApply = request.ObservedCleanWorktreeHashBeforeApply,
            ObservedCleanWorktreeHashAfterApply = afterHash,
            MutationOccurred = mutationOccurred,
            ApplySucceeded = applySucceeded,
            PartialApplyOccurred = partialApplyOccurred,
            FileResults = fileResults,
            IssueCodes = issueCodes,
            AppliedAtUtc = appliedAt,
            SourceApplyReceiptHash = "sha256:pending",
            EvidenceReferences = request.EvidenceReferences.Concat(request.SourceApplyRequest.EvidenceReferences).Distinct(StringComparer.Ordinal).ToArray(),
            BoundaryMaxims = request.BoundaryMaxims.Concat(request.SourceApplyRequest.BoundaryMaxims).Distinct(StringComparer.Ordinal).ToArray(),
            Boundary = SourceApplyReceiptBoundaryText.Boundary
        };

        return receipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(receipt) };
    }

    private static ControlledSourceApplyResult Rejected(IReadOnlyList<ControlledSourceApplyIssue> issues, IReadOnlyList<SourceApplyReceiptFileResult> fileResults) => new()
    {
        Status = ControlledSourceApplyStatuses.Rejected,
        Succeeded = false,
        MutationOccurred = false,
        PartialApplyOccurred = false,
        Issues = issues,
        FileResults = fileResults,
        Receipt = null
    };

    private static SourceApplyReceiptFileResult BuildFileResult(
        SourceApplyRequestFileOperation operation,
        string? beforeHash,
        string? afterHash,
        bool preconditionsSatisfied,
        bool mutationApplied,
        IReadOnlyList<string> issueCodes)
    {
        var result = new SourceApplyReceiptFileResult
        {
            Path = Normalize(operation.Path),
            PreviousPath = NormalizeNullable(operation.PreviousPath),
            OperationKind = Normalize(operation.OperationKind),
            PatchArtifactChangeHash = Normalize(operation.PatchArtifactChangeHash),
            OperationHash = Normalize(operation.OperationHash),
            BeforeContentHash = NormalizeNullable(beforeHash),
            AfterContentHash = NormalizeNullable(afterHash),
            PreconditionsSatisfied = preconditionsSatisfied,
            MutationApplied = mutationApplied,
            Created = false,
            Modified = false,
            Deleted = false,
            Renamed = false,
            Noop = operation.OperationKind == SourceApplyRequestFileOperationKinds.Noop,
            IssueCodes = issueCodes.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray(),
            FileResultHash = "sha256:pending"
        };

        return result with { FileResultHash = SourceApplyReceiptHashing.ComputeFileResultHash(result) };
    }

    private static ControlledSourceApplyContent? RequireContent(SourceApplyRequestFileOperation operation, IReadOnlyDictionary<string, ControlledSourceApplyContent> contentByPath, List<string> issues)
    {
        if (!contentByPath.TryGetValue(NormalizePathKey(operation.Path), out var content))
        {
            issues.Add("ApprovedContentMissing");
            return null;
        }

        var actualHash = SourceApplyReceiptHashing.ComputeContentHash(content.Content);
        CompareNullable(operation.AfterContentHash, content.AfterContentHash, issues, "ContentHashReferenceMismatch");
        CompareNullable(operation.AfterContentHash, actualHash, issues, "ContentHashMismatch");
        return content with { AfterContentHash = Normalize(content.AfterContentHash) };
    }

    private static PatchArtifactFileChange? FindPatchChange(PatchArtifact artifact, SourceApplyRequestFileOperation operation) =>
        artifact.FileChanges.FirstOrDefault(change =>
            string.Equals(Normalize(change.Path), Normalize(operation.Path), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(PatchArtifactHashing.ComputeFileChangeHash(change), Normalize(operation.PatchArtifactChangeHash), StringComparison.Ordinal));

    private static SourceApplyDryRunReceiptFileResult? FindDryRunResult(SourceApplyDryRunReceipt receipt, SourceApplyRequestFileOperation operation) =>
        receipt.FileResults.FirstOrDefault(result =>
            string.Equals(Normalize(result.Path), Normalize(operation.Path), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Normalize(result.OperationKind), Normalize(operation.OperationKind), StringComparison.Ordinal) &&
            string.Equals(Normalize(result.PatchArtifactChangeHash), Normalize(operation.PatchArtifactChangeHash), StringComparison.Ordinal));

    private static IEnumerable<SourceApplyRequestFileOperation> OrderedOperations(IEnumerable<SourceApplyRequestFileOperation> operations) =>
        operations
            .OrderBy(operation => Normalize(operation.Path), StringComparer.Ordinal)
            .ThenBy(operation => Normalize(operation.OperationKind), StringComparer.Ordinal)
            .ThenBy(operation => Normalize(operation.PatchArtifactChangeHash), StringComparer.Ordinal);

    private static string? NormalizeRoot(string workspaceRoot, List<ControlledSourceApplyIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            issues.Add(new("WorkspaceRootRequired", nameof(ControlledSourceApplyRequest.WorkspaceRoot), "Workspace root is required."));
            return null;
        }

        var root = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(root))
        {
            issues.Add(new("WorkspaceRootMissing", nameof(ControlledSourceApplyRequest.WorkspaceRoot), "Workspace root must exist."));
            return null;
        }

        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static string? ResolvePath(string root, string relativePath, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal) || relativePath.Contains(".git", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("UnsafePath");
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Trim()));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("PathOutsideWorkspace");
            return null;
        }

        return fullPath;
    }

    private static string HashFile(string path) =>
        SourceApplyReceiptHashing.ComputeContentHash(File.ReadAllText(path, Encoding.UTF8));

    private static bool IsNoop(SourceApplyRequestFileOperation operation) =>
        string.Equals(operation.OperationKind, SourceApplyRequestFileOperationKinds.Noop, StringComparison.Ordinal);

    private static void RequireSame(Guid left, Guid right, string field, List<ControlledSourceApplyIssue> issues)
    {
        if (left != right)
        {
            issues.Add(new("ProjectMismatch", field, "Evidence project does not match controlled source apply request project."));
        }
    }

    private static void RequireEqual(Guid left, Guid right, string field, List<ControlledSourceApplyIssue> issues)
    {
        if (left != right)
        {
            issues.Add(new("EvidenceMismatch", field, "Required evidence identifiers do not match."));
        }
    }

    private static void RequireEqual(string? left, string? right, string field, List<ControlledSourceApplyIssue> issues)
    {
        if (!string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal))
        {
            issues.Add(new("EvidenceMismatch", field, "Required evidence values do not match."));
        }
    }

    private static void CompareNullable(string? left, string? right, List<string> issues, string code)
    {
        if (!string.Equals(NormalizeNullable(left), NormalizeNullable(right), StringComparison.Ordinal))
        {
            issues.Add(code);
        }
    }

    private static void AddValidationIssues<TIssue>(IEnumerable<TIssue> sourceIssues, List<ControlledSourceApplyIssue> issues, string prefix)
    {
        foreach (var issue in sourceIssues)
        {
            var code = (string?)issue?.GetType().GetProperty("Code")?.GetValue(issue) ?? "Invalid";
            var field = (string?)issue?.GetType().GetProperty("Field")?.GetValue(issue) ?? prefix;
            var message = (string?)issue?.GetType().GetProperty("Message")?.GetValue(issue) ?? "Validation failed.";
            issues.Add(new($"{prefix}{code}", field, message));
        }
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizePathKey(string? value) => Normalize(value).Replace('\\', '/');

    private sealed record PreflightResult(IReadOnlyList<SourceApplyReceiptFileResult> FileResults, IReadOnlyList<PlannedSourceApplyOperation> PlannedOperations);

    private sealed record PlannedSourceApplyOperation(
        SourceApplyRequestFileOperation Operation,
        string? TargetPath,
        string? PreviousPath,
        ControlledSourceApplyContent? Content,
        SourceApplyReceiptFileResult PreflightResult);
}

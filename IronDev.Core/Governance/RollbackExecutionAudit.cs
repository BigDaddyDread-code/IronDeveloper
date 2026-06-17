namespace IronDev.Core.Governance;

public static class RollbackExecutionAuditBoundaryText
{
    public const string Boundary = """
        Rollback execution audit is read-only evidence inspection.
        Rollback execution audit is not rollback execution.
        Rollback execution audit is not source mutation.
        Rollback execution audit is not source apply.
        Rollback execution audit is not workflow continuation.
        Rollback execution audit is not release readiness.
        Rollback execution audit is not release approval.
        Rollback execution audit does not create repository commits, pushes, merges, branches, or pull requests.
        Rollback execution audit only reports whether rollback execution evidence is internally consistent and bounded.
        Human review remains required after rollback execution audit.
        """;
}

public sealed record RollbackExecutionAuditRequest
{
    public required Guid RollbackExecutionAuditRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required RollbackExecutionReceipt RollbackExecutionReceipt { get; init; }
    public required RollbackPlan RollbackPlan { get; init; }
    public required RollbackSupportReceipt RollbackSupportReceipt { get; init; }
    public required SourceApplyReceipt SourceApplyReceipt { get; init; }
    public required SourceApplyRequest SourceApplyRequest { get; init; }
    public required PatchArtifact PatchArtifact { get; init; }
    public DateTimeOffset AuditedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackExecutionAuditBoundaryText.Boundary;
}

public sealed record RollbackExecutionAuditReport
{
    public required Guid RollbackExecutionAuditReportId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid RollbackExecutionReceiptId { get; init; }
    public required string RollbackExecutionReceiptHash { get; init; }
    public required Guid SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required bool EvidenceConsistent { get; init; }
    public required bool ReceiptHashValid { get; init; }
    public required bool FileResultHashesValid { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required bool MutationOccurred { get; init; }
    public required bool PartialRollbackOccurred { get; init; }
    public required bool WorkflowBoundaryAllowsContinuation { get; init; }
    public required bool ReleaseBoundaryInfersReadiness { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required IReadOnlyList<RollbackExecutionAuditFileResult> FileResults { get; init; }
    public required IReadOnlyList<RollbackExecutionAuditIssue> Issues { get; init; }
    public required DateTimeOffset AuditedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackExecutionAuditBoundaryText.Boundary;
}

public sealed record RollbackExecutionAuditFileResult
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string OperationKind { get; init; }
    public required string RollbackActionHash { get; init; }
    public required string PatchArtifactChangeHash { get; init; }
    public required bool PlannedActionFound { get; init; }
    public required bool PatchArtifactChangeFound { get; init; }
    public required bool FileResultHashValid { get; init; }
    public required bool MutationApplied { get; init; }
    public required bool FlagsConsistentWithOperation { get; init; }
    public required IReadOnlyList<RollbackExecutionAuditIssue> Issues { get; init; }
}

public sealed record RollbackExecutionAuditIssue(string Code, string Field, string Message);

public sealed class RollbackExecutionAuditor
{
    private static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
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

    private static readonly string[] AuthorityMarkers =
    [
        "workflow continued",
        "workflow can continue",
        "release approved",
        "release ready",
        "release readiness",
        "policy satisfied",
        "approval granted",
        "source apply approved",
        "source applied",
        "rollback cleaned up",
        "crash cleaned up",
        Join("git ", "committed"),
        Join("git ", "pushed"),
        Join("git ", "merged"),
        "pull request created",
        "memory promoted",
        "retrieval activated"
    ];

    public RollbackExecutionAuditReport Audit(RollbackExecutionAuditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<RollbackExecutionAuditIssue>();
        var fileReports = new List<RollbackExecutionAuditFileResult>();
        ValidateRequest(request, issues);
        AddValidationIssues(RollbackExecutionReceiptValidation.Validate(request.RollbackExecutionReceipt).Issues, "RollbackExecutionReceipt", issues);
        AddValidationIssues(RollbackPlanValidation.Validate(request.RollbackPlan).Issues, "RollbackPlan", issues);
        AddValidationIssues(RollbackSupportReceiptValidation.Validate(request.RollbackSupportReceipt).Issues, "RollbackSupportReceipt", issues);
        AddValidationIssues(SourceApplyReceiptValidation.Validate(request.SourceApplyReceipt).Issues, "SourceApplyReceipt", issues);
        AddValidationIssues(SourceApplyRequestValidation.Validate(request.SourceApplyRequest).Issues, "SourceApplyRequest", issues);
        AddValidationIssues(PatchArtifactValidation.Validate(request.PatchArtifact).Issues, "PatchArtifact", issues);

        var receipt = request.RollbackExecutionReceipt;
        var plan = request.RollbackPlan;
        var support = request.RollbackSupportReceipt;
        var sourceReceipt = request.SourceApplyReceipt;
        var sourceRequest = request.SourceApplyRequest;
        var patch = request.PatchArtifact;

        var computedReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt);
        var receiptHashValid = StringEquals(receipt.RollbackExecutionReceiptHash, computedReceiptHash);
        if (!receiptHashValid)
        {
            Add(issues, "ReceiptHashMismatch", nameof(receipt.RollbackExecutionReceiptHash), "Rollback execution receipt hash does not match the recomputed hash.");
        }

        var fileResultHashesValid = true;
        foreach (var file in receipt.FileResults)
        {
            var computed = RollbackExecutionReceiptHashing.ComputeFileResultHash(file);
            if (!StringEquals(file.FileResultHash, computed))
            {
                fileResultHashesValid = false;
                Add(issues, "FileResultHashMismatch", file.Path, "Rollback execution file result hash does not match the recomputed hash.");
            }
        }

        CheckPatchHashes(patch, issues);
        CheckEvidenceBinding(request, receipt, plan, support, sourceReceipt, sourceRequest, patch, issues);
        CheckSourceApplyBinding(receipt, sourceReceipt, sourceRequest, patch, support, issues);
        CheckFileResults(receipt, plan, patch, issues, fileReports);
        CheckTruthTable(receipt, plan, issues);
        ScanTextGraph(request, issues);

        return new RollbackExecutionAuditReport
        {
            RollbackExecutionAuditReportId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            RollbackExecutionReceiptId = receipt.RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = receipt.RollbackExecutionReceiptHash,
            SourceApplyReceiptId = sourceReceipt.SourceApplyReceiptId,
            SourceApplyReceiptHash = sourceReceipt.SourceApplyReceiptHash,
            RollbackPlanId = plan.RollbackPlanId,
            RollbackPlanHash = plan.RollbackPlanHash,
            RollbackSupportReceiptId = support.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = support.RollbackSupportReceiptHash,
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            EvidenceConsistent = issues.Count == 0,
            ReceiptHashValid = receiptHashValid,
            FileResultHashesValid = fileResultHashesValid,
            RollbackSucceeded = receipt.RollbackSucceeded,
            MutationOccurred = receipt.MutationOccurred,
            PartialRollbackOccurred = receipt.PartialRollbackOccurred,
            WorkflowBoundaryAllowsContinuation = false,
            ReleaseBoundaryInfersReadiness = false,
            HumanReviewRequired = true,
            FileResults = fileReports,
            Issues = issues,
            AuditedAtUtc = request.AuditedAtUtc == default ? DateTimeOffset.UtcNow : request.AuditedAtUtc,
            EvidenceReferences = request.EvidenceReferences,
            BoundaryMaxims = request.BoundaryMaxims,
            Boundary = request.Boundary
        };
    }

    private static void ValidateRequest(RollbackExecutionAuditRequest request, List<RollbackExecutionAuditIssue> issues)
    {
        if (request.RollbackExecutionAuditRequestId == Guid.Empty) Add(issues, "AuditRequestIdRequired", nameof(request.RollbackExecutionAuditRequestId), "Rollback execution audit request id is required.");
        if (request.ProjectId == Guid.Empty) Add(issues, "ProjectIdRequired", nameof(request.ProjectId), "Project id is required.");
        if (request.AuditedAtUtc == default) Add(issues, "AuditedAtRequired", nameof(request.AuditedAtUtc), "Audited timestamp is required.");
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);
        ScanText(request.Boundary, nameof(request.Boundary), issues);
        ScanTexts(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        ScanTexts(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);
    }

    private static void CheckPatchHashes(PatchArtifact patch, List<RollbackExecutionAuditIssue> issues)
    {
        var computedChangeSetHash = PatchArtifactHashing.ComputeChangeSetHash(patch.FileChanges);
        if (!StringEquals(patch.ChangeSetHash, computedChangeSetHash))
        {
            Add(issues, "PatchChangeSetHashMismatch", nameof(patch.ChangeSetHash), "Patch artifact change-set hash does not match the recomputed hash.");
        }

        var computedPatchHash = PatchArtifactHashing.ComputePatchHash(patch, computedChangeSetHash);
        if (!StringEquals(patch.PatchHash, computedPatchHash))
        {
            Add(issues, "PatchHashMismatch", nameof(patch.PatchHash), "Patch artifact hash does not match the recomputed hash.");
        }
    }

    private static void CheckEvidenceBinding(
        RollbackExecutionAuditRequest request,
        RollbackExecutionReceipt receipt,
        RollbackPlan plan,
        RollbackSupportReceipt support,
        SourceApplyReceipt sourceReceipt,
        SourceApplyRequest sourceRequest,
        PatchArtifact patch,
        List<RollbackExecutionAuditIssue> issues)
    {
        Match(request.ProjectId, receipt.ProjectId, nameof(receipt.ProjectId), "ProjectMismatch", issues);
        Match(request.ProjectId, plan.ProjectId, nameof(plan.ProjectId), "ProjectMismatch", issues);
        Match(request.ProjectId, support.ProjectId, nameof(support.ProjectId), "ProjectMismatch", issues);
        Match(request.ProjectId, sourceReceipt.ProjectId, nameof(sourceReceipt.ProjectId), "ProjectMismatch", issues);
        Match(request.ProjectId, sourceRequest.ProjectId, nameof(sourceRequest.ProjectId), "ProjectMismatch", issues);
        Match(request.ProjectId, patch.ProjectId, nameof(patch.ProjectId), "ProjectMismatch", issues);

        Match(receipt.RollbackPlanId, plan.RollbackPlanId, nameof(receipt.RollbackPlanId), "RollbackPlanIdMismatch", issues);
        Match(receipt.RollbackPlanHash, plan.RollbackPlanHash, nameof(receipt.RollbackPlanHash), "RollbackPlanHashMismatch", issues);
        Match(receipt.RollbackSupportReceiptId, support.RollbackSupportReceiptId, nameof(receipt.RollbackSupportReceiptId), "RollbackSupportReceiptIdMismatch", issues);
        Match(receipt.RollbackSupportReceiptHash, support.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash), "RollbackSupportReceiptHashMismatch", issues);
        Match(receipt.SourceApplyReceiptId, sourceReceipt.SourceApplyReceiptId, nameof(receipt.SourceApplyReceiptId), "SourceApplyReceiptIdMismatch", issues);
        Match(receipt.SourceApplyReceiptHash, sourceReceipt.SourceApplyReceiptHash, nameof(receipt.SourceApplyReceiptHash), "SourceApplyReceiptHashMismatch", issues);
        Match(receipt.SourceApplyRequestId, sourceRequest.SourceApplyRequestId, nameof(receipt.SourceApplyRequestId), "SourceApplyRequestIdMismatch", issues);
        Match(receipt.SourceApplyRequestHash, sourceRequest.SourceApplyRequestHash, nameof(receipt.SourceApplyRequestHash), "SourceApplyRequestHashMismatch", issues);
        Match(receipt.PatchArtifactId, patch.PatchArtifactId, nameof(receipt.PatchArtifactId), "PatchArtifactIdMismatch", issues);
        Match(receipt.PatchHash, patch.PatchHash, nameof(receipt.PatchHash), "PatchHashMismatch", issues);
        Match(receipt.ChangeSetHash, patch.ChangeSetHash, nameof(receipt.ChangeSetHash), "ChangeSetHashMismatch", issues);
        Match(receipt.SourceBaselineHash, patch.SourceBaselineHash, nameof(receipt.SourceBaselineHash), "SourceBaselineMismatch", issues);
        Match(receipt.SourceBaselineHash, plan.SourceBaselineHash, nameof(plan.SourceBaselineHash), "SourceBaselineMismatch", issues);
        Match(receipt.SourceBaselineHash, support.SourceBaselineHash, nameof(support.SourceBaselineHash), "SourceBaselineMismatch", issues);
        Match(receipt.SourceBaselineHash, sourceRequest.SourceBaselineHash, nameof(sourceRequest.SourceBaselineHash), "SourceBaselineMismatch", issues);
        Match(receipt.SourceBaselineHash, sourceReceipt.SourceBaselineHash, nameof(sourceReceipt.SourceBaselineHash), "SourceBaselineMismatch", issues);
        Match(receipt.WorkspaceBoundaryHash, patch.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash), "WorkspaceBoundaryMismatch", issues);
        Match(receipt.WorkspaceBoundaryHash, plan.WorkspaceBoundaryHash, nameof(plan.WorkspaceBoundaryHash), "WorkspaceBoundaryMismatch", issues);
        Match(receipt.WorkspaceBoundaryHash, support.WorkspaceBoundaryHash, nameof(support.WorkspaceBoundaryHash), "WorkspaceBoundaryMismatch", issues);
        Match(receipt.WorkspaceBoundaryHash, sourceRequest.WorkspaceBoundaryHash, nameof(sourceRequest.WorkspaceBoundaryHash), "WorkspaceBoundaryMismatch", issues);
        Match(receipt.WorkspaceBoundaryHash, sourceReceipt.WorkspaceBoundaryHash, nameof(sourceReceipt.WorkspaceBoundaryHash), "WorkspaceBoundaryMismatch", issues);
        Match(receipt.ExpectedBranch, plan.ExpectedBranch, nameof(receipt.ExpectedBranch), "ExpectedBranchMismatch", issues);
        Match(receipt.ExpectedBranch, support.ExpectedBranch, nameof(support.ExpectedBranch), "ExpectedBranchMismatch", issues);
        Match(receipt.ExpectedBranch, sourceRequest.ExpectedBranch, nameof(sourceRequest.ExpectedBranch), "ExpectedBranchMismatch", issues);
        Match(receipt.ExpectedBranch, sourceReceipt.ExpectedBranch, nameof(sourceReceipt.ExpectedBranch), "ExpectedBranchMismatch", issues);
        Match(receipt.ExpectedCleanWorktreeHash, sourceReceipt.ObservedCleanWorktreeHashAfterApply, nameof(receipt.ExpectedCleanWorktreeHash), "ExpectedCleanWorktreeHashMismatch", issues);
        Match(receipt.ObservedBranch, sourceReceipt.ObservedBranch, nameof(receipt.ObservedBranch), "ObservedBranchMismatch", issues);
        Match(receipt.ObservedSourceBaselineHash, sourceReceipt.SourceBaselineHash, nameof(receipt.ObservedSourceBaselineHash), "ObservedSourceBaselineMismatch", issues);
        Match(receipt.ObservedCleanWorktreeHashBeforeRollback, sourceReceipt.ObservedCleanWorktreeHashAfterApply, nameof(receipt.ObservedCleanWorktreeHashBeforeRollback), "ObservedCleanWorktreeBeforeRollbackMismatch", issues);
    }

    private static void CheckSourceApplyBinding(
        RollbackExecutionReceipt receipt,
        SourceApplyReceipt sourceReceipt,
        SourceApplyRequest sourceRequest,
        PatchArtifact patch,
        RollbackSupportReceipt support,
        List<RollbackExecutionAuditIssue> issues)
    {
        if (!sourceReceipt.MutationOccurred)
        {
            Add(issues, "SourceApplyReceiptMutationRequired", nameof(sourceReceipt.MutationOccurred), "Rollback execution must be tied to a source apply receipt that records mutation.");
        }

        if (!sourceReceipt.ApplySucceeded && !sourceReceipt.PartialApplyOccurred)
        {
            Add(issues, "SourceApplyReceiptSuccessOrPartialRequired", nameof(sourceReceipt.ApplySucceeded), "Rollback execution must be tied to a successful or partial source apply receipt.");
        }

        Match(sourceReceipt.SourceApplyRequestId, sourceRequest.SourceApplyRequestId, nameof(sourceReceipt.SourceApplyRequestId), "SourceApplyRequestIdMismatch", issues);
        Match(sourceReceipt.SourceApplyRequestHash, sourceRequest.SourceApplyRequestHash, nameof(sourceReceipt.SourceApplyRequestHash), "SourceApplyRequestHashMismatch", issues);
        Match(sourceReceipt.PatchArtifactId, patch.PatchArtifactId, nameof(sourceReceipt.PatchArtifactId), "PatchArtifactIdMismatch", issues);
        Match(sourceReceipt.PatchHash, patch.PatchHash, nameof(sourceReceipt.PatchHash), "PatchHashMismatch", issues);
        Match(sourceReceipt.ChangeSetHash, patch.ChangeSetHash, nameof(sourceReceipt.ChangeSetHash), "ChangeSetHashMismatch", issues);
        Match(sourceReceipt.RollbackSupportReceiptId, support.RollbackSupportReceiptId, nameof(sourceReceipt.RollbackSupportReceiptId), "RollbackSupportReceiptIdMismatch", issues);
        Match(sourceReceipt.RollbackSupportReceiptHash, support.RollbackSupportReceiptHash, nameof(sourceReceipt.RollbackSupportReceiptHash), "RollbackSupportReceiptHashMismatch", issues);
        Match(receipt.SourceApplyReceiptId, sourceReceipt.SourceApplyReceiptId, nameof(receipt.SourceApplyReceiptId), "SourceApplyReceiptIdMismatch", issues);
        Match(receipt.SourceApplyReceiptHash, sourceReceipt.SourceApplyReceiptHash, nameof(receipt.SourceApplyReceiptHash), "SourceApplyReceiptHashMismatch", issues);
    }

    private static void CheckFileResults(
        RollbackExecutionReceipt receipt,
        RollbackPlan plan,
        PatchArtifact patch,
        List<RollbackExecutionAuditIssue> issues,
        List<RollbackExecutionAuditFileResult> fileReports)
    {
        var actionsByHash = plan.FileActions.GroupBy(action => action.RollbackActionHash, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var changeHashes = patch.FileChanges.Select(PatchArtifactHashing.ComputeFileChangeHash).ToHashSet(StringComparer.Ordinal);
        var seenResults = new HashSet<string>(StringComparer.Ordinal);
        var resultActionHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in receipt.FileResults)
        {
            var perFileIssues = new List<RollbackExecutionAuditIssue>();
            var duplicateKey = $"{result.Path}|{result.RollbackActionHash}";
            if (!seenResults.Add(duplicateKey))
            {
                Add(issues, "DuplicateFileResult", result.Path, "Rollback execution receipt contains duplicate file result for the same path and rollback action hash.");
                Add(perFileIssues, "DuplicateFileResult", result.Path, "Duplicate rollback file result.");
            }

            var plannedActionFound = actionsByHash.TryGetValue(result.RollbackActionHash, out var matchingActions) && matchingActions.Length == 1;
            if (!plannedActionFound)
            {
                Add(issues, "MissingRollbackPlanAction", result.Path, "Rollback execution file result does not match exactly one rollback plan action.");
                Add(perFileIssues, "MissingRollbackPlanAction", result.Path, "No matching rollback plan action was found.");
            }
            else
            {
                var action = matchingActions![0];
                resultActionHashes.Add(action.RollbackActionHash);
                CheckActionResultBinding(action, result, issues, perFileIssues);
            }

            var patchArtifactChangeFound = result.OperationKind == RollbackPlanFileActionKinds.Noop || changeHashes.Contains(result.PatchArtifactChangeHash);
            if (!patchArtifactChangeFound)
            {
                Add(issues, "PatchArtifactChangeHashMismatch", result.Path, "Rollback execution file result does not match a patch artifact file change hash.");
                Add(perFileIssues, "PatchArtifactChangeHashMismatch", result.Path, "No matching patch artifact file change was found.");
            }

            var fileResultHashValid = StringEquals(result.FileResultHash, RollbackExecutionReceiptHashing.ComputeFileResultHash(result));
            if (!fileResultHashValid)
            {
                Add(perFileIssues, "FileResultHashMismatch", result.Path, "File result hash mismatch.");
            }

            var flagsConsistent = OperationFlagsConsistent(result);
            if (!flagsConsistent)
            {
                Add(issues, "OperationFlagMismatch", result.Path, "Rollback execution file result flags do not match the operation kind.");
                Add(perFileIssues, "OperationFlagMismatch", result.Path, "Operation flags are inconsistent.");
            }

            if (!result.MutationApplied && result.OperationKind != RollbackPlanFileActionKinds.Noop && result.IssueCodes.Count == 0)
            {
                Add(issues, "FailedFileResultIssueCodesRequired", result.Path, "Failed or unapplied rollback file results must include issue codes.");
                Add(perFileIssues, "FailedFileResultIssueCodesRequired", result.Path, "Issue codes are required for unapplied rollback results.");
            }

            fileReports.Add(new RollbackExecutionAuditFileResult
            {
                Path = result.Path,
                PreviousPath = result.PreviousPath,
                OperationKind = result.OperationKind,
                RollbackActionHash = result.RollbackActionHash,
                PatchArtifactChangeHash = result.PatchArtifactChangeHash,
                PlannedActionFound = plannedActionFound,
                PatchArtifactChangeFound = patchArtifactChangeFound,
                FileResultHashValid = fileResultHashValid,
                MutationApplied = result.MutationApplied,
                FlagsConsistentWithOperation = flagsConsistent,
                Issues = perFileIssues
            });
        }

        foreach (var action in plan.FileActions)
        {
            if (!resultActionHashes.Contains(action.RollbackActionHash))
            {
                Add(issues, "MissingFileResultForRollbackPlanAction", action.Path, "Rollback execution receipt is missing a file result for a planned rollback action.");
            }
        }

        if (receipt.PartialRollbackOccurred && resultActionHashes.Count != plan.FileActions.Count)
        {
            Add(issues, "PartialRollbackMissingPlannedOperation", nameof(receipt.FileResults), "Partial rollback receipts must preserve every planned rollback operation.");
        }
    }

    private static void CheckActionResultBinding(RollbackPlanFileAction action, RollbackExecutionReceiptFileResult result, List<RollbackExecutionAuditIssue> issues, List<RollbackExecutionAuditIssue> perFileIssues)
    {
        Match(action.Path, result.Path, result.Path, "RollbackPlanActionPathMismatch", issues, perFileIssues);
        Match(action.PreviousPath ?? string.Empty, result.PreviousPath ?? string.Empty, result.Path, "RollbackPlanActionPreviousPathMismatch", issues, perFileIssues);
        Match(action.PlannedActionKind, result.OperationKind, result.Path, "RollbackActionKindMismatch", issues, perFileIssues);
    }

    private static void CheckTruthTable(RollbackExecutionReceipt receipt, RollbackPlan plan, List<RollbackExecutionAuditIssue> issues)
    {
        var allActionsAreNoop = plan.FileActions.Count > 0 && plan.FileActions.All(action => action.PlannedActionKind == RollbackPlanFileActionKinds.Noop);
        if (receipt.RollbackSucceeded && !receipt.MutationOccurred && !allActionsAreNoop)
        {
            Add(issues, "RollbackSuccessRequiresMutationOrOnlyNoop", nameof(receipt.RollbackSucceeded), "RollbackSucceeded without mutation is allowed only when every planned action is Noop.");
        }

        if (receipt.PartialRollbackOccurred && !receipt.MutationOccurred)
        {
            Add(issues, "PartialRollbackRequiresMutation", nameof(receipt.PartialRollbackOccurred), "Partial rollback requires mutation to have started.");
        }

        if (receipt.PartialRollbackOccurred && receipt.RollbackSucceeded)
        {
            Add(issues, "PartialRollbackCannotSucceed", nameof(receipt.PartialRollbackOccurred), "Partial rollback cannot also be successful.");
        }

        if (!receipt.MutationOccurred && !allActionsAreNoop && receipt.RollbackSucceeded)
        {
            Add(issues, "NonNoopNoMutationCannotSucceed", nameof(receipt.MutationOccurred), "A non-Noop rollback with no mutation cannot be successful.");
        }
    }

    private static bool OperationFlagsConsistent(RollbackExecutionReceiptFileResult result) =>
        result.OperationKind switch
        {
            RollbackPlanFileActionKinds.RestoreModifiedFile => result.MutationApplied ? result.Restored && !result.Deleted && !result.Recreated && !result.RenamedBack && !result.Noop : !result.Restored && !result.Deleted && !result.Recreated && !result.RenamedBack && !result.Noop,
            RollbackPlanFileActionKinds.DeleteCreatedFile => result.MutationApplied ? result.Deleted && !result.Restored && !result.Recreated && !result.RenamedBack && !result.Noop : !result.Restored && !result.Deleted && !result.Recreated && !result.RenamedBack && !result.Noop,
            RollbackPlanFileActionKinds.RecreateDeletedFile => result.MutationApplied ? result.Recreated && !result.Restored && !result.Deleted && !result.RenamedBack && !result.Noop : !result.Restored && !result.Deleted && !result.Recreated && !result.RenamedBack && !result.Noop,
            RollbackPlanFileActionKinds.RenameBack => result.MutationApplied ? result.RenamedBack && !result.Restored && !result.Deleted && !result.Recreated && !result.Noop : !result.Restored && !result.Deleted && !result.Recreated && !result.RenamedBack && !result.Noop,
            RollbackPlanFileActionKinds.Noop => !result.MutationApplied && result.Noop && !result.Restored && !result.Deleted && !result.Recreated && !result.RenamedBack,
            _ => false
        };

    private static void ScanTextGraph(RollbackExecutionAuditRequest request, List<RollbackExecutionAuditIssue> issues)
    {
        ScanTexts(request.RollbackExecutionReceipt.EvidenceReferences, "RollbackExecutionReceipt.EvidenceReferences", issues);
        ScanTexts(request.RollbackExecutionReceipt.BoundaryMaxims, "RollbackExecutionReceipt.BoundaryMaxims", issues);
        ScanTexts(request.RollbackExecutionReceipt.IssueCodes, "RollbackExecutionReceipt.IssueCodes", issues);
        foreach (var file in request.RollbackExecutionReceipt.FileResults)
        {
            ScanText(file.Path, "RollbackExecutionReceipt.FileResults.Path", issues);
            ScanText(file.PreviousPath, "RollbackExecutionReceipt.FileResults.PreviousPath", issues);
            ScanTexts(file.IssueCodes, "RollbackExecutionReceipt.FileResults.IssueCodes", issues);
        }
    }

    private static void AddValidationIssues<T>(IEnumerable<T> validationIssues, string prefix, List<RollbackExecutionAuditIssue> issues)
    {
        foreach (var issue in validationIssues)
        {
            var code = issue?.GetType().GetProperty("Code")?.GetValue(issue)?.ToString() ?? "ValidationIssue";
            var field = issue?.GetType().GetProperty("Field")?.GetValue(issue)?.ToString() ?? prefix;
            var message = issue?.GetType().GetProperty("Message")?.GetValue(issue)?.ToString() ?? "Validation issue.";
            Add(issues, $"{prefix}.{code}", field, message);
        }
    }

    private static void Match(Guid expected, Guid actual, string field, string code, List<RollbackExecutionAuditIssue> issues)
    {
        if (expected != actual)
        {
            Add(issues, code, field, "Evidence id binding mismatch.");
        }
    }

    private static void Match(string? expected, string? actual, string field, string code, List<RollbackExecutionAuditIssue> issues)
    {
        if (!StringEquals(expected, actual))
        {
            Add(issues, code, field, "Evidence hash/text binding mismatch.");
        }
    }

    private static void Match(string? expected, string? actual, string field, string code, List<RollbackExecutionAuditIssue> issues, List<RollbackExecutionAuditIssue> perFileIssues)
    {
        if (!StringEquals(expected, actual))
        {
            var issue = new RollbackExecutionAuditIssue(code, field, "Rollback file evidence binding mismatch.");
            issues.Add(issue);
            perFileIssues.Add(issue);
        }
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<RollbackExecutionAuditIssue> issues)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "Required", field, "At least one non-empty value is required.");
        }
    }

    private static void ScanTexts(IEnumerable<string> values, string field, List<RollbackExecutionAuditIssue> issues)
    {
        foreach (var value in values)
        {
            ScanText(value, field, issues);
        }
    }

    private static void ScanText(string? value, string field, List<RollbackExecutionAuditIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (PrivateOrRawMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            Add(issues, "PrivateOrRawMaterial", field, "Rollback execution audit input must not contain private/raw material markers.");
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (ContainsForbiddenAuthorityMarker(normalized, marker))
            {
                Add(issues, "AuthorityClaim", field, "Rollback execution audit input must not contain authority claims.");
                return;
            }
        }
    }

    private static bool ContainsForbiddenAuthorityMarker(string normalized, string marker)
    {
        if (!normalized.Contains(marker, StringComparison.Ordinal))
        {
            return false;
        }

        return !(normalized.Contains($"not {marker}", StringComparison.Ordinal) || normalized.Contains($"does not {marker}", StringComparison.Ordinal) || normalized.Contains("does not declare the crash cleaned up", StringComparison.Ordinal));
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static void Add(List<RollbackExecutionAuditIssue> issues, string code, string field, string message) =>
        issues.Add(new RollbackExecutionAuditIssue(code, field, message));

    private static string Join(string left, string right) => left + right;
}

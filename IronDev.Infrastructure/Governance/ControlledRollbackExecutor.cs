using System.Text;
using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ControlledRollbackExecutor : IControlledRollbackExecutor
{
    private readonly IRollbackExecutionReceiptStore _receiptStore;

    public ControlledRollbackExecutor(IRollbackExecutionReceiptStore receiptStore) =>
        _receiptStore = receiptStore;

    public async Task<ControlledRollbackExecutionResult> RollbackAsync(ControlledRollbackExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = ValidateEvidenceChain(request);
        var preflight = PreflightWorkspace(request, issues);
        if (issues.Count > 0)
        {
            return Rejected(issues, preflight.FileResults);
        }

        var fileResults = preflight.FileResults.ToList();
        var plannedOperations = preflight.PlannedOperations.ToArray();
        var mutationOccurred = false;
        var partial = false;
        var issueCodes = new List<string>();
        var currentOperationIndex = -1;

        try
        {
            for (var index = 0; index < plannedOperations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentOperationIndex = index;
                var planned = plannedOperations[index];
                var result = await ApplyRollbackOperationAsync(planned, cancellationToken);
                fileResults[index] = result;
                mutationOccurred |= result.MutationApplied;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            partial = mutationOccurred;
            issueCodes.Add("RollbackFailedAfterMutationStarted");
            issues.Add(new("RollbackFailed", nameof(ControlledRollbackExecutionRequest.WorkspaceRoot), ex.Message));

            if (currentOperationIndex >= 0 && currentOperationIndex < fileResults.Count)
            {
                var failed = fileResults[currentOperationIndex];
                var failedIssues = failed.IssueCodes.Concat(["RollbackFailed"]).Distinct(StringComparer.Ordinal).ToArray();
                var failedResult = failed with
                {
                    PreconditionsSatisfied = false,
                    MutationApplied = false,
                    Restored = false,
                    Deleted = false,
                    Recreated = false,
                    RenamedBack = false,
                    Noop = false,
                    IssueCodes = failedIssues
                };
                fileResults[currentOperationIndex] = failedResult with
                {
                    FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(failedResult)
                };
            }
        }

        if (issues.Count > 0 && !mutationOccurred)
        {
            return Rejected(issues, fileResults);
        }

        var receipt = BuildReceipt(
            request,
            mutationOccurred,
            rollbackSucceeded: issues.Count == 0,
            partialRollbackOccurred: partial,
            fileResults,
            issueCodes.Concat(issues.Select(issue => issue.Code)).Distinct(StringComparer.Ordinal).ToArray());

        await _receiptStore.SaveAsync(receipt, cancellationToken);

        return new ControlledRollbackExecutionResult
        {
            Status = receipt.RollbackSucceeded ? ControlledRollbackExecutionStatuses.RolledBack : ControlledRollbackExecutionStatuses.PartialFailure,
            Succeeded = receipt.RollbackSucceeded,
            MutationOccurred = receipt.MutationOccurred,
            PartialRollbackOccurred = receipt.PartialRollbackOccurred,
            Issues = issues,
            FileResults = receipt.FileResults,
            Receipt = receipt
        };
    }

    private static List<ControlledRollbackExecutionIssue> ValidateEvidenceChain(ControlledRollbackExecutionRequest request)
    {
        var issues = new List<ControlledRollbackExecutionIssue>();

        AddValidationIssues(RollbackPlanValidation.Validate(request.RollbackPlan).Issues, issues, "RollbackPlan");
        AddValidationIssues(RollbackSupportReceiptValidation.Validate(request.RollbackSupportReceipt).Issues, issues, "RollbackSupportReceipt");
        AddValidationIssues(SourceApplyRequestValidation.Validate(request.SourceApplyRequest).Issues, issues, "SourceApplyRequest");
        AddValidationIssues(SourceApplyReceiptValidation.Validate(request.SourceApplyReceipt).Issues, issues, "SourceApplyReceipt");
        AddValidationIssues(PatchArtifactValidation.Validate(request.PatchArtifact).Issues, issues, "PatchArtifact");

        RequireSame(request.ProjectId, request.RollbackPlan.ProjectId, nameof(request.RollbackPlan.ProjectId), issues);
        RequireSame(request.ProjectId, request.RollbackSupportReceipt.ProjectId, nameof(request.RollbackSupportReceipt.ProjectId), issues);
        RequireSame(request.ProjectId, request.SourceApplyRequest.ProjectId, nameof(request.SourceApplyRequest.ProjectId), issues);
        RequireSame(request.ProjectId, request.SourceApplyReceipt.ProjectId, nameof(request.SourceApplyReceipt.ProjectId), issues);
        RequireSame(request.ProjectId, request.PatchArtifact.ProjectId, nameof(request.PatchArtifact.ProjectId), issues);

        if (!request.SourceApplyReceipt.MutationOccurred)
            issues.Add(new("SourceApplyMutationMissing", nameof(request.SourceApplyReceipt.MutationOccurred), "Rollback requires a source apply receipt with recorded mutation."));
        if (!request.SourceApplyReceipt.ApplySucceeded && !request.SourceApplyReceipt.PartialApplyOccurred)
            issues.Add(new("SourceApplyReceiptNotRollbackable", nameof(request.SourceApplyReceipt), "Rollback requires a successful or partial source apply receipt."));
        if (request.RollbackPlan.ExpiresAtUtc is { } planExpiry && planExpiry <= request.RequestedAtUtc)
            issues.Add(new("RollbackPlanExpired", nameof(request.RollbackPlan.ExpiresAtUtc), "Rollback plan is expired."));
        if (request.RollbackSupportReceipt.ExpiresAtUtc is { } supportExpiry && supportExpiry <= request.RequestedAtUtc)
            issues.Add(new("RollbackSupportExpired", nameof(request.RollbackSupportReceipt.ExpiresAtUtc), "Rollback support receipt is expired."));
        if (request.PatchArtifact.ExpiresAtUtc is { } patchExpiry && patchExpiry <= request.RequestedAtUtc)
            issues.Add(new("PatchArtifactExpired", nameof(request.PatchArtifact.ExpiresAtUtc), "Patch artifact is expired."));
        if (request.SourceApplyRequest.ExpiresAtUtc is { } applyRequestExpiry && applyRequestExpiry <= request.RequestedAtUtc)
            issues.Add(new("SourceApplyRequestExpired", nameof(request.SourceApplyRequest.ExpiresAtUtc), "Source apply request is expired."));

        RequireEqual(request.RollbackPlan.RollbackPlanId, request.RollbackSupportReceipt.RollbackPlanId, "RollbackPlanId", issues);
        RequireEqual(request.RollbackPlan.RollbackPlanHash, request.RollbackSupportReceipt.RollbackPlanHash, "RollbackPlanHash", issues);
        RequireEqual(request.RollbackPlan.PatchArtifactId, request.PatchArtifact.PatchArtifactId, "PatchArtifactId", issues);
        RequireEqual(request.RollbackPlan.PatchHash, request.PatchArtifact.PatchHash, "PatchHash", issues);
        RequireEqual(request.RollbackPlan.ChangeSetHash, request.PatchArtifact.ChangeSetHash, "ChangeSetHash", issues);
        RequireEqual(request.RollbackPlan.ControlledDryRunRequestId, request.PatchArtifact.ControlledDryRunRequestId, "ControlledDryRunRequestId", issues);
        RequireEqual(request.RollbackPlan.DryRunExecutionAuditId, request.PatchArtifact.DryRunExecutionAuditId, "DryRunExecutionAuditId", issues);
        RequireEqual(request.RollbackPlan.DryRunAuditHash, request.PatchArtifact.DryRunAuditHash, "DryRunAuditHash", issues);
        RequireEqual(request.RollbackPlan.DryRunReceiptHash, request.PatchArtifact.DryRunReceiptHash, "DryRunReceiptHash", issues);
        RequireEqual(request.RollbackPlan.PolicySatisfactionId, request.PatchArtifact.PolicySatisfactionId, "PolicySatisfactionId", issues);
        RequireEqual(request.RollbackPlan.PolicySatisfactionHash, request.PatchArtifact.PolicySatisfactionHash, "PolicySatisfactionHash", issues);
        RequireEqual(request.RollbackPlan.SourceBaselineHash, request.PatchArtifact.SourceBaselineHash, "PatchSourceBaselineHash", issues);
        RequireEqual(request.RollbackPlan.WorkspaceBoundaryHash, request.PatchArtifact.WorkspaceBoundaryHash, "PatchWorkspaceBoundaryHash", issues);
        RequireEqual(request.SourceApplyRequest.SourceApplyRequestId, request.SourceApplyReceipt.SourceApplyRequestId, "SourceApplyRequestId", issues);
        RequireEqual(request.SourceApplyRequest.SourceApplyRequestHash, request.SourceApplyReceipt.SourceApplyRequestHash, "SourceApplyRequestHash", issues);
        RequireEqual(request.SourceApplyRequest.PatchArtifactId, request.SourceApplyReceipt.PatchArtifactId, "SourceApplyReceiptPatchArtifactId", issues);
        RequireEqual(request.SourceApplyRequest.PatchHash, request.SourceApplyReceipt.PatchHash, "SourceApplyReceiptPatchHash", issues);
        RequireEqual(request.SourceApplyRequest.ChangeSetHash, request.SourceApplyReceipt.ChangeSetHash, "SourceApplyReceiptChangeSetHash", issues);
        RequireEqual(request.SourceApplyRequest.RollbackSupportReceiptId, request.SourceApplyReceipt.RollbackSupportReceiptId, "SourceApplyReceiptRollbackSupportReceiptId", issues);
        RequireEqual(request.SourceApplyRequest.RollbackSupportReceiptHash, request.SourceApplyReceipt.RollbackSupportReceiptHash, "SourceApplyReceiptRollbackSupportReceiptHash", issues);
        RequireEqual(request.SourceApplyRequest.RollbackPlanId, request.RollbackPlan.RollbackPlanId, "SourceApplyRequestRollbackPlanId", issues);
        RequireEqual(request.SourceApplyRequest.RollbackPlanHash, request.RollbackPlan.RollbackPlanHash, "SourceApplyRequestRollbackPlanHash", issues);
        RequireEqual(request.RollbackSupportReceipt.PatchArtifactId, request.PatchArtifact.PatchArtifactId, "RollbackSupportPatchArtifactId", issues);
        RequireEqual(request.RollbackSupportReceipt.PatchHash, request.PatchArtifact.PatchHash, "RollbackSupportPatchHash", issues);
        RequireEqual(request.RollbackSupportReceipt.ChangeSetHash, request.PatchArtifact.ChangeSetHash, "RollbackSupportChangeSetHash", issues);
        RequireEqual(request.RollbackSupportReceipt.RollbackSupportReceiptId, request.SourceApplyReceipt.RollbackSupportReceiptId, "RollbackSupportReceiptId", issues);
        RequireEqual(request.RollbackSupportReceipt.RollbackSupportReceiptHash, request.SourceApplyReceipt.RollbackSupportReceiptHash, "RollbackSupportReceiptHash", issues);
        RequireEqual(request.RollbackSupportReceipt.SourceBaselineHash, request.RollbackPlan.SourceBaselineHash, "RollbackSupportSourceBaselineHash", issues);
        RequireEqual(request.RollbackSupportReceipt.WorkspaceBoundaryHash, request.RollbackPlan.WorkspaceBoundaryHash, "RollbackSupportWorkspaceBoundaryHash", issues);
        RequireEqual(request.RollbackSupportReceipt.ExpectedBranch, request.RollbackPlan.ExpectedBranch, "RollbackSupportExpectedBranch", issues);
        RequireEqual(request.RollbackSupportReceipt.ExpectedCleanWorktreeHash, request.RollbackPlan.ExpectedCleanWorktreeHash, "RollbackSupportExpectedCleanWorktreeHash", issues);
        RequireEqual(request.RollbackPlan.WorkspaceBoundaryHash, request.ApprovedWorkspaceBoundaryHash, "ApprovedWorkspaceBoundaryHash", issues);
        RequireEqual(request.RollbackPlan.ExpectedBranch, request.ObservedBranch, "ObservedBranch", issues);
        RequireEqual(request.RollbackPlan.SourceBaselineHash, request.ObservedSourceBaselineHash, "ObservedSourceBaselineHash", issues);
        RequireEqual(request.SourceApplyReceipt.ObservedCleanWorktreeHashAfterApply, request.ObservedCleanWorktreeHashBeforeRollback, "ObservedCleanWorktreeHashBeforeRollback", issues);

        return issues;
    }

    private static PreflightResult PreflightWorkspace(ControlledRollbackExecutionRequest request, List<ControlledRollbackExecutionIssue> issues)
    {
        var fileResults = new List<RollbackExecutionReceiptFileResult>();
        var planned = new List<PlannedRollbackOperation>();
        var root = NormalizeRoot(request.WorkspaceRoot, issues);
        if (root is null) return new(fileResults, planned);

        var contentByPath = request.ApprovedContents
            .GroupBy(content => NormalizePathKey(content.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var action in OrderedActions(request.RollbackPlan.FileActions))
        {
            var actionIssues = new List<string>();
            var target = ResolvePath(root, action.Path, actionIssues);
            var previous = string.IsNullOrWhiteSpace(action.PreviousPath) ? null : ResolvePath(root, action.PreviousPath!, actionIssues);
            RejectReparsePoint(root, target, actionIssues);
            RejectReparsePoint(root, previous, actionIssues);

            var patchChange = FindPatchChange(request.PatchArtifact, action);
            var patchChangeHash = patchChange is null ? "sha256:missing-patch-change" : PatchArtifactHashing.ComputeFileChangeHash(patchChange);
            if (patchChange is null && action.PlannedActionKind != RollbackPlanFileActionKinds.Noop) actionIssues.Add("PatchArtifactChangeMissing");
            if (patchChange is { IsBinary: true }) actionIssues.Add("BinaryPatchNotSupported");

            var sourceApplyResult = FindSourceApplyResult(request.SourceApplyReceipt, action, patchChangeHash);
            if (sourceApplyResult is null && action.PlannedActionKind != RollbackPlanFileActionKinds.Noop) actionIssues.Add("SourceApplyFileResultMissing");
            else if (sourceApplyResult is { MutationApplied: false } && action.PlannedActionKind != RollbackPlanFileActionKinds.Noop) actionIssues.Add("SourceApplyMutationMissingForFile");

            if (patchChange is not null) ValidatePatchActionBinding(action, patchChange, actionIssues);

            string? beforeHash = target is not null && File.Exists(target) ? HashFile(target) : null;
            ControlledRollbackContent? content = null;

            switch (action.PlannedActionKind)
            {
                case RollbackPlanFileActionKinds.RestoreModifiedFile:
                    if (target is null || !File.Exists(target)) actionIssues.Add("TargetMissing");
                    CompareNullable(action.ExpectedCurrentContentHash, beforeHash, actionIssues, "CurrentHashMismatch");
                    content = RequireContent(action, contentByPath, actionIssues);
                    CompareNullable(action.RestoreContentHash, content?.ContentHash, actionIssues, "RestoreContentHashMismatch");
                    break;
                case RollbackPlanFileActionKinds.DeleteCreatedFile:
                    if (target is null || !File.Exists(target)) actionIssues.Add("TargetMissing");
                    CompareNullable(action.ExpectedCurrentContentHash, beforeHash, actionIssues, "CurrentHashMismatch");
                    CompareNullable(action.DeleteContentHash, beforeHash, actionIssues, "DeleteContentHashMismatch");
                    break;
                case RollbackPlanFileActionKinds.RecreateDeletedFile:
                    if (target is not null && File.Exists(target)) actionIssues.Add("TargetAlreadyExists");
                    content = RequireContent(action, contentByPath, actionIssues);
                    CompareNullable(action.RestoreContentHash, content?.ContentHash, actionIssues, "RestoreContentHashMismatch");
                    break;
                case RollbackPlanFileActionKinds.RenameBack:
                    if (target is null || !File.Exists(target)) actionIssues.Add("TargetMissing");
                    if (previous is null) actionIssues.Add("PreviousPathMissing");
                    if (previous is not null && File.Exists(previous)) actionIssues.Add("PreviousPathAlreadyExists");
                    CompareNullable(action.ExpectedCurrentContentHash, beforeHash, actionIssues, "CurrentHashMismatch");
                    CompareNullable(action.RestoreContentHash, beforeHash, actionIssues, "RestoreContentHashMismatch");
                    break;
                case RollbackPlanFileActionKinds.Noop:
                    break;
                default:
                    actionIssues.Add("UnsupportedRollbackActionKind");
                    break;
            }

            var afterHash = action.PlannedActionKind switch
            {
                RollbackPlanFileActionKinds.RestoreModifiedFile => action.RestoreContentHash,
                RollbackPlanFileActionKinds.DeleteCreatedFile => null,
                RollbackPlanFileActionKinds.RecreateDeletedFile => action.RestoreContentHash,
                RollbackPlanFileActionKinds.RenameBack => action.RestoreContentHash,
                _ => beforeHash
            };

            var result = BuildFileResult(action, patchChangeHash, beforeHash, afterHash, preconditionsSatisfied: actionIssues.Count == 0, mutationApplied: false, actionIssues);
            fileResults.Add(result);
            planned.Add(new(action, target, previous, content, patchChangeHash, result));

            foreach (var issue in actionIssues)
                issues.Add(new(issue, action.Path, $"Rollback action failed preflight: {issue}."));
        }

        return new(fileResults, planned);
    }

    private static async Task<RollbackExecutionReceiptFileResult> ApplyRollbackOperationAsync(PlannedRollbackOperation plan, CancellationToken cancellationToken)
    {
        var action = plan.Action;
        var mutationApplied = false;
        var restored = false;
        var deleted = false;
        var recreated = false;
        var renamedBack = false;
        var noop = false;
        string? afterHash = plan.PreflightResult.AfterContentHash;

        switch (action.PlannedActionKind)
        {
            case RollbackPlanFileActionKinds.RestoreModifiedFile:
                Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath!)!);
                await File.WriteAllTextAsync(plan.TargetPath!, plan.Content!.Content, Encoding.UTF8, cancellationToken);
                afterHash = HashFile(plan.TargetPath!);
                mutationApplied = restored = true;
                break;
            case RollbackPlanFileActionKinds.DeleteCreatedFile:
                File.Delete(plan.TargetPath!);
                afterHash = null;
                mutationApplied = deleted = true;
                break;
            case RollbackPlanFileActionKinds.RecreateDeletedFile:
                Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath!)!);
                await File.WriteAllTextAsync(plan.TargetPath!, plan.Content!.Content, Encoding.UTF8, cancellationToken);
                afterHash = HashFile(plan.TargetPath!);
                mutationApplied = recreated = true;
                break;
            case RollbackPlanFileActionKinds.RenameBack:
                Directory.CreateDirectory(Path.GetDirectoryName(plan.PreviousPath!)!);
                File.Move(plan.TargetPath!, plan.PreviousPath!);
                afterHash = HashFile(plan.PreviousPath!);
                mutationApplied = renamedBack = true;
                break;
            case RollbackPlanFileActionKinds.Noop:
                noop = true;
                break;
        }

        var result = plan.PreflightResult with
        {
            AfterContentHash = afterHash,
            PreconditionsSatisfied = true,
            MutationApplied = mutationApplied,
            Restored = restored,
            Deleted = deleted,
            Recreated = recreated,
            RenamedBack = renamedBack,
            Noop = noop,
            IssueCodes = []
        };

        return result with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(result) };
    }

    private static RollbackExecutionReceipt BuildReceipt(ControlledRollbackExecutionRequest request, bool mutationOccurred, bool rollbackSucceeded, bool partialRollbackOccurred, IReadOnlyList<RollbackExecutionReceiptFileResult> fileResults, IReadOnlyList<string> issueCodes)
    {
        var rolledBackAt = DateTimeOffset.UtcNow;
        var afterHash = mutationOccurred
            ? RollbackExecutionReceiptHashing.ComputeContentHash(string.Join("\n", fileResults.Select(result => result.FileResultHash).Order(StringComparer.Ordinal)))
            : request.ObservedCleanWorktreeHashBeforeRollback;

        var receipt = new RollbackExecutionReceipt
        {
            RollbackExecutionReceiptId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            ControlledRollbackExecutionRequestId = request.ControlledRollbackExecutionRequestId,
            RollbackPlanId = request.RollbackPlan.RollbackPlanId,
            RollbackPlanHash = request.RollbackPlan.RollbackPlanHash,
            RollbackSupportReceiptId = request.RollbackSupportReceipt.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = request.RollbackSupportReceipt.RollbackSupportReceiptHash,
            SourceApplyRequestId = request.SourceApplyRequest.SourceApplyRequestId,
            SourceApplyRequestHash = request.SourceApplyRequest.SourceApplyRequestHash,
            SourceApplyReceiptId = request.SourceApplyReceipt.SourceApplyReceiptId,
            SourceApplyReceiptHash = request.SourceApplyReceipt.SourceApplyReceiptHash,
            PatchArtifactId = request.PatchArtifact.PatchArtifactId,
            PatchHash = request.PatchArtifact.PatchHash,
            ChangeSetHash = request.PatchArtifact.ChangeSetHash,
            SourceBaselineHash = request.RollbackPlan.SourceBaselineHash,
            WorkspaceBoundaryHash = request.RollbackPlan.WorkspaceBoundaryHash,
            ExpectedBranch = request.RollbackPlan.ExpectedBranch,
            ExpectedCleanWorktreeHash = request.RollbackPlan.ExpectedCleanWorktreeHash,
            ObservedBranch = request.ObservedBranch,
            ObservedSourceBaselineHash = request.ObservedSourceBaselineHash,
            ObservedCleanWorktreeHashBeforeRollback = request.ObservedCleanWorktreeHashBeforeRollback,
            ObservedCleanWorktreeHashAfterRollback = afterHash,
            MutationOccurred = mutationOccurred,
            RollbackSucceeded = rollbackSucceeded,
            PartialRollbackOccurred = partialRollbackOccurred,
            FileResults = fileResults,
            IssueCodes = issueCodes,
            RolledBackAtUtc = rolledBackAt,
            RollbackExecutionReceiptHash = "sha256:pending",
            EvidenceReferences = request.EvidenceReferences.Concat(request.RollbackPlan.EvidenceReferences).Concat(request.RollbackSupportReceipt.EvidenceReferences).Concat(request.SourceApplyReceipt.EvidenceReferences).Distinct(StringComparer.Ordinal).ToArray(),
            BoundaryMaxims = request.BoundaryMaxims.Concat(request.RollbackPlan.BoundaryMaxims).Concat(request.RollbackSupportReceipt.BoundaryMaxims).Concat(request.SourceApplyReceipt.BoundaryMaxims).Distinct(StringComparer.Ordinal).ToArray(),
            Boundary = RollbackExecutionBoundaryText.Boundary
        };

        return receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };
    }

    private static ControlledRollbackExecutionResult Rejected(IReadOnlyList<ControlledRollbackExecutionIssue> issues, IReadOnlyList<RollbackExecutionReceiptFileResult> fileResults) => new()
    {
        Status = ControlledRollbackExecutionStatuses.Rejected,
        Succeeded = false,
        MutationOccurred = false,
        PartialRollbackOccurred = false,
        Issues = issues,
        FileResults = fileResults,
        Receipt = null
    };

    private static RollbackExecutionReceiptFileResult BuildFileResult(RollbackPlanFileAction action, string patchArtifactChangeHash, string? beforeHash, string? afterHash, bool preconditionsSatisfied, bool mutationApplied, IReadOnlyList<string> issueCodes)
    {
        var result = new RollbackExecutionReceiptFileResult
        {
            Path = Normalize(action.Path),
            PreviousPath = NormalizeNullable(action.PreviousPath),
            OperationKind = Normalize(action.PlannedActionKind),
            PatchArtifactChangeHash = Normalize(patchArtifactChangeHash),
            RollbackActionHash = Normalize(action.RollbackActionHash),
            BeforeContentHash = NormalizeNullable(beforeHash),
            AfterContentHash = NormalizeNullable(afterHash),
            PreconditionsSatisfied = preconditionsSatisfied,
            MutationApplied = mutationApplied,
            Restored = false,
            Deleted = false,
            Recreated = false,
            RenamedBack = false,
            Noop = action.PlannedActionKind == RollbackPlanFileActionKinds.Noop,
            IssueCodes = issueCodes.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray(),
            FileResultHash = "sha256:pending"
        };
        return result with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(result) };
    }

    private static void ValidatePatchActionBinding(RollbackPlanFileAction action, PatchArtifactFileChange change, List<string> issues)
    {
        switch (action.PlannedActionKind)
        {
            case RollbackPlanFileActionKinds.DeleteCreatedFile:
                CompareNullable(action.ExpectedCurrentContentHash, change.AfterContentHash, issues, "PatchAfterHashMismatch");
                CompareNullable(action.DeleteContentHash, change.AfterContentHash, issues, "PatchDeleteHashMismatch");
                break;
            case RollbackPlanFileActionKinds.RestoreModifiedFile:
                CompareNullable(action.ExpectedCurrentContentHash, change.AfterContentHash, issues, "PatchAfterHashMismatch");
                CompareNullable(action.RestoreContentHash, change.BeforeContentHash, issues, "PatchBeforeHashMismatch");
                break;
            case RollbackPlanFileActionKinds.RecreateDeletedFile:
                CompareNullable(action.RestoreContentHash, change.BeforeContentHash, issues, "PatchBeforeHashMismatch");
                break;
            case RollbackPlanFileActionKinds.RenameBack:
                CompareNullable(action.PreviousPath, change.PreviousPath, issues, "PatchPreviousPathMismatch");
                CompareNullable(action.ExpectedCurrentContentHash, change.AfterContentHash, issues, "PatchAfterHashMismatch");
                CompareNullable(action.RestoreContentHash, change.BeforeContentHash, issues, "PatchBeforeHashMismatch");
                break;
        }
    }

    private static ControlledRollbackContent? RequireContent(RollbackPlanFileAction action, IReadOnlyDictionary<string, ControlledRollbackContent> contentByPath, List<string> issues)
    {
        if (!contentByPath.TryGetValue(NormalizePathKey(action.Path), out var content))
        {
            issues.Add("ApprovedRollbackContentMissing");
            return null;
        }
        var actualHash = RollbackExecutionReceiptHashing.ComputeContentHash(content.Content);
        CompareNullable(content.ContentHash, actualHash, issues, "RollbackContentHashMismatch");
        return content with { ContentHash = Normalize(content.ContentHash) };
    }

    private static PatchArtifactFileChange? FindPatchChange(PatchArtifact artifact, RollbackPlanFileAction action) =>
        artifact.FileChanges.FirstOrDefault(change => string.Equals(Normalize(change.Path), Normalize(action.Path), StringComparison.OrdinalIgnoreCase) && string.Equals(RequiredRollbackActionKind(change.ChangeKind), Normalize(action.PlannedActionKind), StringComparison.Ordinal));

    private static SourceApplyReceiptFileResult? FindSourceApplyResult(SourceApplyReceipt receipt, RollbackPlanFileAction action, string patchArtifactChangeHash) =>
        receipt.FileResults.FirstOrDefault(result => string.Equals(Normalize(result.Path), Normalize(action.Path), StringComparison.OrdinalIgnoreCase) && string.Equals(Normalize(result.PatchArtifactChangeHash), Normalize(patchArtifactChangeHash), StringComparison.Ordinal));

    private static string? RequiredRollbackActionKind(string? changeKind) => changeKind switch
    {
        "Create" => RollbackPlanFileActionKinds.DeleteCreatedFile,
        "Modify" => RollbackPlanFileActionKinds.RestoreModifiedFile,
        "Delete" => RollbackPlanFileActionKinds.RecreateDeletedFile,
        "Rename" => RollbackPlanFileActionKinds.RenameBack,
        _ => null
    };

    private static IEnumerable<RollbackPlanFileAction> OrderedActions(IEnumerable<RollbackPlanFileAction> actions) =>
        actions.OrderBy(action => Normalize(action.Path), StringComparer.Ordinal).ThenBy(action => Normalize(action.PlannedActionKind), StringComparer.Ordinal).ThenBy(action => Normalize(action.RollbackActionHash), StringComparer.Ordinal);

    private static string? NormalizeRoot(string workspaceRoot, List<ControlledRollbackExecutionIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            issues.Add(new("WorkspaceRootRequired", nameof(ControlledRollbackExecutionRequest.WorkspaceRoot), "Workspace root is required."));
            return null;
        }
        var root = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(root))
        {
            issues.Add(new("WorkspaceRootMissing", nameof(ControlledRollbackExecutionRequest.WorkspaceRoot), "Workspace root must exist."));
            return null;
        }
        if (HasReparsePoint(root))
        {
            issues.Add(new("WorkspaceRootReparsePoint", nameof(ControlledRollbackExecutionRequest.WorkspaceRoot), "Workspace root must not be a symlink, junction, or reparse point."));
            return null;
        }
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static string? ResolvePath(string root, string relativePath, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal) || relativePath.Contains('\\') || relativePath.Any(char.IsControl))
        {
            issues.Add("UnsafePath");
            return null;
        }
        var normalized = relativePath.Trim().Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".." || string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("UnsafePath");
            return null;
        }
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("PathOutsideWorkspace");
            return null;
        }
        return fullPath;
    }

    private static void RejectReparsePoint(string root, string? path, List<string> issues)
    {
        if (path is null) return;
        var current = File.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(current) && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if ((Directory.Exists(current) || File.Exists(current)) && HasReparsePoint(current))
            {
                issues.Add("ReparsePointRejected");
                return;
            }
            var parent = Directory.GetParent(current);
            if (parent is null) return;
            var parentWithSlash = parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (string.Equals(parentWithSlash, root, StringComparison.OrdinalIgnoreCase)) return;
            current = parent.FullName;
        }
    }

    private static bool HasReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
    private static string HashFile(string path) => RollbackExecutionReceiptHashing.ComputeContentHash(File.ReadAllText(path, Encoding.UTF8));
    private static void RequireSame(Guid left, Guid right, string field, List<ControlledRollbackExecutionIssue> issues) { if (left != right) issues.Add(new("ProjectMismatch", field, "Evidence project does not match controlled rollback execution request project.")); }
    private static void RequireEqual(Guid left, Guid right, string field, List<ControlledRollbackExecutionIssue> issues) { if (left != right) issues.Add(new("EvidenceMismatch", field, "Required evidence identifiers do not match.")); }
    private static void RequireEqual(string? left, string? right, string field, List<ControlledRollbackExecutionIssue> issues) { if (!string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal)) issues.Add(new("EvidenceMismatch", field, "Required evidence values do not match.")); }
    private static void CompareNullable(string? left, string? right, List<string> issues, string code) { if (!string.Equals(NormalizeNullable(left), NormalizeNullable(right), StringComparison.Ordinal)) issues.Add(code); }

    private static void AddValidationIssues<TIssue>(IEnumerable<TIssue> sourceIssues, List<ControlledRollbackExecutionIssue> issues, string prefix)
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
    private sealed record PreflightResult(IReadOnlyList<RollbackExecutionReceiptFileResult> FileResults, IReadOnlyList<PlannedRollbackOperation> PlannedOperations);
    private sealed record PlannedRollbackOperation(RollbackPlanFileAction Action, string? TargetPath, string? PreviousPath, ControlledRollbackContent? Content, string PatchArtifactChangeHash, RollbackExecutionReceiptFileResult PreflightResult);
}

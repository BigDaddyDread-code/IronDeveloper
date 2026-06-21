using IronDev.Core.Governance;
using IronDev.Core.Governance.RollbackStatus;

namespace IronDev.Core.Governance.RollbackExecution;

public static class ControlledRollbackExecutor
{
    private static readonly string[] ResultForbiddenActions =
    [
        "rollback is mutation",
        "rollback plan is not rollback execution",
        "rollback status is not rollback execution",
        "rollback eligibility is not rollback execution",
        "rollback receipt is not commit authority",
        "rollback receipt is not push authority",
        "rollback receipt is not PR authority",
        "rollback receipt is not merge authority",
        "rollback receipt is not release authority",
        "rollback receipt is not deployment authority",
        "rollback receipt is not workflow continuation",
        "do not commit after rollback without commit authority",
        "do not push after rollback without push authority",
        "do not continue workflow after rollback without workflow authority"
    ];

    public static async Task<ControlledRollbackExecutionResult> ExecuteAsync(
        ControlledRollbackExecutionRequest? request,
        IRollbackWorktreeInspector inspector,
        IControlledRollbackGateway gateway,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (request is null)
        {
            return BuildResult(
                null,
                ControlledRollbackExecutionVerdict.Blocked,
                ControlledRollbackFailureKind.MissingRequest,
                isExecuted: false,
                receipt: null,
                preState: null,
                postState: null,
                issues: ["ControlledRollbackExecutionRequestRequired"],
                now);
        }

        now = request.ObservedAtUtc == default ? now : request.ObservedAtUtc;
        if (inspector is null)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Blocked,
                ControlledRollbackFailureKind.PreStateInvalid,
                isExecuted: false,
                receipt: null,
                preState: null,
                postState: null,
                issues: ["RollbackWorktreeInspectorRequired"],
                now);
        }

        if (gateway is null)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Blocked,
                ControlledRollbackFailureKind.GatewayFailed,
                isExecuted: false,
                receipt: null,
                preState: null,
                postState: null,
                issues: ["ControlledRollbackGatewayRequired"],
                now);
        }

        var preflightIssues = new List<string>();
        ValidateRequestEnvelope(request, preflightIssues);
        ValidateApplyReceipt(request, preflightIssues);
        ValidateTarget(request, preflightIssues);
        ValidateAuthorityOrPolicyPath(request, preflightIssues);

        if (preflightIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Blocked,
                Classify(preflightIssues),
                isExecuted: false,
                receipt: null,
                preState: null,
                postState: null,
                preflightIssues,
                now);
        }

        var preState = await inspector.ObservePreRollbackAsync(request, cancellationToken).ConfigureAwait(false);
        var preStateIssues = new List<string>();
        ValidatePreState(request, preState, preStateIssues);
        if (preStateIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Blocked,
                Classify(preStateIssues),
                isExecuted: false,
                receipt: null,
                preState,
                postState: null,
                preStateIssues,
                now);
        }

        ControlledRollbackReceipt? receipt;
        try
        {
            receipt = await gateway.ExecuteRollbackAsync(BuildGatewayRequest(request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Failed,
                ControlledRollbackFailureKind.GatewayFailed,
                isExecuted: false,
                receipt: null,
                preState,
                postState: null,
                issues: ["ControlledRollbackGatewayFailed", ex.GetType().Name],
                now);
        }

        var receiptIssues = new List<string>();
        ValidateReceipt(request, receipt, receiptIssues);
        var receiptValid = receipt is not null && receiptIssues.Count == 0;
        if (!receiptValid)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Failed,
                Classify(receiptIssues.Count == 0 ? ["ControlledRollbackReceiptRequired"] : receiptIssues),
                isExecuted: false,
                receipt,
                preState,
                postState: null,
                issues: receiptIssues.Count == 0 ? ["ControlledRollbackReceiptRequired"] : receiptIssues,
                now);
        }

        var postState = await inspector.ObservePostRollbackAsync(request, receipt!, cancellationToken).ConfigureAwait(false);
        var postStateIssues = new List<string>();
        ValidatePostState(request, postState, postStateIssues);
        if (postStateIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledRollbackExecutionVerdict.Failed,
                ControlledRollbackFailureKind.PostStateInvalid,
                isExecuted: true,
                receipt,
                preState,
                postState,
                postStateIssues,
                now);
        }

        return BuildResult(
            request,
            ControlledRollbackExecutionVerdict.Completed,
            ControlledRollbackFailureKind.None,
            isExecuted: true,
            receipt,
            preState,
            postState,
            issues: [],
            now);
    }

    private static void ValidateRequestEnvelope(
        ControlledRollbackExecutionRequest request,
        ICollection<string> issues)
    {
        RequireText(request.ExecutionId, "ExecutionIdRequired", issues);
        ValidateSingleExplicitScope(request.Repository, "Repository", issues);
        ValidateSingleExplicitScope(request.Branch, "Branch", issues);
        ValidateSingleExplicitScope(request.RunId, "RunId", issues);
        if (string.IsNullOrWhiteSpace(request.PatchHash))
            issues.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            issues.Add("PatchHashInvalid");
        RequireText(request.SourceApplyReceiptRef, "SourceApplyReceiptRefRequired", issues);
        if (request.ObservedAtUtc == default)
            issues.Add("ObservedAtUtcRequired");
    }

    private static void ValidateTarget(
        ControlledRollbackExecutionRequest request,
        ICollection<string> issues)
    {
        var target = request.Target;
        if (target is null)
        {
            issues.Add("RollbackTargetRequired");
            return;
        }

        RequireText(target.EvidenceRef, "RollbackTargetEvidenceRefRequired", issues);
        Match(target.Repository, request.Repository, "RollbackTargetRepositoryMismatch", issues);
        Match(target.Branch, request.Branch, "RollbackTargetBranchMismatch", issues);
        Match(target.RunId, request.RunId, "RollbackTargetRunIdMismatch", issues);
        Match(target.PatchHash, request.PatchHash, "RollbackTargetPatchHashMismatch", issues);
        MatchReceipt(target.SourceApplyReceiptRef, request.SourceApplyReceiptRef, issues);
        RequireText(target.RollbackTargetId, "RollbackTargetIdRequired", issues);

        if (!target.IsBoundToSourceApplyReceipt)
            issues.Add("RollbackTargetNotBoundToSourceApplyReceipt");
        if (!target.IsCompleteRollback || target.RequiresPartialRollback || target.HasPartialRollbackRisk)
            issues.Add("PartialRollbackRisk");

        ValidateExpectedFiles(target.ExpectedFiles, issues);
    }

    private static void ValidateApplyReceipt(
        ControlledRollbackExecutionRequest request,
        ICollection<string> issues)
    {
        var receipt = request.ApplyReceipt;
        if (receipt is null)
        {
            issues.Add("RollbackApplyReceiptRequired");
            return;
        }

        MatchReceipt(receipt.ReceiptRef, request.SourceApplyReceiptRef, issues);
        Match(receipt.Repository, request.Repository, "RollbackApplyReceiptRepositoryMismatch", issues);
        Match(receipt.Branch, request.Branch, "RollbackApplyReceiptBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "RollbackApplyReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "RollbackApplyReceiptPatchHashMismatch", issues);
        if (!receipt.IsSourceApplyReceipt)
            issues.Add("RollbackApplyReceiptNotSourceApply");
        if (!receipt.IsApplyReceiptAcceptedForRollback)
            issues.Add("RollbackApplyReceiptNotAcceptedForRollback");
    }

    private static void ValidateAuthorityOrPolicyPath(
        ControlledRollbackExecutionRequest request,
        ICollection<string> issues)
    {
        if (request.Authority is null && request.PolicyApprovedPath is null)
            issues.Add("RollbackAuthorityOrPolicyPathRequired");

        if (request.Authority is not null)
            ValidateAuthority(request, request.Authority, issues);
        if (request.PolicyApprovedPath is not null)
            ValidatePolicyPath(request, request.PolicyApprovedPath, issues);
    }

    private static void ValidateAuthority(
        ControlledRollbackExecutionRequest request,
        RollbackExecutionAuthorityEvidence authority,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(authority.EvidenceRef) ||
            !(authority.EvidenceRef.StartsWith("rollback-operation-authority:", StringComparison.OrdinalIgnoreCase) ||
              authority.EvidenceRef.StartsWith("operation-eligibility-decision:", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("RollbackAuthorityEvidenceRefInvalid");
        }

        Match(authority.Repository, request.Repository, "RollbackAuthorityRepositoryMismatch", issues);
        Match(authority.Branch, request.Branch, "RollbackAuthorityBranchMismatch", issues);
        Match(authority.RunId, request.RunId, "RollbackAuthorityRunIdMismatch", issues);
        Match(authority.PatchHash, request.PatchHash, "RollbackAuthorityPatchHashMismatch", issues);
        MatchReceipt(authority.SourceApplyReceiptRef, request.SourceApplyReceiptRef, issues);
        Match(authority.RollbackTargetId, request.Target?.RollbackTargetId, "RollbackAuthorityTargetMismatch", issues);

        var decision = authority.Decision;
        if (decision is null)
        {
            issues.Add("RollbackAuthorityDecisionRequired");
            return;
        }

        if (decision.OperationKind != RunAuthorityOperationKind.Rollback)
            issues.Add("RollbackAuthorityOperationMismatch");
        if (!decision.IsEligibleUnderProfileAndGrant)
            issues.Add("RollbackAuthorityNotEligible");
        if (HasValues(decision.BlockedReasons))
            issues.Add("RollbackAuthorityDecisionBlocked");
        if (HasValues(decision.MissingEvidence))
            issues.Add("RollbackAuthorityDecisionMissingEvidence");
    }

    private static void ValidatePolicyPath(
        ControlledRollbackExecutionRequest request,
        RollbackPolicyApprovedPathEvidence policy,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(policy.EvidenceRef) ||
            !policy.EvidenceRef.StartsWith("rollback-policy-approved-path:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("RollbackPolicyApprovedPathEvidenceRefInvalid");
        }

        Match(policy.Repository, request.Repository, "RollbackPolicyPathRepositoryMismatch", issues);
        Match(policy.Branch, request.Branch, "RollbackPolicyPathBranchMismatch", issues);
        Match(policy.RunId, request.RunId, "RollbackPolicyPathRunIdMismatch", issues);
        Match(policy.PatchHash, request.PatchHash, "RollbackPolicyPathPatchHashMismatch", issues);
        MatchReceipt(policy.SourceApplyReceiptRef, request.SourceApplyReceiptRef, issues);
        Match(policy.RollbackTargetId, request.Target?.RollbackTargetId, "RollbackPolicyPathTargetMismatch", issues);
        ValidateSingleExplicitScope(policy.PolicyId, "RollbackPolicyId", issues);

        if (!policy.IsPolicyApprovedRollbackPath)
            issues.Add("RollbackPolicyPathNotApproved");
        if (!policy.IsBoundToFailedOrReversibleSourceApply)
            issues.Add("RollbackPolicyPathNotBoundToFailedOrReversibleSourceApply");
        if (!policy.AllowsOnlyCompleteRollback)
            issues.Add("RollbackPolicyPathDoesNotRequireCompleteRollback");
        if (policy.AllowsPartialRollback)
            issues.Add("PartialRollbackRisk");
        if (policy.AllowsDownstreamMutation)
            issues.Add("RollbackPolicyPathAllowsDownstreamMutation");
        if (policy.ApprovedAtUtc == default)
            issues.Add("RollbackPolicyPathApprovedAtRequired");
        if (policy.ExpiresAtUtc <= request.ObservedAtUtc)
            issues.Add("RollbackPolicyPathExpired");
    }

    private static void ValidatePreState(
        ControlledRollbackExecutionRequest request,
        RollbackPreStateObservation? preState,
        ICollection<string> issues)
    {
        if (preState is null)
        {
            issues.Add("RollbackPreStateRequired");
            return;
        }

        Match(preState.Repository, request.Repository, "RollbackPreStateRepositoryMismatch", issues);
        Match(preState.Branch, request.Branch, "RollbackPreStateBranchMismatch", issues);
        MatchReceipt(preState.SourceApplyReceiptRef, request.SourceApplyReceiptRef, issues);
        Match(preState.RollbackTargetId, request.Target?.RollbackTargetId, "RollbackPreStateTargetMismatch", issues);
        RequireText(preState.HeadCommitId, "RollbackPreStateHeadCommitRequired", issues);
        if (!preState.IsObservedImmediatelyBeforeRollback)
            issues.Add("RollbackPreStateNotImmediate");
        ValidateObservedFiles(request.Target?.ExpectedFiles, preState.ObservedFiles, usePreHash: true, "RollbackPreState", issues);
        ValidateCleanCollections(
            preState.ChangedFilePaths,
            preState.StagedFilePaths,
            preState.UntrackedFilePaths,
            "RollbackPreState",
            issues);
    }

    private static void ValidateReceipt(
        ControlledRollbackExecutionRequest request,
        ControlledRollbackReceipt? receipt,
        ICollection<string> issues)
    {
        if (receipt is null)
        {
            issues.Add("ControlledRollbackReceiptRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(receipt.ReceiptRef) ||
            !receipt.ReceiptRef.StartsWith("controlled-rollback-receipt:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("ControlledRollbackReceiptRefInvalid");
        }

        Match(receipt.Repository, request.Repository, "ControlledRollbackReceiptRepositoryMismatch", issues);
        Match(receipt.Branch, request.Branch, "ControlledRollbackReceiptBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "ControlledRollbackReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "ControlledRollbackReceiptPatchHashMismatch", issues);
        MatchReceipt(receipt.SourceApplyReceiptRef, request.SourceApplyReceiptRef, issues);
        Match(receipt.RollbackTargetId, request.Target?.RollbackTargetId, "ControlledRollbackReceiptTargetMismatch", issues);

        if (!SameFileSet(receipt.RolledBackFilePaths, ExpectedFilePaths(request.Target?.ExpectedFiles)))
            issues.Add("ControlledRollbackReceiptFileSetMismatch");
        if (!receipt.CompleteRollbackExecuted)
            issues.Add("ControlledRollbackReceiptCompleteRollbackRequired");
        if (receipt.PartialRollbackAttempted)
            issues.Add("PartialRollbackAttempted");
        if (receipt.PartialRollbackFailed)
            issues.Add("PartialRollbackFailed");
        if (receipt.ExecutedAtUtc == default)
            issues.Add("ControlledRollbackReceiptExecutedAtRequired");
        if (receipt.CommitAttempted || receipt.PushAttempted || receipt.PullRequestAttempted || receipt.MergeAttempted ||
            receipt.ReleaseAttempted || receipt.DeploymentAttempted || receipt.MemoryWriteAttempted || receipt.ContinuationAttempted)
        {
            issues.Add("ControlledRollbackReceiptDownstreamAuthorityAttempted");
        }
    }

    private static void ValidatePostState(
        ControlledRollbackExecutionRequest request,
        RollbackPostStateObservation? postState,
        ICollection<string> issues)
    {
        if (postState is null)
        {
            issues.Add("RollbackPostStateRequired");
            return;
        }

        Match(postState.Repository, request.Repository, "RollbackPostStateRepositoryMismatch", issues);
        Match(postState.Branch, request.Branch, "RollbackPostStateBranchMismatch", issues);
        MatchReceipt(postState.SourceApplyReceiptRef, request.SourceApplyReceiptRef, issues);
        Match(postState.RollbackTargetId, request.Target?.RollbackTargetId, "RollbackPostStateTargetMismatch", issues);
        if (!postState.IsObservedAfterRollback)
            issues.Add("RollbackPostStateNotObserved");
        if (!postState.MatchesExpectedPostRollbackState)
            issues.Add("RollbackPostStateMismatch");
        ValidateObservedFiles(request.Target?.ExpectedFiles, postState.ObservedFiles, usePreHash: false, "RollbackPostState", issues);
        ValidateCleanCollections(
            postState.RemainingChangedFilePaths,
            postState.RemainingStagedFilePaths,
            postState.RemainingUntrackedFilePaths,
            "RollbackPostState",
            issues);
    }

    private static ControlledRollbackGatewayRequest BuildGatewayRequest(ControlledRollbackExecutionRequest request) =>
        new()
        {
            Repository = request.Repository.Trim(),
            Branch = request.Branch.Trim(),
            RunId = request.RunId.Trim(),
            PatchHash = request.PatchHash.Trim(),
            SourceApplyReceiptRef = request.SourceApplyReceiptRef.Trim(),
            RollbackTargetId = request.Target!.RollbackTargetId.Trim(),
            ExpectedFiles = request.Target.ExpectedFiles.ToArray(),
            CompleteRollbackOnly = true,
            PartialRollbackDisabled = true,
            CommitDisabled = true,
            PushDisabled = true,
            PullRequestDisabled = true,
            MergeDisabled = true,
            ReleaseDisabled = true,
            DeploymentDisabled = true,
            MemoryWriteDisabled = true,
            WorkflowContinuationDisabled = true
        };

    private static ControlledRollbackExecutionResult BuildResult(
        ControlledRollbackExecutionRequest? request,
        ControlledRollbackExecutionVerdict verdict,
        ControlledRollbackFailureKind failureKind,
        bool isExecuted,
        ControlledRollbackReceipt? receipt,
        RollbackPreStateObservation? preState,
        RollbackPostStateObservation? postState,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var status = BuildStatus(request, verdict, receipt, issues, now);
        var validation = GovernedOperationStatusValidator.Validate(status);
        return new ControlledRollbackExecutionResult
        {
            IsRollbackExecuted = isExecuted,
            Verdict = verdict,
            FailureKind = failureKind,
            Receipt = receipt,
            PreState = preState,
            PostState = postState,
            OperationStatus = status,
            StatusValidation = validation,
            Issues = Clean([.. issues, .. validation.Issues, .. validation.RedFlags])
        };
    }

    private static GovernedOperationStatus BuildStatus(
        ControlledRollbackExecutionRequest? request,
        ControlledRollbackExecutionVerdict verdict,
        ControlledRollbackReceipt? receipt,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var state = verdict switch
        {
            ControlledRollbackExecutionVerdict.Completed => GovernedOperationState.Completed,
            ControlledRollbackExecutionVerdict.Failed => GovernedOperationState.Failed,
            _ => GovernedOperationState.Blocked
        };

        return new GovernedOperationStatus
        {
            OperationId = CleanText(request?.ExecutionId, "controlled-rollback-execution-blocked"),
            OperationKind = RunAuthorityOperationKind.Rollback.ToString(),
            Subject = request is null
                ? "controlled rollback execution"
                : $"controlled rollback for {CleanText(request.Repository, "unknown-repository")} {CleanText(request.Branch, "unknown-branch")} {CleanText(request.PatchHash, "unknown-patch")}",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? Clean(issues) : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? BuildMissingEvidence(issues) : [],
            NextSafeActions = BuildNextSafeActions(state),
            ForbiddenActions = BuildForbiddenActions(state, issues),
            EvidenceRefs = BuildEvidenceRefs(request),
            ReceiptRefs = receipt is null ? BuildReceiptRefs(request) : Clean([receipt.ReceiptRef, .. BuildReceiptRefs(request)]),
            ExpiresAtUtc = null,
            ObservedAtUtc = now
        };
    }

    private static IReadOnlyList<string> BuildNextSafeActions(GovernedOperationState state) =>
        state switch
        {
            GovernedOperationState.Completed => ["inspect controlled rollback receipt before requesting commit authority"],
            GovernedOperationState.Failed => ["inspect rollback execution failure", "request fresh rollback authority before retry"],
            _ => ["collect corrected rollback execution evidence", "request controlled rollback executor review after issues are fixed"]
        };

    private static IReadOnlyList<string> BuildForbiddenActions(
        GovernedOperationState state,
        IReadOnlyCollection<string> issues)
    {
        var actions = new List<string>(ResultForbiddenActions);
        if (state == GovernedOperationState.Failed && ContainsAny(issues, ["PostState", "Mismatch", "DirtyWorktree"]))
        {
            actions.Add("do not continue workflow after rollback mismatch");
            actions.Add("do not treat failed rollback as stable");
        }

        if (ContainsAny(issues, ["PartialRollback", "CompleteRollback", "FileSetMismatch"]))
        {
            actions.Add("do not treat partial rollback as successful rollback");
            actions.Add("do not continue workflow after partial rollback failure");
        }

        return Clean(actions);
    }

    private static IReadOnlyList<string> BuildMissingEvidence(IReadOnlyCollection<string> issues)
    {
        var missing = new List<string>();
        if (ContainsAny(issues, ["Request"])) missing.Add("controlled-rollback-execution-request");
        if (ContainsAny(issues, ["Target"])) missing.Add("rollback-target-evidence");
        if (ContainsAny(issues, ["Authority"])) missing.Add("rollback-operation-authority");
        if (ContainsAny(issues, ["Policy"])) missing.Add("rollback-policy-approved-path");
        if (ContainsAny(issues, ["SourceApplyReceipt", "ApplyReceipt"])) missing.Add("matching-source-apply-receipt");
        if (ContainsAny(issues, ["PreState", "Worktree", "Dirty"])) missing.Add("clean-immediate-worktree-state");
        if (ContainsAny(issues, ["ExpectedFiles", "FileSet", "ObservedFiles"])) missing.Add("exact-rollback-file-evidence");
        return Clean(missing);
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(ControlledRollbackExecutionRequest? request) =>
        Clean(
        [
            Ref("controlled-rollback-execution-request", request?.ExecutionId),
            Ref("repo", request?.Repository),
            Ref("branch", request?.Branch),
            Ref("run", request?.RunId),
            Ref("patch-hash", request?.PatchHash),
            Ref("source-apply-receipt", request?.SourceApplyReceiptRef),
            request?.ApplyReceipt?.ReceiptRef,
            request?.Target?.EvidenceRef,
            request?.Authority?.EvidenceRef,
            request?.PolicyApprovedPath?.EvidenceRef,
            .. ValuesOrEmpty(request?.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildReceiptRefs(ControlledRollbackExecutionRequest? request) =>
        Clean(ValuesOrEmpty(request?.ReceiptRefs));

    private static void ValidateExpectedFiles(
        IReadOnlyCollection<RollbackFileExpectation>? files,
        ICollection<string> issues)
    {
        if (files is null || files.Count == 0)
        {
            issues.Add("ExpectedFilesRequired");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!IsSafeRelativePath(file.Path))
                issues.Add($"UnsafeRollbackPath:{file.Path}");
            if (!seen.Add(file.Path.Trim()))
                issues.Add($"DuplicateRollbackPath:{file.Path}");
            if (file.ShouldExistBeforeRollback && !IsSafeHashText(file.ExpectedPreRollbackHash))
                issues.Add($"ExpectedPreRollbackHashRequired:{file.Path}");
            if (file.ShouldExistAfterRollback && !IsSafeHashText(file.ExpectedPostRollbackHash))
                issues.Add($"ExpectedPostRollbackHashRequired:{file.Path}");
        }
    }

    private static void ValidateObservedFiles(
        IReadOnlyCollection<RollbackFileExpectation>? expected,
        IReadOnlyCollection<RollbackObservedFileState>? observed,
        bool usePreHash,
        string label,
        ICollection<string> issues)
    {
        if (expected is null || expected.Count == 0 || observed is null)
        {
            issues.Add($"{label}ObservedFilesRequired");
            return;
        }

        var expectedPaths = ExpectedFilePaths(expected);
        var observedPaths = Clean(observed.Select(file => file.Path));
        if (observedPaths.Count != observed.Count || !SameFileSet(observedPaths, expectedPaths))
        {
            issues.Add($"{label}ObservedFileSetMismatch");
            return;
        }

        foreach (var file in expected)
        {
            var actual = observed.First(item => Same(item.Path, file.Path));
            var shouldExist = usePreHash ? file.ShouldExistBeforeRollback : file.ShouldExistAfterRollback;
            var expectedHash = usePreHash ? file.ExpectedPreRollbackHash : file.ExpectedPostRollbackHash;
            if (actual.Exists != shouldExist)
                issues.Add($"{label}FileExistenceMismatch:{file.Path}");
            if (shouldExist && !Same(actual.ContentHash, expectedHash))
                issues.Add($"{label}FileHashMismatch:{file.Path}");
            if (!shouldExist && !string.IsNullOrWhiteSpace(actual.ContentHash))
                issues.Add($"{label}UnexpectedFileHash:{file.Path}");
        }
    }

    private static void ValidateCleanCollections(
        IReadOnlyCollection<string>? changed,
        IReadOnlyCollection<string>? staged,
        IReadOnlyCollection<string>? untracked,
        string label,
        ICollection<string> issues)
    {
        if (changed is null || staged is null || untracked is null)
        {
            issues.Add($"{label}WorktreeCollectionsRequired");
            return;
        }

        if (HasValues(changed) || HasValues(staged) || HasValues(untracked))
            issues.Add("DirtyWorktree");
    }

    private static ControlledRollbackFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("Target", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.RollbackTargetInvalid;
            if (issue.Contains("Policy", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.PolicyApprovedPathInvalid;
            if (issue.Contains("Authority", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.RollbackAuthorityInvalid;
            if (issue.Contains("ApplyReceipt", StringComparison.OrdinalIgnoreCase) || issue.Contains("SourceApplyReceipt", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.ApplyReceiptMismatch;
            if (issue.Contains("PartialRollback", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.PartialRollbackRisk;
            if (issue.Contains("DirtyWorktree", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.DirtyWorktree;
            if (issue.Contains("PreState", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.PreStateInvalid;
            if (issue.Contains("PostState", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.PostStateInvalid;
            if (issue.Contains("Receipt", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.ReceiptInvalid;
            if (issue.Contains("Downstream", StringComparison.OrdinalIgnoreCase)) return ControlledRollbackFailureKind.BoundaryViolation;
        }

        return ControlledRollbackFailureKind.RequestInvalid;
    }

    private static void ValidateSingleExplicitScope(string? value, string label, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{label}Required");
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{label}MustBeSingleExplicitScope");
        }
    }

    private static void MatchReceipt(string? actual, string? expected, ICollection<string> issues)
    {
        if (!Same(actual, expected))
            issues.Add("RollbackApplyReceiptMismatch");
    }

    private static void Match(string? actual, string? expected, string issue, ICollection<string> issues)
    {
        if (!Same(actual, expected))
            issues.Add(issue);
    }

    private static void RequireText(string? value, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static IReadOnlyList<string> ExpectedFilePaths(IReadOnlyCollection<RollbackFileExpectation>? files) =>
        Clean(ValuesOrEmpty(files).Select(file => file.Path));

    private static bool SameFileSet(
        IReadOnlyCollection<string>? left,
        IReadOnlyCollection<string>? right)
    {
        var cleanLeft = Clean(left);
        var cleanRight = Clean(right);
        return cleanLeft.Count == cleanRight.Count && !cleanLeft.Except(cleanRight, StringComparer.OrdinalIgnoreCase).Any();
    }

    private static bool IsSafeHashText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal))
            return false;
        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Any(char.IsControl))
            return false;
        return !new[] { "*", "all", "any", "latest", "current", "approved", "unknown" }
            .Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmed = path.Trim();
        if (!string.Equals(trimmed, path, StringComparison.Ordinal))
            return false;

        var normalized = trimmed.Replace('\\', '/');
        if (trimmed.Contains('\\', StringComparison.Ordinal))
            return false;
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            normalized.StartsWith("~", StringComparison.Ordinal) ||
            normalized.Contains('\0', StringComparison.Ordinal) ||
            normalized.Contains('$', StringComparison.Ordinal) ||
            normalized.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.Length >= 2 &&
            char.IsLetter(normalized[0]) &&
            normalized[1] == ':')
        {
            return false;
        }

        if (normalized == "." ||
            normalized == ".." ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.EndsWith("/..", StringComparison.Ordinal))
        {
            return false;
        }

        return !normalized.Any(char.IsControl);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(IEnumerable<string?> values, IReadOnlyCollection<string> markers) =>
        values.Any(value => markers.Any(marker => value?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true));

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string CleanText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool HasValues(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values).Any(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> Clean(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<T> ValuesOrEmpty<T>(IEnumerable<T>? values) =>
        values ?? [];
}

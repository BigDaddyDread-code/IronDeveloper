namespace IronDev.Core.Governance.InterruptedRunRecovery;

public static class InterruptedRunRecoveryDiagnosisService
{
    public static InterruptedRunRecoveryReport Diagnose(InterruptedRunEvidenceSnapshot? evidence)
    {
        if (evidence is null)
        {
            return Build(
                "unknown-run",
                InterruptedRunStage.Unknown,
                InterruptedRunRecoveryState.NeedsHumanReview,
                completed: [],
                missing: ["interrupted-run-evidence"],
                blocking: ["InterruptedRunEvidenceRequired"],
                next: ["collect run evidence and request human recovery review"]);
        }

        var runId = string.IsNullOrWhiteSpace(evidence.RunId) ? "unknown-run" : evidence.RunId.Trim();
        var completed = BuildCompletedEvidenceRefs(evidence);
        var contradictions = FindContradictions(evidence);
        if (contradictions.Count > 0)
        {
            return Build(
                runId,
                InterruptedRunStage.Unknown,
                InterruptedRunRecoveryState.NeedsHumanReview,
                completed,
                missing: ["consistent-run-evidence"],
                blocking: contradictions,
                next: ["inspect contradictory evidence and request human recovery review"]);
        }

        if (HasPush(evidence) && !HasDraftPullRequest(evidence))
        {
            return Build(
                runId,
                InterruptedRunStage.PushCompletedNoPullRequest,
                InterruptedRunRecoveryState.NeedsPullRequestCreationDecision,
                completed,
                missing: ["draft-pull-request-receipt"],
                blocking: ["PushCompletedWithoutPullRequest"],
                next: ["require explicit draft PR creation authority"]);
        }

        if (HasCommit(evidence) && !HasPushReceipt(evidence))
        {
            return Build(
                runId,
                InterruptedRunStage.CommitCreatedNoPush,
                InterruptedRunRecoveryState.NeedsFreshAuthority,
                completed,
                missing: ["controlled-push-receipt"],
                blocking: ["CommitCreatedWithoutPush"],
                next: ["require explicit push authority decision"]);
        }

        if (HasSourceApplyStarted(evidence) && !HasCompletedSourceApply(evidence))
        {
            return Build(
                runId,
                InterruptedRunStage.SourceApplyStartedNotCompleted,
                evidence.WorktreeState == InterruptedRunWorktreeState.Clean
                    ? InterruptedRunRecoveryState.NeedsHumanReview
                    : InterruptedRunRecoveryState.NeedsRollbackDecision,
                completed,
                missing: MissingForIncompleteSourceApply(evidence),
                blocking: BlockingForIncompleteSourceApply(evidence),
                next: ["inspect worktree and require explicit rollback or recovery authority"]);
        }

        if (HasCommitPackage(evidence) && !HasCommit(evidence))
        {
            return Build(
                runId,
                InterruptedRunStage.CommitPackageCreatedNoCommit,
                InterruptedRunRecoveryState.NeedsFreshAuthority,
                completed,
                missing: ["controlled-commit-receipt", "commit-hash-evidence"],
                blocking: ["CommitPackageCreatedWithoutCommit"],
                next: ["require explicit commit authority decision"]);
        }

        if (HasValidationResult(evidence) && evidence.ValidationOutcome == InterruptedRunValidationOutcome.Failed)
        {
            return Build(
                runId,
                InterruptedRunStage.ValidationFailed,
                InterruptedRunRecoveryState.Blocked,
                completed,
                missing: ["passing-validation-result-package"],
                blocking: ["ValidationFailed"],
                next: ["inspect validation failures and create a revised governed proposal"]);
        }

        if (HasPatch(evidence) && !HasValidationResult(evidence))
        {
            return Build(
                runId,
                InterruptedRunStage.PatchCreatedNoValidation,
                InterruptedRunRecoveryState.NeedsValidationEvidence,
                completed,
                missing: ["validation-result-package"],
                blocking: ["PatchCreatedWithoutValidation"],
                next: ["run governed validation under the correct profile"]);
        }

        if (HasWorkspace(evidence) && !HasPatch(evidence))
        {
            return Build(
                runId,
                InterruptedRunStage.WorkspaceCreatedNoPatch,
                InterruptedRunRecoveryState.Blocked,
                completed,
                missing: ["patch-proposal-evidence", "patch-package-evidence"],
                blocking: ["WorkspaceCreatedWithoutPatch"],
                next: ["inspect workspace or create a new governed patch proposal request"]);
        }

        return Build(
            runId,
            InterruptedRunStage.Unknown,
            InterruptedRunRecoveryState.NeedsHumanReview,
            completed,
            missing: ["clear-interrupted-stage-evidence"],
            blocking: ["NoInterruptedRunStateDetected"],
            next: ["inspect available evidence and request human recovery review"]);
    }

    private static IReadOnlyList<string> FindContradictions(InterruptedRunEvidenceSnapshot evidence)
    {
        var contradictions = new List<string>();
        AddValidationContradictions(evidence, contradictions);

        if (HasDraftPullRequest(evidence) && !HasPushReceipt(evidence))
            contradictions.Add("DraftPullRequestReceiptWithoutPushReceipt");
        if (HasDraftPullRequest(evidence) && !HasPush(evidence))
            contradictions.Add("DraftPullRequestReceiptWithoutCompletedPushEvidence");
        if (HasPushReceipt(evidence) && !HasValues(evidence.RemoteBranchEvidenceRefs))
            contradictions.Add("PushReceiptWithoutRemoteBranchEvidence");
        if (HasPushReceipt(evidence) && !HasCommitReceipt(evidence))
            contradictions.Add("PushReceiptWithoutCommitReceipt");
        if (HasCommitReceipt(evidence) && !HasValues(evidence.CommitHashEvidenceRefs))
            contradictions.Add("CommitReceiptWithoutCommitHashEvidence");
        if (HasCommitReceipt(evidence) && !HasCompletedSourceApply(evidence))
            contradictions.Add("CommitReceiptWithoutCompletedSourceApplyReceipt");
        if (HasCompletedSourceApply(evidence) && evidence.WorktreeState == InterruptedRunWorktreeState.ApplyFailed)
            contradictions.Add("CompletedSourceApplyReceiptContradictsFailedWorktreeEvidence");

        return Clean(contradictions);
    }

    private static void AddValidationContradictions(
        InterruptedRunEvidenceSnapshot evidence,
        ICollection<string> contradictions)
    {
        if (!HasValidationResult(evidence) || evidence.ValidationOutcome == InterruptedRunValidationOutcome.Passed)
            return;

        var prefix = evidence.ValidationOutcome switch
        {
            InterruptedRunValidationOutcome.Failed => "ValidationFailed",
            InterruptedRunValidationOutcome.Inconclusive => "ValidationInconclusive",
            InterruptedRunValidationOutcome.Unknown => "ValidationUnknown",
            _ => "ValidationInvalid"
        };

        if (HasSourceApplyStarted(evidence))
            contradictions.Add($"{prefix}WithSourceApplyStartedEvidence");
        if (HasCompletedSourceApply(evidence))
            contradictions.Add($"{prefix}WithCompletedSourceApplyReceipt");
        if (HasCommitPackage(evidence))
            contradictions.Add($"{prefix}WithCommitPackageEvidence");
        if (HasCommitReceipt(evidence))
            contradictions.Add($"{prefix}WithCommitReceipt");
        if (HasValues(evidence.CommitHashEvidenceRefs))
            contradictions.Add($"{prefix}WithCommitHashEvidence");
        if (HasPushReceipt(evidence))
            contradictions.Add($"{prefix}WithPushReceipt");
        if (HasValues(evidence.RemoteBranchEvidenceRefs))
            contradictions.Add($"{prefix}WithRemoteBranchEvidence");
        if (HasDraftPullRequest(evidence))
            contradictions.Add($"{prefix}WithDraftPullRequestReceipt");
    }

    private static IReadOnlyList<string> MissingForIncompleteSourceApply(InterruptedRunEvidenceSnapshot evidence)
    {
        var missing = new List<string> { "completed-source-apply-receipt" };
        if (evidence.WorktreeState is InterruptedRunWorktreeState.Unknown or InterruptedRunWorktreeState.Dirty or InterruptedRunWorktreeState.Mismatched or InterruptedRunWorktreeState.ApplyFailed)
            missing.Add("certain-clean-worktree-state");
        return Clean(missing);
    }

    private static IReadOnlyList<string> BlockingForIncompleteSourceApply(InterruptedRunEvidenceSnapshot evidence)
    {
        var blocking = new List<string> { "SourceApplyStartedWithoutCompletedReceipt" };
        if (evidence.WorktreeState == InterruptedRunWorktreeState.Unknown)
            blocking.Add("WorktreeStateUnknown");
        if (evidence.WorktreeState == InterruptedRunWorktreeState.Dirty)
            blocking.Add("WorktreeStateDirty");
        if (evidence.WorktreeState == InterruptedRunWorktreeState.Mismatched)
            blocking.Add("WorktreeStateMismatched");
        if (evidence.WorktreeState == InterruptedRunWorktreeState.ApplyFailed)
            blocking.Add("WorktreeStateApplyFailed");
        return Clean(blocking);
    }

    private static InterruptedRunRecoveryReport Build(
        string runId,
        InterruptedRunStage stage,
        InterruptedRunRecoveryState state,
        IReadOnlyCollection<string> completed,
        IReadOnlyCollection<string> missing,
        IReadOnlyCollection<string> blocking,
        IReadOnlyCollection<string> next) =>
        new()
        {
            RunId = runId,
            DetectedStage = stage,
            RecoveryState = state,
            CompletedEvidenceRefs = Clean(completed),
            MissingEvidenceRefs = Clean(missing),
            BlockingReasons = Clean(blocking),
            NextSafeActions = Clean(next),
            Boundary = RunRecoveryBoundary.Diagnosis
        };

    private static IReadOnlyList<string> BuildCompletedEvidenceRefs(InterruptedRunEvidenceSnapshot evidence) =>
        Clean(
        [
            .. ValuesOrEmpty(evidence.WorkspaceEvidenceRefs),
            .. ValuesOrEmpty(evidence.PatchProposalEvidenceRefs),
            .. ValuesOrEmpty(evidence.PatchPackageEvidenceRefs),
            .. ValuesOrEmpty(evidence.ValidationResultPackageEvidenceRefs),
            .. ValuesOrEmpty(evidence.SourceApplyStartedEvidenceRefs),
            .. ValuesOrEmpty(evidence.CompletedSourceApplyReceiptRefs),
            .. ValuesOrEmpty(evidence.CommitPackageEvidenceRefs),
            .. ValuesOrEmpty(evidence.CommitReceiptRefs),
            .. ValuesOrEmpty(evidence.CommitHashEvidenceRefs),
            .. ValuesOrEmpty(evidence.PushReceiptRefs),
            .. ValuesOrEmpty(evidence.RemoteBranchEvidenceRefs),
            .. ValuesOrEmpty(evidence.DraftPullRequestReceiptRefs),
            .. ValuesOrEmpty(evidence.HostileTextEvidenceRefs),
            .. ValuesOrEmpty(evidence.UiStateEvidenceRefs),
            .. ValuesOrEmpty(evidence.MemoryEvidenceRefs),
            .. ValuesOrEmpty(evidence.HistoricalApprovalEvidenceRefs)
        ]);

    private static bool HasWorkspace(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.WorkspaceEvidenceRefs);

    private static bool HasPatch(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.PatchProposalEvidenceRefs) || HasValues(evidence.PatchPackageEvidenceRefs);

    private static bool HasValidationResult(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.ValidationResultPackageEvidenceRefs);

    private static bool HasSourceApplyStarted(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.SourceApplyStartedEvidenceRefs);

    private static bool HasCompletedSourceApply(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.CompletedSourceApplyReceiptRefs);

    private static bool HasCommitPackage(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.CommitPackageEvidenceRefs);

    private static bool HasCommit(InterruptedRunEvidenceSnapshot evidence) =>
        HasCommitReceipt(evidence) && HasValues(evidence.CommitHashEvidenceRefs);

    private static bool HasCommitReceipt(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.CommitReceiptRefs);

    private static bool HasPush(InterruptedRunEvidenceSnapshot evidence) =>
        HasPushReceipt(evidence) && HasValues(evidence.RemoteBranchEvidenceRefs);

    private static bool HasPushReceipt(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.PushReceiptRefs);

    private static bool HasDraftPullRequest(InterruptedRunEvidenceSnapshot evidence) =>
        HasValues(evidence.DraftPullRequestReceiptRefs);

    private static bool HasValues(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values).Any(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<T> ValuesOrEmpty<T>(IEnumerable<T>? values) =>
        values ?? [];
}

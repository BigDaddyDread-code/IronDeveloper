using IronDev.Core.Governance;

namespace IronDev.Core.SourceApply;

public static class SourceApplyExecutionGate
{
    public static SourceApplyExecutionGateDecision Evaluate(
        SourceApplyExecutionRequest request,
        SourceApplyRequest? sourceApplyRequest,
        PatchArtifactVerificationResult? verification,
        SourceApplyApprovalEvidence? approval,
        SourceApplyReadinessReport? readiness,
        SourceApplyDryRunResult? dryRun,
        RollbackPlanDraft? rollbackDraft,
        SourceSnapshot? preSnapshot,
        ConscienceDecision? conscienceDecision,
        string? thoughtLedgerRef,
        DateTimeOffset? now = null)
    {
        var reasons = new List<string>();

        if (sourceApplyRequest is null)
            reasons.Add("MissingSourceApplyRequest");
        else
        {
            if (!Same(request.RunId, sourceApplyRequest.RunId))
                reasons.Add("SourceApplyRequestRunMismatch");
            if (!Same(request.SourceApplyRequestId, sourceApplyRequest.SourceApplyRequestId))
                reasons.Add("SourceApplyRequestIdMismatch");
            if (!Same(request.SourceRepoIdentity, sourceApplyRequest.SourceRepoIdentity))
                reasons.Add("SourceApplyRequestRepoMismatch");
            if (!Same(request.BaseCommit, sourceApplyRequest.BaseCommit))
                reasons.Add("SourceApplyRequestBaseCommitMismatch");
            if (!Same(request.PatchSha256, sourceApplyRequest.PatchSha256))
                reasons.Add("SourceApplyRequestPatchHashMismatch");
        }

        if (verification is null || verification.Decision != PatchArtifactVerificationDecision.Verified)
            reasons.Add("PatchVerificationNotSatisfied");

        if (approval is null)
        {
            reasons.Add("MissingApprovalEvidence");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(approval.SourceApplyRequestId) || !Same(approval.SourceApplyRequestId, request.SourceApplyRequestId))
                reasons.Add("ApprovalSourceApplyRequestMismatch");
            if (!Same(approval.RunId, request.RunId))
                reasons.Add("ApprovalRunMismatch");
            if (!Same(approval.SourceRepoIdentity, request.SourceRepoIdentity))
                reasons.Add("ApprovalSourceRepoMismatch");
            if (!Same(approval.BaseCommit, request.BaseCommit))
                reasons.Add("ApprovalBaseCommitMismatch");
            if (!Same(approval.PatchSha256, request.PatchSha256))
                reasons.Add("ApprovalPatchHashMismatch");
            if (!SameSet(approval.ApprovedChangedFiles, request.ChangedFiles))
                reasons.Add("ApprovalChangedFilesMismatch");
            if (string.IsNullOrWhiteSpace(approval.ApprovedBy) || !approval.HumanReviewRequired)
                reasons.Add("ApprovalMissingHumanReviewer");
            if (!IsBoundedApprovalStatement(approval.ApprovalText))
                reasons.Add("OverbroadApproval");
        }

        if (readiness is null)
            reasons.Add("MissingSourceApplyReadiness");
        else if (readiness.Readiness != SourceApplyReadiness.ReadyForFutureControlledApply)
            reasons.Add("SourceApplyReadinessNotReady");

        if (dryRun is null)
            reasons.Add("MissingDryRunResult");
        else
        {
            if (!dryRun.PatchAppliedInRehearsalWorkspace)
                reasons.Add("DryRunDidNotApplyPatch");
            if (!Same(dryRun.RehearsalHeadCommit, request.BaseCommit))
                reasons.Add("DryRunBaseCommitMismatch");
            if (dryRun.SourceRepoMutated)
                reasons.Add("DryRunMutatedSourceRepo");
        }

        if (rollbackDraft is null)
            reasons.Add("RollbackPlanMissing");

        if (preSnapshot is null)
        {
            reasons.Add("MissingPreSourceSnapshot");
        }
        else
        {
            if (!Same(preSnapshot.HeadCommit, request.BaseCommit))
                reasons.Add("SourceHeadMismatch");
            if (!string.IsNullOrWhiteSpace(preSnapshot.StatusPorcelain))
                reasons.Add("SourceRepoDirty");
        }

        ValidateConscience(request, conscienceDecision, now, reasons);

        var ledger = string.IsNullOrWhiteSpace(thoughtLedgerRef) ? conscienceDecision?.ThoughtLedgerRef : thoughtLedgerRef;
        if (string.IsNullOrWhiteSpace(ledger))
            reasons.Add("MissingThoughtLedger");

        return new SourceApplyExecutionGateDecision
        {
            SourceApplyExecutionGateDecisionId = $"source_apply_exec_gate_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceApplyExecutionRequestId = request.SourceApplyExecutionRequestId,
            SourceApplyRequestId = request.SourceApplyRequestId,
            Decision = reasons.Count == 0 ? SourceApplyExecutionGateDecisionOutcome.AllowApplyToWorkingTree : SourceApplyExecutionGateDecisionOutcome.Block,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    private static void ValidateConscience(SourceApplyExecutionRequest request, ConscienceDecision? decision, DateTimeOffset? now, List<string> reasons)
    {
        if (decision is null)
        {
            reasons.Add("MissingConscienceDecision");
            return;
        }

        if (decision.ActionKind != GovernedActionKind.SourceApply)
            reasons.Add("ConscienceDecisionActionMismatch");
        if (decision.Decision != ConscienceDecisionOutcome.Allow)
            reasons.Add("ConscienceDecisionDoesNotAllow");
        if (decision.ExpiresAtUtc is not null && decision.ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow))
            reasons.Add("ConscienceDecisionExpired");
        if (!Same(decision.SubjectId, request.SourceApplyExecutionRequestId) && !Same(decision.SubjectId, request.SourceApplyRequestId) && !Same(decision.SubjectId, request.RunId))
            reasons.Add("ConscienceDecisionSubjectMismatch");
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsBoundedApprovalStatement(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Contains("I approve this source-apply request for controlled working-tree application only.", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("This approval does not permit commit, push, pull request creation, merge, release, deployment, or workflow continuation.", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve commit", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve push", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve pull request", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve pr", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve merge", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve release", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("approve deployment", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("continue workflow", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("ready to merge", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("ready to release", StringComparison.OrdinalIgnoreCase);

    private static bool SameSet(string[] first, string[] second)
    {
        var normalizedFirst = first.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var normalizedSecond = second.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        return normalizedFirst.Length == normalizedSecond.Length && normalizedFirst.Zip(normalizedSecond).All(pair => Same(pair.First, pair.Second));
    }
}

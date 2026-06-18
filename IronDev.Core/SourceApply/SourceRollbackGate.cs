using IronDev.Core.Governance;

namespace IronDev.Core.SourceApply;

public static class SourceRollbackGate
{
    public static SourceRollbackGateDecision Evaluate(
        SourceRollbackRequest request,
        SourceApplyReceipt? applyReceipt,
        SourceSnapshot? currentSnapshot,
        ConscienceDecision? conscienceDecision,
        bool reversePatchCheckPassed,
        DateTimeOffset? now = null)
    {
        var reasons = new List<string>();

        if (applyReceipt is null)
        {
            reasons.Add("MissingSourceApplyReceipt");
        }
        else
        {
            if (!string.Equals(applyReceipt.SourceApplyReceiptId, request.SourceApplyReceiptId, StringComparison.OrdinalIgnoreCase))
                reasons.Add("SourceApplyReceiptMismatch");
            if (applyReceipt.Decision != SourceApplyReceiptDecision.AppliedToWorkingTree)
                reasons.Add("SourceApplyReceiptNotApplied");
            if (string.IsNullOrWhiteSpace(applyReceipt.PostApplyDiffSha256))
                reasons.Add("MissingPostApplyDiffHash");
            else if (!string.Equals(applyReceipt.PostApplyDiffSha256, request.CurrentDiffSha256, StringComparison.OrdinalIgnoreCase))
                reasons.Add("CurrentDiffDoesNotMatchApplyReceipt");
        }

        if (currentSnapshot is null)
        {
            reasons.Add("MissingCurrentSourceSnapshot");
        }
        else if (!string.Equals(currentSnapshot.HeadCommit, request.BaseCommit, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("SourceHeadMismatch");
        }

        if (!reversePatchCheckPassed)
            reasons.Add("ReversePatchCheckFailed");

        ValidateConscience(request, conscienceDecision, now, reasons);

        if (string.IsNullOrWhiteSpace(request.ThoughtLedgerRef))
            reasons.Add("MissingThoughtLedger");

        return new SourceRollbackGateDecision
        {
            SourceRollbackGateDecisionId = $"source_rollback_gate_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceRollbackRequestId = request.SourceRollbackRequestId,
            SourceApplyReceiptId = request.SourceApplyReceiptId,
            Decision = reasons.Count == 0 ? SourceRollbackGateDecisionOutcome.AllowRollback : SourceRollbackGateDecisionOutcome.Block,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    private static void ValidateConscience(SourceRollbackRequest request, ConscienceDecision? decision, DateTimeOffset? now, List<string> reasons)
    {
        if (decision is null)
        {
            reasons.Add("MissingConscienceDecision");
            return;
        }

        if (decision.ActionKind != GovernedActionKind.SourceRollback && decision.ActionKind != GovernedActionKind.RollbackExecution)
            reasons.Add("ConscienceDecisionActionMismatch");
        if (decision.Decision != ConscienceDecisionOutcome.Allow)
            reasons.Add("ConscienceDecisionDoesNotAllow");
        if (decision.ExpiresAtUtc is not null && decision.ExpiresAtUtc <= (now ?? DateTimeOffset.UtcNow))
            reasons.Add("ConscienceDecisionExpired");
        if (!string.Equals(decision.SubjectId, request.SourceRollbackRequestId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(decision.SubjectId, request.SourceApplyReceiptId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(decision.SubjectId, request.RunId, StringComparison.OrdinalIgnoreCase))
            reasons.Add("ConscienceDecisionSubjectMismatch");
    }
}

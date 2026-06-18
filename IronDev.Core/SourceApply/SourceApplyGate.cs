namespace IronDev.Core.SourceApply;

public static class SourceApplyGate
{
    public static SourceApplyGateDecision Evaluate(
        SourceApplyRequest request,
        PatchArtifactVerificationResult? verification,
        SourceApplyApprovalEvidence? approval,
        bool sourceRepoExists,
        bool sourceRepoClean,
        bool testEvidencePresent,
        bool toolEvidencePresent,
        bool governanceEvidencePresent,
        bool rollbackPlanDraftPresent)
    {
        var reasons = new List<string>();

        if (verification is null)
        {
            reasons.Add("MissingPatchVerification");
        }
        else if (verification.Decision != PatchArtifactVerificationDecision.Verified)
        {
            reasons.Add("PatchVerificationBlocked");
            reasons.AddRange(verification.Reasons);
        }

        if (approval is null)
        {
            reasons.Add("MissingApprovalEvidence");
        }
        else
        {
            if (!string.Equals(approval.RunId, request.RunId, StringComparison.OrdinalIgnoreCase))
                reasons.Add("ApprovalRunIdMismatch");
            if (!string.Equals(approval.SourceRepoIdentity, request.SourceRepoIdentity, StringComparison.OrdinalIgnoreCase))
                reasons.Add("ApprovalSourceRepoMismatch");
            if (!string.Equals(approval.BaseCommit, request.BaseCommit, StringComparison.OrdinalIgnoreCase))
                reasons.Add("ApprovalBaseCommitMismatch");
            if (!string.Equals(approval.PatchSha256, request.PatchSha256, StringComparison.OrdinalIgnoreCase))
                reasons.Add("ApprovalPatchHashMismatch");
            if (!SameSet(approval.ApprovedChangedFiles, request.ChangedFiles))
                reasons.Add("ApprovalChangedFilesMismatch");
            if (!approval.HumanReviewRequired || string.IsNullOrWhiteSpace(approval.ApprovedBy) || string.IsNullOrWhiteSpace(approval.ApprovalText) || approval.ApprovalText.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
                reasons.Add("ApprovalMissingHumanReviewer");
        }

        if (!sourceRepoExists)
            reasons.Add("SourceRepoMissing");
        if (!sourceRepoClean)
            reasons.Add("SourceRepoDirty");
        if (!rollbackPlanDraftPresent)
            reasons.Add("RollbackPlanMissing");
        if (!testEvidencePresent)
            reasons.Add("TestEvidenceMissing");
        if (!toolEvidencePresent)
            reasons.Add("ToolEvidenceMissing");
        if (!governanceEvidencePresent)
            reasons.Add("GovernanceEvidenceMissing");

        return new SourceApplyGateDecision
        {
            SourceApplyGateDecisionId = $"source_apply_gate_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceApplyRequestId = request.SourceApplyRequestId,
            PatchArtifactVerificationId = verification?.PatchArtifactVerificationId ?? string.Empty,
            ApprovalEvidenceId = approval?.ApprovalEvidenceId,
            Decision = reasons.Count == 0 ? SourceApplyGateDecisionOutcome.AllowDryRun : SourceApplyGateDecisionOutcome.Block,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    private static bool SameSet(string[] first, string[] second)
    {
        var normalizedFirst = first.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var normalizedSecond = second.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        return normalizedFirst.Length == normalizedSecond.Length && normalizedFirst.Zip(normalizedSecond).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
    }
}

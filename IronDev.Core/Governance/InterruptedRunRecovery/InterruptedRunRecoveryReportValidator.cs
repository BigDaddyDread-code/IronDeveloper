namespace IronDev.Core.Governance.InterruptedRunRecovery;

public static class InterruptedRunRecoveryReportValidator
{
    public static InterruptedRunRecoveryReportValidationResult Validate(InterruptedRunRecoveryReport? report)
    {
        var issues = new List<string>();
        if (report is null)
        {
            return new InterruptedRunRecoveryReportValidationResult
            {
                IsValid = false,
                Issues = ["InterruptedRunRecoveryReportRequired"]
            };
        }

        if (string.IsNullOrWhiteSpace(report.RunId))
            issues.Add("RunIdRequired");
        if (!Enum.IsDefined(report.DetectedStage))
            issues.Add("DetectedStageInvalid");
        if (!Enum.IsDefined(report.RecoveryState))
            issues.Add("RecoveryStateInvalid");
        if (report.CompletedEvidenceRefs is null)
            issues.Add("CompletedEvidenceRefsRequired");
        if (report.MissingEvidenceRefs is null)
            issues.Add("MissingEvidenceRefsRequired");
        if (report.BlockingReasons is null)
            issues.Add("BlockingReasonsRequired");
        if (report.NextSafeActions is null)
            issues.Add("NextSafeActionsRequired");
        else if (report.NextSafeActions.Any(IsVagueContinuationAction))
            issues.Add("NextSafeActionMustNotResumeOrContinueRun");

        ValidateBoundary(report.Boundary, issues);

        return new InterruptedRunRecoveryReportValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = Clean(issues)
        };
    }

    private static void ValidateBoundary(RunRecoveryBoundary? boundary, ICollection<string> issues)
    {
        if (boundary is null)
        {
            issues.Add("RunRecoveryBoundaryRequired");
            return;
        }

        if (!boundary.CanExplainState)
            issues.Add("CanExplainStateRequired");
        if (!boundary.CanInspectEvidence)
            issues.Add("CanInspectEvidenceRequired");
        if (boundary.CanResumeRun)
            issues.Add("CanResumeRunMustBeFalse");
        if (boundary.CanRetryStep)
            issues.Add("CanRetryStepMustBeFalse");
        if (boundary.CanApplySource)
            issues.Add("CanApplySourceMustBeFalse");
        if (boundary.CanRollbackSource)
            issues.Add("CanRollbackSourceMustBeFalse");
        if (boundary.CanCreateCommit)
            issues.Add("CanCreateCommitMustBeFalse");
        if (boundary.CanPush)
            issues.Add("CanPushMustBeFalse");
        if (boundary.CanCreatePullRequest)
            issues.Add("CanCreatePullRequestMustBeFalse");
        if (boundary.CanContinueWorkflow)
            issues.Add("CanContinueWorkflowMustBeFalse");
        if (boundary.CanPromoteMemory)
            issues.Add("CanPromoteMemoryMustBeFalse");
        if (boundary.CanSatisfyPolicy)
            issues.Add("CanSatisfyPolicyMustBeFalse");
        if (boundary.CanAcceptApproval)
            issues.Add("CanAcceptApprovalMustBeFalse");
    }

    private static bool IsVagueContinuationAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return normalized.Equals("continue run", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("resume run", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("retry step", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("continue workflow automatically", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("resume this run automatically", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

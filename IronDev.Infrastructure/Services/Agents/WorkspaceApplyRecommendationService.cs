using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class WorkspaceApplyRecommendationService : IWorkspaceApplyRecommendationService
{
    public WorkspaceApplyRecommendation Recommend(WorkspaceApplyRecommendationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Report);

        var report = request.Report;
        if (string.Equals(report.Outcome, "success", StringComparison.OrdinalIgnoreCase))
            return RecommendSuccess(report);

        if (string.Equals(report.Outcome, "failure", StringComparison.OrdinalIgnoreCase))
            return RecommendFailure(report);

        if (string.Equals(report.Outcome, "unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                report,
                WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport,
                "No usable source-report or failure-package evidence was available.",
                humanReviewRequired: true,
                safeToRetry: false,
                safeToCommitAfterReview: false,
                sourceReviewRequiredBeforeRetry: false);
        }

        return Build(
            report,
            WorkspaceApplyRecommendedActions.CollectMissingEvidence,
            "Workspace apply evidence is incomplete or inconsistent.",
            humanReviewRequired: true,
            safeToRetry: false,
            safeToCommitAfterReview: false,
            sourceReviewRequiredBeforeRetry: true,
            warnings: ["Workspace apply outcome was not recognized."]);
    }

    private static WorkspaceApplyRecommendation RecommendSuccess(WorkspaceApplyReportSummary report)
    {
        if (report.DeleteCount > 0)
        {
            return Build(
                report,
                WorkspaceApplyRecommendedActions.InspectFailurePackage,
                "Report contains delete operations, which are outside the current copy-only apply support.",
                humanReviewRequired: true,
                safeToRetry: false,
                safeToCommitAfterReview: false,
                sourceReviewRequiredBeforeRetry: true,
                warnings: ["Delete operations are outside the current copy-only apply support."]);
        }

        if (report.SourceRepoMutated &&
            report.ApplyVerified &&
            report.SourceMatchesWorkspace &&
            report.PostApplyValidationSucceeded)
        {
            return Build(
                report,
                WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
                "The governed workspace apply spine completed successfully. Human review is required before commit or PR creation.",
                humanReviewRequired: true,
                safeToRetry: false,
                safeToCommitAfterReview: true,
                sourceReviewRequiredBeforeRetry: false);
        }

        return Build(
            report,
            WorkspaceApplyRecommendedActions.CollectMissingEvidence,
            "Workspace apply evidence is incomplete or inconsistent.",
            humanReviewRequired: true,
            safeToRetry: false,
            safeToCommitAfterReview: false,
            sourceReviewRequiredBeforeRetry: true,
            warnings: ["Success outcome did not include the complete verified apply state."]);
    }

    private static WorkspaceApplyRecommendation RecommendFailure(WorkspaceApplyReportSummary report)
    {
        if (report.SourceRepoMutated && !report.ApplyVerified)
        {
            return Build(
                report,
                WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed,
                "Source repository may have been mutated, but apply verification did not complete.",
                humanReviewRequired: true,
                safeToRetry: false,
                safeToCommitAfterReview: false,
                sourceReviewRequiredBeforeRetry: true);
        }

        if (report.SourceRepoMutated &&
            report.ApplyVerified &&
            !report.PostApplyValidationSucceeded)
        {
            return Build(
                report,
                WorkspaceApplyRecommendedActions.FixValidationFailure,
                "Source mutation was verified, but post-apply validation did not succeed.",
                humanReviewRequired: true,
                safeToRetry: false,
                safeToCommitAfterReview: false,
                sourceReviewRequiredBeforeRetry: true);
        }

        if (!report.SourceRepoMutated)
        {
            return Build(
                report,
                WorkspaceApplyRecommendedActions.RetryAfterFixingBlockers,
                "No source mutation was reported. Fix blockers before retrying the governed spine.",
                humanReviewRequired: false,
                safeToRetry: true,
                safeToCommitAfterReview: false,
                sourceReviewRequiredBeforeRetry: false);
        }

        return Build(
            report,
            WorkspaceApplyRecommendedActions.CollectMissingEvidence,
            "Workspace apply evidence is incomplete or inconsistent.",
            humanReviewRequired: true,
            safeToRetry: false,
            safeToCommitAfterReview: false,
            sourceReviewRequiredBeforeRetry: true,
            warnings: ["Failure outcome did not match a supported deterministic recommendation path."]);
    }

    private static WorkspaceApplyRecommendation Build(
        WorkspaceApplyReportSummary report,
        string recommendedAction,
        string reason,
        bool humanReviewRequired,
        bool safeToRetry,
        bool safeToCommitAfterReview,
        bool sourceReviewRequiredBeforeRetry,
        IReadOnlyList<string>? warnings = null)
    {
        if (!WorkspaceApplyRecommendedActions.All.Contains(recommendedAction))
            throw new InvalidOperationException($"Unknown workspace apply recommendation action '{recommendedAction}'.");

        return new WorkspaceApplyRecommendation
        {
            RecommendedAction = recommendedAction,
            Reason = reason,
            HumanReviewRequired = humanReviewRequired,
            SafeToRetry = safeToRetry,
            SafeToCommitAfterReview = safeToCommitAfterReview,
            SourceReviewRequiredBeforeRetry = sourceReviewRequiredBeforeRetry,
            BlocksAutomaticExecution = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes,
            Warnings = [.. report.Warnings, .. warnings ?? []]
        };
    }
}

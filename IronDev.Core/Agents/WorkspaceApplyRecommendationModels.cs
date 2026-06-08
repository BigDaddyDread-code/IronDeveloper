namespace IronDev.Core.Agents;

public static class WorkspaceApplyRecommendedActions
{
    public const string HumanReviewOrCommit = "human_review_or_commit";
    public const string InspectFailurePackage = "inspect_failure_package";
    public const string DoNotRetryUntilSourceReviewed = "do_not_retry_until_source_reviewed";
    public const string FixValidationFailure = "fix_validation_failure";
    public const string RetryAfterFixingBlockers = "retry_after_fixing_blockers";
    public const string CollectMissingEvidence = "collect_missing_evidence";
    public const string NoWorkspaceApplyReport = "no_workspace_apply_report";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        HumanReviewOrCommit,
        InspectFailurePackage,
        DoNotRetryUntilSourceReviewed,
        FixValidationFailure,
        RetryAfterFixingBlockers,
        CollectMissingEvidence,
        NoWorkspaceApplyReport
    };
}

public sealed record WorkspaceApplyRecommendationRequest
{
    public required WorkspaceApplyReportSummary Report { get; init; }
}

public sealed record WorkspaceApplyRecommendation
{
    public required string RecommendedAction { get; init; }
    public required string Reason { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required bool SafeToRetry { get; init; }
    public required bool SafeToCommitAfterReview { get; init; }
    public required bool SourceReviewRequiredBeforeRetry { get; init; }
    public required bool BlocksAutomaticExecution { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

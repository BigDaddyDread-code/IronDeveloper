namespace IronDev.Core.Agents;

public static class WorkspaceApplyRequestedActions
{
    public const string HumanReviewSourceChanges = "human_review_source_changes";
    public const string InspectFailureEvidence = "inspect_failure_evidence";
    public const string ReviewSourceBeforeRetry = "review_source_before_retry";
    public const string FixValidationFailure = "fix_validation_failure";
    public const string RetryGovernedSpineAfterFixingBlockers = "retry_governed_spine_after_fixing_blockers";
    public const string CollectMissingEvidence = "collect_missing_evidence";
    public const string NoActionAvailable = "no_action_available";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        HumanReviewSourceChanges,
        InspectFailureEvidence,
        ReviewSourceBeforeRetry,
        FixValidationFailure,
        RetryGovernedSpineAfterFixingBlockers,
        CollectMissingEvidence,
        NoActionAvailable
    };
}

public sealed record WorkspaceApplyActionRequestInput
{
    public required WorkspaceApplyReportSummary Report { get; init; }
    public required WorkspaceApplyRecommendation Recommendation { get; init; }
}

public sealed record WorkspaceApplyActionRequest
{
    public required string RequestedAction { get; init; }
    public required string Reason { get; init; }
    public required bool HumanApprovalRequired { get; init; }
    public required bool AutomaticExecutionAllowed { get; init; }
    public required bool MutatesSourceRepo { get; init; }
    public required bool RequiresFreshHumanDecision { get; init; }
    public string? SuggestedCommand { get; init; }
    public IReadOnlyList<string> SuggestedCommandArguments { get; init; } = [];
    public IReadOnlyList<string> Preconditions { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

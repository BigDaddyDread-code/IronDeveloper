namespace IronDev.Core.Agents;

public static class WorkspaceApplyActionReviewStatuses
{
    public const string ReadyForHumanReview = "ready_for_human_review";
    public const string BlockedForEvidence = "blocked_for_evidence";
    public const string BlockedForSourceReview = "blocked_for_source_review";
    public const string BlockedForValidationFailure = "blocked_for_validation_failure";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ReadyForHumanReview,
        BlockedForEvidence,
        BlockedForSourceReview,
        BlockedForValidationFailure
    };
}

public sealed record WorkspaceApplyActionReviewInput
{
    public required WorkspaceApplyReportSummary Report { get; init; }
    public required WorkspaceApplyRecommendation Recommendation { get; init; }
    public required WorkspaceApplyActionRequest ActionRequest { get; init; }
}

public sealed record WorkspaceApplyActionReview
{
    public required string ReviewStatus { get; init; }
    public required string Summary { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required bool ApprovalCanBeGrantedByThisPackage { get; init; }
    public required bool ExecutionCanStartFromThisPackage { get; init; }
    public required bool SourceRepoMayBeMutated { get; init; }
    public required string RequestedAction { get; init; }
    public required string RecommendedAction { get; init; }
    public IReadOnlyList<string> ReviewChecklist { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

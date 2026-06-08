namespace IronDev.Core.Agents.Skills;

public static class AgentSkillRequestContextRecommendedActions
{
    public const string NoActionAvailable = "no_action_available";
    public const string ReviewRequest = "review_request";
    public const string CollectMissingEvidence = "collect_missing_evidence";
    public const string RequestSeparateApproval = "request_separate_approval";
    public const string StopBlockedByPolicy = "stop_blocked_by_policy";
    public const string StopUnknownSkill = "stop_unknown_skill";
    public const string StopDangerousCapability = "stop_dangerous_capability";
}

public sealed record AgentSkillRequestContextInput
{
    public required AgentSkillRequestPackage RequestPackage { get; init; }
    public required AgentSkillRequestReview ReviewPackage { get; init; }
}

public sealed record AgentSkillRequestContext
{
    public required string ContextId { get; init; }
    public required string RequestId { get; init; }
    public required string ReviewId { get; init; }
    public required string ProjectId { get; init; }
    public required string AgentName { get; init; }
    public required string SkillId { get; init; }
    public required string Purpose { get; init; }
    public required bool SkillKnown { get; init; }
    public required string Decision { get; init; }
    public required string ReviewStatus { get; init; }
    public required string RiskTier { get; init; }
    public required string Category { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required bool HumanApprovalRequired { get; init; }
    public required bool PolicyAllowed { get; init; }
    public required bool PolicyBlocked { get; init; }
    public required bool DangerousCapability { get; init; }
    public required bool ExecutionCanStartFromContext { get; init; }
    public required bool ApprovalCanBeGrantedByContext { get; init; }
    public required bool SourceMutationAllowed { get; init; }
    public required bool WorkspaceMutationAllowed { get; init; }
    public required bool ExternalSystemAllowed { get; init; }
    public required bool CreatesTicketAllowed { get; init; }
    public required bool WritesMemoryAllowed { get; init; }
    public required string RecommendedNextAction { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> ParametersSummary { get; init; } = [];
    public IReadOnlyList<string> ReviewChecklist { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Interpretation { get; init; } = [];
}

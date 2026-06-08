namespace IronDev.Core.Agents.Skills;

public static class AgentSkillRequestReviewStatuses
{
    public const string ReadyForHumanReview = "ready_for_human_review";
    public const string ApprovalRequired = "approval_required";
    public const string BlockedByPolicy = "blocked_by_policy";
    public const string BlockedForUnknownSkill = "blocked_for_unknown_skill";
    public const string BlockedForDangerousCapability = "blocked_for_dangerous_capability";
}

public sealed record AgentSkillRequestReviewInput
{
    public required AgentSkillRequestPackage RequestPackage { get; init; }
}

public sealed record AgentSkillRequestReview
{
    public required string ReviewId { get; init; }
    public required string RequestId { get; init; }
    public required string ProjectId { get; init; }
    public required string AgentName { get; init; }
    public required string SkillId { get; init; }
    public required string Purpose { get; init; }
    public required string ReviewStatus { get; init; }
    public required string Summary { get; init; }
    public required string Decision { get; init; }
    public required string RiskTier { get; init; }
    public required string Category { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required bool HumanApprovalRequired { get; init; }
    public required bool ApprovalCanBeGrantedByReview { get; init; }
    public required bool ExecutionCanStartFromReview { get; init; }
    public required bool SourceMutationAllowed { get; init; }
    public required bool WorkspaceMutationAllowed { get; init; }
    public required bool ExternalSystemAllowed { get; init; }
    public required bool CreatesTicketAllowed { get; init; }
    public required bool WritesMemoryAllowed { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> ParametersSummary { get; init; } = [];
    public IReadOnlyList<string> ReviewChecklist { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

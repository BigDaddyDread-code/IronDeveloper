using IronDev.Core.Agents;

namespace IronDev.Core.Agents.ApprovalPolicy;

public static class WorkspaceApplyPolicyActionTypes
{
    public const string WorkspaceApplyActionReview = "workspace_apply_action_review";
}

public sealed record WorkspaceApplyPolicyContextInput
{
    public required string ProjectId { get; init; }
    public required WorkspaceApplyReportSummary Report { get; init; }
    public required WorkspaceApplyRecommendation Recommendation { get; init; }
    public required WorkspaceApplyActionRequest ActionRequest { get; init; }
    public required WorkspaceApplyActionReview ActionReview { get; init; }
    public required ProjectApprovalPolicy Policy { get; init; }
}

public sealed record WorkspaceApplyPolicyContext
{
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public required string RiskTier { get; init; }
    public required string ActionType { get; init; }
    public required string RequestedAction { get; init; }
    public required bool HumanApprovalRequired { get; init; }
    public required bool AutomaticExecutionAllowed { get; init; }
    public required bool SourceMutationAllowed { get; init; }
    public required bool ExecutionCanStartFromPolicyContext { get; init; }
    public required bool ApprovalCanBeGrantedByPolicyContext { get; init; }
    public string? MatchedRuleDescription { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

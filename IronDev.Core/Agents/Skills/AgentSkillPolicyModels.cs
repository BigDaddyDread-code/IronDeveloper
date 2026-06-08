using IronDev.Core.Agents.ApprovalPolicy;

namespace IronDev.Core.Agents.Skills;

public static class AgentSkillPolicyActionTypes
{
    public const string AgentSkill = "agent_skill";
}

public sealed record AgentSkillPolicyEvaluationRequest
{
    public required string ProjectId { get; init; }
    public required string SkillId { get; init; }
    public required ProjectApprovalPolicy Policy { get; init; }
    public string? RequestedAction { get; init; }
    public string? EvidenceHash { get; init; }
    public string? RunId { get; init; }
    public string? WorkspacePath { get; init; }
    public string? SourceRepo { get; init; }
}

public sealed record AgentSkillPolicyEvaluation
{
    public required string SkillId { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public required string RiskTier { get; init; }
    public required string Category { get; init; }
    public required bool SkillKnown { get; init; }
    public required bool HumanApprovalRequired { get; init; }
    public required bool AutomaticExecutionAllowed { get; init; }
    public required bool SkillExecutionAllowedByPolicy { get; init; }
    public required bool SourceMutationAllowed { get; init; }
    public required bool WorkspaceMutationAllowed { get; init; }
    public required bool ExternalSystemAllowed { get; init; }
    public required bool CreatesTicketAllowed { get; init; }
    public required bool WritesMemoryAllowed { get; init; }
    public string? MatchedRuleDescription { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

namespace IronDev.Core.Agents.Skills;

public static class AgentSkillApprovalDecisions
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Invalid = "invalid";
}

public static class AgentSkillApprovalActorKinds
{
    public const string Human = "human";
    public const string SystemTestFixture = "system_test_fixture";
    public const string Unknown = "unknown";
}

public sealed record AgentSkillApprovalEvidence
{
    public required bool ApprovalEvidenceAvailable { get; init; }
    public required string ApprovalId { get; init; }
    public required string ProjectId { get; init; }
    public required string RequestId { get; init; }
    public required string ReviewId { get; init; }
    public required string SkillId { get; init; }
    public required string ApprovedAction { get; init; }
    public required string ApprovedBy { get; init; }
    public required string ApprovedByKind { get; init; }
    public required DateTimeOffset ApprovedUtc { get; init; }
    public DateTimeOffset? ExpiresUtc { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public required bool AllowsExecution { get; init; }
    public required bool AllowsSourceMutation { get; init; }
    public required bool AllowsWorkspaceMutation { get; init; }
    public required bool AllowsExternalSystem { get; init; }
    public required bool AllowsTicketCreation { get; init; }
    public required bool AllowsMemoryWrite { get; init; }
    public required bool AllowsGitOperation { get; init; }
    public required bool AllowsGithubOperation { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record AgentSkillApprovalEvidenceBinding
{
    public required bool BindingValid { get; init; }
    public required string ApprovalId { get; init; }
    public required string RequestId { get; init; }
    public required string ReviewId { get; init; }
    public required string SkillId { get; init; }
    public required bool AllowsExecution { get; init; }
    public required bool AllowsWorkspaceMutation { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record AgentSkillApprovalEvidenceBindingRequest
{
    public required AgentSkillRequestPackage RequestPackage { get; init; }
    public required AgentSkillRequestReview ReviewPackage { get; init; }
    public AgentSkillApprovalEvidence? ApprovalEvidence { get; init; }
    public DateTimeOffset? NowUtc { get; init; }
}

public interface IAgentSkillApprovalEvidenceBinder
{
    AgentSkillApprovalEvidenceBinding Bind(AgentSkillApprovalEvidenceBindingRequest request);
}

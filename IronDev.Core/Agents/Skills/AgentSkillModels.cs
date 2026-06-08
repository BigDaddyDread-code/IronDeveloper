using IronDev.Core.Agents.ApprovalPolicy;

namespace IronDev.Core.Agents.Skills;

public static class AgentSkillCategories
{
    public const string WorkspaceContext = "workspace_context";
    public const string WorkspaceCommand = "workspace_command";
    public const string WorkspaceApply = "workspace_apply";
    public const string AgentReview = "agent_review";
    public const string Memory = "memory";
    public const string Ticketing = "ticketing";
    public const string Git = "git";
    public const string External = "external";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkspaceContext,
        WorkspaceCommand,
        WorkspaceApply,
        AgentReview,
        Memory,
        Ticketing,
        Git,
        External
    };
}

public static class AgentSkillIds
{
    public const string WorkspaceReadApplyContext = "workspace.read_apply_context";
    public const string WorkspaceReadSourceReport = "workspace.read_source_report";
    public const string WorkspaceReadFailurePackage = "workspace.read_failure_package";

    public const string WorkspaceRecommendApplyAction = "workspace.recommend_apply_action";
    public const string WorkspaceCreateActionRequest = "workspace.create_action_request";
    public const string WorkspaceCreateActionReview = "workspace.create_action_review";
    public const string WorkspaceEvaluatePolicyContext = "workspace.evaluate_policy_context";

    public const string WorkspaceCheck = "workspace.check";
    public const string WorkspacePrepare = "workspace.prepare";
    public const string WorkspaceValidate = "workspace.validate";
    public const string WorkspaceDiff = "workspace.diff";
    public const string WorkspacePromotionPackage = "workspace.promotion_package";
    public const string WorkspaceFailurePackage = "workspace.failure_package";
    public const string WorkspaceSourceReport = "workspace.source_report";

    public const string WorkspaceApplyCopy = "workspace.apply_copy";

    public const string MemorySearch = "memory.search";
    public const string TicketCreate = "ticket.create";
    public const string GitCommit = "git.commit";
    public const string GitHubPullRequestCreate = "github.pull_request.create";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkspaceReadApplyContext,
        WorkspaceReadSourceReport,
        WorkspaceReadFailurePackage,
        WorkspaceRecommendApplyAction,
        WorkspaceCreateActionRequest,
        WorkspaceCreateActionReview,
        WorkspaceEvaluatePolicyContext,
        WorkspaceCheck,
        WorkspacePrepare,
        WorkspaceValidate,
        WorkspaceDiff,
        WorkspacePromotionPackage,
        WorkspaceFailurePackage,
        WorkspaceSourceReport,
        WorkspaceApplyCopy,
        MemorySearch,
        TicketCreate,
        GitCommit,
        GitHubPullRequestCreate
    };
}

public sealed record AgentSkillDefinition
{
    public required string SkillId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string RiskTier { get; init; }
    public required bool CanReadEvidence { get; init; }
    public required bool CanExecuteProcess { get; init; }
    public required bool CanMutateWorkspace { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanWriteMemory { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanUseExternalSystem { get; init; }
    public required bool RequiresHumanApproval { get; init; }
    public IReadOnlyList<string> ReadsEvidenceTypes { get; init; } = [];
    public IReadOnlyList<string> ProducesEvidenceTypes { get; init; } = [];
    public string? InputContract { get; init; }
    public string? OutputContract { get; init; }

    public bool HasKnownCategory => AgentSkillCategories.All.Contains(Category);

    public bool HasKnownRiskTier => ProjectApprovalRiskTiers.All.Contains(RiskTier);
}

namespace IronDev.Core.Agents.ApprovalPolicy;

public static class ProjectApprovalDecisions
{
    public const string AllowedByPolicy = "allowed_by_policy";
    public const string ApprovalRequired = "approval_required";
    public const string BlockedByPolicy = "blocked_by_policy";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AllowedByPolicy,
        ApprovalRequired,
        BlockedByPolicy
    };
}

public static class ProjectApprovalRiskTiers
{
    public const string ReadOnly = "read_only";
    public const string WorkspacePreparation = "workspace_preparation";
    public const string WorkspaceValidation = "workspace_validation";
    public const string WorkspaceReporting = "workspace_reporting";
    public const string WorkspaceIntent = "workspace_intent";
    public const string SourceMutation = "source_mutation";
    public const string GitOperation = "git_operation";
    public const string MemoryWrite = "memory_write";
    public const string TicketWrite = "ticket_write";
    public const string ExternalSystem = "external_system";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ReadOnly,
        WorkspacePreparation,
        WorkspaceValidation,
        WorkspaceReporting,
        WorkspaceIntent,
        SourceMutation,
        GitOperation,
        MemoryWrite,
        TicketWrite,
        ExternalSystem
    };

    private static readonly IReadOnlySet<string> LowRiskAutoAllowed = new HashSet<string>(StringComparer.Ordinal)
    {
        ReadOnly,
        WorkspaceReporting,
        WorkspaceIntent
    };

    private static readonly IReadOnlySet<string> ApprovalOnlyWhenAllowed = new HashSet<string>(StringComparer.Ordinal)
    {
        SourceMutation,
        GitOperation,
        MemoryWrite,
        TicketWrite
    };

    public static bool IsKnown(string riskTier) => All.Contains(riskTier);

    public static bool CanAutoExecuteWhenAllowed(string riskTier) => LowRiskAutoAllowed.Contains(riskTier);

    public static bool RequiresApprovalWhenAlwaysAllowed(string riskTier) => ApprovalOnlyWhenAllowed.Contains(riskTier);

    public static bool IsExternalSystem(string riskTier) =>
        string.Equals(riskTier, ExternalSystem, StringComparison.Ordinal);
}

public static class ProjectApprovalModes
{
    public const string AlwaysAllow = "always_allow";
    public const string AskEveryTime = "ask_every_time";
    public const string AlwaysBlock = "always_block";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AlwaysAllow,
        AskEveryTime,
        AlwaysBlock
    };
}

public sealed record ProjectApprovalPolicy
{
    public required string ProjectId { get; init; }
    public required string DefaultMode { get; init; }
    public IReadOnlyList<ProjectApprovalPolicyRule> Rules { get; init; } = [];

    public static ProjectApprovalPolicy CreateDefault(string projectId) =>
        new()
        {
            ProjectId = projectId,
            DefaultMode = ProjectApprovalModes.AskEveryTime,
            Rules =
            [
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.ReadOnly, Mode = ProjectApprovalModes.AlwaysAllow },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting, Mode = ProjectApprovalModes.AlwaysAllow },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent, Mode = ProjectApprovalModes.AlwaysAllow },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.WorkspacePreparation, Mode = ProjectApprovalModes.AskEveryTime },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.WorkspaceValidation, Mode = ProjectApprovalModes.AskEveryTime },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.SourceMutation, Mode = ProjectApprovalModes.AskEveryTime },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.GitOperation, Mode = ProjectApprovalModes.AlwaysBlock },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.MemoryWrite, Mode = ProjectApprovalModes.AskEveryTime },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.TicketWrite, Mode = ProjectApprovalModes.AskEveryTime },
                new ProjectApprovalPolicyRule { RiskTier = ProjectApprovalRiskTiers.ExternalSystem, Mode = ProjectApprovalModes.AlwaysBlock }
            ]
        };
}

public sealed record ProjectApprovalPolicyRule
{
    public required string RiskTier { get; init; }
    public string? ActionType { get; init; }
    public string? RequestedAction { get; init; }
    public required string Mode { get; init; }
    public string? Reason { get; init; }
}

public sealed record ProjectApprovalEvaluationRequest
{
    public required string ProjectId { get; init; }
    public required string RiskTier { get; init; }
    public string? ActionType { get; init; }
    public string? RequestedAction { get; init; }
    public string? EvidenceHash { get; init; }
    public string? RunId { get; init; }
    public string? WorkspacePath { get; init; }
    public string? SourceRepo { get; init; }
    public required ProjectApprovalPolicy Policy { get; init; }
}

public sealed record ProjectApprovalEvaluationResult
{
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public required string AppliedMode { get; init; }
    public string? MatchedRuleDescription { get; init; }
    public required bool HumanApprovalRequired { get; init; }
    public required bool AutomaticExecutionAllowed { get; init; }
    public required bool SourceMutationAllowed { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

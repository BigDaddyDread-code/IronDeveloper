namespace IronDev.Core.Agents.Skills;

public static class AgentSkillExecutionStatuses
{
    public const string Succeeded = "succeeded";
    public const string BlockedByContext = "blocked_by_context";
    public const string BlockedByPolicy = "blocked_by_policy";
    public const string BlockedUnknownSkill = "blocked_unknown_skill";
    public const string BlockedDangerousCapability = "blocked_dangerous_capability";
    public const string BlockedUnsupportedSkill = "blocked_unsupported_skill";
    public const string Failed = "failed";
}

public sealed record AgentSkillExecutionRequest
{
    public required AgentSkillRequestContext SkillRequestContext { get; init; }

    public required string RequestedByAgent { get; init; }

    public string? ProjectId { get; init; }

    public string? RunId { get; init; }

    public string? WorkspacePath { get; init; }

    public string? SourceRepo { get; init; }

    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AgentSkillWorkspaceApplyContextExecutionPayload
{
    public required bool WorkspaceApplyContextAvailable { get; init; }

    public required string RunId { get; init; }

    public required string WorkspacePath { get; init; }

    public string? Outcome { get; init; }

    public string? RecommendedAction { get; init; }

    public string? RequestedAction { get; init; }

    public string? ReviewStatus { get; init; }

    public string? PolicyDecision { get; init; }

    public string? RiskTier { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AgentSkillExecutionResult
{
    public required string ExecutionId { get; init; }

    public required string ContextId { get; init; }

    public required string RequestId { get; init; }

    public required string ReviewId { get; init; }

    public required string SkillId { get; init; }

    public required string Status { get; init; }

    public required string Summary { get; init; }

    public required bool Executed { get; init; }

    public required bool ReadOnlyExecution { get; init; }

    public required bool SourceMutated { get; init; }

    public required bool WorkspaceMutated { get; init; }

    public required bool ExternalSystemCalled { get; init; }

    public required bool TicketCreated { get; init; }

    public required bool MemoryWritten { get; init; }

    public required bool ApprovalGranted { get; init; }

    public required bool ShellCommandRun { get; init; }

    public object? Payload { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];
}

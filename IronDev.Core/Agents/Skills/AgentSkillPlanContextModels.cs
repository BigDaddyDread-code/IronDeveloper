namespace IronDev.Core.Agents.Skills;

public static class AgentSkillPlanStepStatuses
{
    public const string Planned = "planned";
    public const string Ready = "ready";
    public const string InProgress = "in_progress";
    public const string Satisfied = "satisfied";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Unknown = "unknown";
}

public sealed record AgentSkillPlanContext
{
    public required bool PlanContextAvailable { get; init; }
    public required string BindingId { get; init; }
    public required string ProjectId { get; init; }
    public required string SkillId { get; init; }
    public required string PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? PlanTitle { get; init; }
    public string? PlanSourceKind { get; init; }
    public string? PlanSourceId { get; init; }
    public string? CurrentStepId { get; init; }
    public string? CurrentStepTitle { get; init; }
    public required string RequestedAction { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<AgentSkillPlanContextStep> Steps { get; init; } = [];
    public IReadOnlyList<string> DependencyStepIds { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public required bool CanApprove { get; init; }
    public required bool CanExecute { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanMutateWorkspace { get; init; }
    public required bool CanWriteMemory { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanUseExternalSystem { get; init; }
    public required bool CanChangePolicy { get; init; }
}

public sealed record AgentSkillPlanContextStep
{
    public required string StepId { get; init; }
    public string? ParentStepId { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string IntendedSkillId { get; init; }
    public string? RequestedAction { get; init; }
    public IReadOnlyList<string> DependsOnStepIds { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public required bool IsCurrentStep { get; init; }
    public required bool IsSatisfied { get; init; }
    public required bool IsBlocked { get; init; }
}

public sealed record AgentSkillPlanContextBindingRequest
{
    public required string ProjectId { get; init; }
    public required string SkillId { get; init; }
    public required string RequestedAction { get; init; }
    public required string Purpose { get; init; }
    public string? PlanId { get; init; }
    public string? CurrentStepId { get; init; }
    public IReadOnlyList<AgentSkillPlanContextStep> Steps { get; init; } = [];
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
}

public interface IAgentSkillPlanContextBinder
{
    AgentSkillPlanContext Bind(AgentSkillPlanContextBindingRequest request);
}

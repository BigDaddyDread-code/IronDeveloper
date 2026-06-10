using IronDev.Core.Agents.Skills;

namespace IronDev.Core.AgentMemory.Execution;

public enum MemoryExecutionAuditOutcome
{
    BlockedByMemory = 1,
    BlockedByPolicy = 2,
    BlockedByApproval = 3,
    BlockedByContext = 4,
    ExecutedSucceeded = 5,
    ExecutedFailed = 6,
    ExecutedBlockedByTool = 7
}

public sealed record MemoryExecutionAuditRecord
{
    public required string AuditId { get; init; }

    public required AgentMemoryScope Scope { get; init; }

    public required string ExecutionId { get; init; }

    public required string ContextId { get; init; }

    public required string RequestId { get; init; }

    public required string ReviewId { get; init; }

    public required string SkillId { get; init; }

    public required string DecisionId { get; init; }

    public required MemoryGovernanceActionType ActionType { get; init; }

    public required MemoryExecutionAuditOutcome Outcome { get; init; }

    public required string ExecutionStatus { get; init; }

    public required MemoryExecutionGateDecision GateDecision { get; init; }

    public MemoryGovernanceDecision? GovernanceDecision { get; init; }

    public string? GovernanceCheckId { get; init; }

    public required bool Executed { get; init; }

    public required bool SourceMutated { get; init; }

    public required bool WorkspaceMutated { get; init; }

    public required bool ExternalSystemCalled { get; init; }

    public required bool TicketCreated { get; init; }

    public required bool MemoryWritten { get; init; }

    public required bool ApprovalGranted { get; init; }

    public required bool ShellCommandRun { get; init; }

    public string? ToolName { get; init; }

    public string? AffectedArtifactType { get; init; }

    public string? AffectedArtifactId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<string> MemoryItemIds { get; init; }

    public required IReadOnlyList<string> InfluenceIds { get; init; }

    public required IReadOnlyList<string> HandoffMemorySliceIds { get; init; }

    public required IReadOnlyList<string> EvidencePaths { get; init; }

    public required IReadOnlyList<string> Blockers { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<MemoryGovernanceIssueCode> IssueCodes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record MemoryExecutionAuditDraft
{
    public required AgentSkillExecutionRequest Request { get; init; }

    public required AgentSkillExecutionResult Result { get; init; }

    public required MemoryExecutionGateResult GateResult { get; init; }

    public required MemoryExecutionAuditOutcome Outcome { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record MemoryExecutionAuditQuery
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public required string RunId { get; init; }

    public string? AgentId { get; init; }

    public string? ExecutionId { get; init; }

    public string? DecisionId { get; init; }

    public string? GovernanceCheckId { get; init; }

    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }

    public int Take { get; init; } = 100;
}

public interface IMemoryExecutionAuditStore
{
    Task<MemoryExecutionAuditRecord> AppendAsync(
        MemoryExecutionAuditDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryExecutionAuditRecord>> QueryAsync(
        MemoryExecutionAuditQuery query,
        CancellationToken cancellationToken = default);
}

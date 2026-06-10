using IronDev.Core.AgentMemory;

namespace IronDev.Core.AgentMemory.Execution;

public enum MemoryExecutionGateDecision
{
    NotMemoryBacked = 0,
    Allowed = 1,
    WarningRequiresOuterApproval = 2,
    Blocked = 3
}

public sealed record MemoryBackedExecutionReference
{
    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }

    public string? ProposalId { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }
}

public sealed record MemoryBackedExecutionContext
{
    public required AgentMemoryScope Scope { get; init; }

    public required MemoryGovernanceActionType ActionType { get; init; }

    public required string DecisionId { get; init; }

    public required IReadOnlyList<MemoryBackedExecutionReference> ReferencedArtifacts { get; init; }

    public bool RequireGovernanceCheck { get; init; } = true;

    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? TargetAgentId { get; init; }

    public string? ToolName { get; init; }

    public string? AffectedArtifactType { get; init; }

    public string? AffectedArtifactId { get; init; }

    public string? CorrelationId { get; init; }

    public bool InfluenceRecordRequired { get; init; } = true;

    public MemoryGovernanceCheckResult? SuppliedGovernanceResult { get; init; }
}

public sealed record MemoryExecutionEvidence
{
    public required bool IsMemoryBacked { get; init; }

    public string? GovernanceCheckId { get; init; }

    public string? DecisionId { get; init; }

    public required MemoryExecutionGateDecision GateDecision { get; init; }

    public MemoryGovernanceDecision? GovernanceDecision { get; init; }

    public IReadOnlyList<MemoryGovernanceIssueCode> IssueCodes { get; init; } = [];

    public IReadOnlyList<string> MemoryItemIds { get; init; } = [];

    public IReadOnlyList<string> InfluenceIds { get; init; } = [];

    public IReadOnlyList<string> HandoffMemorySliceIds { get; init; } = [];
}

public sealed record MemoryExecutionGateResult
{
    public required MemoryExecutionGateDecision Decision { get; init; }

    public required bool MayProceedToPolicyGate { get; init; }

    public required string Summary { get; init; }

    public required MemoryExecutionEvidence Evidence { get; init; }

    public MemoryGovernanceCheckResult? GovernanceResult { get; init; }

    public IReadOnlyList<MemoryGovernanceIssue> Issues { get; init; } = [];
}

public interface IMemoryExecutionGate
{
    Task<MemoryExecutionGateResult> EvaluateAsync(
        MemoryBackedExecutionContext? context,
        CancellationToken cancellationToken = default);
}

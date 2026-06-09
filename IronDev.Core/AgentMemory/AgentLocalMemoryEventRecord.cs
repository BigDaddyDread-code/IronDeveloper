namespace IronDev.Core.AgentMemory;

public sealed record AgentLocalMemoryEventRecord
{
    public required string MemoryEventId { get; init; }

    public required string MemoryItemId { get; init; }

    public required AgentLocalMemoryEventType EventType { get; init; }

    public string? EventReason { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? CreatedByAgentId { get; init; }

    public string? CreatedByUserId { get; init; }

    public string? CorrelationId { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? EventJson { get; init; }
}

namespace IronDev.Core.AgentMemory;

public sealed record MemoryInfluenceRecord
{
    public required string InfluenceId { get; init; }

    public required string MemoryItemId { get; init; }

    public required AgentMemoryScope Scope { get; init; }

    public required string DecisionId { get; init; }

    public required MemoryInfluenceType InfluenceType { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required decimal Confidence { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

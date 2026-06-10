namespace IronDev.Core.AgentMemory;

public sealed record MemoryInfluenceDraft
{
    public required string InfluenceId { get; init; }

    public required string MemoryItemId { get; init; }

    public required string DecisionId { get; init; }

    public required MemoryInfluenceType InfluenceType { get; init; }

    public required string InfluenceSummary { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required decimal Confidence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? AffectedArtifactType { get; init; }

    public string? AffectedArtifactId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public string? InfluenceJson { get; init; }
}

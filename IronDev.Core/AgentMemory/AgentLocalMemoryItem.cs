namespace IronDev.Core.AgentMemory;

public sealed record AgentLocalMemoryItem
{
    public required string MemoryItemId { get; init; }

    public required AgentMemoryScope Scope { get; init; }

    public required AgentMemoryType MemoryType { get; init; }

    public required MemoryAuthorityLevel AuthorityLevel { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required decimal Confidence { get; init; }

    public required MemoryLifecycleStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? SupersedesMemoryItemId { get; init; }

    public string? KnownLimitations { get; init; }
}

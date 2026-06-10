namespace IronDev.Core.AgentMemory;

public sealed record MemoryInfluenceQuery
{
    public string? MemoryItemId { get; init; }

    public string? DecisionId { get; init; }

    public MemoryInfluenceType? InfluenceType { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }

    public int Take { get; init; } = 50;
}

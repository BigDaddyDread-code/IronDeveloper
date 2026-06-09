namespace IronDev.Core.AgentMemory;

public sealed record AgentLocalMemoryQuery
{
    public AgentMemoryType? MemoryType { get; init; }

    public MemoryAuthorityLevel? AuthorityLevel { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }

    public bool IncludeExpired { get; init; }

    public int Take { get; init; } = 50;
}

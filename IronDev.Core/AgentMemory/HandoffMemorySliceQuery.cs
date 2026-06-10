namespace IronDev.Core.AgentMemory;

public sealed record HandoffMemorySliceQuery
{
    public string? SourceAgentId { get; init; }

    public string? TargetAgentId { get; init; }

    public HandoffMemoryAllowedUse? AllowedUse { get; init; }

    public bool IncludeExpired { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }

    public int Take { get; init; } = 50;
}

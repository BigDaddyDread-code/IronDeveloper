namespace IronDev.Core.AgentMemory;

public sealed record HandoffMemoryItemSnapshot
{
    public required string MemoryItemId { get; init; }

    public required AgentMemoryType MemoryType { get; init; }

    public required MemoryAuthorityLevel AuthorityLevelAtHandoff { get; init; }

    public required MemoryLifecycleStatus StatusAtHandoff { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required decimal Confidence { get; init; }
}

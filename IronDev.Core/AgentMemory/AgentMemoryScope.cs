namespace IronDev.Core.AgentMemory;

public sealed record AgentMemoryScope
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public required string RunId { get; init; }

    public required string AgentId { get; init; }
}

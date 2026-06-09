namespace IronDev.Core.AgentMemory;

public sealed record AgentMemorySiloContext
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public required string RunId { get; init; }

    public required string AgentId { get; init; }

    public string? WorkflowId { get; init; }

    public string? TicketId { get; init; }

    public string? CorrelationId { get; init; }
}

namespace IronDev.Core.AgentMemory;

public sealed record HandoffMemorySliceRecord
{
    public required string HandoffMemorySliceId { get; init; }

    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public required string RunId { get; init; }

    public required string SourceAgentId { get; init; }

    public required string TargetAgentId { get; init; }

    public required IReadOnlyList<string> MemoryItemIds { get; init; }

    public required string Summary { get; init; }

    public required HandoffMemoryAllowedUse AllowedUse { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required decimal Confidence { get; init; }

    public required IReadOnlyList<HandoffMemoryItemSnapshot> MemorySnapshots { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<string>? InfluenceIds { get; init; }

    public string? DecisionId { get; init; }

    public string? CorrelationId { get; init; }

    public string? HandoffJson { get; init; }
}

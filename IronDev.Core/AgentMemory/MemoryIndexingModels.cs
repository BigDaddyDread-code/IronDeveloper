namespace IronDev.Core.AgentMemory;

public enum MemoryIndexArtifactType
{
    RunMemoryReport = 1,
    AgentRunMemoryReport = 2,
    MemoryImprovementProposal = 3,
    MemoryImprovementProposalEvent = 4,
    MemoryInfluenceSummary = 5,
    HandoffSummary = 6
}

public enum MemoryIndexAuthorityLevel
{
    ObservedProjection = 1,
    ReviewQueue = 2,
    ReviewedPositive = 3,
    Rejected = 4,
    Deprecated = 5
}

public enum MemoryIndexStatus
{
    Pending = 1,
    Indexed = 2,
    Failed = 3,
    Superseded = 4,
    Skipped = 5
}

public enum MemoryIndexEventType
{
    Queued = 1,
    Indexed = 2,
    Failed = 3,
    Superseded = 4,
    Skipped = 5
}

public sealed record MemoryIndexProjection
{
    public required string IndexRecordId { get; init; }

    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public string? RunId { get; init; }

    public string? AgentId { get; init; }

    public required MemoryIndexArtifactType ArtifactType { get; init; }

    public required string ArtifactId { get; init; }

    public required MemoryIndexAuthorityLevel AuthorityLevel { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public string? SourceHashSha256 { get; init; }
}

public sealed record MemoryIndexQueueRecord
{
    public required string IndexRecordId { get; init; }

    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public string? RunId { get; init; }

    public string? AgentId { get; init; }

    public required MemoryIndexArtifactType ArtifactType { get; init; }

    public required string ArtifactId { get; init; }

    public required MemoryIndexAuthorityLevel AuthorityLevel { get; init; }

    public required MemoryIndexStatus Status { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? IndexedAt { get; init; }

    public string? WeaviateObjectId { get; init; }

    public string? LastError { get; init; }

    public string? SourceHashSha256 { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WeaviateMemoryIndexResult
{
    public required bool Success { get; init; }

    public string? WeaviateObjectId { get; init; }

    public string? Error { get; init; }
}

public interface IMemoryIndexProjectionBuilder
{
    Task<IReadOnlyList<MemoryIndexProjection>> BuildRunProjectionsAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryIndexProjection>> BuildProposalProjectionsAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        CancellationToken cancellationToken = default);
}

public interface IMemoryIndexQueueStore
{
    Task QueueAsync(
        MemoryIndexProjection projection,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryIndexQueueRecord>> QueryPendingAsync(
        string tenantId,
        string projectId,
        int take,
        CancellationToken cancellationToken = default);

    Task AddEventAsync(
        string indexRecordId,
        MemoryIndexEventType eventType,
        string? weaviateObjectId = null,
        string? error = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryIndexQueueRecord>> QueryAsync(
        string tenantId,
        string projectId,
        string? campaignId,
        string? runId,
        MemoryIndexStatus? status,
        int take,
        CancellationToken cancellationToken = default);
}

public interface IWeaviateMemoryIndexer
{
    Task<WeaviateMemoryIndexResult> IndexAsync(
        MemoryIndexProjection projection,
        CancellationToken cancellationToken = default);
}

public interface IMemoryIndexingService
{
    Task QueueRunAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        CancellationToken cancellationToken = default);

    Task<int> ProcessPendingAsync(
        string tenantId,
        string projectId,
        int take,
        CancellationToken cancellationToken = default);
}

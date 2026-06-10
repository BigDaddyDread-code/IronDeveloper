namespace IronDev.Core.AgentMemory.Collective;

public sealed record CollectiveMemoryPromotionRequest
{
    public required string PromotionRequestId { get; init; }

    public required CollectiveMemoryItem Candidate { get; init; }

    public required CollectiveMemoryAggregationResult AggregationResult { get; init; }

    public required CollectiveMemoryPromotionDecision Decision { get; init; }

    public required string DecisionId { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public string? DecidedByUserId { get; init; }

    public string? DecidedByAgentId { get; init; }

    public string? Reason { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }
}

public enum CollectiveMemoryPromotionDecision
{
    Accept = 1,
    Reject = 2,
    Defer = 3,
    Deprecate = 4,
    Supersede = 5
}

public sealed record CollectiveMemoryPromotionResult
{
    public required string PromotionRequestId { get; init; }

    public required CollectiveMemoryPromotionDecision Decision { get; init; }

    public required bool CreatedCollectiveMemory { get; init; }

    public string? CollectiveMemoryId { get; init; }

    public required CollectiveMemoryPromotionOutcome Outcome { get; init; }

    public required IReadOnlyList<CollectiveMemoryPromotionIssue> Issues { get; init; }
}

public enum CollectiveMemoryPromotionOutcome
{
    AcceptedCreated = 1,
    RejectedRecorded = 2,
    Deferred = 3,
    Deprecated = 4,
    Superseded = 5,
    Blocked = 6
}

public sealed record CollectiveMemoryPromotionIssue
{
    public required string Code { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }
}

public enum CollectiveMemoryEventType
{
    Created = 1,
    Accepted = 2,
    Rejected = 3,
    Deferred = 4,
    Deprecated = 5,
    Superseded = 6,
    Invalidated = 7,
    Reviewed = 8
}

public sealed record CollectiveMemoryEventRecord
{
    public required string CollectiveMemoryEventId { get; init; }

    public required string CollectiveMemoryId { get; init; }

    public required CollectiveMemoryEventType EventType { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? CreatedByUserId { get; init; }

    public string? CreatedByAgentId { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public string? EventJson { get; init; }
}

public interface ICollectiveMemoryPromotionService
{
    Task<CollectiveMemoryPromotionResult> PromoteAsync(
        CollectiveMemoryPromotionRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICollectiveMemoryStore
{
    Task<CollectiveMemoryItem?> GetAsync(
        CollectiveMemoryScope scope,
        string collectiveMemoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectiveMemoryItem>> QueryAsync(
        CollectiveMemoryScope scope,
        CollectiveMemoryQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectiveMemoryEventRecord>> GetEventsAsync(
        CollectiveMemoryScope scope,
        string collectiveMemoryId,
        CancellationToken cancellationToken = default);
}

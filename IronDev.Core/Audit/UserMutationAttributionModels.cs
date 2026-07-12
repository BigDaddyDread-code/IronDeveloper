namespace IronDev.Core.Audit;

public sealed record UserMutationAttributionRecord
{
    public required int ActorUserId { get; init; }
    public int? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string SourceSurface { get; init; }
    public required string SourceClient { get; init; }
    public required string Method { get; init; }
    public required string Route { get; init; }
    public required string Phase { get; init; }
    public int? StatusCode { get; init; }
}

public interface IUserMutationAttributionStore
{
    Task AppendAsync(UserMutationAttributionRecord record, CancellationToken cancellationToken = default);
}

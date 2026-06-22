namespace IronDev.Core.Governance;

public interface IOperationTimelineReadRepository
{
    OperationTimelineReadResult GetByOperationId(
        string operationId,
        FrontendReadinessReadScope scope);
}

public sealed record OperationTimelineEventReadRecord
{
    public required string OperationId { get; init; }
    public required string EntryId { get; init; }
    public required string EventKind { get; init; }
    public required string Summary { get; init; }
    public bool IsTenantScoped { get; init; } = true;
    public int? TenantId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
    public bool ContainsRawPayload { get; init; }
    public bool ContainsPrivateMaterial { get; init; }
    public bool ContainsPatchPayload { get; init; }
    public bool ContainsHiddenMaterial { get; init; }
    public bool ClaimsAuthority { get; init; }
    public bool ClaimsContinuation { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsPolicySatisfaction { get; init; }
    public bool ClaimsExecution { get; init; }
}

public sealed record OperationTimelineReadResult
{
    public required bool Found { get; init; }
    public FrontendOperationTimelineReadModel? Timeline { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static OperationTimelineReadResult NotFound(params string[] issues) =>
        new()
        {
            Found = false,
            Timeline = null,
            Issues = issues,
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
}

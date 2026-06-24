namespace IronDev.Core.Governance;

public enum OperationIdentityLookupStatus
{
    Unknown = 0,
    FoundOne = 1,
    NotFound = 2,
    FoundMultiple = 3,
    InvalidRequest = 4
}

public sealed record OperationIdentityLookupRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
}

public sealed record OperationIdentityLookupMatch
{
    public required string OperationId { get; init; }
    public required OperationIdentityLifecycleState LifecycleState { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required OperationReferenceKind MatchedReferenceKind { get; init; }
    public required string MatchedReferenceId { get; init; }
    public required DateTimeOffset MatchedReferenceObservedAtUtc { get; init; }
    public required string MatchedReferenceSource { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record OperationIdentityLookupResult
{
    public required OperationIdentityLookupStatus LookupStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required IReadOnlyList<OperationIdentityLookupMatch> Matches { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record OperationIdentityLookupValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

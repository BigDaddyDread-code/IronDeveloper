namespace IronDev.Core.Governance;

public enum OperationCorrelationSurfaceKind
{
    Unknown = 0,
    OperationStatus = 1,
    EvidenceMetadata = 2,
    ReceiptMetadata = 3,
    TimelineEvent = 4,
    GovernanceEvent = 5,
    ValidationResult = 6,
    PatchPackageMetadata = 7,
    SourceApplyReceipt = 8,
    CommitPackageReceipt = 9,
    PushReceipt = 10,
    PullRequestReceipt = 11
}

public sealed record OperationCorrelationLink
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationCorrelationGroup
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required IReadOnlyList<OperationCorrelationLink> Links { get; init; }
}

public sealed record OperationCorrelationValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

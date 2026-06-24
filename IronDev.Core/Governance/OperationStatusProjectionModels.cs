namespace IronDev.Core.Governance;

public enum OperationStatusProjectionEventKind
{
    Unknown = 0,
    OperationMinted = 1,
    RunStarted = 2,
    RunLinked = 3,
    PatchArtifactCreated = 4,
    PatchArtifactLinked = 5,
    SourceApplyStarted = 6,
    SourceApplyObserved = 7,
    CommitPackageCreated = 8,
    CommitObserved = 9,
    PushObserved = 10,
    PullRequestObserved = 11,
    EvidenceObserved = 12,
    ReceiptObserved = 13,
    ValidationObserved = 14,
    AuthorityBoundaryObserved = 15,
    BlockedObserved = 16,
    InterruptedObserved = 17,
    RecoveryObserved = 18,
    RollbackObserved = 19,
    FailedObserved = 20,
    CompletedObserved = 21
}

public enum OperationProjectedStatusKind
{
    Unknown = 0,
    NoEvents = 1,
    Minted = 2,
    RunObserved = 3,
    PatchArtifactObserved = 4,
    SourceApplyObserved = 5,
    CommitPackageObserved = 6,
    CommitObserved = 7,
    PushObserved = 8,
    PullRequestObserved = 9,
    BlockedObserved = 10,
    InterruptedObserved = 11,
    RecoveryObserved = 12,
    RollbackObserved = 13,
    FailedObserved = 14,
    CompletedObserved = 15
}

public sealed record OperationStatusProjectionEvent
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ProjectionEventId { get; init; }
    public required long AppendPosition { get; init; }
    public required OperationStatusProjectionEventKind EventKind { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string Source { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record OperationStatusProjectionRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string ProjectionVersion { get; init; }
    public required IReadOnlyList<OperationStatusProjectionEvent> Events { get; init; }
}

public sealed record OperationProjectedStatus
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string ProjectionVersion { get; init; }
    public required OperationProjectedStatusKind ProjectedStatusKind { get; init; }
    public string? LastStatusChangingEventId { get; init; }
    public OperationStatusProjectionEventKind? LastStatusChangingEventKind { get; init; }
    public DateTimeOffset? LastStatusChangedAtUtc { get; init; }
    public DateTimeOffset? LastRecordedAtUtc { get; init; }
    public required IReadOnlyList<string> SourceEventIds { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record OperationStatusProjectionResult
{
    public required bool IsValid { get; init; }
    public OperationProjectedStatus? ProjectedStatus { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

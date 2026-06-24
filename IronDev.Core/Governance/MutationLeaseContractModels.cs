namespace IronDev.Core.Governance;

public enum MutationLeaseContractValidationStatus
{
    Unknown = 0,
    Valid = 1,
    InvalidRequest = 2,
    RejectedUnsafePayload = 3,
    UnsupportedMutationKind = 4,
    UnsupportedLeaseMode = 5,
    Unassessable = 6
}

public enum MutationLeaseMode
{
    Unknown = 0,
    ExclusiveMutation = 1,
    ObserveOnly = 2
}

public enum MutationLeaseState
{
    Unknown = 0,
    Requested = 1,
    ObservedHeld = 2,
    ObservedDenied = 3,
    ObservedReleased = 4,
    ObservedExpired = 5,
    ObservedConflicted = 6
}

public enum MutationLeaseSurfaceKind
{
    Unknown = 0,
    SourceApply = 1,
    Commit = 2,
    Push = 3,
    DraftPullRequest = 4,
    ReadyForReview = 5,
    ReviewerRequest = 6,
    Merge = 7,
    Release = 8,
    Deploy = 9,
    Rollback = 10,
    Recovery = 11,
    MemoryPromotion = 12,
    WorkflowContinuation = 13
}

public sealed record MutationLeaseScope
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurfaceKind { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string IdempotencyKeyFingerprint { get; init; }
}

public sealed record MutationLeaseContractRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurfaceKind { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string IdempotencyKeyFingerprint { get; init; }
    public required MutationLeaseMode LeaseMode { get; init; }
    public required string LeaseOwnerRef { get; init; }
    public required int RequestedLeaseDurationSeconds { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record MutationLeaseContractRecord
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurfaceKind { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string IdempotencyKeyFingerprint { get; init; }
    public required MutationLeaseMode LeaseMode { get; init; }
    public required MutationLeaseState LeaseState { get; init; }
    public required string LeaseOwnerRef { get; init; }
    public required string LeaseTokenRef { get; init; }
    public required string FenceTokenRef { get; init; }
    public required string LeaseSequenceRef { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? ReleasedAtUtc { get; init; }
    public required string DeniedReasonCode { get; init; }
    public required string ConflictReasonCode { get; init; }
    public required string Source { get; init; }
    public required bool IsRedacted { get; init; }
    public required string RedactionReason { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record MutationLeaseContractValidationResult
{
    public required bool IsValid { get; init; }
    public required MutationLeaseContractValidationStatus ValidationStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurfaceKind { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string IdempotencyKeyFingerprint { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

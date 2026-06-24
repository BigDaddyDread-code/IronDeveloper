namespace IronDev.Core.Governance;

public enum ConcurrentMutationObservationState
{
    Unknown = 0,
    Requested = 1,
    InProgress = 2,
    ObservedHeld = 3,
    ObservedDenied = 4,
    ObservedReleased = 5,
    ObservedExpired = 6,
    ObservedConflicted = 7,
    Succeeded = 8,
    Failed = 9,
    Cancelled = 10,
    Interrupted = 11
}

public enum ConcurrentMutationGuardDecisionKind
{
    Invalid = 0,
    AllowedToProceedToNextGate = 1,
    BlockedByActiveMutation = 2,
    BlockedByConflictingLease = 3,
    BlockedByConflictingIdempotency = 4,
    BlockedByStaleObservation = 5,
    BlockedByUnknownState = 6
}

public enum ConcurrentMutationConflictKind
{
    None = 0,
    InvalidRequest = 1,
    ActiveMutation = 2,
    ConflictingLease = 3,
    ConflictingIdempotency = 4,
    StaleObservation = 5,
    UnknownState = 6,
    TooManyObservations = 7,
    UnsafePayload = 8
}

public sealed record ConcurrentMutationGuardRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string RequestedAttemptRef { get; init; }
    public required string IdempotencyKeyRef { get; init; }
    public required string IdempotencyFingerprint { get; init; }
    public string? ObservedLeaseRef { get; init; }
    public MutationLeaseState? ObservedLeaseState { get; init; }
    public string? ObservedLeaseOwnerRef { get; init; }
    public string? ObservedFenceRef { get; init; }
    public string? ObservedSequenceRef { get; init; }
    public DateTimeOffset? ObservedExpiresAtUtc { get; init; }
    public required DateTimeOffset NowUtc { get; init; }
}

public sealed record ConcurrentMutationObservation
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string AttemptRef { get; init; }
    public required string IdempotencyKeyRef { get; init; }
    public required string IdempotencyFingerprint { get; init; }
    public required ConcurrentMutationObservationState ObservedState { get; init; }
    public string? ObservedLeaseRef { get; init; }
    public string? ObservedLeaseOwnerRef { get; init; }
    public string? ObservedFenceRef { get; init; }
    public string? ObservedSequenceRef { get; init; }
    public required DateTimeOffset ObservedStartedAtUtc { get; init; }
    public DateTimeOffset? ObservedUpdatedAtUtc { get; init; }
    public DateTimeOffset? ObservedExpiresAtUtc { get; init; }
    public string? TerminalOutcomeRef { get; init; }
}

public sealed record ConcurrentMutationGuardReadResult
{
    public required IReadOnlyList<ConcurrentMutationObservation> Observations { get; init; }
    public required bool WasTruncated { get; init; }
    public required string TruncationReason { get; init; }

    public static ConcurrentMutationGuardReadResult FromObservations(
        IReadOnlyList<ConcurrentMutationObservation> observations) =>
        new()
        {
            Observations = observations,
            WasTruncated = false,
            TruncationReason = string.Empty
        };
}

public sealed record ConcurrentMutationGuardValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required bool HasUnsafePayload { get; init; }
}

public sealed record ConcurrentMutationGuardDecision
{
    public required ConcurrentMutationGuardDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required ConcurrentMutationConflictKind ConflictKind { get; init; }
    public required string ConflictRef { get; init; }
    public required string MatchedAttemptRef { get; init; }
    public required string MatchedIdempotencyKeyRef { get; init; }
    public required string MatchedLeaseRef { get; init; }
    public required string MatchedFenceRef { get; init; }
    public required string MatchedSequenceRef { get; init; }
    public required bool SafeToReuseExistingAttempt { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string RecordFingerprint { get; init; }
}

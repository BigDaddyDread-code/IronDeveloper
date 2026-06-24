namespace IronDev.Core.Governance;

public enum SafeRetryAttemptOutcome
{
    Unknown = 0,
    Requested = 1,
    InProgress = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Interrupted = 6
}

public enum SafeRetryFailureClass
{
    Unknown = 0,
    PreMutationInfrastructureFailure = 1,
    PreMutationDependencyUnavailable = 2,
    PreMutationTimeout = 3,
    PreMutationLeaseUnavailable = 4,
    PreMutationConcurrentGuardBlocked = 5,
    ValidationFailed = 6,
    PolicyDenied = 7,
    ApprovalMissing = 8,
    AuthorityBoundaryViolation = 9,
    UnsafePayloadRejected = 10,
    SecretOrCredentialRejected = 11,
    IdempotencyConflict = 12,
    MutationBoundaryUnknown = 13,
    MutationMayHaveStarted = 14,
    PartialMutationObserved = 15,
    PostStateUnknown = 16,
    ProviderRejectedAfterMutationStarted = 17,
    SourceStateUnknown = 18,
    ManualCancellation = 19
}

public enum SafeRetryMutationBoundaryState
{
    Unknown = 0,
    NotStarted = 1,
    Started = 2,
    PartiallyObserved = 3,
    Completed = 4
}

public enum SafeRetryCurrentGuardState
{
    Unknown = 0,
    AllowedToProceedToNextGate = 1,
    BlockedByActiveMutation = 2,
    BlockedByConflictingLease = 3,
    BlockedByConflictingIdempotency = 4,
    BlockedByStaleObservation = 5,
    BlockedByUnknownState = 6
}

public enum SafeRetryAssessmentDecisionKind
{
    Invalid = 0,
    RetryRequestMayProceedToAuthorityGate = 1,
    BlockedByNonTerminalAttempt = 2,
    BlockedBySucceededAttempt = 3,
    BlockedByCancelledAttempt = 4,
    BlockedByInterruptedAttempt = 5,
    BlockedByUnknownFailureClass = 6,
    BlockedByUnsafeFailureClass = 7,
    BlockedByMutationBoundaryUnknown = 8,
    BlockedByMutationMayHaveStarted = 9,
    BlockedByMissingReceiptEvidence = 10,
    BlockedByUnknownPostState = 11,
    BlockedByConcurrentMutationGuard = 12,
    BlockedByConflictingIdempotency = 13,
    BlockedByRetryBudget = 14,
    BlockedByUnsafePayload = 15
}

public enum SafeRetryAssessmentBlockKind
{
    None = 0,
    InvalidRequest = 1,
    UnsafePayload = 2,
    NonTerminalAttempt = 3,
    SucceededAttempt = 4,
    CancelledAttempt = 5,
    InterruptedAttempt = 6,
    UnknownFailureClass = 7,
    UnsafeFailureClass = 8,
    MutationBoundaryUnknown = 9,
    MutationMayHaveStarted = 10,
    MissingReceiptEvidence = 11,
    UnknownPostState = 12,
    ConcurrentMutationGuard = 13,
    ConflictingIdempotency = 14,
    RetryBudget = 15
}

public sealed record SafeRetryAssessmentRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string FailedAttemptRef { get; init; }
    public required SafeRetryAttemptOutcome FailedAttemptOutcome { get; init; }
    public required SafeRetryFailureClass FailureClass { get; init; }
    public required SafeRetryMutationBoundaryState MutationBoundaryState { get; init; }
    public required string FailureReceiptRef { get; init; }
    public required string TerminalOutcomeRef { get; init; }
    public required string PostStateObservationRef { get; init; }
    public required string PreviousIdempotencyKeyRef { get; init; }
    public required string PreviousIdempotencyFingerprint { get; init; }
    public required string ProposedRetryAttemptRef { get; init; }
    public required string ProposedRetryIdempotencyKeyRef { get; init; }
    public required string ProposedRetryIdempotencyFingerprint { get; init; }
    public required string RetryLineageRef { get; init; }
    public required int PriorRetryCount { get; init; }
    public required int MaxRetryCount { get; init; }
    public required SafeRetryCurrentGuardState CurrentGuardDecision { get; init; }
    public required string CurrentGuardDecisionRef { get; init; }
    public required DateTimeOffset AssessedAtUtc { get; init; }
    public required DateTimeOffset NowUtc { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record SafeRetryPriorAttemptMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string MutationTargetRef { get; init; }
    public required string AttemptRef { get; init; }
    public required string RetryLineageRef { get; init; }
    public required SafeRetryAttemptOutcome Outcome { get; init; }
    public required SafeRetryFailureClass FailureClass { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record SafeRetryLineageReadResult
{
    public required string RetryLineageRef { get; init; }
    public required IReadOnlyList<SafeRetryPriorAttemptMetadata> PriorAttempts { get; init; }
    public required bool WasTruncated { get; init; }
    public required string TruncationReason { get; init; }

    public static SafeRetryLineageReadResult Empty(string retryLineageRef) =>
        new()
        {
            RetryLineageRef = retryLineageRef,
            PriorAttempts = [],
            WasTruncated = false,
            TruncationReason = string.Empty
        };
}

public sealed record SafeRetryContractValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingReceiptEvidence { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required bool HasUnsafePayload { get; init; }
}

public sealed record SafeRetryAssessmentDecision
{
    public required SafeRetryAssessmentDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required SafeRetryAssessmentBlockKind BlockKind { get; init; }
    public required string FailedAttemptRef { get; init; }
    public required string RetryLineageRef { get; init; }
    public required string MatchedFailureReceiptRef { get; init; }
    public required string MatchedTerminalOutcomeRef { get; init; }
    public required string MatchedPostStateObservationRef { get; init; }
    public required string MatchedGuardDecisionRef { get; init; }
    public required bool SafeRetryCandidateForNextGate { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresFreshConcurrentGuard { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

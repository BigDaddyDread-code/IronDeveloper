namespace IronDev.Core.Governance;

public enum PostStateObservationSubjectKind
{
    Unknown = 0,
    SourceApplyTarget = 1,
    WorktreeState = 2,
    PatchPackageState = 3,
    CommitHead = 4,
    RemoteHead = 5,
    DraftPullRequest = 6,
    PullRequestProviderState = 7,
    RollbackTarget = 8,
    RecoveryTarget = 9,
    OperationState = 10,
    ReceiptState = 11,
    ReadModelState = 12,
    ExternalProviderState = 13,
    MemoryPromotionTarget = 14,
    WorkflowContinuationTarget = 15
}

public enum PostStateObservationMethod
{
    Unknown = 0,
    ReferenceOnly = 1,
    ProviderReadback = 2,
    LocalMetadataReadback = 3,
    ReceiptReadback = 4,
    StatusReadback = 5,
    ReadModelReadback = 6,
    ManualOperatorObservation = 7,
    SyntheticTestObservation = 8
}

public enum PostStateTransitionExpectation
{
    Unknown = 0,
    NoChangeExpected = 1,
    MutationExpected = 2,
    RollbackExpected = 3,
    RecoveryExpected = 4,
    ObservationOnly = 5
}

public enum PostStateObservedTransition
{
    Unknown = 0,
    NoChangeObserved = 1,
    ExpectedChangeObserved = 2,
    UnexpectedChangeObserved = 3,
    PartialChangeObserved = 4,
    DivergentChangeObserved = 5,
    ProviderAcceptedOutcomeUnknown = 6,
    ProviderRejectedBeforeMutation = 7,
    ProviderRejectedAfterMutationStarted = 8,
    ObservationUnavailable = 9,
    ObservationFailed = 10
}

public enum PostStateObservationCompleteness
{
    Unknown = 0,
    Complete = 1,
    Partial = 2,
    Stale = 3,
    Truncated = 4,
    Unavailable = 5
}

public enum PostStateObservationTrustLevel
{
    Unknown = 0,
    SelfReported = 1,
    ProviderMetadata = 2,
    LocalMetadata = 3,
    ReceiptBacked = 4,
    ReadModelBacked = 5,
    OperatorObserved = 6,
    TestFixture = 7
}

public enum PostStateBoundarySignal
{
    Unknown = 0,
    SupportsRetryAssessmentOnly = 1,
    RequiresFreshObservation = 2,
    RequiresPostStateTriage = 3,
    RequiresRecoveryAssessment = 4,
    RequiresRollbackAssessment = 5,
    RequiresManualTriage = 6,
    RequiresReadModelRebuildAssessment = 7,
    RequiresReceiptConflictAssessment = 8
}

public enum PostStateObservationDecisionKind
{
    Invalid = 0,
    AcceptedAsPostStateEvidence = 1,
    BlockedByUnknownSubject = 2,
    BlockedByUnknownMethod = 3,
    BlockedByUnknownTransition = 4,
    BlockedByUnknownCompleteness = 5,
    BlockedByUnknownTrustLevel = 6,
    BlockedByMissingEvidence = 7,
    BlockedByUnsafePayload = 8,
    BlockedByStaleObservation = 9,
    BlockedByExpiredObservation = 10,
    BlockedByInconsistentState = 11,
    BlockedByUntrustedObservation = 12
}

public enum PostStateObservationBlockKind
{
    None = 0,
    InvalidRequest = 1,
    UnknownSubject = 2,
    UnknownMethod = 3,
    UnknownTransition = 4,
    UnknownCompleteness = 5,
    UnknownTrustLevel = 6,
    MissingEvidence = 7,
    UnsafePayload = 8,
    StaleObservation = 9,
    ExpiredObservation = 10,
    InconsistentState = 11,
    UntrustedObservation = 12
}

public sealed record PostStateObservationRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string ObservationRef { get; init; }
    public required PostStateObservationSubjectKind SubjectKind { get; init; }
    public required PostStateObservationMethod ObservationMethod { get; init; }
    public required PostStateTransitionExpectation TransitionExpectation { get; init; }
    public required PostStateObservedTransition ObservedTransition { get; init; }
    public required PostStateObservationCompleteness ObservationCompleteness { get; init; }
    public required PostStateObservationTrustLevel ObservationTrustLevel { get; init; }
    public string? PreStateRef { get; init; }
    public string? PreStateFingerprint { get; init; }
    public string? ExpectedPostStateRef { get; init; }
    public string? ExpectedPostStateFingerprint { get; init; }
    public string? ObservedPostStateRef { get; init; }
    public string? ObservedPostStateFingerprint { get; init; }
    public string? FailureClassificationRef { get; init; }
    public string? FailureClassRef { get; init; }
    public string? FailureReceiptRef { get; init; }
    public string? MutationReceiptRef { get; init; }
    public string? ProviderStateRef { get; init; }
    public string? ReadModelStateRef { get; init; }
    public string? ConcurrentGuardDecisionRef { get; init; }
    public string? LeaseObservationRef { get; init; }
    public string? IdempotencyKeyRef { get; init; }
    public string? IdempotencyFingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? ObservationExpiresAtUtc { get; init; }
    public required string ObserverVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record PostStateObservationValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record PostStateObservationDecision
{
    public required PostStateObservationDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required PostStateObservationBlockKind BlockKind { get; init; }
    public required PostStateBoundarySignal BoundarySignal { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string ObservationRef { get; init; }
    public required PostStateObservationSubjectKind SubjectKind { get; init; }
    public required PostStateObservationMethod ObservationMethod { get; init; }
    public required PostStateTransitionExpectation TransitionExpectation { get; init; }
    public required PostStateObservedTransition ObservedTransition { get; init; }
    public required PostStateObservationCompleteness ObservationCompleteness { get; init; }
    public required PostStateObservationTrustLevel ObservationTrustLevel { get; init; }
    public required string MatchedPreStateRef { get; init; }
    public required string MatchedExpectedPostStateRef { get; init; }
    public required string MatchedObservedPostStateRef { get; init; }
    public required string MatchedFailureClassificationRef { get; init; }
    public required string MatchedFailureReceiptRef { get; init; }
    public required string MatchedMutationReceiptRef { get; init; }
    public required string MatchedProviderStateRef { get; init; }
    public required string MatchedReadModelStateRef { get; init; }
    public required string MatchedConcurrentGuardDecisionRef { get; init; }
    public required string MatchedLeaseObservationRef { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresFreshConcurrentGuard { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

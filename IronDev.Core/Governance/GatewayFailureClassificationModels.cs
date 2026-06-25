namespace IronDev.Core.Governance;

public enum GatewayFailurePhase
{
    Unknown = 0,
    RequestValidation = 1,
    AuthorityEvaluation = 2,
    PolicyEvaluation = 3,
    ApprovalEvaluation = 4,
    FreshnessEvaluation = 5,
    ConcurrentMutationGuard = 6,
    LeaseObservation = 7,
    IdempotencyEvaluation = 8,
    PreMutationValidation = 9,
    PreMutationDependencyCheck = 10,
    MutationDispatch = 11,
    ProviderAcceptedBoundaryUnknown = 12,
    ProviderRejectedBeforeMutation = 13,
    ProviderRejectedAfterMutationStarted = 14,
    PostStateObservation = 15,
    ReceiptPersistence = 16,
    StatusProjection = 17,
    RollbackPlanning = 18,
    RecoveryPlanning = 19,
    WorkflowContinuationPlanning = 20,
    ManualCancellation = 21
}

public enum GatewayFailureClass
{
    Unknown = 0,
    InvalidRequest = 1,
    UnsupportedMutationSurface = 2,
    UnsupportedGatewayOperation = 3,
    MalformedReference = 4,
    UnsafePayloadRejected = 5,
    SecretOrCredentialRejected = 6,
    AuthorityBoundaryViolation = 7,
    ApprovalMissing = 8,
    ApprovalDenied = 9,
    PolicyDenied = 10,
    ValidationFailed = 11,
    FreshnessExpired = 12,
    StaleValidation = 13,
    StalePatch = 14,
    PatchBaseMoved = 15,
    SourceStateUnknown = 16,
    DirtyWorktree = 17,
    BranchHeadMismatch = 18,
    RemoteHeadMismatch = 19,
    PreMutationInfrastructureFailure = 20,
    PreMutationDependencyUnavailable = 21,
    PreMutationTimeout = 22,
    PreMutationLeaseUnavailable = 23,
    PreMutationConcurrentGuardBlocked = 24,
    IdempotencyConflict = 25,
    MutationBoundaryUnknown = 26,
    MutationMayHaveStarted = 27,
    PartialMutationObserved = 28,
    ProviderAcceptedButOutcomeUnknown = 29,
    ProviderRejectedBeforeMutation = 30,
    ProviderRejectedAfterMutationStarted = 31,
    PostStateUnknown = 32,
    ReceiptPersistenceFailed = 33,
    ReceiptConflict = 34,
    ReceiptUnsafePayloadRejected = 35,
    StatusProjectionFailed = 36,
    ReadModelStale = 37,
    ReadModelUnavailable = 38,
    RollbackPlanUnavailable = 39,
    RecoveryPlanUnavailable = 40,
    InterruptedRun = 41,
    ManualCancellation = 42,
    OperatorAbort = 43,
    RateLimited = 44,
    ExternalProviderUnavailable = 45,
    ExternalProviderTimeout = 46
}

public enum GatewayFailureMutationBoundaryState
{
    Unknown = 0,
    MutationNotStarted = 1,
    MutationMayHaveStarted = 2,
    MutationStarted = 3,
    MutationPartiallyObserved = 4,
    MutationCompleted = 5,
    ObservationOnly = 6
}

public enum GatewayFailureRoutingHint
{
    Unknown = 0,
    BlockedUntilClassified = 1,
    MayProceedToRetryAssessment = 2,
    RequiresRecoveryAssessment = 3,
    RequiresRollbackAssessment = 4,
    RequiresPostStateObservation = 5,
    RequiresFreshValidation = 6,
    RequiresFreshAuthority = 7,
    RequiresManualTriage = 8,
    RequiresReceiptConflictResolution = 9,
    RequiresReadModelRebuild = 10
}

public enum GatewayFailureClassificationDecisionKind
{
    Invalid = 0,
    Classified = 1,
    BlockedByUnknownFailureClass = 2,
    BlockedByUnknownFailurePhase = 3,
    BlockedByUnknownMutationBoundary = 4,
    BlockedByUnsafePayload = 5,
    BlockedByMissingEvidence = 6,
    BlockedByInconsistentBoundary = 7,
    BlockedByMissingPostStateObservation = 8,
    BlockedByMissingConcurrentGuardEvidence = 9,
    BlockedByMissingLeaseEvidence = 10,
    BlockedByMissingIdempotencyEvidence = 11
}

public enum GatewayFailureClassificationBlockKind
{
    None = 0,
    InvalidRequest = 1,
    UnknownFailureClass = 2,
    UnknownFailurePhase = 3,
    UnknownMutationBoundary = 4,
    UnsafePayload = 5,
    MissingEvidence = 6,
    InconsistentBoundary = 7,
    MissingPostStateObservation = 8,
    MissingConcurrentGuardEvidence = 9,
    MissingLeaseEvidence = 10,
    MissingIdempotencyEvidence = 11
}

public sealed record GatewayFailureClassificationRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string GatewayRef { get; init; }
    public required string FailureRef { get; init; }
    public required GatewayFailurePhase FailurePhase { get; init; }
    public required GatewayFailureClass FailureClass { get; init; }
    public required GatewayFailureMutationBoundaryState MutationBoundaryState { get; init; }
    public string? FailureEvidenceRef { get; init; }
    public string? FailureReceiptRef { get; init; }
    public string? PostStateObservationRef { get; init; }
    public string? ConcurrentGuardDecisionRef { get; init; }
    public string? LeaseObservationRef { get; init; }
    public string? IdempotencyKeyRef { get; init; }
    public string? IdempotencyFingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset ClassifiedAtUtc { get; init; }
    public required string ClassifierVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record GatewayFailureClassificationValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record GatewayFailureClassificationDecision
{
    public required GatewayFailureClassificationDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required GatewayFailureClassificationBlockKind BlockKind { get; init; }
    public required GatewayFailurePhase FailurePhase { get; init; }
    public required GatewayFailureClass FailureClass { get; init; }
    public required GatewayFailureMutationBoundaryState MutationBoundaryState { get; init; }
    public required GatewayFailureRoutingHint RoutingHint { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string GatewayRef { get; init; }
    public required string FailureRef { get; init; }
    public required string MatchedFailureEvidenceRef { get; init; }
    public required string MatchedFailureReceiptRef { get; init; }
    public required string MatchedPostStateObservationRef { get; init; }
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

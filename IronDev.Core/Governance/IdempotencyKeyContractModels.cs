namespace IronDev.Core.Governance;

public enum IdempotencySubjectKind
{
    Unknown = 0,
    SourceApply = 1,
    Commit = 2,
    Push = 3,
    DraftPullRequest = 4,
    PullRequestBranchUpdate = 5,
    ReadyForReview = 6,
    Rollback = 7,
    Retry = 8,
    Recovery = 9,
    WorkflowContinuation = 10,
    MergeReadiness = 11,
    ReleaseReadiness = 12,
    DeploymentReadiness = 13
}

public enum IdempotencyEvidenceKind
{
    Unknown = 0,
    ClientProvidedKey = 1,
    ExecutorRequestKey = 2,
    ReceiptBackedKey = 3,
    ProviderRequestKey = 4,
    OperationStatusKey = 5,
    RetryLineageKey = 6,
    RecoveryLineageKey = 7,
    SyntheticTestKey = 8
}

public enum IdempotencyEvidenceTrustLevel
{
    Unknown = 0,
    SelfReported = 1,
    RequestFingerprintBacked = 2,
    ReceiptBacked = 3,
    OperationStatusBacked = 4,
    LineageBacked = 5,
    ProviderMetadataBacked = 6,
    OperatorObserved = 7,
    TestFixture = 8
}

public enum IdempotencyObservationFreshness
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3,
    NotTimestamped = 4
}

public enum IdempotencyPriorState
{
    Unknown = 0,
    NoPriorObservation = 1,
    PriorInProgressSameRequest = 2,
    PriorCompletedSameRequest = 3,
    PriorFailedSameRequest = 4,
    PriorCancelledSameRequest = 5,
    PriorConflictingRequest = 6,
    PriorConflictingTarget = 7,
    PriorConflictingAuthority = 8,
    PriorExpired = 9,
    PriorUnavailable = 10,
    Ambiguous = 11
}

public enum IdempotencyKeyDecisionKind
{
    Invalid = 0,
    MayProceedToNextAuthorityGate = 1,
    DuplicateRequestEvidenceOnly = 2,
    DuplicateCompletedNoExecution = 3,
    BlockedByMissingIdempotencyKey = 4,
    BlockedByMalformedIdempotencyKey = 5,
    BlockedByStaleIdempotencyObservation = 6,
    BlockedByExpiredIdempotencyObservation = 7,
    BlockedByDuplicateInProgress = 8,
    BlockedByPriorFailedAttempt = 9,
    BlockedByPriorCancelledAttempt = 10,
    BlockedByConflictingIdempotencyKey = 11,
    BlockedByConflictingRequestFingerprint = 12,
    BlockedByConflictingAuthorityFingerprint = 13,
    BlockedByConflictingTargetFingerprint = 14,
    BlockedByUntrustedIdempotencyEvidence = 15,
    BlockedByMissingIdempotencyEvidence = 16,
    BlockedByAmbiguousIdempotencyState = 17,
    BlockedByUnsafePayload = 18
}

public enum IdempotencyKeyBlockKind
{
    None = 0,
    InvalidRequest = 1,
    MissingKey = 2,
    MalformedKey = 3,
    StaleObservation = 4,
    ExpiredObservation = 5,
    DuplicateInProgress = 6,
    PriorFailedAttempt = 7,
    PriorCancelledAttempt = 8,
    ConflictingKey = 9,
    ConflictingRequest = 10,
    ConflictingAuthority = 11,
    ConflictingTarget = 12,
    UntrustedEvidence = 13,
    MissingEvidence = 14,
    AmbiguousState = 15,
    UnsafePayload = 16
}

public sealed record IdempotencyKeyContractRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required IdempotencySubjectKind SubjectKind { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string RequestRef { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string IdempotencyScopeRef { get; init; }
    public string? IdempotencyObservationRef { get; init; }
    public string? PriorAttemptRef { get; init; }
    public string? PriorReceiptRef { get; init; }
    public string? PriorOperationStatusRef { get; init; }
    public string? PriorLineageRef { get; init; }
    public required string RequestFingerprint { get; init; }
    public string? ExpectedRequestFingerprint { get; init; }
    public string? ObservedRequestFingerprint { get; init; }
    public string? AuthorityFingerprint { get; init; }
    public string? ExpectedAuthorityFingerprint { get; init; }
    public string? ObservedAuthorityFingerprint { get; init; }
    public string? TargetFingerprint { get; init; }
    public string? ExpectedTargetFingerprint { get; init; }
    public string? ObservedTargetFingerprint { get; init; }
    public string? EffectFingerprint { get; init; }
    public string? ExpectedEffectFingerprint { get; init; }
    public string? ObservedEffectFingerprint { get; init; }
    public required IdempotencyEvidenceKind EvidenceKind { get; init; }
    public required IdempotencyEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required IdempotencyObservationFreshness ObservationFreshness { get; init; }
    public required IdempotencyPriorState PriorState { get; init; }
    public string? AuthorityReceiptRef { get; init; }
    public string? PolicySatisfactionRef { get; init; }
    public string? ValidationReceiptRef { get; init; }
    public string? ConcurrentGuardDecisionRef { get; init; }
    public string? DirtyWorktreeGuardRef { get; init; }
    public string? MovedBaseGuardRef { get; init; }
    public string? StaleValidationGuardRef { get; init; }
    public string? BranchRemoteHeadVerificationRef { get; init; }
    public string? PostStateObservationRef { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? EvidenceExpiresAtUtc { get; init; }
    public required string ContractVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record IdempotencyKeyContractValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required bool HasMalformedKey { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record IdempotencyKeyContractDecision
{
    public required IdempotencyKeyDecisionKind Decision { get; init; }
    public required IdempotencyKeyBlockKind BlockKind { get; init; }
    public required string Reason { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required IdempotencySubjectKind SubjectKind { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string RequestRef { get; init; }
    public required string MatchedIdempotencyKey { get; init; }
    public required string MatchedIdempotencyScopeRef { get; init; }
    public required string MatchedIdempotencyObservationRef { get; init; }
    public required string MatchedPriorAttemptRef { get; init; }
    public required string MatchedPriorReceiptRef { get; init; }
    public required string MatchedPriorOperationStatusRef { get; init; }
    public required string MatchedPriorLineageRef { get; init; }
    public required string MatchedRequestFingerprint { get; init; }
    public required string MatchedExpectedRequestFingerprint { get; init; }
    public required string MatchedObservedRequestFingerprint { get; init; }
    public required string MatchedAuthorityFingerprint { get; init; }
    public required string MatchedExpectedAuthorityFingerprint { get; init; }
    public required string MatchedObservedAuthorityFingerprint { get; init; }
    public required string MatchedTargetFingerprint { get; init; }
    public required string MatchedExpectedTargetFingerprint { get; init; }
    public required string MatchedObservedTargetFingerprint { get; init; }
    public required string MatchedEffectFingerprint { get; init; }
    public required string MatchedExpectedEffectFingerprint { get; init; }
    public required string MatchedObservedEffectFingerprint { get; init; }
    public required IdempotencyEvidenceKind EvidenceKind { get; init; }
    public required IdempotencyEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required IdempotencyObservationFreshness ObservationFreshness { get; init; }
    public required IdempotencyPriorState PriorState { get; init; }
    public required string MatchedAuthorityReceiptRef { get; init; }
    public required string MatchedPolicySatisfactionRef { get; init; }
    public required string MatchedValidationReceiptRef { get; init; }
    public required string MatchedConcurrentGuardDecisionRef { get; init; }
    public required string MatchedDirtyWorktreeGuardRef { get; init; }
    public required string MatchedMovedBaseGuardRef { get; init; }
    public required string MatchedStaleValidationGuardRef { get; init; }
    public required string MatchedBranchRemoteHeadVerificationRef { get; init; }
    public required string MatchedPostStateObservationRef { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresAcceptedApproval { get; init; }
    public required bool RequiresPolicySatisfaction { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresConcurrentGuard { get; init; }
    public required bool RequiresDirtyWorktreeGuard { get; init; }
    public required bool RequiresMovedBaseGuard { get; init; }
    public required bool RequiresStaleValidationGuard { get; init; }
    public required bool RequiresBranchRemoteHeadVerification { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

namespace IronDev.Core.Governance;

public enum StaleValidationSubjectKind
{
    Unknown = 0,
    PatchPackage = 1,
    SourceApplyTarget = 2,
    CommitPackage = 3,
    PushTarget = 4,
    DraftPullRequestTarget = 5,
    RollbackTarget = 6,
    RecoveryTarget = 7,
    WorkflowContinuationTarget = 8,
    MergeTarget = 9,
    ReleaseCandidateTarget = 10,
    DeploymentTarget = 11
}

public enum ValidationEvidenceKind
{
    Unknown = 0,
    BuildResult = 1,
    FocusedTestResult = 2,
    CompatibilityTestResult = 3,
    CorridorTestResult = 4,
    CombinedCorridorResult = 5,
    GovernanceBoundaryScan = 6,
    SecretScan = 7,
    DiffCheck = 8,
    CachedDiffCheck = 9,
    CompositeValidationReceipt = 10,
    ProviderCiStatus = 11,
    OperatorReportedValidation = 12,
    SyntheticTestValidation = 13
}

public enum ValidationEvidenceTrustLevel
{
    Unknown = 0,
    SelfReported = 1,
    ReceiptBacked = 2,
    ProviderMetadataBacked = 3,
    BuildReceiptBacked = 4,
    TestReceiptBacked = 5,
    GovernanceReceiptBacked = 6,
    OperatorObserved = 7,
    TestFixture = 8
}

public enum ValidationObservationFreshness
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3,
    NotTimestamped = 4
}

public enum ValidationOutcomeState
{
    Unknown = 0,
    Passed = 1,
    Failed = 2,
    TimedOut = 3,
    Cancelled = 4,
    NotRun = 5,
    Partial = 6,
    Unavailable = 7
}

public enum ValidationScopeKind
{
    Unknown = 0,
    FocusedSlice = 1,
    CompatibilityPair = 2,
    Corridor = 3,
    CombinedCorridor = 4,
    Build = 5,
    GovernanceBoundary = 6,
    DiffCheck = 7,
    CachedDiffCheck = 8,
    Composite = 9
}

public enum StaleValidationGuardDecisionKind
{
    Invalid = 0,
    MayProceedToNextAuthorityGate = 1,
    BlockedByStaleValidation = 2,
    BlockedByExpiredValidation = 3,
    BlockedByFailedValidation = 4,
    BlockedByIncompleteValidation = 5,
    BlockedByUnknownValidationState = 6,
    BlockedByMissingValidationEvidence = 7,
    BlockedByUntrustedValidationEvidence = 8,
    BlockedByInconsistentValidationEvidence = 9,
    BlockedByUnsafePayload = 10
}

public enum StaleValidationGuardBlockKind
{
    None = 0,
    InvalidRequest = 1,
    StaleValidation = 2,
    ExpiredValidation = 3,
    FailedValidation = 4,
    IncompleteValidation = 5,
    UnknownValidationState = 6,
    MissingEvidence = 7,
    UntrustedEvidence = 8,
    InconsistentEvidence = 9,
    UnsafePayload = 10
}

public sealed record StaleValidationGuardRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required StaleValidationSubjectKind SubjectKind { get; init; }
    public required ValidationEvidenceKind ValidationEvidenceKind { get; init; }
    public required ValidationEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required ValidationObservationFreshness ObservationFreshness { get; init; }
    public required ValidationOutcomeState ValidationOutcome { get; init; }
    public required ValidationScopeKind ValidationScope { get; init; }
    public string? ValidationEvidenceRef { get; init; }
    public string? ValidationReceiptRef { get; init; }
    public string? BuildReceiptRef { get; init; }
    public string? TestReceiptRef { get; init; }
    public string? GovernanceReceiptRef { get; init; }
    public string? ProviderCiStateRef { get; init; }
    public string? OperatorObservationRef { get; init; }
    public string? PostStateObservationRef { get; init; }
    public string? DirtyWorktreeGuardRef { get; init; }
    public string? MovedBaseGuardRef { get; init; }
    public string? ConcurrentGuardDecisionRef { get; init; }
    public string? ExpectedValidationTargetRef { get; init; }
    public string? ObservedValidationTargetRef { get; init; }
    public string? ExpectedValidationFingerprint { get; init; }
    public string? ObservedValidationFingerprint { get; init; }
    public string? ExpectedSourceStateRef { get; init; }
    public string? ObservedSourceStateRef { get; init; }
    public string? ExpectedPatchPackageRef { get; init; }
    public string? ObservedPatchPackageRef { get; init; }
    public string? ExpectedCommitRef { get; init; }
    public string? ObservedCommitRef { get; init; }
    public string? ExpectedHeadRef { get; init; }
    public string? ObservedHeadRef { get; init; }
    public string? ExpectedBaseRef { get; init; }
    public string? ObservedBaseRef { get; init; }
    public required DateTimeOffset ValidatedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? EvidenceExpiresAtUtc { get; init; }
    public required string GuardVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record StaleValidationGuardValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record StaleValidationGuardDecision
{
    public required StaleValidationGuardDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required StaleValidationGuardBlockKind BlockKind { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required StaleValidationSubjectKind SubjectKind { get; init; }
    public required ValidationEvidenceKind ValidationEvidenceKind { get; init; }
    public required ValidationEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required ValidationObservationFreshness ObservationFreshness { get; init; }
    public required ValidationOutcomeState ValidationOutcome { get; init; }
    public required ValidationScopeKind ValidationScope { get; init; }
    public required string MatchedValidationEvidenceRef { get; init; }
    public required string MatchedValidationReceiptRef { get; init; }
    public required string MatchedBuildReceiptRef { get; init; }
    public required string MatchedTestReceiptRef { get; init; }
    public required string MatchedGovernanceReceiptRef { get; init; }
    public required string MatchedProviderCiStateRef { get; init; }
    public required string MatchedOperatorObservationRef { get; init; }
    public required string MatchedPostStateObservationRef { get; init; }
    public required string MatchedDirtyWorktreeGuardRef { get; init; }
    public required string MatchedMovedBaseGuardRef { get; init; }
    public required string MatchedConcurrentGuardDecisionRef { get; init; }
    public required string MatchedExpectedValidationTargetRef { get; init; }
    public required string MatchedObservedValidationTargetRef { get; init; }
    public required string MatchedExpectedValidationFingerprint { get; init; }
    public required string MatchedObservedValidationFingerprint { get; init; }
    public required string MatchedExpectedSourceStateRef { get; init; }
    public required string MatchedObservedSourceStateRef { get; init; }
    public required string MatchedExpectedPatchPackageRef { get; init; }
    public required string MatchedObservedPatchPackageRef { get; init; }
    public required string MatchedExpectedCommitRef { get; init; }
    public required string MatchedObservedCommitRef { get; init; }
    public required string MatchedExpectedHeadRef { get; init; }
    public required string MatchedObservedHeadRef { get; init; }
    public required string MatchedExpectedBaseRef { get; init; }
    public required string MatchedObservedBaseRef { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresFreshConcurrentGuard { get; init; }
    public required bool RequiresDirtyWorktreeGuard { get; init; }
    public required bool RequiresMovedBaseGuard { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

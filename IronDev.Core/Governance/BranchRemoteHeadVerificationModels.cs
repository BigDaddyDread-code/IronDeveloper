namespace IronDev.Core.Governance;

public enum BranchRemoteHeadSubjectKind
{
    Unknown = 0,
    SourceApplyTarget = 1,
    CommitPackageTarget = 2,
    PushTarget = 3,
    DraftPullRequestTarget = 4,
    RollbackTarget = 5,
    RecoveryTarget = 6,
    WorkflowContinuationTarget = 7,
    MergeTarget = 8,
    ReleaseCandidateTarget = 9,
    DeploymentTarget = 10
}

public enum BranchRemoteHeadEvidenceKind
{
    Unknown = 0,
    LocalBranchObservation = 1,
    LocalHeadObservation = 2,
    RemoteHeadObservation = 3,
    BranchRemoteCompositeObservation = 4,
    ProviderBranchState = 5,
    OperatorBranchObservation = 6,
    TestFixtureBranchObservation = 7
}

public enum BranchRemoteHeadEvidenceTrustLevel
{
    Unknown = 0,
    SelfReported = 1,
    ReceiptBacked = 2,
    ProviderMetadataBacked = 3,
    LocalObservationBacked = 4,
    OperatorObserved = 5,
    TestFixture = 6
}

public enum BranchRemoteHeadObservationFreshness
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3,
    NotTimestamped = 4
}

public enum BranchRemoteHeadVerificationOutcome
{
    Unknown = 0,
    Verified = 1,
    BranchMismatch = 2,
    RemoteMismatch = 3,
    HeadMismatch = 4,
    BaseMismatch = 5,
    DetachedHead = 6,
    AmbiguousBranch = 7,
    MissingBranch = 8,
    MissingRemote = 9,
    MissingHead = 10,
    RemoteUnavailable = 11,
    DeletedRemoteBranch = 12
}

public enum BranchRemoteHeadVerificationDecisionKind
{
    Invalid = 0,
    MayProceedToNextAuthorityGate = 1,
    BlockedByStaleObservation = 2,
    BlockedByExpiredObservation = 3,
    BlockedByBranchMismatch = 4,
    BlockedByRemoteMismatch = 5,
    BlockedByHeadMismatch = 6,
    BlockedByBaseMismatch = 7,
    BlockedByDetachedHead = 8,
    BlockedByAmbiguousBranch = 9,
    BlockedByMissingBranchRemoteHeadEvidence = 10,
    BlockedByDeletedRemoteBranch = 11,
    BlockedByRemoteUnavailable = 12,
    BlockedByUntrustedEvidence = 13,
    BlockedByInconsistentEvidence = 14,
    BlockedByUnsafePayload = 15
}

public enum BranchRemoteHeadVerificationBlockKind
{
    None = 0,
    InvalidRequest = 1,
    StaleObservation = 2,
    ExpiredObservation = 3,
    BranchMismatch = 4,
    RemoteMismatch = 5,
    HeadMismatch = 6,
    BaseMismatch = 7,
    DetachedHead = 8,
    AmbiguousBranch = 9,
    MissingEvidence = 10,
    DeletedRemoteBranch = 11,
    RemoteUnavailable = 12,
    UntrustedEvidence = 13,
    InconsistentEvidence = 14,
    UnsafePayload = 15
}

public sealed record BranchRemoteHeadVerificationRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required BranchRemoteHeadSubjectKind SubjectKind { get; init; }
    public required BranchRemoteHeadEvidenceKind EvidenceKind { get; init; }
    public required BranchRemoteHeadEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required BranchRemoteHeadObservationFreshness ObservationFreshness { get; init; }
    public required BranchRemoteHeadVerificationOutcome VerificationOutcome { get; init; }
    public string? BranchObservationRef { get; init; }
    public string? RemoteObservationRef { get; init; }
    public string? HeadObservationRef { get; init; }
    public string? CompositeObservationRef { get; init; }
    public string? ProviderBranchStateRef { get; init; }
    public string? OperatorObservationRef { get; init; }
    public string? ExpectedBranchRef { get; init; }
    public string? ObservedBranchRef { get; init; }
    public string? ExpectedRemoteRef { get; init; }
    public string? ObservedRemoteRef { get; init; }
    public string? ExpectedRemoteUrlFingerprint { get; init; }
    public string? ObservedRemoteUrlFingerprint { get; init; }
    public string? ExpectedLocalHeadRef { get; init; }
    public string? ObservedLocalHeadRef { get; init; }
    public string? ExpectedRemoteHeadRef { get; init; }
    public string? ObservedRemoteHeadRef { get; init; }
    public string? ExpectedBaseRef { get; init; }
    public string? ObservedBaseRef { get; init; }
    public string? ExpectedSourceStateRef { get; init; }
    public string? ObservedSourceStateRef { get; init; }
    public string? ExpectedPatchPackageRef { get; init; }
    public string? ObservedPatchPackageRef { get; init; }
    public string? ExpectedCommitRef { get; init; }
    public string? ObservedCommitRef { get; init; }
    public string? DirtyWorktreeGuardRef { get; init; }
    public string? MovedBaseGuardRef { get; init; }
    public string? StaleValidationGuardRef { get; init; }
    public string? ConcurrentGuardDecisionRef { get; init; }
    public string? PostStateObservationRef { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? EvidenceExpiresAtUtc { get; init; }
    public required string GuardVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record BranchRemoteHeadVerificationValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record BranchRemoteHeadVerificationDecision
{
    public required BranchRemoteHeadVerificationDecisionKind Decision { get; init; }
    public required BranchRemoteHeadVerificationBlockKind BlockKind { get; init; }
    public required string Reason { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required BranchRemoteHeadSubjectKind SubjectKind { get; init; }
    public required BranchRemoteHeadEvidenceKind EvidenceKind { get; init; }
    public required BranchRemoteHeadEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required BranchRemoteHeadObservationFreshness ObservationFreshness { get; init; }
    public required BranchRemoteHeadVerificationOutcome VerificationOutcome { get; init; }
    public required string MatchedBranchObservationRef { get; init; }
    public required string MatchedRemoteObservationRef { get; init; }
    public required string MatchedHeadObservationRef { get; init; }
    public required string MatchedCompositeObservationRef { get; init; }
    public required string MatchedProviderBranchStateRef { get; init; }
    public required string MatchedOperatorObservationRef { get; init; }
    public required string MatchedExpectedBranchRef { get; init; }
    public required string MatchedObservedBranchRef { get; init; }
    public required string MatchedExpectedRemoteRef { get; init; }
    public required string MatchedObservedRemoteRef { get; init; }
    public required string MatchedExpectedRemoteUrlFingerprint { get; init; }
    public required string MatchedObservedRemoteUrlFingerprint { get; init; }
    public required string MatchedExpectedLocalHeadRef { get; init; }
    public required string MatchedObservedLocalHeadRef { get; init; }
    public required string MatchedExpectedRemoteHeadRef { get; init; }
    public required string MatchedObservedRemoteHeadRef { get; init; }
    public required string MatchedExpectedBaseRef { get; init; }
    public required string MatchedObservedBaseRef { get; init; }
    public required string MatchedExpectedSourceStateRef { get; init; }
    public required string MatchedObservedSourceStateRef { get; init; }
    public required string MatchedExpectedPatchPackageRef { get; init; }
    public required string MatchedObservedPatchPackageRef { get; init; }
    public required string MatchedExpectedCommitRef { get; init; }
    public required string MatchedObservedCommitRef { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresDirtyWorktreeGuard { get; init; }
    public required bool RequiresMovedBaseGuard { get; init; }
    public required bool RequiresStaleValidationGuard { get; init; }
    public required bool RequiresConcurrentGuard { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

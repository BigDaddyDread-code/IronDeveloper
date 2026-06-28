namespace IronDev.Core.Governance;

public enum MovedBaseGuardSubjectKind
{
    Unknown = 0,
    PatchApplyTarget = 1,
    SourceApplyTarget = 2,
    CommitTarget = 3,
    PushTarget = 4,
    DraftPullRequestTarget = 5,
    MergeTarget = 6,
    RollbackTarget = 7,
    RecoveryTarget = 8,
    WorkflowContinuationTarget = 9,
    ReleaseCandidateTarget = 10,
    DeploymentTarget = 11
}

public enum MovedBaseObservedState
{
    Unknown = 0,
    Matching = 1,
    BaseMoved = 2,
    HeadMoved = 3,
    RemoteHeadMoved = 4,
    BranchMoved = 5,
    MergeBaseMoved = 6,
    Diverged = 7,
    Behind = 8,
    Ahead = 9,
    Missing = 10,
    Deleted = 11,
    Unavailable = 12,
    Ambiguous = 13
}

public enum MovedBaseEvidenceKind
{
    Unknown = 0,
    RefObservation = 1,
    PostStateObservation = 2,
    DirtyWorktreeGuardEvidence = 3,
    ValidationReceipt = 4,
    PatchPackageReceipt = 5,
    CommitPackageReceipt = 6,
    PushReceipt = 7,
    PullRequestProviderMetadata = 8,
    ProviderMetadata = 9,
    OperatorReportedObservation = 10,
    SyntheticTestObservation = 11
}

public enum MovedBaseEvidenceTrustLevel
{
    Unknown = 0,
    SelfReported = 1,
    RefObservationBacked = 2,
    PostStateObservationBacked = 3,
    DirtyWorktreeGuardBacked = 4,
    ReceiptBacked = 5,
    ProviderMetadataBacked = 6,
    OperatorObserved = 7,
    TestFixture = 8
}

public enum MovedBaseObservationFreshness
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3,
    NotTimestamped = 4
}

public enum MovedBaseGuardDecisionKind
{
    Invalid = 0,
    MayProceedToNextAuthorityGate = 1,
    BlockedByMovedBase = 2,
    BlockedByMovedHead = 3,
    BlockedByMovedRemoteHead = 4,
    BlockedByMovedBranch = 5,
    BlockedByMovedMergeBase = 6,
    BlockedByDivergedRef = 7,
    BlockedByUnknownBaseState = 8,
    BlockedByStaleRefObservation = 9,
    BlockedByExpiredRefObservation = 10,
    BlockedByUntrustedRefEvidence = 11,
    BlockedByMissingRefEvidence = 12,
    BlockedByInconsistentRefEvidence = 13,
    BlockedByUnsafePayload = 14
}

public enum MovedBaseGuardBlockKind
{
    None = 0,
    InvalidRequest = 1,
    MovedBase = 2,
    MovedHead = 3,
    MovedRemoteHead = 4,
    MovedBranch = 5,
    MovedMergeBase = 6,
    DivergedRef = 7,
    UnknownBaseState = 8,
    StaleObservation = 9,
    ExpiredObservation = 10,
    UntrustedEvidence = 11,
    MissingEvidence = 12,
    InconsistentEvidence = 13,
    UnsafePayload = 14
}

public sealed record MovedBaseGuardRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required MovedBaseGuardSubjectKind SubjectKind { get; init; }
    public required MovedBaseObservedState ObservedState { get; init; }
    public required MovedBaseEvidenceKind EvidenceKind { get; init; }
    public required MovedBaseEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required MovedBaseObservationFreshness ObservationFreshness { get; init; }
    public string? RefObservationRef { get; init; }
    public string? PostStateObservationRef { get; init; }
    public string? DirtyWorktreeGuardRef { get; init; }
    public string? ValidationReceiptRef { get; init; }
    public string? PatchPackageRef { get; init; }
    public string? CommitPackageRef { get; init; }
    public string? PushReceiptRef { get; init; }
    public string? PullRequestProviderStateRef { get; init; }
    public string? ProviderStateRef { get; init; }
    public string? OperatorObservationRef { get; init; }
    public string? ExpectedBaseRef { get; init; }
    public string? ObservedBaseRef { get; init; }
    public string? ExpectedHeadRef { get; init; }
    public string? ObservedHeadRef { get; init; }
    public string? ExpectedRemoteHeadRef { get; init; }
    public string? ObservedRemoteHeadRef { get; init; }
    public string? ExpectedBranchRef { get; init; }
    public string? ObservedBranchRef { get; init; }
    public string? ExpectedMergeBaseFingerprint { get; init; }
    public string? ObservedMergeBaseFingerprint { get; init; }
    public string? ExpectedTargetFingerprint { get; init; }
    public string? ObservedTargetFingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? EvidenceExpiresAtUtc { get; init; }
    public required string GuardVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record MovedBaseGuardValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record MovedBaseGuardDecision
{
    public required MovedBaseGuardDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required MovedBaseGuardBlockKind BlockKind { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required MovedBaseGuardSubjectKind SubjectKind { get; init; }
    public required MovedBaseObservedState ObservedState { get; init; }
    public required MovedBaseEvidenceKind EvidenceKind { get; init; }
    public required MovedBaseEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required MovedBaseObservationFreshness ObservationFreshness { get; init; }
    public required string MatchedRefObservationRef { get; init; }
    public required string MatchedPostStateObservationRef { get; init; }
    public required string MatchedDirtyWorktreeGuardRef { get; init; }
    public required string MatchedValidationReceiptRef { get; init; }
    public required string MatchedPatchPackageRef { get; init; }
    public required string MatchedCommitPackageRef { get; init; }
    public required string MatchedPushReceiptRef { get; init; }
    public required string MatchedPullRequestProviderStateRef { get; init; }
    public required string MatchedProviderStateRef { get; init; }
    public required string MatchedOperatorObservationRef { get; init; }
    public required string MatchedExpectedBaseRef { get; init; }
    public required string MatchedObservedBaseRef { get; init; }
    public required string MatchedExpectedHeadRef { get; init; }
    public required string MatchedObservedHeadRef { get; init; }
    public required string MatchedExpectedRemoteHeadRef { get; init; }
    public required string MatchedObservedRemoteHeadRef { get; init; }
    public required string MatchedExpectedBranchRef { get; init; }
    public required string MatchedObservedBranchRef { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresFreshConcurrentGuard { get; init; }
    public required bool RequiresDirtyWorktreeGuard { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

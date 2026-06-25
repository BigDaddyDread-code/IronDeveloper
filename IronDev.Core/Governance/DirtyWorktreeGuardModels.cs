namespace IronDev.Core.Governance;

public enum DirtyWorktreeGuardSubjectKind
{
    Unknown = 0,
    RepositoryWorktree = 1,
    PatchApplyTarget = 2,
    CommitTarget = 3,
    PushTarget = 4,
    DraftPullRequestTarget = 5,
    RollbackTarget = 6,
    RecoveryTarget = 7,
    WorkflowContinuationTarget = 8
}

public enum DirtyWorktreeState
{
    Unknown = 0,
    Clean = 1,
    Dirty = 2,
    Modified = 3,
    Untracked = 4,
    Deleted = 5,
    Renamed = 6,
    Conflict = 7,
    MergeInProgress = 8,
    RebaseInProgress = 9,
    CherryPickInProgress = 10,
    DetachedHead = 11,
    IndexLocked = 12,
    Unreadable = 13,
    Unavailable = 14
}

public enum DirtyWorktreeEvidenceKind
{
    Unknown = 0,
    PostStateObservation = 1,
    WorktreeStateObservation = 2,
    ProviderMetadata = 3,
    ReceiptBackedObservation = 4,
    OperatorReportedObservation = 5,
    SyntheticTestObservation = 6
}

public enum DirtyWorktreeEvidenceTrustLevel
{
    Unknown = 0,
    SelfReported = 1,
    PostStateObservationBacked = 2,
    ReceiptBacked = 3,
    ProviderMetadataBacked = 4,
    OperatorObserved = 5,
    TestFixture = 6
}

public enum DirtyWorktreeObservationFreshness
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3,
    NotTimestamped = 4
}

public enum DirtyWorktreeGuardDecisionKind
{
    Invalid = 0,
    MayProceedToNextAuthorityGate = 1,
    BlockedByDirtyWorktree = 2,
    BlockedByUnknownWorktreeState = 3,
    BlockedByStaleWorktreeObservation = 4,
    BlockedByExpiredWorktreeObservation = 5,
    BlockedByUntrustedWorktreeEvidence = 6,
    BlockedByMissingWorktreeEvidence = 7,
    BlockedByInconsistentWorktreeEvidence = 8,
    BlockedByUnsafePayload = 9
}

public enum DirtyWorktreeGuardBlockKind
{
    None = 0,
    InvalidRequest = 1,
    DirtyWorktree = 2,
    UnknownWorktreeState = 3,
    StaleObservation = 4,
    ExpiredObservation = 5,
    UntrustedEvidence = 6,
    MissingEvidence = 7,
    InconsistentEvidence = 8,
    UnsafePayload = 9
}

public sealed record DirtyWorktreeGuardRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required DirtyWorktreeGuardSubjectKind SubjectKind { get; init; }
    public required DirtyWorktreeState WorktreeState { get; init; }
    public required DirtyWorktreeEvidenceKind EvidenceKind { get; init; }
    public required DirtyWorktreeEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required DirtyWorktreeObservationFreshness ObservationFreshness { get; init; }
    public string? WorktreeObservationRef { get; init; }
    public string? PostStateObservationRef { get; init; }
    public string? FailureClassificationRef { get; init; }
    public string? FailureReceiptRef { get; init; }
    public string? MutationReceiptRef { get; init; }
    public string? ProviderStateRef { get; init; }
    public string? OperatorObservationRef { get; init; }
    public string? ExpectedHeadRef { get; init; }
    public string? ObservedHeadRef { get; init; }
    public string? ExpectedBranchRef { get; init; }
    public string? ObservedBranchRef { get; init; }
    public string? ExpectedWorktreeFingerprint { get; init; }
    public string? ObservedWorktreeFingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? EvidenceExpiresAtUtc { get; init; }
    public required string GuardVersion { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
}

public sealed record DirtyWorktreeGuardValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record DirtyWorktreeGuardDecision
{
    public required DirtyWorktreeGuardDecisionKind Decision { get; init; }
    public required string Reason { get; init; }
    public required DirtyWorktreeGuardBlockKind BlockKind { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required MutationLeaseSurfaceKind MutationSurface { get; init; }
    public required string AttemptRef { get; init; }
    public required string TargetRef { get; init; }
    public required string GuardRef { get; init; }
    public required DirtyWorktreeGuardSubjectKind SubjectKind { get; init; }
    public required DirtyWorktreeState WorktreeState { get; init; }
    public required DirtyWorktreeEvidenceKind EvidenceKind { get; init; }
    public required DirtyWorktreeEvidenceTrustLevel EvidenceTrustLevel { get; init; }
    public required DirtyWorktreeObservationFreshness ObservationFreshness { get; init; }
    public required string MatchedWorktreeObservationRef { get; init; }
    public required string MatchedPostStateObservationRef { get; init; }
    public required string MatchedFailureClassificationRef { get; init; }
    public required string MatchedFailureReceiptRef { get; init; }
    public required string MatchedMutationReceiptRef { get; init; }
    public required string MatchedProviderStateRef { get; init; }
    public required string MatchedOperatorObservationRef { get; init; }
    public required string MatchedExpectedHeadRef { get; init; }
    public required string MatchedObservedHeadRef { get; init; }
    public required string MatchedExpectedBranchRef { get; init; }
    public required string MatchedObservedBranchRef { get; init; }
    public required bool RequiresFreshAuthority { get; init; }
    public required bool RequiresFreshValidation { get; init; }
    public required bool RequiresFreshConcurrentGuard { get; init; }
    public required bool RequiresFreshPostStateObservation { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

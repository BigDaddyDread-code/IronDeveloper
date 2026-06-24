namespace IronDev.Core.Governance;

public enum WorktreeStateKind
{
    Unknown = 0,
    Clean = 1,
    Dirty = 2,
    UntrackedOnly = 3,
    Conflicted = 4,
    Unavailable = 5
}

public enum HeadStateKind
{
    Unknown = 0,
    Attached = 1,
    Detached = 2,
    Missing = 3,
    Unavailable = 4
}

public enum WorktreeBaseHeadFreshnessState
{
    Unknown = 0,
    Fresh = 1,
    WorktreeChanged = 2,
    WorktreeConflicted = 3,
    BaseMoved = 4,
    HeadMoved = 5,
    HeadDetached = 6,
    HeadMissing = 7,
    RepositoryMismatch = 8,
    ObservationStale = 9,
    ObservationExpired = 10,
    MissingExpectation = 11,
    MissingObservation = 12,
    MissingRule = 13,
    AmbiguousObservation = 14,
    Unassessable = 15
}

public enum WorktreeBaseHeadFreshnessResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoObservations = 2,
    Assessed = 3,
    MixedFreshness = 4,
    MissingExpectations = 5,
    MissingObservations = 6,
    MissingRules = 7,
    AmbiguousObservations = 8,
    Unassessable = 9
}

public sealed record WorktreeBaseHeadFreshnessRule
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string RuleId { get; init; }
    public required TimeSpan ObservationFreshFor { get; init; }
    public required TimeSpan ObservationExpiresAfter { get; init; }
    public bool RequireRepositoryIdentityMatch { get; init; } = true;
    public bool RequireWorktreeClean { get; init; } = true;
    public bool RequireBaseBranchMatch { get; init; } = true;
    public bool RequireBaseCommitMatch { get; init; } = true;
    public bool RequireHeadBranchMatch { get; init; } = true;
    public bool RequireHeadCommitMatch { get; init; } = true;
    public bool RequireAttachedHead { get; init; } = true;
    public required string Source { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ExpectedWorktreeBaseHeadMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ExpectationId { get; init; }
    public required string RepositoryIdentity { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommitSha { get; init; }
    public string? HeadBranch { get; init; }
    public string? HeadCommitSha { get; init; }
    public required WorktreeStateKind ExpectedWorktreeState { get; init; }
    public required HeadStateKind ExpectedHeadState { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record ObservedWorktreeBaseHeadMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ObservationId { get; init; }
    public required string RepositoryIdentity { get; init; }
    public required WorktreeStateKind WorktreeState { get; init; }
    public required HeadStateKind HeadState { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommitSha { get; init; }
    public string? HeadBranch { get; init; }
    public string? HeadCommitSha { get; init; }
    public bool HasUncommittedChanges { get; init; }
    public bool HasUntrackedFiles { get; init; }
    public bool HasConflicts { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record WorktreeBaseHeadFreshnessReadModelRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<WorktreeBaseHeadFreshnessRule> Rules { get; init; }
    public required IReadOnlyList<ExpectedWorktreeBaseHeadMetadata> Expectations { get; init; }
    public required IReadOnlyList<ObservedWorktreeBaseHeadMetadata> Observations { get; init; }
}

public sealed record WorktreeBaseHeadFreshnessAssessment
{
    public string? ExpectationId { get; init; }
    public string? ObservationId { get; init; }
    public required WorktreeBaseHeadFreshnessState FreshnessState { get; init; }
    public required string RepositoryIdentity { get; init; }
    public string? ExpectedBaseBranch { get; init; }
    public string? ObservedBaseBranch { get; init; }
    public string? ExpectedBaseCommitSha { get; init; }
    public string? ObservedBaseCommitSha { get; init; }
    public string? ExpectedHeadBranch { get; init; }
    public string? ObservedHeadBranch { get; init; }
    public string? ExpectedHeadCommitSha { get; init; }
    public string? ObservedHeadCommitSha { get; init; }
    public WorktreeStateKind? ExpectedWorktreeState { get; init; }
    public WorktreeStateKind? ObservedWorktreeState { get; init; }
    public HeadStateKind? ExpectedHeadState { get; init; }
    public HeadStateKind? ObservedHeadState { get; init; }
    public required bool HasUncommittedChanges { get; init; }
    public required bool HasUntrackedFiles { get; init; }
    public required bool HasConflicts { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public required TimeSpan Age { get; init; }
    public DateTimeOffset? FreshUntilUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? RuleId { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required bool IsRedacted { get; init; }
    public required string Reason { get; init; }
}

public sealed record WorktreeBaseHeadFreshnessReadModel
{
    public required bool IsValid { get; init; }
    public required WorktreeBaseHeadFreshnessResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<WorktreeBaseHeadFreshnessAssessment> Assessments { get; init; }
    public required IReadOnlyList<string> AmbiguousObservations { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

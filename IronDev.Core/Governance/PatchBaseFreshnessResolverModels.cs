namespace IronDev.Core.Governance;

public enum PatchHashAlgorithm
{
    Unknown = 0,
    Sha256 = 1,
    Sha512 = 2
}

public enum PatchBaseFreshnessState
{
    Unknown = 0,
    Fresh = 1,
    PatchStale = 2,
    PatchExpired = 3,
    PatchHashMismatch = 4,
    BaseBranchMoved = 5,
    MissingBaseObservation = 6,
    MissingRule = 7,
    Unassessable = 8
}

public enum PatchBaseFreshnessResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoPatchArtifacts = 2,
    Assessed = 3,
    MixedFreshness = 4,
    MissingRules = 5,
    MissingBaseObservations = 6,
    AmbiguousPatchBaseMetadata = 7,
    Unassessable = 8
}

public enum PatchArtifactKind
{
    Unknown = 0,
    GeneratedPatch = 1,
    DryRunPatch = 2,
    ApprovedPatchPackage = 3,
    RollbackPatch = 4,
    RecoveryPatch = 5,
    Custom = 6
}

public sealed record PatchBaseFreshnessRule
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string RuleId { get; init; }
    public required PatchArtifactKind PatchKind { get; init; }
    public required TimeSpan FreshFor { get; init; }
    public required TimeSpan ExpiresAfter { get; init; }
    public bool RequireBaseBranchMatch { get; init; } = true;
    public bool RequirePatchHashMatch { get; init; } = true;
    public required string Source { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record PatchArtifactFreshnessMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string PatchArtifactId { get; init; }
    public required PatchArtifactKind PatchKind { get; init; }
    public required string PatchHash { get; init; }
    public required PatchHashAlgorithm HashAlgorithm { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommitSha { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record BaseBranchObservationMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string BaseBranch { get; init; }
    public required string ObservedBaseCommitSha { get; init; }
    public string? ObservedPatchHash { get; init; }
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

public sealed record PatchBaseFreshnessResolverRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<PatchBaseFreshnessRule> Rules { get; init; }
    public required IReadOnlyList<PatchArtifactFreshnessMetadata> PatchArtifacts { get; init; }
    public required IReadOnlyList<BaseBranchObservationMetadata> BaseBranchObservations { get; init; }
}

public sealed record PatchBaseFreshnessAssessment
{
    public required string PatchArtifactId { get; init; }
    public required PatchArtifactKind PatchKind { get; init; }
    public required PatchBaseFreshnessState FreshnessState { get; init; }
    public required string PatchHash { get; init; }
    public required PatchHashAlgorithm HashAlgorithm { get; init; }
    public required string BaseBranch { get; init; }
    public required string PatchBaseCommitSha { get; init; }
    public string? ObservedBaseCommitSha { get; init; }
    public required DateTimeOffset PatchCreatedAtUtc { get; init; }
    public DateTimeOffset? BaseObservedAtUtc { get; init; }
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

public sealed record PatchBaseFreshnessResolverResult
{
    public required bool IsValid { get; init; }
    public required PatchBaseFreshnessResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<PatchBaseFreshnessAssessment> Assessments { get; init; }
    public required IReadOnlyList<string> AmbiguousPatchBaseMetadata { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

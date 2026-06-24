namespace IronDev.Core.Governance;

public enum ForbiddenActionKind
{
    Unknown = 0,
    SourceApply = 1,
    CommitPackageCreate = 2,
    Commit = 3,
    Push = 4,
    DraftPullRequestCreate = 5,
    PullRequestReadyForReview = 6,
    Merge = 7,
    ReleaseReadinessDecision = 8,
    ReleaseExecution = 9,
    DeploymentReadinessDecision = 10,
    DeploymentExecution = 11,
    Rollback = 12,
    Retry = 13,
    WorkflowContinuation = 14,
    MemoryPromotion = 15,
    AdminChange = 16
}

public enum ForbiddenActionFactKind
{
    Unknown = 0,
    MissingEvidence = 1,
    AmbiguousEvidence = 2,
    MissingAcceptedApprovalReference = 3,
    MissingPolicySatisfactionReference = 4,
    ValidationFreshnessUnknown = 5,
    ValidationStale = 6,
    ValidationExpired = 7,
    PatchFreshnessUnknown = 8,
    PatchStale = 9,
    WorktreeStateUnknown = 10,
    WorktreeUnsafe = 11,
    BaseHeadStateUnknown = 12,
    BaseMoved = 13,
    OperationInterrupted = 14,
    OperationFailed = 15,
    RollbackStateObserved = 16,
    RecoveryStateObserved = 17,
    ProjectedStatusUnavailable = 18,
    ProjectedStatusInvalid = 19,
    CapabilityUnavailable = 20,
    RoleVisibilityOnly = 21,
    TenantScopeMismatch = 22,
    ProjectScopeMismatch = 23,
    ExplicitGovernanceBlock = 24
}

public enum ForbiddenActionFactSeverity
{
    Unknown = 0,
    Info = 1,
    Warning = 2,
    Blocking = 3,
    Critical = 4
}

public sealed record ForbiddenActionInputFact
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string FactId { get; init; }
    public required ForbiddenActionFactKind FactKind { get; init; }
    public required ForbiddenActionFactSeverity Severity { get; init; }
    public required bool IsBlocking { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public string? DisplayLabel { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public enum ForbiddenActionResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoActionRequested = 2,
    NoForbiddenFactsObserved = 3,
    Forbidden = 4,
    AmbiguousFacts = 5
}

public sealed record ForbiddenActionResolverRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required ForbiddenActionKind ActionKind { get; init; }
    public OperationProjectedStatusKind ProjectedStatusKind { get; init; } = OperationProjectedStatusKind.Unknown;
    public MissingEvidenceResolutionStatus MissingEvidenceStatus { get; init; } = MissingEvidenceResolutionStatus.Unknown;
    public required IReadOnlyList<ForbiddenActionInputFact> Facts { get; init; }
}

public sealed record ForbiddenActionFinding
{
    public required string FactId { get; init; }
    public required ForbiddenActionFactKind FactKind { get; init; }
    public required ForbiddenActionFactSeverity Severity { get; init; }
    public required string Reason { get; init; }
    public required string Source { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required bool IsRedacted { get; init; }
}

public sealed record ForbiddenActionResolutionResult
{
    public required bool IsValid { get; init; }
    public required ForbiddenActionResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required ForbiddenActionKind ActionKind { get; init; }
    public required IReadOnlyList<ForbiddenActionFinding> Findings { get; init; }
    public required IReadOnlyList<string> AmbiguousFacts { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

namespace IronDev.Core.Governance;

public enum ValidationResultKind
{
    Unknown = 0,
    FocusedTests = 1,
    StackedLane = 2,
    GovernanceStatusCorridor = 3,
    ReadAdapterCorridor = 4,
    Build = 5,
    DiffCheck = 6,
    CachedDiffCheck = 7,
    SqlIntegration = 8,
    GovernanceBoundary = 9,
    FrontendContract = 10,
    StaticAnalysis = 11,
    SecurityScan = 12,
    ReceiptBoundary = 13,
    EvidenceBoundary = 14,
    Custom = 15
}

public enum ValidationResultOutcome
{
    Unknown = 0,
    Passed = 1,
    Failed = 2,
    Errored = 3,
    Cancelled = 4,
    Skipped = 5,
    NotRun = 6
}

public enum ValidationStalenessState
{
    Unknown = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3,
    MissingRule = 4,
    Unassessable = 5
}

public enum ValidationStalenessResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoValidationResults = 2,
    Assessed = 3,
    MixedStaleness = 4,
    MissingRules = 5,
    AmbiguousValidationResults = 6,
    Unassessable = 7
}

public sealed record ValidationStalenessRule
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string RuleId { get; init; }
    public required ValidationResultKind ValidationKind { get; init; }
    public required TimeSpan FreshFor { get; init; }
    public required TimeSpan ExpiresAfter { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ValidationResultMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ValidationResultId { get; init; }
    public required ValidationResultKind ValidationKind { get; init; }
    public required ValidationResultOutcome Outcome { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record ValidationStalenessResolverRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<ValidationStalenessRule> Rules { get; init; }
    public required IReadOnlyList<ValidationResultMetadata> ValidationResults { get; init; }
}

public sealed record ValidationResultStalenessAssessment
{
    public required string ValidationResultId { get; init; }
    public required ValidationResultKind ValidationKind { get; init; }
    public required ValidationResultOutcome Outcome { get; init; }
    public required ValidationStalenessState StalenessState { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
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

public sealed record ValidationStalenessResolverResult
{
    public required bool IsValid { get; init; }
    public required ValidationStalenessResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<ValidationResultStalenessAssessment> Assessments { get; init; }
    public required IReadOnlyList<string> AmbiguousValidationResults { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

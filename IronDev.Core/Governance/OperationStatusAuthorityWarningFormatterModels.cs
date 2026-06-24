namespace IronDev.Core.Governance;

public enum OperationStatusAuthorityWarningKind
{
    Unknown = 0,
    NoWarning = 1,
    StatusIsNotAuthority = 2,
    EvidenceIsNotApproval = 3,
    ReceiptIsNotExecutionProof = 4,
    ValidationIsNotApproval = 5,
    FreshnessIsNotPermission = 6,
    WorktreeStateIsNotMutationAuthority = 7,
    InterruptedRunIsNotRetryAuthority = 8,
    RollbackPlanIsNotRollbackExecution = 9,
    RecoveryPlanIsNotRecoveryAuthority = 10,
    PaginationIsNotActionQueue = 11,
    EnvelopeIsNotAuthority = 12,
    NextSafeActionTextIsDisplayOnly = 13,
    RedactionIsNotDenial = 14,
    UiStateIsNotAuthority = 15,
    ManualAuthorityReviewRequired = 16
}

public enum OperationStatusAuthorityWarningSeverity
{
    Unknown = 0,
    Info = 1,
    Notice = 2,
    Warning = 3,
    BoundaryWarning = 4
}

public enum OperationStatusAuthorityWarningFormatterStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoWarnings = 2,
    Formatted = 3,
    AmbiguousInput = 4,
    Unassessable = 5
}

public sealed record OperationStatusAuthorityWarningFacts
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public OperationProjectedStatusKind ProjectedStatus { get; init; } = OperationProjectedStatusKind.Unknown;
    public OperationStatusReadEnvelopeKind EnvelopeKind { get; init; } = OperationStatusReadEnvelopeKind.Unknown;
    public OperationStatusReadErrorCode EnvelopeErrorCode { get; init; } = OperationStatusReadErrorCode.Unknown;
    public MissingEvidenceResolutionStatus MissingEvidenceStatus { get; init; } = MissingEvidenceResolutionStatus.Unknown;
    public ForbiddenActionResolutionStatus ForbiddenActionStatus { get; init; } = ForbiddenActionResolutionStatus.Unknown;
    public ReceiptReferenceResolutionStatus ReceiptResolutionStatus { get; init; } = ReceiptReferenceResolutionStatus.Unknown;
    public EvidenceResolutionStatus EvidenceResolutionStatus { get; init; } = EvidenceResolutionStatus.Unknown;
    public ValidationStalenessResolutionStatus ValidationStalenessStatus { get; init; } = ValidationStalenessResolutionStatus.Unknown;
    public PatchBaseFreshnessResolutionStatus PatchBaseFreshnessStatus { get; init; } = PatchBaseFreshnessResolutionStatus.Unknown;
    public WorktreeBaseHeadFreshnessResolutionStatus WorktreeBaseHeadFreshnessStatus { get; init; } = WorktreeBaseHeadFreshnessResolutionStatus.Unknown;
    public InterruptedRunReadModelStatus InterruptedRunStatus { get; init; } = InterruptedRunReadModelStatus.Unknown;
    public RollbackRecoveryReadModelStatus RollbackRecoveryStatus { get; init; } = RollbackRecoveryReadModelStatus.Unknown;
    public OperationStatusNextSafeActionFormatterStatus NextSafeActionFormatterStatus { get; init; } = OperationStatusNextSafeActionFormatterStatus.Unknown;
    public bool HasNextSafeActionDisplayLines { get; init; }
    public bool HasPageSummary { get; init; }
    public bool HasRedactedSummary { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record OperationStatusAuthorityWarningFormatterRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public OperationStatusReadEnvelope? ReadEnvelope { get; init; }
    public OperationStatusNextSafeActionFormatterResult? NextSafeActionFormatterResult { get; init; }
    public OperationStatusAuthorityWarningFacts? WarningFacts { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationStatusAuthorityWarningLine
{
    public required OperationStatusAuthorityWarningKind WarningKind { get; init; }
    public required OperationStatusAuthorityWarningSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Boundary { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationStatusAuthorityWarningFormatterResult
{
    public required bool IsValid { get; init; }
    public required OperationStatusAuthorityWarningFormatterStatus FormatterStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<OperationStatusAuthorityWarningLine> Lines { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record OperationStatusAuthorityWarningFormatterValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

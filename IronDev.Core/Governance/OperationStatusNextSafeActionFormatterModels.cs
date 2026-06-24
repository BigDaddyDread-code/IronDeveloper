namespace IronDev.Core.Governance;

public enum OperationStatusNextSafeActionDisplayKind
{
    Unknown = 0,
    NoGuidance = 1,
    ReviewInvalidRequest = 2,
    ReviewNotFound = 3,
    ReviewAmbiguousStatus = 4,
    ReviewUnassessableStatus = 5,
    ReviewRedactedStatus = 6,
    ReviewMissingEvidence = 7,
    ReviewForbiddenActionFacts = 8,
    ReviewReceiptReferences = 9,
    ReviewEvidenceReferences = 10,
    ReviewValidationStaleness = 11,
    ReviewPatchBaseFreshness = 12,
    ReviewWorktreeBaseHeadFreshness = 13,
    ReviewInterruptedRun = 14,
    ReviewRollbackRecoveryMaterial = 15,
    ReviewStatusPage = 16,
    ManualReviewRequired = 17
}

public enum OperationStatusNextSafeActionSeverity
{
    Unknown = 0,
    Info = 1,
    Notice = 2,
    Warning = 3,
    BlockedDisplay = 4
}

public enum OperationStatusNextSafeActionFormatterStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoGuidance = 2,
    Formatted = 3,
    AmbiguousInput = 4,
    Unassessable = 5
}

public sealed record OperationStatusNextSafeActionDiagnosticFacts
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
    public required string Source { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record OperationStatusNextSafeActionFormatterRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public OperationStatusReadEnvelope? ReadEnvelope { get; init; }
    public OperationStatusNextSafeActionDiagnosticFacts? DiagnosticFacts { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationStatusNextSafeActionLine
{
    public required OperationStatusNextSafeActionDisplayKind DisplayKind { get; init; }
    public required OperationStatusNextSafeActionSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Rationale { get; init; }
    public required string AuthorityBoundary { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationStatusNextSafeActionFormatterResult
{
    public required bool IsValid { get; init; }
    public required OperationStatusNextSafeActionFormatterStatus FormatterStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<OperationStatusNextSafeActionLine> Lines { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record OperationStatusNextSafeActionFormatterValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

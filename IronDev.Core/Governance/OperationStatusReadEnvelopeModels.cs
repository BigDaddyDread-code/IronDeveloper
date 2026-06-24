namespace IronDev.Core.Governance;

public enum OperationStatusReadKind
{
    Unknown = 0,
    SingleStatus = 1,
    StatusPage = 2,
    TimelineSummary = 3,
    DiagnosticSummary = 4,
    CursorPage = 5,
    ReferenceLookupStatus = 6
}

public enum OperationStatusReadEnvelopeKind
{
    Unknown = 0,
    Success = 1,
    NotFound = 2,
    InvalidRequest = 3,
    Ambiguous = 4,
    Unassessable = 5,
    Redacted = 6,
    Error = 7
}

public enum OperationStatusReadErrorCode
{
    Unknown = 0,
    None = 1,
    OperationStatusNotFound = 2,
    OperationStatusPageNotFound = 3,
    OperationStatusCursorInvalid = 4,
    OperationStatusCursorAmbiguous = 5,
    OperationStatusRequestInvalid = 6,
    OperationStatusScopeInvalid = 7,
    OperationStatusInputAmbiguous = 8,
    OperationStatusUnassessable = 9,
    OperationStatusRedacted = 10,
    OperationStatusReadModelError = 11
}

public enum OperationStatusReadIssueSeverity
{
    Unknown = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

public sealed record OperationStatusReadContext
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public required OperationStatusReadKind ReadKind { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationStatusDiagnosticStatusSummary
{
    public MissingEvidenceResolutionStatus MissingEvidenceStatus { get; init; } = MissingEvidenceResolutionStatus.Unknown;
    public ForbiddenActionResolutionStatus ForbiddenActionStatus { get; init; } = ForbiddenActionResolutionStatus.Unknown;
    public ReceiptReferenceResolutionStatus ReceiptResolutionStatus { get; init; } = ReceiptReferenceResolutionStatus.Unknown;
    public EvidenceResolutionStatus EvidenceResolutionStatus { get; init; } = EvidenceResolutionStatus.Unknown;
    public ValidationStalenessResolutionStatus ValidationStalenessStatus { get; init; } = ValidationStalenessResolutionStatus.Unknown;
    public PatchBaseFreshnessResolutionStatus PatchBaseFreshnessStatus { get; init; } = PatchBaseFreshnessResolutionStatus.Unknown;
    public WorktreeBaseHeadFreshnessResolutionStatus WorktreeBaseHeadFreshnessStatus { get; init; } = WorktreeBaseHeadFreshnessResolutionStatus.Unknown;
    public InterruptedRunReadModelStatus InterruptedRunStatus { get; init; } = InterruptedRunReadModelStatus.Unknown;
    public RollbackRecoveryReadModelStatus RollbackRecoveryStatus { get; init; } = RollbackRecoveryReadModelStatus.Unknown;
}

public sealed record OperationStatusSafeSummary
{
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required OperationProjectedStatusKind ProjectedStatus { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required DateTimeOffset LastEventAtUtc { get; init; }
    public required int TimelineEventCount { get; init; }
    public required OperationStatusDiagnosticStatusSummary DiagnosticStatusSummary { get; init; }
    public required bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record OperationStatusPageEnvelopeSummary
{
    public required int PageSize { get; init; }
    public required int ItemCount { get; init; }
    public required int MatchedCount { get; init; }
    public required int ScannedCount { get; init; }
    public required bool HasMore { get; init; }
    public required bool HasNextCursor { get; init; }
    public required string CursorState { get; init; }
}

public sealed record OperationStatusReadIssue
{
    public required OperationStatusReadErrorCode Code { get; init; }
    public required string Message { get; init; }
    public required OperationStatusReadIssueSeverity Severity { get; init; }
    public string? Field { get; init; }
    public required bool IsUserCorrectable { get; init; }
}

public sealed record OperationStatusReadEnvelope
{
    public required bool IsValid { get; init; }
    public required OperationStatusReadEnvelopeKind EnvelopeKind { get; init; }
    public required OperationStatusReadErrorCode ErrorCode { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public required OperationStatusReadKind ReadKind { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required string Source { get; init; }
    public OperationStatusSafeSummary? SafeSummary { get; init; }
    public OperationStatusPageEnvelopeSummary? PageSummary { get; init; }
    public required IReadOnlyList<OperationStatusReadIssue> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record OperationStatusReadEnvelopeValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

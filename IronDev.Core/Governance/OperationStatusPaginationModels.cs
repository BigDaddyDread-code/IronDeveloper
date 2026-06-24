namespace IronDev.Core.Governance;

public enum OperationStatusPageSortField
{
    Unknown = 0,
    CreatedAtUtc = 1,
    UpdatedAtUtc = 2,
    LastEventAtUtc = 3,
    OperationId = 4,
    CorrelationId = 5,
    ProjectedStatus = 6
}

public enum OperationStatusPageSortDirection
{
    Unknown = 0,
    Ascending = 1,
    Descending = 2
}

public enum OperationStatusPageResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoRows = 2,
    NoMatches = 3,
    PageReturned = 4,
    CursorExhausted = 5,
    AmbiguousCursor = 6,
    Unassessable = 7
}

public sealed record OperationStatusSummaryRow
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required OperationProjectedStatusKind ProjectedStatus { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required DateTimeOffset LastEventAtUtc { get; init; }
    public required int TimelineEventCount { get; init; }
    public OperationCorrelationSurfaceKind SurfaceKind { get; init; } = OperationCorrelationSurfaceKind.Unknown;
    public string? SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
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
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record OperationStatusPageFilter
{
    public string? OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<OperationProjectedStatusKind> ProjectedStatuses { get; init; } = [];
    public OperationCorrelationSurfaceKind SurfaceKind { get; init; } = OperationCorrelationSurfaceKind.Unknown;
    public string? SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public DateTimeOffset? CreatedFromUtc { get; init; }
    public DateTimeOffset? CreatedToUtc { get; init; }
    public DateTimeOffset? UpdatedFromUtc { get; init; }
    public DateTimeOffset? UpdatedToUtc { get; init; }
    public DateTimeOffset? LastEventFromUtc { get; init; }
    public DateTimeOffset? LastEventToUtc { get; init; }
    public IReadOnlyList<MissingEvidenceResolutionStatus> MissingEvidenceStatuses { get; init; } = [];
    public IReadOnlyList<ForbiddenActionResolutionStatus> ForbiddenActionStatuses { get; init; } = [];
    public IReadOnlyList<ReceiptReferenceResolutionStatus> ReceiptResolutionStatuses { get; init; } = [];
    public IReadOnlyList<EvidenceResolutionStatus> EvidenceResolutionStatuses { get; init; } = [];
    public IReadOnlyList<ValidationStalenessResolutionStatus> ValidationStalenessStatuses { get; init; } = [];
    public IReadOnlyList<PatchBaseFreshnessResolutionStatus> PatchBaseFreshnessStatuses { get; init; } = [];
    public IReadOnlyList<WorktreeBaseHeadFreshnessResolutionStatus> WorktreeBaseHeadFreshnessStatuses { get; init; } = [];
    public IReadOnlyList<InterruptedRunReadModelStatus> InterruptedRunStatuses { get; init; } = [];
    public IReadOnlyList<RollbackRecoveryReadModelStatus> RollbackRecoveryStatuses { get; init; } = [];
    public bool IncludeRedacted { get; init; }
}

public sealed record OperationStatusPageCursor
{
    public required OperationStatusPageSortField SortField { get; init; }
    public required OperationStatusPageSortDirection SortDirection { get; init; }
    public required string LastSortValue { get; init; }
    public required string LastOperationId { get; init; }
    public required string LastCorrelationId { get; init; }
}

public sealed record OperationStatusPageRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required int PageSize { get; init; }
    public required OperationStatusPageSortField SortField { get; init; }
    public required OperationStatusPageSortDirection SortDirection { get; init; }
    public OperationStatusPageFilter Filter { get; init; } = new();
    public OperationStatusPageCursor? Cursor { get; init; }
    public required IReadOnlyList<OperationStatusSummaryRow>? Rows { get; init; }
}

public sealed record OperationStatusPageItem
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required OperationProjectedStatusKind ProjectedStatus { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required DateTimeOffset LastEventAtUtc { get; init; }
    public required int TimelineEventCount { get; init; }
    public OperationCorrelationSurfaceKind SurfaceKind { get; init; } = OperationCorrelationSurfaceKind.Unknown;
    public string? SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
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
    public required bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
    public IReadOnlyList<string> MatchedFilterReasons { get; init; } = [];
}

public sealed record OperationStatusPageResult
{
    public required bool IsValid { get; init; }
    public required OperationStatusPageResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required int PageSize { get; init; }
    public required IReadOnlyList<OperationStatusPageItem> Items { get; init; }
    public OperationStatusPageCursor? NextCursor { get; init; }
    public required bool HasMore { get; init; }
    public required int MatchedCount { get; init; }
    public required int ScannedCount { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

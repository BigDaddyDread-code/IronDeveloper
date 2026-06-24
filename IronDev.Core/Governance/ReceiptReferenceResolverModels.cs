namespace IronDev.Core.Governance;

public enum ReceiptReferenceKind
{
    Unknown = 0,
    GenericReceipt = 1,
    EvidenceReceipt = 2,
    ValidationReceipt = 3,
    PatchArtifactReceipt = 4,
    SourceApplyReceipt = 5,
    RollbackReceipt = 6,
    RecoveryReceipt = 7,
    CommitPackageReceipt = 8,
    CommitReceipt = 9,
    PushReceipt = 10,
    PullRequestReceipt = 11,
    MergeReadinessReceipt = 12,
    ReleaseReadinessReceipt = 13,
    DeploymentReadinessReceipt = 14,
    MemoryPromotionReceipt = 15,
    WorkflowContinuationReceipt = 16,
    AuditReceipt = 17
}

public enum ReceiptReferenceResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoReferences = 2,
    Resolved = 3,
    PartiallyResolved = 4,
    NotFound = 5,
    AmbiguousReferences = 6
}

public sealed record ReceiptReferenceRequestItem
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptReferenceId { get; init; }
    public required ReceiptReferenceKind ReceiptKind { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed record AvailableReceiptMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required ReceiptReferenceKind ReceiptKind { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record ResolvedReceiptReference
{
    public required string ReceiptReferenceId { get; init; }
    public required string ReceiptId { get; init; }
    public required ReceiptReferenceKind ReceiptKind { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string Source { get; init; }
    public required bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record UnresolvedReceiptReference
{
    public required string ReceiptReferenceId { get; init; }
    public required ReceiptReferenceKind ReceiptKind { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required string Reason { get; init; }
}

public sealed record ReceiptReferenceResolverRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<ReceiptReferenceRequestItem> RequestedReferences { get; init; }
    public required IReadOnlyList<AvailableReceiptMetadata> AvailableReceipts { get; init; }
}

public sealed record ReceiptReferenceResolverResult
{
    public required bool IsValid { get; init; }
    public required ReceiptReferenceResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<ResolvedReceiptReference> ResolvedReceipts { get; init; }
    public required IReadOnlyList<UnresolvedReceiptReference> UnresolvedReceipts { get; init; }
    public required IReadOnlyList<string> AmbiguousReceipts { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

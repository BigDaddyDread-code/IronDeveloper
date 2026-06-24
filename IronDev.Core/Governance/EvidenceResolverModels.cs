namespace IronDev.Core.Governance;

public enum EvidenceReferenceKind
{
    Unknown = 0,
    GenericEvidence = 1,
    ValidationEvidence = 2,
    PatchArtifactEvidence = 3,
    BuildResultEvidence = 4,
    TestResultEvidence = 5,
    StaticAnalysisEvidence = 6,
    ReceiptEvidence = 7,
    ApprovalEvidenceReference = 8,
    PolicyEvidenceReference = 9,
    SourceApplyEvidence = 10,
    RollbackEvidence = 11,
    RecoveryEvidence = 12,
    CommitEvidence = 13,
    PushEvidence = 14,
    PullRequestEvidence = 15,
    MergeReadinessEvidence = 16,
    ReleaseReadinessEvidence = 17,
    DeploymentReadinessEvidence = 18,
    MemoryPromotionEvidence = 19,
    WorkflowContinuationEvidence = 20,
    AuditEvidence = 21
}

public enum EvidenceResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoReferences = 2,
    Resolved = 3,
    PartiallyResolved = 4,
    NotFound = 5,
    AmbiguousEvidence = 6,
    RedactionFailed = 7
}

public enum EvidencePayloadState
{
    Unknown = 0,
    MetadataOnly = 1,
    PayloadSuppliedForRedaction = 2,
    RedactedPreviewAvailable = 3,
    PayloadSuppressed = 4
}

public enum EvidenceRedactionReasonKind
{
    Unknown = 0,
    SecretDetected = 1,
    TokenDetected = 2,
    AuthorizationHeaderDetected = 3,
    ConnectionStringDetected = 4,
    PrivateKeyDetected = 5,
    PrivateReasoningDetected = 6,
    PromptOrModelTextDetected = 7,
    RawPayloadMarkerDetected = 8,
    PatchOrDiffContentDetected = 9,
    ValidationLogContentDetected = 10,
    RequestResponseBodyDetected = 11,
    PayloadTooLarge = 12,
    UnsafeControlCharacters = 13,
    UnhandledUnsafeContent = 14
}

public sealed record EvidenceReferenceRequestItem
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string EvidenceReferenceId { get; init; }
    public required EvidenceReferenceKind EvidenceKind { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public bool RequestRedactedPreview { get; init; }
}

public sealed record AvailableEvidenceMetadata
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string EvidenceId { get; init; }
    public required EvidenceReferenceKind EvidenceKind { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string Source { get; init; }
    public EvidencePayloadState PayloadState { get; init; } = EvidencePayloadState.MetadataOnly;
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record SuppliedEvidencePayloadForRedaction
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string EvidenceId { get; init; }
    public required string PayloadText { get; init; }
    public required string PayloadContentType { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset SuppliedAtUtc { get; init; }
}

public sealed record RedactedEvidencePreview
{
    public required string EvidenceId { get; init; }
    public required string PreviewText { get; init; }
    public required EvidencePayloadState PayloadState { get; init; }
    public required bool WasRedacted { get; init; }
    public required bool WasSuppressed { get; init; }
    public required IReadOnlyList<EvidenceRedactionReasonKind> RedactionReasons { get; init; }
    public required bool PreviewTruncated { get; init; }
    public required string Source { get; init; }
}

public sealed record ResolvedEvidenceReference
{
    public required string EvidenceReferenceId { get; init; }
    public required string EvidenceId { get; init; }
    public required EvidenceReferenceKind EvidenceKind { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string Source { get; init; }
    public required EvidencePayloadState PayloadState { get; init; }
    public required bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
    public RedactedEvidencePreview? RedactedPreview { get; init; }
}

public sealed record UnresolvedEvidenceReference
{
    public required string EvidenceReferenceId { get; init; }
    public required EvidenceReferenceKind EvidenceKind { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required string Reason { get; init; }
}

public sealed record EvidenceResolverRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<EvidenceReferenceRequestItem> RequestedReferences { get; init; }
    public required IReadOnlyList<AvailableEvidenceMetadata> AvailableEvidence { get; init; }
    public required IReadOnlyList<SuppliedEvidencePayloadForRedaction> SuppliedPayloadsForRedaction { get; init; }
}

public sealed record EvidenceResolverResult
{
    public required bool IsValid { get; init; }
    public required EvidenceResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<ResolvedEvidenceReference> ResolvedEvidence { get; init; }
    public required IReadOnlyList<UnresolvedEvidenceReference> UnresolvedEvidence { get; init; }
    public required IReadOnlyList<string> AmbiguousEvidence { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

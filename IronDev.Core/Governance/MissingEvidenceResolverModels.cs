namespace IronDev.Core.Governance;

public enum MissingEvidenceRequirementKind
{
    Unknown = 0,
    OperationIdentity = 1,
    CorrelationLink = 2,
    TimelineEntry = 3,
    StatusProjectionEvent = 4,
    PatchArtifactMetadata = 5,
    ValidationResultMetadata = 6,
    EvidenceMetadata = 7,
    ReceiptMetadata = 8,
    ApprovalRecordReference = 9,
    PolicySatisfactionRecordReference = 10,
    SourceApplyReceiptReference = 11,
    CommitPackageReceiptReference = 12,
    CommitReceiptReference = 13,
    PushReceiptReference = 14,
    PullRequestReceiptReference = 15,
    RollbackReceiptReference = 16,
    RecoveryReceiptReference = 17,
    ReleaseReadinessEvidenceReference = 18,
    DeploymentReadinessEvidenceReference = 19
}

public enum MissingEvidenceRequirementSeverity
{
    Unknown = 0,
    Info = 1,
    Required = 2,
    Blocking = 3,
    Critical = 4
}

public enum ObservedEvidenceKind
{
    Unknown = 0,
    OperationIdentity = 1,
    CorrelationLink = 2,
    TimelineEntry = 3,
    StatusProjectionEvent = 4,
    PatchArtifactMetadata = 5,
    ValidationResultMetadata = 6,
    EvidenceMetadata = 7,
    ReceiptMetadata = 8,
    ApprovalRecordReference = 9,
    PolicySatisfactionRecordReference = 10,
    SourceApplyReceiptReference = 11,
    CommitPackageReceiptReference = 12,
    CommitReceiptReference = 13,
    PushReceiptReference = 14,
    PullRequestReceiptReference = 15,
    RollbackReceiptReference = 16,
    RecoveryReceiptReference = 17,
    ReleaseReadinessEvidenceReference = 18,
    DeploymentReadinessEvidenceReference = 19
}

public sealed record MissingEvidenceRequirement
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string RequirementId { get; init; }
    public required MissingEvidenceRequirementKind RequirementKind { get; init; }
    public required string RequiredLabel { get; init; }
    public required string RequiredFor { get; init; }
    public required MissingEvidenceRequirementSeverity Severity { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ObservedEvidenceReference
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ObservedEvidenceId { get; init; }
    public required ObservedEvidenceKind EvidenceKind { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record MissingEvidenceResolverRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<MissingEvidenceRequirement> Requirements { get; init; }
    public required IReadOnlyList<ObservedEvidenceReference> ObservedEvidence { get; init; }
}

public enum MissingEvidenceResolutionStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoRequirements = 2,
    Complete = 3,
    MissingEvidence = 4,
    AmbiguousEvidence = 5
}

public sealed record MissingEvidenceItem
{
    public required string RequirementId { get; init; }
    public required MissingEvidenceRequirementKind RequirementKind { get; init; }
    public required string RequiredLabel { get; init; }
    public required string RequiredFor { get; init; }
    public required MissingEvidenceRequirementSeverity Severity { get; init; }
    public required string MissingReason { get; init; }
}

public sealed record SatisfiedEvidenceItem
{
    public required string RequirementId { get; init; }
    public required MissingEvidenceRequirementKind RequirementKind { get; init; }
    public required string ObservedEvidenceId { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required bool IsRedacted { get; init; }
}

public sealed record MissingEvidenceResolutionResult
{
    public required bool IsValid { get; init; }
    public required MissingEvidenceResolutionStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<MissingEvidenceItem> MissingEvidence { get; init; }
    public required IReadOnlyList<SatisfiedEvidenceItem> SatisfiedEvidence { get; init; }
    public required IReadOnlyList<string> AmbiguousEvidence { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

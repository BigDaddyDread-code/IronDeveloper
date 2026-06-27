namespace IronDev.Core.Governance;

public enum RoleVisibilityLevel
{
    Unknown = 0,
    NotVisible = 1,
    PresenceOnly = 2,
    ReferenceOnly = 3,
    MetadataOnly = 4,
    SummaryOnly = 5,
    RedactedDetails = 6,
    DetailEligibilityHint = 7
}

public enum RoleVisibilitySurface
{
    Unknown = 0,
    Planning = 1,
    Proposal = 2,
    ApprovalPackage = 3,
    PolicyReview = 4,
    ValidationReview = 5,
    SourceApply = 6,
    Commit = 7,
    Push = 8,
    PullRequest = 9,
    ReadyForReview = 10,
    MergeReadiness = 11,
    ReleaseReadiness = 12,
    DeploymentReadiness = 13,
    Rollback = 14,
    Retry = 15,
    Recovery = 16,
    WorkflowContinuation = 17,
    Memory = 18,
    Audit = 19,
    OperationStatus = 20,
    ReceiptReadModel = 21,
    FrontendReadOnly = 22
}

public enum RoleVisibilityMaterialKind
{
    Unknown = 0,
    PlanSummary = 1,
    ProposalSummary = 2,
    ApprovalPackageSummary = 3,
    PolicyReviewSummary = 4,
    ValidationSummary = 5,
    ValidationEvidenceRefs = 6,
    SourceApplySummary = 7,
    PatchMetadata = 8,
    CommitPackageSummary = 9,
    PushReceiptSummary = 10,
    PullRequestMetadata = 11,
    PullRequestDiffSummary = 12,
    ReadyForReviewSummary = 13,
    MergeReadinessSummary = 14,
    ReleaseReadinessSummary = 15,
    DeploymentReadinessSummary = 16,
    RollbackSummary = 17,
    RetrySummary = 18,
    RecoverySummary = 19,
    WorkflowContinuationSummary = 20,
    OperationStatusSummary = 21,
    ReceiptMetadata = 22,
    AuditTrailSummary = 23,
    MemorySummary = 24,
    SensitiveFindingSummary = 25,
    SecretScanSummary = 26,
    RawPayload = 27,
    CredentialMaterial = 28,
    PrivateReasoning = 29
}

public enum RoleVisibilitySensitivityKind
{
    Unknown = 0,
    Normal = 1,
    Internal = 2,
    Confidential = 3,
    SecuritySensitive = 4,
    SecretLike = 5,
    CredentialLike = 6,
    PrivateReasoning = 7,
    RawPayload = 8
}

public sealed record RoleVisibilityMatrixEntry
{
    public required string RoleId { get; init; }
    public required string CatalogVersion { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required GovernanceRoleScopeKind RoleScopeKind { get; init; }
    public required RoleVisibilitySurface Surface { get; init; }
    public required RoleVisibilityMaterialKind MaterialKind { get; init; }
    public required RoleVisibilitySensitivityKind SensitivityKind { get; init; }
    public required RoleVisibilityLevel VisibilityLevel { get; init; }
    public required bool RequiresRedaction { get; init; }
    public required bool RequiresSeparateRoleAssignment { get; init; }
    public required bool RequiresSeparateVisibilityDecision { get; init; }
    public required bool RequiresSeparatePolicyDecision { get; init; }
    public required string Reason { get; init; }
    public required string BoundaryStatement { get; init; }
}

public sealed record RoleVisibilityMatrix
{
    public required string MatrixId { get; init; }
    public required string CatalogId { get; init; }
    public required string CatalogVersion { get; init; }
    public required IReadOnlyList<RoleVisibilityMatrixEntry> Entries { get; init; }
    public required string CreatedReason { get; init; }
    public required string BoundaryStatement { get; init; }
}

public sealed record RoleVisibilityMatrixValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required IReadOnlyList<string> InvalidRoleIds { get; init; }
    public required IReadOnlyList<string> UnknownRoleIds { get; init; }
    public required IReadOnlyList<string> DuplicateMatrixKeys { get; init; }
    public required IReadOnlyList<string> UnsafeEntryRefs { get; init; }
    public required IReadOnlyList<string> OverexposedMaterialRefs { get; init; }
}

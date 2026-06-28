namespace IronDev.Core.Governance;

public enum ExternalViewerRedactionMaterialKind
{
    Unknown = 0,
    PublicMetadata = 1,
    TenantScopedMetadata = 2,
    ProjectScopedMetadata = 3,
    OperationStatusMetadata = 4,
    RedactedOperationStatusSummary = 5,
    RedactedValidationSummary = 6,
    RedactedReviewSummary = 7,
    RedactedApprovalSummary = 8,
    RedactedDiagnosticSummary = 9,
    RedactedAuditSummary = 10,
    RedactedReleaseReadinessSummary = 11,
    RedactedPolicySummary = 12,
    RedactedErrorSummary = 13,
    RedactedLogSummary = 14,
    RedactedReceiptSummary = 15,
    RawPayload = 16,
    RawProviderResponse = 17,
    RawSource = 18,
    RawDiff = 19,
    RawPatch = 20,
    RawLog = 21,
    CredentialMaterial = 22,
    SecretMaterial = 23,
    PrivateReasoning = 24,
    AuthorityMarker = 25,
    ApprovalRecord = 26,
    PolicySatisfactionRecord = 27,
    SourcePatch = 28,
    CommitPackage = 29,
    PushReceipt = 30,
    PullRequestMutationReceipt = 31,
    ReleaseOrDeployReceipt = 32
}

public enum ExternalViewerRedactionRequestedIntent
{
    Unknown = 0,
    ReadOnlyInspect = 1,
    ReadOnlySummarise = 2,
    GrantExternalAccess = 3,
    CreateShareLink = 4,
    ExportRawData = 5,
    ViewRawPayload = 6,
    ViewSecrets = 7,
    ViewCredentials = 8,
    ViewPrivateReasoning = 9,
    BypassRedaction = 10,
    CrossTenantVisibility = 11,
    PlatformVisibility = 12,
    ApprovalAuthority = 13,
    PolicySatisfaction = 14,
    ValidationRefresh = 15,
    SourceSafetyProof = 16,
    DiagnosticExecution = 17,
    RetryAuthority = 18,
    RollbackAuthority = 19,
    RecoveryAuthority = 20,
    MutationAuthority = 21,
    WorkflowContinuation = 22,
    MergeAuthority = 23,
    ReleaseAuthority = 24,
    DeploymentAuthority = 25
}

public enum ExternalViewerRedactionClassification
{
    Invalid = 0,
    Hidden = 1,
    MetadataOnlyCandidate = 2,
    RedactedSummaryCandidate = 3,
    BlockedByNonExternalViewerRole = 4,
    BlockedByActionIntent = 5,
    BlockedByAccessIntent = 6,
    BlockedByRawMaterial = 7,
    BlockedBySecretMaterial = 8,
    BlockedByCredentialMaterial = 9,
    BlockedByPrivateReasoningMaterial = 10,
    BlockedByAuthorityMarker = 11,
    BlockedByMissingCatalogEvidence = 12,
    BlockedByMissingMatrixEvidence = 13,
    BlockedByMissingSourceEvidence = 14,
    BlockedByMissingRedactionEvidence = 15,
    BlockedByMissingTenantBoundaryEvidence = 16,
    BlockedByPolicyMaterial = 17,
    BlockedByApprovalMaterial = 18,
    BlockedByMutationMaterial = 19,
    BlockedByReleaseDeployMaterial = 20,
    BlockedByUnknownMaterial = 21,
    BlockedByUnknownIntent = 22
}

public sealed record ExternalViewerRedactionRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedRoleKey { get; init; }
    public required RoleVisibilitySurface RequestedSurface { get; init; }
    public required ExternalViewerRedactionMaterialKind RequestedMaterialKind { get; init; }
    public required ExternalViewerRedactionRequestedIntent RequestedIntent { get; init; }
    public required string SourceEvidenceRef { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string VisibilityMatrixEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
    public string? OptionalTenantBoundaryEvidenceRef { get; init; }
}

public sealed record ExternalViewerRedactionDecision
{
    public required ExternalViewerRedactionClassification Classification { get; init; }
    public required RoleVisibilityLevel EffectiveCandidateVisibility { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required string RecordFingerprint { get; init; }
    public required bool GrantsExternalViewerAuthority { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
    public required bool CreatesShareLink { get; init; }
    public required bool ExportsRawData { get; init; }
    public required bool GrantsCrossTenantVisibility { get; init; }
    public required bool GrantsPlatformVisibility { get; init; }
    public required bool GrantsApprovalAuthority { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool RefreshesValidation { get; init; }
    public required bool ProvesSourceSafety { get; init; }
    public required bool GrantsDiagnosticExecutionAuthority { get; init; }
    public required bool GrantsRetryAuthority { get; init; }
    public required bool GrantsRollbackAuthority { get; init; }
    public required bool GrantsRecoveryAuthority { get; init; }
    public required bool GrantsMutationAuthority { get; init; }
    public required bool GrantsWorkflowContinuation { get; init; }
    public required bool GrantsMergeAuthority { get; init; }
    public required bool GrantsReleaseAuthority { get; init; }
    public required bool GrantsDeploymentAuthority { get; init; }
    public required bool BypassesRedaction { get; init; }
    public required bool DisclosesSecrets { get; init; }
    public required bool DisclosesCredentials { get; init; }
    public required bool DisclosesRawPayload { get; init; }
    public required bool DisclosesRawSource { get; init; }
    public required bool DisclosesRawLogs { get; init; }
    public required bool DisclosesPrivateReasoning { get; init; }
}

public sealed record ExternalViewerRedactionValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

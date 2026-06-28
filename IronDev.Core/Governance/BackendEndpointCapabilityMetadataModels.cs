namespace IronDev.Core.Governance;

public enum BackendEndpointHttpMethodKind
{
    Unknown = 0,
    Get = 1,
    Head = 2,
    Options = 3,
    Post = 4,
    Put = 5,
    Patch = 6,
    Delete = 7
}

public enum BackendEndpointCapabilityKind
{
    Unknown = 0,
    ReadOnlyMetadata = 1,
    ReadOnlySummary = 2,
    RedactedSummary = 3,
    StatusReadModel = 4,
    ReceiptReadModel = 5,
    ProposalReadModel = 6,
    ApprovalPackageReadModel = 7,
    PolicyReviewReadModel = 8,
    AuditReadModel = 9,
    ValidationReviewReadModel = 10,
    OperationDiagnosticReadModel = 11,
    ReleaseReadinessReadModel = 12,
    MutationEndpoint = 13,
    ExecutionEndpoint = 14,
    AdminEndpoint = 15,
    RawExportEndpoint = 16,
    ExternalShareEndpoint = 17
}

public enum BackendEndpointSensitivityKind
{
    Unknown = 0,
    PublicMetadata = 1,
    InternalMetadata = 2,
    TenantScopedMetadata = 3,
    ProjectScopedMetadata = 4,
    OperationScopedMetadata = 5,
    RedactedSummary = 6,
    SensitiveSummary = 7,
    RawPayload = 8,
    CredentialMaterial = 9,
    SecretMaterial = 10,
    PrivateReasoning = 11,
    MutationMaterial = 12,
    ReleaseDeployMaterial = 13
}

public enum BackendEndpointCapabilityIntent
{
    Unknown = 0,
    InspectMetadata = 1,
    ListMetadata = 2,
    SummariseMetadata = 3,
    AuthorizeRouteAccess = 4,
    InvokeEndpoint = 5,
    CreateRouteGuard = 6,
    GrantRoleAccess = 7,
    GrantExternalAccess = 8,
    SatisfyPolicy = 9,
    AcceptApproval = 10,
    RefreshValidation = 11,
    ProveSourceSafety = 12,
    ExecuteDiagnostic = 13,
    ExecuteRetry = 14,
    ExecuteRollback = 15,
    ExecuteRecovery = 16,
    MutateSource = 17,
    ContinueWorkflow = 18,
    Merge = 19,
    Release = 20,
    Deploy = 21,
    BypassRedaction = 22,
    DiscloseSecrets = 23,
    DiscloseRawPayload = 24,
    DisclosePrivateReasoning = 25
}

public enum BackendEndpointCapabilityClassification
{
    Invalid = 0,
    MetadataOnlyCandidate = 1,
    SummaryCandidate = 2,
    RedactedSummaryCandidate = 3,
    BlockedByUnknownEndpoint = 4,
    BlockedByUnknownIntent = 5,
    BlockedByActionIntent = 6,
    BlockedByInvocationIntent = 7,
    BlockedByAccessIntent = 8,
    BlockedByPolicyIntent = 9,
    BlockedByApprovalIntent = 10,
    BlockedByMutationIntent = 11,
    BlockedByWorkflowIntent = 12,
    BlockedByReleaseDeployIntent = 13,
    BlockedByRedactionBypassIntent = 14,
    BlockedBySecretDisclosureIntent = 15,
    BlockedByRawDisclosureIntent = 16,
    BlockedByPrivateReasoningDisclosureIntent = 17,
    BlockedByMissingEndpointMetadataEvidence = 18,
    BlockedByMissingCatalogEvidence = 19,
    BlockedByMissingMatrixEvidence = 20,
    BlockedByMissingPolicyEvidence = 21,
    BlockedByMissingRedactionEvidence = 22,
    BlockedByMissingTenantBoundaryEvidence = 23,
    BlockedBySensitiveCapability = 24,
    BlockedByRawCapability = 25,
    BlockedBySecretCapability = 26,
    BlockedByPrivateReasoningCapability = 27
}

public sealed record BackendEndpointCapabilityMetadataEntry
{
    public required string EndpointKey { get; init; }
    public required string DisplayName { get; init; }
    public required string RouteTemplate { get; init; }
    public required BackendEndpointHttpMethodKind HttpMethod { get; init; }
    public required BackendEndpointCapabilityKind CapabilityKind { get; init; }
    public required RoleVisibilitySurface VisibilitySurface { get; init; }
    public required RoleVisibilityMaterialKind VisibilityMaterialKind { get; init; }
    public required BackendEndpointSensitivityKind SensitivityKind { get; init; }
    public required string OwningSubsystem { get; init; }
    public required string BoundaryStatement { get; init; }
    public required IReadOnlyList<string> RequiredEvidenceRefs { get; init; }
    public required bool RequiresSeparateRoleAssignment { get; init; }
    public required bool RequiresSeparateVisibilityDecision { get; init; }
    public required bool RequiresSeparateAccessDecision { get; init; }
    public required bool RequiresSeparatePolicyDecision { get; init; }
    public required bool RequiresSeparateRedactionDecision { get; init; }
    public required bool RequiresTenantBoundaryDecision { get; init; }
    public required bool RequiresSeparateApprovalDecision { get; init; }
    public required bool RequiresSeparateExecutionAuthority { get; init; }
    public required bool RequiresSeparateMutationAuthority { get; init; }
    public required bool RequiresSeparateWorkflowAuthority { get; init; }
}

public sealed record BackendEndpointCapabilityMetadataCatalog
{
    public required string CatalogId { get; init; }
    public required string CatalogVersion { get; init; }
    public required IReadOnlyList<BackendEndpointCapabilityMetadataEntry> Entries { get; init; }
    public required string BoundaryStatement { get; init; }
}

public sealed record BackendEndpointCapabilityMetadataRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedEndpointKey { get; init; }
    public required BackendEndpointCapabilityIntent RequestedIntent { get; init; }
    public required string EndpointMetadataEvidenceRef { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string VisibilityMatrixEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
    public string? OptionalTenantBoundaryEvidenceRef { get; init; }
}

public sealed record BackendEndpointCapabilityDecision
{
    public required BackendEndpointCapabilityClassification Classification { get; init; }
    public required RoleVisibilityLevel EffectiveCandidateVisibility { get; init; }
    public required string EndpointKey { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required string RecordFingerprint { get; init; }
    public required bool GrantsEndpointAuthority { get; init; }
    public required bool GrantsRouteAccess { get; init; }
    public required bool AllowsInvocation { get; init; }
    public required bool CreatesRouteGuard { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
    public required bool GrantsExternalAccess { get; init; }
    public required bool AcceptsApproval { get; init; }
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
    public required bool DisclosesPrivateReasoning { get; init; }
}

public sealed record BackendEndpointCapabilityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

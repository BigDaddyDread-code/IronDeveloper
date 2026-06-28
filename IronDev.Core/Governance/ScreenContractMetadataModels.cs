namespace IronDev.Core.Governance;

public enum ScreenContractKind
{
    Unknown = 0,
    MetadataCatalog = 1,
    StatusViewer = 2,
    ReceiptViewer = 3,
    ProposalViewer = 4,
    ApprovalPackageViewer = 5,
    PolicyReviewViewer = 6,
    AuditViewer = 7,
    ValidationReviewViewer = 8,
    DiagnosticViewer = 9,
    ReleaseReadinessViewer = 10,
    ExternalRedactedViewer = 11,
    ActionRequestViewer = 12,
    AdminViewer = 13,
    MutationViewer = 14,
    ReleaseDeployViewer = 15
}

public enum ScreenContractSensitivityKind
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

public sealed record ScreenContractMetadataEntry
{
    public required string ScreenKey { get; init; }
    public required string DisplayName { get; init; }
    public required string FrontendRoutePattern { get; init; }
    public required string OwningSubsystem { get; init; }
    public required ScreenContractKind ScreenKind { get; init; }
    public required RoleVisibilitySurface VisibilitySurface { get; init; }
    public required RoleVisibilityMaterialKind VisibilityMaterialKind { get; init; }
    public required ScreenContractSensitivityKind SensitivityKind { get; init; }
    public required string PrimaryEndpointKey { get; init; }
    public required IReadOnlyList<string> RelatedEndpointKeys { get; init; }
    public required IReadOnlyList<string> RequiredEvidenceRefs { get; init; }
    public required string BoundaryStatement { get; init; }
    public required bool IsReadOnly { get; init; }
    public required bool IsActionScreen { get; init; }
    public required bool IsMutationScreen { get; init; }
    public required bool IsAdminScreen { get; init; }
    public required bool IsReleaseDeployScreen { get; init; }
    public required bool AllowsLocalAuthorityState { get; init; }
    public required bool AllowsClientSidePermissionDecision { get; init; }
    public required bool AllowsActionInvocation { get; init; }
    public required bool AllowsMutation { get; init; }
    public required bool AllowsWorkflowContinuation { get; init; }
    public required bool AllowsApproval { get; init; }
    public required bool AllowsPolicySatisfaction { get; init; }
    public required bool AllowsRedactionBypass { get; init; }
    public required bool AllowsRawPayloadDisplay { get; init; }
    public required bool AllowsSecretDisplay { get; init; }
    public required bool AllowsPrivateReasoningDisplay { get; init; }
    public required bool RequiresSeparateRoleAssignment { get; init; }
    public required bool RequiresSeparateVisibilityDecision { get; init; }
    public required bool RequiresSeparateAccessDecision { get; init; }
    public required bool RequiresSeparatePolicyDecision { get; init; }
    public required bool RequiresSeparateRedactionDecision { get; init; }
    public required bool RequiresTenantBoundaryDecision { get; init; }
    public required bool RequiresSeparateActionAuthority { get; init; }
    public required bool RequiresSeparateMutationAuthority { get; init; }
    public required bool RequiresSeparateWorkflowAuthority { get; init; }
}

public sealed record ScreenContractMetadataCatalog
{
    public required string CatalogId { get; init; }
    public required string CatalogVersion { get; init; }
    public required string BoundaryStatement { get; init; }
    public required IReadOnlyList<ScreenContractMetadataEntry> Entries { get; init; }
}

public sealed record ScreenContractMetadataResponse
{
    public required string CatalogId { get; init; }
    public required string CatalogVersion { get; init; }
    public required string BoundaryStatement { get; init; }
    public required IReadOnlyList<ScreenContractMetadataEntry> Entries { get; init; }
}

public sealed record ScreenContractMetadataValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

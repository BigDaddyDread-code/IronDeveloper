namespace IronDev.Core.Governance;

public enum RoleForbiddenActionKind
{
    Unknown = 0,
    RoleAssignment = 1,
    RoleGrant = 2,
    RoleRevoke = 3,
    PermissionManagement = 4,
    AccessGrant = 5,
    VisibilityGrant = 6,
    ExternalAccessGrant = 7,
    ShareLinkCreation = 8,
    RawExport = 9,
    CrossTenantVisibility = 10,
    PlatformVisibility = 11,
    Impersonation = 12,
    ApprovalAcceptance = 13,
    PolicySatisfaction = 14,
    ValidationRefresh = 15,
    SourceSafetyProof = 16,
    DiagnosticExecution = 17,
    RetryExecution = 18,
    RollbackExecution = 19,
    RecoveryExecution = 20,
    SourceMutation = 21,
    PatchApply = 22,
    CommitCreation = 23,
    PushExecution = 24,
    PullRequestCreation = 25,
    ReadyForReview = 26,
    WorkflowContinuation = 27,
    Merge = 28,
    Release = 29,
    Deployment = 30,
    RedactionBypass = 31,
    SecretDisclosure = 32,
    CredentialDisclosure = 33,
    RawPayloadDisclosure = 34,
    RawProviderResponseDisclosure = 35,
    RawSourceDisclosure = 36,
    RawLogDisclosure = 37,
    PrivateReasoningDisclosure = 38,
    EndpointInvocation = 39,
    RouteAccess = 40,
    RouteGuardCreation = 41,
    ScreenAccess = 42,
    UiAuthority = 43,
    ClientSidePermissionDecision = 44,
    LocalAuthorityState = 45
}

public enum ForbiddenActionReasonKind
{
    Unknown = 0,
    RoleEvidenceCannotGrantAuthority = 1,
    RequiresSeparateRoleAssignment = 2,
    RequiresSeparateVisibilityDecision = 3,
    RequiresSeparateAccessDecision = 4,
    RequiresSeparateApprovalDecision = 5,
    RequiresSeparatePolicyDecision = 6,
    RequiresSeparateValidationEvidence = 7,
    RequiresSeparateSourceSafetyEvidence = 8,
    RequiresSeparateExecutionAuthority = 9,
    RequiresSeparateMutationAuthority = 10,
    RequiresSeparateWorkflowAuthority = 11,
    RequiresSeparateReleaseAuthority = 12,
    RequiresSeparateDeploymentAuthority = 13,
    RequiresSeparateRedactionDecision = 14,
    RequiresSeparateTenantBoundaryDecision = 15,
    SensitiveMaterialNeverFromRoleEvidence = 16,
    RawMaterialNeverFromRoleEvidence = 17,
    PrivateReasoningNeverFromRoleEvidence = 18
}

public enum ForbiddenActionAuthoritySourceKind
{
    Unknown = 0,
    RoleEvidence = 1,
    RoleCatalogMetadata = 2,
    VisibilityMatrixMetadata = 3,
    ScreenContractMetadata = 4,
    EndpointCapabilityMetadata = 5,
    ApprovalEvidence = 6,
    PolicyEvidence = 7,
    ExecutionAuthority = 8,
    MutationAuthority = 9,
    WorkflowAuthority = 10,
    ReleaseAuthority = 11,
    DeploymentAuthority = 12
}

public enum ForbiddenActionLookupClassification
{
    Invalid = 0,
    Forbidden = 1,
    NoCatalogGrantSeparateAuthorityRequired = 2,
    BlockedByUnknownRole = 3,
    BlockedByUnknownAction = 4,
    BlockedByUnknownAuthoritySource = 5,
    BlockedByUnsafeEvidence = 6,
    BlockedByMissingRoleCatalogEvidence = 7,
    BlockedByMissingForbiddenCatalogEvidence = 8
}

public sealed record ForbiddenActionCatalogEntry
{
    public required string RoleId { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required string RoleDisplayName { get; init; }
    public required RoleForbiddenActionKind RoleForbiddenActionKind { get; init; }
    public required ForbiddenActionReasonKind ReasonKind { get; init; }
    public required string BoundaryStatement { get; init; }
    public required IReadOnlyList<string> RequiredSeparateEvidenceRefs { get; init; }
    public required bool AppliesWhenAuthoritySourceIsRoleEvidence { get; init; }
    public required bool IsForbidden { get; init; }
    public required bool IsAllowed { get; init; }
    public required bool GrantsAuthority { get; init; }
    public required bool GrantsPermission { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool AllowsExecution { get; init; }
    public required bool AllowsMutation { get; init; }
    public required bool AllowsWorkflowContinuation { get; init; }
    public required bool AllowsRelease { get; init; }
    public required bool AllowsDeployment { get; init; }
    public required bool BypassesRedaction { get; init; }
    public required bool DisclosesSecrets { get; init; }
    public required bool DisclosesCredentials { get; init; }
    public required bool DisclosesRawPayload { get; init; }
    public required bool DisclosesPrivateReasoning { get; init; }
}

public sealed record ForbiddenActionCatalog
{
    public required string CatalogId { get; init; }
    public required string CatalogVersion { get; init; }
    public required string BoundaryStatement { get; init; }
    public required IReadOnlyList<ForbiddenActionCatalogEntry> Entries { get; init; }
}

public sealed record ForbiddenActionLookupRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedRoleId { get; init; }
    public required RoleForbiddenActionKind RequestedActionKind { get; init; }
    public required ForbiddenActionAuthoritySourceKind AuthoritySourceKind { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string ForbiddenActionCatalogEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalApprovalEvidenceRef { get; init; }
    public string? OptionalExecutionAuthorityRef { get; init; }
    public string? OptionalMutationAuthorityRef { get; init; }
    public string? OptionalWorkflowAuthorityRef { get; init; }
    public string? OptionalReleaseAuthorityRef { get; init; }
    public string? OptionalRedactionDecisionRef { get; init; }
}

public sealed record ForbiddenActionLookupDecision
{
    public required ForbiddenActionLookupClassification Classification { get; init; }
    public required string RoleId { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required RoleForbiddenActionKind ActionKind { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required string RecordFingerprint { get; init; }
    public required bool IsAllowed { get; init; }
    public required bool GrantsAuthority { get; init; }
    public required bool GrantsPermission { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool AllowsExecution { get; init; }
    public required bool AllowsMutation { get; init; }
    public required bool AllowsWorkflowContinuation { get; init; }
    public required bool AllowsMerge { get; init; }
    public required bool AllowsRelease { get; init; }
    public required bool AllowsDeployment { get; init; }
    public required bool BypassesRedaction { get; init; }
    public required bool DisclosesSecrets { get; init; }
    public required bool DisclosesCredentials { get; init; }
    public required bool DisclosesRawPayload { get; init; }
    public required bool DisclosesPrivateReasoning { get; init; }
    public required bool RequiresSeparateAuthority { get; init; }
}

public sealed record ForbiddenActionCatalogValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

namespace IronDev.Core.Governance;

public enum RolePermissionAuditEventKind
{
    Unknown = 0,
    RoleAssignmentRequested = 1,
    RoleAssignmentProposed = 2,
    RoleAssignmentAttemptBlocked = 3,
    RoleAssignmentRejected = 4,
    RoleGrantRequested = 5,
    RoleGrantProposed = 6,
    RoleGrantAttemptBlocked = 7,
    RoleGrantRejected = 8,
    RoleRevokeRequested = 9,
    RoleRevokeProposed = 10,
    RoleRevokeAttemptBlocked = 11,
    RoleRevokeRejected = 12,
    PermissionGrantRequested = 13,
    PermissionGrantProposed = 14,
    PermissionGrantAttemptBlocked = 15,
    PermissionGrantRejected = 16,
    PermissionRevokeRequested = 17,
    PermissionRevokeProposed = 18,
    PermissionRevokeAttemptBlocked = 19,
    PermissionRevokeRejected = 20,
    AccessGrantRequested = 21,
    AccessGrantProposed = 22,
    AccessGrantAttemptBlocked = 23,
    AccessGrantRejected = 24,
    VisibilityGrantRequested = 25,
    VisibilityGrantProposed = 26,
    VisibilityGrantAttemptBlocked = 27,
    VisibilityGrantRejected = 28,
    ExternalAccessRequested = 29,
    ExternalAccessAttemptBlocked = 30,
    ExternalAccessRejected = 31,
    TenantBoundaryOverrideRequested = 32,
    TenantBoundaryOverrideAttemptBlocked = 33,
    TenantBoundaryOverrideRejected = 34,
    PlatformPermissionRequested = 35,
    PlatformPermissionAttemptBlocked = 36,
    PlatformPermissionRejected = 37,
    AuditOnlyObservation = 38,
    InvalidAuditInput = 39
}

public enum RolePermissionAuditSubjectKind
{
    Unknown = 0,
    Role = 1,
    Permission = 2,
    Access = 3,
    Visibility = 4,
    ExternalAccess = 5,
    TenantBoundary = 6,
    PlatformPermission = 7,
    SystemPermission = 8
}

public enum RolePermissionAuditOutcomeKind
{
    Unknown = 0,
    AuditOnly = 1,
    Requested = 2,
    Proposed = 3,
    Blocked = 4,
    Rejected = 5,
    Invalid = 6,
    Superseded = 7
}

public enum RolePermissionAuditAuthoritySourceKind
{
    Unknown = 0,
    RoleEvidence = 1,
    RoleCatalogMetadata = 2,
    ForbiddenActionCatalogMetadata = 3,
    MissingEvidenceVisibilityMetadata = 4,
    EndpointCapabilityMetadata = 5,
    ScreenContractMetadata = 6,
    ApprovalEvidence = 7,
    PolicyEvidence = 8,
    ExternalSystemObservation = 9,
    HumanSubmittedEvidence = 10
}

public enum RolePermissionAuditClassification
{
    Invalid = 0,
    AuditRecordCandidate = 1,
    BlockedAuditRecordCandidate = 2,
    RejectedAuditRecordCandidate = 3,
    AuditOnlyObservationCandidate = 4,
    BlockedByUnknownEvent = 5,
    BlockedByUnknownSubject = 6,
    BlockedByUnknownOutcome = 7,
    BlockedByUnknownAuthoritySource = 8,
    BlockedByUnsafeText = 9,
    BlockedByMissingRoleCatalogEvidence = 10,
    BlockedByMissingForbiddenActionCatalogEvidence = 11,
    BlockedByMissingSourceEvidence = 12,
    BlockedByForbiddenActionCatalog = 13,
    BlockedByPerformedChangeLanguage = 14,
    BlockedByAuthorityGrantLanguage = 15,
    BlockedByRawMaterial = 16,
    BlockedBySecretMaterial = 17,
    BlockedByCredentialMaterial = 18,
    BlockedByPrivateReasoningMaterial = 19
}

public sealed record RolePermissionAuditRequest
{
    public required string CorrelationId { get; init; }
    public required RolePermissionAuditEventKind RequestedEventKind { get; init; }
    public required RolePermissionAuditSubjectKind RequestedSubjectKind { get; init; }
    public required RolePermissionAuditOutcomeKind RequestedOutcomeKind { get; init; }
    public required RolePermissionAuditAuthoritySourceKind RequestedAuthoritySourceKind { get; init; }
    public required string RequestedRoleId { get; init; }
    public string? RequestedTargetRoleId { get; init; }
    public string? RequestedPermissionKey { get; init; }
    public required string RequestedActorRef { get; init; }
    public required string RequestedTenantRef { get; init; }
    public required string RequestedProjectRef { get; init; }
    public required string RequestedOperationRef { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string ForbiddenActionCatalogEvidenceRef { get; init; }
    public string? MissingEvidenceVisibilityEvidenceRef { get; init; }
    public required string SourceEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalApprovalEvidenceRef { get; init; }
    public string? OptionalTenantBoundaryEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
    public string? PreviousAuditRecordFingerprint { get; init; }
}

public sealed record RolePermissionAuditRecord
{
    public required string AuditRecordId { get; init; }
    public required string CorrelationId { get; init; }
    public required RolePermissionAuditEventKind EventKind { get; init; }
    public required RolePermissionAuditSubjectKind SubjectKind { get; init; }
    public required RolePermissionAuditOutcomeKind OutcomeKind { get; init; }
    public required RolePermissionAuditAuthoritySourceKind AuthoritySourceKind { get; init; }
    public required string RoleId { get; init; }
    public required string TargetRoleId { get; init; }
    public required string PermissionKey { get; init; }
    public required string ActorRef { get; init; }
    public required string TenantRef { get; init; }
    public required string ProjectRef { get; init; }
    public required string OperationRef { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required string PreviousAuditRecordFingerprint { get; init; }
    public required string RecordFingerprint { get; init; }
    public required string BoundaryStatement { get; init; }
    public required bool IsAuditOnly { get; init; }
    public required bool IsImmutableRecord { get; init; }
    public required bool IsAppendOnlyContract { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool GrantsPermissionAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsExternalAccess { get; init; }
    public required bool GrantsTenantBoundaryOverride { get; init; }
    public required bool GrantsPlatformAuthority { get; init; }
    public required bool AcceptsApproval { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool RefreshesValidation { get; init; }
    public required bool ProvesSourceSafety { get; init; }
    public required bool CreatesEvidence { get; init; }
    public required bool SatisfiesEvidence { get; init; }
    public required bool OverridesMissingEvidence { get; init; }
    public required bool WaivesEvidenceRequirement { get; init; }
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

public sealed record RolePermissionAuditDecision
{
    public required RolePermissionAuditClassification Classification { get; init; }
    public required RolePermissionAuditRecord Record { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required string RecordFingerprint { get; init; }
    public required bool IsRecordedAuthority { get; init; }
    public required bool IsAppliedChange { get; init; }
    public required bool IsAuthorizationDecision { get; init; }
    public required bool IsPermissionDecision { get; init; }
    public required bool RequiresSeparateAuthority { get; init; }
}

public sealed record RolePermissionAuditValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

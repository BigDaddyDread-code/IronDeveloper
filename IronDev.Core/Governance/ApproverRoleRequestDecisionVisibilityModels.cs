namespace IronDev.Core.Governance;

public enum ApproverRoleRequestDecisionMaterialKind
{
    Unknown = 0,
    ApproverRoleRequestMetadata = 1,
    ApproverRoleRequestSummary = 2,
    RedactedApproverRoleRequestRationaleSummary = 3,
    ApproverRoleDecisionMetadata = 4,
    ApproverRoleDecisionSummary = 5,
    ApproverRoleDecisionOutcomeSummary = 6,
    RedactedApproverRoleDecisionRationaleSummary = 7,
    ApprovalPackageReferenceSummary = 8,
    RawPayload = 9,
    CredentialMaterial = 10,
    PrivateReasoning = 11,
    AuthorityMarker = 12
}

public enum ApproverRoleRequestDecisionRequestedIntent
{
    Unknown = 0,
    ReadOnlyInspect = 1,
    ReadOnlySummarise = 2,
    CreateApproverRequest = 3,
    GrantApproverRole = 4,
    AssignApproverRole = 5,
    ApprovalAuthority = 6,
    ApprovalAcceptance = 7,
    PolicySatisfaction = 8,
    MutationAuthority = 9,
    WorkflowContinuation = 10,
    MergeAuthority = 11,
    ReleaseAuthority = 12,
    DeploymentAuthority = 13,
    VisibilityGrant = 14,
    RedactionBypass = 15,
    PrivateReasoningDisclosure = 16
}

public enum ApproverRoleRequestDecisionVisibilityClassification
{
    Invalid = 0,
    Hidden = 1,
    MetadataOnlyCandidate = 2,
    SummaryCandidate = 3,
    RedactedSummaryCandidate = 4,
    BlockedByNonApproverRole = 5,
    BlockedByActionIntent = 6,
    BlockedByApprovalIntent = 7,
    BlockedByMissingCatalogEvidence = 8,
    BlockedByMissingMatrixEvidence = 9,
    BlockedByMissingRequestDecisionEvidence = 10,
    BlockedByMissingRedactionEvidence = 11,
    BlockedBySensitiveMaterial = 12,
    BlockedByPrivateReasoningMaterial = 13,
    BlockedByCredentialMaterial = 14,
    BlockedByAuthorityMarker = 15,
    BlockedByUnknownMaterial = 16,
    BlockedByUnknownIntent = 17
}

public sealed record ApproverRoleRequestDecisionVisibilityRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedRoleKey { get; init; }
    public required RoleVisibilitySurface RequestedSurface { get; init; }
    public required ApproverRoleRequestDecisionMaterialKind RequestedMaterialKind { get; init; }
    public required ApproverRoleRequestDecisionRequestedIntent RequestedIntent { get; init; }
    public required string ApproverRequestDecisionEvidenceRef { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string VisibilityMatrixEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
}

public sealed record ApproverRoleRequestDecisionVisibilityDecision
{
    public required ApproverRoleRequestDecisionVisibilityClassification Classification { get; init; }
    public required RoleVisibilityLevel EffectiveCandidateVisibility { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required bool GrantsApproverAuthority { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool CreatesApproverRequest { get; init; }
    public required bool AcceptsApproverRequest { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
    public required bool GrantsApprovalAuthority { get; init; }
    public required bool AcceptsApproval { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool GrantsMutationAuthority { get; init; }
    public required bool GrantsWorkflowContinuation { get; init; }
    public required bool GrantsMergeAuthority { get; init; }
    public required bool GrantsReleaseAuthority { get; init; }
    public required bool GrantsDeploymentAuthority { get; init; }
    public required bool BypassesRedaction { get; init; }
    public required bool DisclosesPrivateReasoning { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record ApproverRoleRequestDecisionVisibilityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

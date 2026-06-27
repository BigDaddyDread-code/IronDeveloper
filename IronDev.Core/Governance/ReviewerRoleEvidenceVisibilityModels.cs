namespace IronDev.Core.Governance;

public enum ReviewerRoleEvidenceMaterialKind
{
    Unknown = 0,
    ReviewerClaimMetadata = 1,
    ReviewerAssignmentClaimSummary = 2,
    ReviewRequestSummary = 3,
    ReviewParticipationSummary = 4,
    ReviewCommentSummary = 5,
    ReviewOutcomeSummary = 6,
    RedactedReviewRationaleSummary = 7,
    RawPayload = 8,
    CredentialMaterial = 9,
    PrivateReasoning = 10,
    AuthorityMarker = 11
}

public enum ReviewerRoleEvidenceRequestedIntent
{
    Unknown = 0,
    ReadOnlyInspect = 1,
    ReadOnlySummarise = 2,
    ActionAuthority = 3,
    ApprovalAuthority = 4,
    PolicySatisfaction = 5,
    MutationAuthority = 6,
    WorkflowContinuation = 7,
    VisibilityGrant = 8,
    RedactionBypass = 9,
    PrivateReasoningDisclosure = 10
}

public enum ReviewerRoleEvidenceVisibilityClassification
{
    Invalid = 0,
    Hidden = 1,
    MetadataOnlyCandidate = 2,
    RedactedSummaryCandidate = 3,
    SummaryCandidate = 4,
    BlockedByNonReviewerRole = 5,
    BlockedByActionIntent = 6,
    BlockedByMissingCatalogEvidence = 7,
    BlockedByMissingMatrixEvidence = 8,
    BlockedByMissingReviewerEvidence = 9,
    BlockedBySensitiveMaterial = 10,
    BlockedByPrivateReasoningMaterial = 11,
    BlockedByCredentialMaterial = 12,
    BlockedByAuthorityMarker = 13,
    BlockedByUnknownMaterial = 14,
    BlockedByUnknownIntent = 15
}

public sealed record ReviewerRoleEvidenceVisibilityRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedRoleKey { get; init; }
    public required RoleVisibilitySurface RequestedSurface { get; init; }
    public required ReviewerRoleEvidenceMaterialKind RequestedMaterialKind { get; init; }
    public required ReviewerRoleEvidenceRequestedIntent RequestedIntent { get; init; }
    public required string ReviewerEvidenceRef { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string VisibilityMatrixEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
}

public sealed record ReviewerRoleEvidenceVisibilityDecision
{
    public required ReviewerRoleEvidenceVisibilityClassification Classification { get; init; }
    public required RoleVisibilityLevel EffectiveCandidateVisibility { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required bool GrantsReviewerAuthority { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
    public required bool GrantsApprovalAuthority { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool GrantsMutationAuthority { get; init; }
    public required bool GrantsWorkflowContinuation { get; init; }
    public required bool BypassesRedaction { get; init; }
    public required bool DisclosesPrivateReasoning { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record ReviewerRoleEvidenceVisibilityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

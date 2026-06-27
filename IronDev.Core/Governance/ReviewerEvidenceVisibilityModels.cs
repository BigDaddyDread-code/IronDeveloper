namespace IronDev.Core.Governance;

public enum ReviewerEvidenceVisibilityIntentKind
{
    Unknown = 0,
    ReadEvidenceSummary = 1,
    ReadEvidenceMetadata = 2,
    ReadEvidenceReference = 3,
    ReadRedactedEvidence = 4,
    ReadReviewContext = 5,
    ActionApprove = 6,
    ActionSatisfyPolicy = 7,
    ActionSourceApply = 8,
    ActionCommit = 9,
    ActionPush = 10,
    ActionPullRequest = 11,
    ActionReadyForReview = 12,
    ActionRequestReviewers = 13,
    ActionMerge = 14,
    ActionRelease = 15,
    ActionDeploy = 16,
    ActionRollback = 17,
    ActionRetry = 18,
    ActionRecover = 19,
    ActionContinueWorkflow = 20,
    ActionBypassRedaction = 21,
    ActionDiscloseRawPayload = 22,
    ActionDiscloseCredential = 23,
    ActionDisclosePrivateReasoning = 24
}

public enum ReviewerEvidenceVisibilityDecisionKind
{
    Invalid = 0,
    MayProceedToSeparateEvidenceVisibilityDecision = 1,
    BlockedByInvalidRoleCatalog = 2,
    BlockedByInvalidVisibilityMatrix = 3,
    BlockedByUnknownReviewerRole = 4,
    BlockedByNonReviewerRole = 5,
    BlockedByRoleVisibilityMismatch = 6,
    BlockedByReviewerEvidenceNotAllowed = 7,
    BlockedByMissingEvidence = 8,
    BlockedBySensitiveEvidencePolicyMissing = 9,
    BlockedByRawOrSecretEvidence = 10,
    BlockedByActionAuthorityAttempt = 11,
    BlockedByApprovalIntent = 12,
    BlockedByPolicyIntent = 13,
    BlockedByWorkflowContinuationIntent = 14,
    BlockedByRedactionBypassIntent = 15,
    BlockedBySensitiveDisclosureIntent = 16,
    BlockedByUnsafePayload = 17
}

public enum ReviewerEvidenceVisibilityBlockKind
{
    None = 0,
    InvalidRequest = 1,
    InvalidCatalog = 2,
    InvalidMatrix = 3,
    UnknownReviewerRole = 4,
    NonReviewerRole = 5,
    RoleVisibilityMismatch = 6,
    EvidenceNotAllowed = 7,
    MissingEvidence = 8,
    SensitiveEvidencePolicyMissing = 9,
    RawOrSecretEvidence = 10,
    ActionAuthorityAttempt = 11,
    ApprovalIntent = 12,
    PolicyIntent = 13,
    WorkflowContinuationIntent = 14,
    RedactionBypassIntent = 15,
    SensitiveDisclosureIntent = 16,
    UnsafePayload = 17
}

public sealed record ReviewerEvidenceVisibilityRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReviewerRoleId { get; init; }
    public required GovernanceRoleKind ReviewerRoleKind { get; init; }
    public required GovernanceRoleScopeKind ReviewerRoleScopeKind { get; init; }
    public required RoleVisibilitySurface EvidenceSurface { get; init; }
    public required RoleVisibilityMaterialKind EvidenceMaterialKind { get; init; }
    public required RoleVisibilitySensitivityKind EvidenceSensitivityKind { get; init; }
    public required RoleVisibilityLevel EvidenceVisibilityLevel { get; init; }
    public required ReviewerEvidenceVisibilityIntentKind IntentKind { get; init; }
    public required string EvidenceRef { get; init; }
    public required string EvidenceSubjectRef { get; init; }
    public required string RoleCatalogId { get; init; }
    public required string RoleCatalogVersion { get; init; }
    public required string RoleCatalogEntryRef { get; init; }
    public required string VisibilityMatrixId { get; init; }
    public required string VisibilityMatrixVersion { get; init; }
    public required string VisibilityMatrixEntryRef { get; init; }
    public string? ReviewerAssignmentEvidenceRef { get; init; }
    public string? ReviewerEvidenceRequestRef { get; init; }
    public string? VisibilityDecisionEvidenceRef { get; init; }
    public string? PolicyDecisionEvidenceRef { get; init; }
    public string? RedactionEvidenceRef { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ReviewerEvidenceVisibilityDecision
{
    public required ReviewerEvidenceVisibilityDecisionKind Decision { get; init; }
    public required ReviewerEvidenceVisibilityBlockKind BlockKind { get; init; }
    public required string Reason { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReviewerRoleId { get; init; }
    public required GovernanceRoleKind ReviewerRoleKind { get; init; }
    public required GovernanceRoleScopeKind ReviewerRoleScopeKind { get; init; }
    public required RoleVisibilitySurface EvidenceSurface { get; init; }
    public required RoleVisibilityMaterialKind EvidenceMaterialKind { get; init; }
    public required RoleVisibilitySensitivityKind EvidenceSensitivityKind { get; init; }
    public required RoleVisibilityLevel EvidenceVisibilityLevel { get; init; }
    public required ReviewerEvidenceVisibilityIntentKind IntentKind { get; init; }
    public required string MatchedEvidenceRef { get; init; }
    public required string MatchedEvidenceSubjectRef { get; init; }
    public required string MatchedRoleCatalogId { get; init; }
    public required string MatchedRoleCatalogVersion { get; init; }
    public required string MatchedRoleCatalogEntryRef { get; init; }
    public required string MatchedVisibilityMatrixId { get; init; }
    public required string MatchedVisibilityMatrixVersion { get; init; }
    public required string MatchedVisibilityMatrixEntryRef { get; init; }
    public required string MatchedReviewerAssignmentEvidenceRef { get; init; }
    public required string MatchedReviewerEvidenceRequestRef { get; init; }
    public required string MatchedVisibilityDecisionEvidenceRef { get; init; }
    public required string MatchedPolicyDecisionEvidenceRef { get; init; }
    public required string MatchedRedactionEvidenceRef { get; init; }
    public required bool RequiresSeparateReviewerAssignment { get; init; }
    public required bool RequiresSeparateReviewerEvidenceRequest { get; init; }
    public required bool RequiresSeparateVisibilityDecision { get; init; }
    public required bool RequiresSeparatePolicyDecision { get; init; }
    public required bool RequiresSeparateRedactionEnforcement { get; init; }
    public required bool RequiresSeparateApproval { get; init; }
    public required bool RequiresSeparatePolicySatisfaction { get; init; }
    public required bool RequiresSeparateActionAuthority { get; init; }
    public required bool RequiresSeparateMutationAuthority { get; init; }
    public required bool RequiresSeparateWorkflowAuthority { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record ReviewerEvidenceVisibilityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

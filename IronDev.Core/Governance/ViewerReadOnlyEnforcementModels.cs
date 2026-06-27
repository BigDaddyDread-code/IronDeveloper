namespace IronDev.Core.Governance;

public enum ViewerReadOnlyRoleKind
{
    Unknown = 0,
    Observer = 1,
    Auditor = 2,
    SystemReadOnly = 3,
    AutomationAgentReadOnly = 4,
    ReviewerReadOnly = 5,
    RequesterReadOnly = 6,
    PlannerReadOnly = 7
}

public enum ViewerReadOnlyIntentKind
{
    Unknown = 0,
    ReadStatus = 1,
    ReadReceipt = 2,
    ReadAudit = 3,
    ReadSummary = 4,
    ReadMetadata = 5,
    ReadReference = 6,
    ReadRedactedDetails = 7,
    ReadFrontendView = 8,
    ActionSourceApply = 9,
    ActionCommit = 10,
    ActionPush = 11,
    ActionPullRequest = 12,
    ActionReadyForReview = 13,
    ActionReviewRequest = 14,
    ActionApprove = 15,
    ActionSatisfyPolicy = 16,
    ActionMerge = 17,
    ActionRelease = 18,
    ActionDeploy = 19,
    ActionRollback = 20,
    ActionRetry = 21,
    ActionRecover = 22,
    ActionContinueWorkflow = 23,
    ActionPromoteMemory = 24,
    ActionBypassRedaction = 25,
    ActionDiscloseSecret = 26,
    ActionDiscloseCredential = 27,
    ActionDisclosePrivateReasoning = 28,
    ActionDiscloseRawPayload = 29
}

public enum ViewerReadOnlyDecisionKind
{
    Invalid = 0,
    MayProceedToSeparateVisibilityDecision = 1,
    BlockedByActionIntent = 2,
    BlockedByMutationIntent = 3,
    BlockedByApprovalIntent = 4,
    BlockedByPolicyIntent = 5,
    BlockedByWorkflowContinuationIntent = 6,
    BlockedByMemoryPromotionIntent = 7,
    BlockedByRedactionBypassIntent = 8,
    BlockedBySensitiveDisclosureIntent = 9,
    BlockedByUnknownRole = 10,
    BlockedByUnknownIntent = 11,
    BlockedByInvalidRoleCatalog = 12,
    BlockedByInvalidVisibilityMatrix = 13,
    BlockedByRoleVisibilityMismatch = 14,
    BlockedByMissingEvidence = 15,
    BlockedByUnsafePayload = 16
}

public enum ViewerReadOnlyBlockKind
{
    None = 0,
    InvalidRequest = 1,
    UnknownRole = 2,
    UnknownIntent = 3,
    InvalidCatalog = 4,
    InvalidMatrix = 5,
    RoleVisibilityMismatch = 6,
    MissingEvidence = 7,
    ActionIntent = 8,
    MutationIntent = 9,
    ApprovalIntent = 10,
    PolicyIntent = 11,
    WorkflowContinuationIntent = 12,
    MemoryPromotionIntent = 13,
    RedactionBypassIntent = 14,
    SensitiveDisclosureIntent = 15,
    UnsafePayload = 16
}

public sealed record ViewerReadOnlyEnforcementRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string RoleId { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required GovernanceRoleScopeKind RoleScopeKind { get; init; }
    public required ViewerReadOnlyRoleKind ViewerRoleKind { get; init; }
    public required RoleVisibilitySurface VisibilitySurface { get; init; }
    public required RoleVisibilityMaterialKind VisibilityMaterialKind { get; init; }
    public required RoleVisibilityLevel VisibilityLevel { get; init; }
    public required RoleVisibilitySensitivityKind SensitivityKind { get; init; }
    public required ViewerReadOnlyIntentKind IntentKind { get; init; }
    public required string RequestedSurfaceRef { get; init; }
    public required string RequestedMaterialRef { get; init; }
    public required string RequestedEvidenceRef { get; init; }
    public required string RoleCatalogId { get; init; }
    public required string RoleCatalogVersion { get; init; }
    public required string RoleCatalogEntryRef { get; init; }
    public required string VisibilityMatrixId { get; init; }
    public required string VisibilityMatrixVersion { get; init; }
    public required string VisibilityMatrixEntryRef { get; init; }
    public string? RoleAssignmentEvidenceRef { get; init; }
    public string? VisibilityDecisionEvidenceRef { get; init; }
    public string? PolicyDecisionEvidenceRef { get; init; }
    public string? RedactionEvidenceRef { get; init; }
    public required string ReasonCode { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ViewerReadOnlyEnforcementDecision
{
    public required ViewerReadOnlyDecisionKind Decision { get; init; }
    public required ViewerReadOnlyBlockKind BlockKind { get; init; }
    public required string Reason { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string RoleId { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required GovernanceRoleScopeKind RoleScopeKind { get; init; }
    public required ViewerReadOnlyRoleKind ViewerRoleKind { get; init; }
    public required RoleVisibilitySurface VisibilitySurface { get; init; }
    public required RoleVisibilityMaterialKind VisibilityMaterialKind { get; init; }
    public required RoleVisibilityLevel VisibilityLevel { get; init; }
    public required RoleVisibilitySensitivityKind SensitivityKind { get; init; }
    public required ViewerReadOnlyIntentKind IntentKind { get; init; }
    public required string MatchedRoleCatalogId { get; init; }
    public required string MatchedRoleCatalogVersion { get; init; }
    public required string MatchedRoleCatalogEntryRef { get; init; }
    public required string MatchedVisibilityMatrixId { get; init; }
    public required string MatchedVisibilityMatrixVersion { get; init; }
    public required string MatchedVisibilityMatrixEntryRef { get; init; }
    public required string MatchedRoleAssignmentEvidenceRef { get; init; }
    public required string MatchedVisibilityDecisionEvidenceRef { get; init; }
    public required string MatchedPolicyDecisionEvidenceRef { get; init; }
    public required string MatchedRedactionEvidenceRef { get; init; }
    public required bool RequiresSeparateRoleAssignment { get; init; }
    public required bool RequiresSeparateVisibilityDecision { get; init; }
    public required bool RequiresSeparatePolicyDecision { get; init; }
    public required bool RequiresSeparateRedactionEnforcement { get; init; }
    public required bool RequiresSeparateActionAuthority { get; init; }
    public required bool RequiresSeparateApproval { get; init; }
    public required bool RequiresSeparatePolicySatisfaction { get; init; }
    public required bool RequiresSeparateMutationAuthority { get; init; }
    public required bool RequiresSeparateWorkflowAuthority { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record ViewerReadOnlyRequestValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

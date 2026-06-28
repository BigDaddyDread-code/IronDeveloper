namespace IronDev.Core.Governance;

public enum MissingEvidenceKind
{
    Unknown = 0,
    RoleAssignmentEvidence = 1,
    VisibilityDecisionEvidence = 2,
    AccessDecisionEvidence = 3,
    TenantBoundaryEvidence = 4,
    RedactionDecisionEvidence = 5,
    ApprovalEvidence = 6,
    PolicySatisfactionEvidence = 7,
    ValidationFreshnessEvidence = 8,
    SourceSafetyEvidence = 9,
    DiagnosticExecutionAuthority = 10,
    RetryAuthority = 11,
    RollbackAuthority = 12,
    RecoveryAuthority = 13,
    MutationAuthority = 14,
    PatchApplyAuthority = 15,
    CommitAuthority = 16,
    PushAuthority = 17,
    PullRequestAuthority = 18,
    ReadyForReviewAuthority = 19,
    WorkflowContinuationEvidence = 20,
    MergeAuthority = 21,
    ReleaseAuthority = 22,
    DeploymentAuthority = 23,
    ExternalAccessEvidence = 24,
    ShareLinkAuthority = 25,
    RawExportAuthority = 26,
    ScreenAccessEvidence = 27,
    EndpointAuthorityEvidence = 28,
    RouteAccessEvidence = 29,
    RouteGuardEvidence = 30,
    SecretDisclosureAuthority = 31,
    CredentialDisclosureAuthority = 32,
    RawPayloadDisclosureAuthority = 33,
    RawProviderResponseDisclosureAuthority = 34,
    RawSourceDisclosureAuthority = 35,
    RawLogDisclosureAuthority = 36,
    PrivateReasoningDisclosureAuthority = 37
}

public enum MissingEvidenceMaterialKind
{
    Unknown = 0,
    PresenceOnly = 1,
    CategoryOnly = 2,
    RedactedSummary = 3,
    RequiredEvidenceReference = 4,
    MissingSubjectReference = 5,
    RoleReference = 6,
    TenantReference = 7,
    ProjectReference = 8,
    OperationReference = 9,
    ApprovalReference = 10,
    PolicyReference = 11,
    ValidationReference = 12,
    DiagnosticReference = 13,
    RecoveryReference = 14,
    MutationReference = 15,
    WorkflowReference = 16,
    ReleaseReference = 17,
    DeploymentReference = 18,
    RawPayload = 19,
    RawProviderResponse = 20,
    RawSource = 21,
    RawDiff = 22,
    RawPatch = 23,
    RawLog = 24,
    CredentialMaterial = 25,
    SecretMaterial = 26,
    PrivateReasoning = 27
}

public enum MissingEvidenceVisibilityIntent
{
    Unknown = 0,
    InspectMissingEvidence = 1,
    SummariseMissingEvidence = 2,
    ListMissingEvidence = 3,
    SatisfyMissingEvidence = 4,
    CreateMissingEvidence = 5,
    OverrideMissingEvidence = 6,
    WaiveEvidenceRequirement = 7,
    RefreshValidation = 8,
    ProveSourceSafety = 9,
    AcceptApproval = 10,
    SatisfyPolicy = 11,
    RunDiagnostic = 12,
    Retry = 13,
    Rollback = 14,
    Recover = 15,
    MutateSource = 16,
    ApplyPatch = 17,
    Commit = 18,
    Push = 19,
    CreatePullRequest = 20,
    ReadyForReview = 21,
    ContinueWorkflow = 22,
    Merge = 23,
    Release = 24,
    Deploy = 25,
    BypassRedaction = 26,
    DiscloseSecret = 27,
    DiscloseCredential = 28,
    DiscloseRawPayload = 29,
    DisclosePrivateReasoning = 30
}

public enum MissingEvidenceVisibilityClassification
{
    Invalid = 0,
    Hidden = 1,
    PresenceOnlyCandidate = 2,
    CategoryOnlyCandidate = 3,
    RedactedSummaryCandidate = 4,
    BlockedByUnknownRole = 5,
    BlockedByUnknownMissingEvidenceKind = 6,
    BlockedByUnknownMaterial = 7,
    BlockedByUnknownIntent = 8,
    BlockedByActionIntent = 9,
    BlockedByEvidenceSatisfactionIntent = 10,
    BlockedByApprovalIntent = 11,
    BlockedByPolicyIntent = 12,
    BlockedByValidationIntent = 13,
    BlockedBySourceSafetyIntent = 14,
    BlockedByExecutionIntent = 15,
    BlockedByMutationIntent = 16,
    BlockedByWorkflowIntent = 17,
    BlockedByReleaseDeployIntent = 18,
    BlockedByRedactionBypassIntent = 19,
    BlockedByDisclosureIntent = 20,
    BlockedByRawMaterial = 21,
    BlockedBySecretMaterial = 22,
    BlockedByCredentialMaterial = 23,
    BlockedByPrivateReasoningMaterial = 24,
    BlockedByMissingRoleCatalogEvidence = 25,
    BlockedByMissingVisibilityMatrixEvidence = 26,
    BlockedByMissingForbiddenActionCatalogEvidence = 27,
    BlockedByMissingSourceMissingEvidenceRef = 28,
    BlockedByMissingTenantBoundaryEvidence = 29,
    BlockedByMissingRedactionEvidence = 30,
    BlockedByForbiddenActionCatalog = 31,
    NoVisibilityRuleSeparateDecisionRequired = 32
}

public sealed record MissingEvidenceVisibilityRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedRoleId { get; init; }
    public required MissingEvidenceKind RequestedMissingEvidenceKind { get; init; }
    public required MissingEvidenceMaterialKind RequestedMaterialKind { get; init; }
    public required MissingEvidenceVisibilityIntent RequestedIntent { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string VisibilityMatrixEvidenceRef { get; init; }
    public required string ForbiddenActionCatalogEvidenceRef { get; init; }
    public required string SourceMissingEvidenceRef { get; init; }
    public string? OptionalTenantBoundaryEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalApprovalEvidenceRef { get; init; }
}

public sealed record MissingEvidenceVisibilityDecision
{
    public required MissingEvidenceVisibilityClassification Classification { get; init; }
    public required string RoleId { get; init; }
    public required GovernanceRoleKind RoleKind { get; init; }
    public required MissingEvidenceKind MissingEvidenceKind { get; init; }
    public required RoleVisibilityLevel EffectiveCandidateVisibility { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required string RecordFingerprint { get; init; }
    public required bool IsEvidenceSatisfied { get; init; }
    public required bool CreatesEvidence { get; init; }
    public required bool OverridesMissingEvidence { get; init; }
    public required bool WaivesEvidenceRequirement { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
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
    public required bool RequiresSeparateAuthority { get; init; }
}

public sealed record MissingEvidenceVisibilityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

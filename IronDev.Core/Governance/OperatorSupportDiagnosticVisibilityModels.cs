namespace IronDev.Core.Governance;

public enum OperatorSupportDiagnosticMaterialKind
{
    Unknown = 0,
    OperationStatusMetadata = 1,
    OperationStatusSummary = 2,
    ValidationSummary = 3,
    FailureClassificationSummary = 4,
    RetryClassificationSummary = 5,
    RollbackReadinessSummary = 6,
    RecoveryRecommendationSummary = 7,
    DependencyHealthSummary = 8,
    EnvironmentReadinessSummary = 9,
    QueueOrRunnerStateSummary = 10,
    RedactedErrorSummary = 11,
    RedactedLogSummary = 12,
    RedactedDiagnosticRationaleSummary = 13,
    RawLog = 14,
    RawPayload = 15,
    RawProviderResponse = 16,
    CredentialMaterial = 17,
    SecretMaterial = 18,
    PrivateReasoning = 19,
    AuthorityMarker = 20,
    SourcePatch = 21,
    CommitPackage = 22,
    PushReceipt = 23,
    PullRequestMutationReceipt = 24,
    ReleaseOrDeployReceipt = 25
}

public enum OperatorSupportDiagnosticRequestedIntent
{
    Unknown = 0,
    ReadOnlyInspect = 1,
    ReadOnlySummarise = 2,
    RunDiagnostic = 3,
    RefreshValidation = 4,
    ProveSourceSafety = 5,
    ExecuteRetry = 6,
    ExecuteRollback = 7,
    ExecuteRecovery = 8,
    MutateSource = 9,
    ApplyPatch = 10,
    Commit = 11,
    Push = 12,
    CreatePullRequest = 13,
    ReadyForReview = 14,
    Merge = 15,
    Release = 16,
    Deploy = 17,
    ApprovalAuthority = 18,
    PolicySatisfaction = 19,
    WorkflowContinuation = 20,
    VisibilityGrant = 21,
    AccessGrant = 22,
    RedactionBypass = 23,
    SecretDisclosure = 24,
    PrivateReasoningDisclosure = 25
}

public enum OperatorSupportDiagnosticVisibilityClassification
{
    Invalid = 0,
    Hidden = 1,
    MetadataOnlyCandidate = 2,
    SummaryCandidate = 3,
    RedactedSummaryCandidate = 4,
    BlockedByNonOperatorSupportRole = 5,
    BlockedByActionIntent = 6,
    BlockedByDiagnosticExecutionIntent = 7,
    BlockedByRetryIntent = 8,
    BlockedByRollbackIntent = 9,
    BlockedByRecoveryIntent = 10,
    BlockedByMutationIntent = 11,
    BlockedByMissingCatalogEvidence = 12,
    BlockedByMissingMatrixEvidence = 13,
    BlockedByMissingDiagnosticEvidence = 14,
    BlockedByMissingRedactionEvidence = 15,
    BlockedBySensitiveMaterial = 16,
    BlockedByRawLogMaterial = 17,
    BlockedByRawPayloadMaterial = 18,
    BlockedByPrivateReasoningMaterial = 19,
    BlockedByCredentialMaterial = 20,
    BlockedBySecretMaterial = 21,
    BlockedByAuthorityMarker = 22,
    BlockedByUnknownMaterial = 23,
    BlockedByUnknownIntent = 24
}

public sealed record OperatorSupportDiagnosticVisibilityRequest
{
    public required string CorrelationId { get; init; }
    public required string RequestedRoleKey { get; init; }
    public required RoleVisibilitySurface RequestedSurface { get; init; }
    public required OperatorSupportDiagnosticMaterialKind RequestedMaterialKind { get; init; }
    public required OperatorSupportDiagnosticRequestedIntent RequestedIntent { get; init; }
    public required string DiagnosticEvidenceRef { get; init; }
    public required string RoleCatalogEvidenceRef { get; init; }
    public required string VisibilityMatrixEvidenceRef { get; init; }
    public string? OptionalPolicyEvidenceRef { get; init; }
    public string? OptionalRedactionEvidenceRef { get; init; }
}

public sealed record OperatorSupportDiagnosticVisibilityDecision
{
    public required OperatorSupportDiagnosticVisibilityClassification Classification { get; init; }
    public required RoleVisibilityLevel EffectiveCandidateVisibility { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required bool GrantsOperatorAuthority { get; init; }
    public required bool GrantsSupportAuthority { get; init; }
    public required bool GrantsRoleAssignmentAuthority { get; init; }
    public required bool GrantsVisibilityAuthority { get; init; }
    public required bool GrantsAccess { get; init; }
    public required bool GrantsDiagnosticExecutionAuthority { get; init; }
    public required bool RefreshesValidation { get; init; }
    public required bool ProvesSourceSafety { get; init; }
    public required bool GrantsRetryAuthority { get; init; }
    public required bool GrantsRollbackAuthority { get; init; }
    public required bool GrantsRecoveryAuthority { get; init; }
    public required bool GrantsApprovalAuthority { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool GrantsMutationAuthority { get; init; }
    public required bool GrantsWorkflowContinuation { get; init; }
    public required bool GrantsMergeAuthority { get; init; }
    public required bool GrantsReleaseAuthority { get; init; }
    public required bool GrantsDeploymentAuthority { get; init; }
    public required bool BypassesRedaction { get; init; }
    public required bool DisclosesSecrets { get; init; }
    public required bool DisclosesPrivateReasoning { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record OperatorSupportDiagnosticVisibilityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> UnsafeRefs { get; init; }
}

namespace IronDev.Core.Governance;

public enum GovernanceDataRecordKind
{
    Unknown = 0,
    GovernanceEvent = 1,
    GovernanceEventReadModel = 2,
    WorkflowRun = 3,
    WorkflowCheckpoint = 4,
    ToolRequest = 5,
    ToolGateDecision = 6,
    ApprovalDecision = 7,
    PolicyDecisionEvent = 8,
    DogfoodReceipt = 9,
    ApplyDryRunReceipt = 10,
    MemoryProposal = 11,
    AgentRunAudit = 12,
    AgentHealthReport = 13,
    GovernanceTraceReport = 14,
    FailedWorkflowDiagnosisReport = 15,
    ApprovalGateDogfoodCorrelationReport = 16,
    BackendOperationalHealthReport = 17
}

public enum GovernanceDataRetentionRuleStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    RuleEvaluationAvailable = 2,
    PreservationRequired = 3,
    HumanReviewRequired = 4
}

public enum GovernanceDataRetentionClass
{
    Unknown = 0,
    PreserveIndefinitely = 1,
    PreserveForAuditWindow = 2,
    PreserveWhileReferenced = 3,
    EligibleForHumanCleanupReview = 4,
    EligibleForFutureArchiveReview = 5,
    EligibleForFutureRedactionReview = 6
}

public enum GovernanceDataPreservationReasonKind
{
    Unknown = 0,
    GovernanceEventIsAppendOnly = 1,
    AuditHoldPresent = 2,
    LegalHoldPresent = 3,
    OpenWorkflowReference = 4,
    OpenApprovalReference = 5,
    OpenPolicyReference = 6,
    OpenToolGateReference = 7,
    OpenMemoryProposalReference = 8,
    MinimumRetentionWindowNotElapsed = 9,
    PrivatePayloadRiskRequiresHumanReview = 10,
    RecordKindRequiresLongTermAudit = 11,
    MissingCreatedUtcRequiresHumanReview = 12,
    UnknownRecordKindRequiresHumanReview = 13,
    MissingCorrelationReferenceRequiresHumanReview = 14,
    RecentReferenceRequiresPreservation = 15
}

public enum GovernanceDataCleanupRecommendationKind
{
    Unknown = 0,
    Preserve = 1,
    ReviewForFutureArchive = 2,
    ReviewForFutureRedaction = 3,
    ReviewForFutureCleanup = 4,
    KeepBecauseReferenced = 5,
    KeepBecauseAuditHold = 6,
    KeepBecauseLegalHold = 7,
    KeepBecauseMinimumWindowNotElapsed = 8
}

public sealed record GovernanceDataRetentionRuleRequest
{
    public required string RecordReferenceId { get; init; }
    public required GovernanceDataRecordKind RecordKind { get; init; }
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset? CreatedUtc { get; init; }
    public DateTimeOffset? LastReferencedUtc { get; init; }
    public bool HasAuditHold { get; init; }
    public bool HasLegalHold { get; init; }
    public bool HasOpenWorkflowReference { get; init; }
    public bool HasOpenApprovalReference { get; init; }
    public bool HasOpenPolicyReference { get; init; }
    public bool HasOpenToolGateReference { get; init; }
    public bool HasOpenMemoryProposalReference { get; init; }
    public bool ContainsPrivatePayloadRisk { get; init; }
}

public sealed record GovernanceDataRetentionRuleResult
{
    public required GovernanceDataRetentionRuleStatus Status { get; init; }
    public required string RecordReferenceId { get; init; }
    public required GovernanceDataRecordKind RecordKind { get; init; }
    public required GovernanceDataRetentionClass RetentionClass { get; init; }
    public required TimeSpan? MinimumRetentionPeriod { get; init; }
    public required DateTimeOffset? EarliestReviewUtc { get; init; }
    public required IReadOnlyList<GovernanceDataPreservationReason> PreservationReasons { get; init; }
    public required IReadOnlyList<GovernanceDataCleanupRecommendation> CleanupRecommendations { get; init; }
    public required IReadOnlyList<string> SafeSummaryLines { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
    public required bool IsRuleEvaluationOnly { get; init; }
    public required bool IsCleanupExecution { get; init; }
    public required bool IsDeletePermission { get; init; }
    public required bool IsPurgePermission { get; init; }
    public required bool IsArchivePermission { get; init; }
    public required bool IsRedactionPermission { get; init; }
    public required bool IsLegalHoldOverride { get; init; }
    public required bool CanDeleteData { get; init; }
    public required bool CanPurgeData { get; init; }
    public required bool CanArchiveData { get; init; }
    public required bool CanRedactData { get; init; }
    public required bool CanRunCleanup { get; init; }
    public required bool CanScheduleCleanup { get; init; }
    public required bool CanMutateSql { get; init; }
    public required bool CanBypassAuditHold { get; init; }
    public required bool CanBypassLegalHold { get; init; }
}

public sealed record GovernanceDataPreservationReason
{
    public required string ReasonId { get; init; }
    public required GovernanceDataPreservationReasonKind Kind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record GovernanceDataCleanupRecommendation
{
    public required string RecommendationId { get; init; }
    public required GovernanceDataCleanupRecommendationKind Kind { get; init; }
    public required string SafeSummary { get; init; }
    public required bool IsReviewOnly { get; init; }
    public required bool IsDeleteCommand { get; init; }
    public required bool IsPurgeCommand { get; init; }
    public required bool IsArchiveCommand { get; init; }
    public required bool IsRedactionCommand { get; init; }
    public required bool RequiresHumanReview { get; init; }
}

public static class GovernanceDataRetentionRuleBoundaries
{
    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Retention rule evaluation is not cleanup execution.",
        "Cleanup eligibility is not deletion permission.",
        "Cleanup recommendation is not cleanup approval.",
        "Expired retention window is not purge authority.",
        "Archive recommendation is not archive execution.",
        "Redaction recommendation is not redaction execution.",
        "Legal hold beats cleanup.",
        "Audit hold beats cleanup.",
        "Governance events are append-only and preserved.",
        "Authority decision records are preserved unless a later explicitly governed retention executor exists.",
        "Retention durations are engineering defaults for future governed review, not legal advice and not cleanup execution."
    ];
}

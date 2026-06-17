namespace IronDev.Core.Governance;

public static class FailedContinuationRecoveryCampaignStatuses
{
    public const string RecoveryEvidenceSatisfied = "RecoveryEvidenceSatisfied";
    public const string RecoveryEvidenceMissing = "RecoveryEvidenceMissing";
    public const string RecoveryEvidenceFailed = "RecoveryEvidenceFailed";
    public const string RecoveryEvidenceStale = "RecoveryEvidenceStale";
    public const string Rejected = "Rejected";
}

public static class FailedContinuationRecoveryFindingSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Blocking = "Blocking";
}

public static class FailedContinuationRecoveryCampaignBoundaryText
{
    public const string Boundary = """
        Failed continuation recovery campaign inspects supplied failed-continuation and recovery evidence only.
        Failed continuation recovery campaign is not workflow continuation.
        Failed continuation recovery campaign does not retry workflow continuation.
        Failed continuation recovery campaign does not mutate workflow state.
        Failed continuation recovery campaign does not create workflow transition records.
        Failed continuation recovery campaign is not release readiness.
        Failed continuation recovery campaign is not release approval.
        Failed continuation recovery campaign is not deployment approval.
        Failed continuation recovery campaign is not merge approval.
        Failed continuation recovery campaign is not release execution.
        Failed continuation recovery campaign is not source apply.
        Failed continuation recovery campaign is not rollback execution.
        Failed continuation recovery campaign does not run git.
        Failed continuation recovery campaign does not create pull requests.
        Failed continuation recovery campaign does not call agents, models, tools, UI, memory, or retrieval.
        Human review remains required before retrying continuation, mutating workflow, or approving release.
        """;
}

public sealed record FailedContinuationRecoveryCampaignRequest
{
    public required Guid FailedContinuationRecoveryCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required WorkflowContinuationFailureEvidence WorkflowContinuationFailure { get; init; }
    public WorkflowTransitionRecoveryEvidence? WorkflowTransitionRecovery { get; init; }
    public StaleAuthorityDetectionResult? StaleAuthorityDetection { get; init; }
    public ReleaseReadinessDecisionRecord? FollowUpReleaseReadinessDecision { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = FailedContinuationRecoveryCampaignBoundaryText.Boundary;
}

public sealed record WorkflowContinuationFailureEvidence
{
    public required Guid GovernedWorkflowContinuationRequestId { get; init; }
    public required string GovernedWorkflowContinuationRequestHash { get; init; }
    public Guid? WorkflowTransitionRecordId { get; init; }
    public string? WorkflowTransitionRecordHash { get; init; }
    public required bool ContinuationAttempted { get; init; }
    public required bool ContinuationSucceeded { get; init; }
    public required bool ContinuationFailed { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required string FromWorkflowStepId { get; init; }
    public required string IntendedToWorkflowStepId { get; init; }
    public required string ExpectedWorkflowStateHash { get; init; }
    public required string ObservedWorkflowStateHash { get; init; }
    public required DateTimeOffset AttemptedAtUtc { get; init; }
    public required IReadOnlyList<string> FailedTransitionReasons { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
}

public sealed record WorkflowTransitionRecoveryEvidence
{
    public required Guid RecoveryEvidenceId { get; init; }
    public required string RecoveryEvidenceHash { get; init; }
    public required bool FailureReviewed { get; init; }
    public required bool WorkflowStateConfirmedUnchanged { get; init; }
    public required bool RetryRequiresHumanReview { get; init; }
    public required string ConfirmedWorkflowRunId { get; init; }
    public required string ConfirmedWorkflowStepId { get; init; }
    public required string ConfirmedWorkflowStateHash { get; init; }
    public required DateTimeOffset ReviewedAtUtc { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
}

public sealed record FailedContinuationRecoveryCampaignResult
{
    public required Guid FailedContinuationRecoveryCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required bool ContinuationFailureConfirmed { get; init; }
    public required bool WorkflowWasMutatedDuringFailure { get; init; }
    public required bool TransitionRecoveryEvidencePresent { get; init; }
    public required bool WorkflowStateConfirmedUnchanged { get; init; }
    public required bool RetryRequiresHumanReview { get; init; }
    public required bool StaleAuthorityDetected { get; init; }
    public required bool FollowUpReadinessEvidencePresent { get; init; }
    public required IReadOnlyList<FailedContinuationRecoveryFinding> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required bool RecoveryEvidenceSatisfied { get; init; }
    public required bool WorkflowContinuationRetried { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool WorkflowTransitionRecordCreated { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool RollbackAuditExecuted { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool AuthorityRefreshed { get; init; }
    public required bool EvidenceReissued { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public string Boundary { get; init; } = FailedContinuationRecoveryCampaignBoundaryText.Boundary;
}

public sealed record FailedContinuationRecoveryFinding
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public interface IFailedContinuationRecoveryCampaignRunner
{
    FailedContinuationRecoveryCampaignResult Run(FailedContinuationRecoveryCampaignRequest? request);
}

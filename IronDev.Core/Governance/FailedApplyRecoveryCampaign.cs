namespace IronDev.Core.Governance;

public static class FailedApplyRecoveryCampaignStatuses
{
    public const string RecoveryEvidenceSatisfied = "RecoveryEvidenceSatisfied";
    public const string RecoveryEvidenceMissing = "RecoveryEvidenceMissing";
    public const string RecoveryEvidenceFailed = "RecoveryEvidenceFailed";
    public const string RecoveryEvidenceStale = "RecoveryEvidenceStale";
    public const string Rejected = "Rejected";
}

public static class FailedApplyRecoveryFindingSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Blocking = "Blocking";
}

public static class FailedApplyRecoveryCampaignBoundaryText
{
    public const string Boundary = """
        Failed apply recovery campaign inspects supplied failed-apply and recovery evidence only.
        Failed apply recovery campaign is not source apply.
        Failed apply recovery campaign does not retry source apply.
        Failed apply recovery campaign is not rollback execution.
        Failed apply recovery campaign is not rollback audit execution.
        Failed apply recovery campaign is not workflow continuation.
        Failed apply recovery campaign does not mutate workflow state.
        Failed apply recovery campaign is not release readiness.
        Failed apply recovery campaign is not release approval.
        Failed apply recovery campaign is not deployment approval.
        Failed apply recovery campaign is not merge approval.
        Failed apply recovery campaign is not release execution.
        Failed apply recovery campaign does not run git.
        Failed apply recovery campaign does not create pull requests.
        Failed apply recovery campaign does not call agents, models, tools, UI, memory, or retrieval.
        Human review remains required before retrying apply, executing rollback, continuing workflow, or approving release.
        """;
}

public sealed record FailedApplyRecoveryCampaignRequest
{
    public required Guid FailedApplyRecoveryCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required SourceApplyFailureEvidence SourceApplyFailure { get; init; }
    public RollbackRecoveryEvidence? RollbackRecovery { get; init; }
    public RollbackAuditEvidence? RollbackAudit { get; init; }
    public StaleAuthorityDetectionResult? StaleAuthorityDetection { get; init; }
    public ReleaseReadinessDecisionRecord? FollowUpReleaseReadinessDecision { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = FailedApplyRecoveryCampaignBoundaryText.Boundary;
}

public sealed record SourceApplyFailureEvidence
{
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public Guid? SourceApplyReceiptId { get; init; }
    public string? SourceApplyReceiptHash { get; init; }
    public required bool SourceApplyAttempted { get; init; }
    public required bool SourceApplySucceeded { get; init; }
    public required bool SourceApplyPartial { get; init; }
    public required bool SourceApplyFailed { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceHash { get; init; }
    public required DateTimeOffset AttemptedAtUtc { get; init; }
    public required IReadOnlyList<string> FailedPaths { get; init; }
    public required IReadOnlyList<string> AppliedPaths { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
}

public sealed record RollbackRecoveryEvidence
{
    public required Guid RollbackExecutionReceiptId { get; init; }
    public required string RollbackExecutionReceiptHash { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required bool RollbackPartial { get; init; }
    public required string RestoredSourceBaselineHash { get; init; }
    public required string RestoredWorkspaceHash { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }
    public required IReadOnlyList<string> RestoredPaths { get; init; }
    public required IReadOnlyList<string> FailedRollbackPaths { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
}

public sealed record RollbackAuditEvidence
{
    public required Guid RollbackAuditReportId { get; init; }
    public required string RollbackAuditReportHash { get; init; }
    public required bool AuditRan { get; init; }
    public required bool AuditConsistent { get; init; }
    public required string AuditedSourceBaselineHash { get; init; }
    public required string AuditedWorkspaceHash { get; init; }
    public required DateTimeOffset AuditedAtUtc { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
}

public sealed record FailedApplyRecoveryCampaignResult
{
    public required Guid FailedApplyRecoveryCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required bool SourceApplyFailureConfirmed { get; init; }
    public required bool RollbackEvidencePresent { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required bool RollbackAuditPresent { get; init; }
    public required bool RollbackAuditConsistent { get; init; }
    public required bool StaleAuthorityDetected { get; init; }
    public required bool FollowUpReadinessEvidencePresent { get; init; }
    public required IReadOnlyList<FailedApplyRecoveryFinding> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required bool RecoveryEvidenceSatisfied { get; init; }
    public required bool SourceApplyRetried { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecutedByCampaign { get; init; }
    public required bool RollbackAuditExecutedByCampaign { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool AuthorityRefreshed { get; init; }
    public required bool EvidenceReissued { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public string Boundary { get; init; } = FailedApplyRecoveryCampaignBoundaryText.Boundary;
}

public sealed record FailedApplyRecoveryFinding
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public interface IFailedApplyRecoveryCampaignRunner
{
    FailedApplyRecoveryCampaignResult Run(FailedApplyRecoveryCampaignRequest? request);
}

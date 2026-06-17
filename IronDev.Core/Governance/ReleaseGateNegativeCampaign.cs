namespace IronDev.Core.Governance;

public static class ReleaseGateNegativeCampaignStatuses
{
    public const string Completed = "Completed";
    public const string CompletedWithUnexpectedPasses = "CompletedWithUnexpectedPasses";
    public const string CompletedWithUnexpectedFailureShape = "CompletedWithUnexpectedFailureShape";
    public const string Rejected = "Rejected";
}

public static class ReleaseGateNegativeCaseKinds
{
    public const string InvalidRequest = "InvalidRequest";
    public const string UnsafeMaterial = "UnsafeMaterial";
    public const string AuthorityClaim = "AuthorityClaim";
    public const string MissingEvidence = "MissingEvidence";
    public const string FailedEvidence = "FailedEvidence";
    public const string StaleEvidence = "StaleEvidence";
    public const string ExpiredEvidence = "ExpiredEvidence";
    public const string SubjectMismatch = "SubjectMismatch";
    public const string WorkflowMismatch = "WorkflowMismatch";
    public const string HashMismatch = "HashMismatch";
    public const string FollowUpRecoveryIncomplete = "FollowUpRecoveryIncomplete";
    public const string UnexpectedApprovalClaim = "UnexpectedApprovalClaim";
    public const string UnexpectedExecutionClaim = "UnexpectedExecutionClaim";
    public const string StoreFailure = "StoreFailure";
    public const string ReadBackFailure = "ReadBackFailure";
}

public static class ReleaseGateNegativeCampaignFindingSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Blocking = "Blocking";
}

public static class ReleaseGateNegativeCampaignBoundaryText
{
    public const string Boundary = """
        Release gate negative campaign runs explicitly supplied negative governed release-gate cases only.
        Release gate negative campaign is not release readiness.
        Release gate negative campaign is not release approval.
        Release gate negative campaign is not deployment approval.
        Release gate negative campaign is not merge approval.
        Release gate negative campaign is not release execution.
        Release gate negative campaign does not create release-readiness reports.
        Release gate negative campaign does not repair evidence.
        Release gate negative campaign does not refresh authority.
        Release gate negative campaign does not reissue evidence.
        Release gate negative campaign does not execute source apply.
        Release gate negative campaign does not execute rollback.
        Release gate negative campaign does not continue workflow.
        Release gate negative campaign does not mutate workflow state.
        Release gate negative campaign does not run git.
        Release gate negative campaign does not create pull requests.
        Release gate negative campaign does not call agents, models, tools, UI, memory, or retrieval.
        Human review remains required.
        """;
}

public sealed record ReleaseGateNegativeCampaignRequest
{
    public required Guid ReleaseGateNegativeCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required int MaxCases { get; init; }
    public required IReadOnlyList<ReleaseGateNegativeCase> Cases { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = ReleaseGateNegativeCampaignBoundaryText.Boundary;
}

public sealed record ReleaseGateNegativeCase
{
    public required Guid ReleaseGateNegativeCaseId { get; init; }
    public required string CaseName { get; init; }
    public required string CaseKind { get; init; }
    public required GovernedReleaseGateRequest GovernedReleaseGateRequest { get; init; }
    public required string ExpectedStatus { get; init; }
    public required bool ExpectedSucceeded { get; init; }
    public required bool ExpectedDecisionRecordStored { get; init; }
    public required bool ExpectedReleaseReadinessEvidenceSatisfied { get; init; }
    public required IReadOnlyList<string> ExpectedIssueCodes { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}

public sealed record ReleaseGateNegativeCampaignResult
{
    public required Guid ReleaseGateNegativeCampaignRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CampaignName { get; init; }
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required int RequestedCaseCount { get; init; }
    public required int CompletedCaseCount { get; init; }
    public required int ExpectedNegativeOutcomeCount { get; init; }
    public required int UnexpectedPassCount { get; init; }
    public required int UnexpectedFailureShapeCount { get; init; }
    public required IReadOnlyList<ReleaseGateNegativeCaseResult> CaseResults { get; init; }
    public required IReadOnlyList<ReleaseGateNegativeCampaignFinding> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool AuthorityRefreshed { get; init; }
    public required bool EvidenceReissued { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public string Boundary { get; init; } = ReleaseGateNegativeCampaignBoundaryText.Boundary;
}

public sealed record ReleaseGateNegativeCaseResult
{
    public required Guid ReleaseGateNegativeCaseId { get; init; }
    public required string CaseName { get; init; }
    public required string CaseKind { get; init; }
    public required bool CaseSucceeded { get; init; }
    public required string ActualStatus { get; init; }
    public required bool ActualSucceeded { get; init; }
    public required bool ActualDecisionRecordStored { get; init; }
    public required bool ActualReleaseReadinessEvidenceSatisfied { get; init; }
    public required bool MatchedExpectedStatus { get; init; }
    public required bool MatchedExpectedSucceeded { get; init; }
    public required bool MatchedExpectedDecisionStorage { get; init; }
    public required bool MatchedExpectedEvidenceSatisfied { get; init; }
    public required bool MatchedExpectedIssues { get; init; }
    public required IReadOnlyList<string> ActualIssueCodes { get; init; }
    public required IReadOnlyList<string> ExpectedIssueCodes { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool HumanReviewRequired { get; init; }
}

public sealed record ReleaseGateNegativeCampaignFinding
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public interface IReleaseGateNegativeCampaignRunner
{
    Task<ReleaseGateNegativeCampaignResult> RunAsync(
        ReleaseGateNegativeCampaignRequest? request,
        CancellationToken cancellationToken = default);
}

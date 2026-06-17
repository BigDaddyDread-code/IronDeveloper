namespace IronDev.Core.Governance;

public static class ReleaseReadinessDecisionRecordReadBoundaryText
{
    public const string AuthorityBoundary = "Release readiness decision record read API is read-only and does not run a release-readiness gate, create release decision records, approve release, approve deployment, approve merge, execute release, execute source apply, execute rollback, continue workflow, run git, call agents/models/tools, promote memory, or activate retrieval.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Release readiness decision record read API is read-only.",
        "Release readiness decision record read API exposes stored ReleaseReadinessDecisionRecord evidence for review only.",
        "Release readiness decision record read API does not run a release-readiness gate.",
        "Release readiness decision record read API does not create release decision records.",
        "Release readiness decision record read API does not approve release, deployment, or merge.",
        "Release readiness decision record read API does not execute release, source apply, rollback, workflow continuation, or git.",
        "Release readiness decision record read API does not call agents, models, tools, memory, or retrieval.",
        "Read ReleaseReadinessDecisionRecord evidence is not ReleaseApproved, DeploymentApproved, MergeApproved, ReleaseExecuted, or WorkflowContinued.",
        "Human review remains required for release approval, deployment, and merge."
    ];
}

public sealed record ReleaseReadinessDecisionRecordReadBoundary
{
    public bool ReadCreatesReleaseReadinessDecisionRecord { get; init; }
    public bool ReadRunsReleaseReadinessGate { get; init; }
    public bool ReadApprovesRelease { get; init; }
    public bool ReadApprovesDeployment { get; init; }
    public bool ReadApprovesMerge { get; init; }
    public bool ReadExecutesRelease { get; init; }
    public bool ReadExecutesSourceApply { get; init; }
    public bool ReadExecutesRollback { get; init; }
    public bool ReadContinuesWorkflow { get; init; }
    public bool ReadRunsGit { get; init; }
    public bool ReadCallsAgentsModelsOrTools { get; init; }
    public bool ReadPromotesMemory { get; init; }
    public bool ReadActivatesRetrieval { get; init; }
    public bool HumanReviewRequired { get; init; } = true;
    public bool HumanReviewRequiredForReleaseApproval { get; init; } = true;
    public bool HumanReviewRequiredForDeployment { get; init; } = true;
    public bool HumanReviewRequiredForMerge { get; init; } = true;
}

public sealed record ReleaseReadinessDecisionReasonReadModel
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record ReleaseReadinessDecisionRecordReadModel
{
    public required Guid ReleaseReadinessDecisionRecordId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ReleaseReadinessReportId { get; init; }
    public required string ReleaseReadinessReportHash { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string DecisionStatus { get; init; }
    public required bool ReleaseReadinessEvidenceSatisfied { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool SourceApplyExecutedByDecision { get; init; }
    public required bool RollbackExecutedByDecision { get; init; }
    public required bool WorkflowMutatedByDecision { get; init; }
    public required bool GitOperationExecutedByDecision { get; init; }
    public required bool ReleaseExecutedByDecision { get; init; }
    public required bool HumanReviewRequiredForReleaseApproval { get; init; }
    public required bool HumanReviewRequiredForDeployment { get; init; }
    public required bool HumanReviewRequiredForMerge { get; init; }
    public required IReadOnlyList<ReleaseReadinessDecisionReasonReadModel> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required DateTimeOffset DecidedAtUtc { get; init; }
    public required string ReleaseReadinessDecisionRecordHash { get; init; }
    public required string Boundary { get; init; }
    public required string AuthorityBoundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool MutationOccurredInThisApi { get; init; }
    public required bool ReleaseReadinessGateRanByThisApi { get; init; }
    public required bool ReleaseApprovedByThisApi { get; init; }
    public required bool DeploymentApprovedByThisApi { get; init; }
    public required bool MergeApprovedByThisApi { get; init; }
    public required bool ReleaseExecutedByThisApi { get; init; }
    public required bool SourceApplyExecutedByThisApi { get; init; }
    public required bool RollbackExecutedByThisApi { get; init; }
    public required bool WorkflowContinuedByThisApi { get; init; }
    public required bool GitOperationExecutedByThisApi { get; init; }
    public required bool HumanReviewRequired { get; init; }
}

public sealed record ReleaseReadinessDecisionRecordListReadModel
{
    public required Guid ProjectId { get; init; }
    public required IReadOnlyList<ReleaseReadinessDecisionRecordReadModel> Records { get; init; }
    public required int Count { get; init; }
    public required bool MutationOccurredInThisApi { get; init; }
    public required bool ReleaseReadinessGateRanByThisApi { get; init; }
    public required bool ReleaseApprovedByThisApi { get; init; }
    public required bool DeploymentApprovedByThisApi { get; init; }
    public required bool MergeApprovedByThisApi { get; init; }
    public required bool ReleaseExecutedByThisApi { get; init; }
    public required bool SourceApplyExecutedByThisApi { get; init; }
    public required bool RollbackExecutedByThisApi { get; init; }
    public required bool WorkflowContinuedByThisApi { get; init; }
    public required bool GitOperationExecutedByThisApi { get; init; }
    public required bool HumanReviewRequired { get; init; }
}

public interface IReleaseReadinessDecisionRecordQueryService
{
    Task<ReleaseReadinessDecisionRecordReadModel?> GetAsync(
        Guid projectId,
        Guid releaseReadinessDecisionRecordId,
        CancellationToken cancellationToken = default);

    Task<ReleaseReadinessDecisionRecordReadModel?> GetByRecordHashAsync(
        Guid projectId,
        string releaseReadinessDecisionRecordHash,
        CancellationToken cancellationToken = default);

    Task<ReleaseReadinessDecisionRecordListReadModel> ListByReleaseReadinessReportAsync(
        Guid projectId,
        Guid releaseReadinessReportId,
        int take,
        CancellationToken cancellationToken = default);

    Task<ReleaseReadinessDecisionRecordListReadModel> ListByWorkflowRunAsync(
        Guid projectId,
        string workflowRunId,
        int take,
        CancellationToken cancellationToken = default);

    Task<ReleaseReadinessDecisionRecordListReadModel> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        int take,
        CancellationToken cancellationToken = default);
}

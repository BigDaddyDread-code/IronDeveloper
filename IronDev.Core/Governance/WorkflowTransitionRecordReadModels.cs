namespace IronDev.Core.Governance;

public static class WorkflowTransitionRecordReadBoundaryText
{
    public const string AuthorityBoundary = "Workflow transition record read API is read-only and does not continue workflow, mutate workflow state, transition workflow, complete workflow steps, start next workflow steps, infer release readiness, approve release, execute source apply, execute rollback, call agents/models/tools, promote memory, or activate retrieval.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Workflow transition record read API is read-only.",
        "Workflow transition record read API exposes stored WorkflowTransitionRecord evidence for review only.",
        "Workflow transition record read API does not continue workflow.",
        "Workflow transition record read API does not mutate workflow state.",
        "Workflow transition record read API does not transition workflow.",
        "Workflow transition record read API does not complete workflow steps or start next workflow steps.",
        "Workflow transition record read API does not infer release readiness or approve release.",
        "Workflow transition record read API does not execute source apply or rollback.",
        "Workflow transition record read API does not call agents, models, tools, git, memory, or retrieval.",
        "Read WorkflowTransitionRecord evidence is not WorkflowContinued, ReleaseReady, or ReleaseApproved.",
        "Human review remains required."
    ];
}

public sealed record WorkflowTransitionRecordReadBoundary
{
    public bool ReadCreatesWorkflowTransitionRecord { get; init; }
    public bool ReadMutatesWorkflowState { get; init; }
    public bool ReadContinuesWorkflow { get; init; }
    public bool ReadTransitionsWorkflow { get; init; }
    public bool ReadCompletesWorkflowStep { get; init; }
    public bool ReadStartsNextWorkflowStep { get; init; }
    public bool ReadEvaluatesContinuationGate { get; init; }
    public bool ReadSatisfiesContinuationGate { get; init; }
    public bool ReadInfersReleaseReadiness { get; init; }
    public bool ReadApprovesRelease { get; init; }
    public bool ReadExecutesSourceApply { get; init; }
    public bool ReadExecutesRollback { get; init; }
    public bool ReadRunsGit { get; init; }
    public bool ReadCallsAgentsModelsOrTools { get; init; }
    public bool ReadPromotesMemory { get; init; }
    public bool ReadActivatesRetrieval { get; init; }
    public bool HumanReviewRequired { get; init; } = true;
    public bool HumanReviewRequiredForReleaseReadiness { get; init; } = true;
    public bool HumanReviewRequiredForReleaseApproval { get; init; } = true;
}

public sealed record WorkflowTransitionRecordReadModel
{
    public required Guid WorkflowTransitionRecordId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string TransitionKind { get; init; }
    public required string PreviousWorkflowStateHash { get; init; }
    public required string NewWorkflowStateHash { get; init; }
    public required string PreviousStepStateHash { get; init; }
    public required string NewStepStateHash { get; init; }
    public string? PreviousStepId { get; init; }
    public string? NextStepId { get; init; }
    public required Guid WorkflowContinuationGateEvaluationId { get; init; }
    public required string WorkflowContinuationGateEvaluationHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public Guid? RollbackExecutionReceiptId { get; init; }
    public string? RollbackExecutionReceiptHash { get; init; }
    public Guid? RollbackExecutionAuditReportId { get; init; }
    public string? RollbackExecutionAuditReportHash { get; init; }
    public required bool WorkflowStateMutated { get; init; }
    public required bool StepCompleted { get; init; }
    public required bool NextStepStarted { get; init; }
    public required bool ReleaseReadinessInferred { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required DateTimeOffset TransitionedAtUtc { get; init; }
    public required string WorkflowTransitionRecordHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required string Boundary { get; init; }
    public required string AuthorityBoundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required bool MutationOccurredInThisApi { get; init; }
    public required bool WorkflowContinuationExecutedByThisApi { get; init; }
    public required bool ReleaseReadinessInferredByThisApi { get; init; }
    public required bool ReleaseApprovedByThisApi { get; init; }
    public required bool HumanReviewRequired { get; init; }
}

public sealed record WorkflowTransitionRecordListReadModel
{
    public required Guid ProjectId { get; init; }
    public required IReadOnlyList<WorkflowTransitionRecordReadModel> Records { get; init; }
    public required int Count { get; init; }
    public required bool MutationOccurredInThisApi { get; init; }
    public required bool WorkflowContinuationExecutedByThisApi { get; init; }
    public required bool ReleaseReadinessInferredByThisApi { get; init; }
    public required bool ReleaseApprovedByThisApi { get; init; }
    public required bool HumanReviewRequired { get; init; }
}

public interface IWorkflowTransitionRecordQueryService
{
    Task<WorkflowTransitionRecordReadModel?> GetAsync(Guid projectId, Guid workflowTransitionRecordId, CancellationToken cancellationToken = default);
    Task<WorkflowTransitionRecordReadModel?> GetByRecordHashAsync(Guid projectId, string workflowTransitionRecordHash, CancellationToken cancellationToken = default);
    Task<WorkflowTransitionRecordListReadModel> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, int take, CancellationToken cancellationToken = default);
    Task<WorkflowTransitionRecordListReadModel> ListByWorkflowStepAsync(Guid projectId, string workflowRunId, string workflowStepId, int take, CancellationToken cancellationToken = default);
    Task<WorkflowTransitionRecordListReadModel> ListByContinuationGateEvaluationAsync(Guid projectId, Guid workflowContinuationGateEvaluationId, int take, CancellationToken cancellationToken = default);
    Task<WorkflowTransitionRecordListReadModel> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, int take, CancellationToken cancellationToken = default);
    Task<WorkflowTransitionRecordListReadModel> ListByRollbackExecutionReceiptAsync(Guid projectId, Guid rollbackExecutionReceiptId, int take, CancellationToken cancellationToken = default);
}

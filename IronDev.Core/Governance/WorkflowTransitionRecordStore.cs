namespace IronDev.Core.Governance;

public interface IWorkflowTransitionRecordStore
{
    Task SaveAsync(WorkflowTransitionRecord record, CancellationToken cancellationToken = default);

    Task<WorkflowTransitionRecord?> GetAsync(
        Guid projectId,
        Guid workflowTransitionRecordId,
        CancellationToken cancellationToken = default);

    Task<WorkflowTransitionRecord?> GetByRecordHashAsync(
        Guid projectId,
        string workflowTransitionRecordHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowRunAsync(
        Guid projectId,
        string workflowRunId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowStepAsync(
        Guid projectId,
        string workflowRunId,
        string workflowStepId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTransitionRecord>> ListByContinuationGateEvaluationAsync(
        Guid projectId,
        Guid workflowContinuationGateEvaluationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTransitionRecord>> ListBySourceApplyReceiptAsync(
        Guid projectId,
        Guid sourceApplyReceiptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowTransitionRecord>> ListByRollbackExecutionReceiptAsync(
        Guid projectId,
        Guid rollbackExecutionReceiptId,
        CancellationToken cancellationToken = default);
}

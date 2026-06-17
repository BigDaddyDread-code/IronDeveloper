using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class WorkflowTransitionRecordQueryService : IWorkflowTransitionRecordQueryService
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;
    private const string RedactedUnsafeText = "[redacted: sensitive workflow transition record text]";

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private readonly IWorkflowTransitionRecordStore _store;

    public WorkflowTransitionRecordQueryService(IWorkflowTransitionRecordStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<WorkflowTransitionRecordReadModel?> GetAsync(Guid projectId, Guid workflowTransitionRecordId, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(projectId, workflowTransitionRecordId, cancellationToken);
        return record is null ? null : ToReadModel(record);
    }

    public async Task<WorkflowTransitionRecordReadModel?> GetByRecordHashAsync(Guid projectId, string workflowTransitionRecordHash, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetByRecordHashAsync(projectId, workflowTransitionRecordHash, cancellationToken);
        return record is null ? null : ToReadModel(record);
    }

    public async Task<WorkflowTransitionRecordListReadModel> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, int take, CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByWorkflowRunAsync(projectId, workflowRunId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    public async Task<WorkflowTransitionRecordListReadModel> ListByWorkflowStepAsync(Guid projectId, string workflowRunId, string workflowStepId, int take, CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByWorkflowStepAsync(projectId, workflowRunId, workflowStepId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    public async Task<WorkflowTransitionRecordListReadModel> ListByContinuationGateEvaluationAsync(Guid projectId, Guid workflowContinuationGateEvaluationId, int take, CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByContinuationGateEvaluationAsync(projectId, workflowContinuationGateEvaluationId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    public async Task<WorkflowTransitionRecordListReadModel> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, int take, CancellationToken cancellationToken = default)
    {
        var records = await _store.ListBySourceApplyReceiptAsync(projectId, sourceApplyReceiptId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    public async Task<WorkflowTransitionRecordListReadModel> ListByRollbackExecutionReceiptAsync(Guid projectId, Guid rollbackExecutionReceiptId, int take, CancellationToken cancellationToken = default)
    {
        var records = await _store.ListByRollbackExecutionReceiptAsync(projectId, rollbackExecutionReceiptId, cancellationToken);
        return ToListReadModel(projectId, records, take);
    }

    private static WorkflowTransitionRecordListReadModel ToListReadModel(Guid projectId, IReadOnlyList<WorkflowTransitionRecord> records, int take)
    {
        var items = records.Take(NormalizeTake(take)).Select(ToReadModel).ToArray();
        return new WorkflowTransitionRecordListReadModel
        {
            ProjectId = projectId,
            Records = items,
            Count = items.Length,
            MutationOccurredInThisApi = false,
            WorkflowContinuationExecutedByThisApi = false,
            ReleaseReadinessInferredByThisApi = false,
            ReleaseApprovedByThisApi = false,
            HumanReviewRequired = true
        };
    }

    private static int NormalizeTake(int take) =>
        take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

    private static WorkflowTransitionRecordReadModel ToReadModel(WorkflowTransitionRecord record) => new()
    {
        WorkflowTransitionRecordId = record.WorkflowTransitionRecordId,
        ProjectId = record.ProjectId,
        WorkflowRunId = SafeText(record.WorkflowRunId),
        WorkflowStepId = SafeText(record.WorkflowStepId),
        TransitionKind = SafeText(record.TransitionKind),
        PreviousWorkflowStateHash = SafeText(record.PreviousWorkflowStateHash),
        NewWorkflowStateHash = SafeText(record.NewWorkflowStateHash),
        PreviousStepStateHash = SafeText(record.PreviousStepStateHash),
        NewStepStateHash = SafeText(record.NewStepStateHash),
        PreviousStepId = SafeNullable(record.PreviousStepId),
        NextStepId = SafeNullable(record.NextStepId),
        WorkflowContinuationGateEvaluationId = record.WorkflowContinuationGateEvaluationId,
        WorkflowContinuationGateEvaluationHash = SafeText(record.WorkflowContinuationGateEvaluationHash),
        SourceApplyRequestId = record.SourceApplyRequestId,
        SourceApplyRequestHash = SafeText(record.SourceApplyRequestHash),
        SourceApplyReceiptId = record.SourceApplyReceiptId,
        SourceApplyReceiptHash = SafeText(record.SourceApplyReceiptHash),
        RollbackExecutionReceiptId = record.RollbackExecutionReceiptId,
        RollbackExecutionReceiptHash = SafeNullable(record.RollbackExecutionReceiptHash),
        RollbackExecutionAuditReportId = record.RollbackExecutionAuditReportId,
        RollbackExecutionAuditReportHash = SafeNullable(record.RollbackExecutionAuditReportHash),
        WorkflowStateMutated = record.WorkflowStateMutated,
        StepCompleted = record.StepCompleted,
        NextStepStarted = record.NextStepStarted,
        ReleaseReadinessInferred = record.ReleaseReadinessInferred,
        ReleaseApproved = record.ReleaseApproved,
        SourceApplyExecuted = record.SourceApplyExecuted,
        RollbackExecuted = record.RollbackExecuted,
        TransitionedAtUtc = record.TransitionedAtUtc,
        WorkflowTransitionRecordHash = SafeText(record.WorkflowTransitionRecordHash),
        EvidenceReferences = record.EvidenceReferences.Select(SafeText).ToArray(),
        BoundaryMaxims = record.BoundaryMaxims.Select(SafeText).ToArray(),
        Boundary = SafeText(record.Boundary),
        AuthorityBoundary = WorkflowTransitionRecordReadBoundaryText.AuthorityBoundary,
        Warnings = WorkflowTransitionRecordReadBoundaryText.Warnings,
        MutationOccurredInThisApi = false,
        WorkflowContinuationExecutedByThisApi = false,
        ReleaseReadinessInferredByThisApi = false,
        ReleaseApprovedByThisApi = false,
        HumanReviewRequired = true
    };

    private static string? SafeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SafeText(value);

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return PrivateMaterialMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? RedactedUnsafeText
            : value.Trim();
    }
}

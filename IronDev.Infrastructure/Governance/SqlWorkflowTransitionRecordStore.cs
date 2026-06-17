using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlWorkflowTransitionRecordStore : IWorkflowTransitionRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlWorkflowTransitionRecordStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(WorkflowTransitionRecord record, CancellationToken cancellationToken = default)
    {
        var validation = WorkflowTransitionRecordValidation.Validate(record);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(record));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_WorkflowTransitionRecord_Save",
            new
            {
                record.WorkflowTransitionRecordId,
                record.ProjectId,
                WorkflowRunId = Normalize(record.WorkflowRunId),
                WorkflowStepId = Normalize(record.WorkflowStepId),
                TransitionKind = Normalize(record.TransitionKind),
                PreviousWorkflowStateHash = Normalize(record.PreviousWorkflowStateHash),
                NewWorkflowStateHash = Normalize(record.NewWorkflowStateHash),
                PreviousStepStateHash = Normalize(record.PreviousStepStateHash),
                NewStepStateHash = Normalize(record.NewStepStateHash),
                PreviousStepId = NormalizeNullable(record.PreviousStepId),
                NextStepId = NormalizeNullable(record.NextStepId),
                record.WorkflowContinuationGateEvaluationId,
                WorkflowContinuationGateEvaluationHash = Normalize(record.WorkflowContinuationGateEvaluationHash),
                record.SourceApplyRequestId,
                SourceApplyRequestHash = Normalize(record.SourceApplyRequestHash),
                record.SourceApplyReceiptId,
                SourceApplyReceiptHash = Normalize(record.SourceApplyReceiptHash),
                record.RollbackExecutionReceiptId,
                RollbackExecutionReceiptHash = NormalizeNullable(record.RollbackExecutionReceiptHash),
                record.RollbackExecutionAuditReportId,
                RollbackExecutionAuditReportHash = NormalizeNullable(record.RollbackExecutionAuditReportHash),
                record.WorkflowStateMutated,
                record.StepCompleted,
                record.NextStepStarted,
                record.ReleaseReadinessInferred,
                record.ReleaseApproved,
                record.SourceApplyExecuted,
                record.RollbackExecuted,
                record.TransitionedAtUtc,
                WorkflowTransitionRecordHash = Normalize(record.WorkflowTransitionRecordHash),
                EvidenceReferencesJson = JsonSerializer.Serialize(record.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(record.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(record.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<WorkflowTransitionRecord?> GetAsync(Guid projectId, Guid workflowTransitionRecordId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<WorkflowTransitionRecordRow>(new CommandDefinition(
            "governance.usp_WorkflowTransitionRecord_Get",
            new { ProjectId = projectId, WorkflowTransitionRecordId = workflowTransitionRecordId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<WorkflowTransitionRecord?> GetByRecordHashAsync(Guid projectId, string workflowTransitionRecordHash, CancellationToken cancellationToken = default)
    {
        RequireText(workflowTransitionRecordHash, nameof(workflowTransitionRecordHash));
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<WorkflowTransitionRecordRow>(new CommandDefinition(
            "governance.usp_WorkflowTransitionRecord_GetByRecordHash",
            new { ProjectId = projectId, WorkflowTransitionRecordHash = Normalize(workflowTransitionRecordHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, CancellationToken cancellationToken = default)
    {
        RequireText(workflowRunId, nameof(workflowRunId));
        return await QueryListAsync("governance.usp_WorkflowTransitionRecord_ListByWorkflowRun", new { ProjectId = projectId, WorkflowRunId = Normalize(workflowRunId) }, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowStepAsync(Guid projectId, string workflowRunId, string workflowStepId, CancellationToken cancellationToken = default)
    {
        RequireText(workflowRunId, nameof(workflowRunId));
        RequireText(workflowStepId, nameof(workflowStepId));
        return await QueryListAsync("governance.usp_WorkflowTransitionRecord_ListByWorkflowStep", new { ProjectId = projectId, WorkflowRunId = Normalize(workflowRunId), WorkflowStepId = Normalize(workflowStepId) }, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowTransitionRecord>> ListByContinuationGateEvaluationAsync(Guid projectId, Guid workflowContinuationGateEvaluationId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation", new { ProjectId = projectId, WorkflowContinuationGateEvaluationId = workflowContinuationGateEvaluationId }, cancellationToken);

    public async Task<IReadOnlyList<WorkflowTransitionRecord>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt", new { ProjectId = projectId, SourceApplyReceiptId = sourceApplyReceiptId }, cancellationToken);

    public async Task<IReadOnlyList<WorkflowTransitionRecord>> ListByRollbackExecutionReceiptAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt", new { ProjectId = projectId, RollbackExecutionReceiptId = rollbackExecutionReceiptId }, cancellationToken);

    private async Task<IReadOnlyList<WorkflowTransitionRecord>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkflowTransitionRecordRow>(new CommandDefinition(
            procedure,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private static string Normalize(string value) => value.Trim();

    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class WorkflowTransitionRecordRow
    {
        public Guid WorkflowTransitionRecordId { get; init; }
        public Guid ProjectId { get; init; }
        public string WorkflowRunId { get; init; } = string.Empty;
        public string WorkflowStepId { get; init; } = string.Empty;
        public string TransitionKind { get; init; } = string.Empty;
        public string PreviousWorkflowStateHash { get; init; } = string.Empty;
        public string NewWorkflowStateHash { get; init; } = string.Empty;
        public string PreviousStepStateHash { get; init; } = string.Empty;
        public string NewStepStateHash { get; init; } = string.Empty;
        public string? PreviousStepId { get; init; }
        public string? NextStepId { get; init; }
        public Guid WorkflowContinuationGateEvaluationId { get; init; }
        public string WorkflowContinuationGateEvaluationHash { get; init; } = string.Empty;
        public Guid SourceApplyRequestId { get; init; }
        public string SourceApplyRequestHash { get; init; } = string.Empty;
        public Guid SourceApplyReceiptId { get; init; }
        public string SourceApplyReceiptHash { get; init; } = string.Empty;
        public Guid? RollbackExecutionReceiptId { get; init; }
        public string? RollbackExecutionReceiptHash { get; init; }
        public Guid? RollbackExecutionAuditReportId { get; init; }
        public string? RollbackExecutionAuditReportHash { get; init; }
        public bool WorkflowStateMutated { get; init; }
        public bool StepCompleted { get; init; }
        public bool NextStepStarted { get; init; }
        public bool ReleaseReadinessInferred { get; init; }
        public bool ReleaseApproved { get; init; }
        public bool SourceApplyExecuted { get; init; }
        public bool RollbackExecuted { get; init; }
        public DateTimeOffset TransitionedAtUtc { get; init; }
        public string WorkflowTransitionRecordHash { get; init; } = string.Empty;
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;

        public WorkflowTransitionRecord ToRecord() => new()
        {
            WorkflowTransitionRecordId = WorkflowTransitionRecordId,
            ProjectId = ProjectId,
            WorkflowRunId = WorkflowRunId,
            WorkflowStepId = WorkflowStepId,
            TransitionKind = TransitionKind,
            PreviousWorkflowStateHash = PreviousWorkflowStateHash,
            NewWorkflowStateHash = NewWorkflowStateHash,
            PreviousStepStateHash = PreviousStepStateHash,
            NewStepStateHash = NewStepStateHash,
            PreviousStepId = PreviousStepId,
            NextStepId = NextStepId,
            WorkflowContinuationGateEvaluationId = WorkflowContinuationGateEvaluationId,
            WorkflowContinuationGateEvaluationHash = WorkflowContinuationGateEvaluationHash,
            SourceApplyRequestId = SourceApplyRequestId,
            SourceApplyRequestHash = SourceApplyRequestHash,
            SourceApplyReceiptId = SourceApplyReceiptId,
            SourceApplyReceiptHash = SourceApplyReceiptHash,
            RollbackExecutionReceiptId = RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = RollbackExecutionReceiptHash,
            RollbackExecutionAuditReportId = RollbackExecutionAuditReportId,
            RollbackExecutionAuditReportHash = RollbackExecutionAuditReportHash,
            WorkflowStateMutated = WorkflowStateMutated,
            StepCompleted = StepCompleted,
            NextStepStarted = NextStepStarted,
            ReleaseReadinessInferred = ReleaseReadinessInferred,
            ReleaseApproved = ReleaseApproved,
            SourceApplyExecuted = SourceApplyExecuted,
            RollbackExecuted = RollbackExecuted,
            TransitionedAtUtc = TransitionedAtUtc,
            WorkflowTransitionRecordHash = WorkflowTransitionRecordHash,
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
}

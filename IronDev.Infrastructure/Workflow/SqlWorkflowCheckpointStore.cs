using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;

namespace IronDev.Infrastructure.Workflow;

public sealed class SqlWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly WorkflowCheckpointValidator _validator;

    public SqlWorkflowCheckpointStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new WorkflowCheckpointValidator())
    {
    }

    internal SqlWorkflowCheckpointStore(IDbConnectionFactory connectionFactory, WorkflowCheckpointValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<WorkflowCheckpoint> CreateAsync(WorkflowCheckpointCreateRequest request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCreate(request);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Workflow checkpoint create request is invalid: " + string.Join("; ", validation.Issues.Select(i => i.Code)));
        }

        var normalized = _validator.Normalize(request);
        var checkpointId = normalized.WorkflowCheckpointId ?? Guid.NewGuid();
        var evidenceJson = JsonSerializer.Serialize(
            normalized.EvidenceReferences.Select(e => new
            {
                EvidenceType = e.EvidenceType.ToString(),
                e.EvidenceId,
                e.EvidenceLabel,
                e.SafeSummary,
                AllowedUse = e.AllowedUse?.ToString(),
                e.GovernanceEventId,
                e.HandoffRecordId,
                e.ThoughtLedgerEntryId,
                e.GroundingReferenceId,
                e.WorkflowRunEvidenceReferenceId
            }),
            JsonOptions);
        var groundingJson = JsonSerializer.Serialize(
            normalized.GroundingReferences.Select(g => new
            {
                g.GroundingReferenceId,
                ClaimType = g.ClaimType.ToString(),
                g.ClaimId,
                g.SafeSummary
            }),
            JsonOptions);

        using var connection = _connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("WorkflowCheckpointId", checkpointId);
        parameters.Add("WorkflowRunId", normalized.WorkflowRunId);
        parameters.Add("WorkflowRunStepId", normalized.WorkflowRunStepId);
        parameters.Add("ProjectId", normalized.ProjectId);
        parameters.Add("CheckpointKey", normalized.CheckpointKey);
        parameters.Add("CheckpointName", normalized.CheckpointName);
        parameters.Add("CheckpointType", normalized.CheckpointType.ToString());
        parameters.Add("Status", normalized.Status.ToString());
        parameters.Add("SubjectType", normalized.SubjectType);
        parameters.Add("SubjectId", normalized.SubjectId);
        parameters.Add("SafeSummary", normalized.SafeSummary);
        parameters.Add("StateVersion", normalized.StateVersion);
        parameters.Add("StateJson", normalized.StateJson);
        parameters.Add("StateHashSha256", normalized.StateHashSha256);
        parameters.Add("CorrelationId", normalized.CorrelationId);
        parameters.Add("CausationId", normalized.CausationId);
        parameters.Add("CreatedByActorType", normalized.CreatedByActorType);
        parameters.Add("CreatedByActorId", normalized.CreatedByActorId);
        parameters.Add("MetadataVersion", normalized.MetadataVersion);
        parameters.Add("MetadataJson", normalized.MetadataJson);
        parameters.Add("EvidenceReferencesJson", evidenceJson);
        parameters.Add("GroundingReferencesJson", groundingJson);
        parameters.Add("GrantsApproval", normalized.GrantsApproval);
        parameters.Add("GrantsExecution", normalized.GrantsExecution);
        parameters.Add("MutatesSource", normalized.MutatesSource);
        parameters.Add("PromotesMemory", normalized.PromotesMemory);
        parameters.Add("StartsWorkflow", normalized.StartsWorkflow);
        parameters.Add("ContinuesWorkflow", normalized.ContinuesWorkflow);
        parameters.Add("ResumesWorkflow", normalized.ResumesWorkflow);
        parameters.Add("SatisfiesPolicy", normalized.SatisfiesPolicy);
        parameters.Add("TransfersAuthority", normalized.TransfersAuthority);
        parameters.Add("ApprovesRelease", normalized.ApprovesRelease);
        parameters.Add("CreatesAcceptedMemory", normalized.CreatesAcceptedMemory);

        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            "workflow.usp_WorkflowCheckpoint_Create",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var checkpointRow = await grid.ReadSingleAsync<WorkflowCheckpointRow>();
        var evidenceRows = (await grid.ReadAsync<WorkflowCheckpointEvidenceReferenceRow>()).ToList();
        var groundingRows = (await grid.ReadAsync<WorkflowCheckpointGroundingReferenceRow>()).ToList();
        return Map(checkpointRow, evidenceRows, groundingRows);
    }

    public async Task<WorkflowCheckpoint?> GetAsync(Guid projectId, Guid workflowRunId, Guid workflowCheckpointId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            "workflow.usp_WorkflowCheckpoint_Get",
            new { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowCheckpointId = workflowCheckpointId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var checkpointRow = await grid.ReadSingleOrDefaultAsync<WorkflowCheckpointRow>();
        var evidenceRows = (await grid.ReadAsync<WorkflowCheckpointEvidenceReferenceRow>()).ToList();
        var groundingRows = (await grid.ReadAsync<WorkflowCheckpointGroundingReferenceRow>()).ToList();
        return checkpointRow is null ? null : Map(checkpointRow, evidenceRows, groundingRows);
    }

    public Task<IReadOnlyList<WorkflowCheckpointSummary>> ListByRunAsync(Guid projectId, Guid workflowRunId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("workflow.usp_WorkflowCheckpoint_ListByRun", new { ProjectId = projectId, WorkflowRunId = workflowRunId, Take = WorkflowCheckpointValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<WorkflowCheckpointSummary>> ListByStepAsync(Guid projectId, Guid workflowRunId, Guid workflowRunStepId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("workflow.usp_WorkflowCheckpoint_ListByStep", new { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowRunStepId = workflowRunStepId, Take = WorkflowCheckpointValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<WorkflowCheckpointSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("workflow.usp_WorkflowCheckpoint_ListByCorrelation", new { ProjectId = projectId, CorrelationId = correlationId, Take = WorkflowCheckpointValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<WorkflowCheckpointSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("workflow.usp_WorkflowCheckpoint_ListBySubject", new { ProjectId = projectId, SubjectType = subjectType, SubjectId = subjectId, Take = WorkflowCheckpointValidator.NormalizeTake(take) }, cancellationToken);

    private async Task<IReadOnlyList<WorkflowCheckpointSummary>> QuerySummaryAsync(string procedureName, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkflowCheckpointSummaryRow>(new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return rows.Select(MapSummary).ToList();
    }

    private static WorkflowCheckpoint Map(
        WorkflowCheckpointRow row,
        IReadOnlyList<WorkflowCheckpointEvidenceReferenceRow> evidenceRows,
        IReadOnlyList<WorkflowCheckpointGroundingReferenceRow> groundingRows) => new()
    {
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        ProjectId = row.ProjectId,
        CheckpointKey = row.CheckpointKey,
        CheckpointName = row.CheckpointName,
        CheckpointType = Enum.Parse<WorkflowCheckpointType>(row.CheckpointType, ignoreCase: false),
        Status = Enum.Parse<WorkflowCheckpointStatus>(row.Status, ignoreCase: false),
        SubjectType = row.SubjectType,
        SubjectId = row.SubjectId,
        SafeSummary = row.SafeSummary,
        StateVersion = row.StateVersion,
        StateJson = row.StateJson,
        StateHashSha256 = row.StateHashSha256,
        CorrelationId = row.CorrelationId,
        CausationId = row.CausationId,
        CreatedByActorType = row.CreatedByActorType,
        CreatedByActorId = row.CreatedByActorId,
        MetadataVersion = row.MetadataVersion,
        MetadataJson = row.MetadataJson,
        GrantsApproval = row.GrantsApproval,
        GrantsExecution = row.GrantsExecution,
        MutatesSource = row.MutatesSource,
        PromotesMemory = row.PromotesMemory,
        StartsWorkflow = row.StartsWorkflow,
        ContinuesWorkflow = row.ContinuesWorkflow,
        ResumesWorkflow = row.ResumesWorkflow,
        SatisfiesPolicy = row.SatisfiesPolicy,
        TransfersAuthority = row.TransfersAuthority,
        ApprovesRelease = row.ApprovesRelease,
        CreatesAcceptedMemory = row.CreatesAcceptedMemory,
        EvidenceReferences = evidenceRows.Select(MapEvidence).ToList(),
        GroundingReferences = groundingRows.Select(MapGrounding).ToList(),
        CreatedUtc = row.CreatedUtc
    };

    private static WorkflowCheckpointSummary MapSummary(WorkflowCheckpointSummaryRow row) => new()
    {
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        ProjectId = row.ProjectId,
        CheckpointKey = row.CheckpointKey,
        CheckpointName = row.CheckpointName,
        CheckpointType = Enum.Parse<WorkflowCheckpointType>(row.CheckpointType, ignoreCase: false),
        Status = Enum.Parse<WorkflowCheckpointStatus>(row.Status, ignoreCase: false),
        SubjectType = row.SubjectType,
        SubjectId = row.SubjectId,
        StateHashSha256 = row.StateHashSha256,
        CorrelationId = row.CorrelationId,
        CausationId = row.CausationId,
        EvidenceReferenceCount = row.EvidenceReferenceCount,
        GroundingReferenceCount = row.GroundingReferenceCount,
        CreatedUtc = row.CreatedUtc
    };

    private static WorkflowCheckpointEvidenceReference MapEvidence(WorkflowCheckpointEvidenceReferenceRow row) => new()
    {
        WorkflowCheckpointEvidenceReferenceId = row.WorkflowCheckpointEvidenceReferenceId,
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        ProjectId = row.ProjectId,
        EvidenceType = Enum.Parse<WorkflowRunEvidenceType>(row.EvidenceType, ignoreCase: false),
        EvidenceId = row.EvidenceId,
        EvidenceLabel = row.EvidenceLabel,
        SafeSummary = row.SafeSummary,
        AllowedUse = string.IsNullOrWhiteSpace(row.AllowedUse) ? null : Enum.Parse<WorkflowRunEvidenceAllowedUse>(row.AllowedUse, ignoreCase: false),
        GovernanceEventId = row.GovernanceEventId,
        HandoffRecordId = row.HandoffRecordId,
        ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
        GroundingReferenceId = row.GroundingReferenceId,
        WorkflowRunEvidenceReferenceId = row.WorkflowRunEvidenceReferenceId,
        CreatedUtc = row.CreatedUtc
    };

    private static WorkflowCheckpointGroundingReference MapGrounding(WorkflowCheckpointGroundingReferenceRow row) => new()
    {
        WorkflowCheckpointGroundingReferenceId = row.WorkflowCheckpointGroundingReferenceId,
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        ProjectId = row.ProjectId,
        GroundingReferenceId = row.GroundingReferenceId,
        ClaimType = Enum.Parse<WorkflowRunGroundingClaimType>(row.ClaimType, ignoreCase: false),
        ClaimId = row.ClaimId,
        SafeSummary = row.SafeSummary,
        CreatedUtc = row.CreatedUtc
    };

    private sealed class WorkflowCheckpointRow
    {
        public Guid WorkflowCheckpointId { get; init; }
        public Guid WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid ProjectId { get; init; }
        public string CheckpointKey { get; init; } = string.Empty;
        public string CheckpointName { get; init; } = string.Empty;
        public string CheckpointType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public string? SafeSummary { get; init; }
        public int StateVersion { get; init; }
        public string StateJson { get; init; } = "{}";
        public string? StateHashSha256 { get; init; }
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string CreatedByActorType { get; init; } = string.Empty;
        public string CreatedByActorId { get; init; } = string.Empty;
        public int MetadataVersion { get; init; }
        public string MetadataJson { get; init; } = "{}";
        public bool GrantsApproval { get; init; }
        public bool GrantsExecution { get; init; }
        public bool MutatesSource { get; init; }
        public bool PromotesMemory { get; init; }
        public bool StartsWorkflow { get; init; }
        public bool ContinuesWorkflow { get; init; }
        public bool ResumesWorkflow { get; init; }
        public bool SatisfiesPolicy { get; init; }
        public bool TransfersAuthority { get; init; }
        public bool ApprovesRelease { get; init; }
        public bool CreatesAcceptedMemory { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class WorkflowCheckpointSummaryRow
    {
        public Guid WorkflowCheckpointId { get; init; }
        public Guid WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid ProjectId { get; init; }
        public string CheckpointKey { get; init; } = string.Empty;
        public string CheckpointName { get; init; } = string.Empty;
        public string CheckpointType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public string? StateHashSha256 { get; init; }
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public int EvidenceReferenceCount { get; init; }
        public int GroundingReferenceCount { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class WorkflowCheckpointEvidenceReferenceRow
    {
        public Guid WorkflowCheckpointEvidenceReferenceId { get; init; }
        public Guid WorkflowCheckpointId { get; init; }
        public Guid WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid ProjectId { get; init; }
        public string EvidenceType { get; init; } = string.Empty;
        public string EvidenceId { get; init; } = string.Empty;
        public string? EvidenceLabel { get; init; }
        public string? SafeSummary { get; init; }
        public string? AllowedUse { get; init; }
        public Guid? GovernanceEventId { get; init; }
        public Guid? HandoffRecordId { get; init; }
        public Guid? ThoughtLedgerEntryId { get; init; }
        public Guid? GroundingReferenceId { get; init; }
        public Guid? WorkflowRunEvidenceReferenceId { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class WorkflowCheckpointGroundingReferenceRow
    {
        public Guid WorkflowCheckpointGroundingReferenceId { get; init; }
        public Guid WorkflowCheckpointId { get; init; }
        public Guid WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid GroundingReferenceId { get; init; }
        public string ClaimType { get; init; } = string.Empty;
        public string ClaimId { get; init; } = string.Empty;
        public string? SafeSummary { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}

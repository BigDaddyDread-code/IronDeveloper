using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;

namespace IronDev.Infrastructure.Workflow;

public sealed class SqlWorkflowStepStore : IWorkflowStepStore
{
    private const string CreateProcedure = "workflow.usp_WorkflowStep_Create";
    private const string GetProcedure = "workflow.usp_WorkflowStep_Get";
    private const string ListByRunProcedure = "workflow.usp_WorkflowStep_ListByRun";
    private const string ListByCorrelationProcedure = "workflow.usp_WorkflowStep_ListByCorrelation";
    private const string ListBySubjectProcedure = "workflow.usp_WorkflowStep_ListBySubject";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly WorkflowStepValidator _validator;

    public SqlWorkflowStepStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new WorkflowStepValidator())
    {
    }

    internal SqlWorkflowStepStore(IDbConnectionFactory connectionFactory, WorkflowStepValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<WorkflowStep> CreateAsync(WorkflowStepCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalid(_validator.ValidateCreate(request), nameof(request));

        var normalized = _validator.Normalize(request);
        var workflowRunStepId = normalized.WorkflowRunStepId.GetValueOrDefault(Guid.NewGuid());
        var createdUtc = DateTimeOffset.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            CreateProcedure,
            new
            {
                WorkflowRunStepId = workflowRunStepId,
                normalized.WorkflowRunId,
                normalized.ProjectId,
                normalized.StepKey,
                normalized.StepName,
                StepType = normalized.StepType.ToString(),
                Status = normalized.Status.ToString(),
                normalized.AgentRole,
                normalized.AgentId,
                normalized.SubjectType,
                normalized.SubjectId,
                normalized.SafeSummary,
                normalized.SequenceNumber,
                normalized.CorrelationId,
                normalized.CausationId,
                normalized.MetadataVersion,
                normalized.MetadataJson,
                EvidenceReferencesJson = JsonSerializer.Serialize(normalized.EvidenceReferences.Select(evidence => new
                {
                    evidenceType = evidence.EvidenceType.ToString(),
                    evidence.EvidenceId,
                    evidence.EvidenceLabel,
                    evidence.SafeSummary,
                    allowedUse = evidence.AllowedUse?.ToString(),
                    evidence.GovernanceEventId,
                    evidence.AgentHandoffId,
                    evidence.ThoughtLedgerEntryId,
                    evidence.GroundingEvidenceReferenceId
                })),
                GroundingReferencesJson = JsonSerializer.Serialize(normalized.GroundingReferences.Select(grounding => new
                {
                    grounding.GroundingEvidenceReferenceId,
                    claimType = grounding.ClaimType.ToString(),
                    grounding.ClaimId,
                    grounding.SafeSummary
                })),
                CreatedUtc = createdUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var step = ReadStep(grid) ?? throw new InvalidOperationException("Workflow step creation stored procedure returned no row.");
        ThrowIfInvalid(_validator.ValidateMaterialized(step), nameof(request));
        return step;
    }

    public async Task<WorkflowStep?> GetAsync(Guid projectId, Guid workflowRunId, Guid workflowRunStepId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || workflowRunId == Guid.Empty || workflowRunStepId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            GetProcedure,
            new { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowRunStepId = workflowRunStepId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return ReadStep(grid);
    }

    public Task<IReadOnlyList<WorkflowStepSummary>> ListByRunAsync(Guid projectId, Guid workflowRunId, int take, CancellationToken cancellationToken = default)
    {
        if (workflowRunId == Guid.Empty)
            throw new ArgumentException("WorkflowRunId is required.", nameof(workflowRunId));

        return ListAsync(ListByRunProcedure, new { ProjectId = RequireProject(projectId), WorkflowRunId = workflowRunId, Take = WorkflowStepValidator.NormalizeTake(take) }, cancellationToken);
    }

    public Task<IReadOnlyList<WorkflowStepSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));

        return ListAsync(ListByCorrelationProcedure, new { ProjectId = RequireProject(projectId), CorrelationId = correlationId, Take = WorkflowStepValidator.NormalizeTake(take) }, cancellationToken);
    }

    public Task<IReadOnlyList<WorkflowStepSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectType))
            throw new ArgumentException("SubjectType is required.", nameof(subjectType));
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("SubjectId is required.", nameof(subjectId));

        return ListAsync(
            ListBySubjectProcedure,
            new { ProjectId = RequireProject(projectId), SubjectType = subjectType.Trim(), SubjectId = subjectId.Trim(), Take = WorkflowStepValidator.NormalizeTake(take) },
            cancellationToken);
    }

    private async Task<IReadOnlyList<WorkflowStepSummary>> ListAsync(string procedureName, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static WorkflowStep? ReadStep(SqlMapper.GridReader grid)
    {
        var row = grid.ReadSingleOrDefault<StepRow>();
        var evidence = grid.Read<EvidenceRow>().ToArray();
        var grounding = grid.Read<GroundingRow>().ToArray();
        return row?.ToStep(evidence, grounding);
    }

    private static Guid RequireProject(Guid projectId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId is required.", nameof(projectId));

        return projectId;
    }

    private static void ThrowIfInvalid(WorkflowRunValidationResult result, string paramName)
    {
        if (!result.IsValid)
            throw new ArgumentException(string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), paramName);
    }

    private sealed class StepRow
    {
        public Guid WorkflowRunStepId { get; set; }
        public Guid WorkflowRunId { get; set; }
        public Guid ProjectId { get; set; }
        public string StepKey { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public string StepType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? AgentRole { get; set; }
        public string? AgentId { get; set; }
        public string? SubjectType { get; set; }
        public string? SubjectId { get; set; }
        public string? SafeSummary { get; set; }
        public int SequenceNumber { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public int MetadataVersion { get; set; }
        public string MetadataJson { get; set; } = string.Empty;
        public bool GrantsApproval { get; set; }
        public bool GrantsExecution { get; set; }
        public bool MutatesSource { get; set; }
        public bool PromotesMemory { get; set; }
        public bool StartsWorkflow { get; set; }
        public bool ContinuesWorkflow { get; set; }
        public bool SatisfiesPolicy { get; set; }
        public bool TransfersAuthority { get; set; }
        public bool ApprovesRelease { get; set; }
        public bool CreatesAcceptedMemory { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public WorkflowStep ToStep(IReadOnlyList<EvidenceRow> evidence, IReadOnlyList<GroundingRow> grounding) =>
            new()
            {
                WorkflowRunStepId = WorkflowRunStepId,
                WorkflowRunId = WorkflowRunId,
                ProjectId = ProjectId,
                StepKey = StepKey,
                StepName = StepName,
                StepType = Enum.Parse<WorkflowRunStepType>(StepType),
                Status = Enum.Parse<WorkflowRunStatus>(Status),
                AgentRole = AgentRole,
                AgentId = AgentId,
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                SafeSummary = SafeSummary,
                SequenceNumber = SequenceNumber,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                MetadataVersion = MetadataVersion,
                MetadataJson = MetadataJson,
                GrantsApproval = GrantsApproval,
                GrantsExecution = GrantsExecution,
                MutatesSource = MutatesSource,
                PromotesMemory = PromotesMemory,
                StartsWorkflow = StartsWorkflow,
                ContinuesWorkflow = ContinuesWorkflow,
                SatisfiesPolicy = SatisfiesPolicy,
                TransfersAuthority = TransfersAuthority,
                ApprovesRelease = ApprovesRelease,
                CreatesAcceptedMemory = CreatesAcceptedMemory,
                EvidenceReferences = evidence.Select(row => row.ToEvidence()).ToArray(),
                GroundingReferences = grounding.Select(row => row.ToGrounding()).ToArray(),
                CreatedUtc = CreatedUtc
            };
    }

    private sealed class EvidenceRow
    {
        public Guid WorkflowRunEvidenceReferenceId { get; set; }
        public Guid WorkflowRunId { get; set; }
        public Guid? WorkflowRunStepId { get; set; }
        public string? StepKey { get; set; }
        public Guid ProjectId { get; set; }
        public string EvidenceType { get; set; } = string.Empty;
        public string EvidenceId { get; set; } = string.Empty;
        public string? EvidenceLabel { get; set; }
        public string? SafeSummary { get; set; }
        public string? AllowedUse { get; set; }
        public Guid? GovernanceEventId { get; set; }
        public Guid? AgentHandoffId { get; set; }
        public Guid? ThoughtLedgerEntryId { get; set; }
        public Guid? GroundingEvidenceReferenceId { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public WorkflowRunEvidenceReference ToEvidence() =>
            new()
            {
                WorkflowRunEvidenceReferenceId = WorkflowRunEvidenceReferenceId,
                WorkflowRunId = WorkflowRunId,
                WorkflowRunStepId = WorkflowRunStepId,
                StepKey = StepKey,
                ProjectId = ProjectId,
                EvidenceType = Enum.Parse<WorkflowRunEvidenceType>(EvidenceType),
                EvidenceId = EvidenceId,
                EvidenceLabel = EvidenceLabel,
                SafeSummary = SafeSummary,
                AllowedUse = string.IsNullOrWhiteSpace(AllowedUse) ? null : Enum.Parse<WorkflowRunEvidenceAllowedUse>(AllowedUse),
                GovernanceEventId = GovernanceEventId,
                AgentHandoffId = AgentHandoffId,
                ThoughtLedgerEntryId = ThoughtLedgerEntryId,
                GroundingEvidenceReferenceId = GroundingEvidenceReferenceId,
                CreatedUtc = CreatedUtc
            };
    }

    private sealed class GroundingRow
    {
        public Guid WorkflowRunGroundingReferenceId { get; set; }
        public Guid WorkflowRunId { get; set; }
        public Guid? WorkflowRunStepId { get; set; }
        public string? StepKey { get; set; }
        public Guid ProjectId { get; set; }
        public Guid GroundingEvidenceReferenceId { get; set; }
        public string ClaimType { get; set; } = string.Empty;
        public string ClaimId { get; set; } = string.Empty;
        public string? SafeSummary { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public WorkflowRunGroundingReference ToGrounding() =>
            new()
            {
                WorkflowRunGroundingReferenceId = WorkflowRunGroundingReferenceId,
                WorkflowRunId = WorkflowRunId,
                WorkflowRunStepId = WorkflowRunStepId,
                StepKey = StepKey,
                ProjectId = ProjectId,
                GroundingEvidenceReferenceId = GroundingEvidenceReferenceId,
                ClaimType = Enum.Parse<WorkflowRunGroundingClaimType>(ClaimType),
                ClaimId = ClaimId,
                SafeSummary = SafeSummary,
                CreatedUtc = CreatedUtc
            };
    }

    private sealed class SummaryRow
    {
        public Guid WorkflowRunStepId { get; set; }
        public Guid WorkflowRunId { get; set; }
        public Guid ProjectId { get; set; }
        public string StepKey { get; set; } = string.Empty;
        public string StepName { get; set; } = string.Empty;
        public string StepType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? AgentRole { get; set; }
        public string? AgentId { get; set; }
        public string? SubjectType { get; set; }
        public string? SubjectId { get; set; }
        public int SequenceNumber { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public int EvidenceReferenceCount { get; set; }
        public int GroundingReferenceCount { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public WorkflowStepSummary ToSummary() =>
            new()
            {
                WorkflowRunStepId = WorkflowRunStepId,
                WorkflowRunId = WorkflowRunId,
                ProjectId = ProjectId,
                StepKey = StepKey,
                StepName = StepName,
                StepType = Enum.Parse<WorkflowRunStepType>(StepType),
                Status = Enum.Parse<WorkflowRunStatus>(Status),
                AgentRole = AgentRole,
                AgentId = AgentId,
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                SequenceNumber = SequenceNumber,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                EvidenceReferenceCount = EvidenceReferenceCount,
                GroundingReferenceCount = GroundingReferenceCount,
                CreatedUtc = CreatedUtc
            };
    }
}

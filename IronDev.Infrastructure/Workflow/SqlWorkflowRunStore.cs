using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;

namespace IronDev.Infrastructure.Workflow;

public sealed class SqlWorkflowRunStore : IWorkflowRunStore
{
    private const string CreateProcedure = "workflow.usp_WorkflowRun_Create";
    private const string GetProcedure = "workflow.usp_WorkflowRun_Get";
    private const string ListByProjectProcedure = "workflow.usp_WorkflowRun_ListByProject";
    private const string ListByCorrelationProcedure = "workflow.usp_WorkflowRun_ListByCorrelation";
    private const string ListBySubjectProcedure = "workflow.usp_WorkflowRun_ListBySubject";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly WorkflowRunValidator _validator;

    public SqlWorkflowRunStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new WorkflowRunValidator())
    {
    }

    internal SqlWorkflowRunStore(IDbConnectionFactory connectionFactory, WorkflowRunValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<WorkflowRun> CreateAsync(WorkflowRunCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalid(_validator.ValidateCreate(request), nameof(request));

        var normalized = _validator.Normalize(request);
        var workflowRunId = normalized.WorkflowRunId.GetValueOrDefault(Guid.NewGuid());
        var createdUtc = DateTimeOffset.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            CreateProcedure,
            new
            {
                WorkflowRunId = workflowRunId,
                normalized.ProjectId,
                normalized.WorkflowType,
                normalized.WorkflowName,
                Status = normalized.Status.ToString(),
                normalized.SubjectType,
                normalized.SubjectId,
                normalized.SubjectSummary,
                normalized.CorrelationId,
                normalized.CausationId,
                normalized.CreatedByActorType,
                normalized.CreatedByActorId,
                normalized.MetadataVersion,
                normalized.MetadataJson,
                StepsJson = JsonSerializer.Serialize(normalized.Steps.Select(step => new
                {
                    step.StepKey,
                    step.StepName,
                    stepType = step.StepType.ToString(),
                    status = step.Status.ToString(),
                    step.AgentRole,
                    step.AgentId,
                    step.SubjectType,
                    step.SubjectId,
                    step.SafeSummary,
                    step.MetadataVersion,
                    step.MetadataJson
                })),
                EvidenceReferencesJson = JsonSerializer.Serialize(normalized.EvidenceReferences.Select(evidence => new
                {
                    evidence.StepKey,
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
                    grounding.StepKey,
                    grounding.GroundingEvidenceReferenceId,
                    claimType = grounding.ClaimType.ToString(),
                    grounding.ClaimId,
                    grounding.SafeSummary
                })),
                CreatedUtc = createdUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var run = ReadRun(grid) ?? throw new InvalidOperationException("Workflow run creation stored procedure returned no row.");
        ThrowIfInvalid(_validator.ValidateMaterialized(run), nameof(request));
        return run;
    }

    public async Task<WorkflowRun?> GetAsync(Guid projectId, Guid workflowRunId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || workflowRunId == Guid.Empty)
            return null;

        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            GetProcedure,
            new { ProjectId = projectId, WorkflowRunId = workflowRunId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return ReadRun(grid);
    }

    public Task<IReadOnlyList<WorkflowRunSummary>> ListByProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default) =>
        ListAsync(ListByProjectProcedure, new { ProjectId = RequireProject(projectId), Take = WorkflowRunValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<WorkflowRunSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));

        return ListAsync(ListByCorrelationProcedure, new { ProjectId = RequireProject(projectId), CorrelationId = correlationId, Take = WorkflowRunValidator.NormalizeTake(take) }, cancellationToken);
    }

    public Task<IReadOnlyList<WorkflowRunSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectType))
            throw new ArgumentException("SubjectType is required.", nameof(subjectType));

        if (string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("SubjectId is required.", nameof(subjectId));

        return ListAsync(
            ListBySubjectProcedure,
            new { ProjectId = RequireProject(projectId), SubjectType = subjectType.Trim(), SubjectId = subjectId.Trim(), Take = WorkflowRunValidator.NormalizeTake(take) },
            cancellationToken);
    }

    private async Task<IReadOnlyList<WorkflowRunSummary>> ListAsync(string procedureName, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static WorkflowRun? ReadRun(SqlMapper.GridReader grid)
    {
        var row = grid.ReadSingleOrDefault<RunRow>();
        var steps = grid.Read<StepRow>().ToArray();
        var evidence = grid.Read<EvidenceRow>().ToArray();
        var grounding = grid.Read<GroundingRow>().ToArray();
        return row?.ToWorkflowRun(steps, evidence, grounding);
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
            throw new ArgumentException(FormatIssues(result.Issues), paramName);
    }

    private static string FormatIssues(IReadOnlyList<WorkflowRunValidationIssue> issues) =>
        string.Join("; ", issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class RunRow
    {
        public Guid WorkflowRunId { get; set; }
        public Guid ProjectId { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public string? SubjectSummary { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public string CreatedByActorType { get; set; } = string.Empty;
        public string CreatedByActorId { get; set; } = string.Empty;
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

        public WorkflowRun ToWorkflowRun(
            IReadOnlyList<StepRow> steps,
            IReadOnlyList<EvidenceRow> evidence,
            IReadOnlyList<GroundingRow> grounding) =>
            new()
            {
                WorkflowRunId = WorkflowRunId,
                ProjectId = ProjectId,
                WorkflowType = WorkflowType,
                WorkflowName = WorkflowName,
                Status = Enum.Parse<WorkflowRunStatus>(Status),
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                SubjectSummary = SubjectSummary,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                CreatedByActorType = CreatedByActorType,
                CreatedByActorId = CreatedByActorId,
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
                Steps = steps.Select(row => row.ToStep()).ToArray(),
                EvidenceReferences = evidence.Select(row => row.ToEvidence()).ToArray(),
                GroundingReferences = grounding.Select(row => row.ToGrounding()).ToArray(),
                CreatedUtc = CreatedUtc
            };
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

        public WorkflowRunStep ToStep() =>
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
        public Guid WorkflowRunId { get; set; }
        public Guid ProjectId { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectId { get; set; } = string.Empty;
        public Guid? CorrelationId { get; set; }
        public Guid? CausationId { get; set; }
        public int StepCount { get; set; }
        public int EvidenceReferenceCount { get; set; }
        public int GroundingReferenceCount { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public WorkflowRunSummary ToSummary() =>
            new()
            {
                WorkflowRunId = WorkflowRunId,
                ProjectId = ProjectId,
                WorkflowType = WorkflowType,
                WorkflowName = WorkflowName,
                Status = Enum.Parse<WorkflowRunStatus>(Status),
                SubjectType = SubjectType,
                SubjectId = SubjectId,
                CorrelationId = CorrelationId,
                CausationId = CausationId,
                StepCount = StepCount,
                EvidenceReferenceCount = EvidenceReferenceCount,
                GroundingReferenceCount = GroundingReferenceCount,
                CreatedUtc = CreatedUtc
            };
    }
}

using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;
using Microsoft.Data.SqlClient;

namespace IronDev.Infrastructure.Workflow;

public sealed class SqlApplyDryRunStore : IApplyDryRunStore
{
    private const string CreateProcedure = "workflow.usp_ApplyDryRun_Create";
    private const string GetProcedure = "workflow.usp_ApplyDryRun_Get";
    private const string ListByWorkflowRunProcedure = "workflow.usp_ApplyDryRun_ListByWorkflowRun";
    private const string ListByControlledApplyPlanProcedure = "workflow.usp_ApplyDryRun_ListByControlledApplyPlan";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ApplyDryRunStoreValidator _validator;

    public SqlApplyDryRunStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new ApplyDryRunStoreValidator())
    {
    }

    internal SqlApplyDryRunStore(IDbConnectionFactory connectionFactory, ApplyDryRunStoreValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ApplyDryRunStoreResult> CreateAsync(ApplyDryRunCreateRequest? request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCreate(request);
        if (!validation.IsValid || validation.NormalizedRequest is null)
        {
            return ApplyDryRunStoreValidator.Invalid(validation.Issues, validation.HasUnsafeMaterial);
        }

        var normalized = validation.NormalizedRequest;
        using var connection = _connectionFactory.CreateConnection();

        try
        {
            var row = await connection.QuerySingleAsync<RecordRow>(new CommandDefinition(
                CreateProcedure,
                new
                {
                    normalized.DryRunId,
                    normalized.WorkflowRunId,
                    normalized.WorkflowStepId,
                    normalized.ControlledApplyPlanReferenceId,
                    normalized.SourceApplyApprovalRequirementReferenceId,
                    normalized.PatchProposalEvidencePackageReferenceId,
                    normalized.ProjectReferenceId,
                    normalized.TargetReferenceId,
                    Status = normalized.Status.ToString(),
                    OutcomeKind = normalized.OutcomeKind.ToString(),
                    normalized.SafeSummary,
                    EvidenceReferencesJson = SerializeReferences(normalized.EvidenceReferences),
                    GateReferencesJson = SerializeGateReferences(normalized.GateReferences),
                    ValidationReferencesJson = SerializeReferences(normalized.ValidationReferences),
                    RollbackReferencesJson = SerializeReferences(normalized.RollbackReferences),
                    RisksJson = SerializeRisks(normalized.Risks),
                    MissingEvidenceJson = SerializeMissingEvidence(normalized.MissingEvidence),
                    normalized.CorrelationId,
                    normalized.MetadataJson,
                    normalized.IsStoreRecordOnly,
                    normalized.IsDryRunPerformed,
                    normalized.IsSourceApply,
                    normalized.IsPatchApplication,
                    normalized.IsApproval,
                    normalized.IsApprovalSatisfied,
                    normalized.CanPerformDryRun,
                    normalized.CanApplySource,
                    normalized.CanMutateFiles,
                    normalized.CanReadSourceFiles,
                    normalized.CanRunCommand,
                    normalized.CanInvokeTool,
                    normalized.CanRunValidation,
                    normalized.CanRollback,
                    normalized.CanSatisfyPolicy,
                    normalized.CanTransitionWorkflow,
                    normalized.CanPromoteMemory,
                    normalized.CanActivateRetrieval
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

            return ApplyDryRunStoreValidator.Stored(row.ToRecord());
        }
        catch (SqlException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                      ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyDryRunStoreValidator.Duplicate(
            [
                new ApplyDryRunStoreIssue
                {
                    Reason = ApplyDryRunReason.DuplicateRejected,
                    Message = "Apply dry-run record already exists.",
                    Field = nameof(ApplyDryRunCreateRequest.DryRunId)
                }
            ]);
        }
    }

    public async Task<ApplyDryRunRecord?> GetByIdAsync(string dryRunId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dryRunId))
            return null;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition(
            GetProcedure,
            new { DryRunId = dryRunId.Trim() },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public Task<IReadOnlyList<ApplyDryRunSummary>> ListByWorkflowRunAsync(string workflowRunId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowRunId))
            return Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>([]);

        return ListAsync(ListByWorkflowRunProcedure, new { WorkflowRunId = workflowRunId.Trim(), Take = ApplyDryRunStoreValidator.NormalizeTake(take) }, cancellationToken);
    }

    public Task<IReadOnlyList<ApplyDryRunSummary>> ListByControlledApplyPlanAsync(string controlledApplyPlanReferenceId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(controlledApplyPlanReferenceId))
            return Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>([]);

        return ListAsync(ListByControlledApplyPlanProcedure, new { ControlledApplyPlanReferenceId = controlledApplyPlanReferenceId.Trim(), Take = ApplyDryRunStoreValidator.NormalizeTake(take) }, cancellationToken);
    }

    private async Task<IReadOnlyList<ApplyDryRunSummary>> ListAsync(string procedureName, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    private static string SerializeReferences(IReadOnlyList<ApplyDryRunReference> references) =>
        JsonSerializer.Serialize(references.Select(reference => new
        {
            kind = reference.Kind.ToString(),
            reference.ReferenceId,
            reference.SafeSummary
        }), JsonOptions);

    private static string SerializeGateReferences(IReadOnlyList<ApplyDryRunGateReference> references) =>
        JsonSerializer.Serialize(references.Select(reference => new
        {
            kind = reference.Kind.ToString(),
            reference.ReferenceId,
            reference.SafeSummary
        }), JsonOptions);

    private static string SerializeRisks(IReadOnlyList<ApplyDryRunRisk> risks) =>
        JsonSerializer.Serialize(risks.Select(risk => new
        {
            kind = risk.Kind.ToString(),
            severity = risk.Severity.ToString(),
            risk.RiskId,
            risk.SafeSummary
        }), JsonOptions);

    private static string SerializeMissingEvidence(IReadOnlyList<ApplyDryRunMissingEvidence> missingEvidence) =>
        JsonSerializer.Serialize(missingEvidence.Select(missing => new
        {
            kind = missing.Kind.ToString(),
            missing.ReferenceId,
            missing.SafeSummary
        }), JsonOptions);

    private sealed class RecordRow
    {
        public string DryRunId { get; set; } = string.Empty;
        public string WorkflowRunId { get; set; } = string.Empty;
        public string WorkflowStepId { get; set; } = string.Empty;
        public string ControlledApplyPlanReferenceId { get; set; } = string.Empty;
        public string SourceApplyApprovalRequirementReferenceId { get; set; } = string.Empty;
        public string PatchProposalEvidencePackageReferenceId { get; set; } = string.Empty;
        public string ProjectReferenceId { get; set; } = string.Empty;
        public string TargetReferenceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string OutcomeKind { get; set; } = string.Empty;
        public string SafeSummary { get; set; } = string.Empty;
        public string EvidenceReferencesJson { get; set; } = "[]";
        public string GateReferencesJson { get; set; } = "[]";
        public string ValidationReferencesJson { get; set; } = "[]";
        public string RollbackReferencesJson { get; set; } = "[]";
        public string RisksJson { get; set; } = "[]";
        public string MissingEvidenceJson { get; set; } = "[]";
        public string CorrelationId { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public bool IsStoreRecordOnly { get; set; }
        public bool IsDryRunPerformed { get; set; }
        public bool IsSourceApply { get; set; }
        public bool IsPatchApplication { get; set; }
        public bool IsApproval { get; set; }
        public bool IsApprovalSatisfied { get; set; }
        public bool CanPerformDryRun { get; set; }
        public bool CanApplySource { get; set; }
        public bool CanMutateFiles { get; set; }
        public bool CanReadSourceFiles { get; set; }
        public bool CanRunCommand { get; set; }
        public bool CanInvokeTool { get; set; }
        public bool CanRunValidation { get; set; }
        public bool CanRollback { get; set; }
        public bool CanSatisfyPolicy { get; set; }
        public bool CanTransitionWorkflow { get; set; }
        public bool CanPromoteMemory { get; set; }
        public bool CanActivateRetrieval { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public ApplyDryRunRecord ToRecord() =>
            new()
            {
                DryRunId = DryRunId,
                WorkflowRunId = WorkflowRunId,
                WorkflowStepId = WorkflowStepId,
                ControlledApplyPlanReferenceId = ControlledApplyPlanReferenceId,
                SourceApplyApprovalRequirementReferenceId = SourceApplyApprovalRequirementReferenceId,
                PatchProposalEvidencePackageReferenceId = PatchProposalEvidencePackageReferenceId,
                ProjectReferenceId = ProjectReferenceId,
                TargetReferenceId = TargetReferenceId,
                Status = Enum.Parse<ApplyDryRunRecordStatus>(Status),
                OutcomeKind = Enum.Parse<ApplyDryRunOutcomeKind>(OutcomeKind),
                SafeSummary = SafeSummary,
                EvidenceReferences = DeserializeReferences(EvidenceReferencesJson),
                GateReferences = DeserializeGateReferences(GateReferencesJson),
                ValidationReferences = DeserializeReferences(ValidationReferencesJson),
                RollbackReferences = DeserializeReferences(RollbackReferencesJson),
                Risks = DeserializeRisks(RisksJson),
                MissingEvidence = DeserializeMissingEvidence(MissingEvidenceJson),
                CorrelationId = CorrelationId,
                MetadataJson = MetadataJson,
                IsStoreRecordOnly = IsStoreRecordOnly,
                IsDryRunPerformed = IsDryRunPerformed,
                IsSourceApply = IsSourceApply,
                IsPatchApplication = IsPatchApplication,
                IsApproval = IsApproval,
                IsApprovalSatisfied = IsApprovalSatisfied,
                CanPerformDryRun = CanPerformDryRun,
                CanApplySource = CanApplySource,
                CanMutateFiles = CanMutateFiles,
                CanReadSourceFiles = CanReadSourceFiles,
                CanRunCommand = CanRunCommand,
                CanInvokeTool = CanInvokeTool,
                CanRunValidation = CanRunValidation,
                CanRollback = CanRollback,
                CanSatisfyPolicy = CanSatisfyPolicy,
                CanTransitionWorkflow = CanTransitionWorkflow,
                CanPromoteMemory = CanPromoteMemory,
                CanActivateRetrieval = CanActivateRetrieval,
                CreatedUtc = CreatedUtc
            };
    }

    private sealed class SummaryRow
    {
        public string DryRunId { get; set; } = string.Empty;
        public string WorkflowRunId { get; set; } = string.Empty;
        public string WorkflowStepId { get; set; } = string.Empty;
        public string ControlledApplyPlanReferenceId { get; set; } = string.Empty;
        public string ProjectReferenceId { get; set; } = string.Empty;
        public string TargetReferenceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string OutcomeKind { get; set; } = string.Empty;
        public int EvidenceReferenceCount { get; set; }
        public int GateReferenceCount { get; set; }
        public int ValidationReferenceCount { get; set; }
        public int RollbackReferenceCount { get; set; }
        public int RiskCount { get; set; }
        public int MissingEvidenceCount { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }

        public ApplyDryRunSummary ToSummary() =>
            new()
            {
                DryRunId = DryRunId,
                WorkflowRunId = WorkflowRunId,
                WorkflowStepId = WorkflowStepId,
                ControlledApplyPlanReferenceId = ControlledApplyPlanReferenceId,
                ProjectReferenceId = ProjectReferenceId,
                TargetReferenceId = TargetReferenceId,
                Status = Enum.Parse<ApplyDryRunRecordStatus>(Status),
                OutcomeKind = Enum.Parse<ApplyDryRunOutcomeKind>(OutcomeKind),
                EvidenceReferenceCount = EvidenceReferenceCount,
                GateReferenceCount = GateReferenceCount,
                ValidationReferenceCount = ValidationReferenceCount,
                RollbackReferenceCount = RollbackReferenceCount,
                RiskCount = RiskCount,
                MissingEvidenceCount = MissingEvidenceCount,
                CreatedUtc = CreatedUtc
            };
    }

    private static IReadOnlyList<ApplyDryRunReference> DeserializeReferences(string json) =>
        JsonSerializer.Deserialize<ReferenceDto[]>(json, JsonOptions)?.Select(reference => new ApplyDryRunReference
        {
            Kind = Enum.Parse<ApplyDryRunReferenceKind>(reference.Kind),
            ReferenceId = reference.ReferenceId,
            SafeSummary = reference.SafeSummary
        }).ToArray() ?? [];

    private static IReadOnlyList<ApplyDryRunGateReference> DeserializeGateReferences(string json) =>
        JsonSerializer.Deserialize<GateReferenceDto[]>(json, JsonOptions)?.Select(reference => new ApplyDryRunGateReference
        {
            Kind = Enum.Parse<ApplyDryRunGateKind>(reference.Kind),
            ReferenceId = reference.ReferenceId,
            SafeSummary = reference.SafeSummary
        }).ToArray() ?? [];

    private static IReadOnlyList<ApplyDryRunRisk> DeserializeRisks(string json) =>
        JsonSerializer.Deserialize<RiskDto[]>(json, JsonOptions)?.Select(risk => new ApplyDryRunRisk
        {
            Kind = Enum.Parse<ApplyDryRunRiskKind>(risk.Kind),
            Severity = Enum.Parse<ApplyDryRunRiskSeverity>(risk.Severity),
            RiskId = risk.RiskId,
            SafeSummary = risk.SafeSummary
        }).ToArray() ?? [];

    private static IReadOnlyList<ApplyDryRunMissingEvidence> DeserializeMissingEvidence(string json) =>
        JsonSerializer.Deserialize<MissingDto[]>(json, JsonOptions)?.Select(missing => new ApplyDryRunMissingEvidence
        {
            Kind = Enum.Parse<ApplyDryRunReferenceKind>(missing.Kind),
            ReferenceId = missing.ReferenceId,
            SafeSummary = missing.SafeSummary
        }).ToArray() ?? [];

    private sealed record ReferenceDto(string Kind, string ReferenceId, string SafeSummary);
    private sealed record GateReferenceDto(string Kind, string ReferenceId, string SafeSummary);
    private sealed record RiskDto(string Kind, string Severity, string RiskId, string SafeSummary);
    private sealed record MissingDto(string Kind, string ReferenceId, string SafeSummary);
}

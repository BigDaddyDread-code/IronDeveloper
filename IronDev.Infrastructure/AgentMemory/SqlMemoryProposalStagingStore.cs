using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlMemoryProposalStagingStore : IMemoryProposalStagingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly MemoryProposalValidator _validator;

    public SqlMemoryProposalStagingStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new MemoryProposalValidator())
    {
    }

    internal SqlMemoryProposalStagingStore(IDbConnectionFactory connectionFactory, MemoryProposalValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<MemoryProposal> CreateAsync(MemoryProposalCreateRequest request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCreate(request);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Memory proposal create request is invalid: " + string.Join("; ", validation.Issues.Select(issue => issue.Code)));
        }

        var normalized = _validator.Normalize(request);
        var memoryProposalId = normalized.MemoryProposalId ?? Guid.NewGuid();

        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            "memory.usp_MemoryProposal_Create",
            BuildCreateParameters(normalized, memoryProposalId),
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var proposal = await grid.ReadSingleAsync<MemoryProposalRow>();
        var evidence = (await grid.ReadAsync<MemoryProposalEvidenceReferenceRow>()).ToList();
        var grounding = (await grid.ReadAsync<MemoryProposalGroundingReferenceRow>()).ToList();
        var workflow = (await grid.ReadAsync<MemoryProposalWorkflowReferenceRow>()).ToList();
        return Map(proposal, evidence, grounding, workflow);
    }

    public async Task<MemoryProposal?> GetAsync(Guid projectId, Guid memoryProposalId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            "memory.usp_MemoryProposal_Get",
            new { ProjectId = projectId, MemoryProposalId = memoryProposalId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var proposal = await grid.ReadSingleOrDefaultAsync<MemoryProposalRow>();
        var evidence = (await grid.ReadAsync<MemoryProposalEvidenceReferenceRow>()).ToList();
        var grounding = (await grid.ReadAsync<MemoryProposalGroundingReferenceRow>()).ToList();
        var workflow = (await grid.ReadAsync<MemoryProposalWorkflowReferenceRow>()).ToList();
        return proposal is null ? null : Map(proposal, evidence, grounding, workflow);
    }

    public Task<IReadOnlyList<MemoryProposalSummary>> ListByProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("memory.usp_MemoryProposal_ListByProject", new { ProjectId = projectId, Take = MemoryProposalValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<MemoryProposalSummary>> ListByStatusAsync(Guid projectId, MemoryProposalStatus status, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("memory.usp_MemoryProposal_ListByStatus", new { ProjectId = projectId, ProposalStatus = status.ToString(), Take = MemoryProposalValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<MemoryProposalSummary>> ListByWorkflowRunAsync(Guid projectId, Guid workflowRunId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("memory.usp_MemoryProposal_ListByWorkflowRun", new { ProjectId = projectId, WorkflowRunId = workflowRunId, Take = MemoryProposalValidator.NormalizeTake(take) }, cancellationToken);

    public Task<IReadOnlyList<MemoryProposalSummary>> ListBySourceAsync(Guid projectId, string sourceType, string sourceId, int take, CancellationToken cancellationToken = default) =>
        QuerySummaryAsync("memory.usp_MemoryProposal_ListBySource", new { ProjectId = projectId, SourceType = sourceType, SourceId = sourceId, Take = MemoryProposalValidator.NormalizeTake(take) }, cancellationToken);

    private async Task<IReadOnlyList<MemoryProposalSummary>> QuerySummaryAsync(string procedureName, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<MemoryProposalSummaryRow>(new CommandDefinition(
            procedureName,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return rows.Select(MapSummary).ToList();
    }

    private static DynamicParameters BuildCreateParameters(MemoryProposalCreateRequest request, Guid memoryProposalId)
    {
        var evidenceJson = JsonSerializer.Serialize(
            request.EvidenceReferences.Select(e => new
            {
                e.MemoryProposalEvidenceReferenceId,
                EvidenceType = e.EvidenceType.ToString(),
                e.EvidenceId,
                e.EvidenceLabel,
                e.SafeSummary,
                AllowedUse = e.AllowedUse?.ToString(),
                e.GovernanceEventId,
                e.WorkflowRunEvidenceReferenceId,
                e.WorkflowRunStepId,
                e.WorkflowCheckpointId,
                e.AgentHandoffId,
                e.ThoughtLedgerEntryId
            }),
            JsonOptions);

        var groundingJson = JsonSerializer.Serialize(
            request.GroundingReferences.Select(g => new
            {
                g.MemoryProposalGroundingReferenceId,
                g.GroundingReferenceId,
                ClaimType = g.ClaimType.ToString(),
                g.ClaimId,
                g.SafeSummary
            }),
            JsonOptions);

        var workflowJson = JsonSerializer.Serialize(
            request.WorkflowReferences.Select(w => new
            {
                w.MemoryProposalWorkflowReferenceId,
                w.WorkflowRunId,
                w.WorkflowRunStepId,
                w.WorkflowCheckpointId,
                ReferenceType = w.ReferenceType.ToString(),
                w.SafeSummary
            }),
            JsonOptions);

        var parameters = new DynamicParameters();
        parameters.Add("MemoryProposalId", memoryProposalId);
        parameters.Add("TenantId", request.TenantId);
        parameters.Add("ProjectId", request.ProjectId);
        parameters.Add("ProposalKey", request.ProposalKey);
        parameters.Add("ProposalType", request.ProposalType.ToString());
        parameters.Add("TargetMemoryScope", request.TargetMemoryScope.ToString());
        parameters.Add("ProposalStatus", request.ProposalStatus.ToString());
        parameters.Add("SourceType", request.SourceType);
        parameters.Add("SourceId", request.SourceId);
        parameters.Add("SourceAgentRole", request.SourceAgentRole);
        parameters.Add("SourceAgentId", request.SourceAgentId);
        parameters.Add("SubjectType", request.SubjectType);
        parameters.Add("SubjectId", request.SubjectId);
        parameters.Add("SafeProposedMemory", request.SafeProposedMemory);
        parameters.Add("SafeRationaleSummary", request.SafeRationaleSummary);
        parameters.Add("SafeRiskSummary", request.SafeRiskSummary);
        parameters.Add("ConfidenceLabel", request.ConfidenceLabel);
        parameters.Add("ConfidentialityLabel", request.ConfidentialityLabel.ToString());
        parameters.Add("SanitizationStatus", request.SanitizationStatus.ToString());
        parameters.Add("WorkflowRunId", request.WorkflowRunId);
        parameters.Add("WorkflowRunStepId", request.WorkflowRunStepId);
        parameters.Add("WorkflowCheckpointId", request.WorkflowCheckpointId);
        parameters.Add("CorrelationId", request.CorrelationId);
        parameters.Add("CausationId", request.CausationId);
        parameters.Add("CreatedByActorType", request.CreatedByActorType);
        parameters.Add("CreatedByActorId", request.CreatedByActorId);
        parameters.Add("MetadataVersion", request.MetadataVersion);
        parameters.Add("MetadataJson", request.MetadataJson);
        parameters.Add("IsAcceptedMemory", request.IsAcceptedMemory);
        parameters.Add("CreatesAcceptedMemory", request.CreatesAcceptedMemory);
        parameters.Add("PromotesMemory", request.PromotesMemory);
        parameters.Add("WritesCollectiveMemory", request.WritesCollectiveMemory);
        parameters.Add("WritesAgentMemory", request.WritesAgentMemory);
        parameters.Add("WritesVectorIndex", request.WritesVectorIndex);
        parameters.Add("IsRetrievalAuthority", request.IsRetrievalAuthority);
        parameters.Add("IsPolicy", request.IsPolicy);
        parameters.Add("IsApproval", request.IsApproval);
        parameters.Add("SatisfiesPolicy", request.SatisfiesPolicy);
        parameters.Add("GrantsApproval", request.GrantsApproval);
        parameters.Add("GrantsExecution", request.GrantsExecution);
        parameters.Add("StartsWorkflow", request.StartsWorkflow);
        parameters.Add("ContinuesWorkflow", request.ContinuesWorkflow);
        parameters.Add("MutatesSource", request.MutatesSource);
        parameters.Add("ApprovesRelease", request.ApprovesRelease);
        parameters.Add("EvidenceReferencesJson", evidenceJson);
        parameters.Add("GroundingReferencesJson", groundingJson);
        parameters.Add("WorkflowReferencesJson", workflowJson);
        return parameters;
    }

    private static MemoryProposal Map(
        MemoryProposalRow row,
        IReadOnlyList<MemoryProposalEvidenceReferenceRow> evidenceRows,
        IReadOnlyList<MemoryProposalGroundingReferenceRow> groundingRows,
        IReadOnlyList<MemoryProposalWorkflowReferenceRow> workflowRows) => new()
    {
        MemoryProposalId = row.MemoryProposalId,
        TenantId = row.TenantId,
        ProjectId = row.ProjectId,
        ProposalKey = row.ProposalKey,
        ProposalType = Enum.Parse<MemoryProposalType>(row.ProposalType),
        TargetMemoryScope = Enum.Parse<MemoryProposalTargetScope>(row.TargetMemoryScope),
        ProposalStatus = Enum.Parse<MemoryProposalStatus>(row.ProposalStatus),
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceAgentRole = row.SourceAgentRole,
        SourceAgentId = row.SourceAgentId,
        SubjectType = row.SubjectType,
        SubjectId = row.SubjectId,
        SafeProposedMemory = row.SafeProposedMemory,
        SafeRationaleSummary = row.SafeRationaleSummary,
        SafeRiskSummary = row.SafeRiskSummary,
        ConfidenceLabel = row.ConfidenceLabel,
        ConfidentialityLabel = Enum.Parse<MemoryProposalConfidentialityLabel>(row.ConfidentialityLabel),
        SanitizationStatus = Enum.Parse<MemoryProposalSanitizationStatus>(row.SanitizationStatus),
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        CorrelationId = row.CorrelationId,
        CausationId = row.CausationId,
        CreatedByActorType = row.CreatedByActorType,
        CreatedByActorId = row.CreatedByActorId,
        MetadataVersion = row.MetadataVersion,
        MetadataJson = row.MetadataJson,
        IsAcceptedMemory = row.IsAcceptedMemory,
        CreatesAcceptedMemory = row.CreatesAcceptedMemory,
        PromotesMemory = row.PromotesMemory,
        WritesCollectiveMemory = row.WritesCollectiveMemory,
        WritesAgentMemory = row.WritesAgentMemory,
        WritesVectorIndex = row.WritesVectorIndex,
        IsRetrievalAuthority = row.IsRetrievalAuthority,
        IsPolicy = row.IsPolicy,
        IsApproval = row.IsApproval,
        SatisfiesPolicy = row.SatisfiesPolicy,
        GrantsApproval = row.GrantsApproval,
        GrantsExecution = row.GrantsExecution,
        StartsWorkflow = row.StartsWorkflow,
        ContinuesWorkflow = row.ContinuesWorkflow,
        MutatesSource = row.MutatesSource,
        ApprovesRelease = row.ApprovesRelease,
        EvidenceReferences = evidenceRows.Select(MapEvidence).ToList(),
        GroundingReferences = groundingRows.Select(MapGrounding).ToList(),
        WorkflowReferences = workflowRows.Select(MapWorkflow).ToList(),
        CreatedUtc = row.CreatedUtc
    };

    private static MemoryProposalSummary MapSummary(MemoryProposalSummaryRow row) => new()
    {
        MemoryProposalId = row.MemoryProposalId,
        ProjectId = row.ProjectId,
        ProposalKey = row.ProposalKey,
        ProposalType = Enum.Parse<MemoryProposalType>(row.ProposalType),
        TargetMemoryScope = Enum.Parse<MemoryProposalTargetScope>(row.TargetMemoryScope),
        ProposalStatus = Enum.Parse<MemoryProposalStatus>(row.ProposalStatus),
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SubjectType = row.SubjectType,
        SubjectId = row.SubjectId,
        SafeProposedMemory = row.SafeProposedMemory,
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        CorrelationId = row.CorrelationId,
        EvidenceReferenceCount = row.EvidenceReferenceCount,
        GroundingReferenceCount = row.GroundingReferenceCount,
        WorkflowReferenceCount = row.WorkflowReferenceCount,
        CreatedUtc = row.CreatedUtc
    };

    private static MemoryProposalEvidenceReference MapEvidence(MemoryProposalEvidenceReferenceRow row) => new()
    {
        MemoryProposalEvidenceReferenceId = row.MemoryProposalEvidenceReferenceId,
        MemoryProposalId = row.MemoryProposalId,
        ProjectId = row.ProjectId,
        EvidenceType = Enum.Parse<MemoryProposalEvidenceType>(row.EvidenceType),
        EvidenceId = row.EvidenceId,
        EvidenceLabel = row.EvidenceLabel,
        SafeSummary = row.SafeSummary,
        AllowedUse = string.IsNullOrWhiteSpace(row.AllowedUse) ? null : Enum.Parse<MemoryProposalEvidenceAllowedUse>(row.AllowedUse),
        GovernanceEventId = row.GovernanceEventId,
        WorkflowRunEvidenceReferenceId = row.WorkflowRunEvidenceReferenceId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        AgentHandoffId = row.AgentHandoffId,
        ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
        CreatedUtc = row.CreatedUtc
    };

    private static MemoryProposalGroundingReference MapGrounding(MemoryProposalGroundingReferenceRow row) => new()
    {
        MemoryProposalGroundingReferenceId = row.MemoryProposalGroundingReferenceId,
        MemoryProposalId = row.MemoryProposalId,
        ProjectId = row.ProjectId,
        GroundingReferenceId = row.GroundingReferenceId,
        ClaimType = Enum.Parse<MemoryProposalGroundingClaimType>(row.ClaimType),
        ClaimId = row.ClaimId,
        SafeSummary = row.SafeSummary,
        CreatedUtc = row.CreatedUtc
    };

    private static MemoryProposalWorkflowReference MapWorkflow(MemoryProposalWorkflowReferenceRow row) => new()
    {
        MemoryProposalWorkflowReferenceId = row.MemoryProposalWorkflowReferenceId,
        MemoryProposalId = row.MemoryProposalId,
        ProjectId = row.ProjectId,
        WorkflowRunId = row.WorkflowRunId,
        WorkflowRunStepId = row.WorkflowRunStepId,
        WorkflowCheckpointId = row.WorkflowCheckpointId,
        ReferenceType = Enum.Parse<MemoryProposalWorkflowReferenceType>(row.ReferenceType),
        SafeSummary = row.SafeSummary,
        CreatedUtc = row.CreatedUtc
    };

    private sealed class MemoryProposalRow
    {
        public Guid MemoryProposalId { get; init; }
        public Guid? TenantId { get; init; }
        public Guid ProjectId { get; init; }
        public string ProposalKey { get; init; } = string.Empty;
        public string ProposalType { get; init; } = string.Empty;
        public string TargetMemoryScope { get; init; } = string.Empty;
        public string ProposalStatus { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public string? SourceId { get; init; }
        public string? SourceAgentRole { get; init; }
        public string? SourceAgentId { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public string SafeProposedMemory { get; init; } = string.Empty;
        public string? SafeRationaleSummary { get; init; }
        public string? SafeRiskSummary { get; init; }
        public string? ConfidenceLabel { get; init; }
        public string ConfidentialityLabel { get; init; } = string.Empty;
        public string SanitizationStatus { get; init; } = string.Empty;
        public Guid? WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid? WorkflowCheckpointId { get; init; }
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string CreatedByActorType { get; init; } = string.Empty;
        public string CreatedByActorId { get; init; } = string.Empty;
        public int MetadataVersion { get; init; }
        public string MetadataJson { get; init; } = "{}";
        public bool IsAcceptedMemory { get; init; }
        public bool CreatesAcceptedMemory { get; init; }
        public bool PromotesMemory { get; init; }
        public bool WritesCollectiveMemory { get; init; }
        public bool WritesAgentMemory { get; init; }
        public bool WritesVectorIndex { get; init; }
        public bool IsRetrievalAuthority { get; init; }
        public bool IsPolicy { get; init; }
        public bool IsApproval { get; init; }
        public bool SatisfiesPolicy { get; init; }
        public bool GrantsApproval { get; init; }
        public bool GrantsExecution { get; init; }
        public bool StartsWorkflow { get; init; }
        public bool ContinuesWorkflow { get; init; }
        public bool MutatesSource { get; init; }
        public bool ApprovesRelease { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class MemoryProposalSummaryRow
    {
        public Guid MemoryProposalId { get; init; }
        public Guid ProjectId { get; init; }
        public string ProposalKey { get; init; } = string.Empty;
        public string ProposalType { get; init; } = string.Empty;
        public string TargetMemoryScope { get; init; } = string.Empty;
        public string ProposalStatus { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public string? SourceId { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public string SafeProposedMemory { get; init; } = string.Empty;
        public Guid? WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid? WorkflowCheckpointId { get; init; }
        public Guid? CorrelationId { get; init; }
        public int EvidenceReferenceCount { get; init; }
        public int GroundingReferenceCount { get; init; }
        public int WorkflowReferenceCount { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class MemoryProposalEvidenceReferenceRow
    {
        public Guid MemoryProposalEvidenceReferenceId { get; init; }
        public Guid MemoryProposalId { get; init; }
        public Guid ProjectId { get; init; }
        public string EvidenceType { get; init; } = string.Empty;
        public string EvidenceId { get; init; } = string.Empty;
        public string? EvidenceLabel { get; init; }
        public string? SafeSummary { get; init; }
        public string? AllowedUse { get; init; }
        public Guid? GovernanceEventId { get; init; }
        public Guid? WorkflowRunEvidenceReferenceId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid? WorkflowCheckpointId { get; init; }
        public Guid? AgentHandoffId { get; init; }
        public Guid? ThoughtLedgerEntryId { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class MemoryProposalGroundingReferenceRow
    {
        public Guid MemoryProposalGroundingReferenceId { get; init; }
        public Guid MemoryProposalId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid GroundingReferenceId { get; init; }
        public string ClaimType { get; init; } = string.Empty;
        public string ClaimId { get; init; } = string.Empty;
        public string? SafeSummary { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }

    private sealed class MemoryProposalWorkflowReferenceRow
    {
        public Guid MemoryProposalWorkflowReferenceId { get; init; }
        public Guid MemoryProposalId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid? WorkflowRunId { get; init; }
        public Guid? WorkflowRunStepId { get; init; }
        public Guid? WorkflowCheckpointId { get; init; }
        public string ReferenceType { get; init; } = string.Empty;
        public string? SafeSummary { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}

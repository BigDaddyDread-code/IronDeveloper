using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlToolGateDecisionStore : IToolGateDecisionStore
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ToolGateDecisionValidator _validator;

    public SqlToolGateDecisionStore(IDbConnectionFactory connectionFactory, ToolGateDecisionValidator? validator = null)
    {
        _connectionFactory = connectionFactory;
        _validator = validator ?? new ToolGateDecisionValidator();
    }

    public async Task<ToolGateDecisionReadModel> RecordAsync(ToolGateDecisionRecordRequest request, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateRecord(request);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(request));
        }

        var decisionId = request.ToolGateDecisionId ?? Guid.NewGuid();
        var governanceEventId = request.GovernanceEventId ?? Guid.NewGuid();
        var decision = ToolGateDecisionValidator.NormalizeDecision(request.Decision);
        var createdUtc = request.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var eventPayloadJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            toolGateDecisionId = decisionId,
            toolRequestId = request.ToolRequestId,
            decision,
            gateName = request.GateName.Trim(),
            gateVersion = request.GateVersion,
            reasonCode = request.ReasonCode.Trim(),
            grantsApproval = false,
            grantsExecution = false,
            mutatesSource = false,
            promotesMemory = false,
            recordedAtUtc = createdUtc
        });

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleAsync<ToolGateDecisionRow>(new CommandDefinition(
            "governance.usp_ToolGateDecision_Record",
            new
            {
                ToolGateDecisionId = decisionId,
                ProjectId = request.ProjectId,
                ToolRequestId = request.ToolRequestId,
                GovernanceEventId = governanceEventId,
                CorrelationId = request.CorrelationId,
                CausationId = request.CausationId,
                Decision = decision,
                GateName = request.GateName.Trim(),
                GateVersion = request.GateVersion,
                ActorType = request.ActorType.Trim(),
                ActorId = request.ActorId.Trim(),
                ReasonCode = request.ReasonCode.Trim(),
                EvidenceVersion = 1,
                EvidenceJson = request.EvidenceJson.Trim(),
                GovernanceEventPayloadJson = eventPayloadJson
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row.ToReadModel(request.TenantId);
    }

    public async Task<ToolGateDecisionReadModel?> GetAsync(Guid tenantId, Guid projectId, Guid toolGateDecisionId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("ProjectId is required.", nameof(projectId));
        }

        if (toolGateDecisionId == Guid.Empty)
        {
            throw new ArgumentException("ToolGateDecisionId is required.", nameof(toolGateDecisionId));
        }

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ToolGateDecisionRow>(new CommandDefinition(
            "governance.usp_ToolGateDecision_GetById",
            new { ProjectId = projectId, ToolGateDecisionId = toolGateDecisionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReadModel(tenantId);
    }

    public async Task<IReadOnlyList<ToolGateDecisionSummary>> ListForToolRequestAsync(ToolGateDecisionToolRequestQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateToolRequestQuery(query);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ToolGateDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_ToolGateDecision_ListForToolRequest",
            new
            {
                ProjectId = query.ProjectId,
                ToolRequestId = query.ToolRequestId,
                Take = ToolGateDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary(query.TenantId)).ToArray();
    }

    public async Task<IReadOnlyList<ToolGateDecisionSummary>> ListForProjectAsync(ToolGateDecisionProjectQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateProjectQuery(query);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ToolGateDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_ToolGateDecision_ListForProject",
            new
            {
                ProjectId = query.ProjectId,
                Take = ToolGateDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary(query.TenantId)).ToArray();
    }

    public async Task<IReadOnlyList<ToolGateDecisionSummary>> ListForCorrelationAsync(ToolGateDecisionCorrelationQuery query, CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateCorrelationQuery(query);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(query));
        }

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ToolGateDecisionSummaryRow>(new CommandDefinition(
            "governance.usp_ToolGateDecision_ListForCorrelation",
            new
            {
                ProjectId = query.ProjectId,
                CorrelationId = query.CorrelationId,
                Take = ToolGateDecisionValidator.NormalizeTake(query.Take)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary(query.TenantId)).ToArray();
    }

    private sealed class ToolGateDecisionRow
    {
        public Guid ToolGateDecisionId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ToolRequestId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public Guid CorrelationId { get; set; }
        public Guid CausationId { get; set; }
        public string Decision { get; set; } = string.Empty;
        public string GateName { get; set; } = string.Empty;
        public int GateVersion { get; set; }
        public string ActorType { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public string EvidenceJson { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }

        public ToolGateDecisionReadModel ToReadModel(Guid tenantId) => new(
            ToolGateDecisionId,
            tenantId,
            ProjectId,
            ToolRequestId,
            GovernanceEventId,
            CorrelationId,
            CausationId,
            Decision,
            GateName,
            GateVersion,
            ActorType,
            ActorId,
            ReasonCode,
            EvidenceJson,
            CreatedUtc);
    }

    private sealed class ToolGateDecisionSummaryRow
    {
        public Guid ToolGateDecisionId { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ToolRequestId { get; set; }
        public Guid GovernanceEventId { get; set; }
        public Guid CorrelationId { get; set; }
        public Guid CausationId { get; set; }
        public string Decision { get; set; } = string.Empty;
        public string GateName { get; set; } = string.Empty;
        public int GateVersion { get; set; }
        public string ActorType { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ReasonCode { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }

        public ToolGateDecisionSummary ToSummary(Guid tenantId) => new(
            ToolGateDecisionId,
            tenantId,
            ProjectId,
            ToolRequestId,
            GovernanceEventId,
            CorrelationId,
            CausationId,
            Decision,
            GateName,
            GateVersion,
            ActorType,
            ActorId,
            ReasonCode,
            CreatedUtc);
    }
}

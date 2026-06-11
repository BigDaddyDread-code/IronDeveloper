using System.Data;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Data;

namespace IronDev.Infrastructure.ToolExecutionAudit;

public sealed class SqlToolExecutionAuditStore : IToolExecutionAuditStore
{
    private const string AppendProcedure = "toolaudit.AppendToolExecutionAuditRecord";
    private const string GetProcedure = "toolaudit.GetToolExecutionAuditRecord";
    private const string ListByRunProcedure = "toolaudit.ListToolExecutionAuditRecordsByRun";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ToolExecutionAuditValidator _validator;

    public SqlToolExecutionAuditStore(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new ToolExecutionAuditValidator())
    {
    }

    internal SqlToolExecutionAuditStore(
        IDbConnectionFactory connectionFactory,
        ToolExecutionAuditValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ToolExecutionAuditAppendResult> AppendAsync(
        ToolExecutionAuditAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Record);

        var validationIssues = _validator.Validate(request.Record);
        if (validationIssues.Count > 0)
        {
            return new ToolExecutionAuditAppendResult
            {
                Status = ToolExecutionAuditAppendStatus.Rejected,
                ToolExecutionAuditId = request.Record.ToolExecutionAuditId,
                PayloadSha256 = request.Record.PayloadSha256,
                AuditEnvelopeSha256 = request.Record.AuditEnvelopeSha256,
                Issues = validationIssues
            };
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var status = await connection.QuerySingleAsync<string>(
                new CommandDefinition(
                    AppendProcedure,
                    ToParameters(request.Record),
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: cancellationToken));

            var appendStatus = Enum.Parse<ToolExecutionAuditAppendStatus>(status);
            return new ToolExecutionAuditAppendResult
            {
                Status = appendStatus,
                ToolExecutionAuditId = request.Record.ToolExecutionAuditId,
                PayloadSha256 = request.Record.PayloadSha256,
                AuditEnvelopeSha256 = request.Record.AuditEnvelopeSha256,
                Issues = appendStatus == ToolExecutionAuditAppendStatus.Conflict
                    ? [ToolExecutionAuditValidator.Issue(ToolExecutionAuditValidator.ToolAuditStoreConflict, "Tool execution audit record already exists with different payload or audit hash.", "ToolExecutionAuditId")]
                    : []
            };
        }
        catch (Exception ex)
        {
            return new ToolExecutionAuditAppendResult
            {
                Status = ToolExecutionAuditAppendStatus.Rejected,
                ToolExecutionAuditId = request.Record.ToolExecutionAuditId,
                PayloadSha256 = request.Record.PayloadSha256,
                AuditEnvelopeSha256 = request.Record.AuditEnvelopeSha256,
                Issues =
                [
                    ToolExecutionAuditValidator.Issue(
                        ToolExecutionAuditValidator.ToolAuditStoreAppendFailed,
                        $"Tool execution audit append failed: {ex.Message}",
                        "Store")
                ]
            };
        }
    }

    public async Task<ToolExecutionAuditReadResult> GetAsync(
        ToolExecutionAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ToolExecutionAuditRecordRow>(
            new CommandDefinition(
                GetProcedure,
                new
                {
                    query.TenantId,
                    query.ProjectId,
                    query.ToolExecutionAuditId
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return new ToolExecutionAuditReadResult
        {
            Found = row is not null,
            Record = row?.ToRecord()
        };
    }

    public async Task<IReadOnlyList<ToolExecutionAuditRecord>> ListByRunAsync(
        ToolExecutionAuditRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ToolExecutionAuditRecordRow>(
            new CommandDefinition(
                ListByRunProcedure,
                new
                {
                    query.TenantId,
                    query.ProjectId,
                    query.RunId,
                    Take = Math.Clamp(query.Take, 1, 500)
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToArray();
    }

    private static object ToParameters(ToolExecutionAuditRecord record) =>
        new
        {
            record.ToolExecutionAuditId,
            record.TenantId,
            record.ProjectId,
            record.CampaignId,
            record.RunId,
            record.AgentRunId,
            record.ManualExecutionId,
            record.ToolRequestId,
            record.GateDecisionId,
            ToolKind = record.ToolKind.ToString(),
            RequestType = record.RequestType.ToString(),
            AgentKind = record.AgentKind.ToString(),
            record.AgentId,
            record.AgentName,
            record.Status,
            record.Succeeded,
            record.PayloadKind,
            record.PayloadJson,
            record.PayloadSha256,
            record.AuditEnvelopeJson,
            record.AuditEnvelopeSha256,
            EvidenceRefsJson = ToolExecutionAuditJson.Serialize(record.EvidenceRefs),
            record.CreatedAtUtc,
            record.ContainsRawPrivateReasoning,
            record.ContainsSecret,
            record.ClaimsApproval,
            record.ClaimsPolicyApproval,
            record.ClaimsHumanApproval,
            record.ClaimsMemoryPromotion,
            record.ExecutesTool,
            record.MutatesSource,
            record.AppliesPatch,
            record.WritesFiles,
            record.DeletesFiles,
            record.RunsGit,
            record.CallsExternalSystem,
            record.SubmitsGitHubReview,
            record.CreatesPullRequest,
            record.PromotesMemory,
            record.CreatesCollectiveMemory,
            record.WritesWeaviate
        };

    private sealed class ToolExecutionAuditRecordRow
    {
        public string ToolExecutionAuditId { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public string ProjectId { get; init; } = string.Empty;
        public string? CampaignId { get; init; }
        public string? RunId { get; init; }
        public string AgentRunId { get; init; } = string.Empty;
        public string ManualExecutionId { get; init; } = string.Empty;
        public string ToolRequestId { get; init; } = string.Empty;
        public string GateDecisionId { get; init; } = string.Empty;
        public string ToolKind { get; init; } = string.Empty;
        public string RequestType { get; init; } = string.Empty;
        public string AgentKind { get; init; } = string.Empty;
        public string AgentId { get; init; } = string.Empty;
        public string AgentName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool Succeeded { get; init; }
        public string PayloadKind { get; init; } = string.Empty;
        public string PayloadJson { get; init; } = string.Empty;
        public string PayloadSha256 { get; init; } = string.Empty;
        public string AuditEnvelopeJson { get; init; } = string.Empty;
        public string AuditEnvelopeSha256 { get; init; } = string.Empty;
        public string EvidenceRefsJson { get; init; } = "[]";
        public DateTimeOffset CreatedAtUtc { get; init; }
        public bool ContainsRawPrivateReasoning { get; init; }
        public bool ContainsSecret { get; init; }
        public bool ClaimsApproval { get; init; }
        public bool ClaimsPolicyApproval { get; init; }
        public bool ClaimsHumanApproval { get; init; }
        public bool ClaimsMemoryPromotion { get; init; }
        public bool ExecutesTool { get; init; }
        public bool MutatesSource { get; init; }
        public bool AppliesPatch { get; init; }
        public bool WritesFiles { get; init; }
        public bool DeletesFiles { get; init; }
        public bool RunsGit { get; init; }
        public bool CallsExternalSystem { get; init; }
        public bool SubmitsGitHubReview { get; init; }
        public bool CreatesPullRequest { get; init; }
        public bool PromotesMemory { get; init; }
        public bool CreatesCollectiveMemory { get; init; }
        public bool WritesWeaviate { get; init; }

        public ToolExecutionAuditRecord ToRecord() =>
            new()
            {
                ToolExecutionAuditId = ToolExecutionAuditId,
                TenantId = TenantId,
                ProjectId = ProjectId,
                CampaignId = CampaignId,
                RunId = RunId,
                AgentRunId = AgentRunId,
                ManualExecutionId = ManualExecutionId,
                ToolRequestId = ToolRequestId,
                GateDecisionId = GateDecisionId,
                ToolKind = Enum.Parse<AgentToolKind>(ToolKind),
                RequestType = Enum.Parse<AgentToolRequestType>(RequestType),
                AgentKind = Enum.Parse<AgentKind>(AgentKind),
                AgentId = AgentId,
                AgentName = AgentName,
                Status = Status,
                Succeeded = Succeeded,
                PayloadKind = PayloadKind,
                PayloadJson = PayloadJson,
                PayloadSha256 = PayloadSha256,
                AuditEnvelopeJson = AuditEnvelopeJson,
                AuditEnvelopeSha256 = AuditEnvelopeSha256,
                EvidenceRefs = ToolExecutionAuditJson.DeserializeEvidenceRefs(EvidenceRefsJson),
                CreatedAtUtc = CreatedAtUtc,
                ContainsRawPrivateReasoning = ContainsRawPrivateReasoning,
                ContainsSecret = ContainsSecret,
                ClaimsApproval = ClaimsApproval,
                ClaimsPolicyApproval = ClaimsPolicyApproval,
                ClaimsHumanApproval = ClaimsHumanApproval,
                ClaimsMemoryPromotion = ClaimsMemoryPromotion,
                ExecutesTool = ExecutesTool,
                MutatesSource = MutatesSource,
                AppliesPatch = AppliesPatch,
                WritesFiles = WritesFiles,
                DeletesFiles = DeletesFiles,
                RunsGit = RunsGit,
                CallsExternalSystem = CallsExternalSystem,
                SubmitsGitHubReview = SubmitsGitHubReview,
                CreatesPullRequest = CreatesPullRequest,
                PromotesMemory = PromotesMemory,
                CreatesCollectiveMemory = CreatesCollectiveMemory,
                WritesWeaviate = WritesWeaviate
            };
    }
}

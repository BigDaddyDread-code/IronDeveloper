using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlAgentMemoryInfluenceStore : IAgentMemoryInfluenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlAgentMemoryInfluenceStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordAsync(
        AgentMemoryScope scope,
        MemoryInfluenceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfInvalidScope(scope);
        ThrowIfInvalidDraft(draft);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var memory = await connection.QuerySingleOrDefaultAsync<MemorySnapshotRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                MemoryItemId,
                AuthorityLevel,
                CurrentEventType,
                ExpiresAtUtc
            FROM agent.vwAgentLocalMemoryCurrentState
            WHERE MemoryItemId = @MemoryItemId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId;
            """,
            new
            {
                draft.MemoryItemId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (memory is null)
            throw new InvalidOperationException("Memory influence must reference an existing memory item in the bound scope.");

        var status = ToLifecycleStatus(memory.CurrentEventType);
        if (memory.ExpiresAtUtc is not null && memory.ExpiresAtUtc.Value <= DateTime.UtcNow)
            status = MemoryLifecycleStatus.Expired;

        if (status is not (MemoryLifecycleStatus.Active or MemoryLifecycleStatus.ProposedForReview))
            throw new InvalidOperationException($"Cannot record influence from terminal memory status '{status}'.");

        const string sql = """
            INSERT INTO agent.AgentMemoryInfluenceRecord
            (
                InfluenceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryItemId,
                DecisionId,
                InfluenceType,
                InfluenceSummary,
                Confidence,
                MemoryAuthorityLevelAtInfluence,
                MemoryLifecycleStatusAtInfluence,
                AffectedArtifactType,
                AffectedArtifactId,
                EvidenceRefsJson,
                CreatedAtUtc,
                ThoughtLedgerEntryId,
                CorrelationId,
                InfluenceJson,
                ContentHashSha256
            )
            VALUES
            (
                @InfluenceId,
                @TenantId,
                @ProjectId,
                @CampaignId,
                @RunId,
                @AgentId,
                @MemoryItemId,
                @DecisionId,
                @InfluenceType,
                @InfluenceSummary,
                @Confidence,
                @MemoryAuthorityLevelAtInfluence,
                @MemoryLifecycleStatusAtInfluence,
                @AffectedArtifactType,
                @AffectedArtifactId,
                @EvidenceRefsJson,
                @CreatedAtUtc,
                @ThoughtLedgerEntryId,
                @CorrelationId,
                @InfluenceJson,
                @ContentHashSha256
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                draft.InfluenceId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId,
                draft.MemoryItemId,
                draft.DecisionId,
                InfluenceType = (int)draft.InfluenceType,
                draft.InfluenceSummary,
                draft.Confidence,
                MemoryAuthorityLevelAtInfluence = memory.AuthorityLevel,
                MemoryLifecycleStatusAtInfluence = (int)status,
                draft.AffectedArtifactType,
                draft.AffectedArtifactId,
                EvidenceRefsJson = JsonSerializer.Serialize(draft.EvidenceRefs, JsonOptions),
                CreatedAtUtc = draft.CreatedAt.UtcDateTime,
                draft.ThoughtLedgerEntryId,
                draft.CorrelationId,
                draft.InfluenceJson,
                ContentHashSha256 = (byte[]?)null
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryInfluenceRecord>> QueryAsync(
        AgentMemoryScope scope,
        MemoryInfluenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidScope(scope);

        using var connection = _connectionFactory.CreateConnection();
        var take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, 500);

        var rows = (await connection.QueryAsync<InfluenceRow>(new CommandDefinition(
            """
            SELECT TOP (@Take)
                InfluenceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryItemId,
                DecisionId,
                InfluenceType,
                InfluenceSummary,
                Confidence,
                MemoryAuthorityLevelAtInfluence,
                MemoryLifecycleStatusAtInfluence,
                AffectedArtifactType,
                AffectedArtifactId,
                EvidenceRefsJson,
                CreatedAtUtc,
                ThoughtLedgerEntryId,
                CorrelationId,
                InfluenceJson
            FROM agent.AgentMemoryInfluenceRecord
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId
              AND (@MemoryItemId IS NULL OR MemoryItemId = @MemoryItemId)
              AND (@DecisionId IS NULL OR DecisionId = @DecisionId)
              AND (@InfluenceType IS NULL OR InfluenceType = @InfluenceType)
              AND (@CreatedAfter IS NULL OR CreatedAtUtc >= @CreatedAfter)
              AND (@CreatedBefore IS NULL OR CreatedAtUtc <= @CreatedBefore)
            ORDER BY CreatedAtUtc DESC, InfluenceId DESC;
            """,
            new
            {
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId,
                query.MemoryItemId,
                query.DecisionId,
                InfluenceType = query.InfluenceType is null ? (int?)null : (int)query.InfluenceType.Value,
                CreatedAfter = query.CreatedAfter?.UtcDateTime,
                CreatedBefore = query.CreatedBefore?.UtcDateTime,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return rows.Select(ToRecord).ToArray();
    }

    private static MemoryInfluenceRecord ToRecord(InfluenceRow row) =>
        new()
        {
            InfluenceId = row.InfluenceId,
            Scope = new AgentMemoryScope
            {
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                CampaignId = row.CampaignId,
                RunId = row.RunId,
                AgentId = row.AgentId
            },
            MemoryItemId = row.MemoryItemId,
            DecisionId = row.DecisionId,
            InfluenceType = (MemoryInfluenceType)row.InfluenceType,
            InfluenceSummary = row.InfluenceSummary,
            EvidenceRefs = JsonSerializer.Deserialize<IReadOnlyList<EvidenceRef>>(row.EvidenceRefsJson, JsonOptions) ?? Array.Empty<EvidenceRef>(),
            Confidence = row.Confidence,
            MemoryAuthorityLevelAtInfluence = (MemoryAuthorityLevel)row.MemoryAuthorityLevelAtInfluence,
            MemoryStatusAtInfluence = (MemoryLifecycleStatus)row.MemoryLifecycleStatusAtInfluence,
            CreatedAt = ToUtc(row.CreatedAtUtc),
            AffectedArtifactType = row.AffectedArtifactType,
            AffectedArtifactId = row.AffectedArtifactId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            InfluenceJson = row.InfluenceJson
        };

    private static void ThrowIfInvalidDraft(MemoryInfluenceDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.InfluenceId))
            throw new InvalidOperationException("Memory influence ID is required.");

        if (string.IsNullOrWhiteSpace(draft.MemoryItemId))
            throw new InvalidOperationException("Memory influence item ID is required.");

        if (string.IsNullOrWhiteSpace(draft.DecisionId))
            throw new InvalidOperationException("Memory influence decision ID is required.");

        if (string.IsNullOrWhiteSpace(draft.InfluenceSummary))
            throw new InvalidOperationException("Memory influence summary is required.");

        if (draft.EvidenceRefs is null || draft.EvidenceRefs.Count == 0)
            throw new InvalidOperationException("Memory influence requires evidence.");

        if (draft.Confidence < 0 || draft.Confidence > 1)
            throw new InvalidOperationException("Memory influence confidence must be between 0 and 1.");

        if (!Enum.IsDefined(draft.InfluenceType))
            throw new InvalidOperationException($"Unsupported memory influence type '{draft.InfluenceType}'.");
    }

    private static void ThrowIfInvalidScope(AgentMemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.TenantId) ||
            string.IsNullOrWhiteSpace(scope.ProjectId) ||
            string.IsNullOrWhiteSpace(scope.CampaignId) ||
            string.IsNullOrWhiteSpace(scope.RunId) ||
            string.IsNullOrWhiteSpace(scope.AgentId))
        {
            throw new InvalidOperationException("Memory influence operations require a complete memory scope.");
        }
    }

    private static MemoryLifecycleStatus ToLifecycleStatus(int? eventType) =>
        eventType switch
        {
            (int)AgentLocalMemoryEventType.Superseded => MemoryLifecycleStatus.Superseded,
            (int)AgentLocalMemoryEventType.Expired => MemoryLifecycleStatus.Expired,
            (int)AgentLocalMemoryEventType.Invalidated => MemoryLifecycleStatus.Invalidated,
            (int)AgentLocalMemoryEventType.ProposedForReview => MemoryLifecycleStatus.ProposedForReview,
            (int)AgentLocalMemoryEventType.Rejected => MemoryLifecycleStatus.Rejected,
            (int)AgentLocalMemoryEventType.Accepted => MemoryLifecycleStatus.Accepted,
            _ => MemoryLifecycleStatus.Active
        };

    private static async Task OpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
            return;

        if (connection is DbConnection dbConnection)
            await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        else
            connection.Open();
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class MemorySnapshotRow
    {
        public string MemoryItemId { get; set; } = string.Empty;
        public int AuthorityLevel { get; set; }
        public int? CurrentEventType { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    private sealed class InfluenceRow
    {
        public string InfluenceId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string MemoryItemId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public int InfluenceType { get; set; }
        public string InfluenceSummary { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public int MemoryAuthorityLevelAtInfluence { get; set; }
        public int MemoryLifecycleStatusAtInfluence { get; set; }
        public string? AffectedArtifactType { get; set; }
        public string? AffectedArtifactId { get; set; }
        public string EvidenceRefsJson { get; set; } = "[]";
        public DateTime CreatedAtUtc { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public string? InfluenceJson { get; set; }
    }
}

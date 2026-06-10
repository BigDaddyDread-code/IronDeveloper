using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed partial class SqlMemoryIndexQueueStore : IMemoryIndexQueueStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BannedPrivateReasoningTokens =
    [
        "RawPrompt",
        "Prompt",
        "RawCompletion",
        "Completion",
        "ChainOfThought",
        "Scratchpad",
        "PrivateReasoning"
    ];

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlMemoryIndexQueueStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task QueueAsync(
        MemoryIndexProjection projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ThrowIfUnsafeProjection(projection);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            const string insertSql = """
                INSERT INTO agent.AgentMemoryIndexQueue
                (
                    IndexRecordId,
                    TenantId,
                    ProjectId,
                    CampaignId,
                    RunId,
                    AgentId,
                    ArtifactType,
                    ArtifactId,
                    AuthorityLevel,
                    Title,
                    Summary,
                    EvidenceRefsJson,
                    MetadataJson,
                    SourceHashSha256,
                    DecisionId,
                    ThoughtLedgerEntryId,
                    CorrelationId,
                    CreatedAtUtc
                )
                VALUES
                (
                    @IndexRecordId,
                    @TenantId,
                    @ProjectId,
                    @CampaignId,
                    @RunId,
                    @AgentId,
                    @ArtifactType,
                    @ArtifactId,
                    @AuthorityLevel,
                    @Title,
                    @Summary,
                    @EvidenceRefsJson,
                    @MetadataJson,
                    @SourceHashSha256,
                    @DecisionId,
                    @ThoughtLedgerEntryId,
                    @CorrelationId,
                    @CreatedAtUtc
                );
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                insertSql,
                new
                {
                    projection.IndexRecordId,
                    projection.TenantId,
                    projection.ProjectId,
                    projection.CampaignId,
                    projection.RunId,
                    projection.AgentId,
                    ArtifactType = (int)projection.ArtifactType,
                    projection.ArtifactId,
                    AuthorityLevel = (int)projection.AuthorityLevel,
                    projection.Title,
                    projection.Summary,
                    EvidenceRefsJson = JsonSerializer.Serialize(projection.EvidenceRefs, JsonOptions),
                    MetadataJson = projection.Metadata is null ? null : JsonSerializer.Serialize(projection.Metadata, JsonOptions),
                    projection.SourceHashSha256,
                    projection.DecisionId,
                    projection.ThoughtLedgerEntryId,
                    projection.CorrelationId,
                    CreatedAtUtc = projection.CreatedAt.UtcDateTime
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await InsertEventAsync(
                connection,
                transaction,
                projection.IndexRecordId,
                MemoryIndexEventType.Queued,
                weaviateObjectId: null,
                error: null,
                createdAt: projection.CreatedAt,
                cancellationToken).ConfigureAwait(false);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public Task<IReadOnlyList<MemoryIndexQueueRecord>> QueryPendingAsync(
        string tenantId,
        string projectId,
        int take,
        CancellationToken cancellationToken = default) =>
        QueryInternalAsync(tenantId, projectId, campaignId: null, runId: null, MemoryIndexStatus.Pending, take, cancellationToken);

    public async Task AddEventAsync(
        string indexRecordId,
        MemoryIndexEventType eventType,
        string? weaviateObjectId = null,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexRecordId))
            throw new InvalidOperationException("Memory index event requires an index record ID.");

        if (!Enum.IsDefined(eventType))
            throw new InvalidOperationException($"Unsupported memory index event type '{eventType}'.");

        if (eventType == MemoryIndexEventType.Queued)
            throw new InvalidOperationException("Queued index events are inserted only by QueueAsync.");

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var exists = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM agent.AgentMemoryIndexQueue WHERE IndexRecordId = @IndexRecordId;",
            new { IndexRecordId = indexRecordId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (exists != 1)
            throw new InvalidOperationException("Memory index queue record does not exist.");

        await InsertEventAsync(
            connection,
            transaction: null,
            indexRecordId,
            eventType,
            weaviateObjectId,
            error,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MemoryIndexQueueRecord>> QueryAsync(
        string tenantId,
        string projectId,
        string? campaignId,
        string? runId,
        MemoryIndexStatus? status,
        int take,
        CancellationToken cancellationToken = default) =>
        QueryInternalAsync(tenantId, projectId, campaignId, runId, status, take, cancellationToken);

    private async Task<IReadOnlyList<MemoryIndexQueueRecord>> QueryInternalAsync(
        string tenantId,
        string projectId,
        string? campaignId,
        string? runId,
        MemoryIndexStatus? status,
        int take,
        CancellationToken cancellationToken)
    {
        ThrowIfInvalidQueryScope(tenantId, projectId);

        using var connection = _connectionFactory.CreateConnection();
        var clampedTake = Math.Clamp(take <= 0 ? 50 : take, 1, 500);
        var rows = (await connection.QueryAsync<QueueRow>(new CommandDefinition(
            """
            WITH LatestEvent AS
            (
                SELECT
                    IndexRecordId,
                    EventType,
                    WeaviateObjectId,
                    Error,
                    CreatedAtUtc,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY IndexRecordId
                        ORDER BY CreatedAtUtc DESC, IndexEventId DESC
                    ) AS rn
                FROM agent.AgentMemoryIndexEvent
            )
            SELECT TOP (@Take)
                q.IndexRecordId,
                q.TenantId,
                q.ProjectId,
                q.CampaignId,
                q.RunId,
                q.AgentId,
                q.ArtifactType,
                q.ArtifactId,
                q.AuthorityLevel,
                ISNULL(le.EventType, 1) AS Status,
                q.Title,
                q.Summary,
                q.EvidenceRefsJson,
                q.MetadataJson,
                q.SourceHashSha256,
                q.DecisionId,
                q.ThoughtLedgerEntryId,
                q.CorrelationId,
                q.CreatedAtUtc,
                CASE WHEN le.EventType = @IndexedEventType THEN le.CreatedAtUtc ELSE NULL END AS IndexedAtUtc,
                le.WeaviateObjectId,
                le.Error AS LastError
            FROM agent.AgentMemoryIndexQueue q
            LEFT JOIN LatestEvent le
                ON le.IndexRecordId = q.IndexRecordId
               AND le.rn = 1
            WHERE q.TenantId = @TenantId
              AND q.ProjectId = @ProjectId
              AND (@CampaignId IS NULL OR q.CampaignId = @CampaignId)
              AND (@RunId IS NULL OR q.RunId = @RunId)
              AND (@Status IS NULL OR ISNULL(le.EventType, 1) = @Status)
            ORDER BY q.CreatedAtUtc, q.IndexRecordId;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                Status = status is null ? (int?)null : ToEventType(status.Value),
                IndexedEventType = (int)MemoryIndexEventType.Indexed,
                Take = clampedTake
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return rows.Select(ToRecord).ToArray();
    }

    private static async Task InsertEventAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        string indexRecordId,
        MemoryIndexEventType eventType,
        string? weaviateObjectId,
        string? error,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO agent.AgentMemoryIndexEvent
            (
                IndexEventId,
                IndexRecordId,
                EventType,
                WeaviateObjectId,
                Error,
                CreatedAtUtc
            )
            VALUES
            (
                @IndexEventId,
                @IndexRecordId,
                @EventType,
                @WeaviateObjectId,
                @Error,
                @CreatedAtUtc
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                IndexEventId = $"index-event-{Guid.NewGuid():N}",
                IndexRecordId = indexRecordId,
                EventType = (int)eventType,
                WeaviateObjectId = weaviateObjectId,
                Error = error,
                CreatedAtUtc = createdAt.UtcDateTime
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public static void ThrowIfUnsafeProjection(MemoryIndexProjection projection)
    {
        if (string.IsNullOrWhiteSpace(projection.IndexRecordId))
            throw new InvalidOperationException("Memory index projection requires an index record ID.");

        if (string.IsNullOrWhiteSpace(projection.TenantId) ||
            string.IsNullOrWhiteSpace(projection.ProjectId) ||
            string.IsNullOrWhiteSpace(projection.CampaignId))
        {
            throw new InvalidOperationException("Memory index projection requires tenant, project, and campaign identity.");
        }

        if (!Enum.IsDefined(projection.ArtifactType))
            throw new InvalidOperationException($"Unsupported memory index artifact type '{projection.ArtifactType}'.");

        if (!Enum.IsDefined(projection.AuthorityLevel))
            throw new InvalidOperationException($"Unsupported memory index authority level '{projection.AuthorityLevel}'.");

        if (string.IsNullOrWhiteSpace(projection.ArtifactId))
            throw new InvalidOperationException("Memory index projection requires an artifact ID.");

        if (string.IsNullOrWhiteSpace(projection.Title))
            throw new InvalidOperationException("Memory index projection title is required.");

        if (string.IsNullOrWhiteSpace(projection.Summary))
            throw new InvalidOperationException("Memory index projection summary is required.");

        if (projection.EvidenceRefs is null || projection.EvidenceRefs.Count == 0)
            throw new InvalidOperationException("Memory index projection requires evidence references.");

        foreach (var evidence in projection.EvidenceRefs)
            ThrowIfInvalidEvidence(evidence);

        if (!string.IsNullOrWhiteSpace(projection.SourceHashSha256) && !Sha256Regex().IsMatch(projection.SourceHashSha256))
            throw new InvalidOperationException("Memory index projection source hash must be 64 hex characters.");

        ThrowIfContainsPrivateReasoning(projection.Title, "Memory index projection title");
        ThrowIfContainsPrivateReasoning(projection.Summary, "Memory index projection summary");

        if (projection.Metadata is not null)
        {
            foreach (var item in projection.Metadata)
            {
                ThrowIfContainsPrivateReasoning(item.Key, "Memory index projection metadata");
                ThrowIfContainsPrivateReasoning(item.Value, "Memory index projection metadata");
            }
        }
    }

    private static void ThrowIfInvalidEvidence(EvidenceRef evidence)
    {
        if (evidence is null)
            throw new InvalidOperationException("Memory index projection evidence refs cannot contain null entries.");

        if (string.IsNullOrWhiteSpace(evidence.EvidenceId))
            throw new InvalidOperationException("Memory index projection evidence refs require an evidence ID.");

        if (!Enum.IsDefined(evidence.EvidenceType))
            throw new InvalidOperationException($"Unsupported memory index projection evidence type '{evidence.EvidenceType}'.");

        if (string.IsNullOrWhiteSpace(evidence.SourceId))
            throw new InvalidOperationException("Memory index projection evidence refs require a source ID.");
    }

    private static void ThrowIfContainsPrivateReasoning(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var token in BannedPrivateReasoningTokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{fieldName} must not contain raw private reasoning marker '{token}'.");
        }
    }

    private static MemoryIndexQueueRecord ToRecord(QueueRow row) =>
        new()
        {
            IndexRecordId = row.IndexRecordId,
            TenantId = row.TenantId,
            ProjectId = row.ProjectId,
            CampaignId = row.CampaignId,
            RunId = row.RunId,
            AgentId = row.AgentId,
            ArtifactType = (MemoryIndexArtifactType)row.ArtifactType,
            ArtifactId = row.ArtifactId,
            AuthorityLevel = (MemoryIndexAuthorityLevel)row.AuthorityLevel,
            Status = ToStatus(row.Status),
            Title = row.Title,
            Summary = row.Summary,
            EvidenceRefs = JsonSerializer.Deserialize<IReadOnlyList<EvidenceRef>>(row.EvidenceRefsJson, JsonOptions) ?? Array.Empty<EvidenceRef>(),
            CreatedAt = ToUtc(row.CreatedAtUtc),
            IndexedAt = row.IndexedAtUtc is null ? null : ToUtc(row.IndexedAtUtc.Value),
            WeaviateObjectId = row.WeaviateObjectId,
            LastError = row.LastError,
            SourceHashSha256 = row.SourceHashSha256,
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            Metadata = string.IsNullOrWhiteSpace(row.MetadataJson)
                ? null
                : JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(row.MetadataJson, JsonOptions)
        };

    private static MemoryIndexStatus ToStatus(int eventType) =>
        eventType switch
        {
            (int)MemoryIndexEventType.Indexed => MemoryIndexStatus.Indexed,
            (int)MemoryIndexEventType.Failed => MemoryIndexStatus.Failed,
            (int)MemoryIndexEventType.Superseded => MemoryIndexStatus.Superseded,
            (int)MemoryIndexEventType.Skipped => MemoryIndexStatus.Skipped,
            _ => MemoryIndexStatus.Pending
        };

    private static int ToEventType(MemoryIndexStatus status) =>
        status switch
        {
            MemoryIndexStatus.Indexed => (int)MemoryIndexEventType.Indexed,
            MemoryIndexStatus.Failed => (int)MemoryIndexEventType.Failed,
            MemoryIndexStatus.Superseded => (int)MemoryIndexEventType.Superseded,
            MemoryIndexStatus.Skipped => (int)MemoryIndexEventType.Skipped,
            _ => (int)MemoryIndexEventType.Queued
        };

    private static void ThrowIfInvalidQueryScope(string tenantId, string projectId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException("Memory index queue queries require tenant and project identity.");
    }

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

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Regex();

    private sealed class QueueRow
    {
        public string IndexRecordId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string? RunId { get; set; }
        public string? AgentId { get; set; }
        public int ArtifactType { get; set; }
        public string ArtifactId { get; set; } = string.Empty;
        public int AuthorityLevel { get; set; }
        public int Status { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string EvidenceRefsJson { get; set; } = "[]";
        public string? MetadataJson { get; set; }
        public string? SourceHashSha256 { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? IndexedAtUtc { get; set; }
        public string? WeaviateObjectId { get; set; }
        public string? LastError { get; set; }
    }
}

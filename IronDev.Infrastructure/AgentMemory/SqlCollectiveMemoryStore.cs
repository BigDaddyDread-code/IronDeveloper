using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory.Collective;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlCollectiveMemoryStore : ICollectiveMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlCollectiveMemoryStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<CollectiveMemoryItem?> GetAsync(
        CollectiveMemoryScope scope,
        string collectiveMemoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<CollectiveMemoryRow>(new CommandDefinition(
            """
            SELECT TOP (1) *
            FROM agent.vwCollectiveMemoryCurrentState
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CollectiveMemoryId = @CollectiveMemoryId
            """,
            new { scope.TenantId, scope.ProjectId, CollectiveMemoryId = collectiveMemoryId },
            cancellationToken: cancellationToken));

        return row is null ? null : ToItem(row);
    }

    public async Task<IReadOnlyList<CollectiveMemoryItem>> QueryAsync(
        CollectiveMemoryScope scope,
        CollectiveMemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        query ??= new CollectiveMemoryQuery();

        var take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, 200);

        using var connection = _connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<CollectiveMemoryRow>(new CommandDefinition(
            """
            SELECT TOP (@Take) *
            FROM agent.vwCollectiveMemoryCurrentState
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND (@KnowledgeDomainId IS NULL OR KnowledgeDomainId = @KnowledgeDomainId)
              AND (@ComponentId IS NULL OR ComponentId = @ComponentId)
              AND (@RepositoryId IS NULL OR RepositoryId = @RepositoryId)
              AND (@MemoryType IS NULL OR MemoryType = @MemoryType)
              AND (@AuthorityLevel IS NULL OR AuthorityLevel = @AuthorityLevel)
              AND (@Status IS NULL OR CurrentStatus = @Status)
              AND (@ReviewState IS NULL OR CurrentReviewState = @ReviewState)
              AND (@DecisionId IS NULL OR DecisionId = @DecisionId)
              AND (@IncludeDeprecated = 1 OR CurrentStatus <> 'Deprecated')
              AND (@IncludeRejected = 1 OR CurrentStatus <> 'Rejected')
              AND (@TextSearch IS NULL OR Title LIKE '%' + @TextSearch + '%' OR Summary LIKE '%' + @TextSearch + '%')
            ORDER BY CreatedAtUtc DESC, CollectiveMemoryId DESC
            """,
            new
            {
                scope.TenantId,
                scope.ProjectId,
                scope.KnowledgeDomainId,
                scope.ComponentId,
                scope.RepositoryId,
                MemoryType = query.MemoryType is null ? (int?)null : (int)query.MemoryType.Value,
                AuthorityLevel = query.AuthorityLevel is null ? (int?)null : (int)query.AuthorityLevel.Value,
                Status = query.Status?.ToString(),
                ReviewState = query.ReviewState?.ToString(),
                query.DecisionId,
                query.TextSearch,
                IncludeDeprecated = query.IncludeDeprecated ? 1 : 0,
                IncludeRejected = query.IncludeRejected ? 1 : 0,
                Take = take
            },
            cancellationToken: cancellationToken))).ToArray();

        return rows.Select(ToItem).ToArray();
    }

    public async Task<IReadOnlyList<CollectiveMemoryEventRecord>> GetEventsAsync(
        CollectiveMemoryScope scope,
        string collectiveMemoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var connection = _connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<CollectiveMemoryEventRow>(new CommandDefinition(
            """
            SELECT e.*
            FROM agent.CollectiveMemoryEvent e
            INNER JOIN agent.CollectiveMemoryItem i
                ON i.CollectiveMemoryId = e.CollectiveMemoryId
            WHERE i.TenantId = @TenantId
              AND i.ProjectId = @ProjectId
              AND e.CollectiveMemoryId = @CollectiveMemoryId
            ORDER BY e.CreatedAtUtc, e.CollectiveMemoryEventId
            """,
            new { scope.TenantId, scope.ProjectId, CollectiveMemoryId = collectiveMemoryId },
            cancellationToken: cancellationToken))).ToArray();

        return rows.Select(row => new CollectiveMemoryEventRecord
        {
            CollectiveMemoryEventId = row.CollectiveMemoryEventId,
            CollectiveMemoryId = row.CollectiveMemoryId,
            EventType = (CollectiveMemoryEventType)row.EventType,
            Reason = row.Reason,
            CreatedAt = row.CreatedAtUtc,
            CreatedByUserId = row.CreatedByUserId,
            CreatedByAgentId = row.CreatedByAgentId,
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            EventJson = row.EventJson
        }).ToArray();
    }

    private static CollectiveMemoryItem ToItem(CollectiveMemoryRow row) =>
        new()
        {
            CollectiveMemoryId = row.CollectiveMemoryId,
            Scope = new CollectiveMemoryScope
            {
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                KnowledgeDomainId = row.KnowledgeDomainId,
                ComponentId = row.ComponentId,
                RepositoryId = row.RepositoryId
            },
            MemoryType = (CollectiveMemoryType)row.MemoryType,
            AuthorityLevel = (CollectiveMemoryAuthorityLevel)row.AuthorityLevel,
            Status = Enum.Parse<CollectiveMemoryStatus>(row.CurrentStatus),
            ReviewState = Enum.Parse<CollectiveMemoryReviewState>(row.CurrentReviewState),
            Title = row.Title,
            Summary = row.Summary,
            Sources = DeserializeArray<CollectiveMemorySourceRef>(row.SourcesJson),
            EvidenceRefs = DeserializeArray<CollectiveMemoryEvidenceRef>(row.EvidenceRefsJson),
            Contradictions = DeserializeArray<CollectiveMemoryContradictionRef>(row.ContradictionsJson),
            Supersedes = DeserializeArray<CollectiveMemorySupersessionRef>(row.SupersedesJson),
            Confidence = row.Confidence,
            CreatedAt = row.CreatedAtUtc,
            LastReviewedAt = row.LastReviewedAtUtc,
            LastConfirmedAt = row.LastConfirmedAtUtc,
            ExpiresAt = row.ExpiresAtUtc,
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            ContentHashSha256 = row.ContentHashSha256,
            CollectiveMemoryJson = row.CollectiveMemoryJson
        };

    private static IReadOnlyList<T> DeserializeArray<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<T>>(json, JsonOptions) ?? [];

    private sealed record CollectiveMemoryRow
    {
        public required string CollectiveMemoryId { get; init; }
        public required string TenantId { get; init; }
        public required string ProjectId { get; init; }
        public string? KnowledgeDomainId { get; init; }
        public string? ComponentId { get; init; }
        public string? RepositoryId { get; init; }
        public required int MemoryType { get; init; }
        public required int AuthorityLevel { get; init; }
        public required string CurrentStatus { get; init; }
        public required string CurrentReviewState { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required string SourcesJson { get; init; }
        public required string EvidenceRefsJson { get; init; }
        public required string ContradictionsJson { get; init; }
        public required string SupersedesJson { get; init; }
        public required decimal Confidence { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? LastReviewedAtUtc { get; init; }
        public DateTimeOffset? LastConfirmedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string? DecisionId { get; init; }
        public string? ThoughtLedgerEntryId { get; init; }
        public string? CorrelationId { get; init; }
        public string? CollectiveMemoryJson { get; init; }
        public string? ContentHashSha256 { get; init; }
    }

    private sealed record CollectiveMemoryEventRow
    {
        public required string CollectiveMemoryEventId { get; init; }
        public required string CollectiveMemoryId { get; init; }
        public required int EventType { get; init; }
        public required string Reason { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public string? CreatedByUserId { get; init; }
        public string? CreatedByAgentId { get; init; }
        public string? DecisionId { get; init; }
        public string? ThoughtLedgerEntryId { get; init; }
        public string? CorrelationId { get; init; }
        public string? EventJson { get; init; }
    }
}

using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlAgentMemoryHandoffStore : IAgentMemoryHandoffStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlAgentMemoryHandoffStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(
        AgentMemoryScope sourceScope,
        HandoffMemorySliceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceScope);
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfInvalidScope(sourceScope);
        ThrowIfInvalidDraft(sourceScope, draft);

        var memoryItemIds = draft.MemoryItemIds
            .Select(item => item.Trim())
            .ToArray();
        var influenceIds = draft.InfluenceIds?
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var memoryRows = (await connection.QueryAsync<MemorySnapshotRow>(new CommandDefinition(
            """
            SELECT
                MemoryItemId,
                MemoryType,
                AuthorityLevel,
                Title,
                Summary,
                Confidence,
                CurrentEventType,
                ExpiresAtUtc
            FROM agent.vwAgentLocalMemoryCurrentState
            WHERE MemoryItemId IN @MemoryItemIds
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId;
            """,
            new
            {
                MemoryItemIds = memoryItemIds,
                sourceScope.TenantId,
                sourceScope.ProjectId,
                sourceScope.CampaignId,
                sourceScope.RunId,
                sourceScope.AgentId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var memoryById = memoryRows.ToDictionary(row => row.MemoryItemId, StringComparer.Ordinal);
        var missing = memoryItemIds.Where(item => !memoryById.ContainsKey(item)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Handoff memory items must exist in the source agent scope: " + string.Join(", ", missing));

        var snapshots = new List<HandoffMemoryItemSnapshot>();
        foreach (var memoryItemId in memoryItemIds)
        {
            var row = memoryById[memoryItemId];
            var status = ToLifecycleStatus(row.CurrentEventType);
            if (row.ExpiresAtUtc is not null && row.ExpiresAtUtc.Value <= DateTime.UtcNow)
                status = MemoryLifecycleStatus.Expired;

            if (status is not (MemoryLifecycleStatus.Active or MemoryLifecycleStatus.ProposedForReview))
                throw new InvalidOperationException($"Cannot create handoff from terminal memory status '{status}'.");

            snapshots.Add(new HandoffMemoryItemSnapshot
            {
                MemoryItemId = row.MemoryItemId,
                MemoryType = (AgentMemoryType)row.MemoryType,
                AuthorityLevelAtHandoff = (MemoryAuthorityLevel)row.AuthorityLevel,
                StatusAtHandoff = status,
                Title = row.Title,
                Summary = row.Summary,
                Confidence = row.Confidence
            });
        }

        if (influenceIds is { Length: > 0 })
        {
            var influenceCount = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM agent.AgentMemoryInfluenceRecord
                WHERE InfluenceId IN @InfluenceIds
                  AND TenantId = @TenantId
                  AND ProjectId = @ProjectId
                  AND CampaignId = @CampaignId
                  AND RunId = @RunId
                  AND AgentId = @AgentId;
                """,
                new
                {
                    InfluenceIds = influenceIds,
                    sourceScope.TenantId,
                    sourceScope.ProjectId,
                    sourceScope.CampaignId,
                    sourceScope.RunId,
                    sourceScope.AgentId
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (influenceCount != influenceIds.Length)
                throw new InvalidOperationException("Handoff influence IDs must belong to the source agent scope.");
        }

        const string insertSql = """
            INSERT INTO agent.AgentMemoryHandoffSlice
            (
                HandoffMemorySliceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                SourceAgentId,
                TargetAgentId,
                MemoryItemIdsJson,
                MemorySnapshotsJson,
                Summary,
                AllowedUse,
                EvidenceRefsJson,
                Confidence,
                InfluenceIdsJson,
                DecisionId,
                ThoughtLedgerEntryId,
                CorrelationId,
                CreatedAtUtc,
                ExpiresAtUtc,
                HandoffJson,
                ContentHashSha256
            )
            VALUES
            (
                @HandoffMemorySliceId,
                @TenantId,
                @ProjectId,
                @CampaignId,
                @RunId,
                @SourceAgentId,
                @TargetAgentId,
                @MemoryItemIdsJson,
                @MemorySnapshotsJson,
                @Summary,
                @AllowedUse,
                @EvidenceRefsJson,
                @Confidence,
                @InfluenceIdsJson,
                @DecisionId,
                @ThoughtLedgerEntryId,
                @CorrelationId,
                @CreatedAtUtc,
                @ExpiresAtUtc,
                @HandoffJson,
                @ContentHashSha256
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            insertSql,
            new
            {
                draft.HandoffMemorySliceId,
                sourceScope.TenantId,
                sourceScope.ProjectId,
                sourceScope.CampaignId,
                sourceScope.RunId,
                SourceAgentId = sourceScope.AgentId,
                draft.TargetAgentId,
                MemoryItemIdsJson = JsonSerializer.Serialize(memoryItemIds, JsonOptions),
                MemorySnapshotsJson = JsonSerializer.Serialize(snapshots, JsonOptions),
                draft.Summary,
                AllowedUse = (int)draft.AllowedUse,
                EvidenceRefsJson = JsonSerializer.Serialize(draft.EvidenceRefs, JsonOptions),
                draft.Confidence,
                InfluenceIdsJson = influenceIds is null || influenceIds.Length == 0 ? null : JsonSerializer.Serialize(influenceIds, JsonOptions),
                draft.DecisionId,
                draft.ThoughtLedgerEntryId,
                draft.CorrelationId,
                CreatedAtUtc = draft.CreatedAt.UtcDateTime,
                ExpiresAtUtc = draft.ExpiresAt?.UtcDateTime,
                draft.HandoffJson,
                ContentHashSha256 = (byte[]?)null
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryIncomingAsync(
        AgentMemoryScope targetScope,
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetScope);
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidScope(targetScope);

        return QueryAsync(targetScope, query, incoming: true, cancellationToken);
    }

    public Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryOutgoingAsync(
        AgentMemoryScope sourceScope,
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceScope);
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidScope(sourceScope);

        return QueryAsync(sourceScope, query, incoming: false, cancellationToken);
    }

    private async Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryAsync(
        AgentMemoryScope scope,
        HandoffMemorySliceQuery query,
        bool incoming,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, 500);

        var rows = (await connection.QueryAsync<HandoffRow>(new CommandDefinition(
            incoming
                ? """
                  SELECT TOP (@Take)
                      HandoffMemorySliceId,
                      TenantId,
                      ProjectId,
                      CampaignId,
                      RunId,
                      SourceAgentId,
                      TargetAgentId,
                      MemoryItemIdsJson,
                      MemorySnapshotsJson,
                      Summary,
                      AllowedUse,
                      EvidenceRefsJson,
                      Confidence,
                      InfluenceIdsJson,
                      DecisionId,
                      ThoughtLedgerEntryId,
                      CorrelationId,
                      CreatedAtUtc,
                      ExpiresAtUtc,
                      HandoffJson
                  FROM agent.AgentMemoryHandoffSlice
                  WHERE TenantId = @TenantId
                    AND ProjectId = @ProjectId
                    AND CampaignId = @CampaignId
                    AND RunId = @RunId
                    AND TargetAgentId = @BoundAgentId
                    AND (@SourceAgentId IS NULL OR SourceAgentId = @SourceAgentId)
                    AND (@AllowedUse IS NULL OR AllowedUse = @AllowedUse)
                    AND (@CreatedAfter IS NULL OR CreatedAtUtc >= @CreatedAfter)
                    AND (@CreatedBefore IS NULL OR CreatedAtUtc <= @CreatedBefore)
                    AND (@IncludeExpired = 1 OR ExpiresAtUtc IS NULL OR ExpiresAtUtc > SYSUTCDATETIME())
                  ORDER BY CreatedAtUtc DESC, HandoffMemorySliceId DESC;
                  """
                : """
                  SELECT TOP (@Take)
                      HandoffMemorySliceId,
                      TenantId,
                      ProjectId,
                      CampaignId,
                      RunId,
                      SourceAgentId,
                      TargetAgentId,
                      MemoryItemIdsJson,
                      MemorySnapshotsJson,
                      Summary,
                      AllowedUse,
                      EvidenceRefsJson,
                      Confidence,
                      InfluenceIdsJson,
                      DecisionId,
                      ThoughtLedgerEntryId,
                      CorrelationId,
                      CreatedAtUtc,
                      ExpiresAtUtc,
                      HandoffJson
                  FROM agent.AgentMemoryHandoffSlice
                  WHERE TenantId = @TenantId
                    AND ProjectId = @ProjectId
                    AND CampaignId = @CampaignId
                    AND RunId = @RunId
                    AND SourceAgentId = @BoundAgentId
                    AND (@TargetAgentId IS NULL OR TargetAgentId = @TargetAgentId)
                    AND (@AllowedUse IS NULL OR AllowedUse = @AllowedUse)
                    AND (@CreatedAfter IS NULL OR CreatedAtUtc >= @CreatedAfter)
                    AND (@CreatedBefore IS NULL OR CreatedAtUtc <= @CreatedBefore)
                    AND (@IncludeExpired = 1 OR ExpiresAtUtc IS NULL OR ExpiresAtUtc > SYSUTCDATETIME())
                  ORDER BY CreatedAtUtc DESC, HandoffMemorySliceId DESC;
                  """,
            new
            {
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                BoundAgentId = scope.AgentId,
                query.SourceAgentId,
                query.TargetAgentId,
                AllowedUse = query.AllowedUse is null ? (int?)null : (int)query.AllowedUse.Value,
                CreatedAfter = query.CreatedAfter?.UtcDateTime,
                CreatedBefore = query.CreatedBefore?.UtcDateTime,
                query.IncludeExpired,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return rows.Select(ToRecord).ToArray();
    }

    private static HandoffMemorySliceRecord ToRecord(HandoffRow row) =>
        new()
        {
            HandoffMemorySliceId = row.HandoffMemorySliceId,
            TenantId = row.TenantId,
            ProjectId = row.ProjectId,
            CampaignId = row.CampaignId,
            RunId = row.RunId,
            SourceAgentId = row.SourceAgentId,
            TargetAgentId = row.TargetAgentId,
            MemoryItemIds = JsonSerializer.Deserialize<IReadOnlyList<string>>(row.MemoryItemIdsJson, JsonOptions) ?? Array.Empty<string>(),
            Summary = row.Summary,
            AllowedUse = (HandoffMemoryAllowedUse)row.AllowedUse,
            EvidenceRefs = JsonSerializer.Deserialize<IReadOnlyList<EvidenceRef>>(row.EvidenceRefsJson, JsonOptions) ?? Array.Empty<EvidenceRef>(),
            Confidence = row.Confidence,
            MemorySnapshots = JsonSerializer.Deserialize<IReadOnlyList<HandoffMemoryItemSnapshot>>(row.MemorySnapshotsJson, JsonOptions) ?? Array.Empty<HandoffMemoryItemSnapshot>(),
            CreatedAt = ToUtc(row.CreatedAtUtc),
            ExpiresAt = row.ExpiresAtUtc is null ? null : ToUtc(row.ExpiresAtUtc.Value),
            InfluenceIds = string.IsNullOrWhiteSpace(row.InfluenceIdsJson)
                ? null
                : JsonSerializer.Deserialize<IReadOnlyList<string>>(row.InfluenceIdsJson, JsonOptions),
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            HandoffJson = row.HandoffJson
        };

    private static void ThrowIfInvalidDraft(AgentMemoryScope sourceScope, HandoffMemorySliceDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.HandoffMemorySliceId))
            throw new InvalidOperationException("Handoff memory slice ID is required.");

        if (string.IsNullOrWhiteSpace(draft.TargetAgentId))
            throw new InvalidOperationException("Handoff target agent ID is required.");

        if (string.Equals(sourceScope.AgentId, draft.TargetAgentId, StringComparison.Ordinal))
            throw new InvalidOperationException("Handoff source and target agents must be different.");

        if (draft.MemoryItemIds is null || draft.MemoryItemIds.Count == 0)
            throw new InvalidOperationException("Handoff memory item IDs are required.");

        if (draft.MemoryItemIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Handoff memory item IDs cannot contain blank values.");

        if (draft.MemoryItemIds.Select(item => item.Trim()).Distinct(StringComparer.Ordinal).Count() != draft.MemoryItemIds.Count)
            throw new InvalidOperationException("Handoff memory item IDs cannot contain duplicates.");

        if (string.IsNullOrWhiteSpace(draft.Summary))
            throw new InvalidOperationException("Handoff summary is required.");

        if (!Enum.IsDefined(draft.AllowedUse))
            throw new InvalidOperationException($"Unsupported handoff allowed use '{draft.AllowedUse}'.");

        if (draft.EvidenceRefs is null || draft.EvidenceRefs.Count == 0)
            throw new InvalidOperationException("Handoff evidence is required.");

        foreach (var evidenceRef in draft.EvidenceRefs)
        {
            if (evidenceRef is null)
                throw new InvalidOperationException("Handoff evidence refs cannot contain null entries.");

            if (string.IsNullOrWhiteSpace(evidenceRef.EvidenceId))
                throw new InvalidOperationException("Handoff evidence refs require an evidence ID.");

            if (!Enum.IsDefined(evidenceRef.EvidenceType))
                throw new InvalidOperationException($"Unsupported handoff evidence type '{evidenceRef.EvidenceType}'.");

            if (string.IsNullOrWhiteSpace(evidenceRef.SourceId))
                throw new InvalidOperationException("Handoff evidence refs require a source ID.");
        }

        if (draft.Confidence < 0 || draft.Confidence > 1)
            throw new InvalidOperationException("Handoff confidence must be between 0 and 1.");

        if (draft.ExpiresAt is not null && draft.ExpiresAt.Value <= draft.CreatedAt)
            throw new InvalidOperationException("Handoff expiry must be after creation time.");

        if (draft.InfluenceIds is not null)
        {
            if (draft.InfluenceIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Handoff influence IDs cannot contain blank values.");

            if (draft.InfluenceIds.Select(item => item.Trim()).Distinct(StringComparer.Ordinal).Count() != draft.InfluenceIds.Count)
                throw new InvalidOperationException("Handoff influence IDs cannot contain duplicates.");
        }
    }

    private static void ThrowIfInvalidScope(AgentMemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.TenantId) ||
            string.IsNullOrWhiteSpace(scope.ProjectId) ||
            string.IsNullOrWhiteSpace(scope.CampaignId) ||
            string.IsNullOrWhiteSpace(scope.RunId) ||
            string.IsNullOrWhiteSpace(scope.AgentId))
        {
            throw new InvalidOperationException("Handoff memory operations require a complete memory scope.");
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
        public int MemoryType { get; set; }
        public int AuthorityLevel { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public int? CurrentEventType { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    private sealed class HandoffRow
    {
        public string HandoffMemorySliceId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string MemoryItemIdsJson { get; set; } = "[]";
        public string MemorySnapshotsJson { get; set; } = "[]";
        public string Summary { get; set; } = string.Empty;
        public int AllowedUse { get; set; }
        public string EvidenceRefsJson { get; set; } = "[]";
        public decimal Confidence { get; set; }
        public string? InfluenceIdsJson { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string? HandoffJson { get; set; }
    }
}

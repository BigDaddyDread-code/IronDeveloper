using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlAgentLocalMemoryStore : IAgentLocalMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAgentMemoryContractValidator _validator;

    public SqlAgentLocalMemoryStore(
        IDbConnectionFactory connectionFactory,
        IAgentMemoryContractValidator validator)
    {
        _connectionFactory = connectionFactory;
        _validator = validator;
    }

    public async Task CreateAsync(
        AgentLocalMemoryItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfInvalid(_validator.Validate(item));
        ThrowIfNotLocalAuthority(item.AuthorityLevel);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "agent.usp_AgentLocalMemory_Create",
            new
            {
                item.MemoryItemId,
                item.Scope.TenantId,
                item.Scope.ProjectId,
                item.Scope.CampaignId,
                item.Scope.RunId,
                item.Scope.AgentId,
                MemoryType = (int)item.MemoryType,
                AuthorityLevel = (int)item.AuthorityLevel,
                item.Title,
                item.Summary,
                item.Confidence,
                CreatedAtUtc = item.CreatedAt.UtcDateTime,
                CreatedByAgentId = item.Scope.AgentId,
                EvidenceRefsJson = JsonSerializer.Serialize(item.EvidenceRefs ?? Array.Empty<EvidenceRef>(), JsonOptions),
                MemoryJson = (string?)null,
                ExpiresAtUtc = item.ExpiresAt?.UtcDateTime,
                WorkflowId = (string?)null,
                TicketId = (string?)null,
                CorrelationId = (string?)null,
                ThoughtLedgerEntryId = (string?)null,
                item.SupersedesMemoryItemId,
                item.KnownLimitations
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task AddEventAsync(
        AgentMemoryScope scope,
        AgentLocalMemoryEventRecord memoryEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(memoryEvent);
        ThrowIfInvalidScope(scope);
        ThrowIfInvalidAppendEvent(memoryEvent);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var owner = await connection.QuerySingleOrDefaultAsync<MemoryOwnerRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                i.TenantId,
                i.ProjectId,
                i.CampaignId,
                i.RunId,
                i.AgentId,
                v.CurrentEventType
            FROM agent.AgentLocalMemoryItem i
            LEFT JOIN agent.vwAgentLocalMemoryCurrentState v
                ON v.MemoryItemId = i.MemoryItemId
            WHERE i.MemoryItemId = @MemoryItemId;
            """,
            new { memoryEvent.MemoryItemId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (owner is null)
            throw new InvalidOperationException($"Memory item '{memoryEvent.MemoryItemId}' does not exist.");

        if (!ScopeMatches(scope, owner))
            throw new InvalidOperationException("Memory event scope does not match the target memory item scope.");

        ThrowIfInvalidLifecycleTransition(ToLifecycleStatus(owner.CurrentEventType), memoryEvent.EventType);

        await connection.ExecuteAsync(new CommandDefinition(
            "agent.usp_AgentLocalMemory_AddEvent",
            new
            {
                memoryEvent.MemoryEventId,
                memoryEvent.MemoryItemId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId,
                EventType = (int)memoryEvent.EventType,
                memoryEvent.EventReason,
                CreatedAtUtc = memoryEvent.CreatedAt.UtcDateTime,
                memoryEvent.CreatedByAgentId,
                memoryEvent.CreatedByUserId,
                memoryEvent.DecisionId,
                memoryEvent.ThoughtLedgerEntryId,
                memoryEvent.CorrelationId,
                memoryEvent.EventJson
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentLocalMemoryItem>> QueryOwnMemoryAsync(
        AgentMemoryScope scope,
        AgentLocalMemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidScope(scope);

        using var connection = _connectionFactory.CreateConnection();
        var take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, 500);

        var rows = (await connection.QueryAsync<MemoryItemRow>(new CommandDefinition(
            """
            SELECT TOP (@Take)
                MemoryItemId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryType,
                AuthorityLevel,
                Title,
                Summary,
                Confidence,
                CreatedAtUtc,
                ExpiresAtUtc,
                SupersedesMemoryItemId,
                KnownLimitations,
                CurrentEventType,
                CurrentEventAtUtc
            FROM agent.vwAgentLocalMemoryCurrentState
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId
              AND (@MemoryType IS NULL OR MemoryType = @MemoryType)
              AND (@AuthorityLevel IS NULL OR AuthorityLevel = @AuthorityLevel)
              AND (@CreatedAfter IS NULL OR CreatedAtUtc >= @CreatedAfter)
              AND (@CreatedBefore IS NULL OR CreatedAtUtc <= @CreatedBefore)
              AND
              (
                  @IncludeExpired = 1
                  OR
                  (
                      ISNULL(CurrentEventType, 1) <> @ExpiredEventType
                      AND (ExpiresAtUtc IS NULL OR ExpiresAtUtc > SYSUTCDATETIME())
                  )
              )
            ORDER BY CreatedAtUtc DESC, MemoryItemId DESC;
            """,
            new
            {
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId,
                MemoryType = query.MemoryType is null ? (int?)null : (int)query.MemoryType.Value,
                AuthorityLevel = query.AuthorityLevel is null ? (int?)null : (int)query.AuthorityLevel.Value,
                CreatedAfter = query.CreatedAfter?.UtcDateTime,
                CreatedBefore = query.CreatedBefore?.UtcDateTime,
                IncludeExpired = query.IncludeExpired,
                ExpiredEventType = (int)AgentLocalMemoryEventType.Expired,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return await HydrateAsync(connection, rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentLocalMemoryItem?> GetOwnMemoryItemAsync(
        AgentMemoryScope scope,
        string memoryItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ThrowIfInvalidScope(scope);

        if (string.IsNullOrWhiteSpace(memoryItemId))
            return null;

        using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<MemoryItemRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                MemoryItemId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryType,
                AuthorityLevel,
                Title,
                Summary,
                Confidence,
                CreatedAtUtc,
                ExpiresAtUtc,
                SupersedesMemoryItemId,
                KnownLimitations,
                CurrentEventType,
                CurrentEventAtUtc
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
                MemoryItemId = memoryItemId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
            return null;

        var hydrated = await HydrateAsync(connection, [row], cancellationToken).ConfigureAwait(false);
        return hydrated.SingleOrDefault();
    }

    public async Task<IReadOnlyList<AgentLocalMemoryEventRecord>> GetEventHistoryAsync(
        AgentMemoryScope scope,
        string memoryItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ThrowIfInvalidScope(scope);

        if (string.IsNullOrWhiteSpace(memoryItemId))
            return Array.Empty<AgentLocalMemoryEventRecord>();

        using var connection = _connectionFactory.CreateConnection();

        var rows = (await connection.QueryAsync<EventRow>(new CommandDefinition(
            """
            SELECT
                e.MemoryEventId,
                e.MemoryItemId,
                e.EventType,
                e.EventReason,
                e.CreatedAtUtc,
                e.CreatedByAgentId,
                e.CreatedByUserId,
                e.CorrelationId,
                e.DecisionId,
                e.ThoughtLedgerEntryId,
                e.EventJson
            FROM agent.AgentLocalMemoryEvent e
            INNER JOIN agent.AgentLocalMemoryItem i
                ON i.MemoryItemId = e.MemoryItemId
            WHERE e.MemoryItemId = @MemoryItemId
              AND i.TenantId = @TenantId
              AND i.ProjectId = @ProjectId
              AND i.CampaignId = @CampaignId
              AND i.RunId = @RunId
              AND i.AgentId = @AgentId
            ORDER BY e.CreatedAtUtc, e.MemoryEventId;
            """,
            new
            {
                MemoryItemId = memoryItemId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return rows.Select(ToEvent).ToArray();
    }

    private static async Task<IReadOnlyList<AgentLocalMemoryItem>> HydrateAsync(
        IDbConnection connection,
        IReadOnlyList<MemoryItemRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return Array.Empty<AgentLocalMemoryItem>();

        var ids = rows.Select(row => row.MemoryItemId).ToArray();
        var evidenceRows = (await connection.QueryAsync<EvidenceRow>(new CommandDefinition(
            """
            SELECT MemoryItemId, EvidenceId, EvidenceType, SourceId, SourceUri, Summary, CapturedAtUtc
            FROM agent.AgentLocalMemoryEvidenceRef
            WHERE MemoryItemId IN @MemoryItemIds
            ORDER BY EvidenceRefRowId;
            """,
            new { MemoryItemIds = ids },
            cancellationToken: cancellationToken)).ConfigureAwait(false))
            .GroupBy(row => row.MemoryItemId)
            .ToDictionary(group => group.Key, group => group.Select(ToEvidence).ToArray());

        return rows.Select(row => ToItem(row, evidenceRows.TryGetValue(row.MemoryItemId, out var evidence)
            ? evidence
            : Array.Empty<EvidenceRef>())).ToArray();
    }

    private static AgentLocalMemoryItem ToItem(MemoryItemRow row, IReadOnlyList<EvidenceRef> evidenceRefs) =>
        new()
        {
            MemoryItemId = row.MemoryItemId,
            Scope = new AgentMemoryScope
            {
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                CampaignId = row.CampaignId,
                RunId = row.RunId,
                AgentId = row.AgentId
            },
            MemoryType = (AgentMemoryType)row.MemoryType,
            AuthorityLevel = (MemoryAuthorityLevel)row.AuthorityLevel,
            Title = row.Title,
            Summary = row.Summary,
            EvidenceRefs = evidenceRefs,
            Confidence = row.Confidence,
            Status = ToLifecycleStatus(row.CurrentEventType),
            CreatedAt = ToUtc(row.CreatedAtUtc),
            ExpiresAt = row.ExpiresAtUtc is null ? null : ToUtc(row.ExpiresAtUtc.Value),
            SupersedesMemoryItemId = row.SupersedesMemoryItemId,
            KnownLimitations = row.KnownLimitations
        };

    private static EvidenceRef ToEvidence(EvidenceRow row) =>
        new()
        {
            EvidenceId = row.EvidenceId,
            EvidenceType = (EvidenceType)row.EvidenceType,
            SourceId = row.SourceId,
            SourceUri = row.SourceUri,
            Summary = row.Summary,
            CapturedAt = row.CapturedAtUtc is null ? null : ToUtc(row.CapturedAtUtc.Value)
        };

    private static AgentLocalMemoryEventRecord ToEvent(EventRow row) =>
        new()
        {
            MemoryEventId = row.MemoryEventId,
            MemoryItemId = row.MemoryItemId,
            EventType = (AgentLocalMemoryEventType)row.EventType,
            EventReason = row.EventReason,
            CreatedAt = ToUtc(row.CreatedAtUtc),
            CreatedByAgentId = row.CreatedByAgentId,
            CreatedByUserId = row.CreatedByUserId,
            CorrelationId = row.CorrelationId,
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            EventJson = row.EventJson
        };

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

    private static void ThrowIfInvalid(MemoryValidationResult result)
    {
        if (result.IsValid)
            return;

        throw new InvalidOperationException("Agent local memory failed validation: " +
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private static void ThrowIfInvalidScope(AgentMemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.TenantId) ||
            string.IsNullOrWhiteSpace(scope.ProjectId) ||
            string.IsNullOrWhiteSpace(scope.CampaignId) ||
            string.IsNullOrWhiteSpace(scope.RunId) ||
            string.IsNullOrWhiteSpace(scope.AgentId))
        {
            throw new InvalidOperationException("Agent local memory operations require a complete memory scope.");
        }
    }

    private static void ThrowIfNotLocalAuthority(MemoryAuthorityLevel authorityLevel)
    {
        if (authorityLevel is MemoryAuthorityLevel.ObservedOnly or MemoryAuthorityLevel.CandidatePattern)
            return;

        throw new InvalidOperationException("Local agent memory can only be created as ObservedOnly or CandidatePattern.");
    }

    private static void ThrowIfInvalidEventIdentity(AgentLocalMemoryEventRecord memoryEvent)
    {
        if (string.IsNullOrWhiteSpace(memoryEvent.MemoryEventId))
            throw new InvalidOperationException("Memory event ID is required.");

        if (string.IsNullOrWhiteSpace(memoryEvent.MemoryItemId))
            throw new InvalidOperationException("Memory event item ID is required.");
    }

    private static void ThrowIfInvalidAppendEvent(AgentLocalMemoryEventRecord memoryEvent)
    {
        ThrowIfInvalidEventIdentity(memoryEvent);

        if (memoryEvent.EventType == AgentLocalMemoryEventType.Created)
            throw new InvalidOperationException("Created memory events are inserted only by CreateAsync and cannot be appended as lifecycle transitions.");

        if (memoryEvent.EventType is AgentLocalMemoryEventType.Rejected or AgentLocalMemoryEventType.Accepted)
            throw new InvalidOperationException("Local agent memory cannot append Rejected or Accepted lifecycle events.");

        if (memoryEvent.EventType is not (AgentLocalMemoryEventType.Superseded or AgentLocalMemoryEventType.Expired or AgentLocalMemoryEventType.Invalidated or AgentLocalMemoryEventType.ProposedForReview))
            throw new InvalidOperationException($"Unsupported local memory lifecycle event '{memoryEvent.EventType}'.");
    }

    private static void ThrowIfInvalidLifecycleTransition(MemoryLifecycleStatus currentStatus, AgentLocalMemoryEventType nextEventType)
    {
        var allowed = currentStatus switch
        {
            MemoryLifecycleStatus.Active => nextEventType is
                AgentLocalMemoryEventType.Superseded or
                AgentLocalMemoryEventType.Expired or
                AgentLocalMemoryEventType.Invalidated or
                AgentLocalMemoryEventType.ProposedForReview,
            MemoryLifecycleStatus.ProposedForReview => nextEventType is
                AgentLocalMemoryEventType.Expired or
                AgentLocalMemoryEventType.Invalidated,
            MemoryLifecycleStatus.Expired => nextEventType == AgentLocalMemoryEventType.Invalidated,
            _ => false
        };

        if (!allowed)
            throw new InvalidOperationException($"Invalid local memory lifecycle transition from {currentStatus} to {nextEventType}.");
    }

    private static bool ScopeMatches(AgentMemoryScope scope, MemoryOwnerRow owner) =>
        string.Equals(scope.TenantId, owner.TenantId, StringComparison.Ordinal) &&
        string.Equals(scope.ProjectId, owner.ProjectId, StringComparison.Ordinal) &&
        string.Equals(scope.CampaignId, owner.CampaignId, StringComparison.Ordinal) &&
        string.Equals(scope.RunId, owner.RunId, StringComparison.Ordinal) &&
        string.Equals(scope.AgentId, owner.AgentId, StringComparison.Ordinal);

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

    private class MemoryOwnerRow
    {
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int? CurrentEventType { get; set; }
    }

    private sealed class MemoryItemRow : MemoryOwnerRow
    {
        public string MemoryItemId { get; set; } = string.Empty;
        public int MemoryType { get; set; }
        public int AuthorityLevel { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string? SupersedesMemoryItemId { get; set; }
        public string? KnownLimitations { get; set; }
        public DateTime? CurrentEventAtUtc { get; set; }
    }

    private sealed class EvidenceRow
    {
        public string MemoryItemId { get; set; } = string.Empty;
        public string EvidenceId { get; set; } = string.Empty;
        public int EvidenceType { get; set; }
        public string SourceId { get; set; } = string.Empty;
        public string? SourceUri { get; set; }
        public string? Summary { get; set; }
        public DateTime? CapturedAtUtc { get; set; }
    }

    private sealed class EventRow
    {
        public string MemoryEventId { get; set; } = string.Empty;
        public string MemoryItemId { get; set; } = string.Empty;
        public int EventType { get; set; }
        public string? EventReason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedByAgentId { get; set; }
        public string? CreatedByUserId { get; set; }
        public string? CorrelationId { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? EventJson { get; set; }
    }
}

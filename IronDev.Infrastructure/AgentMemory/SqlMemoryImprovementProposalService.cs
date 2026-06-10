using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlMemoryImprovementProposalService : IMemoryImprovementProposalService
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

    public SqlMemoryImprovementProposalService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(
        MemoryImprovementProposalDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfInvalidDraftShape(draft);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        await ValidateSourcesAsync(connection, draft.Scope, draft.Sources, cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "agent.usp_MemoryImprovementProposal_Create",
            new
            {
                draft.ProposalId,
                draft.Scope.TenantId,
                draft.Scope.ProjectId,
                draft.Scope.CampaignId,
                draft.Scope.RunId,
                draft.Scope.AgentId,
                ProposalType = (int)draft.ProposalType,
                draft.Title,
                draft.Summary,
                SourcesJson = JsonSerializer.Serialize(draft.Sources, JsonOptions),
                EvidenceRefsJson = JsonSerializer.Serialize(draft.EvidenceRefs, JsonOptions),
                draft.Confidence,
                draft.ProposedByAgentId,
                draft.ProposedByUserId,
                CreatedAtUtc = draft.CreatedAt.UtcDateTime,
                draft.ThoughtLedgerEntryId,
                draft.CorrelationId,
                draft.ProposalJson
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task AddEventAsync(
        AgentMemoryScope scope,
        MemoryImprovementProposalEventDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(draft);
        ThrowIfInvalidScope(scope);
        ThrowIfInvalidEventDraft(draft);

        if (draft.EventType == MemoryImprovementProposalEventType.Submitted)
            throw new InvalidOperationException("Submitted proposal events are inserted only by CreateAsync.");

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var proposal = await LoadProposalAsync(connection, scope, draft.ProposalId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
            throw new InvalidOperationException("Memory improvement proposal does not exist in the requested scope.");

        var currentStatus = await LoadCurrentStatusAsync(connection, draft.ProposalId, cancellationToken).ConfigureAwait(false);
        ThrowIfInvalidLifecycleTransition(currentStatus, draft.EventType);

        await connection.ExecuteAsync(new CommandDefinition(
            "agent.usp_MemoryImprovementProposal_AddEvent",
            new
            {
                draft.ProposalEventId,
                draft.ProposalId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId,
                EventType = (int)draft.EventType,
                draft.Reason,
                CreatedAtUtc = draft.CreatedAt.UtcDateTime,
                draft.CreatedByUserId,
                draft.CreatedByAgentId,
                draft.ThoughtLedgerEntryId,
                draft.CorrelationId,
                draft.EventJson
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryImprovementProposalRecord>> QueryAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        MemoryImprovementProposalQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidScopeParts(tenantId, projectId, campaignId, runId);

        using var connection = _connectionFactory.CreateConnection();
        var take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, 500);

        var rows = (await connection.QueryAsync<ProposalRow>(new CommandDefinition(
            """
            WITH LatestEvent AS
            (
                SELECT
                    ProposalId,
                    EventType,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY ProposalId
                        ORDER BY CreatedAtUtc DESC, ProposalEventId DESC
                    ) AS rn
                FROM agent.AgentMemoryImprovementProposalEvent
            )
            SELECT TOP (@Take)
                p.ProposalId,
                p.TenantId,
                p.ProjectId,
                p.CampaignId,
                p.RunId,
                p.AgentId,
                p.ProposalType,
                ISNULL(le.EventType, 1) AS CurrentStatus,
                p.Title,
                p.Summary,
                p.SourcesJson,
                p.EvidenceRefsJson,
                p.Confidence,
                p.CreatedAtUtc,
                p.ProposedByAgentId,
                p.ProposedByUserId,
                p.CorrelationId,
                p.ThoughtLedgerEntryId,
                p.ProposalJson
            FROM agent.AgentMemoryImprovementProposal p
            LEFT JOIN LatestEvent le
                ON le.ProposalId = p.ProposalId
               AND le.rn = 1
            WHERE p.TenantId = @TenantId
              AND p.ProjectId = @ProjectId
              AND p.CampaignId = @CampaignId
              AND p.RunId = @RunId
              AND (@AgentId IS NULL OR p.AgentId = @AgentId)
              AND (@ProposalType IS NULL OR p.ProposalType = @ProposalType)
              AND (@Status IS NULL OR ISNULL(le.EventType, 1) = @Status)
              AND (@CreatedAfter IS NULL OR p.CreatedAtUtc >= @CreatedAfter)
              AND (@CreatedBefore IS NULL OR p.CreatedAtUtc <= @CreatedBefore)
            ORDER BY p.CreatedAtUtc DESC, p.ProposalId DESC;
            """,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                query.AgentId,
                ProposalType = query.ProposalType is null ? (int?)null : (int)query.ProposalType.Value,
                Status = query.Status is null ? (int?)null : (int)query.Status.Value,
                CreatedAfter = query.CreatedAfter?.UtcDateTime,
                CreatedBefore = query.CreatedBefore?.UtcDateTime,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return rows.Select(ToRecord).ToArray();
    }

    public async Task<IReadOnlyList<MemoryImprovementProposalEventRecord>> GetEventsAsync(
        AgentMemoryScope scope,
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ThrowIfInvalidScope(scope);

        if (string.IsNullOrWhiteSpace(proposalId))
            return Array.Empty<MemoryImprovementProposalEventRecord>();

        using var connection = _connectionFactory.CreateConnection();
        var proposal = await LoadProposalAsync(connection, scope, proposalId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
            return Array.Empty<MemoryImprovementProposalEventRecord>();

        var rows = await connection.QueryAsync<EventRow>(new CommandDefinition(
            """
            SELECT
                ProposalEventId,
                ProposalId,
                EventType,
                Reason,
                CreatedAtUtc,
                CreatedByUserId,
                CreatedByAgentId,
                ThoughtLedgerEntryId,
                CorrelationId,
                EventJson
            FROM agent.AgentMemoryImprovementProposalEvent
            WHERE ProposalId = @ProposalId
            ORDER BY CreatedAtUtc, ProposalEventId;
            """,
            new { ProposalId = proposalId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToEventRecord).ToArray();
    }

    private static async Task<ProposalRow?> LoadProposalAsync(
        IDbConnection connection,
        AgentMemoryScope scope,
        string proposalId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<ProposalRow>(new CommandDefinition(
            """
            SELECT TOP (1)
                ProposalId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                ProposalType,
                1 AS CurrentStatus,
                Title,
                Summary,
                SourcesJson,
                EvidenceRefsJson,
                Confidence,
                CreatedAtUtc,
                ProposedByAgentId,
                ProposedByUserId,
                CorrelationId,
                ThoughtLedgerEntryId,
                ProposalJson
            FROM agent.AgentMemoryImprovementProposal
            WHERE ProposalId = @ProposalId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId;
            """,
            new
            {
                ProposalId = proposalId,
                scope.TenantId,
                scope.ProjectId,
                scope.CampaignId,
                scope.RunId,
                scope.AgentId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task<MemoryImprovementProposalStatus> LoadCurrentStatusAsync(
        IDbConnection connection,
        string proposalId,
        CancellationToken cancellationToken)
    {
        var eventType = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            SELECT TOP (1) EventType
            FROM agent.AgentMemoryImprovementProposalEvent
            WHERE ProposalId = @ProposalId
            ORDER BY CreatedAtUtc DESC, ProposalEventId DESC;
            """,
            new { ProposalId = proposalId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return eventType is null
            ? MemoryImprovementProposalStatus.Submitted
            : (MemoryImprovementProposalStatus)eventType.Value;
    }

    private static async Task ValidateSourcesAsync(
        IDbConnection connection,
        AgentMemoryScope scope,
        IReadOnlyList<MemoryImprovementProposalSource> sources,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            if (!string.IsNullOrWhiteSpace(source.MemoryItemId))
            {
                var count = await connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM agent.AgentLocalMemoryItem
                    WHERE MemoryItemId = @MemoryItemId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                      AND CampaignId = @CampaignId
                      AND RunId = @RunId
                      AND AgentId = @AgentId;
                    """,
                    new
                    {
                        source.MemoryItemId,
                        scope.TenantId,
                        scope.ProjectId,
                        scope.CampaignId,
                        scope.RunId,
                        scope.AgentId
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (count != 1)
                    throw new InvalidOperationException("Proposal memory item source must belong to the exact proposal scope.");
            }

            if (!string.IsNullOrWhiteSpace(source.InfluenceId))
            {
                var count = await connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM agent.AgentMemoryInfluenceRecord
                    WHERE InfluenceId = @InfluenceId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                      AND CampaignId = @CampaignId
                      AND RunId = @RunId
                      AND AgentId = @AgentId;
                    """,
                    new
                    {
                        source.InfluenceId,
                        scope.TenantId,
                        scope.ProjectId,
                        scope.CampaignId,
                        scope.RunId,
                        scope.AgentId
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (count != 1)
                    throw new InvalidOperationException("Proposal influence source must belong to the exact proposal scope.");
            }

            if (!string.IsNullOrWhiteSpace(source.HandoffMemorySliceId))
            {
                var count = await connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM agent.AgentMemoryHandoffSlice
                    WHERE HandoffMemorySliceId = @HandoffMemorySliceId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                      AND CampaignId = @CampaignId
                      AND RunId = @RunId
                      AND (SourceAgentId = @AgentId OR TargetAgentId = @AgentId);
                    """,
                    new
                    {
                        source.HandoffMemorySliceId,
                        scope.TenantId,
                        scope.ProjectId,
                        scope.CampaignId,
                        scope.RunId,
                        scope.AgentId
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (count != 1)
                    throw new InvalidOperationException("Proposal handoff source must belong to the proposal run and involve the proposal agent.");
            }

            if (!string.IsNullOrWhiteSpace(source.RunMemoryFindingType) &&
                !Enum.TryParse<RunMemoryFindingType>(source.RunMemoryFindingType, ignoreCase: false, out _))
            {
                throw new InvalidOperationException($"Unsupported run memory finding type '{source.RunMemoryFindingType}'.");
            }
        }
    }

    private static MemoryImprovementProposalRecord ToRecord(ProposalRow row) =>
        new()
        {
            ProposalId = row.ProposalId,
            Scope = new AgentMemoryScope
            {
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                CampaignId = row.CampaignId,
                RunId = row.RunId,
                AgentId = row.AgentId
            },
            ProposalType = (MemoryImprovementProposalType)row.ProposalType,
            CurrentStatus = (MemoryImprovementProposalStatus)row.CurrentStatus,
            Title = row.Title,
            Summary = row.Summary,
            Sources = JsonSerializer.Deserialize<IReadOnlyList<MemoryImprovementProposalSource>>(row.SourcesJson, JsonOptions) ?? Array.Empty<MemoryImprovementProposalSource>(),
            EvidenceRefs = JsonSerializer.Deserialize<IReadOnlyList<EvidenceRef>>(row.EvidenceRefsJson, JsonOptions) ?? Array.Empty<EvidenceRef>(),
            Confidence = row.Confidence,
            CreatedAt = ToUtc(row.CreatedAtUtc),
            ProposedByAgentId = row.ProposedByAgentId,
            ProposedByUserId = row.ProposedByUserId,
            CorrelationId = row.CorrelationId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            ProposalJson = row.ProposalJson
        };

    private static MemoryImprovementProposalEventRecord ToEventRecord(EventRow row) =>
        new()
        {
            ProposalEventId = row.ProposalEventId,
            ProposalId = row.ProposalId,
            EventType = (MemoryImprovementProposalEventType)row.EventType,
            Reason = row.Reason,
            CreatedAt = ToUtc(row.CreatedAtUtc),
            CreatedByUserId = row.CreatedByUserId,
            CreatedByAgentId = row.CreatedByAgentId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            EventJson = row.EventJson
        };

    private static void ThrowIfInvalidDraftShape(MemoryImprovementProposalDraft draft)
    {
        ThrowIfInvalidScope(draft.Scope);

        if (string.IsNullOrWhiteSpace(draft.ProposalId))
            throw new InvalidOperationException("Memory improvement proposal ID is required.");

        if (!Enum.IsDefined(draft.ProposalType))
            throw new InvalidOperationException($"Unsupported memory improvement proposal type '{draft.ProposalType}'.");

        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new InvalidOperationException("Memory improvement proposal title is required.");

        if (string.IsNullOrWhiteSpace(draft.Summary))
            throw new InvalidOperationException("Memory improvement proposal summary is required.");

        if (draft.Sources is null || draft.Sources.Count == 0)
            throw new InvalidOperationException("Memory improvement proposals require source references.");

        if (draft.Sources.Any(source => source is null || !HasAnySourceReference(source)))
            throw new InvalidOperationException("Memory improvement proposal sources cannot be blank.");

        if (draft.EvidenceRefs is null || draft.EvidenceRefs.Count == 0)
            throw new InvalidOperationException("Memory improvement proposals require evidence references.");

        foreach (var evidenceRef in draft.EvidenceRefs)
            ThrowIfInvalidEvidence(evidenceRef);

        if (draft.Confidence < 0 || draft.Confidence > 1)
            throw new InvalidOperationException("Memory improvement proposal confidence must be between 0 and 1.");

        if (string.IsNullOrWhiteSpace(draft.ProposedByAgentId) && string.IsNullOrWhiteSpace(draft.ProposedByUserId))
            throw new InvalidOperationException("Memory improvement proposals require an agent or user proposer.");

        if (!string.IsNullOrWhiteSpace(draft.ProposedByAgentId) &&
            !string.Equals(draft.ProposedByAgentId, draft.Scope.AgentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Agent-created memory improvement proposals must be created by the scoped agent.");
        }

        ThrowIfContainsPrivateReasoning(draft.ProposalJson, "Memory improvement proposal JSON");
    }

    private static void ThrowIfInvalidEventDraft(MemoryImprovementProposalEventDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.ProposalEventId))
            throw new InvalidOperationException("Memory improvement proposal event ID is required.");

        if (string.IsNullOrWhiteSpace(draft.ProposalId))
            throw new InvalidOperationException("Memory improvement proposal event proposal ID is required.");

        if (!Enum.IsDefined(draft.EventType))
            throw new InvalidOperationException($"Unsupported memory improvement proposal event type '{draft.EventType}'.");

        if (string.IsNullOrWhiteSpace(draft.CreatedByAgentId) && string.IsNullOrWhiteSpace(draft.CreatedByUserId))
            throw new InvalidOperationException("Memory improvement proposal events require an agent or user actor.");

        ThrowIfContainsPrivateReasoning(draft.EventJson, "Memory improvement proposal event JSON");
    }

    private static void ThrowIfInvalidEvidence(EvidenceRef evidenceRef)
    {
        if (evidenceRef is null)
            throw new InvalidOperationException("Memory improvement proposal evidence refs cannot contain null entries.");

        if (string.IsNullOrWhiteSpace(evidenceRef.EvidenceId))
            throw new InvalidOperationException("Memory improvement proposal evidence refs require an evidence ID.");

        if (!Enum.IsDefined(evidenceRef.EvidenceType))
            throw new InvalidOperationException($"Unsupported memory improvement proposal evidence type '{evidenceRef.EvidenceType}'.");

        if (string.IsNullOrWhiteSpace(evidenceRef.SourceId))
            throw new InvalidOperationException("Memory improvement proposal evidence refs require a source ID.");
    }

    private static void ThrowIfInvalidLifecycleTransition(
        MemoryImprovementProposalStatus currentStatus,
        MemoryImprovementProposalEventType nextEventType)
    {
        var allowed = currentStatus == MemoryImprovementProposalStatus.Submitted &&
            nextEventType is MemoryImprovementProposalEventType.Withdrawn or
                MemoryImprovementProposalEventType.Rejected or
                MemoryImprovementProposalEventType.AcceptedForFutureImplementation or
                MemoryImprovementProposalEventType.Superseded;

        if (!allowed)
            throw new InvalidOperationException($"Invalid memory improvement proposal lifecycle transition from {currentStatus} to {nextEventType}.");
    }

    private static bool HasAnySourceReference(MemoryImprovementProposalSource source) =>
        !string.IsNullOrWhiteSpace(source.MemoryItemId) ||
        !string.IsNullOrWhiteSpace(source.InfluenceId) ||
        !string.IsNullOrWhiteSpace(source.HandoffMemorySliceId) ||
        !string.IsNullOrWhiteSpace(source.RunMemoryFindingType) ||
        !string.IsNullOrWhiteSpace(source.ThoughtLedgerEntryId) ||
        !string.IsNullOrWhiteSpace(source.DecisionId);

    private static void ThrowIfInvalidScope(AgentMemoryScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.TenantId) ||
            string.IsNullOrWhiteSpace(scope.ProjectId) ||
            string.IsNullOrWhiteSpace(scope.CampaignId) ||
            string.IsNullOrWhiteSpace(scope.RunId) ||
            string.IsNullOrWhiteSpace(scope.AgentId))
        {
            throw new InvalidOperationException("Memory improvement proposal operations require a complete memory scope.");
        }
    }

    private static void ThrowIfInvalidScopeParts(
        string tenantId,
        string projectId,
        string campaignId,
        string runId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(campaignId) ||
            string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("Memory improvement proposal queries require tenant, project, campaign, and run identity.");
        }
    }

    private static void ThrowIfContainsPrivateReasoning(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        foreach (var token in BannedPrivateReasoningTokens)
        {
            if (json.Contains(token, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{fieldName} must not contain raw private reasoning field '{token}'.");
        }
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

    private sealed class ProposalRow
    {
        public string ProposalId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int ProposalType { get; set; }
        public int CurrentStatus { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SourcesJson { get; set; } = "[]";
        public string EvidenceRefsJson { get; set; } = "[]";
        public decimal Confidence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? ProposedByAgentId { get; set; }
        public string? ProposedByUserId { get; set; }
        public string? CorrelationId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? ProposalJson { get; set; }
    }

    private sealed class EventRow
    {
        public string ProposalEventId { get; set; } = string.Empty;
        public string ProposalId { get; set; } = string.Empty;
        public int EventType { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedByUserId { get; set; }
        public string? CreatedByAgentId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public string? EventJson { get; set; }
    }
}

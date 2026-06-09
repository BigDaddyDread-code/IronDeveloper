using System.Data;
using System.Data.Common;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlAgentLocalMemoryStore : IAgentLocalMemoryStore
{
    private static readonly AgentLocalMemoryEventType[] LocalEventTypes =
    [
        AgentLocalMemoryEventType.Created,
        AgentLocalMemoryEventType.Superseded,
        AgentLocalMemoryEventType.Expired,
        AgentLocalMemoryEventType.Invalidated,
        AgentLocalMemoryEventType.ProposedForReview
    ];

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAgentMemoryContractValidator _validator;
    private int _schemaEnsured;

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

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            const string insertItem = """
                INSERT INTO agent.AgentLocalMemoryItem
                (
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
                    ContentJson,
                    ContentHashSha256
                )
                VALUES
                (
                    @MemoryItemId,
                    @TenantId,
                    @ProjectId,
                    @CampaignId,
                    @RunId,
                    @AgentId,
                    @MemoryType,
                    @AuthorityLevel,
                    @Title,
                    @Summary,
                    @Confidence,
                    @CreatedAtUtc,
                    @ExpiresAtUtc,
                    @SupersedesMemoryItemId,
                    @KnownLimitations,
                    @ContentJson,
                    @ContentHashSha256
                );
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                insertItem,
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
                    ExpiresAtUtc = item.ExpiresAt?.UtcDateTime,
                    item.SupersedesMemoryItemId,
                    item.KnownLimitations,
                    ContentJson = (string?)null,
                    ContentHashSha256 = (byte[]?)null
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            foreach (var evidence in item.EvidenceRefs ?? Array.Empty<EvidenceRef>())
            {
                await InsertEvidenceAsync(connection, transaction, item.MemoryItemId, evidence, cancellationToken)
                    .ConfigureAwait(false);
            }

            var createdEvent = new AgentLocalMemoryEventRecord
            {
                MemoryEventId = $"memevt-created-{Guid.NewGuid():N}",
                MemoryItemId = item.MemoryItemId,
                EventType = AgentLocalMemoryEventType.Created,
                EventReason = "Memory item created.",
                CreatedAt = item.CreatedAt,
                CreatedByAgentId = item.Scope.AgentId
            };

            await InsertEventAsync(connection, transaction, createdEvent, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task AddEventAsync(
        AgentMemoryScope scope,
        AgentLocalMemoryEventRecord memoryEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(memoryEvent);
        ThrowIfInvalidScope(scope);
        ThrowIfInvalidLocalEvent(memoryEvent);

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var owner = await connection.QuerySingleOrDefaultAsync<MemoryOwnerRow>(new CommandDefinition(
            """
            SELECT TOP (1) TenantId, ProjectId, CampaignId, RunId, AgentId
            FROM agent.AgentLocalMemoryItem
            WHERE MemoryItemId = @MemoryItemId;
            """,
            new { memoryEvent.MemoryItemId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (owner is null)
            throw new InvalidOperationException($"Memory item '{memoryEvent.MemoryItemId}' does not exist.");

        if (!ScopeMatches(scope, owner))
            throw new InvalidOperationException("Memory event scope does not match the target memory item scope.");

        using var transaction = connection.BeginTransaction();
        try
        {
            await InsertEventAsync(connection, transaction, memoryEvent, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<AgentLocalMemoryItem>> QueryOwnMemoryAsync(
        AgentMemoryScope scope,
        AgentLocalMemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalidScope(scope);

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
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
              AND (@IncludeExpired = 1 OR ISNULL(CurrentEventType, 1) <> 3)
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

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _schemaEnsured) == 1)
            return;

        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            IF SCHEMA_ID('agent') IS NULL
                EXEC('CREATE SCHEMA agent');

            IF OBJECT_ID('agent.AgentLocalMemoryItem', 'U') IS NULL
            BEGIN
                CREATE TABLE agent.AgentLocalMemoryItem
                (
                    MemoryItemId NVARCHAR(80) NOT NULL,
                    TenantId NVARCHAR(80) NOT NULL,
                    ProjectId NVARCHAR(80) NOT NULL,
                    CampaignId NVARCHAR(80) NOT NULL,
                    RunId NVARCHAR(80) NOT NULL,
                    AgentId NVARCHAR(120) NOT NULL,
                    MemoryType INT NOT NULL,
                    AuthorityLevel INT NOT NULL,
                    Title NVARCHAR(240) NOT NULL,
                    Summary NVARCHAR(MAX) NOT NULL,
                    Confidence DECIMAL(5,4) NOT NULL,
                    CreatedAtUtc DATETIME2(7) NOT NULL,
                    ExpiresAtUtc DATETIME2(7) NULL,
                    SupersedesMemoryItemId NVARCHAR(80) NULL,
                    KnownLimitations NVARCHAR(MAX) NULL,
                    ContentJson NVARCHAR(MAX) NULL,
                    ContentHashSha256 VARBINARY(32) NULL,
                    CONSTRAINT PK_AgentLocalMemoryItem PRIMARY KEY (MemoryItemId),
                    CONSTRAINT CK_AgentLocalMemoryItem_Confidence CHECK (Confidence >= 0 AND Confidence <= 1),
                    CONSTRAINT CK_AgentLocalMemoryItem_LocalAuthority CHECK (AuthorityLevel IN (1, 2)),
                    CONSTRAINT CK_AgentLocalMemoryItem_MemoryType CHECK (MemoryType IN (1, 2, 3, 4, 5, 6)),
                    CONSTRAINT CK_AgentLocalMemoryItem_CandidatePatternLimitations CHECK
                    (
                        MemoryType <> 6
                        OR (KnownLimitations IS NOT NULL AND LEN(LTRIM(RTRIM(KnownLimitations))) > 0)
                    ),
                    CONSTRAINT FK_AgentLocalMemoryItem_Supersedes FOREIGN KEY (SupersedesMemoryItemId)
                        REFERENCES agent.AgentLocalMemoryItem (MemoryItemId)
                );
            END

            IF OBJECT_ID('agent.AgentLocalMemoryEvidenceRef', 'U') IS NULL
            BEGIN
                CREATE TABLE agent.AgentLocalMemoryEvidenceRef
                (
                    EvidenceRefRowId BIGINT IDENTITY(1,1) NOT NULL,
                    MemoryItemId NVARCHAR(80) NOT NULL,
                    EvidenceId NVARCHAR(120) NOT NULL,
                    EvidenceType INT NOT NULL,
                    SourceId NVARCHAR(160) NOT NULL,
                    SourceUri NVARCHAR(1024) NULL,
                    Summary NVARCHAR(MAX) NULL,
                    CapturedAtUtc DATETIME2(7) NULL,
                    CONSTRAINT PK_AgentLocalMemoryEvidenceRef PRIMARY KEY (EvidenceRefRowId),
                    CONSTRAINT FK_AgentLocalMemoryEvidenceRef_MemoryItem FOREIGN KEY (MemoryItemId)
                        REFERENCES agent.AgentLocalMemoryItem (MemoryItemId),
                    CONSTRAINT UQ_AgentLocalMemoryEvidenceRef_MemoryItem_Evidence UNIQUE (MemoryItemId, EvidenceId),
                    CONSTRAINT CK_AgentLocalMemoryEvidenceRef_EvidenceType CHECK (EvidenceType BETWEEN 1 AND 12)
                );
            END

            IF OBJECT_ID('agent.AgentLocalMemoryEvent', 'U') IS NULL
            BEGIN
                CREATE TABLE agent.AgentLocalMemoryEvent
                (
                    MemoryEventId NVARCHAR(80) NOT NULL,
                    MemoryItemId NVARCHAR(80) NOT NULL,
                    EventType INT NOT NULL,
                    EventReason NVARCHAR(MAX) NULL,
                    CreatedAtUtc DATETIME2(7) NOT NULL,
                    CreatedByAgentId NVARCHAR(120) NULL,
                    CreatedByUserId NVARCHAR(120) NULL,
                    CorrelationId NVARCHAR(120) NULL,
                    DecisionId NVARCHAR(120) NULL,
                    ThoughtLedgerEntryId NVARCHAR(120) NULL,
                    EventJson NVARCHAR(MAX) NULL,
                    CONSTRAINT PK_AgentLocalMemoryEvent PRIMARY KEY (MemoryEventId),
                    CONSTRAINT FK_AgentLocalMemoryEvent_MemoryItem FOREIGN KEY (MemoryItemId)
                        REFERENCES agent.AgentLocalMemoryItem (MemoryItemId),
                    CONSTRAINT CK_AgentLocalMemoryEvent_EventType CHECK (EventType IN (1, 2, 3, 4, 5, 6, 7))
                );
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentLocalMemoryItem_Scope' AND object_id = OBJECT_ID('agent.AgentLocalMemoryItem'))
                CREATE INDEX IX_AgentLocalMemoryItem_Scope ON agent.AgentLocalMemoryItem(TenantId, ProjectId, CampaignId, RunId, AgentId, CreatedAtUtc);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentLocalMemoryItem_CampaignAgent' AND object_id = OBJECT_ID('agent.AgentLocalMemoryItem'))
                CREATE INDEX IX_AgentLocalMemoryItem_CampaignAgent ON agent.AgentLocalMemoryItem(TenantId, ProjectId, CampaignId, AgentId, CreatedAtUtc);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentLocalMemoryEvent_MemoryItem_CreatedAt' AND object_id = OBJECT_ID('agent.AgentLocalMemoryEvent'))
                CREATE INDEX IX_AgentLocalMemoryEvent_MemoryItem_CreatedAt ON agent.AgentLocalMemoryEvent(MemoryItemId, CreatedAtUtc);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentLocalMemoryEvidenceRef_MemoryItem' AND object_id = OBJECT_ID('agent.AgentLocalMemoryEvidenceRef'))
                CREATE INDEX IX_AgentLocalMemoryEvidenceRef_MemoryItem ON agent.AgentLocalMemoryEvidenceRef(MemoryItemId);

            IF OBJECT_ID('agent.TR_AgentLocalMemoryItem_BlockUpdateDelete', 'TR') IS NULL
                EXEC('
                    CREATE TRIGGER agent.TR_AgentLocalMemoryItem_BlockUpdateDelete
                    ON agent.AgentLocalMemoryItem
                    AFTER UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 51001, ''AgentLocalMemoryItem is append-only. Use memory events instead of update/delete.'', 1;
                    END');

            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete', 'TR') IS NULL
                EXEC('
                    CREATE TRIGGER agent.TR_AgentLocalMemoryEvidenceRef_BlockUpdateDelete
                    ON agent.AgentLocalMemoryEvidenceRef
                    AFTER UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 51002, ''AgentLocalMemoryEvidenceRef is append-only. Evidence cannot be silently changed.'', 1;
                    END');

            IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete', 'TR') IS NULL
                EXEC('
                    CREATE TRIGGER agent.TR_AgentLocalMemoryEvent_BlockUpdateDelete
                    ON agent.AgentLocalMemoryEvent
                    AFTER UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 51003, ''AgentLocalMemoryEvent is append-only. Events cannot be updated or deleted.'', 1;
                    END');

            IF OBJECT_ID('agent.vwAgentLocalMemoryCurrentState', 'V') IS NULL
                EXEC('
                    CREATE VIEW agent.vwAgentLocalMemoryCurrentState
                    AS
                    WITH LatestEvent AS
                    (
                        SELECT
                            e.MemoryItemId,
                            e.EventType,
                            e.CreatedAtUtc,
                            ROW_NUMBER() OVER
                            (
                                PARTITION BY e.MemoryItemId
                                ORDER BY e.CreatedAtUtc DESC, e.MemoryEventId DESC
                            ) AS rn
                        FROM agent.AgentLocalMemoryEvent e
                    )
                    SELECT
                        i.MemoryItemId,
                        i.TenantId,
                        i.ProjectId,
                        i.CampaignId,
                        i.RunId,
                        i.AgentId,
                        i.MemoryType,
                        i.AuthorityLevel,
                        i.Title,
                        i.Summary,
                        i.Confidence,
                        i.CreatedAtUtc,
                        i.ExpiresAtUtc,
                        i.SupersedesMemoryItemId,
                        i.KnownLimitations,
                        le.EventType AS CurrentEventType,
                        le.CreatedAtUtc AS CurrentEventAtUtc
                    FROM agent.AgentLocalMemoryItem i
                    LEFT JOIN LatestEvent le
                        ON i.MemoryItemId = le.MemoryItemId
                       AND le.rn = 1');
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        Volatile.Write(ref _schemaEnsured, 1);
    }

    private static async Task InsertEvidenceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string memoryItemId,
        EvidenceRef evidence,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO agent.AgentLocalMemoryEvidenceRef
            (
                MemoryItemId,
                EvidenceId,
                EvidenceType,
                SourceId,
                SourceUri,
                Summary,
                CapturedAtUtc
            )
            VALUES
            (
                @MemoryItemId,
                @EvidenceId,
                @EvidenceType,
                @SourceId,
                @SourceUri,
                @Summary,
                @CapturedAtUtc
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                MemoryItemId = memoryItemId,
                evidence.EvidenceId,
                EvidenceType = (int)evidence.EvidenceType,
                evidence.SourceId,
                evidence.SourceUri,
                evidence.Summary,
                CapturedAtUtc = evidence.CapturedAt?.UtcDateTime
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task InsertEventAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        AgentLocalMemoryEventRecord memoryEvent,
        CancellationToken cancellationToken)
    {
        ThrowIfInvalidLocalEvent(memoryEvent);

        const string sql = """
            INSERT INTO agent.AgentLocalMemoryEvent
            (
                MemoryEventId,
                MemoryItemId,
                EventType,
                EventReason,
                CreatedAtUtc,
                CreatedByAgentId,
                CreatedByUserId,
                CorrelationId,
                DecisionId,
                ThoughtLedgerEntryId,
                EventJson
            )
            VALUES
            (
                @MemoryEventId,
                @MemoryItemId,
                @EventType,
                @EventReason,
                @CreatedAtUtc,
                @CreatedByAgentId,
                @CreatedByUserId,
                @CorrelationId,
                @DecisionId,
                @ThoughtLedgerEntryId,
                @EventJson
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                memoryEvent.MemoryEventId,
                memoryEvent.MemoryItemId,
                EventType = (int)memoryEvent.EventType,
                memoryEvent.EventReason,
                CreatedAtUtc = memoryEvent.CreatedAt.UtcDateTime,
                memoryEvent.CreatedByAgentId,
                memoryEvent.CreatedByUserId,
                memoryEvent.CorrelationId,
                memoryEvent.DecisionId,
                memoryEvent.ThoughtLedgerEntryId,
                memoryEvent.EventJson
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
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

    private static void ThrowIfInvalidLocalEvent(AgentLocalMemoryEventRecord memoryEvent)
    {
        if (string.IsNullOrWhiteSpace(memoryEvent.MemoryEventId))
            throw new InvalidOperationException("Memory event ID is required.");

        if (string.IsNullOrWhiteSpace(memoryEvent.MemoryItemId))
            throw new InvalidOperationException("Memory event item ID is required.");

        if (!LocalEventTypes.Contains(memoryEvent.EventType))
            throw new InvalidOperationException("Local agent memory cannot append Rejected or Accepted lifecycle events.");
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
        public int? CurrentEventType { get; set; }
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
}

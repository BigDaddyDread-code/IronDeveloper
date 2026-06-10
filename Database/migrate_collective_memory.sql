-- =====================================================================
-- migrate_collective_memory.sql
--
-- Creates append-only governed CollectiveMemory persistence.
-- CollectiveMemory may be manually promoted for governance/review only.
-- This migration does not add runtime retrieval, Weaviate, agent use, or
-- automatic promotion paths. Safe to rerun.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.CollectiveMemoryItem', 'U') IS NULL
BEGIN
    CREATE TABLE agent.CollectiveMemoryItem
    (
        CollectiveMemoryId NVARCHAR(100) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        KnowledgeDomainId NVARCHAR(120) NULL,
        ComponentId NVARCHAR(120) NULL,
        RepositoryId NVARCHAR(160) NULL,
        MemoryType INT NOT NULL,
        AuthorityLevel INT NOT NULL,
        Title NVARCHAR(300) NOT NULL,
        Summary NVARCHAR(MAX) NOT NULL,
        SourcesJson NVARCHAR(MAX) NOT NULL,
        EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
        ContradictionsJson NVARCHAR(MAX) NOT NULL,
        SupersedesJson NVARCHAR(MAX) NOT NULL,
        Confidence DECIMAL(5,4) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        LastReviewedAtUtc DATETIME2(7) NULL,
        LastConfirmedAtUtc DATETIME2(7) NULL,
        ExpiresAtUtc DATETIME2(7) NULL,
        DecisionId NVARCHAR(120) NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        CollectiveMemoryJson NVARCHAR(MAX) NULL,
        ContentHashSha256 NVARCHAR(64) NULL,
        CONSTRAINT PK_CollectiveMemoryItem PRIMARY KEY (CollectiveMemoryId),
        CONSTRAINT CK_CollectiveMemoryItem_MemoryType CHECK (MemoryType BETWEEN 1 AND 10),
        CONSTRAINT CK_CollectiveMemoryItem_AuthorityLevel CHECK (AuthorityLevel BETWEEN 1 AND 5),
        CONSTRAINT CK_CollectiveMemoryItem_Confidence CHECK (Confidence >= 0 AND Confidence <= 1),
        CONSTRAINT CK_CollectiveMemoryItem_RequiredText CHECK
        (
            LEN(LTRIM(RTRIM(TenantId))) > 0
            AND LEN(LTRIM(RTRIM(ProjectId))) > 0
            AND LEN(LTRIM(RTRIM(Title))) > 0
            AND LEN(LTRIM(RTRIM(Summary))) > 0
        ),
        CONSTRAINT CK_CollectiveMemoryItem_Json CHECK
        (
            ISJSON(SourcesJson) = 1
            AND ISJSON(EvidenceRefsJson) = 1
            AND ISJSON(ContradictionsJson) = 1
            AND ISJSON(SupersedesJson) = 1
            AND (CollectiveMemoryJson IS NULL OR ISJSON(CollectiveMemoryJson) = 1)
        ),
        CONSTRAINT CK_CollectiveMemoryItem_ContentHash CHECK
        (
            ContentHashSha256 IS NULL
            OR
            (
                LEN(ContentHashSha256) = 64
                AND ContentHashSha256 NOT LIKE '%[^0-9A-Fa-f]%'
            )
        )
    );
END

IF OBJECT_ID('agent.CollectiveMemoryEvent', 'U') IS NULL
BEGIN
    CREATE TABLE agent.CollectiveMemoryEvent
    (
        CollectiveMemoryEventId NVARCHAR(100) NOT NULL,
        CollectiveMemoryId NVARCHAR(100) NOT NULL,
        EventType INT NOT NULL,
        Reason NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CreatedByUserId NVARCHAR(120) NULL,
        CreatedByAgentId NVARCHAR(120) NULL,
        DecisionId NVARCHAR(120) NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        EventJson NVARCHAR(MAX) NULL,
        CONSTRAINT PK_CollectiveMemoryEvent PRIMARY KEY (CollectiveMemoryEventId),
        CONSTRAINT FK_CollectiveMemoryEvent_Item FOREIGN KEY (CollectiveMemoryId)
            REFERENCES agent.CollectiveMemoryItem (CollectiveMemoryId),
        CONSTRAINT CK_CollectiveMemoryEvent_EventType CHECK (EventType BETWEEN 1 AND 8),
        CONSTRAINT CK_CollectiveMemoryEvent_Actor CHECK (CreatedByUserId IS NOT NULL OR CreatedByAgentId IS NOT NULL),
        CONSTRAINT CK_CollectiveMemoryEvent_Reason CHECK (LEN(LTRIM(RTRIM(Reason))) > 0),
        CONSTRAINT CK_CollectiveMemoryEvent_EventJson CHECK (EventJson IS NULL OR ISJSON(EventJson) = 1)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CollectiveMemoryItem_ScopeCreated' AND object_id = OBJECT_ID('agent.CollectiveMemoryItem'))
    CREATE INDEX IX_CollectiveMemoryItem_ScopeCreated
        ON agent.CollectiveMemoryItem(TenantId, ProjectId, KnowledgeDomainId, ComponentId, RepositoryId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CollectiveMemoryItem_Decision' AND object_id = OBJECT_ID('agent.CollectiveMemoryItem'))
    CREATE INDEX IX_CollectiveMemoryItem_Decision
        ON agent.CollectiveMemoryItem(TenantId, ProjectId, DecisionId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CollectiveMemoryEvent_ItemCreated' AND object_id = OBJECT_ID('agent.CollectiveMemoryEvent'))
    CREATE INDEX IX_CollectiveMemoryEvent_ItemCreated
        ON agent.CollectiveMemoryEvent(CollectiveMemoryId, CreatedAtUtc, CollectiveMemoryEventId);

IF OBJECT_ID('agent.TR_CollectiveMemoryItem_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_CollectiveMemoryItem_BlockUpdateDelete
        ON agent.CollectiveMemoryItem
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51100, ''CollectiveMemoryItem is append-only. Collective memory items cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_CollectiveMemoryEvent_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_CollectiveMemoryEvent_BlockUpdateDelete
        ON agent.CollectiveMemoryEvent
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51101, ''CollectiveMemoryEvent is append-only. Collective memory events cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_CollectiveMemoryItem_ValidateInsert', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_CollectiveMemoryItem_ValidateInsert
        ON agent.CollectiveMemoryItem
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                WHERE i.AuthorityLevel = 3
                  AND (NULLIF(LTRIM(RTRIM(ISNULL(i.DecisionId, ''''))), '''') IS NULL OR i.LastReviewedAtUtc IS NULL)
            )
            BEGIN
                THROW 51102, ''Accepted collective memory requires DecisionId and LastReviewedAtUtc.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                WHERE i.AuthorityLevel <> 5
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON(i.SourcesJson))
            )
            BEGIN
                THROW 51103, ''Collective memory requires at least one source reference unless rejected.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                WHERE i.AuthorityLevel = 3
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON(i.EvidenceRefsJson))
            )
            BEGIN
                THROW 51104, ''Accepted collective memory requires at least one evidence reference.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                CROSS APPLY OPENJSON(i.SourcesJson) WITH
                (
                    SourceType INT ''$.sourceType'',
                    SourceId NVARCHAR(160) ''$.sourceId''
                ) s
                WHERE s.SourceType NOT BETWEEN 1 AND 10
                   OR NULLIF(LTRIM(RTRIM(ISNULL(s.SourceId, ''''))), '''') IS NULL
            )
            BEGIN
                THROW 51105, ''Collective memory source refs must include sourceType and sourceId.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                CROSS APPLY OPENJSON(i.EvidenceRefsJson) WITH
                (
                    EvidenceId NVARCHAR(120) ''$.evidenceId'',
                    EvidenceType INT ''$.evidenceType'',
                    SourceId NVARCHAR(160) ''$.sourceId''
                ) e
                WHERE NULLIF(LTRIM(RTRIM(ISNULL(e.EvidenceId, ''''))), '''') IS NULL
                   OR e.EvidenceType NOT BETWEEN 1 AND 12
                   OR NULLIF(LTRIM(RTRIM(ISNULL(e.SourceId, ''''))), '''') IS NULL
            )
            BEGIN
                THROW 51106, ''Collective memory evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                WHERE i.Title LIKE ''%RawPrompt%'' OR i.Title LIKE ''%RawCompletion%'' OR i.Title LIKE ''%ChainOfThought%'' OR i.Title LIKE ''%Scratchpad%'' OR i.Title LIKE ''%PrivateReasoning%''
                   OR i.Summary LIKE ''%RawPrompt%'' OR i.Summary LIKE ''%RawCompletion%'' OR i.Summary LIKE ''%ChainOfThought%'' OR i.Summary LIKE ''%Scratchpad%'' OR i.Summary LIKE ''%PrivateReasoning%''
                   OR ISNULL(i.CollectiveMemoryJson, '''') LIKE ''%RawPrompt%'' OR ISNULL(i.CollectiveMemoryJson, '''') LIKE ''%RawCompletion%'' OR ISNULL(i.CollectiveMemoryJson, '''') LIKE ''%ChainOfThought%'' OR ISNULL(i.CollectiveMemoryJson, '''') LIKE ''%Scratchpad%'' OR ISNULL(i.CollectiveMemoryJson, '''') LIKE ''%PrivateReasoning%''
            )
            BEGIN
                THROW 51107, ''Collective memory must not contain raw prompt, completion, scratchpad, chain-of-thought, or private reasoning markers.'', 1;
            END
        END');

IF OBJECT_ID('agent.TR_CollectiveMemoryEvent_ValidateInsert', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_CollectiveMemoryEvent_ValidateInsert
        ON agent.CollectiveMemoryEvent
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted e
                WHERE e.EventType = 1
                GROUP BY e.CollectiveMemoryId
                HAVING
                (
                    SELECT COUNT(*)
                    FROM agent.CollectiveMemoryEvent existing
                    WHERE existing.CollectiveMemoryId = e.CollectiveMemoryId
                      AND existing.EventType = 1
                ) > 1
            )
            BEGIN
                THROW 51108, ''Collective memory can contain only one Created event.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted e
                WHERE EXISTS
                (
                    SELECT 1
                    FROM agent.CollectiveMemoryEvent existing
                    WHERE existing.CollectiveMemoryId = e.CollectiveMemoryId
                      AND existing.CollectiveMemoryEventId <> e.CollectiveMemoryEventId
                      AND existing.EventType IN (3, 5, 6, 7)
                      AND existing.CreatedAtUtc <= e.CreatedAtUtc
                )
            )
            BEGIN
                THROW 51109, ''Collective memory terminal states cannot receive follow-up events in this PR.'', 1;
            END
        END');

EXEC(N'
CREATE OR ALTER VIEW agent.vwCollectiveMemoryCurrentState
AS
WITH LatestEvent AS
(
    SELECT
        e.*,
        ROW_NUMBER() OVER
        (
            PARTITION BY e.CollectiveMemoryId
            ORDER BY e.CreatedAtUtc DESC, e.EventType DESC, e.CollectiveMemoryEventId DESC
        ) AS rn
    FROM agent.CollectiveMemoryEvent e
)
SELECT
    i.CollectiveMemoryId,
    i.TenantId,
    i.ProjectId,
    i.KnowledgeDomainId,
    i.ComponentId,
    i.RepositoryId,
    i.MemoryType,
    i.AuthorityLevel,
    CASE ISNULL(le.EventType, 1)
        WHEN 1 THEN N''Proposed''
        WHEN 2 THEN N''Active''
        WHEN 3 THEN N''Rejected''
        WHEN 4 THEN N''UnderReview''
        WHEN 5 THEN N''Deprecated''
        WHEN 6 THEN N''Superseded''
        WHEN 7 THEN N''Invalidated''
        WHEN 8 THEN N''UnderReview''
        ELSE N''UnderReview''
    END AS CurrentStatus,
    CASE ISNULL(le.EventType, 1)
        WHEN 1 THEN N''NeedsHumanReview''
        WHEN 2 THEN N''ApprovedForAcceptance''
        WHEN 3 THEN N''RejectedByReview''
        WHEN 4 THEN N''NeedsHumanReview''
        WHEN 5 THEN N''NeedsHumanReview''
        WHEN 6 THEN N''NeedsHumanReview''
        WHEN 7 THEN N''NeedsContradictionReview''
        WHEN 8 THEN N''NeedsHumanReview''
        ELSE N''NeedsHumanReview''
    END AS CurrentReviewState,
    i.Title,
    i.Summary,
    i.SourcesJson,
    i.EvidenceRefsJson,
    i.ContradictionsJson,
    i.SupersedesJson,
    i.Confidence,
    i.CreatedAtUtc,
    i.LastReviewedAtUtc,
    i.LastConfirmedAtUtc,
    i.ExpiresAtUtc,
    i.DecisionId,
    i.ThoughtLedgerEntryId,
    i.CorrelationId,
    i.CollectiveMemoryJson,
    i.ContentHashSha256,
    le.EventType AS CurrentEventType,
    le.CreatedAtUtc AS CurrentEventAtUtc
FROM agent.CollectiveMemoryItem i
LEFT JOIN LatestEvent le
    ON le.CollectiveMemoryId = i.CollectiveMemoryId
   AND le.rn = 1;
');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_CollectiveMemory_CreateFromManualPromotion
    @CollectiveMemoryId NVARCHAR(100),
    @TenantId NVARCHAR(80),
    @ProjectId NVARCHAR(80),
    @KnowledgeDomainId NVARCHAR(120) = NULL,
    @ComponentId NVARCHAR(120) = NULL,
    @RepositoryId NVARCHAR(160) = NULL,
    @MemoryType INT,
    @AuthorityLevel INT,
    @Title NVARCHAR(300),
    @Summary NVARCHAR(MAX),
    @SourcesJson NVARCHAR(MAX),
    @EvidenceRefsJson NVARCHAR(MAX),
    @ContradictionsJson NVARCHAR(MAX),
    @SupersedesJson NVARCHAR(MAX),
    @Confidence DECIMAL(5,4),
    @CreatedAtUtc DATETIME2(7),
    @LastReviewedAtUtc DATETIME2(7) = NULL,
    @LastConfirmedAtUtc DATETIME2(7) = NULL,
    @ExpiresAtUtc DATETIME2(7) = NULL,
    @DecisionId NVARCHAR(120),
    @ThoughtLedgerEntryId NVARCHAR(120) = NULL,
    @CorrelationId NVARCHAR(120) = NULL,
    @CollectiveMemoryJson NVARCHAR(MAX) = NULL,
    @ContentHashSha256 NVARCHAR(64) = NULL,
    @CreatedEventId NVARCHAR(100),
    @DecisionEventId NVARCHAR(100),
    @DecisionEventType INT,
    @Reason NVARCHAR(MAX),
    @CreatedByUserId NVARCHAR(120) = NULL,
    @CreatedByAgentId NVARCHAR(120) = NULL,
    @EventJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@CollectiveMemoryId, N''''))), N'''') IS NULL
        THROW 51120, ''CollectiveMemoryId is required.'', 1;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@TenantId, N''''))), N'''') IS NULL
        OR NULLIF(LTRIM(RTRIM(ISNULL(@ProjectId, N''''))), N'''') IS NULL
        THROW 51121, ''Collective memory scope requires tenant and project.'', 1;

    IF @CreatedByUserId IS NULL AND @CreatedByAgentId IS NULL
        THROW 51122, ''Collective memory manual promotion requires an actor.'', 1;

    IF @DecisionEventType = 2
       AND NULLIF(LTRIM(RTRIM(ISNULL(@CreatedByUserId, N''''))), N'''') IS NULL
        THROW 51131, ''Accepted collective memory requires an explicit human/governance user actor.'', 1;

    IF @DecisionEventType NOT IN (2, 3)
        THROW 51123, ''Collective memory manual promotion supports Accepted or Rejected decision events only.'', 1;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@DecisionId, N''''))), N'''') IS NULL
        THROW 51124, ''Collective memory manual promotion requires DecisionId.'', 1;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@Reason, N''''))), N'''') IS NULL
        THROW 51125, ''Collective memory manual promotion requires a reason.'', 1;

    IF @DecisionEventType = 2 AND (@AuthorityLevel <> 3 OR @LastReviewedAtUtc IS NULL)
        THROW 51126, ''Accepted collective memory requires accepted authority and LastReviewedAtUtc.'', 1;

    IF @DecisionEventType = 3 AND @AuthorityLevel <> 5
        THROW 51127, ''Rejected collective memory requires rejected authority.'', 1;

    IF ISJSON(@SourcesJson) <> 1 OR ISJSON(@EvidenceRefsJson) <> 1 OR ISJSON(@ContradictionsJson) <> 1 OR ISJSON(@SupersedesJson) <> 1
        THROW 51128, ''Collective memory promotion JSON payloads must be valid JSON.'', 1;

    IF @CollectiveMemoryJson IS NOT NULL AND ISJSON(@CollectiveMemoryJson) <> 1
        THROW 51129, ''CollectiveMemoryJson must be valid JSON.'', 1;

    IF @DecisionEventType = 2 AND NOT EXISTS (SELECT 1 FROM OPENJSON(@EvidenceRefsJson))
        THROW 51130, ''Accepted collective memory requires evidence.'', 1;

    BEGIN TRANSACTION;

    INSERT INTO agent.CollectiveMemoryItem
    (
        CollectiveMemoryId, TenantId, ProjectId, KnowledgeDomainId, ComponentId, RepositoryId,
        MemoryType, AuthorityLevel, Title, Summary, SourcesJson, EvidenceRefsJson,
        ContradictionsJson, SupersedesJson, Confidence, CreatedAtUtc, LastReviewedAtUtc,
        LastConfirmedAtUtc, ExpiresAtUtc, DecisionId, ThoughtLedgerEntryId, CorrelationId,
        CollectiveMemoryJson, ContentHashSha256
    )
    VALUES
    (
        @CollectiveMemoryId, @TenantId, @ProjectId, @KnowledgeDomainId, @ComponentId, @RepositoryId,
        @MemoryType, @AuthorityLevel, @Title, @Summary, @SourcesJson, @EvidenceRefsJson,
        @ContradictionsJson, @SupersedesJson, @Confidence, @CreatedAtUtc, @LastReviewedAtUtc,
        @LastConfirmedAtUtc, @ExpiresAtUtc, @DecisionId, @ThoughtLedgerEntryId, @CorrelationId,
        @CollectiveMemoryJson, @ContentHashSha256
    );

    INSERT INTO agent.CollectiveMemoryEvent
    (
        CollectiveMemoryEventId, CollectiveMemoryId, EventType, Reason, CreatedAtUtc,
        CreatedByUserId, CreatedByAgentId, DecisionId, ThoughtLedgerEntryId, CorrelationId, EventJson
    )
    VALUES
    (
        @CreatedEventId, @CollectiveMemoryId, 1, N''Manual promotion request created collective-memory candidate.'',
        @CreatedAtUtc, @CreatedByUserId, @CreatedByAgentId, @DecisionId, @ThoughtLedgerEntryId, @CorrelationId, @EventJson
    );

    INSERT INTO agent.CollectiveMemoryEvent
    (
        CollectiveMemoryEventId, CollectiveMemoryId, EventType, Reason, CreatedAtUtc,
        CreatedByUserId, CreatedByAgentId, DecisionId, ThoughtLedgerEntryId, CorrelationId, EventJson
    )
    VALUES
    (
        @DecisionEventId, @CollectiveMemoryId, @DecisionEventType, @Reason,
        @CreatedAtUtc, @CreatedByUserId, @CreatedByAgentId, @DecisionId, @ThoughtLedgerEntryId, @CorrelationId, @EventJson
    );

    COMMIT TRANSACTION;
END
');

EXEC(N'
CREATE OR ALTER PROCEDURE agent.usp_CollectiveMemory_AddEvent
    @CollectiveMemoryEventId NVARCHAR(100),
    @CollectiveMemoryId NVARCHAR(100),
    @TenantId NVARCHAR(80),
    @ProjectId NVARCHAR(80),
    @EventType INT,
    @Reason NVARCHAR(MAX),
    @CreatedAtUtc DATETIME2(7),
    @CreatedByUserId NVARCHAR(120) = NULL,
    @CreatedByAgentId NVARCHAR(120) = NULL,
    @DecisionId NVARCHAR(120) = NULL,
    @ThoughtLedgerEntryId NVARCHAR(120) = NULL,
    @CorrelationId NVARCHAR(120) = NULL,
    @EventJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @CreatedByUserId IS NULL AND @CreatedByAgentId IS NULL
        THROW 51140, ''Collective memory events require an actor.'', 1;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@Reason, N''''))), N'''') IS NULL
        THROW 51141, ''Collective memory events require a reason.'', 1;

    IF @EventType NOT BETWEEN 1 AND 8
        THROW 51142, ''Collective memory event type is invalid.'', 1;

    IF @EventJson IS NOT NULL AND ISJSON(@EventJson) <> 1
        THROW 51143, ''Collective memory event JSON must be valid JSON.'', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM agent.CollectiveMemoryItem i
        WHERE i.CollectiveMemoryId = @CollectiveMemoryId
          AND i.TenantId = @TenantId
          AND i.ProjectId = @ProjectId
    )
        THROW 51144, ''Collective memory event target was not found in the requested scope.'', 1;

    IF @EventType = 1 AND EXISTS
    (
        SELECT 1
        FROM agent.CollectiveMemoryEvent e
        WHERE e.CollectiveMemoryId = @CollectiveMemoryId
          AND e.EventType = 1
    )
        THROW 51145, ''Collective memory can contain only one Created event.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM agent.CollectiveMemoryEvent e
        WHERE e.CollectiveMemoryId = @CollectiveMemoryId
          AND e.EventType IN (3, 5, 6, 7)
    )
        THROW 51146, ''Collective memory terminal states cannot receive follow-up events in this PR.'', 1;

    INSERT INTO agent.CollectiveMemoryEvent
    (
        CollectiveMemoryEventId, CollectiveMemoryId, EventType, Reason, CreatedAtUtc,
        CreatedByUserId, CreatedByAgentId, DecisionId, ThoughtLedgerEntryId, CorrelationId, EventJson
    )
    VALUES
    (
        @CollectiveMemoryEventId, @CollectiveMemoryId, @EventType, @Reason, @CreatedAtUtc,
        @CreatedByUserId, @CreatedByAgentId, @DecisionId, @ThoughtLedgerEntryId, @CorrelationId, @EventJson
    );
END
');

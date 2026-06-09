-- =====================================================================
-- migrate_agent_local_memory.sql
--
-- Creates append-only scoped agent local memory persistence.
-- Runtime stores must not create or mutate this schema; migrations own DDL.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

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

IF OBJECT_ID('agent.TR_AgentLocalMemoryEvent_ValidateInsert', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentLocalMemoryEvent_ValidateInsert
        ON agent.AgentLocalMemoryEvent
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted e
                WHERE e.EventType = 1
                GROUP BY e.MemoryItemId
                HAVING
                (
                    SELECT COUNT(*)
                    FROM agent.AgentLocalMemoryEvent existing
                    WHERE existing.MemoryItemId = e.MemoryItemId
                      AND existing.EventType = 1
                ) > 1
            )
            BEGIN
                THROW 51004, ''AgentLocalMemoryEvent can contain only one Created event per memory item.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted e
                INNER JOIN agent.AgentLocalMemoryItem i
                    ON i.MemoryItemId = e.MemoryItemId
                WHERE e.EventType = 1
                  AND i.MemoryType = 6
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM agent.AgentLocalMemoryEvidenceRef evidence
                      WHERE evidence.MemoryItemId = e.MemoryItemId
                  )
            )
            BEGIN
                THROW 51005, ''CandidatePattern memory requires evidence before Created event can be recorded.'', 1;
            END
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

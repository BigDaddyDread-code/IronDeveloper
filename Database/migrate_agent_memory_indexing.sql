-- =====================================================================
-- migrate_agent_memory_indexing.sql
--
-- Creates append-only governed memory indexing queue persistence.
-- SQL remains the source of truth; Weaviate receives projections only.
-- Runtime stores must not create or mutate this schema; migrations own DDL.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.AgentMemoryIndexQueue', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryIndexQueue
    (
        IndexRecordId NVARCHAR(100) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        CampaignId NVARCHAR(80) NOT NULL,
        RunId NVARCHAR(80) NULL,
        AgentId NVARCHAR(120) NULL,
        ArtifactType INT NOT NULL,
        ArtifactId NVARCHAR(120) NOT NULL,
        AuthorityLevel INT NOT NULL,
        Title NVARCHAR(300) NOT NULL,
        Summary NVARCHAR(MAX) NOT NULL,
        EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
        MetadataJson NVARCHAR(MAX) NULL,
        SourceHashSha256 NVARCHAR(64) NULL,
        DecisionId NVARCHAR(120) NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_AgentMemoryIndexQueue PRIMARY KEY (IndexRecordId),
        CONSTRAINT CK_AgentMemoryIndexQueue_ArtifactType CHECK (ArtifactType BETWEEN 1 AND 6),
        CONSTRAINT CK_AgentMemoryIndexQueue_Authority CHECK (AuthorityLevel BETWEEN 1 AND 5),
        CONSTRAINT CK_AgentMemoryIndexQueue_Title CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
        CONSTRAINT CK_AgentMemoryIndexQueue_Summary CHECK (LEN(LTRIM(RTRIM(Summary))) > 0),
        CONSTRAINT CK_AgentMemoryIndexQueue_ArtifactId CHECK (LEN(LTRIM(RTRIM(ArtifactId))) > 0),
        CONSTRAINT CK_AgentMemoryIndexQueue_EvidenceRefsJson CHECK
        (
            ISJSON(EvidenceRefsJson) = 1
            AND LEN(LTRIM(RTRIM(EvidenceRefsJson))) > 2
        ),
        CONSTRAINT CK_AgentMemoryIndexQueue_MetadataJson CHECK (MetadataJson IS NULL OR ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_AgentMemoryIndexQueue_SourceHash CHECK
        (
            SourceHashSha256 IS NULL
            OR
            (
                LEN(SourceHashSha256) = 64
                AND SourceHashSha256 NOT LIKE '%[^0-9A-Fa-f]%'
            )
        )
    );
END

IF OBJECT_ID('agent.AgentMemoryIndexEvent', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryIndexEvent
    (
        IndexEventId NVARCHAR(100) NOT NULL,
        IndexRecordId NVARCHAR(100) NOT NULL,
        EventType INT NOT NULL,
        WeaviateObjectId NVARCHAR(160) NULL,
        Error NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_AgentMemoryIndexEvent PRIMARY KEY (IndexEventId),
        CONSTRAINT FK_AgentMemoryIndexEvent_IndexRecord FOREIGN KEY (IndexRecordId)
            REFERENCES agent.AgentMemoryIndexQueue (IndexRecordId),
        CONSTRAINT CK_AgentMemoryIndexEvent_EventType CHECK (EventType BETWEEN 1 AND 5)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryIndexQueue_Pending' AND object_id = OBJECT_ID('agent.AgentMemoryIndexQueue'))
    CREATE INDEX IX_AgentMemoryIndexQueue_Pending
        ON agent.AgentMemoryIndexQueue(TenantId, ProjectId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryIndexQueue_Scope' AND object_id = OBJECT_ID('agent.AgentMemoryIndexQueue'))
    CREATE INDEX IX_AgentMemoryIndexQueue_Scope
        ON agent.AgentMemoryIndexQueue(TenantId, ProjectId, CampaignId, RunId, ArtifactType, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryIndexEvent_RecordCreated' AND object_id = OBJECT_ID('agent.AgentMemoryIndexEvent'))
    CREATE INDEX IX_AgentMemoryIndexEvent_RecordCreated
        ON agent.AgentMemoryIndexEvent(IndexRecordId, CreatedAtUtc);

IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryIndexQueue_BlockUpdateDelete
        ON agent.AgentMemoryIndexQueue
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51060, ''AgentMemoryIndexQueue is append-only. Index records cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryIndexEvent_BlockUpdateDelete
        ON agent.AgentMemoryIndexEvent
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51061, ''AgentMemoryIndexEvent is append-only. Index events cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryIndexQueue_ValidateProjection', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryIndexQueue_ValidateProjection
        ON agent.AgentMemoryIndexQueue
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted q
                CROSS APPLY OPENJSON(q.EvidenceRefsJson) WITH
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
                THROW 51062, ''Memory index queue evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted q
                WHERE q.ArtifactType NOT BETWEEN 1 AND 6
            )
            BEGIN
                THROW 51063, ''Memory index queue supports only approved projection artifact types.'', 1;
            END
        END');

IF OBJECT_ID('agent.TR_AgentMemoryIndexEvent_ValidateInsert', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryIndexEvent_ValidateInsert
        ON agent.AgentMemoryIndexEvent
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted e
                WHERE e.EventType = 1
                GROUP BY e.IndexRecordId
                HAVING
                (
                    SELECT COUNT(*)
                    FROM agent.AgentMemoryIndexEvent existing
                    WHERE existing.IndexRecordId = e.IndexRecordId
                      AND existing.EventType = 1
                ) > 1
            )
            BEGIN
                THROW 51064, ''Memory index queue records can contain only one Queued event.'', 1;
            END
        END');

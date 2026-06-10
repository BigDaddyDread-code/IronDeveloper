-- =====================================================================
-- migrate_agent_memory_influence.sql
--
-- Creates append-only scoped agent memory influence records.
-- Runtime stores must not create or mutate this schema; migrations own DDL.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.AgentMemoryInfluenceRecord', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryInfluenceRecord
    (
        InfluenceId NVARCHAR(80) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        CampaignId NVARCHAR(80) NOT NULL,
        RunId NVARCHAR(80) NOT NULL,
        AgentId NVARCHAR(120) NOT NULL,
        MemoryItemId NVARCHAR(80) NOT NULL,
        DecisionId NVARCHAR(120) NOT NULL,
        InfluenceType INT NOT NULL,
        InfluenceSummary NVARCHAR(MAX) NOT NULL,
        Confidence DECIMAL(5,4) NOT NULL,
        MemoryAuthorityLevelAtInfluence INT NOT NULL,
        MemoryLifecycleStatusAtInfluence INT NOT NULL,
        AffectedArtifactType NVARCHAR(80) NULL,
        AffectedArtifactId NVARCHAR(160) NULL,
        EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        InfluenceJson NVARCHAR(MAX) NULL,
        ContentHashSha256 VARBINARY(32) NULL,
        CONSTRAINT PK_AgentMemoryInfluenceRecord PRIMARY KEY (InfluenceId),
        CONSTRAINT FK_AgentMemoryInfluenceRecord_MemoryItem FOREIGN KEY (MemoryItemId)
            REFERENCES agent.AgentLocalMemoryItem (MemoryItemId),
        CONSTRAINT CK_AgentMemoryInfluenceRecord_Confidence CHECK (Confidence >= 0 AND Confidence <= 1),
        CONSTRAINT CK_AgentMemoryInfluenceRecord_InfluenceType CHECK (InfluenceType BETWEEN 1 AND 7),
        CONSTRAINT CK_AgentMemoryInfluenceRecord_DecisionRequired CHECK (LEN(LTRIM(RTRIM(DecisionId))) > 0),
        CONSTRAINT CK_AgentMemoryInfluenceRecord_SummaryRequired CHECK (LEN(LTRIM(RTRIM(InfluenceSummary))) > 0),
        CONSTRAINT CK_AgentMemoryInfluenceRecord_EvidenceJson CHECK
        (
            ISJSON(EvidenceRefsJson) = 1
            AND LEN(LTRIM(RTRIM(EvidenceRefsJson))) > 2
        )
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryInfluenceRecord_ScopeCreated' AND object_id = OBJECT_ID('agent.AgentMemoryInfluenceRecord'))
    CREATE INDEX IX_AgentMemoryInfluenceRecord_ScopeCreated
        ON agent.AgentMemoryInfluenceRecord(TenantId, ProjectId, CampaignId, RunId, AgentId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryInfluenceRecord_Memory' AND object_id = OBJECT_ID('agent.AgentMemoryInfluenceRecord'))
    CREATE INDEX IX_AgentMemoryInfluenceRecord_Memory
        ON agent.AgentMemoryInfluenceRecord(TenantId, ProjectId, CampaignId, RunId, AgentId, MemoryItemId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryInfluenceRecord_Decision' AND object_id = OBJECT_ID('agent.AgentMemoryInfluenceRecord'))
    CREATE INDEX IX_AgentMemoryInfluenceRecord_Decision
        ON agent.AgentMemoryInfluenceRecord(TenantId, ProjectId, CampaignId, RunId, AgentId, DecisionId, CreatedAtUtc);

IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryInfluenceRecord_BlockUpdateDelete
        ON agent.AgentMemoryInfluenceRecord
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51020, ''AgentMemoryInfluenceRecord is append-only. Influence records cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryInfluenceRecord_ValidateScope', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryInfluenceRecord_ValidateScope
        ON agent.AgentMemoryInfluenceRecord
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted r
                INNER JOIN agent.AgentLocalMemoryItem m
                    ON m.MemoryItemId = r.MemoryItemId
                WHERE m.TenantId <> r.TenantId
                   OR m.ProjectId <> r.ProjectId
                   OR m.CampaignId <> r.CampaignId
                   OR m.RunId <> r.RunId
                   OR m.AgentId <> r.AgentId
            )
            BEGIN
                THROW 51021, ''Memory influence scope does not match the referenced memory item scope.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted r
                INNER JOIN agent.vwAgentLocalMemoryCurrentState s
                    ON s.MemoryItemId = r.MemoryItemId
                WHERE ISNULL(s.CurrentEventType, 1) IN (2, 3, 4, 6, 7)
                   OR (s.ExpiresAtUtc IS NOT NULL AND s.ExpiresAtUtc <= SYSUTCDATETIME())
            )
            BEGIN
                THROW 51022, ''Terminal or time-expired local memory cannot be recorded as active influence.'', 1;
            END
        END');

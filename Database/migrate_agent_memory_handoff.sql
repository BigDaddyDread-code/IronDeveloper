-- =====================================================================
-- migrate_agent_memory_handoff.sql
--
-- Creates append-only scoped agent memory handoff slices.
-- Runtime stores must not create or mutate this schema; migrations own DDL.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.AgentMemoryHandoffSlice', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryHandoffSlice
    (
        HandoffMemorySliceId NVARCHAR(80) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        CampaignId NVARCHAR(80) NOT NULL,
        RunId NVARCHAR(80) NOT NULL,
        SourceAgentId NVARCHAR(120) NOT NULL,
        TargetAgentId NVARCHAR(120) NOT NULL,
        MemoryItemIdsJson NVARCHAR(MAX) NOT NULL,
        MemorySnapshotsJson NVARCHAR(MAX) NOT NULL,
        Summary NVARCHAR(MAX) NOT NULL,
        AllowedUse INT NOT NULL,
        EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
        Confidence DECIMAL(5,4) NOT NULL,
        InfluenceIdsJson NVARCHAR(MAX) NULL,
        DecisionId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        ExpiresAtUtc DATETIME2(7) NULL,
        HandoffJson NVARCHAR(MAX) NULL,
        ContentHashSha256 VARBINARY(32) NULL,
        CONSTRAINT PK_AgentMemoryHandoffSlice PRIMARY KEY (HandoffMemorySliceId),
        CONSTRAINT CK_AgentMemoryHandoffSlice_SourceTargetDifferent CHECK (SourceAgentId <> TargetAgentId),
        CONSTRAINT CK_AgentMemoryHandoffSlice_Confidence CHECK (Confidence >= 0 AND Confidence <= 1),
        CONSTRAINT CK_AgentMemoryHandoffSlice_AllowedUse CHECK (AllowedUse BETWEEN 1 AND 4),
        CONSTRAINT CK_AgentMemoryHandoffSlice_MemoryItemIdsJson CHECK
        (
            ISJSON(MemoryItemIdsJson) = 1
            AND LEN(LTRIM(RTRIM(MemoryItemIdsJson))) > 2
        ),
        CONSTRAINT CK_AgentMemoryHandoffSlice_MemorySnapshotsJson CHECK
        (
            ISJSON(MemorySnapshotsJson) = 1
            AND LEN(LTRIM(RTRIM(MemorySnapshotsJson))) > 2
        ),
        CONSTRAINT CK_AgentMemoryHandoffSlice_EvidenceRefsJson CHECK
        (
            ISJSON(EvidenceRefsJson) = 1
            AND LEN(LTRIM(RTRIM(EvidenceRefsJson))) > 2
        ),
        CONSTRAINT CK_AgentMemoryHandoffSlice_SummaryRequired CHECK (LEN(LTRIM(RTRIM(Summary))) > 0)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryHandoffSlice_Incoming' AND object_id = OBJECT_ID('agent.AgentMemoryHandoffSlice'))
    CREATE INDEX IX_AgentMemoryHandoffSlice_Incoming
        ON agent.AgentMemoryHandoffSlice(TenantId, ProjectId, CampaignId, RunId, TargetAgentId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryHandoffSlice_Outgoing' AND object_id = OBJECT_ID('agent.AgentMemoryHandoffSlice'))
    CREATE INDEX IX_AgentMemoryHandoffSlice_Outgoing
        ON agent.AgentMemoryHandoffSlice(TenantId, ProjectId, CampaignId, RunId, SourceAgentId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryHandoffSlice_Correlation' AND object_id = OBJECT_ID('agent.AgentMemoryHandoffSlice'))
    CREATE INDEX IX_AgentMemoryHandoffSlice_Correlation
        ON agent.AgentMemoryHandoffSlice(TenantId, ProjectId, CampaignId, RunId, CorrelationId, CreatedAtUtc);

IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryHandoffSlice_BlockUpdateDelete
        ON agent.AgentMemoryHandoffSlice
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51030, ''AgentMemoryHandoffSlice is append-only. Handoff slices cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryHandoffSlice_ValidateSourceMemory
        ON agent.AgentMemoryHandoffSlice
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted h
                CROSS APPLY OPENJSON(h.MemoryItemIdsJson) ids
                LEFT JOIN agent.AgentLocalMemoryItem m
                    ON m.MemoryItemId = CONVERT(NVARCHAR(80), ids.value)
                   AND m.TenantId = h.TenantId
                   AND m.ProjectId = h.ProjectId
                   AND m.CampaignId = h.CampaignId
                   AND m.RunId = h.RunId
                   AND m.AgentId = h.SourceAgentId
                WHERE m.MemoryItemId IS NULL
            )
            BEGIN
                THROW 51031, ''Handoff slice contains memory items outside the source agent scope.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted h
                CROSS APPLY OPENJSON(h.MemoryItemIdsJson) ids
                INNER JOIN agent.vwAgentLocalMemoryCurrentState s
                    ON s.MemoryItemId = CONVERT(NVARCHAR(80), ids.value)
                   AND s.TenantId = h.TenantId
                   AND s.ProjectId = h.ProjectId
                   AND s.CampaignId = h.CampaignId
                   AND s.RunId = h.RunId
                   AND s.AgentId = h.SourceAgentId
                WHERE ISNULL(s.CurrentEventType, 1) IN (2, 3, 4, 6, 7)
                   OR (s.ExpiresAtUtc IS NOT NULL AND s.ExpiresAtUtc <= SYSUTCDATETIME())
            )
            BEGIN
                THROW 51032, ''Terminal or time-expired local memory cannot be included in a handoff slice.'', 1;
            END
        END');

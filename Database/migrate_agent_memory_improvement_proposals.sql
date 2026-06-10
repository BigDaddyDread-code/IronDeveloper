-- =====================================================================
-- migrate_agent_memory_improvement_proposals.sql
--
-- Creates append-only scoped agent memory improvement proposal persistence.
-- Runtime stores must not create or mutate this schema; migrations own DDL.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.AgentMemoryImprovementProposal', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryImprovementProposal
    (
        ProposalId NVARCHAR(80) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        CampaignId NVARCHAR(80) NOT NULL,
        RunId NVARCHAR(80) NOT NULL,
        AgentId NVARCHAR(120) NOT NULL,
        ProposalType INT NOT NULL,
        Title NVARCHAR(300) NOT NULL,
        Summary NVARCHAR(MAX) NOT NULL,
        SourcesJson NVARCHAR(MAX) NOT NULL,
        EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
        Confidence DECIMAL(5,4) NOT NULL,
        ProposedByAgentId NVARCHAR(120) NULL,
        ProposedByUserId NVARCHAR(120) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        ProposalJson NVARCHAR(MAX) NULL,
        ContentHashSha256 VARBINARY(32) NULL,
        CONSTRAINT PK_AgentMemoryImprovementProposal PRIMARY KEY (ProposalId),
        CONSTRAINT CK_AgentMemoryImprovementProposal_ProposalType CHECK (ProposalType BETWEEN 1 AND 8),
        CONSTRAINT CK_AgentMemoryImprovementProposal_Title CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
        CONSTRAINT CK_AgentMemoryImprovementProposal_Summary CHECK (LEN(LTRIM(RTRIM(Summary))) > 0),
        CONSTRAINT CK_AgentMemoryImprovementProposal_SourcesJson CHECK
        (
            ISJSON(SourcesJson) = 1
            AND LEN(LTRIM(RTRIM(SourcesJson))) > 2
        ),
        CONSTRAINT CK_AgentMemoryImprovementProposal_EvidenceRefsJson CHECK
        (
            ISJSON(EvidenceRefsJson) = 1
            AND LEN(LTRIM(RTRIM(EvidenceRefsJson))) > 2
        ),
        CONSTRAINT CK_AgentMemoryImprovementProposal_Confidence CHECK (Confidence >= 0 AND Confidence <= 1),
        CONSTRAINT CK_AgentMemoryImprovementProposal_Proposer CHECK
        (
            ProposedByAgentId IS NOT NULL
            OR ProposedByUserId IS NOT NULL
        )
    );
END

IF OBJECT_ID('agent.AgentMemoryImprovementProposalEvent', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryImprovementProposalEvent
    (
        ProposalEventId NVARCHAR(80) NOT NULL,
        ProposalId NVARCHAR(80) NOT NULL,
        EventType INT NOT NULL,
        Reason NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CreatedByUserId NVARCHAR(120) NULL,
        CreatedByAgentId NVARCHAR(120) NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        EventJson NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AgentMemoryImprovementProposalEvent PRIMARY KEY (ProposalEventId),
        CONSTRAINT FK_AgentMemoryImprovementProposalEvent_Proposal FOREIGN KEY (ProposalId)
            REFERENCES agent.AgentMemoryImprovementProposal (ProposalId),
        CONSTRAINT CK_AgentMemoryImprovementProposalEvent_EventType CHECK (EventType BETWEEN 1 AND 5)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryImprovementProposal_ScopeCreated' AND object_id = OBJECT_ID('agent.AgentMemoryImprovementProposal'))
    CREATE INDEX IX_AgentMemoryImprovementProposal_ScopeCreated
        ON agent.AgentMemoryImprovementProposal(TenantId, ProjectId, CampaignId, RunId, AgentId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryImprovementProposal_TypeCreated' AND object_id = OBJECT_ID('agent.AgentMemoryImprovementProposal'))
    CREATE INDEX IX_AgentMemoryImprovementProposal_TypeCreated
        ON agent.AgentMemoryImprovementProposal(TenantId, ProjectId, CampaignId, RunId, ProposalType, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryImprovementProposalEvent_ProposalCreated' AND object_id = OBJECT_ID('agent.AgentMemoryImprovementProposalEvent'))
    CREATE INDEX IX_AgentMemoryImprovementProposalEvent_ProposalCreated
        ON agent.AgentMemoryImprovementProposalEvent(ProposalId, CreatedAtUtc);

IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryImprovementProposal_BlockUpdateDelete
        ON agent.AgentMemoryImprovementProposal
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51050, ''AgentMemoryImprovementProposal is append-only. Proposals cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_BlockUpdateDelete
        ON agent.AgentMemoryImprovementProposalEvent
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51051, ''AgentMemoryImprovementProposalEvent is append-only. Proposal events cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposal_ValidateSources', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryImprovementProposal_ValidateSources
        ON agent.AgentMemoryImprovementProposal
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted p
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM OPENJSON(p.SourcesJson) WITH
                    (
                        MemoryItemId NVARCHAR(80) ''$.memoryItemId'',
                        InfluenceId NVARCHAR(80) ''$.influenceId'',
                        HandoffMemorySliceId NVARCHAR(80) ''$.handoffMemorySliceId'',
                        RunMemoryFindingType NVARCHAR(120) ''$.runMemoryFindingType'',
                        ThoughtLedgerEntryId NVARCHAR(120) ''$.thoughtLedgerEntryId'',
                        DecisionId NVARCHAR(120) ''$.decisionId''
                    ) s
                    WHERE NULLIF(LTRIM(RTRIM(ISNULL(s.MemoryItemId, ''''))), '''') IS NOT NULL
                       OR NULLIF(LTRIM(RTRIM(ISNULL(s.InfluenceId, ''''))), '''') IS NOT NULL
                       OR NULLIF(LTRIM(RTRIM(ISNULL(s.HandoffMemorySliceId, ''''))), '''') IS NOT NULL
                       OR NULLIF(LTRIM(RTRIM(ISNULL(s.RunMemoryFindingType, ''''))), '''') IS NOT NULL
                       OR NULLIF(LTRIM(RTRIM(ISNULL(s.ThoughtLedgerEntryId, ''''))), '''') IS NOT NULL
                       OR NULLIF(LTRIM(RTRIM(ISNULL(s.DecisionId, ''''))), '''') IS NOT NULL
                )
            )
            BEGIN
                THROW 51052, ''Memory improvement proposals require at least one source reference.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted p
                CROSS APPLY OPENJSON(p.EvidenceRefsJson) WITH
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
                THROW 51053, ''Memory improvement proposal evidence refs must include evidenceId, evidenceType, and sourceId.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted p
                CROSS APPLY OPENJSON(p.SourcesJson) WITH (MemoryItemId NVARCHAR(80) ''$.memoryItemId'') s
                WHERE NULLIF(LTRIM(RTRIM(ISNULL(s.MemoryItemId, ''''))), '''') IS NOT NULL
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM agent.AgentLocalMemoryItem m
                      WHERE m.MemoryItemId = s.MemoryItemId
                        AND m.TenantId = p.TenantId
                        AND m.ProjectId = p.ProjectId
                        AND m.CampaignId = p.CampaignId
                        AND m.RunId = p.RunId
                        AND m.AgentId = p.AgentId
                  )
            )
            BEGIN
                THROW 51054, ''Memory improvement proposal references a memory item outside the proposal scope.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted p
                CROSS APPLY OPENJSON(p.SourcesJson) WITH (InfluenceId NVARCHAR(80) ''$.influenceId'') s
                WHERE NULLIF(LTRIM(RTRIM(ISNULL(s.InfluenceId, ''''))), '''') IS NOT NULL
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM agent.AgentMemoryInfluenceRecord i
                      WHERE i.InfluenceId = s.InfluenceId
                        AND i.TenantId = p.TenantId
                        AND i.ProjectId = p.ProjectId
                        AND i.CampaignId = p.CampaignId
                        AND i.RunId = p.RunId
                        AND i.AgentId = p.AgentId
                  )
            )
            BEGIN
                THROW 51055, ''Memory improvement proposal references an influence outside the proposal scope.'', 1;
            END

            IF EXISTS
            (
                SELECT 1
                FROM inserted p
                CROSS APPLY OPENJSON(p.SourcesJson) WITH (HandoffMemorySliceId NVARCHAR(80) ''$.handoffMemorySliceId'') s
                WHERE NULLIF(LTRIM(RTRIM(ISNULL(s.HandoffMemorySliceId, ''''))), '''') IS NOT NULL
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM agent.AgentMemoryHandoffSlice h
                      WHERE h.HandoffMemorySliceId = s.HandoffMemorySliceId
                        AND h.TenantId = p.TenantId
                        AND h.ProjectId = p.ProjectId
                        AND h.CampaignId = p.CampaignId
                        AND h.RunId = p.RunId
                        AND (h.SourceAgentId = p.AgentId OR h.TargetAgentId = p.AgentId)
                  )
            )
            BEGIN
                THROW 51056, ''Memory improvement proposal references a handoff outside the proposal scope.'', 1;
            END
        END');

IF OBJECT_ID('agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryImprovementProposalEvent_ValidateInsert
        ON agent.AgentMemoryImprovementProposalEvent
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted e
                WHERE e.EventType = 1
                GROUP BY e.ProposalId
                HAVING
                (
                    SELECT COUNT(*)
                    FROM agent.AgentMemoryImprovementProposalEvent existing
                    WHERE existing.ProposalId = e.ProposalId
                      AND existing.EventType = 1
                ) > 1
            )
            BEGIN
                THROW 51057, ''Memory improvement proposals can contain only one Submitted event.'', 1;
            END
        END');

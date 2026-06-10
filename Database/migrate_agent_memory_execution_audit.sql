-- =====================================================================
-- migrate_agent_memory_execution_audit.sql
--
-- Creates append-only scoped audit records for memory-backed execution.
-- Runtime stores must not create or mutate this schema; migrations own DDL.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.AgentMemoryExecutionAudit', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentMemoryExecutionAudit
    (
        AuditId NVARCHAR(120) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        CampaignId NVARCHAR(80) NOT NULL,
        RunId NVARCHAR(80) NOT NULL,
        AgentId NVARCHAR(120) NOT NULL,
        ExecutionId NVARCHAR(160) NOT NULL,
        ContextId NVARCHAR(160) NOT NULL,
        RequestId NVARCHAR(160) NOT NULL,
        ReviewId NVARCHAR(160) NOT NULL,
        SkillId NVARCHAR(160) NOT NULL,
        DecisionId NVARCHAR(120) NOT NULL,
        ActionType INT NOT NULL,
        Outcome INT NOT NULL,
        ExecutionStatus NVARCHAR(80) NOT NULL,
        GateDecision INT NOT NULL,
        GovernanceDecision INT NULL,
        GovernanceCheckId NVARCHAR(120) NULL,
        Executed BIT NOT NULL,
        SourceMutated BIT NOT NULL,
        WorkspaceMutated BIT NOT NULL,
        ExternalSystemCalled BIT NOT NULL,
        TicketCreated BIT NOT NULL,
        MemoryWritten BIT NOT NULL,
        ApprovalGranted BIT NOT NULL,
        ShellCommandRun BIT NOT NULL,
        ToolName NVARCHAR(160) NULL,
        AffectedArtifactType NVARCHAR(80) NULL,
        AffectedArtifactId NVARCHAR(160) NULL,
        ThoughtLedgerEntryId NVARCHAR(120) NULL,
        CorrelationId NVARCHAR(120) NULL,
        Summary NVARCHAR(MAX) NOT NULL,
        MemoryItemIdsJson NVARCHAR(MAX) NOT NULL,
        InfluenceIdsJson NVARCHAR(MAX) NOT NULL,
        HandoffMemorySliceIdsJson NVARCHAR(MAX) NOT NULL,
        EvidencePathsJson NVARCHAR(MAX) NOT NULL,
        BlockersJson NVARCHAR(MAX) NOT NULL,
        WarningsJson NVARCHAR(MAX) NOT NULL,
        IssueCodesJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_AgentMemoryExecutionAudit PRIMARY KEY (AuditId),
        CONSTRAINT CK_AgentMemoryExecutionAudit_ActionType CHECK (ActionType BETWEEN 1 AND 9),
        CONSTRAINT CK_AgentMemoryExecutionAudit_Outcome CHECK (Outcome BETWEEN 1 AND 7),
        CONSTRAINT CK_AgentMemoryExecutionAudit_GateDecision CHECK (GateDecision BETWEEN 1 AND 4),
        CONSTRAINT CK_AgentMemoryExecutionAudit_GovernanceDecision CHECK (GovernanceDecision IS NULL OR GovernanceDecision BETWEEN 1 AND 3),
        CONSTRAINT CK_AgentMemoryExecutionAudit_DecisionRequired CHECK (LEN(LTRIM(RTRIM(DecisionId))) > 0),
        CONSTRAINT CK_AgentMemoryExecutionAudit_SkillRequired CHECK (LEN(LTRIM(RTRIM(SkillId))) > 0),
        CONSTRAINT CK_AgentMemoryExecutionAudit_SummaryRequired CHECK (LEN(LTRIM(RTRIM(Summary))) > 0),
        CONSTRAINT CK_AgentMemoryExecutionAudit_Json CHECK
        (
            ISJSON(MemoryItemIdsJson) = 1
            AND ISJSON(InfluenceIdsJson) = 1
            AND ISJSON(HandoffMemorySliceIdsJson) = 1
            AND ISJSON(EvidencePathsJson) = 1
            AND ISJSON(BlockersJson) = 1
            AND ISJSON(WarningsJson) = 1
            AND ISJSON(IssueCodesJson) = 1
        )
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryExecutionAudit_RunCreated' AND object_id = OBJECT_ID('agent.AgentMemoryExecutionAudit'))
    CREATE INDEX IX_AgentMemoryExecutionAudit_RunCreated
        ON agent.AgentMemoryExecutionAudit(TenantId, ProjectId, CampaignId, RunId, AgentId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryExecutionAudit_Decision' AND object_id = OBJECT_ID('agent.AgentMemoryExecutionAudit'))
    CREATE INDEX IX_AgentMemoryExecutionAudit_Decision
        ON agent.AgentMemoryExecutionAudit(TenantId, ProjectId, CampaignId, RunId, AgentId, DecisionId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryExecutionAudit_GovernanceCheck' AND object_id = OBJECT_ID('agent.AgentMemoryExecutionAudit'))
    CREATE INDEX IX_AgentMemoryExecutionAudit_GovernanceCheck
        ON agent.AgentMemoryExecutionAudit(TenantId, ProjectId, CampaignId, RunId, AgentId, GovernanceCheckId, CreatedAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentMemoryExecutionAudit_Execution' AND object_id = OBJECT_ID('agent.AgentMemoryExecutionAudit'))
    CREATE INDEX IX_AgentMemoryExecutionAudit_Execution
        ON agent.AgentMemoryExecutionAudit(TenantId, ProjectId, CampaignId, RunId, AgentId, ExecutionId, CreatedAtUtc);

IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryExecutionAudit_BlockUpdateDelete
        ON agent.AgentMemoryExecutionAudit
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51080, ''AgentMemoryExecutionAudit is append-only. Audit records cannot be updated or deleted.'', 1;
        END');

IF OBJECT_ID('agent.TR_AgentMemoryExecutionAudit_ValidateInsert', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentMemoryExecutionAudit_ValidateInsert
        ON agent.AgentMemoryExecutionAudit
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            IF EXISTS
            (
                SELECT 1
                FROM inserted a
                WHERE NOT EXISTS (SELECT 1 FROM OPENJSON(a.MemoryItemIdsJson))
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON(a.InfluenceIdsJson))
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON(a.HandoffMemorySliceIdsJson))
            )
            BEGIN
                THROW 51081, ''Memory execution audit requires at least one memory, influence, or handoff reference.'', 1;
            END
        END');

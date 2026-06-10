-- =====================================================================
-- migrate_agent_run_audit_envelope.sql
--
-- Durable append-only storage for safe AgentRunAuditEnvelope records.
-- The API exposes read-only projections; this table is the persistence
-- surface only and does not add runtime execution or approval authority.
-- Safe to rerun. All blocks are idempotent.
-- =====================================================================

IF SCHEMA_ID('agent') IS NULL
    EXEC('CREATE SCHEMA agent');

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NULL
BEGIN
    CREATE TABLE agent.AgentRunAuditEnvelope
    (
        AgentRunAuditEnvelopeId BIGINT IDENTITY(1,1) NOT NULL,
        TenantId NVARCHAR(80) NOT NULL,
        ProjectId NVARCHAR(80) NOT NULL,
        CampaignId NVARCHAR(80) NOT NULL,
        RunId NVARCHAR(120) NOT NULL,
        AgentRunId NVARCHAR(160) NOT NULL,
        AgentId NVARCHAR(160) NOT NULL,
        AgentName NVARCHAR(200) NOT NULL,
        AgentKind INT NOT NULL,
        ExecutionMode INT NOT NULL,
        Status INT NOT NULL,
        TriggerType INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        HasRawPrivateReasoning BIT NOT NULL,
        HasAuthorityClaim BIT NOT NULL,
        HasApprovalClaim BIT NOT NULL,
        HasMemoryPromotionClaim BIT NOT NULL,
        HasRuntimeActionOutput BIT NOT NULL,
        HasAuthorityCreatingOutput BIT NOT NULL,
        HasBlockedCapabilityAttempt BIT NOT NULL,
        HasBoundaryBlock BIT NOT NULL,
        EnvelopeSha256 CHAR(64) NOT NULL,
        EnvelopeJson NVARCHAR(MAX) NOT NULL,
        AppendedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_AgentRunAuditEnvelope PRIMARY KEY (AgentRunAuditEnvelopeId),
        CONSTRAINT UX_AgentRunAuditEnvelope_Run UNIQUE (TenantId, ProjectId, AgentRunId),
        CONSTRAINT CK_AgentRunAuditEnvelope_ProjectRequired CHECK (LEN(LTRIM(RTRIM(ProjectId))) > 0),
        CONSTRAINT CK_AgentRunAuditEnvelope_AgentRunRequired CHECK (LEN(LTRIM(RTRIM(AgentRunId))) > 0),
        CONSTRAINT CK_AgentRunAuditEnvelope_AgentRequired CHECK (LEN(LTRIM(RTRIM(AgentId))) > 0 AND LEN(LTRIM(RTRIM(AgentName))) > 0),
        CONSTRAINT CK_AgentRunAuditEnvelope_Status CHECK (Status BETWEEN 1 AND 7),
        CONSTRAINT CK_AgentRunAuditEnvelope_TriggerType CHECK (TriggerType BETWEEN 1 AND 4),
        CONSTRAINT CK_AgentRunAuditEnvelope_Sha256 CHECK (EnvelopeSha256 NOT LIKE '%[^0-9a-f]%' AND LEN(EnvelopeSha256) = 64),
        CONSTRAINT CK_AgentRunAuditEnvelope_Json CHECK (ISJSON(EnvelopeJson) = 1)
    );
END

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AgentRunAuditEnvelope_NoRawPrivateReasoning' AND parent_object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    ALTER TABLE agent.AgentRunAuditEnvelope WITH CHECK
        ADD CONSTRAINT CK_AgentRunAuditEnvelope_NoRawPrivateReasoning CHECK (HasRawPrivateReasoning = 0);

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AgentRunAuditEnvelope_NoAuthorityClaim' AND parent_object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    ALTER TABLE agent.AgentRunAuditEnvelope WITH CHECK
        ADD CONSTRAINT CK_AgentRunAuditEnvelope_NoAuthorityClaim CHECK (HasAuthorityClaim = 0);

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AgentRunAuditEnvelope_NoApprovalClaim' AND parent_object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    ALTER TABLE agent.AgentRunAuditEnvelope WITH CHECK
        ADD CONSTRAINT CK_AgentRunAuditEnvelope_NoApprovalClaim CHECK (HasApprovalClaim = 0);

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AgentRunAuditEnvelope_NoMemoryPromotionClaim' AND parent_object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    ALTER TABLE agent.AgentRunAuditEnvelope WITH CHECK
        ADD CONSTRAINT CK_AgentRunAuditEnvelope_NoMemoryPromotionClaim CHECK (HasMemoryPromotionClaim = 0);

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AgentRunAuditEnvelope_NoRuntimeActionOutput' AND parent_object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    ALTER TABLE agent.AgentRunAuditEnvelope WITH CHECK
        ADD CONSTRAINT CK_AgentRunAuditEnvelope_NoRuntimeActionOutput CHECK (HasRuntimeActionOutput = 0);

IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AgentRunAuditEnvelope_NoAuthorityCreatingOutput' AND parent_object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    ALTER TABLE agent.AgentRunAuditEnvelope WITH CHECK
        ADD CONSTRAINT CK_AgentRunAuditEnvelope_NoAuthorityCreatingOutput CHECK (HasAuthorityCreatingOutput = 0);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentRunAuditEnvelope_ProjectCreated' AND object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    CREATE INDEX IX_AgentRunAuditEnvelope_ProjectCreated
        ON agent.AgentRunAuditEnvelope(ProjectId, CreatedAtUtc DESC, AgentRunId ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentRunAuditEnvelope_Scope' AND object_id = OBJECT_ID('agent.AgentRunAuditEnvelope'))
    CREATE INDEX IX_AgentRunAuditEnvelope_Scope
        ON agent.AgentRunAuditEnvelope(TenantId, ProjectId, CampaignId, RunId, AgentId, CreatedAtUtc DESC);

IF OBJECT_ID('agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete', 'TR') IS NULL
    EXEC('
        CREATE TRIGGER agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete
        ON agent.AgentRunAuditEnvelope
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51120, ''AgentRunAuditEnvelope is append-only. Audit envelopes cannot be updated or deleted.'', 1;
        END');

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'toolaudit')
BEGIN
    EXEC(N'CREATE SCHEMA toolaudit');
END;
GO

IF OBJECT_ID(N'toolaudit.ToolExecutionAuditRecord', N'U') IS NULL
BEGIN
    CREATE TABLE toolaudit.ToolExecutionAuditRecord
    (
        ToolExecutionAuditId NVARCHAR(160) NOT NULL,
        TenantId NVARCHAR(120) NOT NULL,
        ProjectId NVARCHAR(120) NOT NULL,
        CampaignId NVARCHAR(120) NULL,
        RunId NVARCHAR(120) NULL,
        AgentRunId NVARCHAR(160) NOT NULL,
        ManualExecutionId NVARCHAR(160) NOT NULL,
        ToolRequestId NVARCHAR(160) NOT NULL,
        GateDecisionId NVARCHAR(160) NOT NULL,
        ToolKind NVARCHAR(80) NOT NULL,
        RequestType NVARCHAR(80) NOT NULL,
        AgentKind NVARCHAR(80) NOT NULL,
        AgentId NVARCHAR(160) NOT NULL,
        AgentName NVARCHAR(200) NOT NULL,
        Status NVARCHAR(80) NOT NULL,
        Succeeded BIT NOT NULL,
        PayloadKind NVARCHAR(120) NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL,
        PayloadSha256 CHAR(64) NOT NULL,
        AuditEnvelopeJson NVARCHAR(MAX) NOT NULL,
        AuditEnvelopeSha256 CHAR(64) NOT NULL,
        EvidenceRefsJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ContainsRawPrivateReasoning BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ContainsRawPrivateReasoning DEFAULT(0),
        ContainsSecret BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ContainsSecret DEFAULT(0),
        ClaimsApproval BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ClaimsApproval DEFAULT(0),
        ClaimsPolicyApproval BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ClaimsPolicyApproval DEFAULT(0),
        ClaimsHumanApproval BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ClaimsHumanApproval DEFAULT(0),
        ClaimsMemoryPromotion BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ClaimsMemoryPromotion DEFAULT(0),
        ExecutesTool BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_ExecutesTool DEFAULT(0),
        MutatesSource BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_MutatesSource DEFAULT(0),
        AppliesPatch BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_AppliesPatch DEFAULT(0),
        WritesFiles BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_WritesFiles DEFAULT(0),
        DeletesFiles BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_DeletesFiles DEFAULT(0),
        RunsGit BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_RunsGit DEFAULT(0),
        CallsExternalSystem BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_CallsExternalSystem DEFAULT(0),
        SubmitsGitHubReview BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_SubmitsGitHubReview DEFAULT(0),
        CreatesPullRequest BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_CreatesPullRequest DEFAULT(0),
        PromotesMemory BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_PromotesMemory DEFAULT(0),
        CreatesCollectiveMemory BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_CreatesCollectiveMemory DEFAULT(0),
        WritesWeaviate BIT NOT NULL CONSTRAINT DF_ToolExecutionAuditRecord_WritesWeaviate DEFAULT(0),
        CONSTRAINT PK_ToolExecutionAuditRecord PRIMARY KEY (TenantId, ProjectId, ToolExecutionAuditId),
        CONSTRAINT CK_ToolExecutionAuditRecord_Ids CHECK
        (
            LEN(ToolExecutionAuditId) > 0 AND
            LEN(TenantId) > 0 AND
            LEN(ProjectId) > 0 AND
            LEN(AgentRunId) > 0 AND
            LEN(ManualExecutionId) > 0 AND
            LEN(ToolRequestId) > 0 AND
            LEN(GateDecisionId) > 0 AND
            LEN(AgentId) > 0 AND
            LEN(AgentName) > 0
        ),
        CONSTRAINT CK_ToolExecutionAuditRecord_ToolKind CHECK (ToolKind IN (N'TestRun', N'PatchProposal')),
        CONSTRAINT CK_ToolExecutionAuditRecord_RequestType CHECK (RequestType IN (N'TestExecutionRequest', N'PatchProposalRequest')),
        CONSTRAINT CK_ToolExecutionAuditRecord_AgentKind CHECK (AgentKind IN (N'TestingAgent', N'ImplementationAgent')),
        CONSTRAINT CK_ToolExecutionAuditRecord_PayloadKind CHECK (PayloadKind IN (N'ManualTesterAgentToolExecution', N'ManualImplementationPatchProposal')),
        CONSTRAINT CK_ToolExecutionAuditRecord_ToolShape CHECK
        (
            (ToolKind = N'TestRun' AND RequestType = N'TestExecutionRequest' AND AgentKind = N'TestingAgent' AND PayloadKind = N'ManualTesterAgentToolExecution') OR
            (ToolKind = N'PatchProposal' AND RequestType = N'PatchProposalRequest' AND AgentKind = N'ImplementationAgent' AND PayloadKind = N'ManualImplementationPatchProposal')
        ),
        CONSTRAINT CK_ToolExecutionAuditRecord_Json CHECK
        (
            ISJSON(PayloadJson) = 1 AND
            ISJSON(AuditEnvelopeJson) = 1 AND
            ISJSON(EvidenceRefsJson) = 1
        ),
        CONSTRAINT CK_ToolExecutionAuditRecord_Hashes CHECK
        (
            PayloadSha256 LIKE REPLICATE('[0-9a-f]', 64) AND
            AuditEnvelopeSha256 LIKE REPLICATE('[0-9a-f]', 64)
        ),
        CONSTRAINT CK_ToolExecutionAuditRecord_NoRawPrivateReasoning CHECK (ContainsRawPrivateReasoning = 0),
        CONSTRAINT CK_ToolExecutionAuditRecord_NoSecret CHECK (ContainsSecret = 0),
        CONSTRAINT CK_ToolExecutionAuditRecord_NoApprovalClaim CHECK (ClaimsApproval = 0 AND ClaimsPolicyApproval = 0 AND ClaimsHumanApproval = 0),
        CONSTRAINT CK_ToolExecutionAuditRecord_NoMemoryPromotionClaim CHECK (ClaimsMemoryPromotion = 0 AND PromotesMemory = 0 AND CreatesCollectiveMemory = 0),
        CONSTRAINT CK_ToolExecutionAuditRecord_NoUnsafeEffects CHECK
        (
            ExecutesTool = 0 AND
            MutatesSource = 0 AND
            AppliesPatch = 0 AND
            WritesFiles = 0 AND
            DeletesFiles = 0 AND
            RunsGit = 0 AND
            CallsExternalSystem = 0 AND
            SubmitsGitHubReview = 0 AND
            CreatesPullRequest = 0 AND
            WritesWeaviate = 0
        )
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolExecutionAuditRecord_Run' AND object_id = OBJECT_ID(N'toolaudit.ToolExecutionAuditRecord'))
BEGIN
    CREATE INDEX IX_ToolExecutionAuditRecord_Run
    ON toolaudit.ToolExecutionAuditRecord(TenantId, ProjectId, RunId, CreatedAtUtc, ToolExecutionAuditId);
END;
GO

IF OBJECT_ID(N'toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete;
END;
GO

CREATE TRIGGER toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete
ON toolaudit.ToolExecutionAuditRecord
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52071, 'Tool execution audit records are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE toolaudit.AppendToolExecutionAuditRecord
    @ToolExecutionAuditId NVARCHAR(160),
    @TenantId NVARCHAR(120),
    @ProjectId NVARCHAR(120),
    @CampaignId NVARCHAR(120) = NULL,
    @RunId NVARCHAR(120) = NULL,
    @AgentRunId NVARCHAR(160),
    @ManualExecutionId NVARCHAR(160),
    @ToolRequestId NVARCHAR(160),
    @GateDecisionId NVARCHAR(160),
    @ToolKind NVARCHAR(80),
    @RequestType NVARCHAR(80),
    @AgentKind NVARCHAR(80),
    @AgentId NVARCHAR(160),
    @AgentName NVARCHAR(200),
    @Status NVARCHAR(80),
    @Succeeded BIT,
    @PayloadKind NVARCHAR(120),
    @PayloadJson NVARCHAR(MAX),
    @PayloadSha256 CHAR(64),
    @AuditEnvelopeJson NVARCHAR(MAX),
    @AuditEnvelopeSha256 CHAR(64),
    @EvidenceRefsJson NVARCHAR(MAX),
    @CreatedAtUtc DATETIMEOFFSET(7),
    @ContainsRawPrivateReasoning BIT,
    @ContainsSecret BIT,
    @ClaimsApproval BIT,
    @ClaimsPolicyApproval BIT,
    @ClaimsHumanApproval BIT,
    @ClaimsMemoryPromotion BIT,
    @ExecutesTool BIT,
    @MutatesSource BIT,
    @AppliesPatch BIT,
    @WritesFiles BIT,
    @DeletesFiles BIT,
    @RunsGit BIT,
    @CallsExternalSystem BIT,
    @SubmitsGitHubReview BIT,
    @CreatesPullRequest BIT,
    @PromotesMemory BIT,
    @CreatesCollectiveMemory BIT,
    @WritesWeaviate BIT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ExistingPayloadSha256 CHAR(64);
    DECLARE @ExistingAuditEnvelopeSha256 CHAR(64);

    SELECT
        @ExistingPayloadSha256 = PayloadSha256,
        @ExistingAuditEnvelopeSha256 = AuditEnvelopeSha256
    FROM toolaudit.ToolExecutionAuditRecord
    WHERE TenantId = @TenantId
      AND ProjectId = @ProjectId
      AND ToolExecutionAuditId = @ToolExecutionAuditId;

    IF @ExistingPayloadSha256 IS NOT NULL
    BEGIN
        IF @ExistingPayloadSha256 = @PayloadSha256 AND @ExistingAuditEnvelopeSha256 = @AuditEnvelopeSha256
            SELECT CAST(N'AlreadyExists' AS NVARCHAR(40)) AS Status;
        ELSE
            SELECT CAST(N'Conflict' AS NVARCHAR(40)) AS Status;
        RETURN;
    END;

    INSERT INTO toolaudit.ToolExecutionAuditRecord
    (
        ToolExecutionAuditId,
        TenantId,
        ProjectId,
        CampaignId,
        RunId,
        AgentRunId,
        ManualExecutionId,
        ToolRequestId,
        GateDecisionId,
        ToolKind,
        RequestType,
        AgentKind,
        AgentId,
        AgentName,
        Status,
        Succeeded,
        PayloadKind,
        PayloadJson,
        PayloadSha256,
        AuditEnvelopeJson,
        AuditEnvelopeSha256,
        EvidenceRefsJson,
        CreatedAtUtc,
        ContainsRawPrivateReasoning,
        ContainsSecret,
        ClaimsApproval,
        ClaimsPolicyApproval,
        ClaimsHumanApproval,
        ClaimsMemoryPromotion,
        ExecutesTool,
        MutatesSource,
        AppliesPatch,
        WritesFiles,
        DeletesFiles,
        RunsGit,
        CallsExternalSystem,
        SubmitsGitHubReview,
        CreatesPullRequest,
        PromotesMemory,
        CreatesCollectiveMemory,
        WritesWeaviate
    )
    VALUES
    (
        @ToolExecutionAuditId,
        @TenantId,
        @ProjectId,
        @CampaignId,
        @RunId,
        @AgentRunId,
        @ManualExecutionId,
        @ToolRequestId,
        @GateDecisionId,
        @ToolKind,
        @RequestType,
        @AgentKind,
        @AgentId,
        @AgentName,
        @Status,
        @Succeeded,
        @PayloadKind,
        @PayloadJson,
        @PayloadSha256,
        @AuditEnvelopeJson,
        @AuditEnvelopeSha256,
        @EvidenceRefsJson,
        @CreatedAtUtc,
        @ContainsRawPrivateReasoning,
        @ContainsSecret,
        @ClaimsApproval,
        @ClaimsPolicyApproval,
        @ClaimsHumanApproval,
        @ClaimsMemoryPromotion,
        @ExecutesTool,
        @MutatesSource,
        @AppliesPatch,
        @WritesFiles,
        @DeletesFiles,
        @RunsGit,
        @CallsExternalSystem,
        @SubmitsGitHubReview,
        @CreatesPullRequest,
        @PromotesMemory,
        @CreatesCollectiveMemory,
        @WritesWeaviate
    );

    SELECT CAST(N'Appended' AS NVARCHAR(40)) AS Status;
END;
GO

CREATE OR ALTER PROCEDURE toolaudit.GetToolExecutionAuditRecord
    @TenantId NVARCHAR(120),
    @ProjectId NVARCHAR(120),
    @ToolExecutionAuditId NVARCHAR(160)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) *
    FROM toolaudit.ToolExecutionAuditRecord
    WHERE TenantId = @TenantId
      AND ProjectId = @ProjectId
      AND ToolExecutionAuditId = @ToolExecutionAuditId;
END;
GO

CREATE OR ALTER PROCEDURE toolaudit.ListToolExecutionAuditRecordsByRun
    @TenantId NVARCHAR(120),
    @ProjectId NVARCHAR(120),
    @RunId NVARCHAR(120),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END) *
    FROM toolaudit.ToolExecutionAuditRecord
    WHERE TenantId = @TenantId
      AND ProjectId = @ProjectId
      AND RunId = @RunId
    ORDER BY CreatedAtUtc ASC, ToolExecutionAuditId ASC;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevToolExecutionAuditRuntimeRole' AND type = N'R')
BEGIN
    CREATE ROLE IronDevToolExecutionAuditRuntimeRole;
END;
GO

GRANT EXECUTE ON OBJECT::toolaudit.AppendToolExecutionAuditRecord TO IronDevToolExecutionAuditRuntimeRole;
GRANT EXECUTE ON OBJECT::toolaudit.GetToolExecutionAuditRecord TO IronDevToolExecutionAuditRuntimeRole;
GRANT EXECUTE ON OBJECT::toolaudit.ListToolExecutionAuditRecordsByRun TO IronDevToolExecutionAuditRuntimeRole;
GRANT SELECT ON OBJECT::toolaudit.ToolExecutionAuditRecord TO IronDevToolExecutionAuditRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::toolaudit.ToolExecutionAuditRecord TO IronDevToolExecutionAuditRuntimeRole;
DENY ALTER ON SCHEMA::toolaudit TO IronDevToolExecutionAuditRuntimeRole;
GO

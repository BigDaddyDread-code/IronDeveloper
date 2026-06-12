IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
BEGIN
    THROW 52200, 'governance.GovernanceEvent must exist before governance.ToolGateDecision migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL
BEGIN
    THROW 52201, 'governance.ToolRequest must exist before governance.ToolGateDecision migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NULL
BEGIN
    CREATE TABLE governance.ToolGateDecision
    (
        ToolGateDecisionId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ToolRequestId UNIQUEIDENTIFIER NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        CausationId UNIQUEIDENTIFIER NOT NULL,
        Decision NVARCHAR(40) NOT NULL,
        GateName NVARCHAR(160) NOT NULL,
        GateVersion INT NOT NULL,
        ActorType NVARCHAR(80) NOT NULL,
        ActorId NVARCHAR(200) NOT NULL,
        ReasonCode NVARCHAR(160) NOT NULL,
        EvidenceVersion INT NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        GrantsApproval BIT NOT NULL CONSTRAINT DF_ToolGateDecision_GrantsApproval DEFAULT 0,
        GrantsExecution BIT NOT NULL CONSTRAINT DF_ToolGateDecision_GrantsExecution DEFAULT 0,
        MutatesSource BIT NOT NULL CONSTRAINT DF_ToolGateDecision_MutatesSource DEFAULT 0,
        PromotesMemory BIT NOT NULL CONSTRAINT DF_ToolGateDecision_PromotesMemory DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ToolGateDecision_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ToolGateDecision PRIMARY KEY CLUSTERED (ToolGateDecisionId),
        CONSTRAINT FK_ToolGateDecision_ToolRequest FOREIGN KEY (ToolRequestId) REFERENCES governance.ToolRequest(ToolRequestId),
        CONSTRAINT FK_ToolGateDecision_GovernanceEvent FOREIGN KEY (GovernanceEventId) REFERENCES governance.GovernanceEvent(EventId),
        CONSTRAINT CK_ToolGateDecision_Decision_Allowed CHECK (Decision IN (N'Passed', N'Blocked', N'RequiresApproval')),
        CONSTRAINT CK_ToolGateDecision_Decision_NotApproval CHECK (Decision NOT IN (N'Approved', N'Authorized', N'Executable', N'ReadyToRun', N'HumanApproved', N'ExecutionGranted', N'PermissionGranted')),
        CONSTRAINT CK_ToolGateDecision_GateName_NotBlank CHECK (LEN(LTRIM(RTRIM(GateName))) > 0),
        CONSTRAINT CK_ToolGateDecision_GateVersion_Positive CHECK (GateVersion > 0),
        CONSTRAINT CK_ToolGateDecision_ActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(ActorType))) > 0),
        CONSTRAINT CK_ToolGateDecision_ActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(ActorId))) > 0),
        CONSTRAINT CK_ToolGateDecision_ReasonCode_NotBlank CHECK (LEN(LTRIM(RTRIM(ReasonCode))) > 0),
        CONSTRAINT CK_ToolGateDecision_EvidenceVersion_Positive CHECK (EvidenceVersion > 0),
        CONSTRAINT CK_ToolGateDecision_EvidenceJson_IsJson CHECK (ISJSON(EvidenceJson) = 1),
        CONSTRAINT CK_ToolGateDecision_EvidenceJson_SchemaVersion CHECK (JSON_VALUE(EvidenceJson, '$.schemaVersion') IS NOT NULL),
        CONSTRAINT CK_ToolGateDecision_NoApprovalGrant CHECK (GrantsApproval = 0),
        CONSTRAINT CK_ToolGateDecision_NoExecutionGrant CHECK (GrantsExecution = 0),
        CONSTRAINT CK_ToolGateDecision_NoSourceMutation CHECK (MutatesSource = 0),
        CONSTRAINT CK_ToolGateDecision_NoMemoryPromotion CHECK (PromotesMemory = 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolGateDecision_Project_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ToolGateDecision'))
BEGIN
    CREATE INDEX IX_ToolGateDecision_Project_CreatedUtc
    ON governance.ToolGateDecision(ProjectId, CreatedUtc DESC, ToolGateDecisionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolGateDecision_ToolRequest_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ToolGateDecision'))
BEGIN
    CREATE INDEX IX_ToolGateDecision_ToolRequest_CreatedUtc
    ON governance.ToolGateDecision(ToolRequestId, CreatedUtc DESC, ToolGateDecisionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolGateDecision_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ToolGateDecision'))
BEGIN
    CREATE INDEX IX_ToolGateDecision_Correlation_CreatedUtc
    ON governance.ToolGateDecision(CorrelationId, CreatedUtc DESC, ToolGateDecisionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolGateDecision_GovernanceEventId' AND object_id = OBJECT_ID(N'governance.ToolGateDecision'))
BEGIN
    CREATE INDEX IX_ToolGateDecision_GovernanceEventId
    ON governance.ToolGateDecision(GovernanceEventId);
END;
GO

IF OBJECT_ID(N'governance.TR_ToolGateDecision_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete
ON governance.ToolGateDecision
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52290, 'Tool gate decisions are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolGateDecision_Record
    @ToolGateDecisionId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ToolRequestId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @Decision NVARCHAR(40),
    @GateName NVARCHAR(160),
    @GateVersion INT,
    @ActorType NVARCHAR(80),
    @ActorId NVARCHAR(200),
    @ReasonCode NVARCHAR(160),
    @EvidenceVersion INT,
    @EvidenceJson NVARCHAR(MAX),
    @GovernanceEventPayloadJson NVARCHAR(MAX)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    DECLARE @ToolRequestProjectId UNIQUEIDENTIFIER;
    DECLARE @ToolRequestGovernanceEventId UNIQUEIDENTIFIER;
    DECLARE @ToolRequestCorrelationId UNIQUEIDENTIFIER;

    SELECT
        @ToolRequestProjectId = ProjectId,
        @ToolRequestGovernanceEventId = GovernanceEventId,
        @ToolRequestCorrelationId = CorrelationId
    FROM governance.ToolRequest
    WHERE ToolRequestId = @ToolRequestId;

    IF @ToolRequestProjectId IS NULL
    BEGIN
        THROW 52202, 'Referenced tool request does not exist.', 1;
    END;

    IF @ToolRequestProjectId <> @ProjectId
    BEGIN
        THROW 52203, 'Tool gate decision project does not match referenced tool request project.', 1;
    END;

    DECLARE @EffectiveCorrelationId UNIQUEIDENTIFIER = COALESCE(@CorrelationId, @ToolRequestCorrelationId, @ToolGateDecisionId);
    DECLARE @EffectiveCausationId UNIQUEIDENTIFIER = COALESCE(@CausationId, @ToolRequestGovernanceEventId);

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @ToolGateDecisionId)
        BEGIN
            THROW 52204, 'Tool gate decision already exists.', 1;
        END;

        INSERT INTO governance.GovernanceEvent
        (
            EventId,
            ProjectId,
            EventType,
            ActorType,
            ActorId,
            CorrelationId,
            CausationId,
            SubjectType,
            SubjectId,
            PayloadVersion,
            PayloadJson
        )
        VALUES
        (
            @GovernanceEventId,
            @ProjectId,
            N'tool.gate.decision.recorded',
            @ActorType,
            @ActorId,
            @EffectiveCorrelationId,
            @EffectiveCausationId,
            N'tool_gate_decision',
            CONVERT(NVARCHAR(36), @ToolGateDecisionId),
            1,
            @GovernanceEventPayloadJson
        );

        INSERT INTO governance.ToolGateDecision
        (
            ToolGateDecisionId,
            ProjectId,
            ToolRequestId,
            GovernanceEventId,
            CorrelationId,
            CausationId,
            Decision,
            GateName,
            GateVersion,
            ActorType,
            ActorId,
            ReasonCode,
            EvidenceVersion,
            EvidenceJson,
            GrantsApproval,
            GrantsExecution,
            MutatesSource,
            PromotesMemory
        )
        VALUES
        (
            @ToolGateDecisionId,
            @ProjectId,
            @ToolRequestId,
            @GovernanceEventId,
            @EffectiveCorrelationId,
            @EffectiveCausationId,
            @Decision,
            @GateName,
            @GateVersion,
            @ActorType,
            @ActorId,
            @ReasonCode,
            @EvidenceVersion,
            @EvidenceJson,
            0,
            0,
            0,
            0
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    EXEC governance.usp_ToolGateDecision_GetById @ProjectId = @ProjectId, @ToolGateDecisionId = @ToolGateDecisionId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolGateDecision_GetById
    @ProjectId UNIQUEIDENTIFIER,
    @ToolGateDecisionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ToolGateDecisionId,
        ProjectId,
        ToolRequestId,
        GovernanceEventId,
        CorrelationId,
        CausationId,
        Decision,
        GateName,
        GateVersion,
        ActorType,
        ActorId,
        ReasonCode,
        EvidenceVersion,
        EvidenceJson,
        CreatedUtc
    FROM governance.ToolGateDecision
    WHERE ProjectId = @ProjectId
      AND ToolGateDecisionId = @ToolGateDecisionId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolGateDecision_ListForToolRequest
    @ProjectId UNIQUEIDENTIFIER,
    @ToolRequestId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        ToolGateDecisionId,
        ProjectId,
        ToolRequestId,
        GovernanceEventId,
        CorrelationId,
        CausationId,
        Decision,
        GateName,
        GateVersion,
        ActorType,
        ActorId,
        ReasonCode,
        CreatedUtc
    FROM governance.ToolGateDecision
    WHERE ProjectId = @ProjectId
      AND ToolRequestId = @ToolRequestId
    ORDER BY CreatedUtc DESC, ToolGateDecisionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolGateDecision_ListForProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        ToolGateDecisionId,
        ProjectId,
        ToolRequestId,
        GovernanceEventId,
        CorrelationId,
        CausationId,
        Decision,
        GateName,
        GateVersion,
        ActorType,
        ActorId,
        ReasonCode,
        CreatedUtc
    FROM governance.ToolGateDecision
    WHERE ProjectId = @ProjectId
    ORDER BY CreatedUtc DESC, ToolGateDecisionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolGateDecision_ListForCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        ToolGateDecisionId,
        ProjectId,
        ToolRequestId,
        GovernanceEventId,
        CorrelationId,
        CausationId,
        Decision,
        GateName,
        GateVersion,
        ActorType,
        ActorId,
        ReasonCode,
        CreatedUtc
    FROM governance.ToolGateDecision
    WHERE ProjectId = @ProjectId
      AND CorrelationId = @CorrelationId
    ORDER BY CreatedUtc DESC, ToolGateDecisionId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_ToolGateDecision_Record TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ToolGateDecision_GetById TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ToolGateDecision_ListForToolRequest TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ToolGateDecision_ListForProject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ToolGateDecision_ListForCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.ToolGateDecision TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ToolGateDecision TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

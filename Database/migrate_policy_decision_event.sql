IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
BEGIN
    THROW 52400, 'governance.GovernanceEvent must exist before governance.PolicyDecisionEvent migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL
BEGIN
    THROW 52401, 'governance.ToolRequest must exist before governance.PolicyDecisionEvent migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NULL
BEGIN
    THROW 52402, 'governance.ToolGateDecision must exist before governance.PolicyDecisionEvent migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NULL
BEGIN
    THROW 52403, 'governance.ApprovalDecision must exist before governance.PolicyDecisionEvent migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NULL
BEGIN
    CREATE TABLE governance.PolicyDecisionEvent
    (
        PolicyDecisionEventId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        PolicyScope NVARCHAR(120) NOT NULL,
        PolicyName NVARCHAR(160) NOT NULL,
        PolicyVersion INT NOT NULL,
        SubjectType NVARCHAR(120) NOT NULL,
        SubjectId NVARCHAR(200) NOT NULL,
        Decision NVARCHAR(40) NOT NULL,
        RequirementCode NVARCHAR(160) NOT NULL,
        ReasonCode NVARCHAR(160) NOT NULL,
        Reason NVARCHAR(500) NULL,
        DecidedByActorType NVARCHAR(80) NOT NULL,
        DecidedByActorId NVARCHAR(200) NOT NULL,
        RelatedToolRequestId UNIQUEIDENTIFIER NULL,
        RelatedToolGateDecisionId UNIQUEIDENTIFIER NULL,
        RelatedApprovalDecisionId UNIQUEIDENTIFIER NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        EvidenceVersion INT NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_PolicyDecisionEvent_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PolicyDecisionEvent PRIMARY KEY CLUSTERED (PolicyDecisionEventId),
        CONSTRAINT FK_PolicyDecisionEvent_GovernanceEvent FOREIGN KEY (GovernanceEventId) REFERENCES governance.GovernanceEvent(EventId),
        CONSTRAINT FK_PolicyDecisionEvent_ToolRequest FOREIGN KEY (RelatedToolRequestId) REFERENCES governance.ToolRequest(ToolRequestId),
        CONSTRAINT FK_PolicyDecisionEvent_ToolGateDecision FOREIGN KEY (RelatedToolGateDecisionId) REFERENCES governance.ToolGateDecision(ToolGateDecisionId),
        CONSTRAINT FK_PolicyDecisionEvent_ApprovalDecision FOREIGN KEY (RelatedApprovalDecisionId) REFERENCES governance.ApprovalDecision(ApprovalDecisionId),
        CONSTRAINT CK_PolicyDecisionEvent_PolicyScope_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicyScope))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_PolicyName_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicyName))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_PolicyVersion_Positive CHECK (PolicyVersion > 0),
        CONSTRAINT CK_PolicyDecisionEvent_SubjectType_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectType))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_Decision_Allowed CHECK (Decision IN (N'NoPolicyBlock', N'Blocked', N'RequiresApproval', N'NotApplicable')),
        CONSTRAINT CK_PolicyDecisionEvent_Decision_NotAuthority CHECK (Decision NOT IN (N'Allowed', N'Approved', N'Authorized', N'Executable', N'ReadyToRun', N'PermissionGranted', N'PolicySatisfied', N'ApprovalSatisfied', N'ExecutionGranted', N'CanExecute', N'ApplyAllowed', N'PromotionAllowed', N'ReleaseApproved')),
        CONSTRAINT CK_PolicyDecisionEvent_RequirementCode_NotBlank CHECK (LEN(LTRIM(RTRIM(RequirementCode))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_ReasonCode_NotBlank CHECK (LEN(LTRIM(RTRIM(ReasonCode))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_DecidedByActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(DecidedByActorType))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_DecidedByActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(DecidedByActorId))) > 0),
        CONSTRAINT CK_PolicyDecisionEvent_EvidenceVersion_Positive CHECK (EvidenceVersion > 0),
        CONSTRAINT CK_PolicyDecisionEvent_EvidenceJson_IsJson CHECK (ISJSON(EvidenceJson) = 1),
        CONSTRAINT CK_PolicyDecisionEvent_EvidenceJson_Versioned CHECK (JSON_VALUE(EvidenceJson, '$.schema') IS NOT NULL OR JSON_VALUE(EvidenceJson, '$.schemaVersion') IS NOT NULL)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_Project_CreatedUtc' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_Project_CreatedUtc
    ON governance.PolicyDecisionEvent(ProjectId, CreatedUtc DESC, PolicyDecisionEventId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_Subject_CreatedUtc
    ON governance.PolicyDecisionEvent(ProjectId, PolicyScope, SubjectType, SubjectId, CreatedUtc DESC, PolicyDecisionEventId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_Correlation_CreatedUtc
    ON governance.PolicyDecisionEvent(CorrelationId, CreatedUtc DESC, PolicyDecisionEventId DESC)
    WHERE CorrelationId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_GovernanceEventId' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_GovernanceEventId
    ON governance.PolicyDecisionEvent(GovernanceEventId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_RelatedToolRequest' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_RelatedToolRequest
    ON governance.PolicyDecisionEvent(RelatedToolRequestId)
    WHERE RelatedToolRequestId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_RelatedToolGateDecision' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_RelatedToolGateDecision
    ON governance.PolicyDecisionEvent(RelatedToolGateDecisionId)
    WHERE RelatedToolGateDecisionId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_RelatedApprovalDecision' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_RelatedApprovalDecision
    ON governance.PolicyDecisionEvent(RelatedApprovalDecisionId)
    WHERE RelatedApprovalDecisionId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyDecisionEvent_Project_Decision_CreatedUtc' AND object_id = OBJECT_ID(N'governance.PolicyDecisionEvent'))
BEGIN
    CREATE INDEX IX_PolicyDecisionEvent_Project_Decision_CreatedUtc
    ON governance.PolicyDecisionEvent(ProjectId, Decision, CreatedUtc DESC, PolicyDecisionEventId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_PolicyDecisionEvent_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_PolicyDecisionEvent_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_PolicyDecisionEvent_ValidateInsert
ON governance.PolicyDecisionEvent
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.ToolRequest request
            ON request.ToolRequestId = i.RelatedToolRequestId
        WHERE request.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52490, 'Policy decision related tool request belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.ToolGateDecision gateDecision
            ON gateDecision.ToolGateDecisionId = i.RelatedToolGateDecisionId
        WHERE gateDecision.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52491, 'Policy decision related tool gate decision belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.ApprovalDecision approvalDecision
            ON approvalDecision.ApprovalDecisionId = i.RelatedApprovalDecisionId
        WHERE approvalDecision.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52492, 'Policy decision related approval decision belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE JSON_VALUE(i.EvidenceJson, '$.grantsApproval') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.grantsExecution') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.executionPermission') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.mutatesSource') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.promotesMemory') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.startsWorkflow') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.satisfiesPolicy') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.transfersAuthority') = N'true'
    )
    BEGIN
        THROW 52493, 'Policy decision evidence must not claim approval, execution, source mutation, workflow, memory promotion, policy satisfaction, or authority transfer.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.EvidenceJson) LIKE N'%rawprompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%raw prompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%rawcompletion%'
           OR LOWER(i.EvidenceJson) LIKE N'%raw completion%'
           OR LOWER(i.EvidenceJson) LIKE N'%chainofthought%'
           OR LOWER(i.EvidenceJson) LIKE N'%chain-of-thought%'
           OR LOWER(i.EvidenceJson) LIKE N'%chain of thought%'
           OR LOWER(i.EvidenceJson) LIKE N'%scratchpad%'
           OR LOWER(i.EvidenceJson) LIKE N'%privatereasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%private reasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%hiddenreasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%hidden reasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%system prompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%developer prompt%'
    )
    BEGIN
        THROW 52494, 'Policy decision evidence must not contain hidden or private reasoning markers.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_PolicyDecisionEvent_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_PolicyDecisionEvent_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_PolicyDecisionEvent_BlockUpdateDelete
ON governance.PolicyDecisionEvent
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52495, 'Policy decision events are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicyDecisionEvent_Record
    @PolicyDecisionEventId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @PolicyScope NVARCHAR(120),
    @PolicyName NVARCHAR(160),
    @PolicyVersion INT,
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(200),
    @Decision NVARCHAR(40),
    @RequirementCode NVARCHAR(160),
    @ReasonCode NVARCHAR(160),
    @Reason NVARCHAR(500) = NULL,
    @DecidedByActorType NVARCHAR(80),
    @DecidedByActorId NVARCHAR(200),
    @RelatedToolRequestId UNIQUEIDENTIFIER = NULL,
    @RelatedToolGateDecisionId UNIQUEIDENTIFIER = NULL,
    @RelatedApprovalDecisionId UNIQUEIDENTIFIER = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @EvidenceVersion INT,
    @EvidenceJson NVARCHAR(MAX),
    @GovernanceEventPayloadJson NVARCHAR(MAX),
    @CreatedUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF @RelatedToolRequestId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ToolRequest WHERE ToolRequestId = @RelatedToolRequestId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52410, 'Related tool request does not exist for this project.', 1;
    END;

    IF @RelatedToolGateDecisionId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @RelatedToolGateDecisionId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52411, 'Related tool gate decision does not exist for this project.', 1;
    END;

    IF @RelatedApprovalDecisionId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ApprovalDecision WHERE ApprovalDecisionId = @RelatedApprovalDecisionId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52412, 'Related approval decision does not exist for this project.', 1;
    END;

    DECLARE @EffectiveCorrelationId UNIQUEIDENTIFIER = COALESCE(@CorrelationId, @PolicyDecisionEventId);
    DECLARE @EffectiveCreatedUtc DATETIMEOFFSET(7) = COALESCE(@CreatedUtc, SYSUTCDATETIME());

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM governance.PolicyDecisionEvent WHERE PolicyDecisionEventId = @PolicyDecisionEventId)
        BEGIN
            THROW 52413, 'Policy decision event already exists.', 1;
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
            PayloadJson,
            CreatedUtc
        )
        VALUES
        (
            @GovernanceEventId,
            @ProjectId,
            N'policy.decision.recorded',
            @DecidedByActorType,
            @DecidedByActorId,
            @EffectiveCorrelationId,
            @CausationId,
            N'policy_decision',
            CONVERT(NVARCHAR(36), @PolicyDecisionEventId),
            1,
            @GovernanceEventPayloadJson,
            @EffectiveCreatedUtc
        );

        INSERT INTO governance.PolicyDecisionEvent
        (
            PolicyDecisionEventId,
            ProjectId,
            GovernanceEventId,
            PolicyScope,
            PolicyName,
            PolicyVersion,
            SubjectType,
            SubjectId,
            Decision,
            RequirementCode,
            ReasonCode,
            Reason,
            DecidedByActorType,
            DecidedByActorId,
            RelatedToolRequestId,
            RelatedToolGateDecisionId,
            RelatedApprovalDecisionId,
            CorrelationId,
            CausationId,
            EvidenceVersion,
            EvidenceJson,
            CreatedUtc
        )
        VALUES
        (
            @PolicyDecisionEventId,
            @ProjectId,
            @GovernanceEventId,
            @PolicyScope,
            @PolicyName,
            @PolicyVersion,
            @SubjectType,
            @SubjectId,
            @Decision,
            @RequirementCode,
            @ReasonCode,
            @Reason,
            @DecidedByActorType,
            @DecidedByActorId,
            @RelatedToolRequestId,
            @RelatedToolGateDecisionId,
            @RelatedApprovalDecisionId,
            @EffectiveCorrelationId,
            @CausationId,
            @EvidenceVersion,
            @EvidenceJson,
            @EffectiveCreatedUtc
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    EXEC governance.usp_PolicyDecisionEvent_GetById @PolicyDecisionEventId = @PolicyDecisionEventId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicyDecisionEvent_GetById
    @PolicyDecisionEventId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        PolicyDecisionEventId,
        ProjectId,
        GovernanceEventId,
        PolicyScope,
        PolicyName,
        PolicyVersion,
        SubjectType,
        SubjectId,
        Decision,
        RequirementCode,
        ReasonCode,
        Reason,
        DecidedByActorType,
        DecidedByActorId,
        RelatedToolRequestId,
        RelatedToolGateDecisionId,
        RelatedApprovalDecisionId,
        CorrelationId,
        CausationId,
        EvidenceVersion,
        EvidenceJson,
        CreatedUtc
    FROM governance.PolicyDecisionEvent
    WHERE PolicyDecisionEventId = @PolicyDecisionEventId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicyDecisionEvent_ListForSubject
    @ProjectId UNIQUEIDENTIFIER,
    @PolicyScope NVARCHAR(120),
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(200),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        PolicyDecisionEventId,
        ProjectId,
        GovernanceEventId,
        PolicyScope,
        PolicyName,
        PolicyVersion,
        SubjectType,
        SubjectId,
        Decision,
        RequirementCode,
        ReasonCode,
        DecidedByActorType,
        DecidedByActorId,
        RelatedToolRequestId,
        RelatedToolGateDecisionId,
        RelatedApprovalDecisionId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.PolicyDecisionEvent
    WHERE ProjectId = @ProjectId
      AND PolicyScope = @PolicyScope
      AND SubjectType = @SubjectType
      AND SubjectId = @SubjectId
    ORDER BY CreatedUtc DESC, PolicyDecisionEventId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicyDecisionEvent_ListForProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        PolicyDecisionEventId,
        ProjectId,
        GovernanceEventId,
        PolicyScope,
        PolicyName,
        PolicyVersion,
        SubjectType,
        SubjectId,
        Decision,
        RequirementCode,
        ReasonCode,
        DecidedByActorType,
        DecidedByActorId,
        RelatedToolRequestId,
        RelatedToolGateDecisionId,
        RelatedApprovalDecisionId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.PolicyDecisionEvent
    WHERE ProjectId = @ProjectId
    ORDER BY CreatedUtc DESC, PolicyDecisionEventId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicyDecisionEvent_ListForCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        PolicyDecisionEventId,
        ProjectId,
        GovernanceEventId,
        PolicyScope,
        PolicyName,
        PolicyVersion,
        SubjectType,
        SubjectId,
        Decision,
        RequirementCode,
        ReasonCode,
        DecidedByActorType,
        DecidedByActorId,
        RelatedToolRequestId,
        RelatedToolGateDecisionId,
        RelatedApprovalDecisionId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.PolicyDecisionEvent
    WHERE ProjectId = @ProjectId
      AND CorrelationId = @CorrelationId
    ORDER BY CreatedUtc DESC, PolicyDecisionEventId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_PolicyDecisionEvent_Record TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicyDecisionEvent_GetById TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicyDecisionEvent_ListForSubject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicyDecisionEvent_ListForProject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicyDecisionEvent_ListForCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.PolicyDecisionEvent TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.PolicyDecisionEvent TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
BEGIN
    THROW 52300, 'governance.GovernanceEvent must exist before governance.ApprovalDecision migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NULL
BEGIN
    CREATE TABLE governance.ApprovalDecision
    (
        ApprovalDecisionId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        ApprovalScope NVARCHAR(120) NOT NULL,
        SubjectType NVARCHAR(120) NOT NULL,
        SubjectId NVARCHAR(200) NOT NULL,
        Decision NVARCHAR(40) NOT NULL,
        ReasonCode NVARCHAR(160) NOT NULL,
        Reason NVARCHAR(500) NULL,
        DecidedByActorType NVARCHAR(80) NOT NULL,
        DecidedByActorId NVARCHAR(200) NOT NULL,
        SupersedesApprovalDecisionId UNIQUEIDENTIFIER NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        EvidenceVersion INT NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ApprovalDecision_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ApprovalDecision PRIMARY KEY CLUSTERED (ApprovalDecisionId),
        CONSTRAINT FK_ApprovalDecision_GovernanceEvent FOREIGN KEY (GovernanceEventId) REFERENCES governance.GovernanceEvent(EventId),
        CONSTRAINT FK_ApprovalDecision_Supersedes FOREIGN KEY (SupersedesApprovalDecisionId) REFERENCES governance.ApprovalDecision(ApprovalDecisionId),
        CONSTRAINT CK_ApprovalDecision_Scope_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovalScope))) > 0),
        CONSTRAINT CK_ApprovalDecision_SubjectType_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectType))) > 0),
        CONSTRAINT CK_ApprovalDecision_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_ApprovalDecision_Decision_Allowed CHECK (Decision IN (N'Approved', N'Rejected', N'Revoked', N'Expired')),
        CONSTRAINT CK_ApprovalDecision_Decision_NotExecution CHECK (Decision NOT IN (N'Executed', N'AuthorizedToRun', N'ReadyToExecute', N'Applied', N'Promoted', N'Released', N'GatePassed', N'PolicySatisfied', N'AutoApproved')),
        CONSTRAINT CK_ApprovalDecision_ReasonCode_NotBlank CHECK (LEN(LTRIM(RTRIM(ReasonCode))) > 0),
        CONSTRAINT CK_ApprovalDecision_DecidedByActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(DecidedByActorType))) > 0),
        CONSTRAINT CK_ApprovalDecision_DecidedByActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(DecidedByActorId))) > 0),
        CONSTRAINT CK_ApprovalDecision_EvidenceVersion_Positive CHECK (EvidenceVersion > 0),
        CONSTRAINT CK_ApprovalDecision_EvidenceJson_IsJson CHECK (ISJSON(EvidenceJson) = 1),
        CONSTRAINT CK_ApprovalDecision_EvidenceJson_Versioned CHECK (JSON_VALUE(EvidenceJson, '$.schema') IS NOT NULL OR JSON_VALUE(EvidenceJson, '$.schemaVersion') IS NOT NULL)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalDecision_Project_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ApprovalDecision'))
BEGIN
    CREATE INDEX IX_ApprovalDecision_Project_CreatedUtc
    ON governance.ApprovalDecision(ProjectId, CreatedUtc DESC, ApprovalDecisionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalDecision_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ApprovalDecision'))
BEGIN
    CREATE INDEX IX_ApprovalDecision_Subject_CreatedUtc
    ON governance.ApprovalDecision(ProjectId, ApprovalScope, SubjectType, SubjectId, CreatedUtc DESC, ApprovalDecisionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalDecision_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ApprovalDecision'))
BEGIN
    CREATE INDEX IX_ApprovalDecision_Correlation_CreatedUtc
    ON governance.ApprovalDecision(CorrelationId, CreatedUtc DESC, ApprovalDecisionId DESC)
    WHERE CorrelationId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalDecision_GovernanceEventId' AND object_id = OBJECT_ID(N'governance.ApprovalDecision'))
BEGIN
    CREATE INDEX IX_ApprovalDecision_GovernanceEventId
    ON governance.ApprovalDecision(GovernanceEventId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalDecision_Supersedes' AND object_id = OBJECT_ID(N'governance.ApprovalDecision'))
BEGIN
    CREATE INDEX IX_ApprovalDecision_Supersedes
    ON governance.ApprovalDecision(SupersedesApprovalDecisionId)
    WHERE SupersedesApprovalDecisionId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApprovalDecision_Project_Decision_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ApprovalDecision'))
BEGIN
    CREATE INDEX IX_ApprovalDecision_Project_Decision_CreatedUtc
    ON governance.ApprovalDecision(ProjectId, Decision, CreatedUtc DESC, ApprovalDecisionId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_ApprovalDecision_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ApprovalDecision_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_ApprovalDecision_ValidateInsert
ON governance.ApprovalDecision
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE i.Decision IN (N'Revoked', N'Expired')
          AND i.SupersedesApprovalDecisionId IS NULL
    )
    BEGIN
        THROW 52390, 'Revoked and expired approval decisions must reference a prior approval decision.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE i.Decision = N'Approved'
          AND i.ApprovalScope IN (N'source_apply', N'memory_promotion', N'release_readiness', N'external_side_effect', N'destructive_operation')
          AND i.DecidedByActorType <> N'human'
    )
    BEGIN
        THROW 52391, 'Sensitive approval scopes require a human actor.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE JSON_VALUE(i.EvidenceJson, '$.grantsExecution') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.executionPermission') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.mutatesSource') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.promotesMemory') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.createsPullRequest') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.startsWorkflow') = N'true'
    )
    BEGIN
        THROW 52392, 'Approval decision evidence must not claim execution, source mutation, workflow, external, or memory promotion authority.', 1;
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
        THROW 52393, 'Approval decision evidence must not contain hidden or private reasoning markers.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_ApprovalDecision_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ApprovalDecision_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_ApprovalDecision_BlockUpdateDelete
ON governance.ApprovalDecision
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52394, 'Approval decisions are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ApprovalDecision_Record
    @ApprovalDecisionId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @ApprovalScope NVARCHAR(120),
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(200),
    @Decision NVARCHAR(40),
    @ReasonCode NVARCHAR(160),
    @Reason NVARCHAR(500) = NULL,
    @DecidedByActorType NVARCHAR(80),
    @DecidedByActorId NVARCHAR(200),
    @SupersedesApprovalDecisionId UNIQUEIDENTIFIER = NULL,
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

    DECLARE @EffectiveCorrelationId UNIQUEIDENTIFIER = COALESCE(@CorrelationId, @ApprovalDecisionId);
    DECLARE @EffectiveCreatedUtc DATETIMEOFFSET(7) = COALESCE(@CreatedUtc, SYSUTCDATETIME());

    IF @SupersedesApprovalDecisionId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ApprovalDecision WHERE ApprovalDecisionId = @SupersedesApprovalDecisionId)
    BEGIN
        THROW 52310, 'Superseded approval decision does not exist.', 1;
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM governance.ApprovalDecision WHERE ApprovalDecisionId = @ApprovalDecisionId)
        BEGIN
            THROW 52311, 'Approval decision already exists.', 1;
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
            N'approval.decision.recorded',
            @DecidedByActorType,
            @DecidedByActorId,
            @EffectiveCorrelationId,
            @CausationId,
            N'approval_decision',
            CONVERT(NVARCHAR(36), @ApprovalDecisionId),
            1,
            @GovernanceEventPayloadJson,
            @EffectiveCreatedUtc
        );

        INSERT INTO governance.ApprovalDecision
        (
            ApprovalDecisionId,
            ProjectId,
            GovernanceEventId,
            ApprovalScope,
            SubjectType,
            SubjectId,
            Decision,
            ReasonCode,
            Reason,
            DecidedByActorType,
            DecidedByActorId,
            SupersedesApprovalDecisionId,
            CorrelationId,
            CausationId,
            EvidenceVersion,
            EvidenceJson,
            CreatedUtc
        )
        VALUES
        (
            @ApprovalDecisionId,
            @ProjectId,
            @GovernanceEventId,
            @ApprovalScope,
            @SubjectType,
            @SubjectId,
            @Decision,
            @ReasonCode,
            @Reason,
            @DecidedByActorType,
            @DecidedByActorId,
            @SupersedesApprovalDecisionId,
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

    EXEC governance.usp_ApprovalDecision_GetById @ApprovalDecisionId = @ApprovalDecisionId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ApprovalDecision_GetById
    @ApprovalDecisionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ApprovalDecisionId,
        ProjectId,
        GovernanceEventId,
        ApprovalScope,
        SubjectType,
        SubjectId,
        Decision,
        ReasonCode,
        Reason,
        DecidedByActorType,
        DecidedByActorId,
        SupersedesApprovalDecisionId,
        CorrelationId,
        CausationId,
        EvidenceVersion,
        EvidenceJson,
        CreatedUtc
    FROM governance.ApprovalDecision
    WHERE ApprovalDecisionId = @ApprovalDecisionId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ApprovalDecision_ListForSubject
    @ProjectId UNIQUEIDENTIFIER,
    @ApprovalScope NVARCHAR(120),
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(200),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        ApprovalDecisionId,
        ProjectId,
        GovernanceEventId,
        ApprovalScope,
        SubjectType,
        SubjectId,
        Decision,
        ReasonCode,
        DecidedByActorType,
        DecidedByActorId,
        SupersedesApprovalDecisionId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.ApprovalDecision
    WHERE ProjectId = @ProjectId
      AND ApprovalScope = @ApprovalScope
      AND SubjectType = @SubjectType
      AND SubjectId = @SubjectId
    ORDER BY CreatedUtc DESC, ApprovalDecisionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ApprovalDecision_ListForProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        ApprovalDecisionId,
        ProjectId,
        GovernanceEventId,
        ApprovalScope,
        SubjectType,
        SubjectId,
        Decision,
        ReasonCode,
        DecidedByActorType,
        DecidedByActorId,
        SupersedesApprovalDecisionId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.ApprovalDecision
    WHERE ProjectId = @ProjectId
    ORDER BY CreatedUtc DESC, ApprovalDecisionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ApprovalDecision_ListForCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        ApprovalDecisionId,
        ProjectId,
        GovernanceEventId,
        ApprovalScope,
        SubjectType,
        SubjectId,
        Decision,
        ReasonCode,
        DecidedByActorType,
        DecidedByActorId,
        SupersedesApprovalDecisionId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.ApprovalDecision
    WHERE ProjectId = @ProjectId
      AND CorrelationId = @CorrelationId
    ORDER BY CreatedUtc DESC, ApprovalDecisionId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_ApprovalDecision_Record TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ApprovalDecision_GetById TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ApprovalDecision_ListForSubject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ApprovalDecision_ListForProject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ApprovalDecision_ListForCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.ApprovalDecision TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ApprovalDecision TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
BEGIN
    CREATE TABLE governance.GovernanceEvent
    (
        EventId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        EventType NVARCHAR(160) NOT NULL,
        ActorType NVARCHAR(80) NOT NULL,
        ActorId NVARCHAR(200) NOT NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        SubjectType NVARCHAR(120) NULL,
        SubjectId NVARCHAR(200) NULL,
        PayloadVersion INT NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_GovernanceEvent_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_GovernanceEvent PRIMARY KEY CLUSTERED (EventId),
        CONSTRAINT CK_GovernanceEvent_EventType_NotBlank CHECK (LEN(LTRIM(RTRIM(EventType))) > 0),
        CONSTRAINT CK_GovernanceEvent_ActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(ActorType))) > 0),
        CONSTRAINT CK_GovernanceEvent_ActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(ActorId))) > 0),
        CONSTRAINT CK_GovernanceEvent_PayloadVersion_Positive CHECK (PayloadVersion > 0),
        CONSTRAINT CK_GovernanceEvent_PayloadJson_IsJson CHECK (ISJSON(PayloadJson) = 1)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GovernanceEvent_Project_CreatedUtc' AND object_id = OBJECT_ID(N'governance.GovernanceEvent'))
BEGIN
    CREATE INDEX IX_GovernanceEvent_Project_CreatedUtc
    ON governance.GovernanceEvent(ProjectId, CreatedUtc, EventId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GovernanceEvent_Project_EventType_CreatedUtc' AND object_id = OBJECT_ID(N'governance.GovernanceEvent'))
BEGIN
    CREATE INDEX IX_GovernanceEvent_Project_EventType_CreatedUtc
    ON governance.GovernanceEvent(ProjectId, EventType, CreatedUtc, EventId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GovernanceEvent_CorrelationId_CreatedUtc' AND object_id = OBJECT_ID(N'governance.GovernanceEvent'))
BEGIN
    CREATE INDEX IX_GovernanceEvent_CorrelationId_CreatedUtc
    ON governance.GovernanceEvent(CorrelationId, CreatedUtc, EventId)
    WHERE CorrelationId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GovernanceEvent_CausationId' AND object_id = OBJECT_ID(N'governance.GovernanceEvent'))
BEGIN
    CREATE INDEX IX_GovernanceEvent_CausationId
    ON governance.GovernanceEvent(CausationId)
    WHERE CausationId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GovernanceEvent_Subject' AND object_id = OBJECT_ID(N'governance.GovernanceEvent'))
BEGIN
    CREATE INDEX IX_GovernanceEvent_Subject
    ON governance.GovernanceEvent(ProjectId, SubjectType, SubjectId, CreatedUtc, EventId)
    WHERE SubjectType IS NOT NULL AND SubjectId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'governance.TR_GovernanceEvent_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete
ON governance.GovernanceEvent
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52090, 'Governance events are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.AppendGovernanceEvent
    @EventId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @EventType NVARCHAR(160),
    @ActorType NVARCHAR(80),
    @ActorId NVARCHAR(200),
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @SubjectType NVARCHAR(120) = NULL,
    @SubjectId NVARCHAR(200) = NULL,
    @PayloadVersion INT,
    @PayloadJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.GovernanceEvent WHERE EventId = @EventId)
    BEGIN
        THROW 52091, 'Governance event already exists.', 1;
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
        @EventId,
        @ProjectId,
        @EventType,
        @ActorType,
        @ActorId,
        @CorrelationId,
        @CausationId,
        @SubjectType,
        @SubjectId,
        @PayloadVersion,
        @PayloadJson
    );

    SELECT TOP (1) *
    FROM governance.GovernanceEvent
    WHERE EventId = @EventId;
END;
GO

CREATE OR ALTER PROCEDURE governance.GetGovernanceEvent
    @EventId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) *
    FROM governance.GovernanceEvent
    WHERE EventId = @EventId;
END;
GO

CREATE OR ALTER PROCEDURE governance.ListGovernanceEventsForProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END) *
    FROM governance.GovernanceEvent
    WHERE ProjectId = @ProjectId
    ORDER BY CreatedUtc ASC, EventId ASC;
END;
GO

CREATE OR ALTER PROCEDURE governance.ListGovernanceEventsForCorrelation
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END) *
    FROM governance.GovernanceEvent
    WHERE CorrelationId = @CorrelationId
    ORDER BY CreatedUtc ASC, EventId ASC;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevGovernanceEventRuntimeRole' AND type = N'R')
BEGIN
    CREATE ROLE IronDevGovernanceEventRuntimeRole;
END;
GO

GRANT EXECUTE ON OBJECT::governance.AppendGovernanceEvent TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::governance.GetGovernanceEvent TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::governance.ListGovernanceEventsForProject TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::governance.ListGovernanceEventsForCorrelation TO IronDevGovernanceEventRuntimeRole;
GRANT SELECT ON OBJECT::governance.GovernanceEvent TO IronDevGovernanceEventRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::governance.GovernanceEvent TO IronDevGovernanceEventRuntimeRole;
DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
GO

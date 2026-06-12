IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
BEGIN
    THROW 52100, 'governance.GovernanceEvent must exist before governance.ToolRequest migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL
BEGIN
    CREATE TABLE governance.ToolRequest
    (
        ToolRequestId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        ToolName NVARCHAR(160) NOT NULL,
        OperationName NVARCHAR(160) NOT NULL,
        RequestedByActorType NVARCHAR(80) NOT NULL,
        RequestedByActorId NVARCHAR(200) NOT NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        Purpose NVARCHAR(500) NULL,
        RequestPayloadVersion INT NOT NULL,
        RequestPayloadJson NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(40) NOT NULL CONSTRAINT DF_ToolRequest_Status DEFAULT N'Recorded',
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ToolRequest_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CancelledUtc DATETIMEOFFSET(7) NULL,
        CancelledReason NVARCHAR(500) NULL,
        CONSTRAINT PK_ToolRequest PRIMARY KEY CLUSTERED (ToolRequestId),
        CONSTRAINT FK_ToolRequest_GovernanceEvent FOREIGN KEY (GovernanceEventId) REFERENCES governance.GovernanceEvent(EventId),
        CONSTRAINT CK_ToolRequest_ToolName_NotBlank CHECK (LEN(LTRIM(RTRIM(ToolName))) > 0),
        CONSTRAINT CK_ToolRequest_OperationName_NotBlank CHECK (LEN(LTRIM(RTRIM(OperationName))) > 0),
        CONSTRAINT CK_ToolRequest_RequestedByActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(RequestedByActorType))) > 0),
        CONSTRAINT CK_ToolRequest_RequestedByActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(RequestedByActorId))) > 0),
        CONSTRAINT CK_ToolRequest_RequestPayloadVersion_Positive CHECK (RequestPayloadVersion > 0),
        CONSTRAINT CK_ToolRequest_RequestPayloadJson_IsJson CHECK (ISJSON(RequestPayloadJson) = 1),
        CONSTRAINT CK_ToolRequest_Status_Allowed CHECK (Status IN (N'Recorded', N'Cancelled', N'Superseded')),
        CONSTRAINT CK_ToolRequest_Cancelled_State CHECK ((Status = N'Cancelled' AND CancelledUtc IS NOT NULL) OR (Status <> N'Cancelled' AND CancelledUtc IS NULL AND CancelledReason IS NULL))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolRequest_Project_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ToolRequest'))
BEGIN
    CREATE INDEX IX_ToolRequest_Project_CreatedUtc
    ON governance.ToolRequest(ProjectId, CreatedUtc DESC, ToolRequestId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolRequest_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ToolRequest'))
BEGIN
    CREATE INDEX IX_ToolRequest_Correlation_CreatedUtc
    ON governance.ToolRequest(CorrelationId, CreatedUtc DESC, ToolRequestId DESC)
    WHERE CorrelationId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolRequest_Project_Status_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ToolRequest'))
BEGIN
    CREATE INDEX IX_ToolRequest_Project_Status_CreatedUtc
    ON governance.ToolRequest(ProjectId, Status, CreatedUtc DESC, ToolRequestId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ToolRequest_GovernanceEventId' AND object_id = OBJECT_ID(N'governance.ToolRequest'))
BEGIN
    CREATE INDEX IX_ToolRequest_GovernanceEventId
    ON governance.ToolRequest(GovernanceEventId);
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolRequest_Create
    @ToolRequestId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @ToolName NVARCHAR(160),
    @OperationName NVARCHAR(160),
    @RequestedByActorType NVARCHAR(80),
    @RequestedByActorId NVARCHAR(200),
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @Purpose NVARCHAR(500) = NULL,
    @RequestPayloadVersion INT,
    @RequestPayloadJson NVARCHAR(MAX),
    @GovernanceEventPayloadJson NVARCHAR(MAX)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM governance.ToolRequest WHERE ToolRequestId = @ToolRequestId)
        BEGIN
            THROW 52101, 'Tool request already exists.', 1;
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
            N'tool.request.created',
            @RequestedByActorType,
            @RequestedByActorId,
            @CorrelationId,
            @CausationId,
            N'tool_request',
            CONVERT(NVARCHAR(36), @ToolRequestId),
            1,
            @GovernanceEventPayloadJson
        );

        INSERT INTO governance.ToolRequest
        (
            ToolRequestId,
            ProjectId,
            GovernanceEventId,
            ToolName,
            OperationName,
            RequestedByActorType,
            RequestedByActorId,
            CorrelationId,
            CausationId,
            Purpose,
            RequestPayloadVersion,
            RequestPayloadJson,
            Status
        )
        VALUES
        (
            @ToolRequestId,
            @ProjectId,
            @GovernanceEventId,
            @ToolName,
            @OperationName,
            @RequestedByActorType,
            @RequestedByActorId,
            @CorrelationId,
            @CausationId,
            @Purpose,
            @RequestPayloadVersion,
            @RequestPayloadJson,
            N'Recorded'
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT TOP (1) *
    FROM governance.ToolRequest
    WHERE ToolRequestId = @ToolRequestId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolRequest_GetById
    @ToolRequestId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) *
    FROM governance.ToolRequest
    WHERE ToolRequestId = @ToolRequestId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolRequest_ListForProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END)
        ToolRequestId,
        ProjectId,
        GovernanceEventId,
        ToolName,
        OperationName,
        RequestedByActorType,
        RequestedByActorId,
        CorrelationId,
        CausationId,
        Status,
        CreatedUtc
    FROM governance.ToolRequest
    WHERE ProjectId = @ProjectId
    ORDER BY CreatedUtc DESC, ToolRequestId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ToolRequest_ListForCorrelation
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END)
        ToolRequestId,
        ProjectId,
        GovernanceEventId,
        ToolName,
        OperationName,
        RequestedByActorType,
        RequestedByActorId,
        CorrelationId,
        CausationId,
        Status,
        CreatedUtc
    FROM governance.ToolRequest
    WHERE CorrelationId = @CorrelationId
    ORDER BY CreatedUtc DESC, ToolRequestId DESC;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevGovernanceEventRuntimeRole' AND type = N'R')
BEGIN
    CREATE ROLE IronDevGovernanceEventRuntimeRole;
END;
GO

GRANT EXECUTE ON OBJECT::governance.usp_ToolRequest_Create TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::governance.usp_ToolRequest_GetById TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::governance.usp_ToolRequest_ListForProject TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::governance.usp_ToolRequest_ListForCorrelation TO IronDevGovernanceEventRuntimeRole;
GRANT SELECT ON OBJECT::governance.ToolRequest TO IronDevGovernanceEventRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ToolRequest TO IronDevGovernanceEventRuntimeRole;
DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
GO

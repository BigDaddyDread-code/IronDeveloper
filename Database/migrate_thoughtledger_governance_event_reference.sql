IF SCHEMA_ID(N'governance') IS NULL
    EXEC(N'CREATE SCHEMA governance');
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
    THROW 52900, 'governance.GovernanceEvent must exist before ThoughtLedger governance references are migrated.', 1;
GO

IF OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference', N'U') IS NULL
BEGIN
    CREATE TABLE governance.ThoughtLedgerGovernanceEventReference
    (
        ThoughtLedgerGovernanceEventReferenceId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ThoughtLedgerEntryId NVARCHAR(200) NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        ReferenceType NVARCHAR(50) NOT NULL,
        ReasonCode NVARCHAR(100) NOT NULL,
        Reason NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        CreatedByActorType NVARCHAR(100) NOT NULL,
        CreatedByActorId NVARCHAR(200) NOT NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ThoughtLedgerGovernanceEventReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ThoughtLedgerGovernanceEventReference PRIMARY KEY CLUSTERED (ThoughtLedgerGovernanceEventReferenceId),
        CONSTRAINT FK_ThoughtLedgerGovernanceEventReference_GovernanceEvent FOREIGN KEY (GovernanceEventId) REFERENCES governance.GovernanceEvent(EventId),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_ThoughtLedgerEntryId_NotBlank CHECK (LEN(LTRIM(RTRIM(ThoughtLedgerEntryId))) > 0),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_GovernanceEventId_NotEmpty CHECK (GovernanceEventId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_ReferenceType_Allowed CHECK (ReferenceType IN (N'Observed', N'Explains', N'Supports', N'Cites', N'CausedBy', N'RelatedEvidence')),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_ReasonCode_NotBlank CHECK (LEN(LTRIM(RTRIM(ReasonCode))) > 0),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_ActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorType))) > 0),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_ActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorId))) > 0),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_MetadataVersion_Positive CHECK (MetadataVersion > 0),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_ThoughtLedgerGovernanceEventReference_MetadataJson_Versioned CHECK (JSON_VALUE(MetadataJson, '$.schema') IS NOT NULL OR JSON_VALUE(MetadataJson, '$.schemaVersion') IS NOT NULL)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ThoughtLedgerGovernanceEventReference_Project_Entry_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference'))
    CREATE INDEX IX_ThoughtLedgerGovernanceEventReference_Project_Entry_CreatedUtc
        ON governance.ThoughtLedgerGovernanceEventReference(ProjectId, ThoughtLedgerEntryId, CreatedUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ThoughtLedgerGovernanceEventReference_Project_Event_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference'))
    CREATE INDEX IX_ThoughtLedgerGovernanceEventReference_Project_Event_CreatedUtc
        ON governance.ThoughtLedgerGovernanceEventReference(ProjectId, GovernanceEventId, CreatedUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ThoughtLedgerGovernanceEventReference_Project_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference'))
    CREATE INDEX IX_ThoughtLedgerGovernanceEventReference_Project_Correlation_CreatedUtc
        ON governance.ThoughtLedgerGovernanceEventReference(ProjectId, CorrelationId, CreatedUtc DESC)
        WHERE CorrelationId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ThoughtLedgerGovernanceEventReference_GovernanceEventId' AND object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference'))
    CREATE INDEX IX_ThoughtLedgerGovernanceEventReference_GovernanceEventId
        ON governance.ThoughtLedgerGovernanceEventReference(GovernanceEventId);
GO

CREATE OR ALTER TRIGGER governance.TR_ThoughtLedgerGovernanceEventReference_ValidateInsert
ON governance.ThoughtLedgerGovernanceEventReference
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        INNER JOIN governance.GovernanceEvent ge ON ge.EventId = i.GovernanceEventId
        WHERE ge.ProjectId <> i.ProjectId
    )
        THROW 52901, 'ThoughtLedger governance reference project must match referenced governance event project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.MetadataJson) LIKE N'%rawprompt%'
           OR LOWER(i.MetadataJson) LIKE N'%raw prompt%'
           OR LOWER(i.MetadataJson) LIKE N'%rawcompletion%'
           OR LOWER(i.MetadataJson) LIKE N'%raw completion%'
           OR LOWER(i.MetadataJson) LIKE N'%chainofthought%'
           OR LOWER(i.MetadataJson) LIKE N'%chain of thought%'
           OR LOWER(i.MetadataJson) LIKE N'%scratchpad%'
           OR LOWER(i.MetadataJson) LIKE N'%private reasoning%'
           OR LOWER(i.MetadataJson) LIKE N'%hidden reasoning%'
           OR LOWER(i.MetadataJson) LIKE N'%system prompt%'
           OR LOWER(i.MetadataJson) LIKE N'%developer prompt%'
    )
        THROW 52902, 'ThoughtLedger governance reference metadata must not contain raw/private reasoning markers.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.approves'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.authorizes'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.executes'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.grantsPermission'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.grantsApproval'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.grantsExecution'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.satisfiesPolicy'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.promotesMemory'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.appliesSource'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.releases'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.overrides'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.owns'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.transfersAuthority'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.sourceApplied'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.memoryPromoted'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.workflowStarted'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.policySatisfied'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.releaseApproved'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.createsA2aHandoff'), N'false')) IN (N'true', N'1', N'yes')
           OR LOWER(COALESCE(JSON_VALUE(i.MetadataJson, '$.createsDogfoodReceipt'), N'false')) IN (N'true', N'1', N'yes')
    )
        THROW 52903, 'ThoughtLedger governance reference metadata must not claim authority, execution, approval, policy satisfaction, source apply, memory promotion, workflow progress, A2A, or dogfood receipt creation.', 1;
END;
GO

CREATE OR ALTER TRIGGER governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete
ON governance.ThoughtLedgerGovernanceEventReference
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52904, 'ThoughtLedger governance event references are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_GetById
    @ThoughtLedgerGovernanceEventReferenceId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ThoughtLedgerGovernanceEventReferenceId,
        ProjectId,
        ThoughtLedgerEntryId,
        GovernanceEventId,
        ReferenceType,
        ReasonCode,
        Reason,
        CorrelationId,
        CausationId,
        CreatedByActorType,
        CreatedByActorId,
        MetadataVersion,
        MetadataJson,
        CreatedUtc
    FROM governance.ThoughtLedgerGovernanceEventReference
    WHERE ThoughtLedgerGovernanceEventReferenceId = @ThoughtLedgerGovernanceEventReferenceId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_Record
    @ThoughtLedgerGovernanceEventReferenceId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ThoughtLedgerEntryId NVARCHAR(200),
    @GovernanceEventId UNIQUEIDENTIFIER,
    @ReferenceType NVARCHAR(50),
    @ReasonCode NVARCHAR(100),
    @Reason NVARCHAR(1000) = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @CreatedByActorType NVARCHAR(100),
    @CreatedByActorId NVARCHAR(200),
    @MetadataVersion INT,
    @MetadataJson NVARCHAR(MAX),
    @CreatedUtc DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @GovernanceEventProjectId UNIQUEIDENTIFIER;
    DECLARE @GovernanceEventCorrelationId UNIQUEIDENTIFIER;

    SELECT
        @GovernanceEventProjectId = ProjectId,
        @GovernanceEventCorrelationId = CorrelationId
    FROM governance.GovernanceEvent
    WHERE EventId = @GovernanceEventId;

    IF @GovernanceEventProjectId IS NULL
        THROW 52905, 'Referenced governance event does not exist.', 1;

    IF @GovernanceEventProjectId <> @ProjectId
        THROW 52906, 'Referenced governance event belongs to a different project.', 1;

    IF EXISTS (SELECT 1 FROM governance.ThoughtLedgerGovernanceEventReference WHERE ThoughtLedgerGovernanceEventReferenceId = @ThoughtLedgerGovernanceEventReferenceId)
        THROW 52907, 'ThoughtLedger governance event reference already exists.', 1;

    INSERT INTO governance.ThoughtLedgerGovernanceEventReference
    (
        ThoughtLedgerGovernanceEventReferenceId,
        ProjectId,
        ThoughtLedgerEntryId,
        GovernanceEventId,
        ReferenceType,
        ReasonCode,
        Reason,
        CorrelationId,
        CausationId,
        CreatedByActorType,
        CreatedByActorId,
        MetadataVersion,
        MetadataJson,
        CreatedUtc
    )
    VALUES
    (
        @ThoughtLedgerGovernanceEventReferenceId,
        @ProjectId,
        LTRIM(RTRIM(@ThoughtLedgerEntryId)),
        @GovernanceEventId,
        LTRIM(RTRIM(@ReferenceType)),
        LTRIM(RTRIM(@ReasonCode)),
        NULLIF(LTRIM(RTRIM(COALESCE(@Reason, N''))), N''),
        COALESCE(@CorrelationId, @GovernanceEventCorrelationId, @ThoughtLedgerGovernanceEventReferenceId),
        @CausationId,
        LTRIM(RTRIM(@CreatedByActorType)),
        LTRIM(RTRIM(@CreatedByActorId)),
        @MetadataVersion,
        LTRIM(RTRIM(@MetadataJson)),
        COALESCE(@CreatedUtc, SYSUTCDATETIME())
    );

    EXEC governance.usp_ThoughtLedgerGovernanceEventReference_GetById @ThoughtLedgerGovernanceEventReferenceId = @ThoughtLedgerGovernanceEventReferenceId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry
    @ProjectId UNIQUEIDENTIFIER,
    @ThoughtLedgerEntryId NVARCHAR(200),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        ThoughtLedgerGovernanceEventReferenceId,
        ProjectId,
        ThoughtLedgerEntryId,
        GovernanceEventId,
        ReferenceType,
        ReasonCode,
        CorrelationId,
        CausationId,
        CreatedByActorType,
        CreatedByActorId,
        CreatedUtc
    FROM governance.ThoughtLedgerGovernanceEventReference
    WHERE ProjectId = @ProjectId
      AND ThoughtLedgerEntryId = LTRIM(RTRIM(@ThoughtLedgerEntryId))
    ORDER BY CreatedUtc DESC, ThoughtLedgerGovernanceEventReferenceId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent
    @ProjectId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        ThoughtLedgerGovernanceEventReferenceId,
        ProjectId,
        ThoughtLedgerEntryId,
        GovernanceEventId,
        ReferenceType,
        ReasonCode,
        CorrelationId,
        CausationId,
        CreatedByActorType,
        CreatedByActorId,
        CreatedUtc
    FROM governance.ThoughtLedgerGovernanceEventReference
    WHERE ProjectId = @ProjectId
      AND GovernanceEventId = @GovernanceEventId
    ORDER BY CreatedUtc DESC, ThoughtLedgerGovernanceEventReferenceId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        ThoughtLedgerGovernanceEventReferenceId,
        ProjectId,
        ThoughtLedgerEntryId,
        GovernanceEventId,
        ReferenceType,
        ReasonCode,
        CorrelationId,
        CausationId,
        CreatedByActorType,
        CreatedByActorId,
        CreatedUtc
    FROM governance.ThoughtLedgerGovernanceEventReference
    WHERE ProjectId = @ProjectId
      AND CorrelationId = @CorrelationId
    ORDER BY CreatedUtc DESC, ThoughtLedgerGovernanceEventReferenceId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_ThoughtLedgerGovernanceEventReference_Record TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ThoughtLedgerGovernanceEventReference_GetById TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ThoughtLedgerGovernanceEventReference TO IronDevGovernanceEventRuntimeRole;
END;
GO
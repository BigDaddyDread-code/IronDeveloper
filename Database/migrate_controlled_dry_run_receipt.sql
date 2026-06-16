IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.ControlledDryRunReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE governance.ControlledDryRunReceipt
    (
        DryRunExecutionAuditId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ControlledDryRunRequestId UNIQUEIDENTIFIER NOT NULL,
        PolicySatisfactionId UNIQUEIDENTIFIER NOT NULL,
        PolicySatisfactionHash NVARCHAR(256) NOT NULL,
        SubjectKind NVARCHAR(128) NOT NULL,
        SubjectId NVARCHAR(256) NOT NULL,
        SubjectHash NVARCHAR(256) NOT NULL,
        WorkspaceId NVARCHAR(256) NOT NULL,
        WorkspaceKind NVARCHAR(128) NOT NULL,
        WorkspaceBoundaryHash NVARCHAR(256) NOT NULL,
        SourceSnapshotReference NVARCHAR(512) NOT NULL,
        ValidationPlanId NVARCHAR(256) NOT NULL,
        ValidationPlanHash NVARCHAR(256) NOT NULL,
        StartedAtUtc DATETIMEOFFSET(7) NOT NULL,
        CompletedAtUtc DATETIMEOFFSET(7) NOT NULL,
        DryRunCompleted BIT NOT NULL,
        DryRunSucceeded BIT NOT NULL,
        ExecutionReportHash NVARCHAR(256) NOT NULL,
        AuditHash NVARCHAR(256) NOT NULL,
        CommandAuditsJson NVARCHAR(MAX) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ControlledDryRunReceipt_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_ControlledDryRunReceipt PRIMARY KEY CLUSTERED (DryRunExecutionAuditId),
        CONSTRAINT CK_ControlledDryRunReceipt_PolicySatisfactionHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicySatisfactionHash))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_SubjectKind_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectKind))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_SubjectHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectHash))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_WorkspaceId_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceId))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_WorkspaceKind_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceKind))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_WorkspaceBoundaryHash_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceBoundaryHash))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_SourceSnapshotReference_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceSnapshotReference))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_ValidationPlanId_NotBlank CHECK (LEN(LTRIM(RTRIM(ValidationPlanId))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_ValidationPlanHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ValidationPlanHash))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_CompletedAfterStarted CHECK (CompletedAtUtc >= StartedAtUtc),
        CONSTRAINT CK_ControlledDryRunReceipt_ExecutionReportHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ExecutionReportHash))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_AuditHash_NotBlank CHECK (LEN(LTRIM(RTRIM(AuditHash))) > 0),
        CONSTRAINT CK_ControlledDryRunReceipt_CommandAuditsJson_IsJson CHECK (ISJSON(CommandAuditsJson) = 1),
        CONSTRAINT CK_ControlledDryRunReceipt_CommandAuditsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(CommandAuditsJson))) > 2),
        CONSTRAINT CK_ControlledDryRunReceipt_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_ControlledDryRunReceipt_EvidenceReferencesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_ControlledDryRunReceipt_BoundaryMaximsJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_ControlledDryRunReceipt_BoundaryMaximsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_ControlledDryRunReceipt_BoundaryText_NotBlank CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ControlledDryRunReceipt_Project_AuditHash' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE UNIQUE INDEX UX_ControlledDryRunReceipt_Project_AuditHash
    ON governance.ControlledDryRunReceipt(ProjectId, AuditHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_CompletedAt' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_CompletedAt
    ON governance.ControlledDryRunReceipt(ProjectId, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_Request' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_Request
    ON governance.ControlledDryRunReceipt(ProjectId, ControlledDryRunRequestId, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_PolicySatisfaction' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_PolicySatisfaction
    ON governance.ControlledDryRunReceipt(ProjectId, PolicySatisfactionId, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_Subject' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_Subject
    ON governance.ControlledDryRunReceipt(ProjectId, SubjectKind, SubjectId, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_SubjectHash' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_SubjectHash
    ON governance.ControlledDryRunReceipt(ProjectId, SubjectHash, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_Workspace' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_Workspace
    ON governance.ControlledDryRunReceipt(ProjectId, WorkspaceId, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_WorkspaceBoundaryHash' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_WorkspaceBoundaryHash
    ON governance.ControlledDryRunReceipt(ProjectId, WorkspaceBoundaryHash, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_ValidationPlanHash' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_ValidationPlanHash
    ON governance.ControlledDryRunReceipt(ProjectId, ValidationPlanHash, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_ExecutionReportHash' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_ExecutionReportHash
    ON governance.ControlledDryRunReceipt(ProjectId, ExecutionReportHash, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ControlledDryRunReceipt_Project_Succeeded' AND object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt'))
BEGIN
    CREATE INDEX IX_ControlledDryRunReceipt_Project_Succeeded
    ON governance.ControlledDryRunReceipt(ProjectId, DryRunSucceeded, CompletedAtUtc DESC, DryRunExecutionAuditId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_ControlledDryRunReceipt_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ControlledDryRunReceipt_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_ControlledDryRunReceipt_ValidateInsert
ON governance.ControlledDryRunReceipt
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.PolicySatisfactionHash, N' ', i.SubjectKind, N' ', i.SubjectId, N' ', i.SubjectHash, N' ',
            i.WorkspaceId, N' ', i.WorkspaceKind, N' ', i.WorkspaceBoundaryHash, N' ', i.SourceSnapshotReference, N' ',
            i.ValidationPlanId, N' ', i.ValidationPlanHash, N' ', i.ExecutionReportHash, N' ', i.AuditHash, N' ',
            i.CommandAuditsJson, N' ', i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson, N' ', i.BoundaryText
        )) AS TextToCheck) c
        WHERE c.TextToCheck LIKE N'%rawprompt%'
           OR c.TextToCheck LIKE N'%raw prompt%'
           OR c.TextToCheck LIKE N'%rawcompletion%'
           OR c.TextToCheck LIKE N'%raw completion%'
           OR c.TextToCheck LIKE N'%rawtooloutput%'
           OR c.TextToCheck LIKE N'%raw tool output%'
           OR c.TextToCheck LIKE N'%chainofthought%'
           OR c.TextToCheck LIKE N'%chain-of-thought%'
           OR c.TextToCheck LIKE N'%chain of thought%'
           OR c.TextToCheck LIKE N'%scratchpad%'
           OR c.TextToCheck LIKE N'%private reasoning%'
           OR c.TextToCheck LIKE N'%hidden reasoning%'
           OR c.TextToCheck LIKE N'%system prompt%'
           OR c.TextToCheck LIKE N'%developer prompt%'
           OR c.TextToCheck LIKE N'%password%'
           OR c.TextToCheck LIKE N'%api_key%'
           OR c.TextToCheck LIKE N'%secret%'
           OR c.TextToCheck LIKE N'%private key%'
           OR c.TextToCheck LIKE N'%bearer%'
           OR c.TextToCheck LIKE N'%creates patch artifact%'
           OR c.TextToCheck LIKE N'%patch artifact created%'
           OR c.TextToCheck LIKE N'%applies source%'
           OR c.TextToCheck LIKE N'%source applied%'
           OR c.TextToCheck LIKE N'%continues workflow%'
           OR c.TextToCheck LIKE N'%workflow continued%'
           OR c.TextToCheck LIKE N'%approves release%'
           OR c.TextToCheck LIKE N'%release approved%'
           OR c.TextToCheck LIKE N'%release ready%'
           OR c.TextToCheck LIKE N'%rollback executed%'
    )
    BEGIN
        THROW 52840, 'Controlled dry-run receipts must not contain raw/private material or action-authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_ControlledDryRunReceipt_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ControlledDryRunReceipt_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_ControlledDryRunReceipt_BlockUpdateDelete
ON governance.ControlledDryRunReceipt
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52841, 'Controlled dry-run receipts are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ControlledDryRunReceipt_Save
    @DryRunExecutionAuditId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ControlledDryRunRequestId UNIQUEIDENTIFIER,
    @PolicySatisfactionId UNIQUEIDENTIFIER,
    @PolicySatisfactionHash NVARCHAR(256),
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(256),
    @SubjectHash NVARCHAR(256),
    @WorkspaceId NVARCHAR(256),
    @WorkspaceKind NVARCHAR(128),
    @WorkspaceBoundaryHash NVARCHAR(256),
    @SourceSnapshotReference NVARCHAR(512),
    @ValidationPlanId NVARCHAR(256),
    @ValidationPlanHash NVARCHAR(256),
    @StartedAtUtc DATETIMEOFFSET(7),
    @CompletedAtUtc DATETIMEOFFSET(7),
    @DryRunCompleted BIT,
    @DryRunSucceeded BIT,
    @ExecutionReportHash NVARCHAR(256),
    @AuditHash NVARCHAR(256),
    @CommandAuditsJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX),
    @CreatedAtUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.ControlledDryRunReceipt WHERE DryRunExecutionAuditId = @DryRunExecutionAuditId)
    BEGIN
        THROW 52842, 'Controlled dry-run receipt already exists.', 1;
    END;

    IF EXISTS (SELECT 1 FROM governance.ControlledDryRunReceipt WHERE ProjectId = @ProjectId AND AuditHash = LTRIM(RTRIM(@AuditHash)))
    BEGIN
        THROW 52843, 'Controlled dry-run receipt audit hash already exists for project.', 1;
    END;

    INSERT INTO governance.ControlledDryRunReceipt
    (
        DryRunExecutionAuditId,
        ProjectId,
        ControlledDryRunRequestId,
        PolicySatisfactionId,
        PolicySatisfactionHash,
        SubjectKind,
        SubjectId,
        SubjectHash,
        WorkspaceId,
        WorkspaceKind,
        WorkspaceBoundaryHash,
        SourceSnapshotReference,
        ValidationPlanId,
        ValidationPlanHash,
        StartedAtUtc,
        CompletedAtUtc,
        DryRunCompleted,
        DryRunSucceeded,
        ExecutionReportHash,
        AuditHash,
        CommandAuditsJson,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        BoundaryText,
        CreatedAtUtc
    )
    VALUES
    (
        @DryRunExecutionAuditId,
        @ProjectId,
        @ControlledDryRunRequestId,
        @PolicySatisfactionId,
        LTRIM(RTRIM(@PolicySatisfactionHash)),
        LTRIM(RTRIM(@SubjectKind)),
        LTRIM(RTRIM(@SubjectId)),
        LTRIM(RTRIM(@SubjectHash)),
        LTRIM(RTRIM(@WorkspaceId)),
        LTRIM(RTRIM(@WorkspaceKind)),
        LTRIM(RTRIM(@WorkspaceBoundaryHash)),
        LTRIM(RTRIM(@SourceSnapshotReference)),
        LTRIM(RTRIM(@ValidationPlanId)),
        LTRIM(RTRIM(@ValidationPlanHash)),
        @StartedAtUtc,
        @CompletedAtUtc,
        @DryRunCompleted,
        @DryRunSucceeded,
        LTRIM(RTRIM(@ExecutionReportHash)),
        LTRIM(RTRIM(@AuditHash)),
        @CommandAuditsJson,
        @EvidenceReferencesJson,
        @BoundaryMaximsJson,
        LTRIM(RTRIM(@BoundaryText)),
        COALESCE(@CreatedAtUtc, SYSUTCDATETIME())
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ControlledDryRunReceipt_Get
    @ProjectId UNIQUEIDENTIFIER,
    @DryRunExecutionAuditId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM governance.ControlledDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND DryRunExecutionAuditId = @DryRunExecutionAuditId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ControlledDryRunReceipt_ListByRequest
    @ProjectId UNIQUEIDENTIFIER,
    @ControlledDryRunRequestId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.ControlledDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND ControlledDryRunRequestId = @ControlledDryRunRequestId
    ORDER BY CompletedAtUtc DESC, DryRunExecutionAuditId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction
    @ProjectId UNIQUEIDENTIFIER,
    @PolicySatisfactionId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.ControlledDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND PolicySatisfactionId = @PolicySatisfactionId
    ORDER BY CompletedAtUtc DESC, DryRunExecutionAuditId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ControlledDryRunReceipt_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.ControlledDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND SubjectKind = LTRIM(RTRIM(@SubjectKind))
      AND SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY CompletedAtUtc DESC, DryRunExecutionAuditId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ControlledDryRunReceipt_ListByAuditHash
    @ProjectId UNIQUEIDENTIFIER,
    @AuditHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.ControlledDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND AuditHash = LTRIM(RTRIM(@AuditHash))
    ORDER BY CompletedAtUtc DESC, DryRunExecutionAuditId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_ControlledDryRunReceipt_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ControlledDryRunReceipt_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ControlledDryRunReceipt_ListByRequest TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ControlledDryRunReceipt_ListBySubject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ControlledDryRunReceipt_ListByAuditHash TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.ControlledDryRunReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ControlledDryRunReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO
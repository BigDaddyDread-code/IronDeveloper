IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.PatchArtifact', N'U') IS NULL
BEGIN
    CREATE TABLE governance.PatchArtifact
    (
        PatchArtifactId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        PatchArtifactKind NVARCHAR(128) NOT NULL,
        ControlledDryRunRequestId UNIQUEIDENTIFIER NOT NULL,
        DryRunExecutionAuditId UNIQUEIDENTIFIER NOT NULL,
        DryRunAuditHash NVARCHAR(256) NOT NULL,
        DryRunReceiptHash NVARCHAR(256) NOT NULL,
        PolicySatisfactionId UNIQUEIDENTIFIER NOT NULL,
        PolicySatisfactionHash NVARCHAR(256) NOT NULL,
        SubjectKind NVARCHAR(128) NOT NULL,
        SubjectId NVARCHAR(256) NOT NULL,
        SubjectHash NVARCHAR(256) NOT NULL,
        SourceSnapshotReference NVARCHAR(512) NOT NULL,
        SourceBaselineHash NVARCHAR(256) NOT NULL,
        WorkspaceBoundaryHash NVARCHAR(256) NOT NULL,
        ValidationPlanId NVARCHAR(256) NOT NULL,
        ValidationPlanHash NVARCHAR(256) NOT NULL,
        PatchHash NVARCHAR(256) NOT NULL,
        ChangeSetHash NVARCHAR(256) NOT NULL,
        FileChangesJson NVARCHAR(MAX) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ExpiresAtUtc DATETIMEOFFSET(7) NULL,
        StoredAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_PatchArtifact_StoredAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_PatchArtifact PRIMARY KEY CLUSTERED (PatchArtifactId),
        CONSTRAINT CK_PatchArtifact_Kind_NotBlank CHECK (LEN(LTRIM(RTRIM(PatchArtifactKind))) > 0),
        CONSTRAINT CK_PatchArtifact_DryRunAuditHash_NotBlank CHECK (LEN(LTRIM(RTRIM(DryRunAuditHash))) > 0),
        CONSTRAINT CK_PatchArtifact_DryRunReceiptHash_NotBlank CHECK (LEN(LTRIM(RTRIM(DryRunReceiptHash))) > 0),
        CONSTRAINT CK_PatchArtifact_PolicySatisfactionHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicySatisfactionHash))) > 0),
        CONSTRAINT CK_PatchArtifact_SubjectKind_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectKind))) > 0),
        CONSTRAINT CK_PatchArtifact_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_PatchArtifact_SubjectHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectHash))) > 0),
        CONSTRAINT CK_PatchArtifact_SourceSnapshotReference_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceSnapshotReference))) > 0),
        CONSTRAINT CK_PatchArtifact_SourceBaselineHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceBaselineHash))) > 0),
        CONSTRAINT CK_PatchArtifact_WorkspaceBoundaryHash_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceBoundaryHash))) > 0),
        CONSTRAINT CK_PatchArtifact_ValidationPlanId_NotBlank CHECK (LEN(LTRIM(RTRIM(ValidationPlanId))) > 0),
        CONSTRAINT CK_PatchArtifact_ValidationPlanHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ValidationPlanHash))) > 0),
        CONSTRAINT CK_PatchArtifact_PatchHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PatchHash))) > 0),
        CONSTRAINT CK_PatchArtifact_ChangeSetHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ChangeSetHash))) > 0),
        CONSTRAINT CK_PatchArtifact_FileChangesJson_IsJson CHECK (ISJSON(FileChangesJson) = 1),
        CONSTRAINT CK_PatchArtifact_FileChangesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(FileChangesJson))) > 2),
        CONSTRAINT CK_PatchArtifact_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_PatchArtifact_EvidenceReferencesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_PatchArtifact_BoundaryMaximsJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_PatchArtifact_BoundaryMaximsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_PatchArtifact_BoundaryText_NotBlank CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0),
        CONSTRAINT CK_PatchArtifact_ExpiresAfterCreated CHECK (ExpiresAtUtc IS NULL OR ExpiresAtUtc > CreatedAtUtc)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PatchArtifact_Project_PatchHash' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE UNIQUE INDEX UX_PatchArtifact_Project_PatchHash
    ON governance.PatchArtifact(ProjectId, PatchHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_Id' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_Id
    ON governance.PatchArtifact(ProjectId, PatchArtifactId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_ChangeSetHash' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_ChangeSetHash
    ON governance.PatchArtifact(ProjectId, ChangeSetHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_DryRunReceiptHash' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_DryRunReceiptHash
    ON governance.PatchArtifact(ProjectId, DryRunReceiptHash, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_DryRunAuditHash' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_DryRunAuditHash
    ON governance.PatchArtifact(ProjectId, DryRunAuditHash, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_ControlledDryRunRequest' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_ControlledDryRunRequest
    ON governance.PatchArtifact(ProjectId, ControlledDryRunRequestId, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_PolicySatisfaction' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_PolicySatisfaction
    ON governance.PatchArtifact(ProjectId, PolicySatisfactionId, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_Subject' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_Subject
    ON governance.PatchArtifact(ProjectId, SubjectKind, SubjectId, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_SubjectHash' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_SubjectHash
    ON governance.PatchArtifact(ProjectId, SubjectHash, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_SourceBaselineHash' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_SourceBaselineHash
    ON governance.PatchArtifact(ProjectId, SourceBaselineHash, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_CreatedAt' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_CreatedAt
    ON governance.PatchArtifact(ProjectId, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PatchArtifact_Project_Kind_CreatedAt' AND object_id = OBJECT_ID(N'governance.PatchArtifact'))
BEGIN
    CREATE INDEX IX_PatchArtifact_Project_Kind_CreatedAt
    ON governance.PatchArtifact(ProjectId, PatchArtifactKind, CreatedAtUtc DESC, PatchArtifactId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_PatchArtifact_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_PatchArtifact_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_PatchArtifact_ValidateInsert
ON governance.PatchArtifact
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.PatchArtifactKind, N' ', i.DryRunAuditHash, N' ', i.DryRunReceiptHash, N' ',
            i.PolicySatisfactionHash, N' ', i.SubjectKind, N' ', i.SubjectId, N' ', i.SubjectHash, N' ',
            i.SourceSnapshotReference, N' ', i.SourceBaselineHash, N' ', i.WorkspaceBoundaryHash, N' ',
            i.ValidationPlanId, N' ', i.ValidationPlanHash, N' ', i.PatchHash, N' ', i.ChangeSetHash, N' ',
            i.FileChangesJson, N' ', i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson, N' ', i.BoundaryText
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
           OR c.TextToCheck LIKE N'%source applied%'
           OR c.TextToCheck LIKE N'%applies source%'
           OR c.TextToCheck LIKE N'%rollback executed%'
           OR c.TextToCheck LIKE N'%continues workflow%'
           OR c.TextToCheck LIKE N'%workflow continued%'
           OR c.TextToCheck LIKE N'%approves release%'
           OR c.TextToCheck LIKE N'%release approved%'
           OR c.TextToCheck LIKE N'%release ready%'
    )
    BEGIN
        THROW 52880, 'Patch artifacts must not contain raw/private material or action-authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_PatchArtifact_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_PatchArtifact_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_PatchArtifact_BlockUpdateDelete
ON governance.PatchArtifact
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52881, 'Patch artifacts are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_Save
    @PatchArtifactId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @PatchArtifactKind NVARCHAR(128),
    @ControlledDryRunRequestId UNIQUEIDENTIFIER,
    @DryRunExecutionAuditId UNIQUEIDENTIFIER,
    @DryRunAuditHash NVARCHAR(256),
    @DryRunReceiptHash NVARCHAR(256),
    @PolicySatisfactionId UNIQUEIDENTIFIER,
    @PolicySatisfactionHash NVARCHAR(256),
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(256),
    @SubjectHash NVARCHAR(256),
    @SourceSnapshotReference NVARCHAR(512),
    @SourceBaselineHash NVARCHAR(256),
    @WorkspaceBoundaryHash NVARCHAR(256),
    @ValidationPlanId NVARCHAR(256),
    @ValidationPlanHash NVARCHAR(256),
    @PatchHash NVARCHAR(256),
    @ChangeSetHash NVARCHAR(256),
    @FileChangesJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX),
    @CreatedAtUtc DATETIMEOFFSET(7),
    @ExpiresAtUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.PatchArtifact WHERE PatchArtifactId = @PatchArtifactId)
    BEGIN
        THROW 52882, 'Patch artifact already exists.', 1;
    END;

    IF EXISTS (SELECT 1 FROM governance.PatchArtifact WHERE ProjectId = @ProjectId AND PatchHash = LTRIM(RTRIM(@PatchHash)))
    BEGIN
        THROW 52883, 'Patch artifact patch hash already exists for project.', 1;
    END;

    INSERT INTO governance.PatchArtifact
    (
        PatchArtifactId,
        ProjectId,
        PatchArtifactKind,
        ControlledDryRunRequestId,
        DryRunExecutionAuditId,
        DryRunAuditHash,
        DryRunReceiptHash,
        PolicySatisfactionId,
        PolicySatisfactionHash,
        SubjectKind,
        SubjectId,
        SubjectHash,
        SourceSnapshotReference,
        SourceBaselineHash,
        WorkspaceBoundaryHash,
        ValidationPlanId,
        ValidationPlanHash,
        PatchHash,
        ChangeSetHash,
        FileChangesJson,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        BoundaryText,
        CreatedAtUtc,
        ExpiresAtUtc
    )
    VALUES
    (
        @PatchArtifactId,
        @ProjectId,
        LTRIM(RTRIM(@PatchArtifactKind)),
        @ControlledDryRunRequestId,
        @DryRunExecutionAuditId,
        LTRIM(RTRIM(@DryRunAuditHash)),
        LTRIM(RTRIM(@DryRunReceiptHash)),
        @PolicySatisfactionId,
        LTRIM(RTRIM(@PolicySatisfactionHash)),
        LTRIM(RTRIM(@SubjectKind)),
        LTRIM(RTRIM(@SubjectId)),
        LTRIM(RTRIM(@SubjectHash)),
        LTRIM(RTRIM(@SourceSnapshotReference)),
        LTRIM(RTRIM(@SourceBaselineHash)),
        LTRIM(RTRIM(@WorkspaceBoundaryHash)),
        LTRIM(RTRIM(@ValidationPlanId)),
        LTRIM(RTRIM(@ValidationPlanHash)),
        LTRIM(RTRIM(@PatchHash)),
        LTRIM(RTRIM(@ChangeSetHash)),
        @FileChangesJson,
        @EvidenceReferencesJson,
        @BoundaryMaximsJson,
        LTRIM(RTRIM(@BoundaryText)),
        @CreatedAtUtc,
        @ExpiresAtUtc
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_Get
    @ProjectId UNIQUEIDENTIFIER,
    @PatchArtifactId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND PatchArtifactId = @PatchArtifactId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_ListByDryRunReceiptHash
    @ProjectId UNIQUEIDENTIFIER,
    @DryRunReceiptHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND DryRunReceiptHash = LTRIM(RTRIM(@DryRunReceiptHash))
    ORDER BY CreatedAtUtc DESC, PatchArtifactId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_ListByDryRunAuditHash
    @ProjectId UNIQUEIDENTIFIER,
    @DryRunAuditHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND DryRunAuditHash = LTRIM(RTRIM(@DryRunAuditHash))
    ORDER BY CreatedAtUtc DESC, PatchArtifactId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_ListByControlledDryRunRequest
    @ProjectId UNIQUEIDENTIFIER,
    @ControlledDryRunRequestId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND ControlledDryRunRequestId = @ControlledDryRunRequestId
    ORDER BY CreatedAtUtc DESC, PatchArtifactId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND SubjectKind = LTRIM(RTRIM(@SubjectKind))
      AND SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY CreatedAtUtc DESC, PatchArtifactId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_ListByPatchHash
    @ProjectId UNIQUEIDENTIFIER,
    @PatchHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND PatchHash = LTRIM(RTRIM(@PatchHash))
    ORDER BY CreatedAtUtc DESC, PatchArtifactId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PatchArtifact_ListBySourceBaselineHash
    @ProjectId UNIQUEIDENTIFIER,
    @SourceBaselineHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.PatchArtifact
    WHERE ProjectId = @ProjectId
      AND SourceBaselineHash = LTRIM(RTRIM(@SourceBaselineHash))
    ORDER BY CreatedAtUtc DESC, PatchArtifactId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_ListByDryRunReceiptHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_ListByDryRunAuditHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_ListByControlledDryRunRequest TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_ListBySubject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_ListByPatchHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PatchArtifact_ListBySourceBaselineHash TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.PatchArtifact TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.PatchArtifact TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

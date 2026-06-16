IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.RollbackSupportReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE governance.RollbackSupportReceipt
    (
        RollbackSupportReceiptId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        RollbackPlanId UNIQUEIDENTIFIER NOT NULL,
        RollbackPlanHash NVARCHAR(256) NOT NULL,
        RollbackGateSatisfied BIT NOT NULL,
        RollbackGateEvaluationHash NVARCHAR(256) NOT NULL,
        PatchArtifactId UNIQUEIDENTIFIER NOT NULL,
        PatchHash NVARCHAR(256) NOT NULL,
        ChangeSetHash NVARCHAR(256) NOT NULL,
        ControlledDryRunRequestId UNIQUEIDENTIFIER NOT NULL,
        DryRunExecutionAuditId UNIQUEIDENTIFIER NOT NULL,
        DryRunAuditHash NVARCHAR(256) NOT NULL,
        DryRunReceiptHash NVARCHAR(256) NOT NULL,
        PolicySatisfactionId UNIQUEIDENTIFIER NOT NULL,
        PolicySatisfactionHash NVARCHAR(256) NOT NULL,
        SubjectKind NVARCHAR(128) NOT NULL,
        SubjectId NVARCHAR(300) NOT NULL,
        SubjectHash NVARCHAR(256) NOT NULL,
        SourceSnapshotReference NVARCHAR(512) NOT NULL,
        SourceBaselineHash NVARCHAR(256) NOT NULL,
        WorkspaceBoundaryHash NVARCHAR(256) NOT NULL,
        ExpectedBranch NVARCHAR(300) NOT NULL,
        ExpectedCleanWorktreeHash NVARCHAR(256) NOT NULL,
        RollbackSupportReceiptHash NVARCHAR(256) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ExpiresAtUtc DATETIMEOFFSET(7) NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        StoredAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_RollbackSupportReceipt_StoredAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_RollbackSupportReceipt PRIMARY KEY CLUSTERED (RollbackSupportReceiptId),
        CONSTRAINT CK_RollbackSupportReceipt_RollbackPlanHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackPlanHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_GateSatisfied CHECK (RollbackGateSatisfied = 1),
        CONSTRAINT CK_RollbackSupportReceipt_RollbackGateEvaluationHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackGateEvaluationHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_PatchHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PatchHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_ChangeSetHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ChangeSetHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_DryRunAuditHash_NotBlank CHECK (LEN(LTRIM(RTRIM(DryRunAuditHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_DryRunReceiptHash_NotBlank CHECK (LEN(LTRIM(RTRIM(DryRunReceiptHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_PolicySatisfactionHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicySatisfactionHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_SubjectKind_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectKind))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_SubjectHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_SourceSnapshotReference_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceSnapshotReference))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_SourceBaselineHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceBaselineHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_WorkspaceBoundaryHash_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceBoundaryHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_ExpectedBranch_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedBranch))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_ExpectedCleanWorktreeHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedCleanWorktreeHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_Hash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackSupportReceiptHash))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_RollbackSupportReceipt_EvidenceReferencesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_RollbackSupportReceipt_BoundaryMaximsJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_RollbackSupportReceipt_BoundaryMaximsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_RollbackSupportReceipt_BoundaryText_NotBlank CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0),
        CONSTRAINT CK_RollbackSupportReceipt_ExpiresAfterCreated CHECK (ExpiresAtUtc IS NULL OR ExpiresAtUtc > CreatedAtUtc)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RollbackSupportReceipt_Project_Hash' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE UNIQUE INDEX UX_RollbackSupportReceipt_Project_Hash
    ON governance.RollbackSupportReceipt(ProjectId, RollbackSupportReceiptHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RollbackSupportReceipt_Project_RollbackPlan' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE UNIQUE INDEX UX_RollbackSupportReceipt_Project_RollbackPlan
    ON governance.RollbackSupportReceipt(ProjectId, RollbackPlanId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackSupportReceipt_Project_PatchArtifact' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE INDEX IX_RollbackSupportReceipt_Project_PatchArtifact
    ON governance.RollbackSupportReceipt(ProjectId, PatchArtifactId, CreatedAtUtc DESC, RollbackSupportReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackSupportReceipt_Project_PatchHash' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE INDEX IX_RollbackSupportReceipt_Project_PatchHash
    ON governance.RollbackSupportReceipt(ProjectId, PatchHash, CreatedAtUtc DESC, RollbackSupportReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackSupportReceipt_Project_RollbackPlanHash' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE INDEX IX_RollbackSupportReceipt_Project_RollbackPlanHash
    ON governance.RollbackSupportReceipt(ProjectId, RollbackPlanHash, CreatedAtUtc DESC, RollbackSupportReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackSupportReceipt_Project_SourceBaselineHash' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE INDEX IX_RollbackSupportReceipt_Project_SourceBaselineHash
    ON governance.RollbackSupportReceipt(ProjectId, SourceBaselineHash, CreatedAtUtc DESC, RollbackSupportReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackSupportReceipt_Project_ExpectedBranch' AND object_id = OBJECT_ID(N'governance.RollbackSupportReceipt'))
BEGIN
    CREATE INDEX IX_RollbackSupportReceipt_Project_ExpectedBranch
    ON governance.RollbackSupportReceipt(ProjectId, ExpectedBranch, CreatedAtUtc DESC, RollbackSupportReceiptId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_RollbackSupportReceipt_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_RollbackSupportReceipt_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_RollbackSupportReceipt_ValidateInsert
ON governance.RollbackSupportReceipt
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM inserted WHERE RollbackGateSatisfied = 0)
    BEGIN
        THROW 52960, 'Rollback support receipts require a satisfied rollback gate.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.RollbackPlanHash, N' ', i.RollbackGateEvaluationHash, N' ', i.PatchHash, N' ', i.ChangeSetHash, N' ',
            i.DryRunAuditHash, N' ', i.DryRunReceiptHash, N' ', i.PolicySatisfactionHash, N' ',
            i.SubjectKind, N' ', i.SubjectId, N' ', i.SubjectHash, N' ', i.SourceSnapshotReference, N' ',
            i.SourceBaselineHash, N' ', i.WorkspaceBoundaryHash, N' ', i.ExpectedBranch, N' ',
            i.ExpectedCleanWorktreeHash, N' ', i.RollbackSupportReceiptHash, N' ', i.EvidenceReferencesJson, N' ',
            i.BoundaryMaximsJson, N' ', i.BoundaryText
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
           OR c.TextToCheck LIKE N'%rollback succeeded%'
           OR c.TextToCheck LIKE N'%continues workflow%'
           OR c.TextToCheck LIKE N'%workflow continued%'
           OR c.TextToCheck LIKE N'%approves release%'
           OR c.TextToCheck LIKE N'%release approved%'
           OR c.TextToCheck LIKE N'%release ready%'
    )
    BEGIN
        THROW 52961, 'Rollback support receipts must not contain raw/private material or action-authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_RollbackSupportReceipt_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_RollbackSupportReceipt_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_RollbackSupportReceipt_BlockUpdateDelete
ON governance.RollbackSupportReceipt
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52962, 'Rollback support receipts are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_Save
    @RollbackSupportReceiptId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackPlanId UNIQUEIDENTIFIER,
    @RollbackPlanHash NVARCHAR(256),
    @RollbackGateSatisfied BIT,
    @RollbackGateEvaluationHash NVARCHAR(256),
    @PatchArtifactId UNIQUEIDENTIFIER,
    @PatchHash NVARCHAR(256),
    @ChangeSetHash NVARCHAR(256),
    @ControlledDryRunRequestId UNIQUEIDENTIFIER,
    @DryRunExecutionAuditId UNIQUEIDENTIFIER,
    @DryRunAuditHash NVARCHAR(256),
    @DryRunReceiptHash NVARCHAR(256),
    @PolicySatisfactionId UNIQUEIDENTIFIER,
    @PolicySatisfactionHash NVARCHAR(256),
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(300),
    @SubjectHash NVARCHAR(256),
    @SourceSnapshotReference NVARCHAR(512),
    @SourceBaselineHash NVARCHAR(256),
    @WorkspaceBoundaryHash NVARCHAR(256),
    @ExpectedBranch NVARCHAR(300),
    @ExpectedCleanWorktreeHash NVARCHAR(256),
    @RollbackSupportReceiptHash NVARCHAR(256),
    @CreatedAtUtc DATETIMEOFFSET(7),
    @ExpiresAtUtc DATETIMEOFFSET(7) = NULL,
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF @RollbackGateSatisfied = 0
    BEGIN
        THROW 52963, 'Rollback support receipts require a satisfied rollback gate.', 1;
    END;

    IF EXISTS (SELECT 1 FROM governance.RollbackSupportReceipt WHERE RollbackSupportReceiptId = @RollbackSupportReceiptId)
    BEGIN
        THROW 52964, 'Rollback support receipt already exists.', 1;
    END;

    IF EXISTS (SELECT 1 FROM governance.RollbackSupportReceipt WHERE ProjectId = @ProjectId AND RollbackSupportReceiptHash = LTRIM(RTRIM(@RollbackSupportReceiptHash)))
    BEGIN
        THROW 52965, 'Rollback support receipt hash already exists for project.', 1;
    END;

    IF EXISTS (SELECT 1 FROM governance.RollbackSupportReceipt WHERE ProjectId = @ProjectId AND RollbackPlanId = @RollbackPlanId)
    BEGIN
        THROW 52966, 'Rollback plan already has a rollback support receipt for project.', 1;
    END;

    INSERT INTO governance.RollbackSupportReceipt
    (
        RollbackSupportReceiptId,
        ProjectId,
        RollbackPlanId,
        RollbackPlanHash,
        RollbackGateSatisfied,
        RollbackGateEvaluationHash,
        PatchArtifactId,
        PatchHash,
        ChangeSetHash,
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
        ExpectedBranch,
        ExpectedCleanWorktreeHash,
        RollbackSupportReceiptHash,
        CreatedAtUtc,
        ExpiresAtUtc,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        BoundaryText
    )
    VALUES
    (
        @RollbackSupportReceiptId,
        @ProjectId,
        @RollbackPlanId,
        LTRIM(RTRIM(@RollbackPlanHash)),
        @RollbackGateSatisfied,
        LTRIM(RTRIM(@RollbackGateEvaluationHash)),
        @PatchArtifactId,
        LTRIM(RTRIM(@PatchHash)),
        LTRIM(RTRIM(@ChangeSetHash)),
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
        LTRIM(RTRIM(@ExpectedBranch)),
        LTRIM(RTRIM(@ExpectedCleanWorktreeHash)),
        LTRIM(RTRIM(@RollbackSupportReceiptHash)),
        @CreatedAtUtc,
        @ExpiresAtUtc,
        @EvidenceReferencesJson,
        @BoundaryMaximsJson,
        LTRIM(RTRIM(@BoundaryText))
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_Get
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM governance.RollbackSupportReceipt
    WHERE ProjectId = @ProjectId
      AND RollbackSupportReceiptId = @RollbackSupportReceiptId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_GetByReceiptHash
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptHash NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM governance.RollbackSupportReceipt
    WHERE ProjectId = @ProjectId
      AND RollbackSupportReceiptHash = LTRIM(RTRIM(@RollbackSupportReceiptHash));
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_ListByPatchArtifact
    @ProjectId UNIQUEIDENTIFIER,
    @PatchArtifactId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.RollbackSupportReceipt
    WHERE ProjectId = @ProjectId
      AND PatchArtifactId = @PatchArtifactId
    ORDER BY CreatedAtUtc DESC, RollbackSupportReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_ListByPatchHash
    @ProjectId UNIQUEIDENTIFIER,
    @PatchHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.RollbackSupportReceipt
    WHERE ProjectId = @ProjectId
      AND PatchHash = LTRIM(RTRIM(@PatchHash))
    ORDER BY CreatedAtUtc DESC, RollbackSupportReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_ListByRollbackPlan
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackPlanId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.RollbackSupportReceipt
    WHERE ProjectId = @ProjectId
      AND RollbackPlanId = @RollbackPlanId
    ORDER BY CreatedAtUtc DESC, RollbackSupportReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash
    @ProjectId UNIQUEIDENTIFIER,
    @SourceBaselineHash NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.RollbackSupportReceipt
    WHERE ProjectId = @ProjectId
      AND SourceBaselineHash = LTRIM(RTRIM(@SourceBaselineHash))
    ORDER BY CreatedAtUtc DESC, RollbackSupportReceiptId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_GetByReceiptHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_ListByPatchArtifact TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_ListByPatchHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_ListByRollbackPlan TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.RollbackSupportReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.RollbackSupportReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

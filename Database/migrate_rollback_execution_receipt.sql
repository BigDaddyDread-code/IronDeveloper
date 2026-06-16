IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.RollbackExecutionReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE governance.RollbackExecutionReceipt
    (
        RollbackExecutionReceiptId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ControlledRollbackExecutionRequestId UNIQUEIDENTIFIER NOT NULL,
        RollbackPlanId UNIQUEIDENTIFIER NOT NULL,
        RollbackPlanHash NVARCHAR(256) NOT NULL,
        RollbackSupportReceiptId UNIQUEIDENTIFIER NOT NULL,
        RollbackSupportReceiptHash NVARCHAR(256) NOT NULL,
        SourceApplyRequestId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyRequestHash NVARCHAR(256) NOT NULL,
        SourceApplyReceiptId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyReceiptHash NVARCHAR(256) NOT NULL,
        PatchArtifactId UNIQUEIDENTIFIER NOT NULL,
        PatchHash NVARCHAR(256) NOT NULL,
        ChangeSetHash NVARCHAR(256) NOT NULL,
        SourceBaselineHash NVARCHAR(256) NOT NULL,
        WorkspaceBoundaryHash NVARCHAR(256) NOT NULL,
        ExpectedBranch NVARCHAR(300) NOT NULL,
        ExpectedCleanWorktreeHash NVARCHAR(256) NOT NULL,
        ObservedBranch NVARCHAR(300) NOT NULL,
        ObservedSourceBaselineHash NVARCHAR(256) NOT NULL,
        ObservedCleanWorktreeHashBeforeRollback NVARCHAR(256) NOT NULL,
        ObservedCleanWorktreeHashAfterRollback NVARCHAR(256) NOT NULL,
        MutationOccurred BIT NOT NULL,
        RollbackSucceeded BIT NOT NULL,
        PartialRollbackOccurred BIT NOT NULL,
        FileResultsJson NVARCHAR(MAX) NOT NULL,
        IssueCodesJson NVARCHAR(MAX) NOT NULL,
        RolledBackAtUtc DATETIMEOFFSET(7) NOT NULL,
        RollbackExecutionReceiptHash NVARCHAR(256) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        StoredAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_RollbackExecutionReceipt_StoredAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_RollbackExecutionReceipt PRIMARY KEY CLUSTERED (RollbackExecutionReceiptId),
        CONSTRAINT CK_RollbackExecutionReceipt_RollbackPlanHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackPlanHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_RollbackSupportHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackSupportReceiptHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_SourceApplyRequestHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyRequestHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_SourceApplyReceiptHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyReceiptHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_PatchHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PatchHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ChangeSetHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ChangeSetHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_SourceBaselineHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceBaselineHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_WorkspaceBoundaryHash_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceBoundaryHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ExpectedBranch_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedBranch))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ExpectedCleanWorktreeHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedCleanWorktreeHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ObservedBranch_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedBranch))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ObservedSourceBaselineHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedSourceBaselineHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ObservedBeforeHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedCleanWorktreeHashBeforeRollback))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_ObservedAfterHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedCleanWorktreeHashAfterRollback))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_FileResultsJson_IsJson CHECK (ISJSON(FileResultsJson) = 1),
        CONSTRAINT CK_RollbackExecutionReceipt_FileResultsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(FileResultsJson))) > 2),
        CONSTRAINT CK_RollbackExecutionReceipt_IssueCodesJson_IsJson CHECK (ISJSON(IssueCodesJson) = 1),
        CONSTRAINT CK_RollbackExecutionReceipt_ReceiptHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackExecutionReceiptHash))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_RollbackExecutionReceipt_EvidenceReferencesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_RollbackExecutionReceipt_BoundaryMaximsJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_RollbackExecutionReceipt_BoundaryMaximsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_RollbackExecutionReceipt_BoundaryText_NotBlank CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0),
        CONSTRAINT CK_RollbackExecutionReceipt_PartialNotSucceeded CHECK (PartialRollbackOccurred = 0 OR RollbackSucceeded = 0),
        CONSTRAINT CK_RollbackExecutionReceipt_PartialRequiresMutation CHECK (PartialRollbackOccurred = 0 OR MutationOccurred = 1)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RollbackExecutionReceipt_Project_Hash' AND object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt'))
BEGIN
    CREATE UNIQUE INDEX UX_RollbackExecutionReceipt_Project_Hash
    ON governance.RollbackExecutionReceipt(ProjectId, RollbackExecutionReceiptHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackExecutionReceipt_Project_SourceApplyReceipt' AND object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt'))
BEGIN
    CREATE INDEX IX_RollbackExecutionReceipt_Project_SourceApplyReceipt
    ON governance.RollbackExecutionReceipt(ProjectId, SourceApplyReceiptId, RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackExecutionReceipt_Project_RollbackPlan' AND object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt'))
BEGIN
    CREATE INDEX IX_RollbackExecutionReceipt_Project_RollbackPlan
    ON governance.RollbackExecutionReceipt(ProjectId, RollbackPlanId, RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackExecutionReceipt_Project_RollbackSupport' AND object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt'))
BEGIN
    CREATE INDEX IX_RollbackExecutionReceipt_Project_RollbackSupport
    ON governance.RollbackExecutionReceipt(ProjectId, RollbackSupportReceiptId, RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RollbackExecutionReceipt_Project_PatchArtifact' AND object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt'))
BEGIN
    CREATE INDEX IX_RollbackExecutionReceipt_Project_PatchArtifact
    ON governance.RollbackExecutionReceipt(ProjectId, PatchArtifactId, RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_RollbackExecutionReceipt_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_RollbackExecutionReceipt_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_RollbackExecutionReceipt_ValidateInsert
ON governance.RollbackExecutionReceipt
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.RollbackPlanHash, N' ', i.RollbackSupportReceiptHash, N' ', i.SourceApplyRequestHash, N' ', i.SourceApplyReceiptHash, N' ',
            i.PatchHash, N' ', i.ChangeSetHash, N' ', i.SourceBaselineHash, N' ', i.WorkspaceBoundaryHash, N' ',
            i.ExpectedBranch, N' ', i.ExpectedCleanWorktreeHash, N' ', i.ObservedBranch, N' ', i.ObservedSourceBaselineHash, N' ',
            i.ObservedCleanWorktreeHashBeforeRollback, N' ', i.ObservedCleanWorktreeHashAfterRollback, N' ', i.FileResultsJson, N' ',
            i.IssueCodesJson, N' ', i.RollbackExecutionReceiptHash, N' ', i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson, N' ', i.BoundaryText
        )) AS TextToCheck) c
        WHERE c.TextToCheck LIKE N'%rawprompt%'
           OR c.TextToCheck LIKE N'%raw prompt%'
           OR c.TextToCheck LIKE N'%rawcompletion%'
           OR c.TextToCheck LIKE N'%raw completion%'
           OR c.TextToCheck LIKE N'%rawtooloutput%'
           OR c.TextToCheck LIKE N'%raw tool output%'
           OR c.TextToCheck LIKE N'%entirepatch%'
           OR c.TextToCheck LIKE N'%entire patch%'
           OR c.TextToCheck LIKE N'%patch payload%'
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
           OR c.TextToCheck LIKE N'%approval granted%'
           OR c.TextToCheck LIKE N'%policy satisfied%'
           OR c.TextToCheck LIKE N'%execution allowed%'
           OR c.TextToCheck LIKE N'%workflow continued%'
           OR c.TextToCheck LIKE N'%release approved%'
           OR c.TextToCheck LIKE N'%release ready%'
           OR c.TextToCheck LIKE N'%memory promoted%'
           OR c.TextToCheck LIKE N'%retrieval activated%'
           OR c.TextToCheck LIKE N'%gitcommitted%'
           OR c.TextToCheck LIKE N'%gitpushed%'
           OR c.TextToCheck LIKE N'%pull request created%'
           OR c.TextToCheck LIKE N'%source applied%'
    )
    BEGIN
        THROW 53451, 'Rollback execution receipts must not contain raw/private material or authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_RollbackExecutionReceipt_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_RollbackExecutionReceipt_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_RollbackExecutionReceipt_BlockUpdateDelete
ON governance.RollbackExecutionReceipt
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53452, 'RollbackExecutionReceipt is append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_Save
    @RollbackExecutionReceiptId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ControlledRollbackExecutionRequestId UNIQUEIDENTIFIER,
    @RollbackPlanId UNIQUEIDENTIFIER,
    @RollbackPlanHash NVARCHAR(256),
    @RollbackSupportReceiptId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptHash NVARCHAR(256),
    @SourceApplyRequestId UNIQUEIDENTIFIER,
    @SourceApplyRequestHash NVARCHAR(256),
    @SourceApplyReceiptId UNIQUEIDENTIFIER,
    @SourceApplyReceiptHash NVARCHAR(256),
    @PatchArtifactId UNIQUEIDENTIFIER,
    @PatchHash NVARCHAR(256),
    @ChangeSetHash NVARCHAR(256),
    @SourceBaselineHash NVARCHAR(256),
    @WorkspaceBoundaryHash NVARCHAR(256),
    @ExpectedBranch NVARCHAR(300),
    @ExpectedCleanWorktreeHash NVARCHAR(256),
    @ObservedBranch NVARCHAR(300),
    @ObservedSourceBaselineHash NVARCHAR(256),
    @ObservedCleanWorktreeHashBeforeRollback NVARCHAR(256),
    @ObservedCleanWorktreeHashAfterRollback NVARCHAR(256),
    @MutationOccurred BIT,
    @RollbackSucceeded BIT,
    @PartialRollbackOccurred BIT,
    @FileResultsJson NVARCHAR(MAX),
    @IssueCodesJson NVARCHAR(MAX),
    @RolledBackAtUtc DATETIMEOFFSET(7),
    @RollbackExecutionReceiptHash NVARCHAR(256),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.RollbackExecutionReceipt WHERE RollbackExecutionReceiptId = @RollbackExecutionReceiptId AND RollbackExecutionReceiptHash = @RollbackExecutionReceiptHash)
        RETURN;

    IF EXISTS (SELECT 1 FROM governance.RollbackExecutionReceipt WHERE RollbackExecutionReceiptId = @RollbackExecutionReceiptId AND RollbackExecutionReceiptHash <> @RollbackExecutionReceiptHash)
        THROW 53453, 'RollbackExecutionReceiptId already exists with different hash.', 1;

    INSERT INTO governance.RollbackExecutionReceipt
    (
        RollbackExecutionReceiptId, ProjectId, ControlledRollbackExecutionRequestId, RollbackPlanId, RollbackPlanHash,
        RollbackSupportReceiptId, RollbackSupportReceiptHash, SourceApplyRequestId, SourceApplyRequestHash,
        SourceApplyReceiptId, SourceApplyReceiptHash, PatchArtifactId, PatchHash, ChangeSetHash,
        SourceBaselineHash, WorkspaceBoundaryHash, ExpectedBranch, ExpectedCleanWorktreeHash, ObservedBranch,
        ObservedSourceBaselineHash, ObservedCleanWorktreeHashBeforeRollback, ObservedCleanWorktreeHashAfterRollback,
        MutationOccurred, RollbackSucceeded, PartialRollbackOccurred, FileResultsJson, IssueCodesJson,
        RolledBackAtUtc, RollbackExecutionReceiptHash, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText
    )
    VALUES
    (
        @RollbackExecutionReceiptId, @ProjectId, @ControlledRollbackExecutionRequestId, @RollbackPlanId, LTRIM(RTRIM(@RollbackPlanHash)),
        @RollbackSupportReceiptId, LTRIM(RTRIM(@RollbackSupportReceiptHash)), @SourceApplyRequestId, LTRIM(RTRIM(@SourceApplyRequestHash)),
        @SourceApplyReceiptId, LTRIM(RTRIM(@SourceApplyReceiptHash)), @PatchArtifactId, LTRIM(RTRIM(@PatchHash)), LTRIM(RTRIM(@ChangeSetHash)),
        LTRIM(RTRIM(@SourceBaselineHash)), LTRIM(RTRIM(@WorkspaceBoundaryHash)), LTRIM(RTRIM(@ExpectedBranch)), LTRIM(RTRIM(@ExpectedCleanWorktreeHash)), LTRIM(RTRIM(@ObservedBranch)),
        LTRIM(RTRIM(@ObservedSourceBaselineHash)), LTRIM(RTRIM(@ObservedCleanWorktreeHashBeforeRollback)), LTRIM(RTRIM(@ObservedCleanWorktreeHashAfterRollback)),
        @MutationOccurred, @RollbackSucceeded, @PartialRollbackOccurred, @FileResultsJson, @IssueCodesJson,
        @RolledBackAtUtc, LTRIM(RTRIM(@RollbackExecutionReceiptHash)), @EvidenceReferencesJson, @BoundaryMaximsJson, @BoundaryText
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_Get
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackExecutionReceiptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.RollbackExecutionReceipt WHERE ProjectId = @ProjectId AND RollbackExecutionReceiptId = @RollbackExecutionReceiptId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_GetByReceiptHash
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackExecutionReceiptHash NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.RollbackExecutionReceipt WHERE ProjectId = @ProjectId AND RollbackExecutionReceiptHash = LTRIM(RTRIM(@RollbackExecutionReceiptHash));
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_ListBySourceApplyReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.RollbackExecutionReceipt
    WHERE ProjectId = @ProjectId AND SourceApplyReceiptId = @SourceApplyReceiptId
    ORDER BY RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_ListByRollbackPlan
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackPlanId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.RollbackExecutionReceipt
    WHERE ProjectId = @ProjectId AND RollbackPlanId = @RollbackPlanId
    ORDER BY RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_ListByRollbackSupportReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.RollbackExecutionReceipt
    WHERE ProjectId = @ProjectId AND RollbackSupportReceiptId = @RollbackSupportReceiptId
    ORDER BY RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_RollbackExecutionReceipt_ListByPatchArtifact
    @ProjectId UNIQUEIDENTIFIER,
    @PatchArtifactId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.RollbackExecutionReceipt
    WHERE ProjectId = @ProjectId AND PatchArtifactId = @PatchArtifactId
    ORDER BY RolledBackAtUtc DESC, RollbackExecutionReceiptId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_GetByReceiptHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_ListBySourceApplyReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_ListByRollbackPlan TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_ListByRollbackSupportReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_RollbackExecutionReceipt_ListByPatchArtifact TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.RollbackExecutionReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.RollbackExecutionReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

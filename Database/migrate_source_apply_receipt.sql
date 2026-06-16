IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.SourceApplyReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE governance.SourceApplyReceipt
    (
        SourceApplyReceiptId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ControlledSourceApplyRequestId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyRequestId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyRequestHash NVARCHAR(256) NOT NULL,
        SourceApplyDryRunReceiptId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyDryRunReceiptHash NVARCHAR(256) NOT NULL,
        SourceApplyGateEvaluationId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyGateEvaluationHash NVARCHAR(256) NOT NULL,
        PatchArtifactId UNIQUEIDENTIFIER NOT NULL,
        PatchHash NVARCHAR(256) NOT NULL,
        ChangeSetHash NVARCHAR(256) NOT NULL,
        RollbackSupportReceiptId UNIQUEIDENTIFIER NOT NULL,
        RollbackSupportReceiptHash NVARCHAR(256) NOT NULL,
        SourceBaselineHash NVARCHAR(256) NOT NULL,
        WorkspaceBoundaryHash NVARCHAR(256) NOT NULL,
        ExpectedBranch NVARCHAR(300) NOT NULL,
        ExpectedCleanWorktreeHash NVARCHAR(256) NOT NULL,
        ObservedBranch NVARCHAR(300) NOT NULL,
        ObservedCleanWorktreeHashBeforeApply NVARCHAR(256) NOT NULL,
        ObservedCleanWorktreeHashAfterApply NVARCHAR(256) NOT NULL,
        MutationOccurred BIT NOT NULL,
        ApplySucceeded BIT NOT NULL,
        PartialApplyOccurred BIT NOT NULL,
        FileResultsJson NVARCHAR(MAX) NOT NULL,
        IssueCodesJson NVARCHAR(MAX) NOT NULL,
        AppliedAtUtc DATETIMEOFFSET(7) NOT NULL,
        SourceApplyReceiptHash NVARCHAR(256) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        StoredAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SourceApplyReceipt_StoredAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_SourceApplyReceipt PRIMARY KEY CLUSTERED (SourceApplyReceiptId),
        CONSTRAINT CK_SourceApplyReceipt_SourceApplyRequestHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyRequestHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_DryRunReceiptHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyDryRunReceiptHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_GateHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyGateEvaluationHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_PatchHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PatchHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_ChangeSetHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ChangeSetHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_RollbackSupportHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackSupportReceiptHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_SourceBaselineHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceBaselineHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_WorkspaceBoundaryHash_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceBoundaryHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_ExpectedBranch_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedBranch))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_ExpectedCleanWorktreeHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedCleanWorktreeHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_ObservedBranch_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedBranch))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_ObservedBeforeHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedCleanWorktreeHashBeforeApply))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_ObservedAfterHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ObservedCleanWorktreeHashAfterApply))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_FileResultsJson_IsJson CHECK (ISJSON(FileResultsJson) = 1),
        CONSTRAINT CK_SourceApplyReceipt_FileResultsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(FileResultsJson))) > 2),
        CONSTRAINT CK_SourceApplyReceipt_IssueCodesJson_IsJson CHECK (ISJSON(IssueCodesJson) = 1),
        CONSTRAINT CK_SourceApplyReceipt_ReceiptHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyReceiptHash))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_SourceApplyReceipt_EvidenceReferencesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_SourceApplyReceipt_BoundaryMaximsJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_SourceApplyReceipt_BoundaryMaximsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_SourceApplyReceipt_BoundaryText_NotBlank CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0),
        CONSTRAINT CK_SourceApplyReceipt_PartialNotSucceeded CHECK (PartialApplyOccurred = 0 OR ApplySucceeded = 0),
        CONSTRAINT CK_SourceApplyReceipt_PartialRequiresMutation CHECK (PartialApplyOccurred = 0 OR MutationOccurred = 1)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SourceApplyReceipt_Project_Hash' AND object_id = OBJECT_ID(N'governance.SourceApplyReceipt'))
BEGIN
    CREATE UNIQUE INDEX UX_SourceApplyReceipt_Project_Hash
    ON governance.SourceApplyReceipt(ProjectId, SourceApplyReceiptHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyReceipt_Project_SourceApplyRequest' AND object_id = OBJECT_ID(N'governance.SourceApplyReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyReceipt_Project_SourceApplyRequest
    ON governance.SourceApplyReceipt(ProjectId, SourceApplyRequestId, AppliedAtUtc DESC, SourceApplyReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyReceipt_Project_DryRunReceipt' AND object_id = OBJECT_ID(N'governance.SourceApplyReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyReceipt_Project_DryRunReceipt
    ON governance.SourceApplyReceipt(ProjectId, SourceApplyDryRunReceiptId, AppliedAtUtc DESC, SourceApplyReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyReceipt_Project_PatchArtifact' AND object_id = OBJECT_ID(N'governance.SourceApplyReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyReceipt_Project_PatchArtifact
    ON governance.SourceApplyReceipt(ProjectId, PatchArtifactId, AppliedAtUtc DESC, SourceApplyReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyReceipt_Project_RollbackSupport' AND object_id = OBJECT_ID(N'governance.SourceApplyReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyReceipt_Project_RollbackSupport
    ON governance.SourceApplyReceipt(ProjectId, RollbackSupportReceiptId, AppliedAtUtc DESC, SourceApplyReceiptId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_SourceApplyReceipt_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_SourceApplyReceipt_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_SourceApplyReceipt_ValidateInsert
ON governance.SourceApplyReceipt
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.SourceApplyRequestHash, N' ', i.SourceApplyDryRunReceiptHash, N' ', i.SourceApplyGateEvaluationHash, N' ',
            i.PatchHash, N' ', i.ChangeSetHash, N' ', i.RollbackSupportReceiptHash, N' ', i.SourceBaselineHash, N' ',
            i.WorkspaceBoundaryHash, N' ', i.ExpectedBranch, N' ', i.ExpectedCleanWorktreeHash, N' ',
            i.ObservedBranch, N' ', i.ObservedCleanWorktreeHashBeforeApply, N' ', i.ObservedCleanWorktreeHashAfterApply, N' ',
            i.FileResultsJson, N' ', i.IssueCodesJson, N' ', i.SourceApplyReceiptHash, N' ', i.EvidenceReferencesJson, N' ',
            i.BoundaryMaximsJson
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
           OR c.TextToCheck LIKE N'%git committed%'
           OR c.TextToCheck LIKE N'%git pushed%'
           OR c.TextToCheck LIKE N'%pull request created%'
           OR c.TextToCheck LIKE N'%rollback executed%'
    )
    BEGIN
        THROW 53401, 'Source apply receipts must not contain raw/private material or authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_SourceApplyReceipt_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_SourceApplyReceipt_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_SourceApplyReceipt_BlockUpdateDelete
ON governance.SourceApplyReceipt
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53402, 'SourceApplyReceipt is append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_Save
    @SourceApplyReceiptId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ControlledSourceApplyRequestId UNIQUEIDENTIFIER,
    @SourceApplyRequestId UNIQUEIDENTIFIER,
    @SourceApplyRequestHash NVARCHAR(256),
    @SourceApplyDryRunReceiptId UNIQUEIDENTIFIER,
    @SourceApplyDryRunReceiptHash NVARCHAR(256),
    @SourceApplyGateEvaluationId UNIQUEIDENTIFIER,
    @SourceApplyGateEvaluationHash NVARCHAR(256),
    @PatchArtifactId UNIQUEIDENTIFIER,
    @PatchHash NVARCHAR(256),
    @ChangeSetHash NVARCHAR(256),
    @RollbackSupportReceiptId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptHash NVARCHAR(256),
    @SourceBaselineHash NVARCHAR(256),
    @WorkspaceBoundaryHash NVARCHAR(256),
    @ExpectedBranch NVARCHAR(300),
    @ExpectedCleanWorktreeHash NVARCHAR(256),
    @ObservedBranch NVARCHAR(300),
    @ObservedCleanWorktreeHashBeforeApply NVARCHAR(256),
    @ObservedCleanWorktreeHashAfterApply NVARCHAR(256),
    @MutationOccurred BIT,
    @ApplySucceeded BIT,
    @PartialApplyOccurred BIT,
    @FileResultsJson NVARCHAR(MAX),
    @IssueCodesJson NVARCHAR(MAX),
    @AppliedAtUtc DATETIMEOFFSET(7),
    @SourceApplyReceiptHash NVARCHAR(256),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.SourceApplyReceipt WHERE SourceApplyReceiptId = @SourceApplyReceiptId AND SourceApplyReceiptHash = @SourceApplyReceiptHash)
        RETURN;

    IF EXISTS (SELECT 1 FROM governance.SourceApplyReceipt WHERE SourceApplyReceiptId = @SourceApplyReceiptId AND SourceApplyReceiptHash <> @SourceApplyReceiptHash)
        THROW 53403, 'SourceApplyReceiptId already exists with different hash.', 1;

    INSERT INTO governance.SourceApplyReceipt
    (
        SourceApplyReceiptId, ProjectId, ControlledSourceApplyRequestId, SourceApplyRequestId, SourceApplyRequestHash,
        SourceApplyDryRunReceiptId, SourceApplyDryRunReceiptHash, SourceApplyGateEvaluationId, SourceApplyGateEvaluationHash,
        PatchArtifactId, PatchHash, ChangeSetHash, RollbackSupportReceiptId, RollbackSupportReceiptHash,
        SourceBaselineHash, WorkspaceBoundaryHash, ExpectedBranch, ExpectedCleanWorktreeHash, ObservedBranch,
        ObservedCleanWorktreeHashBeforeApply, ObservedCleanWorktreeHashAfterApply, MutationOccurred, ApplySucceeded,
        PartialApplyOccurred, FileResultsJson, IssueCodesJson, AppliedAtUtc, SourceApplyReceiptHash,
        EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText
    )
    VALUES
    (
        @SourceApplyReceiptId, @ProjectId, @ControlledSourceApplyRequestId, @SourceApplyRequestId, LTRIM(RTRIM(@SourceApplyRequestHash)),
        @SourceApplyDryRunReceiptId, LTRIM(RTRIM(@SourceApplyDryRunReceiptHash)), @SourceApplyGateEvaluationId, LTRIM(RTRIM(@SourceApplyGateEvaluationHash)),
        @PatchArtifactId, LTRIM(RTRIM(@PatchHash)), LTRIM(RTRIM(@ChangeSetHash)), @RollbackSupportReceiptId, LTRIM(RTRIM(@RollbackSupportReceiptHash)),
        LTRIM(RTRIM(@SourceBaselineHash)), LTRIM(RTRIM(@WorkspaceBoundaryHash)), LTRIM(RTRIM(@ExpectedBranch)), LTRIM(RTRIM(@ExpectedCleanWorktreeHash)), LTRIM(RTRIM(@ObservedBranch)),
        LTRIM(RTRIM(@ObservedCleanWorktreeHashBeforeApply)), LTRIM(RTRIM(@ObservedCleanWorktreeHashAfterApply)), @MutationOccurred, @ApplySucceeded,
        @PartialApplyOccurred, @FileResultsJson, @IssueCodesJson, @AppliedAtUtc, LTRIM(RTRIM(@SourceApplyReceiptHash)),
        @EvidenceReferencesJson, @BoundaryMaximsJson, @BoundaryText
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_Get
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyReceiptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.SourceApplyReceipt WHERE ProjectId = @ProjectId AND SourceApplyReceiptId = @SourceApplyReceiptId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_GetByReceiptHash
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyReceiptHash NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.SourceApplyReceipt WHERE ProjectId = @ProjectId AND SourceApplyReceiptHash = LTRIM(RTRIM(@SourceApplyReceiptHash));
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_ListBySourceApplyRequest
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyRequestId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.SourceApplyReceipt
    WHERE ProjectId = @ProjectId AND SourceApplyRequestId = @SourceApplyRequestId
    ORDER BY AppliedAtUtc DESC, SourceApplyReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_ListBySourceApplyDryRunReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyDryRunReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.SourceApplyReceipt
    WHERE ProjectId = @ProjectId AND SourceApplyDryRunReceiptId = @SourceApplyDryRunReceiptId
    ORDER BY AppliedAtUtc DESC, SourceApplyReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_ListByPatchArtifact
    @ProjectId UNIQUEIDENTIFIER,
    @PatchArtifactId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.SourceApplyReceipt
    WHERE ProjectId = @ProjectId AND PatchArtifactId = @PatchArtifactId
    ORDER BY AppliedAtUtc DESC, SourceApplyReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyReceipt_ListByRollbackSupportReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.SourceApplyReceipt
    WHERE ProjectId = @ProjectId AND RollbackSupportReceiptId = @RollbackSupportReceiptId
    ORDER BY AppliedAtUtc DESC, SourceApplyReceiptId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_GetByReceiptHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_ListBySourceApplyRequest TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_ListBySourceApplyDryRunReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_ListByPatchArtifact TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyReceipt_ListByRollbackSupportReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.SourceApplyReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.SourceApplyReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

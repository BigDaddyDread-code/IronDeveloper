IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.SourceApplyDryRunReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE governance.SourceApplyDryRunReceipt
    (
        SourceApplyDryRunReceiptId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyDryRunRequestId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyDryRunRequestHash NVARCHAR(256) NOT NULL,
        DryRunSatisfied BIT NOT NULL,
        DryRunResultHash NVARCHAR(256) NOT NULL,
        SourceApplyRequestId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyRequestHash NVARCHAR(256) NOT NULL,
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
        FileResultsJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ExpiresAtUtc DATETIMEOFFSET(7) NULL,
        SourceApplyDryRunReceiptHash NVARCHAR(256) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        StoredAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SourceApplyDryRunReceipt_StoredAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_SourceApplyDryRunReceipt PRIMARY KEY CLUSTERED (SourceApplyDryRunReceiptId),
        CONSTRAINT CK_SourceApplyDryRunReceipt_RequestHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyDryRunRequestHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_ResultHash_NotBlank CHECK (LEN(LTRIM(RTRIM(DryRunResultHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_SourceApplyRequestHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyRequestHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_GateHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyGateEvaluationHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_PatchHash_NotBlank CHECK (LEN(LTRIM(RTRIM(PatchHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_ChangeSetHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ChangeSetHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_RollbackSupportHash_NotBlank CHECK (LEN(LTRIM(RTRIM(RollbackSupportReceiptHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_SourceBaselineHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceBaselineHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_WorkspaceBoundaryHash_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkspaceBoundaryHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_ExpectedBranch_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedBranch))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_ExpectedCleanWorktreeHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ExpectedCleanWorktreeHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_FileResultsJson_IsJson CHECK (ISJSON(FileResultsJson) = 1),
        CONSTRAINT CK_SourceApplyDryRunReceipt_FileResultsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(FileResultsJson))) > 2),
        CONSTRAINT CK_SourceApplyDryRunReceipt_Hash_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceApplyDryRunReceiptHash))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_SourceApplyDryRunReceipt_EvidenceReferencesJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_SourceApplyDryRunReceipt_BoundaryMaximsJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_SourceApplyDryRunReceipt_BoundaryMaximsJson_NotEmpty CHECK (LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_SourceApplyDryRunReceipt_BoundaryText_NotBlank CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0),
        CONSTRAINT CK_SourceApplyDryRunReceipt_ExpiresAfterCreated CHECK (ExpiresAtUtc IS NULL OR ExpiresAtUtc > CreatedAtUtc)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_SourceApplyDryRunReceipt_Project_Hash' AND object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt'))
BEGIN
    CREATE UNIQUE INDEX UX_SourceApplyDryRunReceipt_Project_Hash
    ON governance.SourceApplyDryRunReceipt(ProjectId, SourceApplyDryRunReceiptHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyDryRunReceipt_Project_SourceApplyRequest' AND object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyDryRunReceipt_Project_SourceApplyRequest
    ON governance.SourceApplyDryRunReceipt(ProjectId, SourceApplyRequestId, CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyDryRunReceipt_Project_Gate' AND object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyDryRunReceipt_Project_Gate
    ON governance.SourceApplyDryRunReceipt(ProjectId, SourceApplyGateEvaluationId, CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyDryRunReceipt_Project_PatchArtifact' AND object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyDryRunReceipt_Project_PatchArtifact
    ON governance.SourceApplyDryRunReceipt(ProjectId, PatchArtifactId, CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SourceApplyDryRunReceipt_Project_RollbackSupport' AND object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt'))
BEGIN
    CREATE INDEX IX_SourceApplyDryRunReceipt_Project_RollbackSupport
    ON governance.SourceApplyDryRunReceipt(ProjectId, RollbackSupportReceiptId, CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_SourceApplyDryRunReceipt_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_SourceApplyDryRunReceipt_ValidateInsert
ON governance.SourceApplyDryRunReceipt
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.SourceApplyDryRunRequestHash, N' ', i.DryRunResultHash, N' ', i.SourceApplyRequestHash, N' ',
            i.SourceApplyGateEvaluationHash, N' ', i.PatchHash, N' ', i.ChangeSetHash, N' ',
            i.RollbackSupportReceiptHash, N' ', i.SourceBaselineHash, N' ', i.WorkspaceBoundaryHash, N' ',
            i.ExpectedBranch, N' ', i.ExpectedCleanWorktreeHash, N' ', i.FileResultsJson, N' ',
            i.SourceApplyDryRunReceiptHash, N' ', i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson, N' ',
            i.BoundaryText
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
           OR c.TextToCheck LIKE N'%source apply succeeded%'
           OR c.TextToCheck LIKE N'%source mutated%'
           OR c.TextToCheck LIKE N'%git applied%'
           OR c.TextToCheck LIKE N'%patch applied%'
           OR c.TextToCheck LIKE N'%files written%'
           OR c.TextToCheck LIKE N'%rollback executed%'
           OR c.TextToCheck LIKE N'%rollback succeeded%'
           OR c.TextToCheck LIKE N'%workflow continued%'
           OR c.TextToCheck LIKE N'%release approved%'
           OR c.TextToCheck LIKE N'%release ready%'
    )
    BEGIN
        THROW 53201, 'Source apply dry-run receipts must not contain raw/private material or action-authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete
ON governance.SourceApplyDryRunReceipt
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53202, 'Source apply dry-run receipts are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_Save
    @SourceApplyDryRunReceiptId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyDryRunRequestId UNIQUEIDENTIFIER,
    @SourceApplyDryRunRequestHash NVARCHAR(256),
    @DryRunSatisfied BIT,
    @DryRunResultHash NVARCHAR(256),
    @SourceApplyRequestId UNIQUEIDENTIFIER,
    @SourceApplyRequestHash NVARCHAR(256),
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
    @FileResultsJson NVARCHAR(MAX),
    @CreatedAtUtc DATETIMEOFFSET(7),
    @ExpiresAtUtc DATETIMEOFFSET(7) = NULL,
    @SourceApplyDryRunReceiptHash NVARCHAR(256),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX)
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.SourceApplyDryRunReceipt WHERE SourceApplyDryRunReceiptId = @SourceApplyDryRunReceiptId)
    BEGIN
        THROW 53203, 'Source apply dry-run receipt already exists.', 1;
    END;

    IF EXISTS (SELECT 1 FROM governance.SourceApplyDryRunReceipt WHERE ProjectId = @ProjectId AND SourceApplyDryRunReceiptHash = LTRIM(RTRIM(@SourceApplyDryRunReceiptHash)))
    BEGIN
        THROW 53204, 'Source apply dry-run receipt hash already exists for project.', 1;
    END;

    INSERT INTO governance.SourceApplyDryRunReceipt
    (
        SourceApplyDryRunReceiptId,
        ProjectId,
        SourceApplyDryRunRequestId,
        SourceApplyDryRunRequestHash,
        DryRunSatisfied,
        DryRunResultHash,
        SourceApplyRequestId,
        SourceApplyRequestHash,
        SourceApplyGateEvaluationId,
        SourceApplyGateEvaluationHash,
        PatchArtifactId,
        PatchHash,
        ChangeSetHash,
        RollbackSupportReceiptId,
        RollbackSupportReceiptHash,
        SourceBaselineHash,
        WorkspaceBoundaryHash,
        ExpectedBranch,
        ExpectedCleanWorktreeHash,
        FileResultsJson,
        CreatedAtUtc,
        ExpiresAtUtc,
        SourceApplyDryRunReceiptHash,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        BoundaryText
    )
    VALUES
    (
        @SourceApplyDryRunReceiptId,
        @ProjectId,
        @SourceApplyDryRunRequestId,
        LTRIM(RTRIM(@SourceApplyDryRunRequestHash)),
        @DryRunSatisfied,
        LTRIM(RTRIM(@DryRunResultHash)),
        @SourceApplyRequestId,
        LTRIM(RTRIM(@SourceApplyRequestHash)),
        @SourceApplyGateEvaluationId,
        LTRIM(RTRIM(@SourceApplyGateEvaluationHash)),
        @PatchArtifactId,
        LTRIM(RTRIM(@PatchHash)),
        LTRIM(RTRIM(@ChangeSetHash)),
        @RollbackSupportReceiptId,
        LTRIM(RTRIM(@RollbackSupportReceiptHash)),
        LTRIM(RTRIM(@SourceBaselineHash)),
        LTRIM(RTRIM(@WorkspaceBoundaryHash)),
        LTRIM(RTRIM(@ExpectedBranch)),
        LTRIM(RTRIM(@ExpectedCleanWorktreeHash)),
        @FileResultsJson,
        @CreatedAtUtc,
        @ExpiresAtUtc,
        LTRIM(RTRIM(@SourceApplyDryRunReceiptHash)),
        @EvidenceReferencesJson,
        @BoundaryMaximsJson,
        LTRIM(RTRIM(@BoundaryText))
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_Get
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyDryRunReceiptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM governance.SourceApplyDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND SourceApplyDryRunReceiptId = @SourceApplyDryRunReceiptId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyDryRunReceiptHash NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM governance.SourceApplyDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND SourceApplyDryRunReceiptHash = LTRIM(RTRIM(@SourceApplyDryRunReceiptHash));
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyRequestId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.SourceApplyDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND SourceApplyRequestId = @SourceApplyRequestId
    ORDER BY CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyGateEvaluationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.SourceApplyDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND SourceApplyGateEvaluationId = @SourceApplyGateEvaluationId
    ORDER BY CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact
    @ProjectId UNIQUEIDENTIFIER,
    @PatchArtifactId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.SourceApplyDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND PatchArtifactId = @PatchArtifactId
    ORDER BY CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackSupportReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake) *
    FROM governance.SourceApplyDryRunReceipt
    WHERE ProjectId = @ProjectId
      AND RollbackSupportReceiptId = @RollbackSupportReceiptId
    ORDER BY CreatedAtUtc DESC, SourceApplyDryRunReceiptId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.SourceApplyDryRunReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.SourceApplyDryRunReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO

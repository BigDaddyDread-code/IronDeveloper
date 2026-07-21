/* Workbench v0.1 PR-05B: controlled product-neutral repository provisioning. */

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_RepositoryProvisioningAttemptSessionAuthority'
)
    CREATE UNIQUE INDEX UX_ClientOperations_RepositoryProvisioningAttemptSessionAuthority
        ON dbo.ClientOperations
            (Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
             OperationKind, ResourceScopeId, ResultWorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositorySetupConfirmations')
      AND name=N'UQ_RepositorySetupConfirmations_ProjectId'
)
    CREATE UNIQUE INDEX UQ_RepositorySetupConfirmations_ProjectId
        ON dbo.RepositorySetupConfirmations(TenantId, ProjectId, Id);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositorySetupConfirmations')
      AND name=N'UX_RepositorySetupConfirmations_ProvisioningAuthority'
)
    CREATE UNIQUE INDEX UX_RepositorySetupConfirmations_ProvisioningAuthority
        ON dbo.RepositorySetupConfirmations
            (TenantId, ProjectId, Id, RepositoryBindingId, ProjectExecutionProfileId, PlanHash);
GO

IF OBJECT_ID(N'dbo.RepositoryProvisioningAttempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepositoryProvisioningAttempts
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RepositoryProvisioningAttempts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        SetupConfirmationId UNIQUEIDENTIFIER NOT NULL,
        ClientOperationRecordId BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        ActorUserId INT NOT NULL,
        ClientOperationKind NVARCHAR(100) NOT NULL,
        ClientOperationResourceScopeId NVARCHAR(200) NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        AttemptNumber INT NOT NULL,
        ExpectedBindingRevision BIGINT NOT NULL,
        ExpectedExecutionProfileRevision BIGINT NOT NULL,
        PlanHash CHAR(64) NOT NULL,
        DescriptorSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        PlanningBundleSha256 CHAR(64) NOT NULL,
        CanonicalTargetPath NVARCHAR(1000) NOT NULL,
        StagingPath NVARCHAR(1000) NOT NULL,
        State NVARCHAR(40) NOT NULL,
        FailureCode NVARCHAR(100) NULL,
        FailureEvidenceJson NVARCHAR(MAX) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CONSTRAINT UQ_RepositoryProvisioningAttempts_Number UNIQUE
            (TenantId, ProjectId, RepositoryBindingId, AttemptNumber),
        CONSTRAINT UQ_RepositoryProvisioningAttempts_ClientOperation UNIQUE
            (TenantId, ProjectId, ClientOperationId),
        CONSTRAINT UQ_RepositoryProvisioningAttempts_ClientOperationRecord UNIQUE
            (ClientOperationRecordId),
        CONSTRAINT UQ_RepositoryProvisioningAttempts_ProjectId UNIQUE
            (TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryProvisioningAttempts_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryProvisioningAttempts_Profile
            FOREIGN KEY (TenantId, ProjectId, ProjectExecutionProfileId)
            REFERENCES dbo.ProjectExecutionProfiles(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryProvisioningAttempts_ConfirmationAuthority
            FOREIGN KEY
                (TenantId, ProjectId, SetupConfirmationId, RepositoryBindingId,
                 ProjectExecutionProfileId, PlanHash)
            REFERENCES dbo.RepositorySetupConfirmations
                (TenantId, ProjectId, Id, RepositoryBindingId,
                 ProjectExecutionProfileId, PlanHash),
        CONSTRAINT FK_RepositoryProvisioningAttempts_ClientOperationAuthority
            FOREIGN KEY
                (ClientOperationRecordId, TenantId, ProjectId, ClientOperationId, ActorUserId,
                 ClientOperationKind, ClientOperationResourceScopeId, WorkbenchSessionId)
            REFERENCES dbo.ClientOperations
                (Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
                 OperationKind, ResourceScopeId, ResultWorkbenchSessionId),
        CONSTRAINT FK_RepositoryProvisioningAttempts_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_RepositoryProvisioningAttempts_Fence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT CK_RepositoryProvisioningAttempts_AttemptNumber CHECK (AttemptNumber > 0),
        CONSTRAINT CK_RepositoryProvisioningAttempts_ClientOperationAuthority CHECK
            (ClientOperationKind=N'ProvisionRepository' AND
             ClientOperationResourceScopeId=
                 N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':repository-provisioning'),
        CONSTRAINT CK_RepositoryProvisioningAttempts_Revisions CHECK
            (ExpectedBindingRevision > 0 AND ExpectedExecutionProfileRevision > 0),
        CONSTRAINT CK_RepositoryProvisioningAttempts_Paths CHECK
            (LEN(LTRIM(RTRIM(CanonicalTargetPath))) > 0 AND
             LEN(LTRIM(RTRIM(StagingPath))) > 0 AND
             CanonicalTargetPath <> StagingPath),
        CONSTRAINT CK_RepositoryProvisioningAttempts_State CHECK
            (State IN (N'Provisioning', N'Qualified', N'ProvisioningFailed')),
        CONSTRAINT CK_RepositoryProvisioningAttempts_Hashes CHECK
            (LEN(PlanHash)=64 AND LEN(DescriptorSha256)=64 AND
             LEN(TemplateBundleSha256)=64 AND LEN(PlanningBundleSha256)=64 AND
             PlanHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             DescriptorSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             TemplateBundleSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             PlanningBundleSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_RepositoryProvisioningAttempts_Failure CHECK
            ((State=N'ProvisioningFailed' AND FailureCode IS NOT NULL AND ISJSON(FailureEvidenceJson)=1)
             OR
             (State<>N'ProvisioningFailed' AND FailureCode IS NULL AND FailureEvidenceJson IS NULL)),
        CONSTRAINT CK_RepositoryProvisioningAttempts_Completion CHECK
            ((State=N'Provisioning' AND CompletedAtUtc IS NULL)
             OR (State<>N'Provisioning' AND CompletedAtUtc IS NOT NULL))
    );

    CREATE INDEX IX_RepositoryProvisioningAttempts_ProjectState
        ON dbo.RepositoryProvisioningAttempts(TenantId, ProjectId, State, StartedAtUtc DESC);
END;
GO

IF COL_LENGTH(N'dbo.RepositoryProvisioningAttempts', N'ClientOperationKind') IS NULL
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        ADD ClientOperationKind NVARCHAR(100) NULL;
GO

IF COL_LENGTH(N'dbo.RepositoryProvisioningAttempts', N'ClientOperationResourceScopeId') IS NULL
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        ADD ClientOperationResourceScopeId NVARCHAR(200) NULL;
GO

UPDATE dbo.RepositoryProvisioningAttempts
SET ClientOperationKind=N'ProvisionRepository',
    ClientOperationResourceScopeId=
        N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':repository-provisioning'
WHERE ClientOperationKind IS NULL OR ClientOperationResourceScopeId IS NULL;

IF EXISTS
    (SELECT 1 FROM dbo.RepositoryProvisioningAttempts
     WHERE ClientOperationKind IS NULL OR ClientOperationResourceScopeId IS NULL)
    THROW 51206, 'A provisioning attempt has no exact client-operation authority.', 1;
GO

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'ClientOperationKind' AND is_nullable=1
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        ALTER COLUMN ClientOperationKind NVARCHAR(100) NOT NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'ClientOperationResourceScopeId' AND is_nullable=1
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        ALTER COLUMN ClientOperationResourceScopeId NVARCHAR(200) NOT NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'FK_RepositoryProvisioningAttempts_ClientOperation'
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        DROP CONSTRAINT FK_RepositoryProvisioningAttempts_ClientOperation;
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'FK_RepositoryProvisioningAttempts_ClientOperationAuthority'
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        DROP CONSTRAINT FK_RepositoryProvisioningAttempts_ClientOperationAuthority;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'CK_RepositoryProvisioningAttempts_ClientOperationAuthority'
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts WITH CHECK
        ADD CONSTRAINT CK_RepositoryProvisioningAttempts_ClientOperationAuthority CHECK
            (ClientOperationKind=N'ProvisionRepository' AND
             ClientOperationResourceScopeId=
                 N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':repository-provisioning');
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'FK_RepositoryProvisioningAttempts_ClientOperationAuthority'
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts WITH CHECK
        ADD CONSTRAINT FK_RepositoryProvisioningAttempts_ClientOperationAuthority
        FOREIGN KEY
            (ClientOperationRecordId, TenantId, ProjectId, ClientOperationId, ActorUserId,
             ClientOperationKind, ClientOperationResourceScopeId, WorkbenchSessionId)
        REFERENCES dbo.ClientOperations
            (Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
             OperationKind, ResourceScopeId, ResultWorkbenchSessionId);
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'FK_RepositoryProvisioningAttempts_Confirmation'
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts
        DROP CONSTRAINT FK_RepositoryProvisioningAttempts_Confirmation;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'UX_RepositoryProvisioningAttempts_ReceiptAuthority'
)
    CREATE UNIQUE INDEX UX_RepositoryProvisioningAttempts_ReceiptAuthority
        ON dbo.RepositoryProvisioningAttempts
            (TenantId, ProjectId, Id, SetupConfirmationId, RepositoryBindingId,
             ProjectExecutionProfileId, PlanHash);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'UX_RepositoryProvisioningAttempts_OperationResultAuthority'
)
    CREATE UNIQUE INDEX UX_RepositoryProvisioningAttempts_OperationResultAuthority
        ON dbo.RepositoryProvisioningAttempts
            (ClientOperationRecordId, TenantId, ProjectId, Id, WorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningAttempts')
      AND name=N'FK_RepositoryProvisioningAttempts_ConfirmationAuthority'
)
    ALTER TABLE dbo.RepositoryProvisioningAttempts WITH CHECK
        ADD CONSTRAINT FK_RepositoryProvisioningAttempts_ConfirmationAuthority
        FOREIGN KEY
            (TenantId, ProjectId, SetupConfirmationId, RepositoryBindingId,
             ProjectExecutionProfileId, PlanHash)
        REFERENCES dbo.RepositorySetupConfirmations
            (TenantId, ProjectId, Id, RepositoryBindingId,
             ProjectExecutionProfileId, PlanHash);
GO

IF OBJECT_ID(N'dbo.RepositoryProvisioningReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepositoryProvisioningReceipts
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RepositoryProvisioningReceipts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProvisioningAttemptId UNIQUEIDENTIFIER NOT NULL,
        SetupConfirmationId UNIQUEIDENTIFIER NOT NULL,
        ActorUserId INT NOT NULL,
        BranchName NVARCHAR(255) NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        PlanHash CHAR(64) NOT NULL,
        ManifestSha256 CHAR(64) NOT NULL,
        GitTreeId CHAR(40) NOT NULL,
        ManifestJson NVARCHAR(MAX) NOT NULL,
        ReceiptJson NVARCHAR(MAX) NOT NULL,
        ReceiptSha256 CHAR(64) NOT NULL,
        PublishedAtUtc DATETIME2(7) NOT NULL,
        RecordedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_RepositoryProvisioningReceipts_Attempt UNIQUE
            (TenantId, ProjectId, ProvisioningAttemptId),
        CONSTRAINT FK_RepositoryProvisioningReceipts_AttemptAuthority
            FOREIGN KEY
                (TenantId, ProjectId, ProvisioningAttemptId, SetupConfirmationId,
                 RepositoryBindingId, ProjectExecutionProfileId, PlanHash)
            REFERENCES dbo.RepositoryProvisioningAttempts
                (TenantId, ProjectId, Id, SetupConfirmationId,
                 RepositoryBindingId, ProjectExecutionProfileId, PlanHash),
        CONSTRAINT FK_RepositoryProvisioningReceipts_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryProvisioningReceipts_Profile
            FOREIGN KEY (TenantId, ProjectId, ProjectExecutionProfileId)
            REFERENCES dbo.ProjectExecutionProfiles(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryProvisioningReceipts_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_RepositoryProvisioningReceipts_Hashes CHECK
            (LEN(BaselineCommit)=40 AND LEN(GitTreeId)=40 AND
             LEN(PlanHash)=64 AND LEN(ManifestSha256)=64 AND LEN(ReceiptSha256)=64 AND
             BaselineCommit COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             GitTreeId COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             PlanHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             ManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             ReceiptSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_RepositoryProvisioningReceipts_Json CHECK
            (ISJSON(ManifestJson)=1 AND ISJSON(ReceiptJson)=1),
        CONSTRAINT CK_RepositoryProvisioningReceipts_Branch CHECK (BranchName=N'main')
    );
END;
GO

IF COL_LENGTH(N'dbo.RepositoryProvisioningReceipts', N'SetupConfirmationId') IS NULL
    ALTER TABLE dbo.RepositoryProvisioningReceipts
        ADD SetupConfirmationId UNIQUEIDENTIFIER NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'SetupConfirmationId'
      AND is_nullable=1
)
BEGIN
    DECLARE @ReceiptAppendOnlyTriggerWasEnabled BIT = 0;
    IF EXISTS
    (
        SELECT 1 FROM sys.triggers
        WHERE parent_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
          AND name=N'TR_RepositoryProvisioningReceipts_AppendOnly'
          AND is_disabled=0
    )
    BEGIN
        SET @ReceiptAppendOnlyTriggerWasEnabled = 1;
        DISABLE TRIGGER dbo.TR_RepositoryProvisioningReceipts_AppendOnly
            ON dbo.RepositoryProvisioningReceipts;
    END;

    BEGIN TRY
        UPDATE receipt
        SET SetupConfirmationId=attempt.SetupConfirmationId
        FROM dbo.RepositoryProvisioningReceipts receipt
        INNER JOIN dbo.RepositoryProvisioningAttempts attempt
            ON attempt.TenantId=receipt.TenantId
           AND attempt.ProjectId=receipt.ProjectId
           AND attempt.Id=receipt.ProvisioningAttemptId
        WHERE receipt.SetupConfirmationId IS NULL;

        IF EXISTS
            (SELECT 1 FROM dbo.RepositoryProvisioningReceipts WHERE SetupConfirmationId IS NULL)
            THROW 51205, 'A provisioning receipt has no exact setup-confirmation authority.', 1;

        ALTER TABLE dbo.RepositoryProvisioningReceipts
            ALTER COLUMN SetupConfirmationId UNIQUEIDENTIFIER NOT NULL;
    END TRY
    BEGIN CATCH
        IF @ReceiptAppendOnlyTriggerWasEnabled=1
            ENABLE TRIGGER dbo.TR_RepositoryProvisioningReceipts_AppendOnly
                ON dbo.RepositoryProvisioningReceipts;
        THROW;
    END CATCH;

    IF @ReceiptAppendOnlyTriggerWasEnabled=1
        ENABLE TRIGGER dbo.TR_RepositoryProvisioningReceipts_AppendOnly
            ON dbo.RepositoryProvisioningReceipts;
END;
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'FK_RepositoryProvisioningReceipts_Attempt'
)
    ALTER TABLE dbo.RepositoryProvisioningReceipts
        DROP CONSTRAINT FK_RepositoryProvisioningReceipts_Attempt;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'FK_RepositoryProvisioningReceipts_AttemptAuthority'
)
    ALTER TABLE dbo.RepositoryProvisioningReceipts WITH CHECK
        ADD CONSTRAINT FK_RepositoryProvisioningReceipts_AttemptAuthority
        FOREIGN KEY
            (TenantId, ProjectId, ProvisioningAttemptId, SetupConfirmationId,
             RepositoryBindingId, ProjectExecutionProfileId, PlanHash)
        REFERENCES dbo.RepositoryProvisioningAttempts
            (TenantId, ProjectId, Id, SetupConfirmationId,
             RepositoryBindingId, ProjectExecutionProfileId, PlanHash);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'UX_RepositoryProvisioningReceipts_ProjectId'
)
    CREATE UNIQUE INDEX UX_RepositoryProvisioningReceipts_ProjectId
        ON dbo.RepositoryProvisioningReceipts(TenantId, ProjectId, Id);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'UX_RepositoryProvisioningReceipts_OperationResultAuthority'
)
    CREATE UNIQUE INDEX UX_RepositoryProvisioningReceipts_OperationResultAuthority
        ON dbo.RepositoryProvisioningReceipts
            (TenantId, ProjectId, ProvisioningAttemptId, Id);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_RepositoryProvisioningProjectOperation'
)
    CREATE UNIQUE INDEX UX_ClientOperations_RepositoryProvisioningProjectOperation
        ON dbo.ClientOperations(TenantId, OperationKind, ResourceScopeId, ClientOperationId)
        WHERE OperationKind=N'ProvisionRepository';
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultRepositoryProvisioningAttemptId') IS NULL
    ALTER TABLE dbo.ClientOperations ADD ResultRepositoryProvisioningAttemptId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultRepositoryProvisioningReceiptId') IS NULL
    ALTER TABLE dbo.ClientOperations ADD ResultRepositoryProvisioningReceiptId UNIQUEIDENTIFIER NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'CK_ClientOperations_RepositoryProvisioningResultAuthority'
)
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT CK_ClientOperations_RepositoryProvisioningResultAuthority;
GO

ALTER TABLE dbo.ClientOperations WITH CHECK
    ADD CONSTRAINT CK_ClientOperations_RepositoryProvisioningResultAuthority CHECK
        ((ResultRepositoryProvisioningAttemptId IS NULL AND
          ResultRepositoryProvisioningReceiptId IS NULL)
         OR
         (ResultProjectId IS NOT NULL AND
          ResultWorkbenchSessionId IS NOT NULL AND
          ResultRepositoryProvisioningAttemptId IS NOT NULL));
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_RepositoryProvisioningAttempt'
)
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningAttempt;
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_RepositoryProvisioningReceipt'
)
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningReceipt;
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_RepositoryProvisioningAttemptAuthority'
)
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningAttemptAuthority;
GO

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_RepositoryProvisioningReceiptAuthority'
)
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT FK_ClientOperations_RepositoryProvisioningReceiptAuthority;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_RepositoryProvisioningAttemptAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_RepositoryProvisioningAttemptAuthority
        FOREIGN KEY
            (Id, TenantId, ResultProjectId, ResultRepositoryProvisioningAttemptId,
             ResultWorkbenchSessionId)
        REFERENCES dbo.RepositoryProvisioningAttempts
            (ClientOperationRecordId, TenantId, ProjectId, Id, WorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_RepositoryProvisioningReceiptAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_RepositoryProvisioningReceiptAuthority
        FOREIGN KEY
            (TenantId, ResultProjectId, ResultRepositoryProvisioningAttemptId,
             ResultRepositoryProvisioningReceiptId)
        REFERENCES dbo.RepositoryProvisioningReceipts
            (TenantId, ProjectId, ProvisioningAttemptId, Id);
GO

IF OBJECT_ID(N'dbo.TR_RepositoryProvisioningReceipts_AppendOnly', N'TR') IS NULL
    EXEC(N'CREATE TRIGGER dbo.TR_RepositoryProvisioningReceipts_AppendOnly
      ON dbo.RepositoryProvisioningReceipts AFTER UPDATE, DELETE AS
      BEGIN SET NOCOUNT ON; THROW 51204, ''Repository provisioning receipts are append-only.'', 1; END;');
GO

IF EXISTS
(
    SELECT 1 FROM sys.triggers
    WHERE parent_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'TR_RepositoryProvisioningReceipts_AppendOnly'
      AND is_disabled=1
)
    ENABLE TRIGGER dbo.TR_RepositoryProvisioningReceipts_AppendOnly
        ON dbo.RepositoryProvisioningReceipts;
GO

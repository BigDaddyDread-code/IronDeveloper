/* Workbench v0.1 PR-05A: repository setup planning and explicit confirmation authority. */

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchWriteLeases')
      AND name=N'UX_WorkbenchWriteLeases_ExactFence'
)
    CREATE UNIQUE INDEX UX_WorkbenchWriteLeases_ExactFence
        ON dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch);
GO

IF OBJECT_ID(N'dbo.RepositoryBindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepositoryBindings
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RepositoryBindings PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        CurrentRevision BIGINT NOT NULL,
        RepositoryKind NVARCHAR(30) NOT NULL,
        CanonicalPath NVARCHAR(1000) NOT NULL,
        BindingState NVARCHAR(40) NOT NULL,
        DefaultBranch NVARCHAR(255) NULL,
        BaselineCommit CHAR(40) NULL,
        CreatedByActorUserId INT NULL,
        ConfirmedAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_RepositoryBindings_Project UNIQUE (TenantId, ProjectId),
        CONSTRAINT UQ_RepositoryBindings_ProjectId UNIQUE (TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryBindings_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_RepositoryBindings_Actor
            FOREIGN KEY (CreatedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_RepositoryBindings_Revision CHECK (CurrentRevision > 0),
        CONSTRAINT CK_RepositoryBindings_Kind CHECK
            (RepositoryKind IN (N'Greenfield', N'Existing')),
        CONSTRAINT CK_RepositoryBindings_State CHECK
            (BindingState IN
                (N'SetupConfirmed', N'Provisioning', N'Qualified',
                 N'ProvisioningFailed', N'LegacyUnverified')),
        CONSTRAINT CK_RepositoryBindings_Confirmation CHECK
        (
            (BindingState=N'LegacyUnverified')
            OR
            (CreatedByActorUserId IS NOT NULL AND ConfirmedAtUtc IS NOT NULL)
        ),
        CONSTRAINT CK_RepositoryBindings_Baseline CHECK
            (BaselineCommit IS NULL OR
             (DATALENGTH(BaselineCommit)=40
              AND BaselineCommit COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'))
    );
END;
GO

IF OBJECT_ID(N'dbo.RepositoryBindingRevisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepositoryBindingRevisions
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RepositoryBindingRevisions PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        Revision BIGINT NOT NULL,
        SnapshotJson NVARCHAR(MAX) NOT NULL,
        SnapshotHash CHAR(64) NOT NULL,
        ActorUserId INT NULL,
        ChangeKind NVARCHAR(40) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_RepositoryBindingRevisions_Revision UNIQUE
            (TenantId, ProjectId, RepositoryBindingId, Revision),
        CONSTRAINT FK_RepositoryBindingRevisions_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositoryBindingRevisions_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_RepositoryBindingRevisions_Revision CHECK (Revision > 0),
        CONSTRAINT CK_RepositoryBindingRevisions_Json CHECK (ISJSON(SnapshotJson)=1),
        CONSTRAINT CK_RepositoryBindingRevisions_Hash CHECK
            (LEN(SnapshotHash)=64
             AND SnapshotHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_RepositoryBindingRevisions_ChangeKind CHECK
            (ChangeKind IN
                (N'SetupConfirmed', N'LegacyBackfill', N'ProvisioningStarted',
                 N'Qualified', N'ProvisioningFailed'))
    );
END;
GO

IF OBJECT_ID(N'dbo.ProjectExecutionProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectExecutionProfiles
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProjectExecutionProfiles PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        CurrentRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        DescriptorSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        PlanningBundleSha256 CHAR(64) NOT NULL,
        TargetFramework NVARCHAR(100) NOT NULL,
        Language NVARCHAR(50) NOT NULL,
        ApplicationKind NVARCHAR(100) NOT NULL,
        TestFramework NVARCHAR(100) NOT NULL,
        SdkVersion NVARCHAR(50) NOT NULL,
        RuntimeVersion NVARCHAR(50) NOT NULL,
        SolutionPath NVARCHAR(500) NOT NULL,
        AppProjectPath NVARCHAR(500) NOT NULL,
        TestProjectPath NVARCHAR(500) NOT NULL,
        RestoreCommand NVARCHAR(1000) NOT NULL,
        BuildCommand NVARCHAR(1000) NOT NULL,
        TestCommand NVARCHAR(1000) NOT NULL,
        ToolchainManifestId NVARCHAR(200) NOT NULL,
        ExecutionImageReference NVARCHAR(500) NOT NULL,
        PlanningReadiness NVARCHAR(50) NOT NULL,
        CertificationState NVARCHAR(50) NOT NULL,
        CreatedByActorUserId INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_ProjectExecutionProfiles_Project UNIQUE (TenantId, ProjectId),
        CONSTRAINT UQ_ProjectExecutionProfiles_ProjectId UNIQUE (TenantId, ProjectId, Id),
        CONSTRAINT FK_ProjectExecutionProfiles_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectExecutionProfiles_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_ProjectExecutionProfiles_Actor
            FOREIGN KEY (CreatedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectExecutionProfiles_Revision CHECK (CurrentRevision > 0),
        CONSTRAINT CK_ProjectExecutionProfiles_DescriptorRevision CHECK (ProfileDescriptorRevision > 0),
        CONSTRAINT CK_ProjectExecutionProfiles_DescriptorHash CHECK
            (LEN(DescriptorSha256)=64
             AND DescriptorSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_ProjectExecutionProfiles_TemplateHash CHECK
            (LEN(TemplateBundleSha256)=64
             AND TemplateBundleSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_ProjectExecutionProfiles_PlanningHash CHECK
            (LEN(PlanningBundleSha256)=64
             AND PlanningBundleSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_ProjectExecutionProfiles_Readiness CHECK
            (PlanningReadiness=N'PreviewPlanningOnly'),
        CONSTRAINT CK_ProjectExecutionProfiles_Certification CHECK
            (CertificationState=N'NotCertificationReady')
    );
END;
GO

IF OBJECT_ID(N'dbo.ProjectExecutionProfileRevisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectExecutionProfileRevisions
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectExecutionProfileRevisions PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        Revision BIGINT NOT NULL,
        SnapshotJson NVARCHAR(MAX) NOT NULL,
        SnapshotHash CHAR(64) NOT NULL,
        ActorUserId INT NOT NULL,
        ChangeKind NVARCHAR(40) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_ProjectExecutionProfileRevisions_Revision UNIQUE
            (TenantId, ProjectId, ProjectExecutionProfileId, Revision),
        CONSTRAINT FK_ProjectExecutionProfileRevisions_Profile
            FOREIGN KEY (TenantId, ProjectId, ProjectExecutionProfileId)
            REFERENCES dbo.ProjectExecutionProfiles(TenantId, ProjectId, Id),
        CONSTRAINT FK_ProjectExecutionProfileRevisions_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectExecutionProfileRevisions_Revision CHECK (Revision > 0),
        CONSTRAINT CK_ProjectExecutionProfileRevisions_Json CHECK (ISJSON(SnapshotJson)=1),
        CONSTRAINT CK_ProjectExecutionProfileRevisions_Hash CHECK
            (LEN(SnapshotHash)=64
             AND SnapshotHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_ProjectExecutionProfileRevisions_ChangeKind CHECK
            (ChangeKind IN (N'SetupConfirmed', N'ProvisioningUpdated', N'Qualified'))
    );
END;
GO

IF OBJECT_ID(N'dbo.RepositorySetupConfirmations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepositorySetupConfirmations
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RepositorySetupConfirmations PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ActorUserId INT NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        PlanHash CHAR(64) NOT NULL,
        PlanJson NVARCHAR(MAX) NOT NULL,
        ConfirmedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_RepositorySetupConfirmations_ClientOperation UNIQUE
            (TenantId, ActorUserId, ClientOperationId),
        CONSTRAINT UQ_RepositorySetupConfirmations_Project UNIQUE (TenantId, ProjectId),
        CONSTRAINT FK_RepositorySetupConfirmations_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositorySetupConfirmations_Profile
            FOREIGN KEY (TenantId, ProjectId, ProjectExecutionProfileId)
            REFERENCES dbo.ProjectExecutionProfiles(TenantId, ProjectId, Id),
        CONSTRAINT FK_RepositorySetupConfirmations_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_RepositorySetupConfirmations_Fence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT CK_RepositorySetupConfirmations_Hash CHECK
            (LEN(PlanHash)=64
             AND PlanHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_RepositorySetupConfirmations_Json CHECK (ISJSON(PlanJson)=1)
    );
END;
GO

/* Existing LocalPath is compatibility evidence only. It never becomes qualified authority. */
IF EXISTS
(
    SELECT 1
    FROM dbo.Projects project
    WHERE NULLIF(LTRIM(RTRIM(project.LocalPath)), N'') IS NOT NULL
      AND NOT EXISTS
          (SELECT 1 FROM dbo.RepositoryBindings value
           WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id)
)
BEGIN
    INSERT dbo.RepositoryBindings
        (Id, TenantId, ProjectId, CurrentRevision, RepositoryKind, CanonicalPath,
         BindingState, DefaultBranch, BaselineCommit, CreatedByActorUserId,
         ConfirmedAtUtc, CreatedAtUtc, UpdatedAtUtc)
    SELECT NEWID(), project.TenantId, project.Id, 1, N'Existing',
           LTRIM(RTRIM(project.LocalPath)), N'LegacyUnverified', NULL, NULL,
           NULL, NULL, project.CreatedDate, SYSUTCDATETIME()
    FROM dbo.Projects project
    WHERE NULLIF(LTRIM(RTRIM(project.LocalPath)), N'') IS NOT NULL
      AND NOT EXISTS
          (SELECT 1 FROM dbo.RepositoryBindings value
           WHERE value.TenantId=project.TenantId AND value.ProjectId=project.Id);

END;
GO

/* Repair a partially applied legacy backfill on every rerun. */
INSERT dbo.RepositoryBindingRevisions
    (TenantId, ProjectId, RepositoryBindingId, Revision, SnapshotJson,
     SnapshotHash, ActorUserId, ChangeKind, CreatedAtUtc)
SELECT binding.TenantId, binding.ProjectId, binding.Id, 1,
       snapshot.SnapshotJson,
       LOWER(CONVERT(VARCHAR(64), HASHBYTES(
           'SHA2_256',
           CONVERT(VARBINARY(MAX),
               CONVERT(VARCHAR(MAX),
                   snapshot.SnapshotJson COLLATE Latin1_General_100_BIN2_UTF8))), 2)),
       binding.CreatedByActorUserId, N'LegacyBackfill', binding.CreatedAtUtc
FROM dbo.RepositoryBindings binding
CROSS APPLY
(
    SELECT
        binding.Id AS id,
        binding.ProjectId AS projectId,
        binding.CurrentRevision AS revision,
        binding.RepositoryKind AS repositoryKind,
        binding.CanonicalPath AS canonicalPath,
        binding.BindingState AS bindingState,
        binding.DefaultBranch AS defaultBranch,
        binding.BaselineCommit AS baselineCommit,
        binding.CreatedByActorUserId AS createdByActorUserId,
        binding.ConfirmedAtUtc AS confirmedAtUtc
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
) snapshot(SnapshotJson)
WHERE binding.BindingState=N'LegacyUnverified'
  AND NOT EXISTS
      (SELECT 1 FROM dbo.RepositoryBindingRevisions revision
       WHERE revision.TenantId=binding.TenantId
         AND revision.ProjectId=binding.ProjectId
         AND revision.RepositoryBindingId=binding.Id);
GO

IF OBJECT_ID(N'dbo.TR_RepositoryBindingRevisions_AppendOnly', N'TR') IS NULL
    EXEC(N'CREATE TRIGGER dbo.TR_RepositoryBindingRevisions_AppendOnly
      ON dbo.RepositoryBindingRevisions AFTER UPDATE, DELETE AS
      BEGIN SET NOCOUNT ON; THROW 51201, ''Repository binding revisions are append-only.'', 1; END;');
GO

IF OBJECT_ID(N'dbo.TR_ProjectExecutionProfileRevisions_AppendOnly', N'TR') IS NULL
    EXEC(N'CREATE TRIGGER dbo.TR_ProjectExecutionProfileRevisions_AppendOnly
      ON dbo.ProjectExecutionProfileRevisions AFTER UPDATE, DELETE AS
      BEGIN SET NOCOUNT ON; THROW 51202, ''Project execution profile revisions are append-only.'', 1; END;');
GO

IF OBJECT_ID(N'dbo.TR_RepositorySetupConfirmations_AppendOnly', N'TR') IS NULL
    EXEC(N'CREATE TRIGGER dbo.TR_RepositorySetupConfirmations_AppendOnly
      ON dbo.RepositorySetupConfirmations AFTER UPDATE, DELETE AS
      BEGIN SET NOCOUNT ON; THROW 51203, ''Repository setup confirmations are append-only.'', 1; END;');
GO

/* Workbench v0.1 PR-06A: production sandbox qualification attempts and evidence. */

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_SandboxQualificationAuthority'
)
    CREATE UNIQUE INDEX UX_ClientOperations_SandboxQualificationAuthority
        ON dbo.ClientOperations
            (Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
             OperationKind, ResourceScopeId, ResultWorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryProvisioningReceipts')
      AND name=N'UX_RepositoryProvisioningReceipts_SandboxSourceAuthority'
)
    CREATE UNIQUE INDEX UX_RepositoryProvisioningReceipts_SandboxSourceAuthority
        ON dbo.RepositoryProvisioningReceipts
            (TenantId, ProjectId, Id, RepositoryBindingId, ProjectExecutionProfileId,
             BaselineCommit, ManifestSha256, GitTreeId);
GO

IF OBJECT_ID(N'dbo.SandboxQualificationAttempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SandboxQualificationAttempts
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SandboxQualificationAttempts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        RepositoryProvisioningReceiptId UNIQUEIDENTIFIER NOT NULL,
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
        BaselineCommit CHAR(40) NOT NULL,
        SourceManifestSha256 CHAR(64) NOT NULL,
        SourceGitTreeId CHAR(40) NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        DescriptorSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        ToolchainManifestId NVARCHAR(200) NOT NULL,
        ContainerImageDigest NVARCHAR(500) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        SandboxPolicyVersion NVARCHAR(100) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        TrustedSupervisorVersion NVARCHAR(100) NOT NULL,
        TrustedSupervisorSha256 CHAR(64) NOT NULL,
        State NVARCHAR(40) NOT NULL,
        FailureCode NVARCHAR(100) NULL,
        FailureSummary NVARCHAR(1000) NULL,
        EvidenceManifestSha256 CHAR(64) NULL,
        CleanupConfirmed BIT NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        LastRecoveryAttemptAtUtc DATETIME2(7) NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CONSTRAINT UQ_SandboxQualificationAttempts_Number UNIQUE
            (TenantId, ProjectId, RepositoryBindingId, AttemptNumber),
        CONSTRAINT UQ_SandboxQualificationAttempts_ClientOperationActor UNIQUE
            (TenantId, ProjectId, ActorUserId, ClientOperationId),
        CONSTRAINT UQ_SandboxQualificationAttempts_ClientOperationRecord UNIQUE
            (ClientOperationRecordId),
        CONSTRAINT UQ_SandboxQualificationAttempts_ProjectId UNIQUE
            (TenantId, ProjectId, Id),
        CONSTRAINT UQ_SandboxQualificationAttempts_OperationAuthority UNIQUE
            (ClientOperationRecordId, TenantId, ProjectId, Id, WorkbenchSessionId),
        CONSTRAINT FK_SandboxQualificationAttempts_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_SandboxQualificationAttempts_BindingRevision
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId, ExpectedBindingRevision)
            REFERENCES dbo.RepositoryBindingRevisions
                (TenantId, ProjectId, RepositoryBindingId, Revision),
        CONSTRAINT FK_SandboxQualificationAttempts_Profile
            FOREIGN KEY (TenantId, ProjectId, ProjectExecutionProfileId)
            REFERENCES dbo.ProjectExecutionProfiles(TenantId, ProjectId, Id),
        CONSTRAINT FK_SandboxQualificationAttempts_ProfileRevision
            FOREIGN KEY
                (TenantId, ProjectId, ProjectExecutionProfileId,
                 ExpectedExecutionProfileRevision)
            REFERENCES dbo.ProjectExecutionProfileRevisions
                (TenantId, ProjectId, ProjectExecutionProfileId, Revision),
        CONSTRAINT FK_SandboxQualificationAttempts_SourceAuthority
            FOREIGN KEY
                (TenantId, ProjectId, RepositoryProvisioningReceiptId,
                 RepositoryBindingId, ProjectExecutionProfileId, BaselineCommit,
                 SourceManifestSha256, SourceGitTreeId)
            REFERENCES dbo.RepositoryProvisioningReceipts
                (TenantId, ProjectId, Id, RepositoryBindingId, ProjectExecutionProfileId,
                 BaselineCommit, ManifestSha256, GitTreeId),
        CONSTRAINT FK_SandboxQualificationAttempts_ClientOperationAuthority
            FOREIGN KEY
                (ClientOperationRecordId, TenantId, ProjectId, ClientOperationId, ActorUserId,
                 ClientOperationKind, ClientOperationResourceScopeId, WorkbenchSessionId)
            REFERENCES dbo.ClientOperations
                (Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
                 OperationKind, ResourceScopeId, ResultWorkbenchSessionId),
        CONSTRAINT FK_SandboxQualificationAttempts_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_SandboxQualificationAttempts_Fence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT CK_SandboxQualificationAttempts_AttemptNumber CHECK (AttemptNumber > 0),
        CONSTRAINT CK_SandboxQualificationAttempts_ClientOperationAuthority CHECK
            (ClientOperationKind=N'QualifyProductionSandbox' AND
             ClientOperationResourceScopeId=
                 N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':sandbox-qualification'),
        CONSTRAINT CK_SandboxQualificationAttempts_Revisions CHECK
            (ExpectedBindingRevision > 0 AND ExpectedExecutionProfileRevision > 0 AND
             ProfileDescriptorRevision > 0),
        CONSTRAINT CK_SandboxQualificationAttempts_Baseline CHECK
            (DATALENGTH(BaselineCommit)=40 AND DATALENGTH(SourceGitTreeId)=40 AND
             BaselineCommit COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             SourceGitTreeId COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_SandboxQualificationAttempts_Hashes CHECK
            (LEN(SourceManifestSha256)=64 AND LEN(DescriptorSha256)=64 AND LEN(TemplateBundleSha256)=64 AND
             LEN(OfflineFeedManifestSha256)=64 AND LEN(SandboxPolicySha256)=64 AND
             SourceManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             DescriptorSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             TemplateBundleSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             OfflineFeedManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             SandboxPolicySha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
             (EvidenceManifestSha256 IS NULL OR
              (LEN(EvidenceManifestSha256)=64 AND
               EvidenceManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'))),
        CONSTRAINT CK_SandboxQualificationAttempts_ImageDigest CHECK
            (ContainerImageDigest LIKE '%@sha256:%' AND
             LEN(ContainerImageDigest) > LEN('@sha256:') + 64),
        CONSTRAINT CK_SandboxQualificationAttempts_State CHECK
            (State IN (N'Running', N'Passed', N'Failed', N'Cancelled', N'Recovered')),
        CONSTRAINT CK_SandboxQualificationAttempts_Completion CHECK
        (
            (State=N'Running' AND CompletedAtUtc IS NULL AND FailureCode IS NULL AND
             FailureSummary IS NULL AND EvidenceManifestSha256 IS NULL AND
             CleanupConfirmed IS NULL)
            OR
            (State=N'Passed' AND CompletedAtUtc IS NOT NULL AND FailureCode IS NULL AND
             FailureSummary IS NULL AND EvidenceManifestSha256 IS NOT NULL AND
             CleanupConfirmed=1)
            OR
            (State IN (N'Failed', N'Cancelled', N'Recovered') AND
              CompletedAtUtc IS NOT NULL AND FailureCode IS NOT NULL AND
              LEN(LTRIM(RTRIM(FailureSummary))) > 0 AND CleanupConfirmed=1)
        )
    );

    CREATE INDEX IX_SandboxQualificationAttempts_ProjectState
        ON dbo.SandboxQualificationAttempts
            (TenantId, ProjectId, State, StartedAtUtc DESC);
END;
GO

IF COL_LENGTH(N'dbo.SandboxQualificationAttempts', N'TrustedSupervisorVersion') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.SandboxQualificationAttempts)
        THROW 51209, 'Existing sandbox attempts cannot be assigned an inferred trusted-supervisor authority.', 1;
    ALTER TABLE dbo.SandboxQualificationAttempts
        ADD TrustedSupervisorVersion NVARCHAR(100) NOT NULL;
END;
GO

IF COL_LENGTH(N'dbo.SandboxQualificationAttempts', N'TrustedSupervisorSha256') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.SandboxQualificationAttempts)
        THROW 51210, 'Existing sandbox attempts cannot be assigned an inferred trusted-supervisor hash.', 1;
    ALTER TABLE dbo.SandboxQualificationAttempts
        ADD TrustedSupervisorSha256 CHAR(64) NOT NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'CK_SandboxQualificationAttempts_TrustedSupervisorVersion'
)
    ALTER TABLE dbo.SandboxQualificationAttempts WITH CHECK
        ADD CONSTRAINT CK_SandboxQualificationAttempts_TrustedSupervisorVersion CHECK
            (LEN(LTRIM(RTRIM(TrustedSupervisorVersion))) > 0 AND
             DATALENGTH(TrustedSupervisorVersion)=
                 DATALENGTH(LTRIM(RTRIM(TrustedSupervisorVersion))));
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'CK_SandboxQualificationAttempts_TrustedSupervisorSha256'
)
    ALTER TABLE dbo.SandboxQualificationAttempts WITH CHECK
        ADD CONSTRAINT CK_SandboxQualificationAttempts_TrustedSupervisorSha256 CHECK
            (LEN(TrustedSupervisorSha256)=64 AND
             TrustedSupervisorSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%');
GO

IF COL_LENGTH(N'dbo.SandboxQualificationAttempts', N'LastRecoveryAttemptAtUtc') IS NULL
    ALTER TABLE dbo.SandboxQualificationAttempts
        ADD LastRecoveryAttemptAtUtc DATETIME2(7) NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'UX_SandboxQualificationAttempts_ManifestAuthority'
)
    CREATE UNIQUE INDEX UX_SandboxQualificationAttempts_ManifestAuthority
        ON dbo.SandboxQualificationAttempts
            (TenantId, ProjectId, Id, RepositoryBindingId, ExpectedBindingRevision,
             ProjectExecutionProfileId, ExpectedExecutionProfileRevision,
             ActorUserId, EvidenceManifestSha256);
GO

IF EXISTS
(
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'UQ_SandboxQualificationAttempts_ClientOperation'
)
    ALTER TABLE dbo.SandboxQualificationAttempts
        DROP CONSTRAINT UQ_SandboxQualificationAttempts_ClientOperation;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'UQ_SandboxQualificationAttempts_ClientOperationActor'
)
    ALTER TABLE dbo.SandboxQualificationAttempts
        ADD CONSTRAINT UQ_SandboxQualificationAttempts_ClientOperationActor UNIQUE
            (TenantId, ProjectId, ActorUserId, ClientOperationId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'UX_SandboxQualificationAttempts_OneRunningPerProject'
)
    CREATE UNIQUE INDEX UX_SandboxQualificationAttempts_OneRunningPerProject
        ON dbo.SandboxQualificationAttempts(TenantId, ProjectId)
        WHERE State=N'Running';
GO

IF OBJECT_ID(N'dbo.SandboxEvidenceManifests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SandboxEvidenceManifests
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SandboxEvidenceManifests PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ActorUserId INT NOT NULL,
        SchemaVersion INT NOT NULL,
        Passed BIT NOT NULL,
        ManifestJson NVARCHAR(MAX) NOT NULL,
        ManifestSha256 CHAR(64) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_SandboxEvidenceManifests_Attempt UNIQUE
            (TenantId, ProjectId, SandboxQualificationAttemptId),
        CONSTRAINT UQ_SandboxEvidenceManifests_AttemptId UNIQUE
            (TenantId, ProjectId, SandboxQualificationAttemptId, Id),
        CONSTRAINT FK_SandboxEvidenceManifests_AttemptAuthority
            FOREIGN KEY
                (TenantId, ProjectId, SandboxQualificationAttemptId,
                 RepositoryBindingId, RepositoryBindingRevision,
                 ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                 ActorUserId, ManifestSha256)
            REFERENCES dbo.SandboxQualificationAttempts
                (TenantId, ProjectId, Id, RepositoryBindingId, ExpectedBindingRevision,
                 ProjectExecutionProfileId, ExpectedExecutionProfileRevision,
                 ActorUserId, EvidenceManifestSha256),
        CONSTRAINT CK_SandboxEvidenceManifests_Schema CHECK (SchemaVersion=1),
        CONSTRAINT CK_SandboxEvidenceManifests_Json CHECK (ISJSON(ManifestJson)=1),
        CONSTRAINT CK_SandboxEvidenceManifests_Hash CHECK
            (LEN(ManifestSha256)=64 AND
             ManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')
    );
END;
GO

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_SandboxQualificationProjectOperation'
)
    DROP INDEX UX_ClientOperations_SandboxQualificationProjectOperation
        ON dbo.ClientOperations;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_SandboxQualificationProjectOperation'
)
    CREATE UNIQUE INDEX UX_ClientOperations_SandboxQualificationProjectOperation
        ON dbo.ClientOperations
            (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId)
        WHERE OperationKind=N'QualifyProductionSandbox';
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultSandboxQualificationAttemptId') IS NULL
    ALTER TABLE dbo.ClientOperations ADD ResultSandboxQualificationAttemptId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultSandboxEvidenceManifestId') IS NULL
    ALTER TABLE dbo.ClientOperations ADD ResultSandboxEvidenceManifestId UNIQUEIDENTIFIER NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'CK_ClientOperations_SandboxQualificationResultAuthority'
)
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT CK_ClientOperations_SandboxQualificationResultAuthority;
GO

ALTER TABLE dbo.ClientOperations WITH CHECK
    ADD CONSTRAINT CK_ClientOperations_SandboxQualificationResultAuthority CHECK
    (
        (ResultSandboxQualificationAttemptId IS NULL AND
         ResultSandboxEvidenceManifestId IS NULL)
        OR
        (ResultProjectId IS NOT NULL AND ResultWorkbenchSessionId IS NOT NULL AND
         ResultSandboxQualificationAttemptId IS NOT NULL)
    );
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_SandboxQualificationAttemptAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_SandboxQualificationAttemptAuthority
        FOREIGN KEY
            (Id, TenantId, ResultProjectId, ResultSandboxQualificationAttemptId,
             ResultWorkbenchSessionId)
        REFERENCES dbo.SandboxQualificationAttempts
            (ClientOperationRecordId, TenantId, ProjectId, Id, WorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_SandboxEvidenceManifestAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_SandboxEvidenceManifestAuthority
        FOREIGN KEY
            (TenantId, ResultProjectId, ResultSandboxQualificationAttemptId,
             ResultSandboxEvidenceManifestId)
        REFERENCES dbo.SandboxEvidenceManifests
            (TenantId, ProjectId, SandboxQualificationAttemptId, Id);
GO

IF OBJECT_ID(N'dbo.TR_SandboxEvidenceManifests_AppendOnly', N'TR') IS NULL
    EXEC(N'CREATE TRIGGER dbo.TR_SandboxEvidenceManifests_AppendOnly
      ON dbo.SandboxEvidenceManifests AFTER UPDATE, DELETE AS
      BEGIN SET NOCOUNT ON; THROW 51206, ''Sandbox evidence manifests are append-only.'', 1; END;');
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_SandboxQualificationAttempts_TerminalImmutable
  ON dbo.SandboxQualificationAttempts AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(Id) OR UPDATE(TenantId) OR UPDATE(ProjectId) OR
       UPDATE(RepositoryBindingId) OR UPDATE(ProjectExecutionProfileId) OR
       UPDATE(RepositoryProvisioningReceiptId) OR
       UPDATE(ClientOperationRecordId) OR UPDATE(ClientOperationId) OR
       UPDATE(ActorUserId) OR UPDATE(ClientOperationKind) OR
       UPDATE(ClientOperationResourceScopeId) OR UPDATE(WorkbenchSessionId) OR
       UPDATE(LeaseEpoch) OR UPDATE(AttemptNumber) OR
       UPDATE(ExpectedBindingRevision) OR UPDATE(ExpectedExecutionProfileRevision) OR
       UPDATE(BaselineCommit) OR UPDATE(SourceManifestSha256) OR
       UPDATE(SourceGitTreeId) OR UPDATE(ProfileDefinitionId) OR
       UPDATE(ProfileDescriptorRevision) OR UPDATE(DescriptorSha256) OR
       UPDATE(TemplateBundleSha256) OR UPDATE(ToolchainManifestId) OR
       UPDATE(ContainerImageDigest) OR UPDATE(OfflineFeedManifestSha256) OR
       UPDATE(SandboxPolicyVersion) OR UPDATE(SandboxPolicySha256) OR
       UPDATE(TrustedSupervisorVersion) OR UPDATE(TrustedSupervisorSha256) OR
       UPDATE(StartedAtUtc)
      THROW 51207, ''Sandbox qualification attempt authority is immutable.'', 1;
    IF EXISTS
    (
      SELECT 1
      FROM deleted d
      LEFT JOIN inserted i ON i.Id=d.Id
      WHERE i.Id IS NULL OR d.State<>N''Running'' OR
            (i.State=N''Running'' AND
             (i.LastRecoveryAttemptAtUtc IS NULL OR
              (d.LastRecoveryAttemptAtUtc IS NOT NULL AND
               i.LastRecoveryAttemptAtUtc<=d.LastRecoveryAttemptAtUtc)))
    )
      THROW 51208, ''Sandbox qualification attempts allow only advancing recovery scheduling or one Running-to-terminal transition.'', 1;
END;');
GO

IF EXISTS
(
    SELECT 1 FROM sys.triggers
    WHERE parent_id=OBJECT_ID(N'dbo.SandboxEvidenceManifests')
      AND name=N'TR_SandboxEvidenceManifests_AppendOnly'
      AND is_disabled=1
)
    ENABLE TRIGGER dbo.TR_SandboxEvidenceManifests_AppendOnly
        ON dbo.SandboxEvidenceManifests;
GO

IF EXISTS
(
    SELECT 1 FROM sys.triggers
    WHERE parent_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'TR_SandboxQualificationAttempts_TerminalImmutable'
      AND is_disabled=1
)
    ENABLE TRIGGER dbo.TR_SandboxQualificationAttempts_TerminalImmutable
        ON dbo.SandboxQualificationAttempts;
GO

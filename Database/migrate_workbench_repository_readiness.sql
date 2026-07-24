/* Workbench v0.1 PR-06B: mechanically current repository validation, indexing, and readiness evidence. */

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_TechnicalValidationAuthority'
)
    CREATE UNIQUE INDEX UX_ClientOperations_TechnicalValidationAuthority
        ON dbo.ClientOperations
            (Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
             OperationKind, ResourceScopeId, ResultWorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.SandboxEvidenceManifests')
      AND name=N'UX_SandboxEvidenceManifests_ExactAuthority'
)
    CREATE UNIQUE INDEX UX_SandboxEvidenceManifests_ExactAuthority
        ON dbo.SandboxEvidenceManifests
            (TenantId, ProjectId, SandboxQualificationAttemptId, Id, ManifestSha256);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.SandboxQualificationAttempts')
      AND name=N'UX_SandboxQualificationAttempts_TechnicalSourceAuthority'
)
    CREATE UNIQUE INDEX UX_SandboxQualificationAttempts_TechnicalSourceAuthority
        ON dbo.SandboxQualificationAttempts
        (
            TenantId, ProjectId, Id, RepositoryBindingId, ExpectedBindingRevision,
            ProjectExecutionProfileId, ExpectedExecutionProfileRevision,
            ProfileDefinitionId, ProfileDescriptorRevision, DescriptorSha256,
            EvidenceManifestSha256
        );
GO

IF OBJECT_ID(N'dbo.TechnicalValidationAttempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TechnicalValidationAttempts
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TechnicalValidationAttempts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ClientOperationRecordId BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        ActorUserId INT NOT NULL,
        ClientOperationKind NVARCHAR(100) NOT NULL,
        ClientOperationResourceScopeId NVARCHAR(200) NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        AttemptNumber INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestId NVARCHAR(200) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigest NVARCHAR(500) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicyVersion NVARCHAR(100) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        State NVARCHAR(30) NOT NULL,
        FailureCode NVARCHAR(100) NULL,
        FailureSummary NVARCHAR(1000) NULL,
        EvidenceSha256 CHAR(64) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CONSTRAINT UQ_TechnicalValidationAttempts_Number UNIQUE
            (TenantId, ProjectId, RepositoryBindingId, AttemptNumber),
        CONSTRAINT UQ_TechnicalValidationAttempts_ClientOperation UNIQUE
            (TenantId, ProjectId, ActorUserId, ClientOperationId),
        CONSTRAINT UQ_TechnicalValidationAttempts_ClientOperationRecord UNIQUE
            (ClientOperationRecordId),
        CONSTRAINT UQ_TechnicalValidationAttempts_ProjectId UNIQUE
            (TenantId, ProjectId, Id),
        CONSTRAINT UQ_TechnicalValidationAttempts_OperationAuthority UNIQUE
            (ClientOperationRecordId, TenantId, ProjectId, Id, WorkbenchSessionId),
        CONSTRAINT UQ_TechnicalValidationAttempts_Authority UNIQUE
        (
            TenantId, ProjectId, Id,
            RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
            ProjectExecutionProfileId, ProjectExecutionProfileRevision,
            ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
            RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
            ToolchainManifestSha256, ContainerImageDigestSha256,
            SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
            SandboxQualificationAttemptId, SandboxEvidenceManifestId,
            SandboxEvidenceManifestSha256
        ),
        CONSTRAINT FK_TechnicalValidationAttempts_Binding
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId)
            REFERENCES dbo.RepositoryBindings(TenantId, ProjectId, Id),
        CONSTRAINT FK_TechnicalValidationAttempts_BindingRevision
            FOREIGN KEY (TenantId, ProjectId, RepositoryBindingId, RepositoryBindingRevision)
            REFERENCES dbo.RepositoryBindingRevisions
                (TenantId, ProjectId, RepositoryBindingId, Revision),
        CONSTRAINT FK_TechnicalValidationAttempts_Profile
            FOREIGN KEY (TenantId, ProjectId, ProjectExecutionProfileId)
            REFERENCES dbo.ProjectExecutionProfiles(TenantId, ProjectId, Id),
        CONSTRAINT FK_TechnicalValidationAttempts_ProfileRevision
            FOREIGN KEY
                (TenantId, ProjectId, ProjectExecutionProfileId,
                 ProjectExecutionProfileRevision)
            REFERENCES dbo.ProjectExecutionProfileRevisions
                (TenantId, ProjectId, ProjectExecutionProfileId, Revision),
        CONSTRAINT FK_TechnicalValidationAttempts_SandboxAttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, SandboxQualificationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.SandboxQualificationAttempts
            (
                TenantId, ProjectId, Id, RepositoryBindingId, ExpectedBindingRevision,
                ProjectExecutionProfileId, ExpectedExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, DescriptorSha256,
                EvidenceManifestSha256
            ),
        CONSTRAINT FK_TechnicalValidationAttempts_SandboxEvidenceAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, SandboxQualificationAttemptId,
                SandboxEvidenceManifestId, SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.SandboxEvidenceManifests
            (
                TenantId, ProjectId, SandboxQualificationAttemptId, Id, ManifestSha256
            ),
        CONSTRAINT FK_TechnicalValidationAttempts_ClientOperationAuthority
            FOREIGN KEY
            (
                ClientOperationRecordId, TenantId, ProjectId, ClientOperationId,
                ActorUserId, ClientOperationKind, ClientOperationResourceScopeId,
                WorkbenchSessionId
            )
            REFERENCES dbo.ClientOperations
            (
                Id, TenantId, ResultProjectId, ClientOperationId, ActorUserId,
                OperationKind, ResourceScopeId, ResultWorkbenchSessionId
            ),
        CONSTRAINT FK_TechnicalValidationAttempts_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_TechnicalValidationAttempts_Fence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT CK_TechnicalValidationAttempts_AttemptNumber CHECK (AttemptNumber > 0),
        CONSTRAINT CK_TechnicalValidationAttempts_Revisions CHECK
            (RepositoryBindingRevision > 0 AND ProjectExecutionProfileRevision > 0 AND
             ProfileDescriptorRevision > 0),
        CONSTRAINT CK_TechnicalValidationAttempts_Operation CHECK
        (
            ClientOperationKind=N'ValidateRepositoryTechnicalReadiness' AND
            ClientOperationResourceScopeId=
                N'project:' + CONVERT(NVARCHAR(20), ProjectId) + N':technical-readiness'
        ),
        CONSTRAINT CK_TechnicalValidationAttempts_Baseline CHECK
        (
            DATALENGTH(BaselineCommit)=40 AND
            BaselineCommit COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        ),
        CONSTRAINT CK_TechnicalValidationAttempts_Hashes CHECK
        (
            LEN(RestoreCommandSha256)=64 AND
            LEN(BuildCommandSha256)=64 AND
            LEN(TestCommandSha256)=64 AND
            LEN(ProfileDescriptorSha256)=64 AND
            LEN(ToolchainManifestSha256)=64 AND
            LEN(ContainerImageDigestSha256)=64 AND
            LEN(SandboxPolicySha256)=64 AND
            LEN(OfflineFeedManifestSha256)=64 AND
            LEN(TemplateBundleSha256)=64 AND
            LEN(SandboxEvidenceManifestSha256)=64 AND
            RestoreCommandSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            BuildCommandSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            TestCommandSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            ProfileDescriptorSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            ToolchainManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            ContainerImageDigestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            SandboxPolicySha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            OfflineFeedManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            TemplateBundleSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            SandboxEvidenceManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            (EvidenceSha256 IS NULL OR
             (LEN(EvidenceSha256)=64 AND
              EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'))
        ),
        CONSTRAINT CK_TechnicalValidationAttempts_Strings CHECK
        (
            LEN(LTRIM(RTRIM(ToolchainManifestId))) > 0 AND
            LEN(LTRIM(RTRIM(ProfileDefinitionId))) > 0 AND
            LEN(LTRIM(RTRIM(SandboxPolicyVersion))) > 0 AND
            ContainerImageDigest LIKE '%@sha256:%' AND
            RIGHT(ContainerImageDigest, 64)=ContainerImageDigestSha256
        ),
        CONSTRAINT CK_TechnicalValidationAttempts_State CHECK
            (State IN (N'Running', N'Passed', N'Failed', N'Cancelled', N'Recovered')),
        CONSTRAINT CK_TechnicalValidationAttempts_Completion CHECK
        (
            (State=N'Running' AND CompletedAtUtc IS NULL AND FailureCode IS NULL AND
             FailureSummary IS NULL AND EvidenceSha256 IS NULL)
            OR
            (State=N'Passed' AND CompletedAtUtc IS NOT NULL AND
             CompletedAtUtc >= StartedAtUtc AND FailureCode IS NULL AND
             FailureSummary IS NULL AND EvidenceSha256 IS NOT NULL)
            OR
            (State IN (N'Failed', N'Cancelled', N'Recovered') AND
             CompletedAtUtc IS NOT NULL AND CompletedAtUtc >= StartedAtUtc AND
             LEN(LTRIM(RTRIM(FailureCode))) > 0 AND
             LEN(LTRIM(RTRIM(FailureSummary))) > 0 AND EvidenceSha256 IS NOT NULL)
        )
    );

    CREATE UNIQUE INDEX UX_TechnicalValidationAttempts_OneRunningPerProject
        ON dbo.TechnicalValidationAttempts(TenantId, ProjectId)
        WHERE State=N'Running';

    CREATE INDEX IX_TechnicalValidationAttempts_ProjectState
        ON dbo.TechnicalValidationAttempts
            (TenantId, ProjectId, State, StartedAtUtc DESC);
END;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_TechnicalValidationAttempts_ValidateAuthority
  ON dbo.TechnicalValidationAttempts AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        LEFT JOIN deleted prior ON prior.Id=value.Id
        INNER JOIN dbo.SandboxQualificationAttempts sandbox
            ON sandbox.TenantId=value.TenantId
           AND sandbox.ProjectId=value.ProjectId
           AND sandbox.Id=value.SandboxQualificationAttemptId
        INNER JOIN dbo.SandboxEvidenceManifests manifest
            ON manifest.TenantId=value.TenantId
           AND manifest.ProjectId=value.ProjectId
           AND manifest.SandboxQualificationAttemptId=sandbox.Id
           AND manifest.Id=value.SandboxEvidenceManifestId
        INNER JOIN dbo.ProjectExecutionProfiles profile
            ON profile.TenantId=value.TenantId
           AND profile.ProjectId=value.ProjectId
           AND profile.Id=value.ProjectExecutionProfileId
        WHERE prior.Id IS NULL AND
             (sandbox.State<>N''Passed'' OR sandbox.CleanupConfirmed<>1 OR
              manifest.Passed<>1 OR
              sandbox.BaselineCommit<>value.BaselineCommit OR
              sandbox.TemplateBundleSha256<>value.TemplateBundleSha256 OR
              sandbox.ToolchainManifestId<>value.ToolchainManifestId OR
              sandbox.ContainerImageDigest<>value.ContainerImageDigest OR
              sandbox.OfflineFeedManifestSha256<>value.OfflineFeedManifestSha256 OR
              sandbox.SandboxPolicyVersion<>value.SandboxPolicyVersion OR
              sandbox.SandboxPolicySha256<>value.SandboxPolicySha256 OR
              sandbox.ProfileDefinitionId<>value.ProfileDefinitionId OR
              sandbox.ProfileDescriptorRevision<>value.ProfileDescriptorRevision OR
              sandbox.DescriptorSha256<>value.ProfileDescriptorSha256 OR
              profile.RepositoryBindingId<>value.RepositoryBindingId OR
              profile.ProfileDefinitionId<>value.ProfileDefinitionId OR
              profile.ProfileDescriptorRevision<>value.ProfileDescriptorRevision OR
              profile.DescriptorSha256<>value.ProfileDescriptorSha256 OR
              profile.ToolchainManifestId<>value.ToolchainManifestId OR
              profile.TemplateBundleSha256<>value.TemplateBundleSha256 OR
              profile.CurrentRevision<>value.ProjectExecutionProfileRevision OR
              RIGHT(value.ContainerImageDigest, 64)<>value.ContainerImageDigestSha256)
    )
        THROW 51300, ''Technical validation authority does not match the passed sandbox, evidence manifest, or current execution profile.'', 1;
END;');
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_TechnicalValidationAttempts_TerminalImmutable
  ON dbo.TechnicalValidationAttempts AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
        THROW 51301, ''Technical validation attempts cannot be deleted.'', 1;
    IF UPDATE(Id) OR UPDATE(TenantId) OR UPDATE(ProjectId) OR
       UPDATE(ClientOperationRecordId) OR UPDATE(ClientOperationId) OR
       UPDATE(ActorUserId) OR UPDATE(ClientOperationKind) OR
       UPDATE(ClientOperationResourceScopeId) OR UPDATE(WorkbenchSessionId) OR
       UPDATE(LeaseEpoch) OR UPDATE(AttemptNumber) OR UPDATE(RepositoryBindingId) OR
       UPDATE(RepositoryBindingRevision) OR UPDATE(BaselineCommit) OR
       UPDATE(ProjectExecutionProfileId) OR UPDATE(ProjectExecutionProfileRevision) OR
       UPDATE(ProfileDefinitionId) OR UPDATE(ProfileDescriptorRevision) OR
       UPDATE(ProfileDescriptorSha256) OR
       UPDATE(RestoreCommandSha256) OR UPDATE(BuildCommandSha256) OR
       UPDATE(TestCommandSha256) OR UPDATE(ToolchainManifestId) OR
       UPDATE(ToolchainManifestSha256) OR UPDATE(ContainerImageDigest) OR
       UPDATE(ContainerImageDigestSha256) OR UPDATE(SandboxPolicyVersion) OR
       UPDATE(SandboxPolicySha256) OR UPDATE(OfflineFeedManifestSha256) OR
       UPDATE(TemplateBundleSha256) OR UPDATE(SandboxQualificationAttemptId) OR
       UPDATE(SandboxEvidenceManifestId) OR UPDATE(SandboxEvidenceManifestSha256) OR
       UPDATE(StartedAtUtc)
        THROW 51302, ''Technical validation attempt authority is immutable.'', 1;
    IF EXISTS
    (
        SELECT 1
        FROM deleted d
        INNER JOIN inserted i ON i.Id=d.Id
        WHERE d.State<>N''Running'' OR i.State=N''Running''
    )
        THROW 51303, ''Only a single Running-to-terminal technical validation transition is allowed.'', 1;
END;');
GO

/* Each evidence row repeats the exact attempt tuple. This deliberately favors auditable authority over normalization. */
IF OBJECT_ID(N'dbo.RepositoryStateObservations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepositoryStateObservations
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RepositoryStateObservations PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TechnicalValidationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        RepositoryFingerprintSha256 CHAR(64) NOT NULL,
        HeadCommit CHAR(40) NOT NULL,
        GitTreeId CHAR(40) NOT NULL,
        DirtyState NVARCHAR(20) NOT NULL,
        ObservedAtUtc DATETIME2(7) NOT NULL,
        EvidenceSha256 CHAR(64) NOT NULL,
        CONSTRAINT UQ_RepositoryStateObservations_Attempt UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId),
        CONSTRAINT UQ_RepositoryStateObservations_AttemptId UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id),
        CONSTRAINT UQ_RepositoryStateObservations_FingerprintAuthority UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id,
             RepositoryFingerprintSha256),
        CONSTRAINT UQ_RepositoryStateObservations_EvidenceReference UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id,
             RepositoryFingerprintSha256, EvidenceSha256),
        CONSTRAINT FK_RepositoryStateObservations_AttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, TechnicalValidationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.TechnicalValidationAttempts
            (
                TenantId, ProjectId, Id,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            ),
        CONSTRAINT CK_RepositoryStateObservations_Hashes CHECK
        (
            RepositoryFingerprintSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            LEN(RepositoryFingerprintSha256)=64 AND LEN(EvidenceSha256)=64
        ),
        CONSTRAINT CK_RepositoryStateObservations_Commits CHECK
        (
            DATALENGTH(HeadCommit)=40 AND DATALENGTH(GitTreeId)=40 AND
            HeadCommit COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            GitTreeId COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        ),
        CONSTRAINT CK_RepositoryStateObservations_DirtyState CHECK
            (DirtyState IN (N'Clean', N'Dirty', N'Unknown'))
    );
END;
GO

IF OBJECT_ID(N'dbo.BuildValidationRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuildValidationRecords
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BuildValidationRecords PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TechnicalValidationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL,
        RepositoryFingerprintSha256 CHAR(64) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        RestoreOutcome NVARCHAR(20) NOT NULL,
        RestoreExitCode INT NULL,
        RestoreStartedAtUtc DATETIME2(7) NOT NULL,
        RestoreCompletedAtUtc DATETIME2(7) NOT NULL,
        RestoreEvidenceSha256 CHAR(64) NOT NULL,
        BuildOutcome NVARCHAR(20) NOT NULL,
        BuildExitCode INT NULL,
        BuildStartedAtUtc DATETIME2(7) NOT NULL,
        BuildCompletedAtUtc DATETIME2(7) NOT NULL,
        BuildEvidenceSha256 CHAR(64) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        EvidenceSha256 CHAR(64) NOT NULL,
        CONSTRAINT UQ_BuildValidationRecords_Attempt UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId),
        CONSTRAINT UQ_BuildValidationRecords_EvidenceReference UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_BuildValidationRecords_AttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, TechnicalValidationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.TechnicalValidationAttempts
            (
                TenantId, ProjectId, Id,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            ),
        CONSTRAINT FK_BuildValidationRecords_ObservationAuthority
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryStateObservationId, RepositoryFingerprintSha256)
            REFERENCES dbo.RepositoryStateObservations
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 Id, RepositoryFingerprintSha256),
        CONSTRAINT CK_BuildValidationRecords_Outcomes CHECK
        (
            RestoreOutcome IN (N'Passed', N'Failed', N'TimedOut', N'Skipped') AND
            BuildOutcome IN (N'Passed', N'Failed', N'TimedOut', N'Skipped')
        ),
        CONSTRAINT CK_BuildValidationRecords_ExitCodes CHECK
        (
            (RestoreOutcome=N'Skipped' AND RestoreExitCode IS NULL OR
             RestoreOutcome<>N'Skipped' AND RestoreExitCode IS NOT NULL) AND
            (BuildOutcome=N'Skipped' AND BuildExitCode IS NULL OR
             BuildOutcome<>N'Skipped' AND BuildExitCode IS NOT NULL) AND
            (RestoreOutcome<>N'Passed' OR RestoreExitCode=0) AND
            (BuildOutcome<>N'Passed' OR BuildExitCode=0)
        ),
        CONSTRAINT CK_BuildValidationRecords_Timestamps CHECK
        (
            RestoreCompletedAtUtc >= RestoreStartedAtUtc AND
            BuildCompletedAtUtc >= BuildStartedAtUtc AND
            BuildStartedAtUtc >= RestoreCompletedAtUtc AND
            CreatedAtUtc >= BuildCompletedAtUtc
        ),
        CONSTRAINT CK_BuildValidationRecords_Hashes CHECK
        (
            LEN(RestoreEvidenceSha256)=64 AND LEN(BuildEvidenceSha256)=64 AND LEN(EvidenceSha256)=64 AND
            RestoreEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            BuildEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        )
    );
    CREATE UNIQUE INDEX UX_BuildValidationRecords_ObservationAuthority
        ON dbo.BuildValidationRecords
            (TenantId, ProjectId, TechnicalValidationAttemptId,
             RepositoryStateObservationId, RepositoryFingerprintSha256, Id);
END;
GO

IF OBJECT_ID(N'dbo.TestValidationRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TestValidationRecords
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TestValidationRecords PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TechnicalValidationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL,
        RepositoryFingerprintSha256 CHAR(64) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        TestOutcome NVARCHAR(20) NOT NULL,
        TestExitCode INT NULL,
        TotalTests INT NOT NULL,
        PassedTests INT NOT NULL,
        FailedTests INT NOT NULL,
        SkippedTests INT NOT NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NOT NULL,
        EvidenceSha256 CHAR(64) NOT NULL,
        CONSTRAINT UQ_TestValidationRecords_Attempt UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId),
        CONSTRAINT UQ_TestValidationRecords_EvidenceReference UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_TestValidationRecords_AttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, TechnicalValidationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.TechnicalValidationAttempts
            (
                TenantId, ProjectId, Id,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            ),
        CONSTRAINT FK_TestValidationRecords_ObservationAuthority
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryStateObservationId, RepositoryFingerprintSha256)
            REFERENCES dbo.RepositoryStateObservations
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 Id, RepositoryFingerprintSha256),
        CONSTRAINT CK_TestValidationRecords_Outcome CHECK
            (TestOutcome IN (N'Passed', N'Failed', N'TimedOut', N'Skipped')),
        CONSTRAINT CK_TestValidationRecords_ExitCode CHECK
            (((TestOutcome=N'Skipped' AND TestExitCode IS NULL) OR
              (TestOutcome<>N'Skipped' AND TestExitCode IS NOT NULL)) AND
             (TestOutcome<>N'Passed' OR TestExitCode=0)),
        CONSTRAINT CK_TestValidationRecords_Counts CHECK
        (
            TotalTests >= 0 AND PassedTests >= 0 AND FailedTests >= 0 AND SkippedTests >= 0 AND
            TotalTests=PassedTests + FailedTests + SkippedTests AND
            (TestOutcome<>N'Passed' OR FailedTests=0)
        ),
        CONSTRAINT CK_TestValidationRecords_Timestamps CHECK (CompletedAtUtc >= StartedAtUtc),
        CONSTRAINT CK_TestValidationRecords_Hash CHECK
            (LEN(EvidenceSha256)=64 AND
             EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')
    );
END;
GO

IF OBJECT_ID(N'dbo.CodeIndexSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CodeIndexSnapshots
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_CodeIndexSnapshots PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TechnicalValidationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL,
        RepositoryFingerprintSha256 CHAR(64) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        IndexState NVARCHAR(20) NOT NULL,
        IndexSchemaVersion INT NOT NULL,
        IndexerVersion NVARCHAR(100) NOT NULL,
        IndexedFileCount INT NOT NULL,
        IndexedChunkCount INT NOT NULL,
        SourcesJson NVARCHAR(MAX) NOT NULL,
        SourcesSha256 CHAR(64) NOT NULL,
        IndexContentSha256 CHAR(64) NOT NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NOT NULL,
        EvidenceSha256 CHAR(64) NOT NULL,
        CONSTRAINT UQ_CodeIndexSnapshots_Attempt UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId),
        CONSTRAINT UQ_CodeIndexSnapshots_EvidenceReference UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_CodeIndexSnapshots_AttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, TechnicalValidationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.TechnicalValidationAttempts
            (
                TenantId, ProjectId, Id,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            ),
        CONSTRAINT FK_CodeIndexSnapshots_ObservationAuthority
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryStateObservationId, RepositoryFingerprintSha256)
            REFERENCES dbo.RepositoryStateObservations
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 Id, RepositoryFingerprintSha256),
        CONSTRAINT CK_CodeIndexSnapshots_State CHECK
            (IndexState IN (N'Ready', N'Failed', N'Skipped')),
        CONSTRAINT CK_CodeIndexSnapshots_Counts CHECK
            (IndexSchemaVersion > 0 AND
             IndexedFileCount BETWEEN 0 AND 4096 AND
             IndexedChunkCount BETWEEN 0 AND 4096),
        CONSTRAINT CK_CodeIndexSnapshots_Timestamps CHECK (CompletedAtUtc >= StartedAtUtc),
        CONSTRAINT CK_CodeIndexSnapshots_Sources CHECK
        (
            ISJSON(SourcesJson)=1 AND DATALENGTH(SourcesJson)<=2097152 AND
            LEN(SourcesSha256)=64 AND
            SourcesSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            SourcesSha256=IndexContentSha256 AND
            SourcesSha256=
                LOWER(CONVERT(CHAR(64),
                    HASHBYTES('SHA2_256', CONVERT(VARBINARY(MAX),
                        CONVERT(VARCHAR(MAX),
                            SourcesJson COLLATE Latin1_General_100_BIN2_UTF8))), 2))
        ),
        CONSTRAINT CK_CodeIndexSnapshots_Hashes CHECK
        (
            LEN(IndexContentSha256)=64 AND LEN(EvidenceSha256)=64 AND
            IndexContentSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        )
    );
END;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_CodeIndexSnapshots_ValidateSources
  ON dbo.CodeIndexSnapshots AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        OUTER APPLY
        (
            SELECT COUNT(*) AS SourceCount,
                   COUNT(DISTINCT source.Ordinal) AS DistinctOrdinalCount,
                   MIN(source.Ordinal) AS MinimumOrdinal,
                   MAX(source.Ordinal) AS MaximumOrdinal
            FROM OPENJSON(value.SourcesJson)
            WITH (Ordinal INT N''$.ordinal'') source
        ) aggregateValue
        WHERE aggregateValue.SourceCount<>value.IndexedFileCount OR
              aggregateValue.DistinctOrdinalCount<>aggregateValue.SourceCount OR
              (aggregateValue.SourceCount>0 AND
               (aggregateValue.MinimumOrdinal<>1 OR
                aggregateValue.MaximumOrdinal<>aggregateValue.SourceCount)) OR EXISTS
        (
            SELECT 1
            FROM OPENJSON(value.SourcesJson)
            WITH
            (
                Ordinal INT N''$.ordinal'',
                RelativePath NVARCHAR(MAX) N''$.relativePath'',
                ContentSha256 CHAR(64) N''$.contentSha256''
            ) source
            WHERE source.Ordinal IS NULL OR source.Ordinal < 1 OR
                  LEN(LTRIM(RTRIM(source.RelativePath)))=0 OR
                  LEN(source.RelativePath)>1000 OR
                  source.RelativePath<>LTRIM(RTRIM(source.RelativePath)) OR
                  source.RelativePath LIKE N''/%'' OR
                  source.RelativePath LIKE N''[A-Za-z]:%'' OR
                  source.RelativePath LIKE N''%\%'' OR
                  source.RelativePath LIKE N''%//%'' OR
                  source.RelativePath IN (N''.'', N''..'') OR
                  source.RelativePath LIKE N''./%'' OR
                  source.RelativePath LIKE N''../%'' OR
                  source.RelativePath LIKE N''%/./%'' OR
                  source.RelativePath LIKE N''%/../%'' OR
                  source.RelativePath LIKE N''%/.'' OR
                  source.RelativePath LIKE N''%/..'' OR
                  LEN(source.ContentSha256)<>64 OR
                  source.ContentSha256 COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9a-f]%''
        )
    )
        THROW 51306, ''Code-index sources do not match their bounded canonical projection.'', 1;
END;');
GO

IF OBJECT_ID(N'dbo.BuilderModelConfigurationRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderModelConfigurationRecords
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BuilderModelConfigurationRecords PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TechnicalValidationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL,
        RepositoryFingerprintSha256 CHAR(64) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        ConfigurationState NVARCHAR(20) NOT NULL,
        ConfigurationId UNIQUEIDENTIFIER NOT NULL,
        ProviderId NVARCHAR(100) NOT NULL,
        ModelId NVARCHAR(200) NOT NULL,
        ConfigurationRevision BIGINT NOT NULL,
        ConfigurationSha256 CHAR(64) NOT NULL,
        PolicyVersion NVARCHAR(100) NOT NULL,
        PolicySha256 CHAR(64) NOT NULL,
        RecordedAtUtc DATETIME2(7) NOT NULL,
        EvidenceSha256 CHAR(64) NOT NULL,
        CONSTRAINT UQ_BuilderModelConfigurationRecords_Attempt UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId),
        CONSTRAINT UQ_BuilderModelConfigurationRecords_EvidenceReference UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_BuilderModelConfigurationRecords_AttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, TechnicalValidationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.TechnicalValidationAttempts
            (
                TenantId, ProjectId, Id,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            ),
        CONSTRAINT FK_BuilderModelConfigurationRecords_ObservationAuthority
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryStateObservationId, RepositoryFingerprintSha256)
            REFERENCES dbo.RepositoryStateObservations
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 Id, RepositoryFingerprintSha256),
        CONSTRAINT CK_BuilderModelConfigurationRecords_State CHECK
            (ConfigurationState IN (N'Configured', N'Unavailable')),
        CONSTRAINT CK_BuilderModelConfigurationRecords_Revision CHECK
            (ConfigurationRevision > 0 AND
             ConfigurationId<>CONVERT(UNIQUEIDENTIFIER, '00000000-0000-0000-0000-000000000000')),
        CONSTRAINT CK_BuilderModelConfigurationRecords_Strings CHECK
        (
            LEN(LTRIM(RTRIM(ProviderId))) > 0 AND LEN(LTRIM(RTRIM(ModelId))) > 0 AND
            LEN(LTRIM(RTRIM(PolicyVersion))) > 0
        ),
        CONSTRAINT CK_BuilderModelConfigurationRecords_Hashes CHECK
        (
            LEN(ConfigurationSha256)=64 AND LEN(PolicySha256)=64 AND LEN(EvidenceSha256)=64 AND
            ConfigurationSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            PolicySha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        )
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ProjectReadinessAssessments')
      AND name=N'UX_ProjectReadinessAssessments_TechnicalEvidenceAuthority'
)
    CREATE UNIQUE INDEX UX_ProjectReadinessAssessments_TechnicalEvidenceAuthority
        ON dbo.ProjectReadinessAssessments
            (TenantId, ProjectId, Id, Revision, ExecutionReadiness,
             ReasonCode, AssessedAtUtc);
GO

IF OBJECT_ID(N'dbo.ProjectTechnicalReadinessEvidence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectTechnicalReadinessEvidence
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProjectTechnicalReadinessEvidence PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ProjectReadinessAssessmentId BIGINT NOT NULL,
        ProjectReadinessRevision BIGINT NOT NULL,
        TechnicalValidationAttemptId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL,
        RepositoryFingerprintSha256 CHAR(64) NOT NULL,
        RepositoryObservationEvidenceSha256 CHAR(64) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        ProfileDefinitionId NVARCHAR(100) NOT NULL,
        ProfileDescriptorRevision INT NOT NULL,
        ProfileDescriptorSha256 CHAR(64) NOT NULL,
        RestoreCommandSha256 CHAR(64) NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        ToolchainManifestSha256 CHAR(64) NOT NULL,
        ContainerImageDigestSha256 CHAR(64) NOT NULL,
        SandboxPolicySha256 CHAR(64) NOT NULL,
        OfflineFeedManifestSha256 CHAR(64) NOT NULL,
        TemplateBundleSha256 CHAR(64) NOT NULL,
        SandboxQualificationAttemptId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestId UNIQUEIDENTIFIER NOT NULL,
        SandboxEvidenceManifestSha256 CHAR(64) NOT NULL,
        BuildValidationRecordId UNIQUEIDENTIFIER NOT NULL,
        BuildValidationEvidenceSha256 CHAR(64) NOT NULL,
        TestValidationRecordId UNIQUEIDENTIFIER NOT NULL,
        TestValidationEvidenceSha256 CHAR(64) NOT NULL,
        CodeIndexSnapshotId UNIQUEIDENTIFIER NOT NULL,
        CodeIndexEvidenceSha256 CHAR(64) NOT NULL,
        BuilderModelConfigurationRecordId UNIQUEIDENTIFIER NOT NULL,
        BuilderModelConfigurationEvidenceSha256 CHAR(64) NOT NULL,
        RepositoryBindingQualified BIT NOT NULL,
        RepositoryCleanAtBaseline BIT NOT NULL,
        ExecutionProfilePinned BIT NOT NULL,
        RestorePassed BIT NOT NULL,
        BuildPassed BIT NOT NULL,
        TestCommandPassed BIT NOT NULL,
        CodeIndexCurrent BIT NOT NULL,
        SandboxQualified BIT NOT NULL,
        BuilderModelConfigured BIT NOT NULL,
        GateResultsJson NVARCHAR(MAX) NOT NULL,
        GateResultsSha256 CHAR(64) NOT NULL,
        ExecutionReadiness NVARCHAR(50) NOT NULL,
        ReasonCode NVARCHAR(100) NOT NULL,
        AssessedAtUtc DATETIME2(7) NOT NULL,
        EvidenceSha256 CHAR(64) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_ProjectTechnicalReadinessEvidence_Assessment UNIQUE
            (TenantId, ProjectId, ProjectReadinessAssessmentId),
        CONSTRAINT UQ_ProjectTechnicalReadinessEvidence_Attempt UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId),
        CONSTRAINT UQ_ProjectTechnicalReadinessEvidence_ResultAuthority UNIQUE
            (TenantId, ProjectId, TechnicalValidationAttemptId, Id,
             ProjectReadinessAssessmentId),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_AssessmentAuthority
            FOREIGN KEY
                (TenantId, ProjectId, ProjectReadinessAssessmentId,
                 ProjectReadinessRevision, ExecutionReadiness, ReasonCode, AssessedAtUtc)
            REFERENCES dbo.ProjectReadinessAssessments
                (TenantId, ProjectId, Id, Revision, ExecutionReadiness,
                 ReasonCode, AssessedAtUtc),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_AttemptAuthority
            FOREIGN KEY
            (
                TenantId, ProjectId, TechnicalValidationAttemptId,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            )
            REFERENCES dbo.TechnicalValidationAttempts
            (
                TenantId, ProjectId, Id,
                RepositoryBindingId, RepositoryBindingRevision, BaselineCommit,
                ProjectExecutionProfileId, ProjectExecutionProfileRevision,
                ProfileDefinitionId, ProfileDescriptorRevision, ProfileDescriptorSha256,
                RestoreCommandSha256, BuildCommandSha256, TestCommandSha256,
                ToolchainManifestSha256, ContainerImageDigestSha256,
                SandboxPolicySha256, OfflineFeedManifestSha256, TemplateBundleSha256,
                SandboxQualificationAttemptId, SandboxEvidenceManifestId,
                SandboxEvidenceManifestSha256
            ),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_Observation
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 RepositoryStateObservationId, RepositoryFingerprintSha256,
                 RepositoryObservationEvidenceSha256)
            REFERENCES dbo.RepositoryStateObservations
                (TenantId, ProjectId, TechnicalValidationAttemptId, Id,
                 RepositoryFingerprintSha256, EvidenceSha256),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_Build
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 BuildValidationRecordId, BuildValidationEvidenceSha256)
            REFERENCES dbo.BuildValidationRecords
                (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_Test
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 TestValidationRecordId, TestValidationEvidenceSha256)
            REFERENCES dbo.TestValidationRecords
                (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_Index
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 CodeIndexSnapshotId, CodeIndexEvidenceSha256)
            REFERENCES dbo.CodeIndexSnapshots
                (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT FK_ProjectTechnicalReadinessEvidence_ModelConfiguration
            FOREIGN KEY
                (TenantId, ProjectId, TechnicalValidationAttemptId,
                 BuilderModelConfigurationRecordId,
                 BuilderModelConfigurationEvidenceSha256)
            REFERENCES dbo.BuilderModelConfigurationRecords
                (TenantId, ProjectId, TechnicalValidationAttemptId, Id, EvidenceSha256),
        CONSTRAINT CK_ProjectTechnicalReadinessEvidence_State CHECK
            (ExecutionReadiness IN (N'ValidationRequired', N'Ready')),
        CONSTRAINT CK_ProjectTechnicalReadinessEvidence_Gates CHECK
        (
            ISJSON(GateResultsJson)=1 AND
            JSON_VALUE(GateResultsJson, N'$[0].gate')=N'RepositoryBindingQualified' AND
            JSON_VALUE(GateResultsJson, N'$[1].gate')=N'RepositoryCleanAtBaseline' AND
            JSON_VALUE(GateResultsJson, N'$[2].gate')=N'ExecutionProfilePinned' AND
            JSON_VALUE(GateResultsJson, N'$[3].gate')=N'RestorePassed' AND
            JSON_VALUE(GateResultsJson, N'$[4].gate')=N'BuildPassed' AND
            JSON_VALUE(GateResultsJson, N'$[5].gate')=N'TestCommandPassed' AND
            JSON_VALUE(GateResultsJson, N'$[6].gate')=N'CodeIndexCurrent' AND
            JSON_VALUE(GateResultsJson, N'$[7].gate')=N'SandboxQualified' AND
            JSON_VALUE(GateResultsJson, N'$[8].gate')=N'BuilderModelConfigured' AND
            JSON_QUERY(GateResultsJson, N'$[9]') IS NULL AND
            RepositoryBindingQualified=
                CASE JSON_VALUE(GateResultsJson, N'$[0].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            RepositoryCleanAtBaseline=
                CASE JSON_VALUE(GateResultsJson, N'$[1].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            ExecutionProfilePinned=
                CASE JSON_VALUE(GateResultsJson, N'$[2].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            RestorePassed=
                CASE JSON_VALUE(GateResultsJson, N'$[3].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            BuildPassed=
                CASE JSON_VALUE(GateResultsJson, N'$[4].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            TestCommandPassed=
                CASE JSON_VALUE(GateResultsJson, N'$[5].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            CodeIndexCurrent=
                CASE JSON_VALUE(GateResultsJson, N'$[6].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            SandboxQualified=
                CASE JSON_VALUE(GateResultsJson, N'$[7].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            BuilderModelConfigured=
                CASE JSON_VALUE(GateResultsJson, N'$[8].passed') WHEN N'true' THEN 1 ELSE 0 END AND
            JSON_VALUE(GateResultsJson, N'$[0].reasonCode')=
                CASE RepositoryBindingQualified WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'RepositoryBindingNotQualified' END AND
            ((RepositoryCleanAtBaseline=1 AND
              JSON_VALUE(GateResultsJson, N'$[1].reasonCode')=N'RepositoryTechnicalReadinessCurrent') OR
             (RepositoryCleanAtBaseline=0 AND
              JSON_VALUE(GateResultsJson, N'$[1].reasonCode') IN
                (N'RepositoryObservationRequired', N'RepositoryObservationStale'))) AND
            JSON_VALUE(GateResultsJson, N'$[2].reasonCode')=
                CASE ExecutionProfilePinned WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'ExecutionProfileNotPinned' END AND
            JSON_VALUE(GateResultsJson, N'$[3].reasonCode')=
                CASE RestorePassed WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'RestoreValidationRequired' END AND
            JSON_VALUE(GateResultsJson, N'$[4].reasonCode')=
                CASE BuildPassed WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'BuildValidationRequired' END AND
            JSON_VALUE(GateResultsJson, N'$[5].reasonCode')=
                CASE TestCommandPassed WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'TestValidationRequired' END AND
            JSON_VALUE(GateResultsJson, N'$[6].reasonCode')=
                CASE CodeIndexCurrent WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'CodeIndexRequired' END AND
            JSON_VALUE(GateResultsJson, N'$[7].reasonCode')=
                CASE SandboxQualified WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'SandboxQualificationRequired' END AND
            JSON_VALUE(GateResultsJson, N'$[8].reasonCode')=
                CASE BuilderModelConfigured WHEN 1 THEN N'RepositoryTechnicalReadinessCurrent'
                     ELSE N'BuilderModelConfigurationRequired' END AND
            GateResultsSha256=
                LOWER(CONVERT(CHAR(64),
                    HASHBYTES('SHA2_256', CONVERT(VARCHAR(MAX), GateResultsJson)), 2)) AND
            ((ExecutionReadiness=N'Ready' AND
              RepositoryBindingQualified=1 AND RepositoryCleanAtBaseline=1 AND
              ExecutionProfilePinned=1 AND RestorePassed=1 AND BuildPassed=1 AND
              TestCommandPassed=1 AND CodeIndexCurrent=1 AND SandboxQualified=1 AND
              BuilderModelConfigured=1)
             OR
             (ExecutionReadiness=N'ValidationRequired' AND
              (RepositoryBindingQualified=0 OR RepositoryCleanAtBaseline=0 OR
               ExecutionProfilePinned=0 OR RestorePassed=0 OR BuildPassed=0 OR
               TestCommandPassed=0 OR CodeIndexCurrent=0 OR SandboxQualified=0 OR
               BuilderModelConfigured=0)))
        ),
        CONSTRAINT CK_ProjectTechnicalReadinessEvidence_Revision CHECK
            (ProjectReadinessRevision > 0),
        CONSTRAINT CK_ProjectTechnicalReadinessEvidence_Timestamps CHECK
            (CreatedAtUtc >= AssessedAtUtc),
        CONSTRAINT CK_ProjectTechnicalReadinessEvidence_Hashes CHECK
        (
            LEN(RepositoryObservationEvidenceSha256)=64 AND
            LEN(BuildValidationEvidenceSha256)=64 AND
            LEN(TestValidationEvidenceSha256)=64 AND
            LEN(CodeIndexEvidenceSha256)=64 AND
            LEN(BuilderModelConfigurationEvidenceSha256)=64 AND
            LEN(GateResultsSha256)=64 AND
            LEN(EvidenceSha256)=64 AND
            RepositoryObservationEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            BuildValidationEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            TestValidationEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            CodeIndexEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            BuilderModelConfigurationEvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            GateResultsSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            EvidenceSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        )
    );
    CREATE INDEX IX_ProjectTechnicalReadinessEvidence_ProjectCurrent
        ON dbo.ProjectTechnicalReadinessEvidence
            (TenantId, ProjectId, ProjectReadinessRevision DESC);
END;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_ProjectTechnicalReadinessEvidence_ValidateOutcome
  ON dbo.ProjectTechnicalReadinessEvidence AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        INNER JOIN dbo.TechnicalValidationAttempts attempt
            ON attempt.TenantId=value.TenantId
           AND attempt.ProjectId=value.ProjectId
           AND attempt.Id=value.TechnicalValidationAttemptId
        INNER JOIN dbo.BuildValidationRecords build
            ON build.Id=value.BuildValidationRecordId
        INNER JOIN dbo.TestValidationRecords test
            ON test.Id=value.TestValidationRecordId
        INNER JOIN dbo.CodeIndexSnapshots codeIndex
            ON codeIndex.Id=value.CodeIndexSnapshotId
        INNER JOIN dbo.BuilderModelConfigurationRecords model
            ON model.Id=value.BuilderModelConfigurationRecordId
        INNER JOIN dbo.RepositoryStateObservations observation
            ON observation.Id=value.RepositoryStateObservationId
        INNER JOIN dbo.RepositoryBindings binding
            ON binding.TenantId=value.TenantId
           AND binding.ProjectId=value.ProjectId
           AND binding.Id=value.RepositoryBindingId
        INNER JOIN dbo.ProjectExecutionProfiles profile
            ON profile.TenantId=value.TenantId
           AND profile.ProjectId=value.ProjectId
           AND profile.Id=value.ProjectExecutionProfileId
        INNER JOIN dbo.SandboxQualificationAttempts sandbox
            ON sandbox.TenantId=value.TenantId
           AND sandbox.ProjectId=value.ProjectId
           AND sandbox.Id=value.SandboxQualificationAttemptId
        INNER JOIN dbo.SandboxEvidenceManifests manifest
            ON manifest.TenantId=value.TenantId
           AND manifest.ProjectId=value.ProjectId
           AND manifest.Id=value.SandboxEvidenceManifestId
        WHERE
          value.RepositoryBindingQualified<>
            CASE WHEN binding.BindingState=N''Qualified'' AND
                           binding.CurrentRevision=value.RepositoryBindingRevision AND
                           binding.BaselineCommit=value.BaselineCommit THEN 1 ELSE 0 END
          OR value.RepositoryCleanAtBaseline<>
            CASE WHEN observation.DirtyState=N''Clean'' AND
                           observation.HeadCommit=value.BaselineCommit AND
                           observation.RepositoryFingerprintSha256=value.RepositoryFingerprintSha256
                 THEN 1 ELSE 0 END
          OR value.ExecutionProfilePinned<>
            CASE WHEN profile.RepositoryBindingId=value.RepositoryBindingId AND
                           profile.CurrentRevision=value.ProjectExecutionProfileRevision AND
                           profile.ProfileDefinitionId=value.ProfileDefinitionId AND
                           profile.ProfileDescriptorRevision=value.ProfileDescriptorRevision AND
                           profile.DescriptorSha256=value.ProfileDescriptorSha256 AND
                           profile.TemplateBundleSha256=value.TemplateBundleSha256 AND
                           profile.ToolchainManifestId=attempt.ToolchainManifestId
                 THEN 1 ELSE 0 END
          OR value.RestorePassed<>CASE WHEN build.RestoreOutcome=N''Passed'' THEN 1 ELSE 0 END
          OR value.BuildPassed<>CASE WHEN build.BuildOutcome=N''Passed'' THEN 1 ELSE 0 END
          OR value.TestCommandPassed<>CASE WHEN test.TestOutcome=N''Passed'' THEN 1 ELSE 0 END
          OR value.CodeIndexCurrent<>CASE WHEN codeIndex.IndexState=N''Ready'' THEN 1 ELSE 0 END
          OR value.SandboxQualified<>
            CASE WHEN sandbox.State=N''Passed'' AND sandbox.CleanupConfirmed=1 AND
                           manifest.Passed=1 AND
                           sandbox.EvidenceManifestSha256=value.SandboxEvidenceManifestSha256
                 THEN 1 ELSE 0 END
          OR value.BuilderModelConfigured<>
            CASE WHEN model.ConfigurationState=N''Configured'' THEN 1 ELSE 0 END
          OR
          (value.ExecutionReadiness=N''Ready'' AND
           (attempt.State<>N''Passed'' OR build.RestoreOutcome<>N''Passed'' OR
            build.BuildOutcome<>N''Passed'' OR test.TestOutcome<>N''Passed'' OR
            codeIndex.IndexState<>N''Ready'' OR model.ConfigurationState<>N''Configured''))
          OR
          (value.ExecutionReadiness=N''ValidationRequired'' AND attempt.State=N''Running'')
    )
        THROW 51304, ''Technical readiness is not supported by the exact terminal evidence outcome.'', 1;
END;');
GO

/*
This view is the SQL-owned effective projection. External filesystem or host-configuration
drift becomes visible after the explicit observation/validation refresh records it; it is
never inferred from a path or from sandbox qualification alone.
*/
CREATE OR ALTER VIEW dbo.vw_WorkbenchEffectiveProjectReadiness
AS
WITH LatestAssessment AS
(
    SELECT assessment.*,
           ROW_NUMBER() OVER
           (
               PARTITION BY assessment.TenantId, assessment.ProjectId
               ORDER BY assessment.Revision DESC, assessment.Id DESC
           ) AS Position
    FROM dbo.ProjectReadinessAssessments assessment
),
LatestTechnicalAttempt AS
(
    SELECT attempt.*,
           ROW_NUMBER() OVER
           (
               PARTITION BY attempt.TenantId, attempt.ProjectId
               ORDER BY attempt.AttemptNumber DESC, attempt.StartedAtUtc DESC
           ) AS Position
    FROM dbo.TechnicalValidationAttempts attempt
),
LatestSandboxAttempt AS
(
    SELECT attempt.Id, attempt.TenantId, attempt.ProjectId,
           ROW_NUMBER() OVER
           (
               PARTITION BY attempt.TenantId, attempt.ProjectId
               ORDER BY attempt.AttemptNumber DESC, attempt.StartedAtUtc DESC
           ) AS Position
    FROM dbo.SandboxQualificationAttempts attempt
)
SELECT
    project.TenantId,
    project.Id AS ProjectId,
    CAST
    (
        CASE
            WHEN binding.Id IS NULL OR binding.BindingState<>N'Qualified' OR profile.Id IS NULL
                THEN N'NotConfigured'
            WHEN assessment.ExecutionReadiness=N'Ready' AND evidence.Id IS NOT NULL AND
                 latestTechnical.Id=evidence.TechnicalValidationAttemptId AND
                 latestSandbox.Id=evidence.SandboxQualificationAttemptId AND
                 binding.CurrentRevision=evidence.RepositoryBindingRevision AND
                 binding.BaselineCommit=evidence.BaselineCommit AND
                 profile.RepositoryBindingId=evidence.RepositoryBindingId AND
                 profile.CurrentRevision=evidence.ProjectExecutionProfileRevision AND
                 profile.ProfileDefinitionId=evidence.ProfileDefinitionId AND
                 profile.ProfileDescriptorRevision=evidence.ProfileDescriptorRevision AND
                 profile.DescriptorSha256=evidence.ProfileDescriptorSha256 AND
                 profile.TemplateBundleSha256=evidence.TemplateBundleSha256 AND
                 profile.ToolchainManifestId=technical.ToolchainManifestId AND
                 technical.RestoreCommandSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        profile.RestoreCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2)) AND
                 technical.BuildCommandSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        profile.BuildCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2)) AND
                 technical.TestCommandSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        profile.TestCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2)) AND
                 technical.State=N'Passed' AND
                 technical.EvidenceSha256 IS NOT NULL AND
                 observation.HeadCommit=evidence.BaselineCommit AND
                 observation.DirtyState=N'Clean' AND
                 observation.RepositoryFingerprintSha256=evidence.RepositoryFingerprintSha256 AND
                 build.RestoreOutcome=N'Passed' AND build.RestoreExitCode=0 AND
                 build.BuildOutcome=N'Passed' AND build.BuildExitCode=0 AND
                 test.TestOutcome=N'Passed' AND test.TestExitCode=0 AND
                 codeIndex.IndexState=N'Ready' AND
                 model.ConfigurationState=N'Configured' AND
                 sandbox.State=N'Passed' AND sandbox.CleanupConfirmed=1 AND
                 sandbox.EvidenceManifestSha256=evidence.SandboxEvidenceManifestSha256 AND
                 manifest.Passed=1 AND manifest.ManifestSha256=evidence.SandboxEvidenceManifestSha256 AND
                 evidence.RepositoryBindingQualified=1 AND
                 evidence.RepositoryCleanAtBaseline=1 AND
                 evidence.ExecutionProfilePinned=1 AND evidence.RestorePassed=1 AND
                 evidence.BuildPassed=1 AND evidence.TestCommandPassed=1 AND
                 evidence.CodeIndexCurrent=1 AND evidence.SandboxQualified=1 AND
                 evidence.BuilderModelConfigured=1
                THEN N'Ready'
            ELSE N'ValidationRequired'
        END AS NVARCHAR(50)
    ) AS ExecutionReadiness,
    CAST
    (
        CASE
            WHEN binding.Id IS NULL OR binding.BindingState<>N'Qualified'
                THEN N'RepositoryBindingNotQualified'
            WHEN profile.Id IS NULL
                THEN N'ExecutionProfileNotPinned'
            WHEN evidence.Id IS NULL OR observation.Id IS NULL
                THEN N'RepositoryObservationRequired'
            WHEN evidence.RepositoryBindingQualified<>1
                THEN N'RepositoryBindingNotQualified'
            WHEN evidence.ExecutionProfilePinned<>1
                THEN N'ExecutionProfileNotPinned'
            WHEN binding.CurrentRevision<>evidence.RepositoryBindingRevision OR
                 binding.BaselineCommit<>evidence.BaselineCommit OR
                 observation.HeadCommit<>evidence.BaselineCommit OR
                 observation.DirtyState<>N'Clean' OR
                 observation.RepositoryFingerprintSha256<>evidence.RepositoryFingerprintSha256
                THEN N'RepositoryObservationStale'
            WHEN latestTechnical.Id<>evidence.TechnicalValidationAttemptId OR
                 technical.State<>N'Passed' OR
                 profile.CurrentRevision<>evidence.ProjectExecutionProfileRevision OR
                 profile.ProfileDefinitionId<>evidence.ProfileDefinitionId OR
                 profile.ProfileDescriptorRevision<>evidence.ProfileDescriptorRevision OR
                 profile.DescriptorSha256<>evidence.ProfileDescriptorSha256 OR
                 technical.RestoreCommandSha256<>LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        profile.RestoreCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
                 technical.BuildCommandSha256<>LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        profile.BuildCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
                 technical.TestCommandSha256<>LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                    CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                        profile.TestCommand COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
                 build.RestoreOutcome<>N'Passed' OR build.RestoreExitCode<>0 OR
                 evidence.RestorePassed<>1
                THEN N'RestoreValidationRequired'
            WHEN build.BuildOutcome<>N'Passed' OR build.BuildExitCode<>0 OR evidence.BuildPassed<>1
                THEN N'BuildValidationRequired'
            WHEN test.TestOutcome<>N'Passed' OR test.TestExitCode<>0 OR evidence.TestCommandPassed<>1
                THEN N'TestValidationRequired'
            WHEN codeIndex.IndexState<>N'Ready' OR evidence.CodeIndexCurrent<>1
                THEN N'CodeIndexRequired'
            WHEN latestSandbox.Id<>evidence.SandboxQualificationAttemptId OR
                 sandbox.State<>N'Passed' OR sandbox.CleanupConfirmed<>1 OR
                 manifest.Passed<>1 OR evidence.SandboxQualified<>1
                THEN N'SandboxQualificationRequired'
            WHEN model.ConfigurationState<>N'Configured' OR evidence.BuilderModelConfigured<>1
                THEN N'BuilderModelConfigurationRequired'
            ELSE N'RepositoryTechnicalReadinessCurrent'
        END AS NVARCHAR(100)
    ) AS ReasonCode,
    CAST
    (
        CASE
            WHEN binding.Id IS NULL OR binding.BindingState<>N'Qualified' OR profile.Id IS NULL
                THEN N'Configure and qualify a repository before technical validation.'
            WHEN assessment.ExecutionReadiness=N'Ready' AND evidence.Id IS NOT NULL AND
                 latestTechnical.Id=evidence.TechnicalValidationAttemptId AND
                 latestSandbox.Id=evidence.SandboxQualificationAttemptId AND
                 binding.CurrentRevision=evidence.RepositoryBindingRevision AND
                 binding.BaselineCommit=evidence.BaselineCommit AND
                 profile.CurrentRevision=evidence.ProjectExecutionProfileRevision AND
                 profile.ProfileDefinitionId=evidence.ProfileDefinitionId AND
                 profile.ProfileDescriptorRevision=evidence.ProfileDescriptorRevision AND
                 profile.DescriptorSha256=evidence.ProfileDescriptorSha256 AND
                 technical.State=N'Passed' AND observation.DirtyState=N'Clean' AND
                 observation.HeadCommit=evidence.BaselineCommit AND
                 build.RestoreOutcome=N'Passed' AND build.BuildOutcome=N'Passed' AND
                 test.TestOutcome=N'Passed' AND codeIndex.IndexState=N'Ready' AND
                 model.ConfigurationState=N'Configured' AND sandbox.State=N'Passed' AND
                 sandbox.CleanupConfirmed=1 AND manifest.Passed=1 AND
                 evidence.RepositoryBindingQualified=1 AND evidence.RepositoryCleanAtBaseline=1 AND
                 evidence.ExecutionProfilePinned=1 AND evidence.RestorePassed=1 AND
                 evidence.BuildPassed=1 AND evidence.TestCommandPassed=1 AND
                 evidence.CodeIndexCurrent=1 AND evidence.SandboxQualified=1 AND
                 evidence.BuilderModelConfigured=1
                THEN assessment.Summary
            ELSE N'Repository validation evidence is missing, failed, or no longer current.'
        END AS NVARCHAR(500)
    ) AS Summary,
    assessment.Id AS ProjectReadinessAssessmentId,
    assessment.Revision AS ProjectReadinessRevision,
    evidence.Id AS TechnicalReadinessEvidenceId,
    evidence.TechnicalValidationAttemptId
FROM dbo.Projects project
LEFT JOIN dbo.RepositoryBindings binding
    ON binding.TenantId=project.TenantId AND binding.ProjectId=project.Id
LEFT JOIN dbo.ProjectExecutionProfiles profile
    ON profile.TenantId=project.TenantId AND profile.ProjectId=project.Id
LEFT JOIN LatestAssessment assessment
    ON assessment.TenantId=project.TenantId AND assessment.ProjectId=project.Id AND assessment.Position=1
LEFT JOIN dbo.ProjectTechnicalReadinessEvidence evidence
    ON evidence.TenantId=project.TenantId AND evidence.ProjectId=project.Id
   AND evidence.ProjectReadinessAssessmentId=assessment.Id
LEFT JOIN dbo.TechnicalValidationAttempts technical
    ON technical.TenantId=project.TenantId AND technical.ProjectId=project.Id
   AND technical.Id=evidence.TechnicalValidationAttemptId
LEFT JOIN LatestTechnicalAttempt latestTechnical
    ON latestTechnical.TenantId=project.TenantId AND latestTechnical.ProjectId=project.Id
   AND latestTechnical.Position=1
LEFT JOIN LatestSandboxAttempt latestSandbox
    ON latestSandbox.TenantId=project.TenantId AND latestSandbox.ProjectId=project.Id
   AND latestSandbox.Position=1
LEFT JOIN dbo.RepositoryStateObservations observation
    ON observation.Id=evidence.RepositoryStateObservationId
LEFT JOIN dbo.BuildValidationRecords build
    ON build.Id=evidence.BuildValidationRecordId
LEFT JOIN dbo.TestValidationRecords test
    ON test.Id=evidence.TestValidationRecordId
LEFT JOIN dbo.CodeIndexSnapshots codeIndex
    ON codeIndex.Id=evidence.CodeIndexSnapshotId
LEFT JOIN dbo.BuilderModelConfigurationRecords model
    ON model.Id=evidence.BuilderModelConfigurationRecordId
LEFT JOIN dbo.SandboxQualificationAttempts sandbox
    ON sandbox.Id=evidence.SandboxQualificationAttemptId
LEFT JOIN dbo.SandboxEvidenceManifests manifest
    ON manifest.Id=evidence.SandboxEvidenceManifestId;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_ProjectReadinessAssessments_TechnicalEvidenceImmutable
  ON dbo.ProjectReadinessAssessments AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM deleted d
        INNER JOIN inserted i ON i.Id=d.Id
        INNER JOIN dbo.ProjectTechnicalReadinessEvidence evidence
            ON evidence.ProjectReadinessAssessmentId=d.Id
        WHERE i.TenantId<>d.TenantId OR i.ProjectId<>d.ProjectId OR i.Revision<>d.Revision OR
              i.ExecutionReadiness<>d.ExecutionReadiness OR i.ReasonCode<>d.ReasonCode OR
              i.Summary<>d.Summary OR i.AssessedByActorUserId<>d.AssessedByActorUserId OR
              i.AssessedAtUtc<>d.AssessedAtUtc
    )
        THROW 51305, ''A readiness assessment with technical evidence is immutable.'', 1;
END;');
GO

DECLARE @AppendOnlyTriggers TABLE(TableName SYSNAME, TriggerName SYSNAME, ErrorNumber INT);
INSERT @AppendOnlyTriggers(TableName, TriggerName, ErrorNumber)
VALUES
    (N'RepositoryStateObservations', N'TR_RepositoryStateObservations_AppendOnly', 51310),
    (N'BuildValidationRecords', N'TR_BuildValidationRecords_AppendOnly', 51311),
    (N'TestValidationRecords', N'TR_TestValidationRecords_AppendOnly', 51312),
    (N'CodeIndexSnapshots', N'TR_CodeIndexSnapshots_AppendOnly', 51313),
    (N'BuilderModelConfigurationRecords', N'TR_BuilderModelConfigurationRecords_AppendOnly', 51314),
    (N'ProjectTechnicalReadinessEvidence', N'TR_ProjectTechnicalReadinessEvidence_AppendOnly', 51315);

DECLARE @TableName SYSNAME, @TriggerName SYSNAME, @ErrorNumber INT, @Sql NVARCHAR(MAX);
DECLARE append_only_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName, TriggerName, ErrorNumber FROM @AppendOnlyTriggers;
OPEN append_only_cursor;
FETCH NEXT FROM append_only_cursor INTO @TableName, @TriggerName, @ErrorNumber;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @Sql=N'CREATE OR ALTER TRIGGER dbo.' + QUOTENAME(@TriggerName) +
        N' ON dbo.' + QUOTENAME(@TableName) +
        N' AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW ' +
        CONVERT(NVARCHAR(20), @ErrorNumber) +
        N', ''' + REPLACE(@TableName, N'''', N'''''') + N' are append-only.'', 1; END;';
    EXEC sys.sp_executesql @Sql;
    FETCH NEXT FROM append_only_cursor INTO @TableName, @TriggerName, @ErrorNumber;
END;
CLOSE append_only_cursor;
DEALLOCATE append_only_cursor;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_TechnicalValidationProjectOperation'
)
    CREATE UNIQUE INDEX UX_ClientOperations_TechnicalValidationProjectOperation
        ON dbo.ClientOperations
            (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId)
        WHERE OperationKind=N'ValidateRepositoryTechnicalReadiness';
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultTechnicalValidationAttemptId') IS NULL
    ALTER TABLE dbo.ClientOperations
        ADD ResultTechnicalValidationAttemptId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultProjectTechnicalReadinessEvidenceId') IS NULL
    ALTER TABLE dbo.ClientOperations
        ADD ResultProjectTechnicalReadinessEvidenceId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultProjectReadinessAssessmentId') IS NULL
    ALTER TABLE dbo.ClientOperations
        ADD ResultProjectReadinessAssessmentId BIGINT NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'CK_ClientOperations_TechnicalValidationResultAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT CK_ClientOperations_TechnicalValidationResultAuthority CHECK
        (
            (ResultTechnicalValidationAttemptId IS NULL AND
             ResultProjectTechnicalReadinessEvidenceId IS NULL AND
             ResultProjectReadinessAssessmentId IS NULL)
            OR
            (ResultProjectId IS NOT NULL AND ResultWorkbenchSessionId IS NOT NULL AND
             ResultTechnicalValidationAttemptId IS NOT NULL AND
             ((ResultProjectTechnicalReadinessEvidenceId IS NULL AND
               ResultProjectReadinessAssessmentId IS NULL)
              OR
              (ResultProjectTechnicalReadinessEvidenceId IS NOT NULL AND
               ResultProjectReadinessAssessmentId IS NOT NULL)))
        );
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_TechnicalValidationAttemptAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_TechnicalValidationAttemptAuthority
        FOREIGN KEY
            (Id, TenantId, ResultProjectId, ResultTechnicalValidationAttemptId,
             ResultWorkbenchSessionId)
        REFERENCES dbo.TechnicalValidationAttempts
            (ClientOperationRecordId, TenantId, ProjectId, Id, WorkbenchSessionId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'FK_ClientOperations_TechnicalReadinessEvidenceAuthority'
)
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_TechnicalReadinessEvidenceAuthority
        FOREIGN KEY
        (
            TenantId, ResultProjectId, ResultTechnicalValidationAttemptId,
            ResultProjectTechnicalReadinessEvidenceId,
            ResultProjectReadinessAssessmentId
        )
        REFERENCES dbo.ProjectTechnicalReadinessEvidence
        (
            TenantId, ProjectId, TechnicalValidationAttemptId, Id,
            ProjectReadinessAssessmentId
        );
GO

/*
Cleanup order for tests and operational reset tooling (children before parents):
  1. clear ClientOperations PR-06B result columns;
  2. ProjectTechnicalReadinessEvidence;
  3. BuilderModelConfigurationRecords, CodeIndexSnapshots, TestValidationRecords,
     BuildValidationRecords;
  4. RepositoryStateObservations;
  5. TechnicalValidationAttempts;
  6. only then sandbox/profile/binding/readiness parent rows.
Append-only triggers must be disabled only by trusted test/reset tooling and immediately re-enabled.
*/

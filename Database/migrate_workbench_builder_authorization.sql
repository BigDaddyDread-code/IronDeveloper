/*
   Workbench v0.1 PR-07A: authorization-free Builder work-package cores and
   exact, single-use start authorizations.

   This slice deliberately creates no Builder run, execution attempt, or
   execution envelope. PR-07B owns atomic authorization consumption and run
   creation.
*/

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ProjectUnderstandings')
      AND name=N'UX_ProjectUnderstandings_ExactArtifactAuthority'
)
    CREATE UNIQUE INDEX UX_ProjectUnderstandings_ExactArtifactAuthority
        ON dbo.ProjectUnderstandings(TenantId, ProjectId, Id, Revision);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.RepositoryStateObservations')
      AND name=N'UX_RepositoryStateObservations_ProjectId'
)
    CREATE UNIQUE INDEX UX_RepositoryStateObservations_ProjectId
        ON dbo.RepositoryStateObservations(TenantId, ProjectId, Id);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.CodeIndexSnapshots')
      AND name=N'UX_CodeIndexSnapshots_ProjectId'
)
    CREATE UNIQUE INDEX UX_CodeIndexSnapshots_ProjectId
        ON dbo.CodeIndexSnapshots(TenantId, ProjectId, Id);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ClientOperations')
      AND name=N'UX_ClientOperations_BuilderGrantAuthority'
)
    CREATE UNIQUE INDEX UX_ClientOperations_BuilderGrantAuthority
        ON dbo.ClientOperations
            (Id, TenantId, ActorUserId, ClientOperationId,
             ResultProjectId, ResultWorkbenchSessionId);
GO

IF OBJECT_ID(N'dbo.BuilderWorkPackageCores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderWorkPackageCores
    (
        Id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BuilderWorkPackageCores PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        CanonicalizationVersion INT NOT NULL,
        CanonicalJson NVARCHAR(MAX) NOT NULL,
        CoreHash CHAR(64) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_BuilderWorkPackageCores_ProjectId UNIQUE
            (TenantId, ProjectId, Id),
        CONSTRAINT UQ_BuilderWorkPackageCores_ExactHash UNIQUE
            (TenantId, ProjectId, Id, CoreHash),
        CONSTRAINT UQ_BuilderWorkPackageCores_ProjectHash UNIQUE
            (TenantId, ProjectId, CoreHash),
        CONSTRAINT FK_BuilderWorkPackageCores_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT CK_BuilderWorkPackageCores_CanonicalizationVersion CHECK
            (CanonicalizationVersion=1),
        CONSTRAINT CK_BuilderWorkPackageCores_CanonicalJson CHECK
        (
            ISJSON(CanonicalJson)=1 AND
            DATALENGTH(CanonicalJson) BETWEEN 2 AND 2097152 AND
            JSON_VALUE(CanonicalJson, N'$.id') IS NOT NULL AND
            JSON_VALUE(CanonicalJson, N'$.canonicalizationVersion') IS NOT NULL AND
            JSON_VALUE(CanonicalJson, N'$.tenantId') IS NOT NULL AND
            JSON_VALUE(CanonicalJson, N'$.projectId') IS NOT NULL AND
            JSON_VALUE(CanonicalJson, N'$.createdAtUtc') IS NOT NULL AND
            TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(CanonicalJson, N'$.id'))=Id AND
            TRY_CONVERT(INT, JSON_VALUE(CanonicalJson, N'$.canonicalizationVersion'))=
                CanonicalizationVersion AND
            TRY_CONVERT(INT, JSON_VALUE(CanonicalJson, N'$.tenantId'))=TenantId AND
            TRY_CONVERT(INT, JSON_VALUE(CanonicalJson, N'$.projectId'))=ProjectId AND
            TRY_CONVERT(DATETIME2(7), JSON_VALUE(CanonicalJson, N'$.createdAtUtc'), 127)=
                CreatedAtUtc
        ),
        CONSTRAINT CK_BuilderWorkPackageCores_CoreHash CHECK
        (
            LEN(CoreHash)=64 AND
            CoreHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            CoreHash=
                LOWER(CONVERT(CHAR(64),
                    HASHBYTES('SHA2_256', CONVERT(VARBINARY(MAX),
                        CONVERT(VARCHAR(MAX),
                            CanonicalJson COLLATE Latin1_General_100_BIN2_UTF8))), 2))
        )
    );

    CREATE INDEX IX_BuilderWorkPackageCores_ProjectTime
        ON dbo.BuilderWorkPackageCores(TenantId, ProjectId, CreatedAtUtc DESC, Id);
END;
GO

IF OBJECT_ID(N'dbo.BuilderWorkPackageTickets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderWorkPackageTickets
    (
        Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_BuilderWorkPackageTickets PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        BuilderWorkPackageCoreId UNIQUEIDENTIFIER NOT NULL,
        Ordinal INT NOT NULL,
        TicketId BIGINT NOT NULL,
        TicketRevision BIGINT NOT NULL,
        CONSTRAINT UQ_BuilderWorkPackageTickets_Ordinal UNIQUE
            (TenantId, ProjectId, BuilderWorkPackageCoreId, Ordinal),
        CONSTRAINT UQ_BuilderWorkPackageTickets_Ticket UNIQUE
            (TenantId, ProjectId, BuilderWorkPackageCoreId, TicketId),
        CONSTRAINT FK_BuilderWorkPackageTickets_Core
            FOREIGN KEY (TenantId, ProjectId, BuilderWorkPackageCoreId)
            REFERENCES dbo.BuilderWorkPackageCores(TenantId, ProjectId, Id),
        CONSTRAINT FK_BuilderWorkPackageTickets_Ticket
            FOREIGN KEY (TenantId, ProjectId, TicketId)
            REFERENCES dbo.ProjectTickets(TenantId, ProjectId, Id),
        CONSTRAINT CK_BuilderWorkPackageTickets_Ordinal CHECK (Ordinal > 0),
        CONSTRAINT CK_BuilderWorkPackageTickets_Revision CHECK (TicketRevision > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.BuilderWorkPackageArtifactReferences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderWorkPackageArtifactReferences
    (
        Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_BuilderWorkPackageArtifactReferences PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        BuilderWorkPackageCoreId UNIQUEIDENTIFIER NOT NULL,
        Ordinal INT NOT NULL,
        ArtifactKind NVARCHAR(50) NOT NULL,
        ArtifactReferenceId BIGINT NOT NULL,
        ArtifactRevision BIGINT NOT NULL,
        CONSTRAINT UQ_BuilderWorkPackageArtifactReferences_Ordinal UNIQUE
            (TenantId, ProjectId, BuilderWorkPackageCoreId, Ordinal),
        CONSTRAINT UQ_BuilderWorkPackageArtifactReferences_Artifact UNIQUE
            (
                TenantId, ProjectId, BuilderWorkPackageCoreId,
                ArtifactKind, ArtifactReferenceId, ArtifactRevision
            ),
        CONSTRAINT FK_BuilderWorkPackageArtifactReferences_Core
            FOREIGN KEY (TenantId, ProjectId, BuilderWorkPackageCoreId)
            REFERENCES dbo.BuilderWorkPackageCores(TenantId, ProjectId, Id),
        CONSTRAINT FK_BuilderWorkPackageArtifactReferences_Understanding
            FOREIGN KEY (TenantId, ProjectId, ArtifactReferenceId, ArtifactRevision)
            REFERENCES dbo.ProjectUnderstandings(TenantId, ProjectId, Id, Revision),
        CONSTRAINT CK_BuilderWorkPackageArtifactReferences_Ordinal CHECK (Ordinal > 0),
        CONSTRAINT CK_BuilderWorkPackageArtifactReferences_Kind CHECK
            (ArtifactKind=N'ProjectUnderstanding'),
        CONSTRAINT CK_BuilderWorkPackageArtifactReferences_Revision CHECK
            (ArtifactRevision > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.BuilderWorkPackageRepositoryContexts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderWorkPackageRepositoryContexts
    (
        BuilderWorkPackageCoreId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BuilderWorkPackageRepositoryContexts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        RepositoryBindingId UNIQUEIDENTIFIER NOT NULL,
        RepositoryBindingRevision BIGINT NOT NULL,
        BranchName NVARCHAR(255) NOT NULL,
        BaselineCommit CHAR(40) NOT NULL,
        RepositoryStateObservationId UNIQUEIDENTIFIER NOT NULL,
        WorktreeFingerprint CHAR(64) NOT NULL,
        ProjectExecutionProfileId UNIQUEIDENTIFIER NOT NULL,
        ProjectExecutionProfileRevision BIGINT NOT NULL,
        CodeIndexSnapshotId UNIQUEIDENTIFIER NOT NULL,
        CodeIndexSnapshotRevision BIGINT NOT NULL,
        BuildCommandSha256 CHAR(64) NOT NULL,
        TestCommandSha256 CHAR(64) NOT NULL,
        SandboxPolicyVersion NVARCHAR(100) NOT NULL,
        ToolchainManifestId NVARCHAR(200) NOT NULL,
        CONSTRAINT UQ_BuilderWorkPackageRepositoryContexts_ProjectCore UNIQUE
            (TenantId, ProjectId, BuilderWorkPackageCoreId),
        CONSTRAINT FK_BuilderWorkPackageRepositoryContexts_Core
            FOREIGN KEY (TenantId, ProjectId, BuilderWorkPackageCoreId)
            REFERENCES dbo.BuilderWorkPackageCores(TenantId, ProjectId, Id),
        CONSTRAINT FK_BuilderWorkPackageRepositoryContexts_BindingRevision
            FOREIGN KEY
                (TenantId, ProjectId, RepositoryBindingId, RepositoryBindingRevision)
            REFERENCES dbo.RepositoryBindingRevisions
                (TenantId, ProjectId, RepositoryBindingId, Revision),
        CONSTRAINT FK_BuilderWorkPackageRepositoryContexts_ProfileRevision
            FOREIGN KEY
            (
                TenantId, ProjectId, ProjectExecutionProfileId,
                ProjectExecutionProfileRevision
            )
            REFERENCES dbo.ProjectExecutionProfileRevisions
            (
                TenantId, ProjectId, ProjectExecutionProfileId, Revision
            ),
        CONSTRAINT FK_BuilderWorkPackageRepositoryContexts_Observation
            FOREIGN KEY (TenantId, ProjectId, RepositoryStateObservationId)
            REFERENCES dbo.RepositoryStateObservations(TenantId, ProjectId, Id),
        CONSTRAINT FK_BuilderWorkPackageRepositoryContexts_CodeIndex
            FOREIGN KEY (TenantId, ProjectId, CodeIndexSnapshotId)
            REFERENCES dbo.CodeIndexSnapshots(TenantId, ProjectId, Id),
        CONSTRAINT CK_BuilderWorkPackageRepositoryContexts_Revisions CHECK
        (
            RepositoryBindingRevision > 0 AND
            ProjectExecutionProfileRevision > 0 AND
            CodeIndexSnapshotRevision > 0
        ),
        CONSTRAINT CK_BuilderWorkPackageRepositoryContexts_Branch CHECK
        (
            LEN(LTRIM(RTRIM(BranchName))) BETWEEN 1 AND 255 AND
            BranchName=LTRIM(RTRIM(BranchName)) AND
            BranchName NOT LIKE N'% %' AND BranchName NOT LIKE N'%..%' AND
            BranchName NOT LIKE N'%~%' AND BranchName NOT LIKE N'%^%' AND
            BranchName NOT LIKE N'%:%' AND BranchName NOT LIKE N'%?%' AND
            BranchName NOT LIKE N'%*%' AND BranchName NOT LIKE N'%[[]%' AND
            BranchName NOT LIKE N'%\%' AND BranchName NOT LIKE N'/%' AND
            BranchName NOT LIKE N'%/' AND BranchName NOT LIKE N'%.lock'
        ),
        CONSTRAINT CK_BuilderWorkPackageRepositoryContexts_Commit CHECK
        (
            DATALENGTH(BaselineCommit)=40 AND
            BaselineCommit COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        ),
        CONSTRAINT CK_BuilderWorkPackageRepositoryContexts_Hashes CHECK
        (
            LEN(WorktreeFingerprint)=64 AND LEN(BuildCommandSha256)=64 AND
            LEN(TestCommandSha256)=64 AND
            WorktreeFingerprint COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            BuildCommandSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            TestCommandSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        ),
        CONSTRAINT CK_BuilderWorkPackageRepositoryContexts_Strings CHECK
        (
            LEN(LTRIM(RTRIM(SandboxPolicyVersion))) > 0 AND
            LEN(LTRIM(RTRIM(ToolchainManifestId))) > 0
        )
    );
END;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderWorkPackageRepositoryContexts_ValidateAuthority
  ON dbo.BuilderWorkPackageRepositoryContexts AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        INNER JOIN dbo.BuilderWorkPackageCores core
            ON core.TenantId=value.TenantId
           AND core.ProjectId=value.ProjectId
           AND core.Id=value.BuilderWorkPackageCoreId
        INNER JOIN dbo.RepositoryBindings binding
            ON binding.TenantId=value.TenantId
           AND binding.ProjectId=value.ProjectId
           AND binding.Id=value.RepositoryBindingId
        INNER JOIN dbo.ProjectExecutionProfiles profile
            ON profile.TenantId=value.TenantId
           AND profile.ProjectId=value.ProjectId
           AND profile.Id=value.ProjectExecutionProfileId
        INNER JOIN dbo.RepositoryStateObservations observation
            ON observation.TenantId=value.TenantId
           AND observation.ProjectId=value.ProjectId
           AND observation.Id=value.RepositoryStateObservationId
        INNER JOIN dbo.CodeIndexSnapshots codeIndex
            ON codeIndex.TenantId=value.TenantId
           AND codeIndex.ProjectId=value.ProjectId
           AND codeIndex.Id=value.CodeIndexSnapshotId
        INNER JOIN dbo.TechnicalValidationAttempts attempt
            ON attempt.TenantId=value.TenantId
           AND attempt.ProjectId=value.ProjectId
           AND attempt.Id=observation.TechnicalValidationAttemptId
        INNER JOIN dbo.ProjectTechnicalReadinessEvidence evidence
            ON evidence.TenantId=value.TenantId
           AND evidence.ProjectId=value.ProjectId
           AND evidence.TechnicalValidationAttemptId=attempt.Id
        INNER JOIN dbo.BuilderModelConfigurationRecords model
            ON model.TenantId=value.TenantId
           AND model.ProjectId=value.ProjectId
           AND model.Id=evidence.BuilderModelConfigurationRecordId
        LEFT JOIN dbo.vw_WorkbenchEffectiveProjectReadiness readiness
            ON readiness.TenantId=value.TenantId
           AND readiness.ProjectId=value.ProjectId
        WHERE readiness.ExecutionReadiness IS NULL OR readiness.ExecutionReadiness<>N''Ready'' OR
              readiness.TechnicalReadinessEvidenceId<>evidence.Id OR
              binding.BindingState<>N''Qualified'' OR
              binding.CurrentRevision<>value.RepositoryBindingRevision OR
              binding.BaselineCommit<>value.BaselineCommit OR
              profile.RepositoryBindingId<>value.RepositoryBindingId OR
              profile.CurrentRevision<>value.ProjectExecutionProfileRevision OR
              observation.RepositoryBindingId<>value.RepositoryBindingId OR
              observation.RepositoryBindingRevision<>value.RepositoryBindingRevision OR
              observation.HeadCommit<>value.BaselineCommit OR
              observation.DirtyState<>N''Clean'' OR
              observation.RepositoryFingerprintSha256<>value.WorktreeFingerprint OR
              observation.ProjectExecutionProfileId<>value.ProjectExecutionProfileId OR
              observation.ProjectExecutionProfileRevision<>value.ProjectExecutionProfileRevision OR
              codeIndex.TechnicalValidationAttemptId<>attempt.Id OR
              codeIndex.RepositoryStateObservationId<>value.RepositoryStateObservationId OR
              codeIndex.RepositoryFingerprintSha256<>value.WorktreeFingerprint OR
              codeIndex.IndexState<>N''Ready'' OR
              attempt.AttemptNumber<>value.CodeIndexSnapshotRevision OR
              attempt.RepositoryBindingId<>value.RepositoryBindingId OR
              attempt.RepositoryBindingRevision<>value.RepositoryBindingRevision OR
              attempt.BaselineCommit<>value.BaselineCommit OR
              attempt.ProjectExecutionProfileId<>value.ProjectExecutionProfileId OR
              attempt.ProjectExecutionProfileRevision<>value.ProjectExecutionProfileRevision OR
              attempt.BuildCommandSha256<>value.BuildCommandSha256 OR
              attempt.TestCommandSha256<>value.TestCommandSha256 OR
              attempt.SandboxPolicyVersion<>value.SandboxPolicyVersion OR
              attempt.ToolchainManifestId<>value.ToolchainManifestId OR
              TRY_CONVERT(INT, JSON_VALUE(core.CanonicalJson, N''$.tenantId''))<>value.TenantId OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.id''))<>
                    evidence.ProjectReadinessAssessmentId OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.revision''))<>
                    evidence.ProjectReadinessRevision OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.technicalEvidenceId''))<>
                    evidence.Id OR
              JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.evidenceSha256'')<>
                    evidence.EvidenceSha256 OR
              TRY_CONVERT(DATETIME2(7),
                  JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.assessedAtUtc''), 127)<>
                    evidence.AssessedAtUtc OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.evidenceSha256'')<>
                    evidence.RepositoryObservationEvidenceSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.headCommit'')<>
                    observation.HeadCommit OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.gitTreeId'')<>
                    observation.GitTreeId OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.worktreeState'')<>
                    observation.DirtyState OR
              TRY_CONVERT(DATETIME2(7),
                  JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.observedAtUtc''), 127)<>
                    observation.ObservedAtUtc OR
              JSON_VALUE(core.CanonicalJson, N''$.codeIndex.evidenceSha256'')<>
                    evidence.CodeIndexEvidenceSha256 OR
              TRY_CONVERT(INT, JSON_VALUE(core.CanonicalJson, N''$.codeIndex.schemaVersion''))<>
                    codeIndex.IndexSchemaVersion OR
              JSON_VALUE(core.CanonicalJson, N''$.codeIndex.indexerVersion'')<>
                    codeIndex.IndexerVersion OR
              JSON_VALUE(core.CanonicalJson, N''$.codeIndex.indexedContentSha256'')<>
                    codeIndex.IndexContentSha256 OR
              JSON_QUERY(core.CanonicalJson, N''$.codeIndex.sources'')<>codeIndex.SourcesJson OR
              TRY_CONVERT(DATETIME2(7),
                  JSON_VALUE(core.CanonicalJson, N''$.codeIndex.indexedAtUtc''), 127)<>
                    codeIndex.CompletedAtUtc OR
              JSON_VALUE(core.CanonicalJson, N''$.restoreCommandSha256'')<>
                    evidence.RestoreCommandSha256 OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.builderConfigurationId''))<>
                    model.ConfigurationId OR
              TRY_CONVERT(BIGINT,
                  JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.builderConfigurationRevision''))<>
                    model.ConfigurationRevision OR
              JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.providerId'')<>model.ProviderId OR
              JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.modelId'')<>model.ModelId OR
              JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.builderConfigurationSha256'')<>
                    model.ConfigurationSha256 OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.sandbox.qualificationAttemptId''))<>
                    evidence.SandboxQualificationAttemptId OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.sandbox.evidenceManifestId''))<>
                    evidence.SandboxEvidenceManifestId OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.evidenceManifestSha256'')<>
                    evidence.SandboxEvidenceManifestSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.policySha256'')<>
                    evidence.SandboxPolicySha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.qualifiedImageDigest'')<>
                    evidence.ContainerImageDigestSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.toolchainManifestSha256'')<>
                    evidence.ToolchainManifestSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.offlineFeedManifestSha256'')<>
                    evidence.OfflineFeedManifestSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.templateBundleSha256'')<>
                    evidence.TemplateBundleSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryBindingId'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryBindingRevision'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.branchName'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.baselineCommit'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.id'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.worktreeFingerprint'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.projectExecutionProfileId'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.projectExecutionProfileRevision'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.codeIndex.id'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.codeIndex.revision'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.buildCommandSha256'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.testCommandSha256'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.policyVersion'') IS NULL OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.toolchainManifestId'') IS NULL OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.repositoryBindingId''))<>
                    value.RepositoryBindingId OR
              TRY_CONVERT(BIGINT,
                  JSON_VALUE(core.CanonicalJson, N''$.repositoryBindingRevision''))<>
                    value.RepositoryBindingRevision OR
              JSON_VALUE(core.CanonicalJson, N''$.branchName'')<>value.BranchName OR
              JSON_VALUE(core.CanonicalJson, N''$.baselineCommit'')<>value.BaselineCommit OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.id''))<>
                    value.RepositoryStateObservationId OR
              JSON_VALUE(core.CanonicalJson, N''$.repositoryObservation.worktreeFingerprint'')<>
                    value.WorktreeFingerprint OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.projectExecutionProfileId''))<>
                    value.ProjectExecutionProfileId OR
              TRY_CONVERT(BIGINT,
                  JSON_VALUE(core.CanonicalJson, N''$.effectiveProfile.projectExecutionProfileRevision''))<>
                    value.ProjectExecutionProfileRevision OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.codeIndex.id''))<>
                    value.CodeIndexSnapshotId OR
              TRY_CONVERT(BIGINT,
                  JSON_VALUE(core.CanonicalJson, N''$.codeIndex.revision''))<>
                    value.CodeIndexSnapshotRevision OR
              JSON_VALUE(core.CanonicalJson, N''$.buildCommandSha256'')<>
                    value.BuildCommandSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.testCommandSha256'')<>
                    value.TestCommandSha256 OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.policyVersion'')<>
                    value.SandboxPolicyVersion OR
              JSON_VALUE(core.CanonicalJson, N''$.sandbox.toolchainManifestId'')<>
                    value.ToolchainManifestId
    )
        THROW 51400, ''Builder work-package repository context is not exact, current Ready authority.'', 1;
END;');
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderWorkPackageTickets_ValidateCanonical
  ON dbo.BuilderWorkPackageTickets AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        INNER JOIN dbo.BuilderWorkPackageCores core
            ON core.TenantId=value.TenantId
           AND core.ProjectId=value.ProjectId
           AND core.Id=value.BuilderWorkPackageCoreId
        INNER JOIN dbo.ProjectTickets ticket
            ON ticket.TenantId=value.TenantId
           AND ticket.ProjectId=value.ProjectId
           AND ticket.Id=value.TicketId
        INNER JOIN dbo.WorkItems workItem
            ON workItem.TenantId=ticket.TenantId
           AND workItem.ProjectId=ticket.ProjectId
           AND workItem.LegacyTicketId=ticket.Id
        INNER JOIN dbo.WorkItemContracts contract
            ON contract.TenantId=workItem.TenantId
           AND contract.ProjectId=workItem.ProjectId
           AND contract.WorkItemId=workItem.Id
           AND contract.Id=workItem.CurrentContractId
        WHERE ticket.Revision<>value.TicketRevision OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].workItemId''))<>
                    workItem.Id OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].workItemVersion''))<>
                    workItem.Version OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].workItemContractId''))<>
                    contract.Id OR
              TRY_CONVERT(INT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].workItemContractRevision''))<>
                    contract.ContractVersion OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].workItemContractSha256'')<>
                    contract.ContractHash OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ordinal'') IS NULL OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ticketId'') IS NULL OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ticketRevision'') IS NULL OR
              TRY_CONVERT(INT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ordinal''))<>
                    value.Ordinal OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ticketId''))<>
                    value.TicketId OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.tickets['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ticketRevision''))<>
                    value.TicketRevision
    )
        THROW 51401, ''Builder work-package ticket does not match its current ticket revision and canonical core.'', 1;
END;');
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderWorkPackageArtifactReferences_ValidateCanonical
  ON dbo.BuilderWorkPackageArtifactReferences AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        INNER JOIN dbo.BuilderWorkPackageCores core
            ON core.TenantId=value.TenantId
           AND core.ProjectId=value.ProjectId
           AND core.Id=value.BuilderWorkPackageCoreId
        WHERE JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ordinal'') IS NULL OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].artifactKind'') IS NULL OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].artifactReferenceId'') IS NULL OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].revision'') IS NULL OR
              TRY_CONVERT(INT, JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].ordinal''))<>
                    value.Ordinal OR
              JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].artifactKind'')<>
                    value.ArtifactKind OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].artifactReferenceId''))<>
                    value.ArtifactReferenceId OR
              TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                  N''$.governingArtifacts['' + CONVERT(NVARCHAR(20), value.Ordinal-1) + N''].revision''))<>
                    value.ArtifactRevision
    )
        THROW 51402, ''Builder work-package artifact does not match its canonical core.'', 1;
END;');
GO

DECLARE @AppendOnlyBuilderTables TABLE
(
    TableName SYSNAME NOT NULL,
    TriggerName SYSNAME NOT NULL,
    ErrorNumber INT NOT NULL
);

INSERT @AppendOnlyBuilderTables(TableName, TriggerName, ErrorNumber)
VALUES
    (N'BuilderWorkPackageCores', N'TR_BuilderWorkPackageCores_AppendOnly', 51403),
    (N'BuilderWorkPackageTickets', N'TR_BuilderWorkPackageTickets_AppendOnly', 51404),
    (N'BuilderWorkPackageArtifactReferences',
     N'TR_BuilderWorkPackageArtifactReferences_AppendOnly', 51405),
    (N'BuilderWorkPackageRepositoryContexts',
     N'TR_BuilderWorkPackageRepositoryContexts_AppendOnly', 51406);

DECLARE @BuilderTable SYSNAME;
DECLARE @BuilderTrigger SYSNAME;
DECLARE @BuilderError INT;
DECLARE @BuilderTriggerSql NVARCHAR(MAX);
DECLARE append_only_builder_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName, TriggerName, ErrorNumber FROM @AppendOnlyBuilderTables;

OPEN append_only_builder_cursor;
FETCH NEXT FROM append_only_builder_cursor
    INTO @BuilderTable, @BuilderTrigger, @BuilderError;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @BuilderTriggerSql=
        N'CREATE OR ALTER TRIGGER dbo.' + QUOTENAME(@BuilderTrigger) +
        N' ON dbo.' + QUOTENAME(@BuilderTable) +
        N' AFTER UPDATE, DELETE AS
           BEGIN
             SET NOCOUNT ON;
             THROW ' + CONVERT(NVARCHAR(20), @BuilderError) +
             N', ''' + REPLACE(@BuilderTable, N'''', N'''''') +
             N' is append-only.'', 1;
           END;';
    EXEC sys.sp_executesql @BuilderTriggerSql;
    FETCH NEXT FROM append_only_builder_cursor
        INTO @BuilderTable, @BuilderTrigger, @BuilderError;
END;
CLOSE append_only_builder_cursor;
DEALLOCATE append_only_builder_cursor;
GO

IF OBJECT_ID(N'dbo.BuilderExecutionAuthorizations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderExecutionAuthorizations
    (
        Id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BuilderExecutionAuthorizations PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ActorUserId INT NOT NULL,
        BuilderWorkPackageCoreId UNIQUEIDENTIFIER NOT NULL,
        BuilderWorkPackageCoreHash CHAR(64) NOT NULL,
        GrantedAtUtc DATETIME2(7) NOT NULL,
        ExpiresAtUtc DATETIME2(7) NOT NULL,
        SingleUse BIT NOT NULL,
        ConsumedAtUtc DATETIME2(7) NULL,
        ConsumedByBuilderExecutionRunId UNIQUEIDENTIFIER NULL,
        RevokedAtUtc DATETIME2(7) NULL,
        GrantedByWorkbenchSessionId BIGINT NOT NULL,
        GrantedUnderLeaseEpoch BIGINT NOT NULL,
        GrantClientOperationRecordId BIGINT NOT NULL,
        GrantClientOperationId UNIQUEIDENTIFIER NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_BuilderExecutionAuthorizations_ProjectId UNIQUE
            (TenantId, ProjectId, Id),
        CONSTRAINT UQ_BuilderExecutionAuthorizations_GrantOperation UNIQUE
            (GrantClientOperationRecordId),
        CONSTRAINT FK_BuilderExecutionAuthorizations_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_BuilderExecutionAuthorizations_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_BuilderExecutionAuthorizations_ProjectMember
            FOREIGN KEY (TenantId, ProjectId, ActorUserId)
            REFERENCES dbo.ProjectMembers(TenantId, ProjectId, UserId),
        CONSTRAINT FK_BuilderExecutionAuthorizations_CoreHash
            FOREIGN KEY
                (TenantId, ProjectId, BuilderWorkPackageCoreId,
                 BuilderWorkPackageCoreHash)
            REFERENCES dbo.BuilderWorkPackageCores
                (TenantId, ProjectId, Id, CoreHash),
        CONSTRAINT FK_BuilderExecutionAuthorizations_GrantFence
            FOREIGN KEY
                (TenantId, ProjectId, GrantedByWorkbenchSessionId,
                 GrantedUnderLeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT FK_BuilderExecutionAuthorizations_GrantOperation
            FOREIGN KEY
            (
                GrantClientOperationRecordId, TenantId, ActorUserId,
                GrantClientOperationId, ProjectId, GrantedByWorkbenchSessionId
            )
            REFERENCES dbo.ClientOperations
            (
                Id, TenantId, ActorUserId, ClientOperationId,
                ResultProjectId, ResultWorkbenchSessionId
            ),
        CONSTRAINT CK_BuilderExecutionAuthorizations_Hash CHECK
        (
            LEN(BuilderWorkPackageCoreHash)=64 AND
            BuilderWorkPackageCoreHash COLLATE Latin1_General_100_BIN2
                NOT LIKE '%[^0-9a-f]%'
        ),
        CONSTRAINT CK_BuilderExecutionAuthorizations_SingleUse CHECK
            (SingleUse=1),
        CONSTRAINT CK_BuilderExecutionAuthorizations_Expiry CHECK
            (ExpiresAtUtc=DATEADD(MINUTE, 15, GrantedAtUtc)),
        CONSTRAINT CK_BuilderExecutionAuthorizations_Fence CHECK
            (GrantedUnderLeaseEpoch > 0),
        CONSTRAINT CK_BuilderExecutionAuthorizations_TerminalState CHECK
        (
            (ConsumedAtUtc IS NULL AND ConsumedByBuilderExecutionRunId IS NULL) OR
            (ConsumedAtUtc IS NOT NULL AND ConsumedAtUtc>=GrantedAtUtc AND
             ConsumedByBuilderExecutionRunId IS NOT NULL AND
             ConsumedByBuilderExecutionRunId<>
                CONVERT(UNIQUEIDENTIFIER, '00000000-0000-0000-0000-000000000000'))
        ),
        CONSTRAINT CK_BuilderExecutionAuthorizations_ExclusiveTerminal CHECK
            (ConsumedAtUtc IS NULL OR RevokedAtUtc IS NULL),
        CONSTRAINT CK_BuilderExecutionAuthorizations_Revocation CHECK
            (RevokedAtUtc IS NULL OR RevokedAtUtc>=GrantedAtUtc)
    );

    CREATE INDEX IX_BuilderExecutionAuthorizations_ProjectTime
        ON dbo.BuilderExecutionAuthorizations
            (TenantId, ProjectId, GrantedAtUtc DESC, Id);
    CREATE INDEX IX_BuilderExecutionAuthorizations_CoreTime
        ON dbo.BuilderExecutionAuthorizations
            (TenantId, ProjectId, BuilderWorkPackageCoreId, GrantedAtUtc DESC);
END;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderExecutionAuthorizations_ValidateGrant
  ON dbo.BuilderExecutionAuthorizations AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted value
        INNER JOIN dbo.ClientOperations operation
            ON operation.Id=value.GrantClientOperationRecordId
        INNER JOIN dbo.BuilderWorkPackageCores core
            ON core.TenantId=value.TenantId
           AND core.ProjectId=value.ProjectId
           AND core.Id=value.BuilderWorkPackageCoreId
        INNER JOIN dbo.ProjectMembers member
            ON member.TenantId=value.TenantId
           AND member.ProjectId=value.ProjectId
           AND member.UserId=value.ActorUserId
        INNER JOIN dbo.Users actor ON actor.Id=value.ActorUserId
        OUTER APPLY
        (
            SELECT COUNT(*) AS TicketCount,
                   COUNT(DISTINCT ticket.Ordinal) AS DistinctOrdinalCount,
                   MIN(ticket.Ordinal) AS MinimumOrdinal,
                   MAX(ticket.Ordinal) AS MaximumOrdinal
            FROM dbo.BuilderWorkPackageTickets ticket
            WHERE ticket.TenantId=value.TenantId
              AND ticket.ProjectId=value.ProjectId
              AND ticket.BuilderWorkPackageCoreId=value.BuilderWorkPackageCoreId
        ) tickets
        OUTER APPLY
        (
            SELECT COUNT(*) AS ArtifactCount,
                   COUNT(DISTINCT artifact.Ordinal) AS DistinctOrdinalCount,
                   MIN(artifact.Ordinal) AS MinimumOrdinal,
                   MAX(artifact.Ordinal) AS MaximumOrdinal
            FROM dbo.BuilderWorkPackageArtifactReferences artifact
            WHERE artifact.TenantId=value.TenantId
              AND artifact.ProjectId=value.ProjectId
              AND artifact.BuilderWorkPackageCoreId=value.BuilderWorkPackageCoreId
        ) artifacts
        WHERE operation.OperationKind<>N''GrantBuilderExecutionAuthorization'' OR
              operation.Status<>N''Pending'' OR
              operation.ResultProjectId<>value.ProjectId OR
              operation.ResultWorkbenchSessionId<>value.GrantedByWorkbenchSessionId OR
              member.Status<>N''Active'' OR member.ProjectRole=N''Viewer'' OR
              actor.IsActive<>1 OR
              core.CoreHash<>value.BuilderWorkPackageCoreHash OR
              NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.BuilderWorkPackageRepositoryContexts contextValue
                  WHERE contextValue.TenantId=value.TenantId
                    AND contextValue.ProjectId=value.ProjectId
                    AND contextValue.BuilderWorkPackageCoreId=value.BuilderWorkPackageCoreId
              ) OR
              tickets.TicketCount<1 OR
              tickets.TicketCount>100 OR
              tickets.DistinctOrdinalCount<>tickets.TicketCount OR
              tickets.MinimumOrdinal<>1 OR
              tickets.MaximumOrdinal<>tickets.TicketCount OR
              tickets.TicketCount<>
                (SELECT COUNT(*) FROM OPENJSON(core.CanonicalJson, N''$.tickets'')) OR
              artifacts.ArtifactCount<1 OR
              artifacts.ArtifactCount>100 OR
              artifacts.DistinctOrdinalCount<>artifacts.ArtifactCount OR
              artifacts.MinimumOrdinal<>1 OR
              artifacts.MaximumOrdinal<>artifacts.ArtifactCount OR
              artifacts.ArtifactCount<>
                (SELECT COUNT(*) FROM OPENJSON(core.CanonicalJson, N''$.governingArtifacts''))
    )
        THROW 51407, ''Builder execution authorization does not bind one complete exact work package and grant operation.'', 1;
END;');
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderExecutionAuthorizations_TerminalImmutable
  ON dbo.BuilderExecutionAuthorizations AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
        THROW 51408, ''Builder execution authorizations cannot be deleted.'', 1;
    IF UPDATE(Id) OR UPDATE(TenantId) OR UPDATE(ProjectId) OR UPDATE(ActorUserId) OR
       UPDATE(BuilderWorkPackageCoreId) OR UPDATE(BuilderWorkPackageCoreHash) OR
       UPDATE(GrantedAtUtc) OR UPDATE(ExpiresAtUtc) OR UPDATE(SingleUse) OR
       UPDATE(GrantedByWorkbenchSessionId) OR UPDATE(GrantedUnderLeaseEpoch) OR
       UPDATE(GrantClientOperationRecordId) OR UPDATE(GrantClientOperationId)
        THROW 51409, ''Builder execution authorization scope is immutable.'', 1;
    IF EXISTS
    (
        SELECT 1
        FROM deleted d
        INNER JOIN inserted i ON i.Id=d.Id
        WHERE d.ConsumedAtUtc IS NOT NULL OR d.RevokedAtUtc IS NOT NULL OR
              (i.ConsumedAtUtc IS NULL AND i.RevokedAtUtc IS NULL)
    )
        THROW 51410, ''Only one unconsumed-to-consumed or unconsumed-to-revoked transition is allowed.'', 1;
END;');
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultBuilderWorkPackageCoreId') IS NULL
    ALTER TABLE dbo.ClientOperations
        ADD ResultBuilderWorkPackageCoreId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultBuilderExecutionAuthorizationId') IS NULL
    ALTER TABLE dbo.ClientOperations
        ADD ResultBuilderExecutionAuthorizationId UNIQUEIDENTIFIER NULL;
GO

IF OBJECT_ID(N'dbo.CK_ClientOperations_BuilderResultAuthority', N'C') IS NOT NULL
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT CK_ClientOperations_BuilderResultAuthority;
GO

ALTER TABLE dbo.ClientOperations WITH CHECK
    ADD CONSTRAINT CK_ClientOperations_BuilderResultAuthority CHECK
    (
        (
            ResultBuilderWorkPackageCoreId IS NULL OR
            (
                ResultProjectId IS NOT NULL AND
                OperationKind IN
                    (N'CreateBuilderWorkPackage', N'GrantBuilderExecutionAuthorization')
            )
        ) AND
        (
            ResultBuilderExecutionAuthorizationId IS NULL OR
            (
                ResultProjectId IS NOT NULL AND
                OperationKind IN
                    (N'GrantBuilderExecutionAuthorization',
                     N'RevokeBuilderExecutionAuthorization')
            )
        )
    );
GO

IF OBJECT_ID(N'dbo.FK_ClientOperations_BuilderWorkPackageCore', N'F') IS NULL
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_BuilderWorkPackageCore
        FOREIGN KEY
            (TenantId, ResultProjectId, ResultBuilderWorkPackageCoreId)
        REFERENCES dbo.BuilderWorkPackageCores(TenantId, ProjectId, Id);
GO

IF OBJECT_ID(N'dbo.FK_ClientOperations_BuilderExecutionAuthorization', N'F') IS NULL
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_BuilderExecutionAuthorization
        FOREIGN KEY
            (TenantId, ResultProjectId, ResultBuilderExecutionAuthorizationId)
        REFERENCES dbo.BuilderExecutionAuthorizations(TenantId, ProjectId, Id);
GO

IF COL_LENGTH(N'dbo.WorkbenchOutboxEvents', N'BuilderWorkPackageCoreId') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents
        ADD BuilderWorkPackageCoreId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.WorkbenchOutboxEvents', N'BuilderExecutionAuthorizationId') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents
        ADD BuilderExecutionAuthorizationId UNIQUEIDENTIFIER NULL;
GO

IF OBJECT_ID(N'dbo.FK_WorkbenchOutboxEvents_BuilderWorkPackageCore', N'F') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents WITH CHECK
        ADD CONSTRAINT FK_WorkbenchOutboxEvents_BuilderWorkPackageCore
        FOREIGN KEY (TenantId, ProjectId, BuilderWorkPackageCoreId)
        REFERENCES dbo.BuilderWorkPackageCores(TenantId, ProjectId, Id);
GO

IF OBJECT_ID(N'dbo.FK_WorkbenchOutboxEvents_BuilderExecutionAuthorization', N'F') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents WITH CHECK
        ADD CONSTRAINT FK_WorkbenchOutboxEvents_BuilderExecutionAuthorization
        FOREIGN KEY (TenantId, ProjectId, BuilderExecutionAuthorizationId)
        REFERENCES dbo.BuilderExecutionAuthorizations(TenantId, ProjectId, Id);
GO

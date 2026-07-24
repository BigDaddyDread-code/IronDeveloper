/*
   Workbench v0.1 PR-07B: atomically consume one exact Builder authorization,
   freeze the Builder profile and role context, and create a durable prepared
   Builder AgentRun. This migration does not invoke a provider or execute tools.
*/

IF OBJECT_ID(N'dbo.BuilderAgentRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderAgentRuns
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BuilderAgentRuns PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ActorUserId INT NOT NULL,
        BuilderExecutionAuthorizationId UNIQUEIDENTIFIER NOT NULL,
        BuilderWorkPackageCoreId UNIQUEIDENTIFIER NOT NULL,
        BuilderWorkPackageCoreSha256 CHAR(64) NOT NULL,
        BuilderAgentVersion NVARCHAR(100) NOT NULL,
        PromptVersion NVARCHAR(100) NOT NULL,
        ToolPolicyVersion NVARCHAR(100) NOT NULL,
        ContextSchemaVersion NVARCHAR(100) NOT NULL,
        OutputSchemaVersion NVARCHAR(100) NOT NULL,
        EffectiveProfileJson NVARCHAR(MAX) NOT NULL,
        EffectiveProfileSha256 CHAR(64) NOT NULL,
        SystemPrompt NVARCHAR(MAX) NOT NULL,
        PromptSha256 CHAR(64) NOT NULL,
        RoleContextJson NVARCHAR(MAX) NOT NULL,
        RoleContextSha256 CHAR(64) NOT NULL,
        ToolManifestJson NVARCHAR(MAX) NOT NULL,
        ToolManifestSha256 CHAR(64) NOT NULL,
        ProviderInputJson NVARCHAR(MAX) NOT NULL,
        ProviderInputSha256 CHAR(64) NOT NULL,
        ObservedBranchName NVARCHAR(255) NOT NULL,
        ObservedHeadCommit CHAR(40) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        PreparedAtUtc DATETIME2(7) NOT NULL,
        ProviderInvocationPermittedAtUtc DATETIME2(7) NOT NULL,
        ProviderInvokedAtUtc DATETIME2(7) NULL,
        ClientOperationRecordId BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_BuilderAgentRuns_ProjectId UNIQUE (TenantId, ProjectId, Id),
        CONSTRAINT UQ_BuilderAgentRuns_Authorization UNIQUE (BuilderExecutionAuthorizationId),
        CONSTRAINT UQ_BuilderAgentRuns_ClientOperation UNIQUE (ClientOperationRecordId),
        CONSTRAINT FK_BuilderAgentRuns_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_BuilderAgentRuns_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_BuilderAgentRuns_Authorization
            FOREIGN KEY (TenantId, ProjectId, BuilderExecutionAuthorizationId)
            REFERENCES dbo.BuilderExecutionAuthorizations(TenantId, ProjectId, Id),
        CONSTRAINT FK_BuilderAgentRuns_CoreHash
            FOREIGN KEY
                (TenantId, ProjectId, BuilderWorkPackageCoreId, BuilderWorkPackageCoreSha256)
            REFERENCES dbo.BuilderWorkPackageCores(TenantId, ProjectId, Id, CoreHash),
        CONSTRAINT FK_BuilderAgentRuns_ClientOperation
            FOREIGN KEY (ClientOperationRecordId) REFERENCES dbo.ClientOperations(Id),
        CONSTRAINT FK_BuilderAgentRuns_Fence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT CK_BuilderAgentRuns_Status CHECK (Status=N'Prepared'),
        CONSTRAINT CK_BuilderAgentRuns_NoInvocation CHECK (ProviderInvokedAtUtc IS NULL),
        CONSTRAINT CK_BuilderAgentRuns_PermissionTime CHECK
            (ProviderInvocationPermittedAtUtc=PreparedAtUtc),
        CONSTRAINT CK_BuilderAgentRuns_Json CHECK
        (
            ISJSON(EffectiveProfileJson)=1 AND ISJSON(RoleContextJson)=1 AND
            ISJSON(ToolManifestJson)=1 AND ISJSON(ProviderInputJson)=1
        ),
        CONSTRAINT CK_BuilderAgentRuns_Hashes CHECK
        (
            LEN(BuilderWorkPackageCoreSha256)=64 AND
            LEN(EffectiveProfileSha256)=64 AND LEN(PromptSha256)=64 AND
            LEN(RoleContextSha256)=64 AND LEN(ToolManifestSha256)=64 AND
            LEN(ProviderInputSha256)=64 AND
            BuilderWorkPackageCoreSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            EffectiveProfileSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            PromptSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            RoleContextSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            ToolManifestSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            ProviderInputSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        )
    );

    CREATE INDEX IX_BuilderAgentRuns_ProjectTime
        ON dbo.BuilderAgentRuns(TenantId, ProjectId, PreparedAtUtc DESC, Id);
END;
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderAgentRuns_ValidatePreparation
ON dbo.BuilderAgentRuns AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        INNER JOIN dbo.BuilderExecutionAuthorizations authz WITH (UPDLOCK, HOLDLOCK)
            ON authz.TenantId=run.TenantId
           AND authz.ProjectId=run.ProjectId
           AND authz.Id=run.BuilderExecutionAuthorizationId
        INNER JOIN dbo.BuilderWorkPackageCores core
            ON core.TenantId=run.TenantId AND core.ProjectId=run.ProjectId
           AND core.Id=run.BuilderWorkPackageCoreId
        INNER JOIN dbo.BuilderWorkPackageRepositoryContexts repositoryContext
            ON repositoryContext.TenantId=run.TenantId
           AND repositoryContext.ProjectId=run.ProjectId
           AND repositoryContext.BuilderWorkPackageCoreId=run.BuilderWorkPackageCoreId
        INNER JOIN dbo.RepositoryBindings binding
            ON binding.TenantId=run.TenantId AND binding.ProjectId=run.ProjectId
           AND binding.Id=repositoryContext.RepositoryBindingId
        LEFT JOIN dbo.vw_WorkbenchEffectiveProjectReadiness readiness
            ON readiness.TenantId=run.TenantId AND readiness.ProjectId=run.ProjectId
        LEFT JOIN dbo.ProjectTechnicalReadinessEvidence evidence
            ON evidence.TenantId=run.TenantId AND evidence.ProjectId=run.ProjectId
           AND evidence.Id=readiness.TechnicalReadinessEvidenceId
        WHERE authz.ActorUserId<>run.ActorUserId OR
              authz.BuilderWorkPackageCoreId<>run.BuilderWorkPackageCoreId OR
              authz.BuilderWorkPackageCoreHash<>run.BuilderWorkPackageCoreSha256 OR
              authz.SingleUse<>1 OR authz.ConsumedAtUtc IS NOT NULL OR
              authz.ConsumedByBuilderExecutionRunId IS NOT NULL OR
              authz.RevokedAtUtc IS NOT NULL OR
              authz.ExpiresAtUtc<=run.PreparedAtUtc OR
              core.CoreHash<>run.BuilderWorkPackageCoreSha256 OR
              readiness.ExecutionReadiness IS NULL OR readiness.ExecutionReadiness<>N''Ready'' OR
              readiness.TechnicalReadinessEvidenceId IS NULL OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.technicalEvidenceId''))<>
                    readiness.TechnicalReadinessEvidenceId OR
              evidence.EvidenceSha256<>
                    JSON_VALUE(core.CanonicalJson, N''$.readinessAssessment.evidenceSha256'') OR
              binding.BindingState<>N''Qualified'' OR
              binding.CurrentRevision<>repositoryContext.RepositoryBindingRevision OR
              binding.BaselineCommit<>repositoryContext.BaselineCommit OR
              run.ObservedBranchName<>JSON_VALUE(core.CanonicalJson, N''$.branchName'') OR
              run.ObservedHeadCommit<>repositoryContext.BaselineCommit OR
              run.BuilderAgentVersion<>JSON_VALUE(core.CanonicalJson, N''$.builderAgentVersion'') OR
              run.PromptVersion<>JSON_VALUE(core.CanonicalJson, N''$.promptVersion'') OR
              run.ToolPolicyVersion<>JSON_VALUE(core.CanonicalJson, N''$.toolPolicyVersion'') OR
              run.ContextSchemaVersion<>JSON_VALUE(core.CanonicalJson, N''$.contextSchemaVersion'') OR
              run.OutputSchemaVersion<>JSON_VALUE(core.CanonicalJson, N''$.outputSchemaVersion'') OR
              JSON_QUERY(run.EffectiveProfileJson)<>JSON_QUERY(core.CanonicalJson, N''$.effectiveProfile'') OR
              run.EffectiveProfileSha256<>
                LOWER(CONVERT(CHAR(64), HASHBYTES(''SHA2_256'', CONVERT(VARBINARY(MAX),
                    CONVERT(VARCHAR(MAX), run.EffectiveProfileJson COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
              run.PromptSha256<>
                LOWER(CONVERT(CHAR(64), HASHBYTES(''SHA2_256'', CONVERT(VARBINARY(MAX),
                    CONVERT(VARCHAR(MAX), run.SystemPrompt COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
              run.RoleContextSha256<>
                LOWER(CONVERT(CHAR(64), HASHBYTES(''SHA2_256'', CONVERT(VARBINARY(MAX),
                    CONVERT(VARCHAR(MAX), run.RoleContextJson COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
              run.ToolManifestSha256<>
                LOWER(CONVERT(CHAR(64), HASHBYTES(''SHA2_256'', CONVERT(VARBINARY(MAX),
                    CONVERT(VARCHAR(MAX), run.ToolManifestJson COLLATE Latin1_General_100_BIN2_UTF8))), 2)) OR
              run.ProviderInputSha256<>
                LOWER(CONVERT(CHAR(64), HASHBYTES(''SHA2_256'', CONVERT(VARBINARY(MAX),
                    CONVERT(VARCHAR(MAX), run.ProviderInputJson COLLATE Latin1_General_100_BIN2_UTF8))), 2))
    )
        THROW 51500, ''Builder preparation refused because authorization, package, readiness, repository, profile, sandbox, or prompt material is stale or inexact.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        INNER JOIN dbo.BuilderWorkPackageCores core ON core.Id=run.BuilderWorkPackageCoreId
        WHERE TRY_CONVERT(INT, JSON_VALUE(run.RoleContextJson, N''$.materializationVersion''))<>1 OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(run.RoleContextJson, N''$.builderAgentRunId''))<>run.Id OR
              JSON_QUERY(run.RoleContextJson, N''$.package.core'')<>JSON_QUERY(core.CanonicalJson) OR
              JSON_VALUE(run.RoleContextJson, N''$.package.coreSha256'')<>run.BuilderWorkPackageCoreSha256 OR
              TRY_CONVERT(UNIQUEIDENTIFIER,
                  JSON_VALUE(run.RoleContextJson, N''$.package.singleUseAuthorizationId''))<>
                    run.BuilderExecutionAuthorizationId OR
              JSON_VALUE(run.RoleContextJson, N''$.package.singleUse'')<>N''true''
    )
        THROW 51505, ''Prepared Builder role context does not contain the exact authorized package.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        WHERE JSON_VALUE(run.ProviderInputJson, N''$.builderAgentVersion'')<>run.BuilderAgentVersion OR
              JSON_VALUE(run.ProviderInputJson, N''$.promptVersion'')<>run.PromptVersion OR
              JSON_VALUE(run.ProviderInputJson, N''$.contextSchemaVersion'')<>run.ContextSchemaVersion OR
              JSON_VALUE(run.ProviderInputJson, N''$.outputSchemaVersion'')<>run.OutputSchemaVersion OR
              JSON_VALUE(run.ProviderInputJson, N''$.systemPrompt'')<>run.SystemPrompt
    )
        THROW 51506, ''Prepared Builder provider input does not exactly embed the frozen role and prompt contract.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        WHERE JSON_VALUE(run.ProviderInputJson, N''$.roleContextSha256'')<>run.RoleContextSha256 OR
              JSON_VALUE(run.ProviderInputJson, N''$.toolManifestSha256'')<>run.ToolManifestSha256
    )
        THROW 51508, ''Prepared Builder provider input does not exactly bind the frozen context and tool manifest hashes.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        WHERE JSON_VALUE(run.ToolManifestJson, N''$.toolPolicyVersion'')<>run.ToolPolicyVersion OR
              (SELECT COUNT(*) FROM OPENJSON(run.ToolManifestJson, N''$.tools''))<>3 OR
              EXISTS
              (
                  SELECT 1
                  FROM OPENJSON(run.ToolManifestJson, N''$.tools'')
                  WITH
                  (
                      MayUseNetwork BIT N''$.mayUseNetwork'',
                      MayWriteActiveRepository BIT N''$.mayWriteActiveRepository''
                  ) tool
                  WHERE tool.MayUseNetwork<>0 OR tool.MayWriteActiveRepository<>0
              )
    )
        THROW 51507, ''Prepared Builder tool manifest is not the exact non-network, non-active-repository policy.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        INNER JOIN dbo.BuilderWorkPackageTickets packageTicket
            ON packageTicket.TenantId=run.TenantId
           AND packageTicket.ProjectId=run.ProjectId
           AND packageTicket.BuilderWorkPackageCoreId=run.BuilderWorkPackageCoreId
        INNER JOIN dbo.BuilderWorkPackageCores core ON core.Id=run.BuilderWorkPackageCoreId
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.ProjectTickets ticket
            INNER JOIN dbo.WorkItems workItem
                ON workItem.TenantId=ticket.TenantId
               AND workItem.ProjectId=ticket.ProjectId
               AND workItem.LegacyTicketId=ticket.Id
            INNER JOIN dbo.WorkItemContracts contract
                ON contract.TenantId=workItem.TenantId
               AND contract.ProjectId=workItem.ProjectId
               AND contract.WorkItemId=workItem.Id
               AND contract.Id=workItem.CurrentContractId
            WHERE ticket.TenantId=packageTicket.TenantId
              AND ticket.ProjectId=packageTicket.ProjectId
              AND ticket.Id=packageTicket.TicketId
              AND ticket.IsDeleted=0
              AND ticket.Revision=packageTicket.TicketRevision
              AND workItem.Id=TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                N''$.tickets['' + CONVERT(NVARCHAR(20), packageTicket.Ordinal-1) + N''].workItemId''))
              AND workItem.Version=TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                N''$.tickets['' + CONVERT(NVARCHAR(20), packageTicket.Ordinal-1) + N''].workItemVersion''))
              AND contract.Id=TRY_CONVERT(BIGINT, JSON_VALUE(core.CanonicalJson,
                N''$.tickets['' + CONVERT(NVARCHAR(20), packageTicket.Ordinal-1) + N''].workItemContractId''))
              AND contract.ContractVersion=TRY_CONVERT(INT, JSON_VALUE(core.CanonicalJson,
                N''$.tickets['' + CONVERT(NVARCHAR(20), packageTicket.Ordinal-1) + N''].workItemContractRevision''))
              AND contract.ContractHash=JSON_VALUE(core.CanonicalJson,
                N''$.tickets['' + CONVERT(NVARCHAR(20), packageTicket.Ordinal-1) + N''].workItemContractSha256'')
        )
    )
        THROW 51501, ''Builder preparation refused because the exact ticket or Work Item contract changed.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted run
        INNER JOIN dbo.BuilderWorkPackageArtifactReferences artifact
            ON artifact.TenantId=run.TenantId AND artifact.ProjectId=run.ProjectId
           AND artifact.BuilderWorkPackageCoreId=run.BuilderWorkPackageCoreId
        WHERE artifact.ArtifactKind<>N''ProjectUnderstanding'' OR NOT EXISTS
        (
            SELECT 1 FROM dbo.ProjectUnderstandings understanding
            WHERE understanding.TenantId=artifact.TenantId
              AND understanding.ProjectId=artifact.ProjectId
              AND understanding.Id=artifact.ArtifactReferenceId
              AND understanding.Revision=artifact.ArtifactRevision
              AND understanding.Status<>N''Superseded''
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.ProjectUnderstandings newer
                  WHERE newer.TenantId=understanding.TenantId
                    AND newer.ProjectId=understanding.ProjectId
                    AND newer.Status<>N''Superseded''
                    AND (newer.Revision>understanding.Revision OR
                         (newer.Revision=understanding.Revision AND newer.Id>understanding.Id))
              )
        )
    )
        THROW 51502, ''Builder preparation refused because governing role context changed.'', 1;
END;');
GO

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_BuilderAgentRuns_PreparationImmutable
ON dbo.BuilderAgentRuns AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
        THROW 51503, ''Builder AgentRuns cannot be deleted.'', 1;
    IF UPDATE(Id) OR UPDATE(TenantId) OR UPDATE(ProjectId) OR UPDATE(ActorUserId) OR
       UPDATE(BuilderExecutionAuthorizationId) OR UPDATE(BuilderWorkPackageCoreId) OR
       UPDATE(BuilderWorkPackageCoreSha256) OR UPDATE(BuilderAgentVersion) OR
       UPDATE(PromptVersion) OR UPDATE(ToolPolicyVersion) OR UPDATE(ContextSchemaVersion) OR
       UPDATE(OutputSchemaVersion) OR UPDATE(EffectiveProfileJson) OR
       UPDATE(EffectiveProfileSha256) OR UPDATE(SystemPrompt) OR UPDATE(PromptSha256) OR
       UPDATE(RoleContextJson) OR UPDATE(RoleContextSha256) OR UPDATE(ToolManifestJson) OR
       UPDATE(ToolManifestSha256) OR UPDATE(ProviderInputJson) OR
       UPDATE(ProviderInputSha256) OR UPDATE(ObservedBranchName) OR
       UPDATE(ObservedHeadCommit) OR UPDATE(PreparedAtUtc) OR
       UPDATE(ProviderInvocationPermittedAtUtc) OR UPDATE(ClientOperationRecordId) OR
       UPDATE(ClientOperationId) OR UPDATE(WorkbenchSessionId) OR UPDATE(LeaseEpoch)
        THROW 51504, ''Prepared Builder input, authority, hashes, and profile are immutable.'', 1;
END;');
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultBuilderAgentRunId') IS NULL
    ALTER TABLE dbo.ClientOperations ADD ResultBuilderAgentRunId UNIQUEIDENTIFIER NULL;
GO

IF OBJECT_ID(N'dbo.CK_ClientOperations_BuilderPromptPreparationResult', N'C') IS NOT NULL
    ALTER TABLE dbo.ClientOperations
        DROP CONSTRAINT CK_ClientOperations_BuilderPromptPreparationResult;
GO

ALTER TABLE dbo.ClientOperations WITH CHECK
    ADD CONSTRAINT CK_ClientOperations_BuilderPromptPreparationResult CHECK
    (
        ResultBuilderAgentRunId IS NULL OR
        (ResultProjectId IS NOT NULL AND OperationKind=N'PrepareBuilderAgentRun')
    );
GO

IF OBJECT_ID(N'dbo.FK_ClientOperations_BuilderAgentRun', N'F') IS NULL
    ALTER TABLE dbo.ClientOperations WITH CHECK
        ADD CONSTRAINT FK_ClientOperations_BuilderAgentRun
        FOREIGN KEY (TenantId, ResultProjectId, ResultBuilderAgentRunId)
        REFERENCES dbo.BuilderAgentRuns(TenantId, ProjectId, Id);
GO

IF COL_LENGTH(N'dbo.WorkbenchOutboxEvents', N'BuilderAgentRunId') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents ADD BuilderAgentRunId UNIQUEIDENTIFIER NULL;
GO

IF OBJECT_ID(N'dbo.FK_WorkbenchOutboxEvents_BuilderAgentRun', N'F') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents WITH CHECK
        ADD CONSTRAINT FK_WorkbenchOutboxEvents_BuilderAgentRun
        FOREIGN KEY (TenantId, ProjectId, BuilderAgentRunId)
        REFERENCES dbo.BuilderAgentRuns(TenantId, ProjectId, Id);
GO

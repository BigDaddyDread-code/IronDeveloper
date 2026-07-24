/*
   Workbench v0.1 PR-07C: one claimed provider invocation sequence and immutable
   Builder output/evidence. No row grants patch approval or active-repository write.
*/

IF OBJECT_ID(N'dbo.CK_BuilderAgentRuns_Status', N'C') IS NOT NULL
    ALTER TABLE dbo.BuilderAgentRuns DROP CONSTRAINT CK_BuilderAgentRuns_Status;
GO
IF OBJECT_ID(N'dbo.CK_BuilderAgentRuns_NoInvocation', N'C') IS NOT NULL
    ALTER TABLE dbo.BuilderAgentRuns DROP CONSTRAINT CK_BuilderAgentRuns_NoInvocation;
GO
ALTER TABLE dbo.BuilderAgentRuns WITH CHECK ADD CONSTRAINT CK_BuilderAgentRuns_Status
    CHECK (Status IN (N'Prepared', N'Invoking', N'Succeeded', N'Failed'));
GO
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'ExecutionClaimId') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD ExecutionClaimId UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'ExecutionClientOperationId') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD ExecutionClientOperationId UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'ExecutionPayloadSha256') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD ExecutionPayloadSha256 CHAR(64) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'InvocationStartedAtUtc') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD InvocationStartedAtUtc DATETIME2(7) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'CompletedAtUtc') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD CompletedAtUtc DATETIME2(7) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'AttemptCount') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD AttemptCount INT NOT NULL
        CONSTRAINT DF_BuilderAgentRuns_AttemptCount DEFAULT(0);
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'RawPatch') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD RawPatch NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'RawPatchSha256') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD RawPatchSha256 CHAR(64) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'ChangedFileManifestJson') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD ChangedFileManifestJson NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'ChangedFileManifestSha256') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD ChangedFileManifestSha256 CHAR(64) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'SandboxEvidenceManifestSha256') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD SandboxEvidenceManifestSha256 CHAR(64) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'FailureCode') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD FailureCode NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.BuilderAgentRuns', N'FailureEvidence') IS NULL
    ALTER TABLE dbo.BuilderAgentRuns ADD FailureEvidence NVARCHAR(2000) NULL;
GO
IF OBJECT_ID(N'dbo.CK_BuilderAgentRuns_ExecutionState', N'C') IS NOT NULL
    ALTER TABLE dbo.BuilderAgentRuns DROP CONSTRAINT CK_BuilderAgentRuns_ExecutionState;
GO
ALTER TABLE dbo.BuilderAgentRuns WITH CHECK ADD CONSTRAINT CK_BuilderAgentRuns_ExecutionState CHECK
(
    (Status=N'Prepared' AND ExecutionClaimId IS NULL AND ExecutionClientOperationId IS NULL AND
        ExecutionPayloadSha256 IS NULL AND ProviderInvokedAtUtc IS NULL AND
        InvocationStartedAtUtc IS NULL AND CompletedAtUtc IS NULL AND AttemptCount=0) OR
    (Status=N'Invoking' AND ExecutionClaimId IS NOT NULL AND ExecutionClientOperationId IS NOT NULL AND
        ExecutionPayloadSha256 IS NOT NULL AND ProviderInvokedAtUtc IS NOT NULL AND
        InvocationStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NULL AND AttemptCount BETWEEN 0 AND 3) OR
    (Status=N'Succeeded' AND ExecutionClaimId IS NOT NULL AND ExecutionClientOperationId IS NOT NULL AND
        ExecutionPayloadSha256 IS NOT NULL AND ProviderInvokedAtUtc IS NOT NULL AND
        CompletedAtUtc IS NOT NULL AND AttemptCount BETWEEN 1 AND 3 AND RawPatch IS NOT NULL AND
        RawPatchSha256 IS NOT NULL AND ChangedFileManifestJson IS NOT NULL AND
        ChangedFileManifestSha256 IS NOT NULL AND SandboxEvidenceManifestSha256 IS NOT NULL AND
        ISJSON(ChangedFileManifestJson)=1 AND
        RawPatchSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
            CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                RawPatch COLLATE Latin1_General_100_BIN2_UTF8))), 2)) AND
        ChangedFileManifestSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
            CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                ChangedFileManifestJson COLLATE Latin1_General_100_BIN2_UTF8))), 2)) AND
        FailureCode IS NULL AND FailureEvidence IS NULL) OR
    (Status=N'Failed' AND ExecutionClaimId IS NOT NULL AND ExecutionClientOperationId IS NOT NULL AND
        ExecutionPayloadSha256 IS NOT NULL AND ProviderInvokedAtUtc IS NOT NULL AND
        CompletedAtUtc IS NOT NULL AND AttemptCount BETWEEN 1 AND 3 AND
        FailureCode IS NOT NULL AND FailureEvidence IS NOT NULL)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.BuilderAgentRuns')
               AND name=N'UX_BuilderAgentRuns_ExecutionClientOperation')
    CREATE UNIQUE INDEX UX_BuilderAgentRuns_ExecutionClientOperation
        ON dbo.BuilderAgentRuns(TenantId, ActorUserId, ExecutionClientOperationId)
        WHERE ExecutionClientOperationId IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.BuilderAgentRunAttempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderAgentRunAttempts
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BuilderAgentRunAttempts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        BuilderAgentRunId UNIQUEIDENTIFIER NOT NULL,
        AttemptNumber INT NOT NULL,
        SafeRequestId NVARCHAR(100) NOT NULL,
        ProviderRequestId NVARCHAR(200) NULL,
        RawOutput NVARCHAR(MAX) NULL,
        RawOutputSha256 CHAR(64) NULL,
        OutputValid BIT NOT NULL,
        FailureCode NVARCHAR(100) NULL,
        FailureEvidence NVARCHAR(2000) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT UQ_BuilderAgentRunAttempts_Number UNIQUE
            (TenantId, ProjectId, BuilderAgentRunId, AttemptNumber),
        CONSTRAINT FK_BuilderAgentRunAttempts_Run FOREIGN KEY
            (TenantId, ProjectId, BuilderAgentRunId)
            REFERENCES dbo.BuilderAgentRuns(TenantId, ProjectId, Id),
        CONSTRAINT CK_BuilderAgentRunAttempts_Number CHECK (AttemptNumber BETWEEN 1 AND 3),
        CONSTRAINT CK_BuilderAgentRunAttempts_Time CHECK (CompletedAtUtc>=StartedAtUtc),
        CONSTRAINT CK_BuilderAgentRunAttempts_Output CHECK
        (
            (RawOutput IS NULL AND RawOutputSha256 IS NULL) OR
            (RawOutput IS NOT NULL AND RawOutputSha256 IS NOT NULL AND
             RawOutputSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                    RawOutput COLLATE Latin1_General_100_BIN2_UTF8))), 2)))
        ),
        CONSTRAINT CK_BuilderAgentRunAttempts_Result CHECK
        (
            (OutputValid=1 AND FailureCode IS NULL AND FailureEvidence IS NULL) OR
            (OutputValid=0 AND FailureCode IS NOT NULL AND FailureEvidence IS NOT NULL)
        )
    );
END;
GO

IF OBJECT_ID(N'dbo.BuilderAgentRunProposedFiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderAgentRunProposedFiles
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BuilderAgentRunProposedFiles PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        BuilderAgentRunId UNIQUEIDENTIFIER NOT NULL,
        Ordinal INT NOT NULL,
        RelativePath NVARCHAR(1000) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        ContentSha256 CHAR(64) NOT NULL,
        Utf8ByteLength BIGINT NOT NULL,
        CONSTRAINT UQ_BuilderAgentRunProposedFiles_Path UNIQUE
            (TenantId, ProjectId, BuilderAgentRunId, RelativePath),
        CONSTRAINT UQ_BuilderAgentRunProposedFiles_Ordinal UNIQUE
            (TenantId, ProjectId, BuilderAgentRunId, Ordinal),
        CONSTRAINT FK_BuilderAgentRunProposedFiles_Run FOREIGN KEY
            (TenantId, ProjectId, BuilderAgentRunId)
            REFERENCES dbo.BuilderAgentRuns(TenantId, ProjectId, Id),
        CONSTRAINT CK_BuilderAgentRunProposedFiles_Ordinal CHECK (Ordinal BETWEEN 1 AND 1000),
        CONSTRAINT CK_BuilderAgentRunProposedFiles_Size CHECK (Utf8ByteLength BETWEEN 0 AND 2097152),
        CONSTRAINT CK_BuilderAgentRunProposedFiles_Path CHECK
        (
            LEN(LTRIM(RTRIM(RelativePath))) BETWEEN 1 AND 1000 AND
            RelativePath=LTRIM(RTRIM(RelativePath)) AND RelativePath NOT LIKE N'%\%' AND
            RelativePath NOT LIKE N'/%' AND RelativePath NOT LIKE N'%/../%' AND
            RelativePath NOT LIKE N'../%' AND RelativePath NOT LIKE N'%/..' AND
            RelativePath NOT LIKE N'%:%'
        ),
        CONSTRAINT CK_BuilderAgentRunProposedFiles_Hash CHECK
        (
            ContentSha256=LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
                CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX),
                    Content COLLATE Latin1_General_100_BIN2_UTF8))), 2))
        )
    );
END;
GO

IF OBJECT_ID(N'dbo.BuilderAgentRunToolCalls', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BuilderAgentRunToolCalls
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BuilderAgentRunToolCalls PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        BuilderAgentRunId UNIQUEIDENTIFIER NOT NULL,
        Ordinal INT NOT NULL,
        ToolName NVARCHAR(100) NOT NULL,
        ToolVersion NVARCHAR(50) NOT NULL,
        InputSha256 CHAR(64) NOT NULL,
        OutputSha256 CHAR(64) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        CONSTRAINT UQ_BuilderAgentRunToolCalls_Ordinal UNIQUE
            (TenantId, ProjectId, BuilderAgentRunId, Ordinal),
        CONSTRAINT FK_BuilderAgentRunToolCalls_Run FOREIGN KEY
            (TenantId, ProjectId, BuilderAgentRunId)
            REFERENCES dbo.BuilderAgentRuns(TenantId, ProjectId, Id),
        CONSTRAINT CK_BuilderAgentRunToolCalls_Tool CHECK
        (
            ToolName IN
            (N'builder.sandbox.files.read', N'builder.sandbox.files.propose',
             N'builder.sandbox.process.run') AND ToolVersion=N'v1'
        ),
        CONSTRAINT CK_BuilderAgentRunToolCalls_Status CHECK (Status IN (N'Completed', N'Failed'))
        ,CONSTRAINT CK_BuilderAgentRunToolCalls_Hashes CHECK
        (
            LEN(InputSha256)=64 AND LEN(OutputSha256)=64 AND
            InputSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND
            OutputSha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        )
    );
END;
GO

DECLARE @table SYSNAME, @trigger SYSNAME, @error INT, @sql NVARCHAR(MAX);
DECLARE immutable CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM (VALUES
    (N'BuilderAgentRunAttempts', N'TR_BuilderAgentRunAttempts_Immutable', 51610),
    (N'BuilderAgentRunProposedFiles', N'TR_BuilderAgentRunProposedFiles_Immutable', 51611),
    (N'BuilderAgentRunToolCalls', N'TR_BuilderAgentRunToolCalls_Immutable', 51612)
) value(TableName, TriggerName, ErrorNumber);
OPEN immutable;
FETCH NEXT FROM immutable INTO @table, @trigger, @error;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @sql=N'CREATE OR ALTER TRIGGER dbo.'+QUOTENAME(@trigger)+N' ON dbo.'+
        QUOTENAME(@table)+N' AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW '+
        CONVERT(NVARCHAR(20), @error)+N', ''Builder execution evidence is immutable.'', 1; END;';
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM immutable INTO @table, @trigger, @error;
END
CLOSE immutable;
DEALLOCATE immutable;
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
    IF EXISTS
    (
        SELECT 1 FROM deleted d INNER JOIN inserted i ON i.Id=d.Id
        WHERE NOT
        (
            (d.Status=N''Prepared'' AND i.Status=N''Invoking'' AND
             d.ExecutionClaimId IS NULL AND i.ExecutionClaimId IS NOT NULL) OR
            (d.Status=N''Invoking'' AND i.Status IN (N''Invoking'', N''Succeeded'', N''Failed'') AND
             d.ExecutionClaimId=i.ExecutionClaimId)
        )
    )
        THROW 51600, ''Builder AgentRun execution transition is invalid or attempts to reclaim invocation.'', 1;
END;');
GO

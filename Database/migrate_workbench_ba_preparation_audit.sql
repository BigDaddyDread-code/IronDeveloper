/* Workbench v0.1 PR-02B: sanitized, append-only Business Analyst preparation provenance. */

IF OBJECT_ID(N'dbo.WorkbenchAgentRunAttempts', N'U') IS NULL
    THROW 51020, 'Workbench Business Analyst preparation audit requires WorkbenchAgentRunAttempts.', 1;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchAgentRunAttempts')
      AND name=N'UX_WorkbenchAgentRunAttempts_ExactIdentity'
)
    CREATE UNIQUE INDEX UX_WorkbenchAgentRunAttempts_ExactIdentity
        ON dbo.WorkbenchAgentRunAttempts(Id, AgentRunId, ClaimToken, AttemptNumber);
GO

IF OBJECT_ID(N'dbo.WorkbenchBusinessAnalystPreparations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchBusinessAnalystPreparations
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkbenchBusinessAnalystPreparations PRIMARY KEY,
        AgentRunAttemptId BIGINT NOT NULL,
        AgentRunId UNIQUEIDENTIFIER NOT NULL,
        ClaimToken UNIQUEIDENTIFIER NOT NULL,
        AttemptNumber INT NOT NULL,
        EffectiveAnalystProfileHash CHAR(64) NOT NULL,
        AnalystProfilePublishedVersion BIGINT NULL,
        ActualProvider NVARCHAR(80) NOT NULL,
        ActualModel NVARCHAR(200) NOT NULL,
        ProviderTimeoutSeconds INT NOT NULL,
        PromptHash CHAR(64) NOT NULL,
        ToolManifestHash CHAR(64) NOT NULL,
        PreparationHash CHAR(64) NOT NULL,
        PreparedAtUtc DATETIME2(7) NOT NULL,
        RecordedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_WorkbenchBusinessAnalystPreparations_RecordedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_WorkbenchBusinessAnalystPreparations_ExactAttempt
            FOREIGN KEY (AgentRunAttemptId, AgentRunId, ClaimToken, AttemptNumber)
            REFERENCES dbo.WorkbenchAgentRunAttempts(Id, AgentRunId, ClaimToken, AttemptNumber),
        CONSTRAINT UQ_WorkbenchBusinessAnalystPreparations_Attempt UNIQUE (AgentRunAttemptId),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_AttemptNumber CHECK (AttemptNumber > 0),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_PublishedVersion CHECK
            (AnalystProfilePublishedVersion IS NULL OR AnalystProfilePublishedVersion > 0),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_Timeout CHECK
            (ProviderTimeoutSeconds BETWEEN 1 AND 3600),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_Timeline CHECK
            (PreparedAtUtc <= DATEADD(MINUTE, 5, RecordedAtUtc)),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_ProfileHash CHECK
            (LEN(EffectiveAnalystProfileHash)=64 AND
             EffectiveAnalystProfileHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_PromptHash CHECK
            (LEN(PromptHash)=64 AND PromptHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_ToolManifestHash CHECK
            (LEN(ToolManifestHash)=64 AND ToolManifestHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystPreparations_PreparationHash CHECK
            (LEN(PreparationHash)=64 AND PreparationHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchBusinessAnalystPreparations')
      AND name=N'UX_WorkbenchBusinessAnalystPreparations_ExactIdentity'
)
    CREATE UNIQUE INDEX UX_WorkbenchBusinessAnalystPreparations_ExactIdentity
        ON dbo.WorkbenchBusinessAnalystPreparations(Id, AgentRunId, ClaimToken, AttemptNumber);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchBusinessAnalystPreparations')
      AND name=N'IX_WorkbenchBusinessAnalystPreparations_RunAttempt'
)
    CREATE INDEX IX_WorkbenchBusinessAnalystPreparations_RunAttempt
        ON dbo.WorkbenchBusinessAnalystPreparations(AgentRunId, AttemptNumber, PreparedAtUtc DESC);
GO

IF OBJECT_ID(N'dbo.WorkbenchBusinessAnalystToolCallAudits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchBusinessAnalystToolCallAudits
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkbenchBusinessAnalystToolCallAudits PRIMARY KEY,
        PreparationId BIGINT NOT NULL,
        AgentRunId UNIQUEIDENTIFIER NOT NULL,
        ClaimToken UNIQUEIDENTIFIER NOT NULL,
        AttemptNumber INT NOT NULL,
        ToolName NVARCHAR(160) NOT NULL,
        DefinitionVersion NVARCHAR(100) NOT NULL,
        PolicyVersion NVARCHAR(100) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        InputHash CHAR(64) NOT NULL,
        OutputHash CHAR(64) NOT NULL,
        SafeSummary NVARCHAR(500) NOT NULL,
        StartedAtUtc DATETIME2(7) NOT NULL,
        CompletedAtUtc DATETIME2(7) NOT NULL,
        ToolCallHash CHAR(64) NOT NULL,
        RecordedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_WorkbenchBusinessAnalystToolCallAudits_RecordedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_WorkbenchBusinessAnalystToolCallAudits_ExactPreparation
            FOREIGN KEY (PreparationId, AgentRunId, ClaimToken, AttemptNumber)
            REFERENCES dbo.WorkbenchBusinessAnalystPreparations(Id, AgentRunId, ClaimToken, AttemptNumber),
        CONSTRAINT UQ_WorkbenchBusinessAnalystToolCallAudits_PreparationTool UNIQUE (PreparationId, ToolName),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_AttemptNumber CHECK (AttemptNumber > 0),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_Status CHECK
            (Status IN (N'Completed', N'Rejected', N'Failed')),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_Timeline CHECK
            (StartedAtUtc <= CompletedAtUtc AND
             CompletedAtUtc <= DATEADD(MINUTE, 5, RecordedAtUtc)),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_SafeSummary CHECK
            (LEN(SafeSummary) BETWEEN 1 AND 500 AND
             SafeSummary NOT LIKE N'%' + NCHAR(10) + N'%' AND
             SafeSummary NOT LIKE N'%' + NCHAR(13) + N'%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_InputHash CHECK
            (LEN(InputHash)=64 AND InputHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_OutputHash CHECK
            (LEN(OutputHash)=64 AND OutputHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystToolCallAudits_ToolCallHash CHECK
            (LEN(ToolCallHash)=64 AND ToolCallHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')
    );
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_WorkbenchBusinessAnalystPreparations_BlockUpdateDelete
ON dbo.WorkbenchBusinessAnalystPreparations
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51021, 'Workbench Business Analyst preparation provenance is append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_WorkbenchBusinessAnalystToolCallAudits_BlockUpdateDelete
ON dbo.WorkbenchBusinessAnalystToolCallAudits
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51022, 'Workbench Business Analyst tool-call provenance is append-only.', 1;
END;
GO

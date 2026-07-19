/* Workbench v0.1 PR-02A: durable, fenced Business Analyst agent-run authority. */

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchWriteLeases')
      AND name=N'UX_WorkbenchWriteLeases_ExactFence'
)
    CREATE UNIQUE INDEX UX_WorkbenchWriteLeases_ExactFence
        ON dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ProjectChatSessions')
      AND name=N'UX_ProjectChatSessions_ProjectSession'
)
    CREATE UNIQUE INDEX UX_ProjectChatSessions_ProjectSession
        ON dbo.ProjectChatSessions(TenantId, ProjectId, Id);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ChatMessages')
      AND name=N'UX_ChatMessages_ProjectSessionMessage'
)
    CREATE UNIQUE INDEX UX_ChatMessages_ProjectSessionMessage
        ON dbo.ChatMessages(TenantId, ProjectId, ChatSessionId, Id);
GO

IF OBJECT_ID(N'dbo.WorkbenchAgentRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchAgentRuns
    (
        AgentRunId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WorkbenchAgentRuns PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        ActorUserId INT NOT NULL,
        ChatSessionId BIGINT NOT NULL,
        SourceUserMessageId BIGINT NOT NULL,
        ClientOperationRecordId BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        AgentVersion NVARCHAR(100) NOT NULL,
        PromptVersion NVARCHAR(100) NOT NULL,
        ToolPolicyVersion NVARCHAR(100) NOT NULL,
        OutputSchemaVersion INT NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        AttemptCount INT NOT NULL CONSTRAINT DF_WorkbenchAgentRuns_AttemptCount DEFAULT 0,
        ClaimToken UNIQUEIDENTIFIER NULL,
        ClaimedBy NVARCHAR(200) NULL,
        ClaimedAtUtc DATETIME2(7) NULL,
        ClaimExpiresAtUtc DATETIME2(7) NULL,
        CancellationRequestedAtUtc DATETIME2(7) NULL,
        SupersededAtUtc DATETIME2(7) NULL,
        SupersededByWorkbenchSessionId BIGINT NULL,
        SupersededByLeaseEpoch BIGINT NULL,
        ContextSnapshotJson NVARCHAR(MAX) NULL,
        ContextHash CHAR(64) NULL,
        BasedOnUnderstandingRevision BIGINT NULL,
        ValidatedOutputJson NVARCHAR(MAX) NULL,
        OutputHash CHAR(64) NULL,
        AssistantMessageId BIGINT NULL,
        DiagnosticCode NVARCHAR(100) NULL,
        DiagnosticHash CHAR(64) NULL,
        DiagnosticAtUtc DATETIME2(7) NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_WorkbenchAgentRuns_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        StartedAtUtc DATETIME2(7) NULL,
        MaterializedAtUtc DATETIME2(7) NULL,
        CompletedAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_WorkbenchAgentRuns_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkbenchAgentRuns_Session
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId)
            REFERENCES dbo.WorkbenchSessions(TenantId, ProjectId, Id),
        CONSTRAINT FK_WorkbenchAgentRuns_ExactFence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT FK_WorkbenchAgentRuns_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_WorkbenchAgentRuns_ChatSession
            FOREIGN KEY (TenantId, ProjectId, ChatSessionId)
            REFERENCES dbo.ProjectChatSessions(TenantId, ProjectId, Id),
        CONSTRAINT FK_WorkbenchAgentRuns_SourceMessage
            FOREIGN KEY (TenantId, ProjectId, ChatSessionId, SourceUserMessageId)
            REFERENCES dbo.ChatMessages(TenantId, ProjectId, ChatSessionId, Id),
        CONSTRAINT FK_WorkbenchAgentRuns_AssistantMessage
            FOREIGN KEY (TenantId, ProjectId, ChatSessionId, AssistantMessageId)
            REFERENCES dbo.ChatMessages(TenantId, ProjectId, ChatSessionId, Id),
        CONSTRAINT FK_WorkbenchAgentRuns_SupersedingSession
            FOREIGN KEY (TenantId, ProjectId, SupersededByWorkbenchSessionId)
            REFERENCES dbo.WorkbenchSessions(TenantId, ProjectId, Id),
        CONSTRAINT FK_WorkbenchAgentRuns_SupersedingFence
            FOREIGN KEY (TenantId, ProjectId, SupersededByWorkbenchSessionId, SupersededByLeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT FK_WorkbenchAgentRuns_ClientOperation
            FOREIGN KEY (ClientOperationRecordId) REFERENCES dbo.ClientOperations(Id),
        CONSTRAINT CK_WorkbenchAgentRuns_LeaseEpoch CHECK (LeaseEpoch > 0),
        CONSTRAINT CK_WorkbenchAgentRuns_OutputSchemaVersion CHECK (OutputSchemaVersion > 0),
        CONSTRAINT CK_WorkbenchAgentRuns_AttemptCount CHECK (AttemptCount >= 0),
        CONSTRAINT CK_WorkbenchAgentRuns_Status CHECK
            (Status IN (N'Pending', N'Running', N'NeedsInput', N'Completed', N'Failed', N'Cancelled', N'Superseded', N'Stale')),
        CONSTRAINT CK_WorkbenchAgentRuns_OutputJson CHECK
            (ValidatedOutputJson IS NULL OR ISJSON(ValidatedOutputJson) = 1),
        CONSTRAINT CK_WorkbenchAgentRuns_ContextJson CHECK
            (ContextSnapshotJson IS NULL OR ISJSON(ContextSnapshotJson) = 1),
        CONSTRAINT CK_WorkbenchAgentRuns_ContextHash CHECK
            (ContextHash IS NULL OR
             (LEN(ContextHash) = 64 AND ContextHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT CK_WorkbenchAgentRuns_OutputHash CHECK
            (OutputHash IS NULL OR
             (LEN(OutputHash) = 64 AND OutputHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT CK_WorkbenchAgentRuns_DiagnosticHash CHECK
            (DiagnosticHash IS NULL OR
             (LEN(DiagnosticHash) = 64 AND DiagnosticHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT CK_WorkbenchAgentRuns_MaterializationState CHECK
            (
                (Status IN (N'Completed', N'NeedsInput')
                 AND AssistantMessageId IS NOT NULL AND MaterializedAtUtc IS NOT NULL
                 AND ValidatedOutputJson IS NOT NULL AND OutputHash IS NOT NULL AND CompletedAtUtc IS NOT NULL)
                OR
                (Status NOT IN (N'Completed', N'NeedsInput')
                 AND AssistantMessageId IS NULL AND MaterializedAtUtc IS NULL
                 AND ValidatedOutputJson IS NULL AND OutputHash IS NULL)
            ),
        CONSTRAINT CK_WorkbenchAgentRuns_PendingState CHECK
            (Status<>N'Pending' OR (AttemptCount=0 AND ClaimToken IS NULL AND StartedAtUtc IS NULL)),
        CONSTRAINT CK_WorkbenchAgentRuns_RunningState CHECK
            (Status<>N'Running' OR (AttemptCount>0 AND ClaimToken IS NOT NULL AND ClaimedBy IS NOT NULL
                                    AND ClaimedAtUtc IS NOT NULL AND ClaimExpiresAtUtc IS NOT NULL
                                    AND StartedAtUtc IS NOT NULL)),
        CONSTRAINT CK_WorkbenchAgentRuns_SupersededState CHECK
            (Status<>N'Superseded' OR (SupersededAtUtc IS NOT NULL
                                       AND SupersededByWorkbenchSessionId IS NOT NULL
                                       AND SupersededByLeaseEpoch IS NOT NULL)),
        CONSTRAINT UQ_WorkbenchAgentRuns_SourceMessage UNIQUE
            (TenantId, ProjectId, SourceUserMessageId),
        CONSTRAINT UQ_WorkbenchAgentRuns_ClientOperation UNIQUE
            (ClientOperationRecordId)
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchAgentRuns')
      AND name=N'IX_WorkbenchAgentRuns_Claimable'
)
    CREATE INDEX IX_WorkbenchAgentRuns_Claimable
        ON dbo.WorkbenchAgentRuns(Status, ClaimExpiresAtUtc, CreatedAtUtc)
        INCLUDE (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchAgentRuns')
      AND name=N'IX_WorkbenchAgentRuns_ProjectSession'
)
    CREATE INDEX IX_WorkbenchAgentRuns_ProjectSession
        ON dbo.WorkbenchAgentRuns(TenantId, ProjectId, WorkbenchSessionId, CreatedAtUtc DESC);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchAgentRuns')
      AND name=N'UX_WorkbenchAgentRuns_AssistantMessage'
)
    CREATE UNIQUE INDEX UX_WorkbenchAgentRuns_AssistantMessage
        ON dbo.WorkbenchAgentRuns(AssistantMessageId)
        WHERE AssistantMessageId IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.WorkbenchAgentRunAttempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchAgentRunAttempts
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkbenchAgentRunAttempts PRIMARY KEY,
        AgentRunId UNIQUEIDENTIFIER NOT NULL,
        AttemptNumber INT NOT NULL,
        ClaimToken UNIQUEIDENTIFIER NOT NULL,
        WorkerId NVARCHAR(200) NOT NULL,
        ContextHash CHAR(64) NULL,
        Outcome NVARCHAR(50) NULL,
        ResponseHash CHAR(64) NULL,
        DiagnosticCode NVARCHAR(100) NULL,
        StartedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_WorkbenchAgentRunAttempts_StartedAtUtc DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_WorkbenchAgentRunAttempts_Run
            FOREIGN KEY (AgentRunId) REFERENCES dbo.WorkbenchAgentRuns(AgentRunId),
        CONSTRAINT CK_WorkbenchAgentRunAttempts_Number CHECK (AttemptNumber > 0),
        CONSTRAINT CK_WorkbenchAgentRunAttempts_ContextHash CHECK
            (ContextHash IS NULL OR
             (LEN(ContextHash) = 64 AND ContextHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT CK_WorkbenchAgentRunAttempts_ResponseHash CHECK
            (ResponseHash IS NULL OR
             (LEN(ResponseHash) = 64 AND ResponseHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT UQ_WorkbenchAgentRunAttempts_Number UNIQUE (AgentRunId, AttemptNumber),
        CONSTRAINT UQ_WorkbenchAgentRunAttempts_Claim UNIQUE (ClaimToken)
    );
END;
GO

IF COL_LENGTH(N'dbo.WorkbenchOutboxEvents', N'AgentRunId') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents ADD AgentRunId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.WorkbenchOutboxEvents', N'DedupeKey') IS NULL
    ALTER TABLE dbo.WorkbenchOutboxEvents ADD DedupeKey NVARCHAR(300) NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.WorkbenchOutboxEvents')
      AND name=N'FK_WorkbenchOutboxEvents_AgentRun'
)
BEGIN
    ALTER TABLE dbo.WorkbenchOutboxEvents WITH CHECK
        ADD CONSTRAINT FK_WorkbenchOutboxEvents_AgentRun
        FOREIGN KEY (AgentRunId) REFERENCES dbo.WorkbenchAgentRuns(AgentRunId);
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchOutboxEvents')
      AND name=N'IX_WorkbenchOutboxEvents_AgentRun'
)
    CREATE INDEX IX_WorkbenchOutboxEvents_AgentRun
        ON dbo.WorkbenchOutboxEvents(AgentRunId, OccurredAtUtc, Id)
        WHERE AgentRunId IS NOT NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchOutboxEvents')
      AND name=N'UX_WorkbenchOutboxEvents_DedupeKey'
)
    CREATE UNIQUE INDEX UX_WorkbenchOutboxEvents_DedupeKey
        ON dbo.WorkbenchOutboxEvents(DedupeKey)
        WHERE DedupeKey IS NOT NULL;
GO

IF COL_LENGTH(N'dbo.ClientOperations', N'ResultAgentRunId') IS NULL
    ALTER TABLE dbo.ClientOperations ADD ResultAgentRunId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ClientOperations')
      AND name = N'IX_ClientOperations_ResultAgentRunId'
)
BEGIN
    CREATE INDEX IX_ClientOperations_ResultAgentRunId
        ON dbo.ClientOperations(ResultAgentRunId)
        WHERE ResultAgentRunId IS NOT NULL;
END;
GO

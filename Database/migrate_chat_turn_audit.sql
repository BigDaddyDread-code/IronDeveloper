-- ============================================================
-- Migration: Chat Turn Audit
-- Normalizes saved assistant chat envelope metadata out of
-- ChatMessages.Tags and into durable governance/clarification/
-- trace audit tables.
-- ============================================================

IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatTurnGovernance
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChatSessionId BIGINT NOT NULL,
        ChatMessageId BIGINT NOT NULL,
        Mode NVARCHAR(50) NOT NULL,
        ModeConfidence FLOAT NOT NULL,
        ModeReason NVARCHAR(MAX) NOT NULL,
        GateJson NVARCHAR(MAX) NOT NULL,
        RouteSource NVARCHAR(200) NOT NULL CONSTRAINT DF_ChatTurnGovernance_RouteSource DEFAULT N'unknown',
        RouteChallengeJson NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnGovernance_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ChatTurnGovernance_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ChatTurnGovernance_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ChatTurnGovernance_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id),
        CONSTRAINT FK_ChatTurnGovernance_Messages FOREIGN KEY (ChatMessageId) REFERENCES dbo.ChatMessages(Id)
    );

    CREATE UNIQUE INDEX UX_ChatTurnGovernance_MessageTenant
        ON dbo.ChatTurnGovernance(ChatMessageId, TenantId);
END
GO

IF COL_LENGTH('dbo.ChatTurnGovernance', 'RouteSource') IS NULL
BEGIN
    ALTER TABLE dbo.ChatTurnGovernance
        ADD RouteSource NVARCHAR(200) NOT NULL
            CONSTRAINT DF_ChatTurnGovernance_RouteSource DEFAULT N'unknown' WITH VALUES;
END
GO

IF COL_LENGTH('dbo.ChatTurnGovernance', 'RouteChallengeJson') IS NULL
BEGIN
    ALTER TABLE dbo.ChatTurnGovernance
        ADD RouteChallengeJson NVARCHAR(MAX) NULL;
END
GO

IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatTurnClarifications
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChatSessionId BIGINT NOT NULL,
        ChatMessageId BIGINT NOT NULL,
        Required BIT NOT NULL,
        Kind NVARCHAR(100) NOT NULL,
        Reason NVARCHAR(MAX) NULL,
        QuestionsJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnClarifications_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ChatTurnClarifications_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ChatTurnClarifications_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ChatTurnClarifications_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id),
        CONSTRAINT FK_ChatTurnClarifications_Messages FOREIGN KEY (ChatMessageId) REFERENCES dbo.ChatMessages(Id)
    );

    CREATE UNIQUE INDEX UX_ChatTurnClarifications_MessageTenant
        ON dbo.ChatTurnClarifications(ChatMessageId, TenantId);
END
GO

IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatTurnTraces
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChatSessionId BIGINT NOT NULL,
        ChatMessageId BIGINT NOT NULL,
        RouteTraceId NVARCHAR(200) NULL,
        DogfoodTraceId NVARCHAR(200) NULL,
        ContextSummary NVARCHAR(MAX) NULL,
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnTraces_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ChatTurnTraces_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ChatTurnTraces_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ChatTurnTraces_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id),
        CONSTRAINT FK_ChatTurnTraces_Messages FOREIGN KEY (ChatMessageId) REFERENCES dbo.ChatMessages(Id)
    );

    CREATE UNIQUE INDEX UX_ChatTurnTraces_MessageTenant
        ON dbo.ChatTurnTraces(ChatMessageId, TenantId);
END
GO

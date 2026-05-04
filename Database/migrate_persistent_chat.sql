USE [IronDeveloper];
GO

-- 1. Create ProjectChatSessions
IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DROP TABLE dbo.ProjectChatSessions;
CREATE TABLE dbo.ProjectChatSessions
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL,
    ProjectId INT NOT NULL,
    Title NVARCHAR(200) NOT NULL CONSTRAINT DF_ProjectChatSessions_Title DEFAULT 'New Chat',
    Summary NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectChatSessions_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectChatSessions_UpdatedDate DEFAULT SYSUTCDATETIME(),
    
    -- Linked Primary Workspace Items
    PrimaryTicketId BIGINT NULL,
    PrimaryDecisionId BIGINT NULL,
    PrimaryPlanId BIGINT NULL,

    -- Origin Workspace Items
    OriginTicketId BIGINT NULL,
    OriginDecisionId BIGINT NULL,
    OriginPlanId BIGINT NULL,

    CONSTRAINT FK_ProjectChatSessions_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectChatSessions_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
);

-- 2. Drop FKs to ChatMessages so we can rebuild it
ALTER TABLE dbo.ProjectSummaries DROP CONSTRAINT FK_ProjectSummaries_ChatMessages;
ALTER TABLE dbo.ProjectDecisions DROP CONSTRAINT FK_ProjectDecisions_ChatMessages;
ALTER TABLE dbo.ProjectImplementationPlans DROP CONSTRAINT FK_ProjectImplementationPlans_ChatMessages;

-- 3. Static Drop/Recreate ChatMessages (No data to preserve)
IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL DROP TABLE dbo.ChatMessages;
CREATE TABLE dbo.ChatMessages
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL,
    ProjectId INT NOT NULL,
    ChatSessionId BIGINT NOT NULL,
    Role NVARCHAR(50) NOT NULL,
    Message NVARCHAR(MAX) NOT NULL,
    Tags NVARCHAR(500) NULL,
    
    -- Grounded context metadata
    ContextSummary NVARCHAR(MAX) NULL,
    LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL,

    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ChatMessages_CreatedDate DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_ChatMessages_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ChatMessages_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ChatMessages_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id)
);

-- 4. Restore FKs
ALTER TABLE dbo.ProjectSummaries ADD CONSTRAINT FK_ProjectSummaries_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id);
ALTER TABLE dbo.ProjectDecisions ADD CONSTRAINT FK_ProjectDecisions_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id);
ALTER TABLE dbo.ProjectImplementationPlans ADD CONSTRAINT FK_ProjectImplementationPlans_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id);

-- 5. Updated Indexes
IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_ChatMessages_ProjectId_SessionId_CreatedDate')
    DROP INDEX IX_ChatMessages_ProjectId_SessionId_CreatedDate ON dbo.ChatMessages;

CREATE INDEX IX_ChatMessages_ProjectId_ChatSessionId_CreatedDate
    ON dbo.ChatMessages(ProjectId, ChatSessionId, CreatedDate DESC);

CREATE INDEX IX_ProjectChatSessions_ProjectId_UpdatedDate
    ON dbo.ProjectChatSessions(ProjectId, UpdatedDate DESC);
GO

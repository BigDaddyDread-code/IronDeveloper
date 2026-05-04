IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'IronDeveloper')
BEGIN
    CREATE DATABASE [IronDeveloper];
END
GO

USE [IronDeveloper];
GO

IF OBJECT_ID('dbo.ProjectTickets', 'U') IS NOT NULL DROP TABLE dbo.ProjectTickets;
IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DROP TABLE dbo.ProjectImplementationPlans;
IF OBJECT_ID('dbo.ProjectDecisions', 'U') IS NOT NULL DROP TABLE dbo.ProjectDecisions;
IF OBJECT_ID('dbo.DecisionCategories', 'U') IS NOT NULL DROP TABLE dbo.DecisionCategories;
IF OBJECT_ID('dbo.DecisionStatuses', 'U') IS NOT NULL DROP TABLE dbo.DecisionStatuses;
IF OBJECT_ID('dbo.ProjectSummaries', 'U') IS NOT NULL DROP TABLE dbo.ProjectSummaries;
IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL DROP TABLE dbo.ChatMessages;
IF OBJECT_ID('dbo.ProjectFiles', 'U') IS NOT NULL DROP TABLE dbo.ProjectFiles;
IF OBJECT_ID('dbo.Projects', 'U') IS NOT NULL DROP TABLE dbo.Projects;
IF OBJECT_ID('dbo.TenantUsers', 'U') IS NOT NULL DROP TABLE dbo.TenantUsers;
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DROP TABLE dbo.Users;
IF OBJECT_ID('dbo.Tenants', 'U') IS NOT NULL DROP TABLE dbo.Tenants;

CREATE TABLE dbo.Tenants
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    Slug NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT 1,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_Tenants_CreatedDate DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Users
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Email NVARCHAR(256) NOT NULL,
    DisplayName NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(MAX) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedDate DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.TenantUsers
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL,
    UserId INT NOT NULL,
    Role NVARCHAR(50) NOT NULL CONSTRAINT DF_TenantUsers_Role DEFAULT 'Member',
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_TenantUsers_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_TenantUsers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_TenantUsers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id)
);

CREATE TABLE dbo.Projects
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_Projects_Tenant DEFAULT 1,
    Name NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    LocalPath NVARCHAR(500) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_Projects_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NULL,
    LastIndexedUtc DATETIME2 NULL,
    IndexingStatus NVARCHAR(50) NULL,
    CONSTRAINT FK_Projects_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
);

CREATE TABLE dbo.ProjectChatSessions
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectChatSessions_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    Title NVARCHAR(200) NOT NULL CONSTRAINT DF_ProjectChatSessions_Title DEFAULT 'New Chat',
    Summary NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectChatSessions_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectChatSessions_UpdatedDate DEFAULT SYSUTCDATETIME(),
    PrimaryTicketId BIGINT NULL,
    PrimaryDecisionId BIGINT NULL,
    PrimaryPlanId BIGINT NULL,
    OriginTicketId BIGINT NULL,
    OriginDecisionId BIGINT NULL,
    OriginPlanId BIGINT NULL,
    CONSTRAINT FK_ProjectChatSessions_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectChatSessions_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
);

CREATE TABLE dbo.ChatMessages
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ChatMessages_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    ChatSessionId BIGINT NOT NULL,
    Role NVARCHAR(50) NOT NULL,
    Message NVARCHAR(MAX) NOT NULL,
    Tags NVARCHAR(500) NULL,
    ContextSummary NVARCHAR(MAX) NULL,
    LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ChatMessages_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ChatMessages_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ChatMessages_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ChatMessages_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id)
);

CREATE TABLE dbo.ProjectSummaries
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectSummaries_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    Summary NVARCHAR(MAX) NOT NULL,
    SourceChatMessageId BIGINT NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectSummaries_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NULL,
    CONSTRAINT FK_ProjectSummaries_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectSummaries_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ProjectSummaries_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

CREATE TABLE dbo.ProjectDecisions
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectDecisions_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    Detail NVARCHAR(MAX) NOT NULL,
    Reason NVARCHAR(MAX) NULL,
    Category NVARCHAR(100) NULL,
    Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectDecisions_Status DEFAULT 'Accepted',
    SourceChatMessageId BIGINT NULL,
    LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectDecisions_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ProjectDecisions_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectDecisions_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ProjectDecisions_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

CREATE TABLE dbo.ProjectImplementationPlans
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectImplementationPlans_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    TicketId BIGINT NULL,
    Title NVARCHAR(200) NOT NULL,
    Goal NVARCHAR(MAX) NOT NULL,
    Scope NVARCHAR(MAX) NULL,
    ProposedSteps NVARCHAR(MAX) NULL,
    AffectedContext NVARCHAR(MAX) NULL,
    RisksNotes NVARCHAR(MAX) NULL,
    Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectImplementationPlans_Status DEFAULT 'Draft',
    
    -- Linked Context
    LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL,
    
    -- AI Context
    SourceChatMessageId BIGINT NULL,
    
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectImplementationPlans_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NULL,

    CONSTRAINT FK_ProjectImplementationPlans_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectImplementationPlans_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ProjectImplementationPlans_Tickets FOREIGN KEY (TicketId) REFERENCES dbo.ProjectTickets(Id),
    CONSTRAINT FK_ProjectImplementationPlans_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

CREATE TABLE dbo.ProjectTickets
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectTickets_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    SessionId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(200) NOT NULL CONSTRAINT DF_ProjectTickets_Title DEFAULT '',
    TicketType NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_TicketType DEFAULT 'Task',
    Priority NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_Priority DEFAULT 'Medium',
    Summary NVARCHAR(MAX) NULL,
    Background NVARCHAR(MAX) NULL,
    Problem NVARCHAR(MAX) NULL,
    AcceptanceCriteria NVARCHAR(MAX) NULL,
    TechnicalNotes NVARCHAR(MAX) NULL,
    Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_Status DEFAULT 'Draft',
    Content NVARCHAR(MAX) NOT NULL,
    LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectTickets_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ProjectTickets_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectTickets_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
);

CREATE TABLE dbo.ProjectFiles
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectFiles_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    FilePath NVARCHAR(1000) NOT NULL,
    FileExtension NVARCHAR(50) NOT NULL,
    ContentHash NVARCHAR(100) NOT NULL,
    Content NVARCHAR(MAX) NOT NULL,
    LastIndexedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectFiles_LastIndexedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ProjectFiles_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ProjectFiles_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
);

CREATE INDEX IX_ChatMessages_ProjectId_ChatSessionId_CreatedDate
    ON dbo.ChatMessages(ProjectId, ChatSessionId, CreatedDate DESC);

CREATE INDEX IX_ProjectChatSessions_ProjectId_UpdatedDate
    ON dbo.ProjectChatSessions(ProjectId, UpdatedDate DESC);

CREATE INDEX IX_ProjectSummaries_ProjectId_CreatedDate
    ON dbo.ProjectSummaries(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectDecisions_ProjectId_CreatedDate
    ON dbo.ProjectDecisions(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectImplementationPlans_ProjectId_CreatedDate
    ON dbo.ProjectImplementationPlans(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectTickets_ProjectId_CreatedDate
    ON dbo.ProjectTickets(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectFiles_ProjectId_FilePath
    ON dbo.ProjectFiles(ProjectId, FilePath);

CREATE INDEX IX_ProjectFiles_ProjectId_FileExtension
    ON dbo.ProjectFiles(ProjectId, FileExtension);

CREATE UNIQUE INDEX UX_ProjectFiles_ProjectId_FilePath
    ON dbo.ProjectFiles(ProjectId, FilePath);
GO

-- Tenant 1: Default (admin is a member)
INSERT INTO dbo.Tenants (Name, Slug) VALUES ('Default Tenant', 'default');
-- Tenant 2: Isolation (admin is NOT a member — used for cross-tenant isolation tests)
INSERT INTO dbo.Tenants (Name, Slug) VALUES ('Other Tenant', 'other');

-- Admin user — password: 'password123' (BCrypt hash, cost factor 11)
-- Hash: $2a$11$vI8aWBnW3fID.ZQ4/zo1G.q1lRps.9cj1USGKp3R1LrS9hcNaSUna
-- NOTE: Integration tests compute & inject the real hash at test setup time.
INSERT INTO dbo.Users (Email, DisplayName, PasswordHash)
VALUES ('admin@irondev.local', 'Admin User', NULL);

-- Admin only belongs to Tenant 1, NOT Tenant 2.
INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (1, 1, 'Owner');
GO

INSERT INTO dbo.Projects (TenantId, Name, Description)
VALUES (1, 'IronDev', 'AI-assisted development workflow project');
GO

-- ═══ Lookup Tables ══════════════════════════════════════════════════════════

CREATE TABLE dbo.DecisionCategories
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_DecisionCategories_SortOrder DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_DecisionCategories_IsActive DEFAULT 1
);

CREATE TABLE dbo.DecisionStatuses
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_DecisionStatuses_SortOrder DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_DecisionStatuses_IsActive DEFAULT 1
);
GO

-- Seed decision categories
INSERT INTO dbo.DecisionCategories (Name, SortOrder) VALUES
    ('Architecture',       1),
    ('Code Standards',     2),
    ('Product',            3),
    ('Data',               4),
    ('Infrastructure',     5),
    ('AI / Prompting',     6),
    ('UX / UI',            7),
    ('Workflow / Process', 8),
    ('Integration',        9),
    ('Security',          10);

-- Seed decision statuses
INSERT INTO dbo.DecisionStatuses (Name, SortOrder) VALUES
    ('Proposed',    1),
    ('Accepted',    2),
    ('Superseded',  3),
    ('Rejected',    4);
GO

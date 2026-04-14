IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'IronDeveloper_Test')
BEGIN
    CREATE DATABASE [IronDeveloper_Test];
END
GO

USE [IronDeveloper_Test];
GO

IF OBJECT_ID('dbo.ProjectTickets', 'U') IS NOT NULL DROP TABLE dbo.ProjectTickets;
IF OBJECT_ID('dbo.ProjectDecisions', 'U') IS NOT NULL DROP TABLE dbo.ProjectDecisions;
IF OBJECT_ID('dbo.ProjectSummaries', 'U') IS NOT NULL DROP TABLE dbo.ProjectSummaries;
IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL DROP TABLE dbo.ChatMessages;
IF OBJECT_ID('dbo.Projects', 'U') IS NOT NULL DROP TABLE dbo.Projects;

CREATE TABLE dbo.Projects
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_Projects_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NULL
);

CREATE TABLE dbo.ChatMessages
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProjectId INT NOT NULL,
    SessionId UNIQUEIDENTIFIER NOT NULL,
    Role NVARCHAR(50) NOT NULL,
    Message NVARCHAR(MAX) NOT NULL,
    Tags NVARCHAR(500) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ChatMessages_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ChatMessages_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
);

CREATE TABLE dbo.ProjectSummaries
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProjectId INT NOT NULL,
    Summary NVARCHAR(MAX) NOT NULL,
    SourceChatMessageId BIGINT NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectSummaries_CreatedDate DEFAULT SYSUTCDATETIME(),
    UpdatedDate DATETIME2 NULL,
    CONSTRAINT FK_ProjectSummaries_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ProjectSummaries_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

CREATE TABLE dbo.ProjectDecisions
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProjectId INT NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    Detail NVARCHAR(MAX) NOT NULL,
    Reason NVARCHAR(MAX) NULL,
    SourceChatMessageId BIGINT NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectDecisions_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ProjectDecisions_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ProjectDecisions_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

CREATE TABLE dbo.ProjectTickets
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
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
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectTickets_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ProjectTickets_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
);

CREATE INDEX IX_ChatMessages_ProjectId_CreatedDate
    ON dbo.ChatMessages(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ChatMessages_ProjectId_SessionId_CreatedDate
    ON dbo.ChatMessages(ProjectId, SessionId, CreatedDate DESC);

CREATE INDEX IX_ProjectSummaries_ProjectId_CreatedDate
    ON dbo.ProjectSummaries(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectDecisions_ProjectId_CreatedDate
    ON dbo.ProjectDecisions(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectTickets_ProjectId_CreatedDate
    ON dbo.ProjectTickets(ProjectId, CreatedDate DESC);
GO

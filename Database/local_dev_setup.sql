/*
  IronDev Local Development Setup Script
  
  This script initializes the local database for IronDev development.
  It is designed to be idempotent and can be safely rerun.
  
  Instructions:
  1. Create a local SQL Server database named 'IronDeveloper'.
  2. Run this script against that database.
  3. Default login after setup:
     Email: bob@irondev.local
     Password: change-me-local-only
*/

IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'IronDeveloper')
BEGIN
    CREATE DATABASE [IronDeveloper];
END
GO

USE [IronDeveloper];
GO

-- 1. Create Tables if missing
IF OBJECT_ID('dbo.Tenants', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tenants
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Slug NVARCHAR(100) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT 1,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_Tenants_CreatedDate DEFAULT SYSUTCDATETIME()
    );
END

IF OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL,
        DisplayName NVARCHAR(100) NOT NULL,
        PasswordHash NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedDate DEFAULT SYSUTCDATETIME()
    );
END

IF OBJECT_ID('dbo.TenantUsers', 'U') IS NULL
BEGIN
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
END

IF OBJECT_ID('dbo.Projects', 'U') IS NULL
BEGIN
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
        IndexedFileCount INT NULL,
        CONSTRAINT FK_Projects_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
    );
END

IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NULL
BEGIN
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
END

IF OBJECT_ID('dbo.ChatMessages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatMessages
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ChatMessages_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        ChatSessionId BIGINT NOT NULL,
        Role NVARCHAR(50) NOT NULL,
        Message NVARCHAR(MAX) NOT NULL,
        Tags NVARCHAR(MAX) NULL,
        ContextSummary NVARCHAR(MAX) NULL,
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ChatMessages_CreatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ChatMessages_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ChatMessages_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ChatMessages_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id)
    );
END

IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ChatMessages ALTER COLUMN Tags NVARCHAR(MAX) NULL;
END

IF OBJECT_ID('dbo.ProjectTickets', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectTickets
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectTickets_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        SessionId UNIQUEIDENTIFIER NULL,
        Title NVARCHAR(200) NOT NULL CONSTRAINT DF_ProjectTickets_Title DEFAULT '',
        TicketType NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_TicketType DEFAULT 'Task',
        Priority NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_Priority DEFAULT 'Medium',
        Summary NVARCHAR(MAX) NULL,
        Background NVARCHAR(MAX) NULL,
        Problem NVARCHAR(MAX) NULL,
        AcceptanceCriteria NVARCHAR(MAX) NULL,
        TechnicalNotes NVARCHAR(MAX) NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_Status DEFAULT 'Draft',
        Content NVARCHAR(MAX) NOT NULL CONSTRAINT DF_ProjectTickets_Content DEFAULT '',
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,

        -- Extended draft/test fields
        UnitTests NVARCHAR(MAX) NULL,
        IntegrationTests NVARCHAR(MAX) NULL,
        ManualTests NVARCHAR(MAX) NULL,
        RegressionTests NVARCHAR(MAX) NULL,
        BuildValidation NVARCHAR(MAX) NULL,
        ContextSummary NVARCHAR(MAX) NULL,
        IsGenerated BIT NOT NULL CONSTRAINT DF_ProjectTickets_IsGenerated DEFAULT 0,
        GenerationNote NVARCHAR(MAX) NULL,
        SourceChatSessionId BIGINT NULL,
        SourceChatMessageId BIGINT NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_ProjectTickets_IsDeleted DEFAULT 0,

        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectTickets_CreatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectTickets_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectTickets_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );
END

IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatSessionId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceChatSessionId BIGINT NULL;
END

IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatMessageId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceChatMessageId BIGINT NULL;
END

IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ArtifactSourceReferences
    (
        ArtifactSourceReferenceId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ArtifactType NVARCHAR(100) NOT NULL,
        ArtifactId BIGINT NOT NULL,
        SourceType NVARCHAR(100) NOT NULL,
        SourceId BIGINT NULL,
        SourcePath NVARCHAR(1000) NULL,
        SourceSymbol NVARCHAR(500) NULL,
        SourceSection NVARCHAR(500) NULL,
        SourceAnchor NVARCHAR(500) NULL,
        ReferenceType NVARCHAR(100) NOT NULL,
        Summary NVARCHAR(MAX) NULL,
        RelevanceScore DECIMAL(9,4) NULL,
        IsRequired BIT NOT NULL CONSTRAINT DF_ArtifactSourceReferences_IsRequired DEFAULT 0,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ArtifactSourceReferences_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy NVARCHAR(200) NULL,
        CONSTRAINT FK_ArtifactSourceReferences_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ArtifactSourceReferences_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE INDEX IX_ArtifactSourceReferences_Artifact
        ON dbo.ArtifactSourceReferences(TenantId, ProjectId, ArtifactType, ArtifactId);

    CREATE INDEX IX_ArtifactSourceReferences_Source
        ON dbo.ArtifactSourceReferences(TenantId, ProjectId, SourceType, SourceId);
END

IF OBJECT_ID('dbo.ProjectRules', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectRules
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectRules_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        Type NVARCHAR(100) NOT NULL, -- CodeStandard / ArchitectureDecision / WorkflowRule / TestingRule
        Description NVARCHAR(MAX) NOT NULL,
        EnforcementLevel NVARCHAR(50) NOT NULL, -- Advisory / Required / Blocking
        AppliesTo NVARCHAR(50) NOT NULL, -- Ticket / Build / Both
        ValidationHint NVARCHAR(MAX) NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectRules_CreatedDate DEFAULT SYSUTCDATETIME(),
        UpdatedDate DATETIME2 NULL,
        CONSTRAINT FK_ProjectRules_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectRules_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );
END

IF OBJECT_ID('dbo.ProjectSummaries', 'U') IS NULL
BEGIN
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
        CONSTRAINT FK_ProjectSummaries_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );
END

IF OBJECT_ID('dbo.ProjectDecisions', 'U') IS NULL
BEGIN
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
        CONSTRAINT FK_ProjectDecisions_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );
END

IF OBJECT_ID('dbo.ProjectContextDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectContextDocuments
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectContextDocuments_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        DocumentType NVARCHAR(100) NOT NULL,
        AuthorityLevel NVARCHAR(50) NOT NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectContextDocuments_Status DEFAULT 'Active',
        Title NVARCHAR(200) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        Summary NVARCHAR(MAX) NULL,
        Tags NVARCHAR(MAX) NULL,
        AppliesToCapability NVARCHAR(200) NULL,
        AppliesToArea NVARCHAR(200) NULL,
        Source NVARCHAR(200) NULL,
        SupersedesDocumentId BIGINT NULL,
        SourceChatMessageId BIGINT NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectContextDocuments_CreatedDate DEFAULT SYSUTCDATETIME(),
        UpdatedDate DATETIME2 NULL,
        CONSTRAINT FK_ProjectContextDocuments_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectContextDocuments_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE INDEX IX_ProjectContextDocuments_Project_Type_Authority
        ON dbo.ProjectContextDocuments(ProjectId, DocumentType, AuthorityLevel, Status, CreatedDate DESC);
END

IF OBJECT_ID('dbo.ProjectObservableStates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectObservableStates
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectObservableStates_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        ActiveCapability NVARCHAR(200) NULL,
        ActiveMilestone NVARCHAR(200) NULL,
        CurrentFocus NVARCHAR(500) NULL,
        BuildReadiness NVARCHAR(100) NULL,
        IndexStatus NVARCHAR(100) NULL,
        BuilderMode NVARCHAR(100) NULL,
        OpenBlockers NVARCHAR(MAX) NULL,
        LastRecommendation NVARCHAR(MAX) NULL,
        CurrentTargetPath NVARCHAR(1000) NULL,
        KnownCurrentGaps NVARCHAR(MAX) NULL,
        SnapshotJson NVARCHAR(MAX) NULL,
        UpdatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectObservableStates_UpdatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectObservableStates_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectObservableStates_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE UNIQUE INDEX UX_ProjectObservableStates_Tenant_Project
        ON dbo.ProjectObservableStates(TenantId, ProjectId);
END

IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NULL
BEGIN
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
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,
        SourceChatMessageId BIGINT NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectImplementationPlans_CreatedDate DEFAULT SYSUTCDATETIME(),
        UpdatedDate DATETIME2 NULL,
        CONSTRAINT FK_ProjectImplementationPlans_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectImplementationPlans_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );
END

IF OBJECT_ID('dbo.ProjectFiles', 'U') IS NULL
BEGIN
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
END

IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CodeIndexEntries
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_CodeIndexEntries_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        FileId BIGINT NOT NULL,
        Namespace NVARCHAR(500) NULL,
        SymbolName NVARCHAR(500) NULL,
        SymbolType NVARCHAR(50) NULL,
        Summary NVARCHAR(MAX) NULL,
        ChunkText NVARCHAR(MAX) NOT NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_CodeIndexEntries_CreatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CodeIndexEntries_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_CodeIndexEntries_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_CodeIndexEntries_ProjectFiles FOREIGN KEY (FileId) REFERENCES dbo.ProjectFiles(Id) ON DELETE CASCADE
    );
END

IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatMessageFeedback
    (
        Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId      INT NOT NULL,
        ProjectId     INT NOT NULL,
        ChatSessionId BIGINT NULL,
        ChatMessageId BIGINT NOT NULL,
        Rating        NVARCHAR(20) NOT NULL,
        Reason        NVARCHAR(100) NULL,
        Comment       NVARCHAR(MAX) NULL,
        CreatedDate   DATETIME2 NOT NULL CONSTRAINT DF_ChatMessageFeedback_CreatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ChatMessageFeedback_Tenants      FOREIGN KEY (TenantId)      REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ChatMessageFeedback_Projects     FOREIGN KEY (ProjectId)     REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ChatMessageFeedback_Sessions     FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id),
        CONSTRAINT FK_ChatMessageFeedback_ChatMessages FOREIGN KEY (ChatMessageId) REFERENCES dbo.ChatMessages(Id)
    );
END

IF OBJECT_ID('dbo.DecisionCategories', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DecisionCategories
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_DecisionCategories_SortOrder DEFAULT 0,
        IsActive BIT NOT NULL CONSTRAINT DF_DecisionCategories_IsActive DEFAULT 1
    );
END

IF OBJECT_ID('dbo.DecisionStatuses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DecisionStatuses
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_DecisionStatuses_SortOrder DEFAULT 0,
        IsActive BIT NOT NULL CONSTRAINT DF_DecisionStatuses_IsActive DEFAULT 1
    );
END
GO

-- 2. Add Missing Columns (Schema Evolution)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Projects') AND name = N'IndexedFileCount')
BEGIN
    ALTER TABLE dbo.Projects ADD IndexedFileCount INT NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'Problem')
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD Problem NVARCHAR(MAX) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'UnitTests')
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD UnitTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD IntegrationTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD ManualTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD RegressionTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD BuildValidation NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD ContextSummary NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD IsGenerated BIT NOT NULL CONSTRAINT DF_ProjectTickets_IsGenerated DEFAULT 0;
    ALTER TABLE dbo.ProjectTickets ADD GenerationNote NVARCHAR(MAX) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'IsDeleted')
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD IsDeleted BIT NOT NULL CONSTRAINT DF_ProjectTickets_IsDeleted DEFAULT 0;
END
GO

-- 3. Seed Lookup Data
IF NOT EXISTS (SELECT 1 FROM dbo.DecisionCategories)
BEGIN
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
END

IF NOT EXISTS (SELECT 1 FROM dbo.DecisionStatuses)
BEGIN
    INSERT INTO dbo.DecisionStatuses (Name, SortOrder) VALUES
        ('Proposed',    1),
        ('Accepted',    2),
        ('Superseded',  3),
        ('Rejected',    4);
END
GO

-- Seed Project Rules
IF NOT EXISTS (SELECT 1 FROM dbo.ProjectRules)
BEGIN
    INSERT INTO dbo.ProjectRules (TenantId, ProjectId, Name, Type, Description, EnforcementLevel, AppliesTo, ValidationHint)
    VALUES 
    (1, 1, 'SQL Server Source of Truth', 'ArchitectureDecision', 'SQL Server (dbo.ProjectTickets) is the authoritative source of truth for ticket persistence. Do not use Weaviate or other stores as the primary record.', 'Blocking', 'Both', 'Check TicketService.cs for SQL-based persistence.'),
    (1, 1, 'WPF MVVM Pattern', 'CodeStandard', 'All UI must follow the WPF MVVM pattern using CommunityToolkit.Mvvm. ViewModels must not reference UI elements.', 'Required', 'Build', 'Check for [ObservableProperty] and [RelayCommand] in ViewModels.'),
    (1, 1, 'Soft Delete for Tickets', 'WorkflowRule', 'Tickets should be archived (soft-deleted) by setting IsDeleted=1 instead of being physically removed from the database.', 'Required', 'Ticket', 'Ensure ArchiveTicketAsync is used instead of a DELETE command.');
END
GO

-- 4. Seed Local Dev Data
-- Seed Tenant
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Slug = 'local-dev')
BEGIN
    INSERT INTO dbo.Tenants (Name, Slug, IsActive) VALUES ('Local Dev', 'local-dev', 1);
END
GO

-- Seed User (Bob Developer)
-- Password: 'change-me-local-only'
DECLARE @PassHash NVARCHAR(MAX) = '$2a$11$1cy/VVDEmHFmY9ZSuzPojuyv91DkR3AuHEILmTmnQYA1T6oNWs6G.';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = 'bob@irondev.local')
BEGIN
    INSERT INTO dbo.Users (Email, DisplayName, PasswordHash, IsActive) 
    VALUES ('bob@irondev.local', 'Bob Developer', @PassHash, 1);
END
ELSE
BEGIN
    UPDATE dbo.Users SET PasswordHash = @PassHash, IsActive = 1 WHERE Email = 'bob@irondev.local';
END
GO

-- Seed TenantUser Mapping
DECLARE @BobId INT = (SELECT Id FROM dbo.Users WHERE Email = 'bob@irondev.local');
DECLARE @TenantId INT = (SELECT Id FROM dbo.Tenants WHERE Slug = 'local-dev');

IF @BobId IS NOT NULL AND @TenantId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @BobId)
    BEGIN
        INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@TenantId, @BobId, 'Owner');
    END
END
GO

-- Seed Project
DECLARE @TenantId INT = (SELECT Id FROM dbo.Tenants WHERE Slug = 'local-dev');
-- NOTE: Update LocalPath to match your machine if different
DECLARE @LocalPath NVARCHAR(500) = 'C:\Users\bob\source\repos\AIDeveloper';

IF @TenantId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Projects WHERE TenantId = @TenantId AND Name = 'IronDeveloper')
    BEGIN
        INSERT INTO dbo.Projects (TenantId, Name, Description, LocalPath, IndexingStatus)
        VALUES (@TenantId, 'IronDeveloper', 'Main IronDev development project.', @LocalPath, 'Needs Index');
    END
END
GO

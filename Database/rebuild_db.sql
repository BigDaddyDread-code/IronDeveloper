IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'IronDeveloper')
BEGIN
    CREATE DATABASE [IronDeveloper];
END
GO

USE [IronDeveloper];
GO

IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NOT NULL DROP TABLE dbo.ChatMessageFeedback;
IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NOT NULL DROP TABLE dbo.ChatTurnTraces;
IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NOT NULL DROP TABLE dbo.ChatTurnClarifications;
IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NOT NULL DROP TABLE dbo.ChatTurnGovernance;
IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NOT NULL DROP TABLE dbo.ArtifactSourceReferences;
IF OBJECT_ID('dbo.ProjectWorkItemActivity', 'U') IS NOT NULL DROP TABLE dbo.ProjectWorkItemActivity;
IF OBJECT_ID('dbo.ProjectWorkItemFollowers', 'U') IS NOT NULL DROP TABLE dbo.ProjectWorkItemFollowers;
IF OBJECT_ID('dbo.ProjectWorkItemCollaboration', 'U') IS NOT NULL DROP TABLE dbo.ProjectWorkItemCollaboration;
IF OBJECT_ID('dbo.ProjectTickets', 'U') IS NOT NULL DROP TABLE dbo.ProjectTickets;
IF OBJECT_ID('dbo.ProjectRules', 'U') IS NOT NULL DROP TABLE dbo.ProjectRules;
IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DROP TABLE dbo.ProjectImplementationPlans;
IF OBJECT_ID('dbo.ProjectDecisions', 'U') IS NOT NULL DROP TABLE dbo.ProjectDecisions;
IF OBJECT_ID('dbo.ProjectContextDocuments', 'U') IS NOT NULL DROP TABLE dbo.ProjectContextDocuments;
IF OBJECT_ID('dbo.ProjectObservableStates', 'U') IS NOT NULL DROP TABLE dbo.ProjectObservableStates;
IF OBJECT_ID('dbo.DecisionCategories', 'U') IS NOT NULL DROP TABLE dbo.DecisionCategories;
IF OBJECT_ID('dbo.DecisionStatuses', 'U') IS NOT NULL DROP TABLE dbo.DecisionStatuses;
IF OBJECT_ID('dbo.ProjectSummaries', 'U') IS NOT NULL DROP TABLE dbo.ProjectSummaries;
IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DROP TABLE dbo.CodeIndexEntries;
IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL DROP TABLE dbo.ChatMessages;
IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DROP TABLE dbo.ProjectChatSessions;
IF OBJECT_ID('dbo.ProjectFiles', 'U') IS NOT NULL DROP TABLE dbo.ProjectFiles;
IF OBJECT_ID('dbo.ProjectMembers', 'U') IS NOT NULL DROP TABLE dbo.ProjectMembers;
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
    IndexedFileCount INT NULL,
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
    Tags NVARCHAR(MAX) NULL,
    ContextSummary NVARCHAR(MAX) NULL,
    LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ChatMessages_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ChatMessages_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ChatMessages_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ChatMessages_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id)
);

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
    BaDraftJson NVARCHAR(MAX) NULL,
    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnGovernance_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ChatTurnGovernance_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_ChatTurnGovernance_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ChatTurnGovernance_Sessions FOREIGN KEY (ChatSessionId) REFERENCES dbo.ProjectChatSessions(Id),
    CONSTRAINT FK_ChatTurnGovernance_Messages FOREIGN KEY (ChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

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
    CONSTRAINT FK_ProjectContextDocuments_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_ProjectContextDocuments_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id),
    CONSTRAINT FK_ProjectContextDocuments_Supersedes FOREIGN KEY (SupersedesDocumentId) REFERENCES dbo.ProjectContextDocuments(Id)
);

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
    CONSTRAINT FK_ProjectImplementationPlans_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
);

CREATE TABLE dbo.ProjectTickets
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_ProjectTickets_Tenant DEFAULT 1,
    ProjectId INT NOT NULL,
    SessionId UNIQUEIDENTIFIER NULL, -- Made NULLable for modern flows
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
    CONSTRAINT FK_CodeIndexEntries_Files FOREIGN KEY (FileId) REFERENCES dbo.ProjectFiles(Id) ON DELETE CASCADE
);

CREATE INDEX IX_ChatMessages_ProjectId_ChatSessionId_CreatedDate
    ON dbo.ChatMessages(ProjectId, ChatSessionId, CreatedDate DESC);

CREATE INDEX IX_ProjectChatSessions_ProjectId_UpdatedDate
    ON dbo.ProjectChatSessions(ProjectId, UpdatedDate DESC);

CREATE INDEX IX_ProjectSummaries_ProjectId_CreatedDate
    ON dbo.ProjectSummaries(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectDecisions_ProjectId_CreatedDate
    ON dbo.ProjectDecisions(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectContextDocuments_Project_Type_Authority
    ON dbo.ProjectContextDocuments(ProjectId, DocumentType, AuthorityLevel, Status, CreatedDate DESC);

CREATE UNIQUE INDEX UX_ProjectObservableStates_Tenant_Project
    ON dbo.ProjectObservableStates(TenantId, ProjectId);

CREATE INDEX IX_ProjectImplementationPlans_ProjectId_CreatedDate
    ON dbo.ProjectImplementationPlans(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ProjectTickets_ProjectId_CreatedDate
    ON dbo.ProjectTickets(ProjectId, CreatedDate DESC);

CREATE INDEX IX_ArtifactSourceReferences_Artifact
    ON dbo.ArtifactSourceReferences(TenantId, ProjectId, ArtifactType, ArtifactId);

CREATE INDEX IX_ArtifactSourceReferences_Source
    ON dbo.ArtifactSourceReferences(TenantId, ProjectId, SourceType, SourceId);

CREATE INDEX IX_ProjectFiles_ProjectId_FilePath
    ON dbo.ProjectFiles(ProjectId, FilePath);

CREATE INDEX IX_ProjectFiles_ProjectId_FileExtension
    ON dbo.ProjectFiles(ProjectId, FileExtension);

CREATE UNIQUE INDEX UX_ProjectFiles_ProjectId_FilePath
    ON dbo.ProjectFiles(ProjectId, FilePath);

CREATE INDEX IX_ChatMessageFeedback_ProjectId_CreatedDate
    ON dbo.ChatMessageFeedback(ProjectId, CreatedDate DESC);

CREATE UNIQUE INDEX UX_ChatTurnGovernance_MessageTenant
    ON dbo.ChatTurnGovernance(ChatMessageId, TenantId);

CREATE UNIQUE INDEX UX_ChatTurnClarifications_MessageTenant
    ON dbo.ChatTurnClarifications(ChatMessageId, TenantId);

CREATE UNIQUE INDEX UX_ChatTurnTraces_MessageTenant
    ON dbo.ChatTurnTraces(ChatMessageId, TenantId);
GO

-- Tenant 1: Default
INSERT INTO dbo.Tenants (Name, Slug) VALUES ('Default Tenant', 'default');
-- Tenant 2: Isolation
INSERT INTO dbo.Tenants (Name, Slug) VALUES ('Other Tenant', 'other');

-- Admin user — password: 'password123'
DECLARE @PassHash NVARCHAR(MAX) = '$2a$11$FYTpo427b7HP/56mYM/eqeVCaTfJ48BaZDj40vU0BTWouGwDRhiFS';
INSERT INTO dbo.Users (Email, DisplayName, PasswordHash)
VALUES ('admin@irondev.local', 'Admin User', @PassHash);

-- Admin only belongs to Tenant 1, NOT Tenant 2.
INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (1, 1, 'Owner');
GO

INSERT INTO dbo.Projects (TenantId, Name, Description)
VALUES (1, 'IronDev', 'AI-assisted development workflow project');
GO

-- Lookup Tables
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

INSERT INTO dbo.DecisionCategories (Name, SortOrder) VALUES
    ('Architecture', 1), ('Code Standards', 2), ('Product', 3), ('Data', 4), ('Infrastructure', 5), ('AI / Prompting', 6), ('UX / UI', 7), ('Workflow / Process', 8), ('Integration', 9), ('Security', 10);

INSERT INTO dbo.DecisionStatuses (Name, SortOrder) VALUES
    ('Proposed', 1), ('Accepted', 2), ('Superseded', 3), ('Rejected', 4);
GO

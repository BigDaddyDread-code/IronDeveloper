/*
  IronDev LocalTest seed.

  This script is intentionally destructive inside a database whose name contains
  "Test". It should be run only after the LocalTest schema has been created.
*/

IF DB_NAME() NOT LIKE '%Test%'
BEGIN
    THROW 51000, 'Refusing to seed LocalTest data outside a test database.', 1;
END
GO

IF OBJECT_ID('dbo.RunEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RunEvents
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventId UNIQUEIDENTIFIER NOT NULL,
        RunId NVARCHAR(100) NOT NULL,
        TimestampUtc DATETIME2 NOT NULL,
        EventType NVARCHAR(100) NOT NULL,
        Message NVARCHAR(MAX) NOT NULL,
        PayloadJson NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_RunEvents_CreatedUtc DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_RunEvents_EventId ON dbo.RunEvents(EventId);
    CREATE INDEX IX_RunEvents_RunId_Timestamp ON dbo.RunEvents(RunId, TimestampUtc, Id);
END
GO

IF OBJECT_ID('dbo.Runs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Runs
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId NVARCHAR(100) NOT NULL,
        ProjectId INT NULL,
        TicketId BIGINT NULL,
        State NVARCHAR(50) NOT NULL,
        IsDisposable BIT NOT NULL CONSTRAINT DF_Runs_IsDisposable DEFAULT 0,
        Summary NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Runs_Summary DEFAULT '',
        FailureReason NVARCHAR(MAX) NULL,
        WorkspacePath NVARCHAR(1000) NULL,
        CreatedUtc DATETIME2 NOT NULL,
        UpdatedUtc DATETIME2 NOT NULL,
        StartedUtc DATETIME2 NULL,
        CompletedUtc DATETIME2 NULL
    );

    CREATE UNIQUE INDEX UX_Runs_RunId ON dbo.Runs(RunId);
    CREATE INDEX IX_Runs_ProjectTicketUpdated ON dbo.Runs(ProjectId, TicketId, UpdatedUtc DESC);
END
GO

IF COL_LENGTH('dbo.ProjectTickets', 'SourceDocumentVersionId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceDocumentVersionId BIGINT NULL;
END
GO

-- Reset the LocalTest world. The database guard above keeps this fenced.
IF OBJECT_ID('dbo.ProjectChannelPins', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelPins;
IF OBJECT_ID('dbo.ProjectChannelMessageReads', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageReads;
IF OBJECT_ID('dbo.ProjectChannelAssistantTurns', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelAssistantTurns;
IF OBJECT_ID('dbo.ProjectChannelMessageContextLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageContextLinks;
IF OBJECT_ID('dbo.ProjectChannelMessages', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessages;
IF OBJECT_ID('dbo.ProjectChannelMembers', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMembers;
IF OBJECT_ID('dbo.ProjectChannels', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannels;
IF OBJECT_ID('dbo.ProjectDocumentLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentLinks;
IF OBJECT_ID('dbo.ProjectDocumentVersions', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentVersions;
IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocuments;
IF OBJECT_ID('dbo.RunEvents', 'U') IS NOT NULL DELETE FROM dbo.RunEvents;
IF OBJECT_ID('dbo.Runs', 'U') IS NOT NULL DELETE FROM dbo.Runs;
IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NOT NULL DELETE FROM dbo.ChatMessageFeedback;
IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NOT NULL DELETE FROM dbo.ArtifactSourceReferences;
IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DELETE FROM dbo.CodeIndexEntries;
IF OBJECT_ID('dbo.ProjectProfiles', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfiles;
IF OBJECT_ID('dbo.ProjectCommands', 'U') IS NOT NULL DELETE FROM dbo.ProjectCommands;
IF OBJECT_ID('dbo.ProjectRules', 'U') IS NOT NULL DELETE FROM dbo.ProjectRules;
IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DELETE FROM dbo.ProjectImplementationPlans;
IF OBJECT_ID('dbo.ProjectDecisions', 'U') IS NOT NULL DELETE FROM dbo.ProjectDecisions;
IF OBJECT_ID('dbo.ProjectSummaries', 'U') IS NOT NULL DELETE FROM dbo.ProjectSummaries;
IF OBJECT_ID('dbo.ProjectTickets', 'U') IS NOT NULL DELETE FROM dbo.ProjectTickets;
IF OBJECT_ID('dbo.ProjectFiles', 'U') IS NOT NULL DELETE FROM dbo.ProjectFiles;
IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL DELETE FROM dbo.ChatMessages;
IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DELETE FROM dbo.ProjectChatSessions;
IF OBJECT_ID('dbo.Projects', 'U') IS NOT NULL DELETE FROM dbo.Projects;
IF OBJECT_ID('dbo.TenantUsers', 'U') IS NOT NULL DELETE FROM dbo.TenantUsers;
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DELETE FROM dbo.Users;
IF OBJECT_ID('dbo.Tenants', 'U') IS NOT NULL DELETE FROM dbo.Tenants;
GO

SET IDENTITY_INSERT dbo.Tenants ON;
INSERT INTO dbo.Tenants (Id, Name, Slug, IsActive)
VALUES (1, 'Local Test Tenant', 'local-test', 1);
SET IDENTITY_INSERT dbo.Tenants OFF;
GO

-- Password: change-me-local-only
DECLARE @PassHash NVARCHAR(MAX) = '$2a$11$1cy/VVDEmHFmY9ZSuzPojuyv91DkR3AuHEILmTmnQYA1T6oNWs6G.';

SET IDENTITY_INSERT dbo.Users ON;
INSERT INTO dbo.Users (Id, Email, DisplayName, PasswordHash, IsActive)
VALUES (1, 'bob@irondev.local', 'Bob Developer', @PassHash, 1);
SET IDENTITY_INSERT dbo.Users OFF;

INSERT INTO dbo.TenantUsers (TenantId, UserId, Role)
VALUES (1, 1, 'Owner');
GO

SET IDENTITY_INSERT dbo.Projects ON;
INSERT INTO dbo.Projects (Id, TenantId, Name, Description, LocalPath, LastIndexedUtc, IndexingStatus, IndexedFileCount)
VALUES
(
    1,
    1,
    'IronDev Local Test Project',
    'Seeded project for manual Tauri cockpit testing against isolated LocalTest data.',
    'C:\IronDevTestWorkspaces\IronDevLocalTestProject',
    SYSUTCDATETIME(),
    'Ready',
    2
),
(
    2,
    1,
    'BookSeller Test Fixture',
    'Realistic LocalTest fixture for provisioning, build, review, and apply journeys.',
    'C:\IronDevTestWorkspaces\BookSellerTestFixture',
    SYSUTCDATETIME(),
    'Ready',
    4
),
(
    3,
    1,
    'IronDev Setup Test Project',
    'Disposable LocalTest fixture with one setup decision awaiting confirmation.',
    'C:\IronDevTestWorkspaces\IronDevSetupTestProject',
    SYSUTCDATETIME(),
    'Ready',
    2
);
SET IDENTITY_INSERT dbo.Projects OFF;
GO

SET IDENTITY_INSERT dbo.ProjectChannels ON;
INSERT INTO dbo.ProjectChannels
    (Id, TenantId, ProjectId, Name, Slug, Description, ChannelKind, Visibility, Status, CreatedByUserId)
VALUES
    (101, 1, 1, 'General', 'general', 'Project-wide engineering discussion.', 'General', 'Project', 'Active', 1),
    (102, 1, 1, 'Product planning', 'product-planning', 'Restricted product and UX discussion.', 'Custom', 'MembersOnly', 'Active', 1),
    (103, 1, 2, 'General', 'general', 'BookSeller fixture discussion.', 'General', 'Project', 'Active', 1);
SET IDENTITY_INSERT dbo.ProjectChannels OFF;

INSERT INTO dbo.ProjectChannelMembers
    (TenantId, ProjectId, ChannelId, UserId, ChannelRole, NotificationLevel, Status, AddedByUserId)
VALUES
    (1, 1, 101, 1, 'Owner', 'All', 'Active', 1),
    (1, 1, 102, 1, 'Owner', 'Mentions', 'Active', 1),
    (1, 2, 103, 1, 'Owner', 'All', 'Active', 1);
GO

SET IDENTITY_INSERT dbo.ProjectChannelMessages ON;
INSERT INTO dbo.ProjectChannelMessages
    (Id, TenantId, ProjectId, ChannelId, AuthorUserId, Role, Message, MessageFormat, Status)
VALUES
    (10001, 1, 1, 101, 1, 'User', 'Shared channels keep human collaboration visible without creating authority.', 'Markdown', 'Active'),
    (10002, 1, 1, 102, 1, 'User', 'Keep channel visibility separate from workflow authority.', 'Markdown', 'Active'),
    (10003, 1, 2, 103, 1, 'User', 'BookSeller planning belongs here; governed execution still belongs to the work item.', 'Markdown', 'Active');
SET IDENTITY_INSERT dbo.ProjectChannelMessages OFF;
GO

INSERT INTO dbo.ProjectProfiles
    (TenantId, ProjectId, IsExternalProject, ApplicationType, PrimaryLanguage, Framework, DatabaseEngine, DataAccessStyle, TestFramework, SolutionFile, SafeWriteRoot, AllowBuilderApply, AllowWritesOutsideProjectRoot, ProfileNotes)
VALUES
    (1, 1, 1, 'ClassLibrary', 'CSharp', 'DotNet10', 'None', 'None', 'None', 'IronDevLocalTestProject.csproj', 'C:\IronDevTestWorkspaces\IronDevLocalTestProject', 1, 0, 'Tiny deterministic baseline fixture for every PR.'),
    (1, 2, 1, 'ConsoleApp', 'CSharp', 'DotNet10', 'None', 'InMemory', 'SelfTest', 'BookSeller.TestFixture.csproj', 'C:\IronDevTestWorkspaces\BookSellerTestFixture', 1, 0, 'Realistic BookSeller fixture for provisioning/build/review/apply manual testing.'),
    (1, 3, 1, 'ClassLibrary', 'CSharp', 'DotNet10', 'None', 'None', 'None', 'IronDevSetupTestProject.csproj', 'C:\IronDevTestWorkspaces\IronDevSetupTestProject', 1, 0, 'Disposable guided-setup fixture; the build command is intentionally unconfirmed.');
GO

INSERT INTO dbo.ProjectCommands
    (TenantId, ProjectId, CommandType, CommandText, WorkingDirectory, TimeoutSeconds, IsDefault, IsEnabled)
VALUES
    (1, 1, 'Build', 'dotnet build "IronDevLocalTestProject.csproj" -v quiet', 'C:\IronDevTestWorkspaces\IronDevLocalTestProject', 300, 1, 1),
    (1, 1, 'Test', 'dotnet build "IronDevLocalTestProject.csproj" -v quiet', 'C:\IronDevTestWorkspaces\IronDevLocalTestProject', 300, 1, 1),
    (1, 2, 'Build', 'dotnet build "BookSeller.TestFixture.csproj" -v quiet', 'C:\IronDevTestWorkspaces\BookSellerTestFixture', 300, 1, 1),
    (1, 2, 'Test', 'dotnet run --project "BookSeller.TestFixture.csproj" -- --self-test', 'C:\IronDevTestWorkspaces\BookSellerTestFixture', 300, 1, 1),
    (1, 3, 'Test', 'dotnet build "IronDevSetupTestProject.csproj" -v quiet', 'C:\IronDevTestWorkspaces\IronDevSetupTestProject', 300, 1, 1);
GO

SET IDENTITY_INSERT dbo.ProjectChatSessions ON;
INSERT INTO dbo.ProjectChatSessions (Id, TenantId, ProjectId, Title, Summary)
VALUES
(
    4001,
    1,
    1,
    'LocalTest cockpit planning',
    'Seeded trace source for manual testing linked ticket context.'
),
(
    4002,
    1,
    2,
    'BookSeller fixture planning',
    'Seeded trace source for the realistic LocalTest fixture.'
);
SET IDENTITY_INSERT dbo.ProjectChatSessions OFF;
GO

SET IDENTITY_INSERT dbo.ChatMessages ON;
INSERT INTO dbo.ChatMessages (Id, TenantId, ProjectId, ChatSessionId, Role, Message, ContextSummary, Tags)
VALUES
(
    5001,
    1,
    1,
    4001,
    'user',
    'Make the ticket workspace feel like a governed AI engineering cockpit.',
    'Seeded LocalTest trace message.',
    'localtest'
),
(
    5002,
    1,
    2,
    4002,
    'user',
    'Add search-by-author behavior to the BookSeller fixture.',
    'Seeded BookSeller LocalTest trace message.',
    'localtest,bookseller'
);
SET IDENTITY_INSERT dbo.ChatMessages OFF;
GO

SET IDENTITY_INSERT dbo.ProjectDocuments ON;
INSERT INTO dbo.ProjectDocuments
    (Id, TenantId, ProjectId, Title, Slug, DocumentType, CurrentVersionId, Status, CreatedBy, UpdatedBy)
VALUES
    (1001, 1, 1, 'Workspace Manual Test Notes', 'workspace-manual-test-notes', 'DiscussionSummary', 2001, 'Active', 'localtest-seed', 'localtest-seed'),
    (1002, 1, 1, 'Code Standards Draft', 'code-standards-draft', 'Architecture', 2002, 'Active', 'localtest-seed', 'localtest-seed'),
    (1003, 1, 1, 'Testing Companion Direction', 'testing-companion-direction', 'BuildPlan', 2003, 'Active', 'localtest-seed', 'localtest-seed');
SET IDENTITY_INSERT dbo.ProjectDocuments OFF;
GO

SET IDENTITY_INSERT dbo.ProjectDocumentVersions ON;
INSERT INTO dbo.ProjectDocumentVersions
    (Id, DocumentId, VersionMajor, VersionMinor, ContentMarkdown, ChangeSummary, Status, CreatedBy)
VALUES
(
    2001,
    1001,
    0,
    1,
    '# Workspace Manual Test Notes

Use this seeded project to verify the LocalTest badge, ticket list, document list, execution evidence, and honest disabled states.',
    'Initial LocalTest manual notes.',
    'Draft',
    'localtest-seed'
),
(
    2002,
    1002,
    0,
    1,
    '# Code Standards Draft

- Keep React features behind typed API clients.
- Keep governed workflow actions visible.
- Do not fake execution evidence.',
    'Initial LocalTest code standard draft.',
    'Draft',
    'localtest-seed'
),
(
    2003,
    1003,
    0,
    1,
    '# Testing Companion Direction

Manual UI testing should use resettable LocalTest data and should never depend on real development project rows.',
    'Initial LocalTest test-agent direction.',
    'Draft',
    'localtest-seed'
);
SET IDENTITY_INSERT dbo.ProjectDocumentVersions OFF;
GO

SET IDENTITY_INSERT dbo.ProjectTickets ON;
INSERT INTO dbo.ProjectTickets
(
    Id,
    TenantId,
    ProjectId,
    SessionId,
    Title,
    TicketType,
    Priority,
    Summary,
    Background,
    Problem,
    AcceptanceCriteria,
    TechnicalNotes,
    Status,
    Content,
    LinkedFilePaths,
    LinkedSymbols,
    UnitTests,
    IntegrationTests,
    ManualTests,
    BuildValidation,
    ContextSummary,
    IsGenerated,
    GenerationNote,
    SourceChatSessionId,
    SourceChatMessageId,
    SourceDocumentVersionId
)
VALUES
(
    3001,
    1,
    1,
    NEWID(),
    'Add Governed Tool Architecture',
    'Feature',
    'High',
    'Define the governed tool boundary for future agent actions.',
    'IronDev must expose capable tools without letting agents bypass review or write policies.',
    'Tool capability needs product visibility and safety boundaries.',
    'Tool permissions are visible; unsafe actions are blocked; no real repo writes are implied.',
    'Seeded LocalTest ticket with no linked run yet.',
    'Ready',
    'Define a governed tool architecture with evidence-first execution and hard apply boundaries.',
    '["IronDev.Core/Interfaces"]',
    '["ITicketEvidenceSummaryService"]',
    'Service contract tests cover permission boundaries.',
    'API endpoint tests cover blocked and allowed cases.',
    'Verify blocked actions are visible in the cockpit.',
    'dotnet build IronDev.slnx',
    'No linked run yet; this ticket should show an honest empty execution evidence state.',
    0,
    NULL,
    4001,
    5001,
    2002
),
(
    3002,
    1,
    1,
    NEWID(),
    'Wire Start Sandbox Run',
    'Feature',
    'High',
    'Wire the ticket cockpit to the real disposable build-run backend.',
    'Ticket evidence should move from readiness to a real run and then to review.',
    'Users need a visible loop from work contract to execution evidence.',
    'Start run calls the backend; evidence links by project/ticket payload; Review Latest Run opens only for linked runs.',
    'Seeded LocalTest ticket includes a linked run event for review-panel testing.',
    'In Review',
    'Connect Start Sandbox Run to the backend and surface the linked run review panel.',
    '["IronDev.Api/Controllers/TicketsController.cs","IronDev.TauriShell/src/features/tickets"]',
    '["TicketRunReviewDto","TicketEvidenceSummaryDto"]',
    'Service tests cover no linked run and linked run states.',
    'Endpoint contract tests cover wrong project and missing run.',
    'Click Review Latest Run and verify the seeded run panel opens.',
    'dotnet build IronDev.slnx; npm run build',
    'This ticket has seeded linked run evidence.',
    0,
    NULL,
    4001,
    5001,
    2001
),
(
    3003,
    1,
    1,
    NEWID(),
    'Improve Ticket Workspace UI',
    'Task',
    'Medium',
    'Make Tickets feel like orchestration inputs rather than CRUD records.',
    'The Tauri shell now has workflow commands, evidence sections, and inspector panels.',
    'The workspace needs calm, repeatable manual-test data.',
    'Ticket detail, inspector, and command bar render stable states against LocalTest.',
    'Seeded LocalTest UI hardening ticket.',
    'Draft',
    'Harden the Ticket workspace layout, empty states, and disabled action reasons.',
    '["IronDev.TauriShell/src/features/tickets"]',
    '["WorkspaceCommand","ContextInspector"]',
    'Component-level checks can be added later.',
    'Playwright smoke validates the core loop.',
    'Use the LocalTest manual checklist.',
    'npm run build; npm run test',
    'Linked to LocalTest testing companion direction.',
    0,
    NULL,
    NULL,
    NULL,
    2003
),
(
    3101,
    1,
    2,
    NEWID(),
    'Add Search By Author',
    'Feature',
    'High',
    'Exercise a realistic BookSeller change through the governed loop.',
    'The BookSeller fixture is intentionally small but shaped like a real repository.',
    'Users need a repeatable engineering journey against a non-trivial fixture.',
    'Searching by author returns matching books in title order; refusal paths stay visible; apply remains bounded to the fixture root.',
    'Seeded BookSeller ticket for provisioning/build/review/apply manual testing.',
    'Ready',
    'Implement and verify search-by-author behavior in the BookSeller fixture.',
    '["Program.cs","BookSeller.TestFixture.csproj"]',
    '["CatalogService","BookSellerSelfTest"]',
    'Self-test mode covers the seeded behavior.',
    'LocalTest build/test commands run against the fixture root.',
    'Select BookSeller Test Fixture, open the ticket, run the governed journey, and reload backend-owned state.',
    'dotnet build BookSeller.TestFixture.csproj; dotnet run --project BookSeller.TestFixture.csproj -- --self-test',
    'BookSeller realistic fixture ticket.',
    0,
    NULL,
    4002,
    5002,
    NULL
);
SET IDENTITY_INSERT dbo.ProjectTickets OFF;
GO

INSERT INTO dbo.ProjectDocumentLinks (DocumentVersionId, LinkedEntityType, LinkedEntityId, LinkType, CreatedBy)
VALUES
    (2001, 'Ticket', 3002, 'References', 'localtest-seed'),
    (2002, 'Ticket', 3001, 'References', 'localtest-seed'),
    (2003, 'Ticket', 3003, 'References', 'localtest-seed');
GO

INSERT INTO dbo.ArtifactSourceReferences
    (TenantId, ProjectId, ArtifactType, ArtifactId, SourceType, SourceId, ReferenceType, Summary, IsRequired, CreatedBy)
VALUES
    (1, 1, 'Ticket', 3001, 'ProjectDocumentVersion', 2002, 'CreatedFrom', 'Ticket was seeded from Code Standards Draft.', 1, 'localtest-seed'),
    (1, 1, 'Ticket', 3002, 'ProjectDocumentVersion', 2001, 'CreatedFrom', 'Ticket was seeded from Workspace Manual Test Notes.', 1, 'localtest-seed'),
    (1, 1, 'Ticket', 3003, 'ProjectDocumentVersion', 2003, 'CreatedFrom', 'Ticket was seeded from Testing Companion Direction.', 1, 'localtest-seed');
GO

INSERT INTO dbo.Runs
    (RunId, ProjectId, TicketId, State, IsDisposable, Summary, FailureReason, WorkspacePath, CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc)
VALUES
(
    'localtest-run-ticket-3002',
    1,
    3002,
    'Completed',
    1,
    'Seeded sandbox run completed for manual review.',
    NULL,
    NULL,
    DATEADD(minute, -8, SYSUTCDATETIME()),
    DATEADD(minute, -5, SYSUTCDATETIME()),
    DATEADD(minute, -8, SYSUTCDATETIME()),
    DATEADD(minute, -5, SYSUTCDATETIME())
);
GO

INSERT INTO dbo.RunEvents (EventId, RunId, TimestampUtc, EventType, Message, PayloadJson)
VALUES
(
    NEWID(),
    'localtest-run-ticket-3002',
    DATEADD(minute, -8, SYSUTCDATETIME()),
    'RunStarted',
    'Ticket build run started for ticket 3002.',
    '{"projectId":"1","ticketId":"3002","disposableRun":"true","currentNode":"LoadTicket","status":"Running"}'
),
(
    NEWID(),
    'localtest-run-ticket-3002',
    DATEADD(minute, -7, SYSUTCDATETIME()),
    'StepCompleted',
    'Loaded ticket and prepared disposable workspace context.',
    '{"projectId":"1","ticketId":"3002","disposableRun":"true","currentNode":"GeneratePlan","status":"Running","node":"LoadTicket"}'
),
(
    NEWID(),
    'localtest-run-ticket-3002',
    DATEADD(minute, -6, SYSUTCDATETIME()),
    'ToolCallCompleted',
    'Build readiness evidence was gathered for the seeded LocalTest run.',
    '{"projectId":"1","ticketId":"3002","disposableRun":"true","currentNode":"RunValidation","status":"Running","toolName":"BuildReadiness","node":"RunValidation"}'
),
(
    NEWID(),
    'localtest-run-ticket-3002',
    DATEADD(minute, -5, SYSUTCDATETIME()),
    'RunCompleted',
    'Seeded disposable run completed for manual review.',
    '{"projectId":"1","ticketId":"3002","disposableRun":"true","currentNode":"Completed","status":"Completed"}'
);
GO

PRINT 'LocalTest seed complete.';
GO

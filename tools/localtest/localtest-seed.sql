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
VALUES (1, 'localtest@irondev.local', 'Local Test User', @PassHash, 1);
SET IDENTITY_INSERT dbo.Users OFF;

INSERT INTO dbo.TenantUsers (TenantId, UserId, Role)
VALUES (1, 1, 'Owner');
GO

SET IDENTITY_INSERT dbo.Projects ON;
INSERT INTO dbo.Projects (Id, TenantId, Name, Description, LocalPath, IndexingStatus, IndexedFileCount)
VALUES
(
    1,
    1,
    'IronDev Local Test Project',
    'Seeded project for manual Tauri cockpit testing against isolated LocalTest data.',
    'C:\IronDevTestWorkspaces\IronDevLocalTestProject',
    'LocalTest Seeded',
    0
);
SET IDENTITY_INSERT dbo.Projects OFF;
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

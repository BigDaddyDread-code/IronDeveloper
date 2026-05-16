USE [IronDeveloper];
GO

-- 1. Cleanup old/test data
DECLARE @IronDevProjId INT = 2;
DECLARE @BookSellerProjId INT = 5;

-- Delete from junk projects (1, 3, 4)
DELETE FROM dbo.ChatMessageFeedback WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ChatMessages WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectChatSessions WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectImplementationPlans WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectTickets WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectSummaries WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectDecisions WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.CodeIndexEntries WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectFiles WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectProfiles WHERE ProjectId IN (1, 3, 4);
DELETE FROM dbo.ProjectCommands WHERE ProjectId IN (1, 3, 4);

-- Clean up active IronDev project (2)
DELETE FROM dbo.ChatMessageFeedback WHERE ProjectId = @IronDevProjId;
DELETE FROM dbo.ChatMessages WHERE ProjectId = @IronDevProjId;
DELETE FROM dbo.ProjectChatSessions WHERE ProjectId = @IronDevProjId;
DELETE FROM dbo.ProjectImplementationPlans WHERE ProjectId = @IronDevProjId;
DELETE FROM dbo.ProjectTickets WHERE ProjectId = @IronDevProjId;
DELETE FROM dbo.ProjectSummaries WHERE ProjectId = @IronDevProjId;
-- Keep only essential decisions if any (currently keeping 1, 2)
DELETE FROM dbo.ProjectDecisions WHERE ProjectId = @IronDevProjId AND Id NOT IN (1, 2);

-- Delete the junk project records
DELETE FROM dbo.Projects WHERE Id IN (1, 3, 4);

-- 2. Update IronDev project record
UPDATE dbo.Projects 
SET Name = 'IronDev',
    Description = 'Main IronDev development project (Project-of-Record).',
    LocalPath = 'C:\Users\bob\source\repos\AIDeveloper',
    IndexingStatus = 'Needs Index',
    IndexedFileCount = 0
WHERE Id = @IronDevProjId;

-- 3. Set up Project Profile for IronDev
IF NOT EXISTS (SELECT 1 FROM dbo.ProjectProfiles WHERE ProjectId = @IronDevProjId)
BEGIN
    INSERT INTO dbo.ProjectProfiles (TenantId, ProjectId, IsExternalProject, ApplicationType, PrimaryLanguage, Framework, DatabaseEngine, DataAccessStyle, TestFramework, SolutionFile, SafeWriteRoot, AllowBuilderApply, AllowWritesOutsideProjectRoot, ProfileNotes)
    VALUES (3, @IronDevProjId, 0, 'MixedSolution', 'CSharp', 'DotNet10', 'SQLServer', 'Dapper', 'MSTest', 'IronDeveloper\IronDeveloper.slnx', 'C:\Users\bob\source\repos\AIDeveloper', 0, 0, 'Project-of-record for IronDev.');
END

-- 4. Set up Project Commands for IronDev
IF NOT EXISTS (SELECT 1 FROM dbo.ProjectCommands WHERE ProjectId = @IronDevProjId AND CommandType = 'Build')
BEGIN
    INSERT INTO dbo.ProjectCommands (TenantId, ProjectId, CommandType, CommandText, WorkingDirectory)
    VALUES (3, @IronDevProjId, 'Build', 'dotnet build IronDeveloper\IronDeveloper.slnx', 'C:\Users\bob\source\repos\AIDeveloper');
END

IF NOT EXISTS (SELECT 1 FROM dbo.ProjectCommands WHERE ProjectId = @IronDevProjId AND CommandType = 'Test')
BEGIN
    INSERT INTO dbo.ProjectCommands (TenantId, ProjectId, CommandType, CommandText, WorkingDirectory)
    VALUES (3, @IronDevProjId, 'Test', 'dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj', 'C:\Users\bob\source\repos\AIDeveloper');
END

-- 5. Seed Architecture Decisions
-- (Using a fresh batch to avoid ID conflicts)
INSERT INTO dbo.ProjectDecisions (TenantId, ProjectId, Title, Detail, Reason, Category, Status)
VALUES 
(3, @IronDevProjId, 'IronDev Work Hierarchy', 'IronDev work should be structured as: Product Vision -> Project Blueprint -> Capability -> Milestone -> Implementation Plan -> Ticket -> Builder Proposal -> Build/Test Result. Tickets are micro-level buildable tasks.', 'Establish clear planning hierarchy.', 'Workflow / Process', 'Accepted'),
(3, @IronDevProjId, 'Builder Cannot Invent Architecture Decisions', 'Builder can implement inside existing approved project decisions, but must not invent new architecture decisions. Must ask user first.', 'Prevent architectural drift.', 'AI / Prompting', 'Accepted'),
(3, @IronDevProjId, 'Project Profile Is Source of Truth', 'Project Profile is the source of truth for project type, language, framework, database engine, data access style, test framework, etc.', 'Ensure tool grounding.', 'Infrastructure', 'Accepted'),
(3, @IronDevProjId, 'Context Modes', 'Context Agent must distinguish between CodeEvidence, ArchitectureAdvice, ArchitectureDecision, TicketCreation, BuildExecution, and GeneralDiscussion.', 'Optimize prompt relevance.', 'AI / Prompting', 'Accepted'),
(3, @IronDevProjId, 'Build Readiness Before Builder', 'IronDev must evaluate ticket build readiness (decisions, profile, scope) before generating code.', 'Reduce failed builds.', 'Workflow / Process', 'Accepted'),
(3, @IronDevProjId, 'Safe Builder Apply', 'Builder may only write files under SafeWriteRoot after user approval. Must show diffs and validate paths.', 'Safety and auditability.', 'Security', 'Accepted'),
(3, @IronDevProjId, 'BookSeller Is External Sandbox', 'BookSeller at C:\repo\BookSeller is for safe testing only. Must remain outside IronDev repo.', 'Isolation of test targets.', 'Infrastructure', 'Accepted'),
(3, @IronDevProjId, 'IronDev Persistence Standard', 'IronDev uses SQL Server with Dapper. Do not introduce EF Core without explicit decision.', 'Consistency.', 'Architecture', 'Accepted'),
(3, @IronDevProjId, 'IronDev Test Standard', 'IronDev uses MSTest for unit/integration coverage.', 'Consistency.', 'Architecture', 'Accepted'),
(3, @IronDevProjId, 'IronDev Logging Standard', 'IronDev uses Serilog. Follow existing patterns.', 'Consistency.', 'Architecture', 'Accepted'),
(3, @IronDevProjId, 'Stale Index Must Be Visible', 'Warn user if files change after indexing. Builder requires fresh index.', 'Accuracy.', 'UX / UI', 'Accepted'),
(3, @IronDevProjId, 'Builder v0.2 Scope', 'Builder v0.2 is apply + build/test for sandbox only. No auto-fix loop or git automation yet.', 'Controlled rollout.', 'Product', 'Accepted');
GO

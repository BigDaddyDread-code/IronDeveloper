USE [IronDeveloper];
GO

-- 1. SEED TENANTS
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Slug = 'irondev-project')
    INSERT INTO dbo.Tenants (Name, Slug) VALUES ('IronDev Project', 'irondev-project');

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Slug = 'irondev-test')
    INSERT INTO dbo.Tenants (Name, Slug) VALUES ('IronDev Test', 'irondev-test');

GO

-- 2. SEED USERS
-- Password: 'password123' (BCrypt hash)
DECLARE @PassHash NVARCHAR(MAX) = '$2a$11$FYTpo427b7HP/56mYM/eqeVCaTfJ48BaZDj40vU0BTWouGwDRhiFS';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = 'bob@irondeveloper.local')
    INSERT INTO dbo.Users (Email, DisplayName, PasswordHash) VALUES ('bob@irondeveloper.local', 'Bob Developer', @PassHash);
ELSE
    UPDATE dbo.Users SET PasswordHash = @PassHash WHERE Email = 'bob@irondeveloper.local';

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = 'devadmin@irondeveloper.local')
    INSERT INTO dbo.Users (Email, DisplayName, PasswordHash) VALUES ('devadmin@irondeveloper.local', 'Dev Admin', @PassHash);
ELSE
    UPDATE dbo.Users SET PasswordHash = @PassHash WHERE Email = 'devadmin@irondeveloper.local';

GO

-- 3. SEED USERTENANT MAPPINGS
DECLARE @BobId INT = (SELECT Id FROM dbo.Users WHERE Email = 'bob@irondeveloper.local');
DECLARE @AdminId INT = (SELECT Id FROM dbo.Users WHERE Email = 'devadmin@irondeveloper.local');
DECLARE @ProjectId INT = (SELECT Id FROM dbo.Tenants WHERE Slug = 'irondev-project');
DECLARE @TestId INT = (SELECT Id FROM dbo.Tenants WHERE Slug = 'irondev-test');

-- bob -> Project
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId = @ProjectId AND UserId = @BobId)
    INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@ProjectId, @BobId, 'Owner');

-- devadmin -> Project
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId = @ProjectId AND UserId = @AdminId)
    INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@ProjectId, @AdminId, 'Member');

-- devadmin -> Test
IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId = @TestId AND UserId = @AdminId)
    INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@TestId, @AdminId, 'Owner');

GO

-- 4. SEED PROJECTS
DECLARE @ProjectId INT = (SELECT Id FROM dbo.Tenants WHERE Slug = 'irondev-project');

-- IronDeveloper (Dogfood)
IF NOT EXISTS (SELECT 1 FROM dbo.Projects WHERE TenantId = @ProjectId AND Name = 'IronDeveloper')
BEGIN
    INSERT INTO dbo.Projects (TenantId, Name, Description, LocalPath) 
    VALUES (@ProjectId, 'IronDeveloper', 'Main IronDev Dogfooding Project', 'C:\Users\bob\source\repos\AIDeveloper');
END

GO

-- 5. SEED INITIAL TICKETS (Dogfood)
DECLARE @ProjectTableId INT = (SELECT Id FROM dbo.Projects WHERE Name = 'IronDeveloper');
DECLARE @TenantTableId INT = (SELECT TenantId FROM dbo.Projects WHERE Id = @ProjectTableId);

IF @ProjectTableId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectTickets WHERE ProjectId = @ProjectTableId AND Title = 'Port workflow shell')
        INSERT INTO dbo.ProjectTickets (TenantId, ProjectId, SessionId, Title, Status, Content, Summary)
        VALUES (@TenantTableId, @ProjectTableId, NEWID(), 'Port workflow shell', 'Completed', 'Port the TestApp design into main app.', 'Scaffolding Phase Done.');

    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectTickets WHERE ProjectId = @ProjectTableId AND Title = 'Add V1 code indexing')
        INSERT INTO dbo.ProjectTickets (TenantId, ProjectId, SessionId, Title, Status, Content, Summary)
        VALUES (@TenantTableId, @ProjectTableId, NEWID(), 'Add V1 code indexing', 'Active', 'Implement manual directory scan + symbols.', 'Indexing Foundation.');
END

GO

-- 6. SEED INITIAL DECISIONS (Dogfood)
DECLARE @ProjectTableId INT = (SELECT Id FROM dbo.Projects WHERE Name = 'IronDeveloper');
DECLARE @TenantTableId INT = (SELECT TenantId FROM dbo.Projects WHERE Id = @ProjectTableId);

IF @ProjectTableId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectDecisions WHERE ProjectId = @ProjectTableId AND Title = 'Controls Isolation')
        INSERT INTO dbo.ProjectDecisions (TenantId, ProjectId, Title, Detail, Reason)
        VALUES (@TenantTableId, @ProjectTableId, 'Controls Isolation', 'Keep IronDeveloperControls library separate from main app.', 'Max reusability and clean design-system boundary.');

    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectDecisions WHERE ProjectId = @ProjectTableId AND Title = 'Local First Memory')
        INSERT INTO dbo.ProjectDecisions (TenantId, ProjectId, Title, Detail, Reason)
        VALUES (@TenantTableId, @ProjectTableId, 'Local First Memory', 'Use SQL Server as primary working memory before GitHub sync.', 'Latency and reliability for internal developer loop.');
END
GO

-- Project Profile Tables Migration

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectProfiles')
BEGIN
    CREATE TABLE dbo.ProjectProfiles (
        ProjectProfileId bigint IDENTITY(1,1) PRIMARY KEY,
        TenantId int NOT NULL,
        ProjectId int NOT NULL,
        IsExternalProject bit NOT NULL DEFAULT 0,
        ApplicationType nvarchar(100) NULL,
        PrimaryLanguage nvarchar(100) NULL,
        Framework nvarchar(100) NULL,
        RuntimeVersion nvarchar(100) NULL,
        DatabaseEngine nvarchar(100) NULL,
        DataAccessStyle nvarchar(100) NULL,
        TestFramework nvarchar(100) NULL,
        SolutionFile nvarchar(1000) NULL,
        SafeWriteRoot nvarchar(1000) NULL,
        AllowBuilderApply bit NOT NULL DEFAULT 0,
        AllowWritesOutsideProjectRoot bit NOT NULL DEFAULT 0,
        ProfileNotes nvarchar(max) NULL,
        CreatedUtc datetime2 NOT NULL DEFAULT sysutcdatetime(),
        UpdatedUtc datetime2 NULL,
        CONSTRAINT UK_ProjectProfiles_TenantProject UNIQUE (TenantId, ProjectId)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectCommands')
BEGIN
    CREATE TABLE dbo.ProjectCommands (
        ProjectCommandId bigint IDENTITY(1,1) PRIMARY KEY,
        TenantId int NOT NULL,
        ProjectId int NOT NULL,
        CommandType nvarchar(50) NOT NULL,
        CommandText nvarchar(max) NOT NULL,
        WorkingDirectory nvarchar(1000) NULL,
        TimeoutSeconds int NOT NULL DEFAULT 300,
        IsDefault bit NOT NULL DEFAULT 1,
        IsEnabled bit NOT NULL DEFAULT 1,
        CreatedUtc datetime2 NOT NULL DEFAULT sysutcdatetime(),
        UpdatedUtc datetime2 NULL
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProjectProfileOptions')
BEGIN
    CREATE TABLE dbo.ProjectProfileOptions (
        ProjectProfileOptionId int IDENTITY(1,1) PRIMARY KEY,
        Category nvarchar(100) NOT NULL,
        Value nvarchar(100) NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        SortOrder int NOT NULL DEFAULT 0,
        IsActive bit NOT NULL DEFAULT 1
    );
END
GO

-- Seed ProjectProfileOptions
IF NOT EXISTS (SELECT 1 FROM dbo.ProjectProfileOptions)
BEGIN
    INSERT INTO dbo.ProjectProfileOptions (Category, Value, DisplayName, SortOrder)
    VALUES 
    ('ApplicationType', 'WPFDesktop', 'WPF Desktop', 10),
    ('ApplicationType', 'WebApi', 'Web API', 20),
    ('ApplicationType', 'ConsoleApp', 'Console App', 30),
    ('ApplicationType', 'ClassLibrary', 'Class Library', 40),
    ('ApplicationType', 'WorkerService', 'Worker Service', 50),
    ('ApplicationType', 'ExternalSandbox', 'External Sandbox', 60),
    ('ApplicationType', 'MixedSolution', 'Mixed Solution', 70),
    ('ApplicationType', 'Other', 'Other', 80),
    ('ApplicationType', 'Unknown', 'Unknown', 90),

    ('PrimaryLanguage', 'CSharp', 'C#', 10),
    ('PrimaryLanguage', 'TypeScript', 'TypeScript', 20),
    ('PrimaryLanguage', 'Python', 'Python', 30),
    ('PrimaryLanguage', 'Other', 'Other', 40),
    ('PrimaryLanguage', 'Unknown', 'Unknown', 50),

    ('Framework', 'DotNet10', '.NET 10', 10),
    ('Framework', 'DotNet8', '.NET 8', 20),
    ('Framework', 'Node', 'Node.js', 30),
    ('Framework', 'Python', 'Python', 40),
    ('Framework', 'Other', 'Other', 50),
    ('Framework', 'Unknown', 'Unknown', 60),

    ('DatabaseEngine', 'None', 'None', 10),
    ('DatabaseEngine', 'SQLServer', 'SQL Server', 20),
    ('DatabaseEngine', 'SQLite', 'SQLite', 30),
    ('DatabaseEngine', 'PostgreSQL', 'PostgreSQL', 40),
    ('DatabaseEngine', 'MySQL', 'MySQL', 50),
    ('DatabaseEngine', 'Other', 'Other', 60),
    ('DatabaseEngine', 'Unknown', 'Unknown', 70),

    ('DataAccessStyle', 'None', 'None', 10),
    ('DataAccessStyle', 'InMemory', 'In-Memory', 20),
    ('DataAccessStyle', 'Dapper', 'Dapper', 30),
    ('DataAccessStyle', 'EFCore', 'EF Core', 40),
    ('DataAccessStyle', 'RawAdoNet', 'Raw ADO.NET', 50),
    ('DataAccessStyle', 'FileJson', 'File/JSON', 60),
    ('DataAccessStyle', 'ExternalApi', 'External API', 70),
    ('DataAccessStyle', 'Other', 'Other', 80),
    ('DataAccessStyle', 'Unknown', 'Unknown', 90),

    ('TestFramework', 'xUnit', 'xUnit', 10),
    ('TestFramework', 'MSTest', 'MSTest', 20),
    ('TestFramework', 'NUnit', 'NUnit', 30),
    ('TestFramework', 'None', 'None', 40),
    ('TestFramework', 'Unknown', 'Unknown', 50);
END
GO

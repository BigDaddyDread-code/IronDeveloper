using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.SqlClient;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Services;
using IronDev.AI;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Builder;

namespace IronDev.IntegrationTests;

/// <summary>
/// A mutable tenant context for tests — lets each test switch tenants to verify isolation.
/// </summary>
public sealed class TestTenantContext : ICurrentTenantContext
{
    public int TenantId { get; set; }

    public TestTenantContext(int tenantId = 1) => TenantId = tenantId;
}

[TestClass]
public abstract class IntegrationTestBase
{
    protected IServiceProvider ServiceProvider { get; private set; } = default!;
    protected string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Mutable tenant context — tests can switch tenants mid-run to verify isolation.
    /// </summary>
    protected TestTenantContext TenantContext { get; private set; } = default!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json")
            .Build();

        ConnectionString = configuration.GetConnectionString("IronDeveloperDb")
            ?? throw new InvalidOperationException("Missing test connection string.");

        TenantContext = new TestTenantContext(tenantId: 1);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

        // Register the switchable context as a singleton so the same instance is used
        // throughout the test scope — tests mutate TenantContext.TenantId to switch tenants.
        services.AddSingleton<ICurrentTenantContext>(TenantContext);

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IChatHistoryService, ChatHistoryService>();
        services.AddScoped<IChatFeedbackService, ChatFeedbackService>();
        services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ICodeIndexService, SqlCodeIndexService>();
        services.AddScoped<IPromptContextBuilder, PromptContextBuilder>();
        services.AddScoped<IBuilderContextService, BuilderContextService>();
        services.AddScoped<IUserService, UserService>();

        ServiceProvider = services.BuildServiceProvider();

        await ResetDatabaseAsync();
    }

    protected async Task ResetDatabaseAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Delete in correct FK order: children before parents.
        var sql = """
            IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NOT NULL DELETE FROM dbo.ChatMessageFeedback;
            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DELETE FROM dbo.CodeIndexEntries;
            IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DELETE FROM dbo.ProjectImplementationPlans;
            IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DELETE FROM dbo.ProjectChatSessions;
            IF OBJECT_ID('dbo.ProjectRules', 'U') IS NOT NULL DELETE FROM dbo.ProjectRules;
            DELETE FROM dbo.ChatMessages;
            DELETE FROM dbo.ProjectDecisions;
            DELETE FROM dbo.ProjectFiles;
            DELETE FROM dbo.ProjectTickets;
            DELETE FROM dbo.ProjectSummaries;
            DELETE FROM dbo.Projects;
            DELETE FROM dbo.TenantUsers;
            DELETE FROM dbo.Users;
            DELETE FROM dbo.Tenants;
            """;

        await connection.ExecuteAsync(sql);
    }

    /// <summary>
    /// Seeds the default test tenant (Id=1) if not already present, then creates a project under it.
    /// Returns the new project Id.
    /// </summary>
    protected async Task<int> SeedProjectAsync(int tenantId = 1, string name = "IronDev")
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var setupSql = $"""
            -- Ensure Tenants and Users exist (Parents)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'IsActive')
                ALTER TABLE dbo.Tenants ADD IsActive BIT NOT NULL DEFAULT 1;

            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = {tenantId})
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug, IsActive) VALUES ({tenantId}, 'Test Tenant {tenantId}', 'test-{tenantId}', 1);
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Tenants SET IsActive = 1 WHERE Id = {tenantId};
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.Users ON;
                INSERT INTO dbo.Users (Id, Email, DisplayName, IsActive) VALUES (1, 'test@test.com', 'Test', 1);
                SET IDENTITY_INSERT dbo.Users OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Users SET IsActive = 1 WHERE Id = 1;
            END

            -- Ensure core tables exist
            IF OBJECT_ID('dbo.Projects', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Projects
                (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    Name NVARCHAR(200) NOT NULL,
                    Description NVARCHAR(MAX) NULL,
                    LocalPath NVARCHAR(500) NULL,
                    LastIndexedUtc DATETIME2 NULL,
                    IndexingStatus NVARCHAR(50) NULL,
                    IndexedFileCount INT NULL,
                    CONSTRAINT FK_Projects_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
            END

            IF OBJECT_ID('dbo.ProjectTickets', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectTickets
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    SessionId UNIQUEIDENTIFIER NULL,
                    Title NVARCHAR(200) NOT NULL DEFAULT '',
                    TicketType NVARCHAR(50) NOT NULL DEFAULT 'Task',
                    Priority NVARCHAR(50) NOT NULL DEFAULT 'Medium',
                    Summary NVARCHAR(MAX) NULL,
                    Background NVARCHAR(MAX) NULL,
                    Problem NVARCHAR(MAX) NULL,
                    AcceptanceCriteria NVARCHAR(MAX) NULL,
                    TechnicalNotes NVARCHAR(MAX) NULL,
                    Status NVARCHAR(50) NOT NULL DEFAULT 'Draft',
                    Content NVARCHAR(MAX) NOT NULL DEFAULT '',
                    LinkedFilePaths NVARCHAR(MAX) NULL,
                    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
                    LinkedSymbols NVARCHAR(MAX) NULL,
                    UnitTests NVARCHAR(MAX) NULL,
                    IntegrationTests NVARCHAR(MAX) NULL,
                    ManualTests NVARCHAR(MAX) NULL,
                    RegressionTests NVARCHAR(MAX) NULL,
                    BuildValidation NVARCHAR(MAX) NULL,
                    ContextSummary NVARCHAR(MAX) NULL,
                    IsGenerated BIT NOT NULL DEFAULT 0,
                    GenerationNote NVARCHAR(MAX) NULL,
                    IsDeleted BIT NOT NULL DEFAULT 0,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ProjectTickets_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectTickets_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            IF OBJECT_ID('dbo.ProjectFiles', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectFiles
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    FilePath NVARCHAR(1000) NOT NULL,
                    FileExtension NVARCHAR(50) NOT NULL,
                    ContentHash NVARCHAR(100) NOT NULL,
                    Content NVARCHAR(MAX) NOT NULL,
                    LastIndexedUtc DATETIME2 NULL,
                    IndexingStatus NVARCHAR(50) NULL,
                    CONSTRAINT FK_ProjectFiles_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectFiles_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            IF OBJECT_ID('dbo.ProjectDecisions', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectDecisions
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    Title NVARCHAR(200) NOT NULL,
                    Detail NVARCHAR(MAX) NOT NULL,
                    Reason NVARCHAR(MAX) NULL,
                    Category NVARCHAR(100) NULL,
                    Status NVARCHAR(50) NOT NULL DEFAULT 'Accepted',
                    LinkedFilePaths NVARCHAR(MAX) NULL,
                    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
                    LinkedSymbols NVARCHAR(MAX) NULL,
                    SourceChatMessageId BIGINT NULL,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ProjectDecisions_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectDecisions_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            IF OBJECT_ID('dbo.ChatMessages', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatMessages
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NULL,
                    SessionId UNIQUEIDENTIFIER NULL,
                    Sender NVARCHAR(50) NOT NULL,
                    Text NVARCHAR(MAX) NOT NULL,
                    ContextSummary NVARCHAR(MAX) NULL,
                    LinkedFilePaths NVARCHAR(MAX) NULL,
                    LinkedSymbols NVARCHAR(MAX) NULL,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ChatMessages_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ChatMessages_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            -- Extend Projects (Grounding)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Projects') AND name = 'LastIndexedUtc')
                ALTER TABLE dbo.Projects ADD LastIndexedUtc DATETIME2 NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Projects') AND name = N'IndexingStatus')
                ALTER TABLE dbo.Projects ADD IndexingStatus NVARCHAR(50) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Projects') AND name = N'IndexedFileCount')
                ALTER TABLE dbo.Projects ADD IndexedFileCount INT NULL;

            -- Extend ProjectFiles (Grounding)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectFiles') AND name = 'LastIndexedUtc')
                ALTER TABLE dbo.ProjectFiles ADD LastIndexedUtc DATETIME2 NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectFiles') AND name = 'IndexingStatus')
                ALTER TABLE dbo.ProjectFiles ADD IndexingStatus NVARCHAR(50) NULL;

            -- Extend ProjectDecisions
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectDecisions') AND name = 'LinkedFilePaths')
            BEGIN
                ALTER TABLE dbo.ProjectDecisions ADD LinkedFilePaths NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectDecisions ADD LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectDecisions ADD LinkedSymbols NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectDecisions ADD SourceChatMessageId BIGINT NULL;
            END

            -- Extend ProjectTickets (Structured Tickets)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Title')
                ALTER TABLE dbo.ProjectTickets ADD Title NVARCHAR(200) NOT NULL DEFAULT '';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'TicketType')
                ALTER TABLE dbo.ProjectTickets ADD TicketType NVARCHAR(50) NOT NULL DEFAULT 'Task';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Priority')
                ALTER TABLE dbo.ProjectTickets ADD Priority NVARCHAR(50) NOT NULL DEFAULT 'Medium';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Summary')
                ALTER TABLE dbo.ProjectTickets ADD Summary NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Background')
                ALTER TABLE dbo.ProjectTickets ADD Background NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = N'Problem')
                ALTER TABLE dbo.ProjectTickets ADD Problem NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'AcceptanceCriteria')
                ALTER TABLE dbo.ProjectTickets ADD AcceptanceCriteria NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'TechnicalNotes')
                ALTER TABLE dbo.ProjectTickets ADD TechnicalNotes NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Status')
                ALTER TABLE dbo.ProjectTickets ADD Status NVARCHAR(50) NOT NULL DEFAULT 'Draft';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'LinkedFilePaths')
                ALTER TABLE dbo.ProjectTickets ADD LinkedFilePaths NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'LinkedCodeIndexEntryIds')
                ALTER TABLE dbo.ProjectTickets ADD LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'LinkedSymbols')
                ALTER TABLE dbo.ProjectTickets ADD LinkedSymbols NVARCHAR(MAX) NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'IsDeleted')
                ALTER TABLE dbo.ProjectTickets ADD IsDeleted BIT NOT NULL DEFAULT 0;

            -- Extend ProjectTickets (Extended Draft Fields)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'UnitTests')
            BEGIN
                ALTER TABLE dbo.ProjectTickets ADD UnitTests NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectTickets ADD IntegrationTests NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectTickets ADD ManualTests NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectTickets ADD RegressionTests NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectTickets ADD BuildValidation NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectTickets ADD ContextSummary NVARCHAR(MAX) NULL;
                ALTER TABLE dbo.ProjectTickets ADD IsGenerated BIT NOT NULL DEFAULT 0;
                ALTER TABLE dbo.ProjectTickets ADD GenerationNote NVARCHAR(MAX) NULL;
            END
            
            -- Legacy support
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'SessionId' AND is_nullable = 0)
                ALTER TABLE dbo.ProjectTickets ALTER COLUMN SessionId UNIQUEIDENTIFIER NULL;

            -- Ensure ProjectChatSessions exists
            IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectChatSessions
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    Title NVARCHAR(200) NOT NULL DEFAULT 'New Chat',
                    Summary NVARCHAR(MAX) NULL,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
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

            -- Extend ChatMessages (Sessions & Grounding)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'ChatSessionId')
                ALTER TABLE dbo.ChatMessages ADD ChatSessionId BIGINT NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'ContextSummary')
                ALTER TABLE dbo.ChatMessages ADD ContextSummary NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'LinkedFilePaths')
                ALTER TABLE dbo.ChatMessages ADD LinkedFilePaths NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'LinkedSymbols')
                ALTER TABLE dbo.ChatMessages ADD LinkedSymbols NVARCHAR(MAX) NULL;

            -- Fix SessionId and ChatSessionId nullability drift
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'SessionId' AND is_nullable = 0)
                ALTER TABLE dbo.ChatMessages ALTER COLUMN SessionId UNIQUEIDENTIFIER NULL;
            
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'ChatSessionId' AND is_nullable = 0)
                ALTER TABLE dbo.ChatMessages ALTER COLUMN ChatSessionId BIGINT NULL;

            -- Ensure ProjectImplementationPlans exists
            IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectImplementationPlans
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    TicketId BIGINT NULL,
                    Title NVARCHAR(200) NOT NULL,
                    Goal NVARCHAR(MAX) NOT NULL,
                    Scope NVARCHAR(MAX) NULL,
                    ProposedSteps NVARCHAR(MAX) NULL,
                    AffectedContext NVARCHAR(MAX) NULL,
                    RisksNotes NVARCHAR(MAX) NULL,
                    Status NVARCHAR(50) NOT NULL DEFAULT 'Draft',
                    LinkedFilePaths NVARCHAR(MAX) NULL,
                    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
                    LinkedSymbols NVARCHAR(MAX) NULL,
                    SourceChatMessageId BIGINT NULL,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ProjectImplementationPlans_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectImplementationPlans_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END
            
            -- Ensure CodeIndexEntries table exists
            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.CodeIndexEntries
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    FileId BIGINT NOT NULL,
                    Namespace NVARCHAR(500) NULL,
                    SymbolName NVARCHAR(500) NULL,
                    SymbolType NVARCHAR(50) NULL,
                    Summary NVARCHAR(MAX) NULL,
                    ChunkText NVARCHAR(MAX) NOT NULL,
                    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_CodeIndexEntries_CreatedDate DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_CodeIndexEntries_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_CodeIndexEntries_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            -- Ensure ChatMessageFeedback table exists
            IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatMessageFeedback
                (
                    Id             BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId       INT NOT NULL,
                    ProjectId      INT NOT NULL,
                    ChatSessionId  BIGINT NULL,
                    ChatMessageId  BIGINT NULL,
                    Rating         NVARCHAR(50) NOT NULL,
                    Reason         NVARCHAR(200) NULL,
                    Comment        NVARCHAR(MAX) NULL,
                    CreatedDate    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ChatMessageFeedback_Tenants   FOREIGN KEY (TenantId)  REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ChatMessageFeedback_Projects  FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            -- Ensure ProjectRules table exists
            IF OBJECT_ID('dbo.ProjectRules', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectRules
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    Name NVARCHAR(200) NOT NULL,
                    Type NVARCHAR(50) NOT NULL, -- CodeStandard, ArchitectureDecision, etc.
                    Description NVARCHAR(MAX) NOT NULL,
                    EnforcementLevel NVARCHAR(50) NOT NULL, -- Advisory, Required, Blocking
                    AppliesTo NVARCHAR(50) NOT NULL, -- Ticket, Build, Both
                    ValidationHint NVARCHAR(MAX) NULL,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ProjectRules_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectRules_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END
            """;
        await connection.ExecuteAsync(setupSql);

        const string insertSql = """
            INSERT INTO dbo.Projects (TenantId, Name, Description)
            OUTPUT inserted.Id
            VALUES (@TenantId, @Name, @Description);
            """;

        return await connection.QuerySingleAsync<int>(insertSql, new
        {
            TenantId = tenantId,
            Name = name,
            Description = "Integration test project"
        });
    }
}

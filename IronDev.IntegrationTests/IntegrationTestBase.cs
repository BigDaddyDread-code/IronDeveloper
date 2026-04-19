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
        services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ICodeIndexService, SqlCodeIndexService>();
        services.AddScoped<IPromptContextBuilder, PromptContextBuilder>();
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
            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DELETE FROM dbo.CodeIndexEntries;
            DELETE FROM dbo.ProjectDecisions;
            DELETE FROM dbo.ProjectFiles;
            DELETE FROM dbo.ChatMessages;
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
            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = {tenantId})
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug) VALUES ({tenantId}, 'Test Tenant {tenantId}', 'test-{tenantId}');
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
            IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.Users ON;
                INSERT INTO dbo.Users (Id, Email, DisplayName) VALUES (1, 'test@test.com', 'Test');
                SET IDENTITY_INSERT dbo.Users OFF;
            END

            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL
            BEGIN
                -- Ensure table exists for tests
                PRINT 'CodeIndexEntries already exists'
            END
            ELSE
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
                    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_CodeIndexEntries_CreatedDate DEFAULT SYSUTCDATETIME()
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

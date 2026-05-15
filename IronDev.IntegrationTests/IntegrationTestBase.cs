using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.SqlClient;
using Dapper;
using IronDev.Core;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Services;
using IronDev.AI;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;

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
    public virtual async Task TestInitialize()
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
        services.AddScoped<ILlmTraceService, LlmTraceService>();
        services.AddScoped<ICodePatchService, CodePatchService>();
        services.AddScoped<IProjectProfileService, ProjectProfileService>();
        services.AddScoped<IBuilderProposalService, BuilderProposalService>();
        services.AddScoped<IBuildErrorClassifierService, BuildErrorClassifierService>();
        services.AddScoped<IBuilderReadinessService, BuilderReadinessService>();
        services.AddScoped<ICodeChangeProposalService, CodeChangeProposalService>();
        services.AddScoped<IDotNetBuildService, DotNetRunnerService>();
        services.AddScoped<IDotNetTestService, DotNetRunnerService>();
        services.AddScoped<ILLMService, FakeLlmService>();

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
            IF OBJECT_ID('dbo.ProjectProfiles', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfiles;
            IF OBJECT_ID('dbo.ProjectCommands', 'U') IS NOT NULL DELETE FROM dbo.ProjectCommands;
            IF OBJECT_ID('dbo.ProjectProfileOptions', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfileOptions;
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

    protected async Task<int> SeedProjectAsync(int tenantId = 1, string name = "IronDev", string? localPath = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        await connection.ExecuteAsync($"""
            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = {tenantId})
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug) VALUES ({tenantId}, 'Test Tenant {tenantId}', 'test-{tenantId}');
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
        """);

        const string insertSql = """
            INSERT INTO dbo.Projects (TenantId, Name, Description, LocalPath)
            OUTPUT inserted.Id
            VALUES (@TenantId, @Name, @Description, @LocalPath);
            """;

        return await connection.QuerySingleAsync<int>(insertSql, new
        {
            TenantId = tenantId,
            Name = name,
            Description = "Integration test project",
            LocalPath = localPath
        });
    }

    protected async Task SeedProjectProfileAsync(int projectId, string testFramework = "xUnit", bool allowBuilderApply = true, int tenantId = 1)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO dbo.ProjectProfiles (TenantId, ProjectId, TestFramework, AllowBuilderApply, CreatedUtc)
            VALUES (@TenantId, @ProjectId, @TestFramework, @AllowBuilderApply, SYSUTCDATETIME());
        """;

        await connection.ExecuteAsync(sql, new { TenantId = tenantId, ProjectId = projectId, TestFramework = testFramework, AllowBuilderApply = allowBuilderApply });
    }

    protected async Task SeedProjectCommandAsync(int projectId, string type, string command, int tenantId = 1)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO dbo.ProjectCommands (TenantId, ProjectId, CommandType, CommandText, IsDefault, IsEnabled, CreatedUtc)
            VALUES (@TenantId, @ProjectId, @CommandType, @CommandText, 1, 1, SYSUTCDATETIME());
        """;

        await connection.ExecuteAsync(sql, new { TenantId = tenantId, ProjectId = projectId, CommandType = type, CommandText = command });
    }
}

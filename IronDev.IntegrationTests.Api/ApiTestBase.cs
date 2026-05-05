using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Spins up the real IronDev.Api in-process against the test database.
/// Each test class gets a fresh database reset; the factory is shared across the class.
/// </summary>
[TestClass]
public abstract class ApiTestBase
{
    protected static WebApplicationFactory<Program> Factory { get; private set; } = default!;
    protected static HttpClient Client { get; private set; } = default!;
    protected static string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Known test credentials — seeded in SetupAsync.</summary>
    protected const string AdminEmail = "admin@irondev.local";
    protected const string AdminPassword = "password123";

    /// <summary>
    /// Tenant 1: admin IS a member.
    /// Tenant 2: admin is NOT a member (used for rejection tests).
    /// </summary>
    protected const int AssignedTenantId = 1;
    protected const int UnassignedTenantId = 2;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInitialize(TestContext _)
    {
        try
        {
            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Test");
                    // Force the configuration values into the builder so Program.cs picks them up
                    builder.UseSetting("Jwt:Key", "irondev-super-secret-jwt-key-change-in-production-min32chars");
                    builder.UseSetting("Jwt:Issuer", "irondev-api");
                    builder.UseSetting("Jwt:Audience", "irondev-client");
                    builder.UseSetting("ConnectionStrings:IronDeveloperDb", "Server=DESKTOP-KFA0H13;Database=IronDeveloper_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;");

                    builder.ConfigureAppConfiguration((context, cfg) =>
                    {
                        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json");
                        if (File.Exists(path))
                        {
                            cfg.AddJsonFile(path, optional: false);
                        }
                    });
                });

            Client = Factory.CreateClient();

            // Resolve connection string from the factory's configuration.
            var config = Factory.Services.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))
                as Microsoft.Extensions.Configuration.IConfiguration;
            
            if (config == null)
            {
                throw new InvalidOperationException("Could not resolve IConfiguration from Test Host.");
            }

            ConnectionString = config.GetConnectionString("IronDeveloperDb") 
                ?? throw new InvalidOperationException("Connection string 'IronDeveloperDb' not found in appsettings.Test.json");

            await SetupDatabaseAsync();
        }
        catch (Exception ex)
        {
            // Log to console so it's visible in test output
            Console.WriteLine($"FATAL: ClassInitialize failed: {ex}");
            throw;
        }
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassCleanup()
    {
        Client?.Dispose();
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize() => await ResetDomainDataAsync();

    // ── Database helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the required schema extensions exist.
    /// Called once per test class.
    /// </summary>
    private static async Task SetupDatabaseAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync("""
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
                    CONSTRAINT FK_ProjectChatSessions_Tenants_API FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectChatSessions_Projects_API FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
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
            """);
    }

    /// <summary>Clears domain data between tests; tenant/user seed is preserved and synchronized.</summary>
    private static async Task ResetDomainDataAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        
        // 1. Wipe domain data
        await conn.ExecuteAsync("""
            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DELETE FROM dbo.CodeIndexEntries;
            IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DELETE FROM dbo.ProjectImplementationPlans;
            IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DELETE FROM dbo.ProjectChatSessions;
            DELETE FROM dbo.ChatMessages;
            DELETE FROM dbo.ProjectSummaries;
            DELETE FROM dbo.ProjectFiles;
            DELETE FROM dbo.ProjectTickets;
            DELETE FROM dbo.Projects;
            """);

        // 2. Synchronize test tenants and users (resetting any changes from other test suites)
        var hash = BCrypt.Net.BCrypt.HashPassword(AdminPassword, workFactor: 4);
        
        await conn.ExecuteAsync("""
            -- Ensure Tenants and Users exist with expected names and status
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'IsActive')
                ALTER TABLE dbo.Tenants ADD IsActive BIT NOT NULL DEFAULT 1;

            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug, IsActive) VALUES (1, 'Default Tenant', 'default', 1);
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Tenants SET Name = 'Default Tenant', Slug = 'default', IsActive = 1 WHERE Id = 1;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 2)
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug, IsActive) VALUES (2, 'Other Tenant', 'other', 1);
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Tenants SET Name = 'Other Tenant', Slug = 'other', IsActive = 1 WHERE Id = 2;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.Users ON;
                INSERT INTO dbo.Users (Id, Email, DisplayName, PasswordHash, IsActive)
                VALUES (1, @Email, 'Admin User', @Hash, 1);
                SET IDENTITY_INSERT dbo.Users OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Users SET Email = @Email, DisplayName = 'Admin User', PasswordHash = @Hash, IsActive = 1 WHERE Id = 1;
            END

            -- Isolated membership: Admin is ONLY a member of Tenant 1.
            DELETE FROM dbo.TenantUsers WHERE UserId = 1;
            INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (1, 1, 'Owner');
            """, new { Email = AdminEmail, Hash = hash });
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    protected static async Task<string> LoginAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    protected static async Task<string> SelectTenantAsync(string baseToken, int tenantId = AssignedTenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/select");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        request.Content = JsonContent.Create(new { tenantId });

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    protected static HttpClient GetAuthedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

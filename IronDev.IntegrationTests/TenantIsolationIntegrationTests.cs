using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

/// <summary>
/// Proves that data seeded under TenantA is completely invisible to TenantB.
/// Each test seeds data under one tenant, switches the context to another,
/// and asserts that reads return nothing.
/// </summary>
[TestClass]
public class TenantIsolationIntegrationTests : IntegrationTestBase
{
    // ─── Projects ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TenantA_ShouldNotSeeTenantB_Projects()
    {
        // Seed a project under Tenant 2
        await SeedProjectAsync(tenantId: 2, name: "Tenant B Project");

        // Switch context to Tenant 1
        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();

        var projects = await projectService.GetProjectsAsync();

        Assert.AreEqual(0, projects.Count, "Tenant 1 should not see Tenant 2 projects.");
    }

    [TestMethod]
    public async Task TenantA_ShouldNotLoadTenantB_ProjectById()
    {
        // Seed a project under Tenant 2
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2, name: "Tenant B Project");

        // Switch context to Tenant 1
        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();

        var project = await projectService.GetByIdAsync(tenantBProjectId);

        Assert.IsNull(project, "Tenant 1 should not be able to load a Tenant 2 project by Id.");
    }

    // ─── Tickets ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TenantA_ShouldNotSeeTenantB_Tickets()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);

        // Directly insert a ticket under Tenant 2 (bypass service to avoid ownership guard).
        await DirectInsertTicketAsync(tenantId: 2, projectId: tenantBProjectId);

        // Switch to Tenant 1 and seed its own project.
        var tenantAProjectId = await SeedProjectAsync(tenantId: 1);
        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var tickets = await ticketService.GetRecentTicketsAsync(tenantBProjectId);

        Assert.AreEqual(0, tickets.Count, "Tenant 1 should not see Tenant 2 tickets.");
    }

    [TestMethod]
    public async Task TenantA_ShouldNotLoadTenantB_TicketById()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        var ticketId = await DirectInsertTicketAsync(tenantId: 2, projectId: tenantBProjectId);

        TenantContext.TenantId = 1;
        await SeedProjectAsync(tenantId: 1);

        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var ticket = await ticketService.GetTicketByIdAsync(ticketId);

        Assert.IsNull(ticket, "Tenant 1 should not be able to load a Tenant 2 ticket by Id.");
    }

    // ─── Chat Messages ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TenantA_ShouldNotSeeTenantB_ChatMessages()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        
        // Seed a session and message directly under Tenant 2
        var sessionId = await DirectInsertChatSessionAsync(tenantId: 2, projectId: tenantBProjectId, title: "Tenant B Session");
        await DirectInsertChatMessageAsync(tenantId: 2, projectId: tenantBProjectId, sessionId: sessionId);

        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatHistoryService>();

        // Attempting to read Tenant 2's session messages from Tenant 1 context
        var messages = await chatService.GetRecentMessagesAsync(tenantBProjectId, sessionId, 10);

        Assert.AreEqual(0, messages.Count, "Tenant 1 should not see Tenant 2 chat messages.");
    }

    // ─── Project Decisions ────────────────────────────────────────────────────

    [TestMethod]
    public async Task TenantA_ShouldNotSeeTenantB_ProjectDecisions()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        await DirectInsertDecisionAsync(tenantId: 2, projectId: tenantBProjectId);

        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();

        var decisions = await memoryService.GetRecentDecisionsAsync(tenantBProjectId);

        Assert.AreEqual(0, decisions.Count, "Tenant 1 should not see Tenant 2 decisions.");
    }

    // ─── Project Summaries ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task TenantA_ShouldNotSeeTenantB_ProjectSummaries()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        await DirectInsertSummaryAsync(tenantId: 2, projectId: tenantBProjectId);

        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();

        var summary = await memoryService.GetLatestSummaryAsync(tenantBProjectId);

        Assert.IsNull(summary, "Tenant 1 should not see Tenant 2 project summaries.");
    }

    // ─── Project Files (Code Index) ────────────────────────────────────────────

    [TestMethod]
    public async Task TenantA_ShouldNotSeeTenantB_ProjectFiles()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        await DirectInsertProjectFileAsync(tenantId: 2, projectId: tenantBProjectId);

        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();

        // Search for a keyword that exists in the Tenant 2 file.
        var results = await indexService.SearchFilesAsync(tenantBProjectId, "TenantBClass");

        Assert.AreEqual(0, results.Count, "Tenant 1 should not see Tenant 2 indexed files.");
    }

    // ─── Ownership Guard Tests ─────────────────────────────────────────────────

    [TestMethod]
    public async Task SaveTicket_WithOtherTenantProjectId_ShouldThrow()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var ticket = new ProjectTicket
        {
            ProjectId = tenantBProjectId,
            SessionId = Guid.NewGuid(),
            Title = "Cross-Tenant Ticket",
            Content = "Should be rejected"
        };

        try
        {
            await ticketService.SaveTicketAsync(ticket);
            Assert.Fail("Expected UnauthorizedAccessException was not thrown.");
        }
        catch (UnauthorizedAccessException)
        {
            // Expected — test passes.
        }
    }

    [TestMethod]
    public async Task SaveMessage_WithOtherTenantProjectId_ShouldThrow()
    {
        var tenantBProjectId = await SeedProjectAsync(tenantId: 2);
        TenantContext.TenantId = 1;

        using var scope = ServiceProvider.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatHistoryService>();

        var message = new ChatMessage
        {
            ProjectId = tenantBProjectId,
            ChatSessionId = 1, // Dummy ID
            Role = "user",
            Message = "Should be rejected"
        };

        try
        {
            await chatService.SaveMessageAsync(message);
            Assert.Fail("Expected UnauthorizedAccessException was not thrown.");
        }
        catch (UnauthorizedAccessException)
        {
            // Expected — test passes.
        }
    }

    // ─── Direct SQL helpers (bypass services for seeding other-tenant data) ────

    private async Task<long> DirectInsertTicketAsync(int tenantId, int projectId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<long>("""
            INSERT INTO dbo.ProjectTickets (TenantId, ProjectId, SessionId, Title, TicketType, Priority, Status, Content)
            OUTPUT inserted.Id
            VALUES (@TenantId, @ProjectId, NEWID(), 'Test Ticket', 'Task', 'Medium', 'Draft', 'Content');
            """, new { TenantId = tenantId, ProjectId = projectId });
    }

    private async Task<long> DirectInsertChatSessionAsync(int tenantId, int projectId, string title)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<long>("""
            INSERT INTO dbo.ProjectChatSessions (TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (@TenantId, @ProjectId, @Title);
            """, new { TenantId = tenantId, ProjectId = projectId, Title = title });
    }

    private async Task DirectInsertChatMessageAsync(int tenantId, int projectId, long sessionId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO dbo.ChatMessages (TenantId, ProjectId, ChatSessionId, Role, Message)
            VALUES (@TenantId, @ProjectId, @SessionId, 'user', 'Hello from Tenant B');
            """, new { TenantId = tenantId, ProjectId = projectId, SessionId = sessionId });
    }

    private async Task DirectInsertDecisionAsync(int tenantId, int projectId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO dbo.ProjectDecisions (TenantId, ProjectId, Title, Detail)
            VALUES (@TenantId, @ProjectId, 'Tenant B Decision', 'Should not be visible to Tenant A');
            """, new { TenantId = tenantId, ProjectId = projectId });
    }

    private async Task DirectInsertSummaryAsync(int tenantId, int projectId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO dbo.ProjectSummaries (TenantId, ProjectId, Summary)
            VALUES (@TenantId, @ProjectId, 'Tenant B summary — should not be visible to Tenant A');
            """, new { TenantId = tenantId, ProjectId = projectId });
    }

    private async Task DirectInsertProjectFileAsync(int tenantId, int projectId)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content)
            VALUES (@TenantId, @ProjectId, 'src/TenantBClass.cs', '.cs', 'hash-b', 'public class TenantBClass { }');
            """, new { TenantId = tenantId, ProjectId = projectId });
    }
}

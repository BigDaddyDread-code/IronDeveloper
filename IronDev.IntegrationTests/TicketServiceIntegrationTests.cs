using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.SqlClient;
using IronDev.Core.Interfaces;
using IronDev.Core.WorkItems;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class TicketServiceIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SaveTicketAsync_And_GetRecentTicketsAsync_ShouldReturnSavedTicket()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var projectId = await SeedProjectAsync();
        var sessionId = Guid.NewGuid();

        var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = sessionId,
            Title = "Add persistence test for UI save behavior",
            TicketType = "Task",
            Priority = "Medium",
            Status = "Draft",
            Summary = "Create a UI-focused test for save flow.",
            Background = "Need confidence in save behavior.",
            Problem = "No integration coverage exists.",
            AcceptanceCriteria = "- Save persists data correctly",
            Content = "Fallback content",
            TechnicalNotes = ""
        });

        var tickets = await ticketService.GetRecentTicketsAsync(projectId, 10);
        var loaded = await ticketService.GetTicketByIdAsync(ticketId);

        Assert.IsNotNull(loaded);
        Assert.IsTrue(ticketId > 0);
        Assert.HasCount(1, tickets);
        Assert.AreEqual("Add persistence test for UI save behavior", loaded.Title);
        Assert.AreEqual("Task", loaded.TicketType);
        Assert.AreEqual("Draft", loaded.Status);
    }

    [TestMethod]
    public async Task SaveTicketAsync_ShouldCreateDurableWorkItemIdentityAndVersionedContract()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var projectId = await SeedProjectAsync();
        var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = "Make Work Item identity durable",
            TicketType = "Task",
            Priority = "High",
            Status = "Draft",
            Summary = "Persist a Work Item identity row for each ticket-backed item.",
            Problem = "Ticket ids are doing double duty as Work Item ids.",
            AcceptanceCriteria = "- Work Item row exists\n- Current contract is linked",
            TechnicalNotes = "Keep current ticket-backed routes compatible.",
            Content = "Work Item identity contract test",
            LinkedFilePaths = "IronDev.Core/WorkItems/WorkItemIdentityModels.cs",
            SourceChatSessionId = 4001,
            SourceChatMessageId = 5001
        });

        var first = await LoadIdentityRowAsync(ticketId);

        Assert.IsNotNull(first);
        Assert.AreEqual(ticketId, first.Id);
        Assert.AreEqual(ticketId, first.LegacyTicketId);
        Assert.AreEqual(ProjectWorkItemStages.Ticket, first.CurrentStage);
        Assert.AreEqual("Draft", first.CurrentState);
        Assert.AreEqual(1, first.ContractVersion);
        Assert.AreEqual("Make Work Item identity durable", first.Title);
        Assert.AreEqual("4001", first.SourceWorkshopSessionId);
        Assert.AreEqual("5001", first.SourceWorkshopMessageIds);
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.ContractHash));

        var loaded = await ticketService.GetTicketByIdAsync(ticketId);
        Assert.IsNotNull(loaded);
        loaded.AcceptanceCriteria = "- Work Item row exists\n- Current contract is linked\n- Contract changes version";

        await ticketService.SaveTicketAsync(loaded);

        var second = await LoadIdentityRowAsync(ticketId);
        var contractCount = await CountContractsAsync(ticketId);

        Assert.IsNotNull(second);
        Assert.AreEqual(2, contractCount);
        Assert.AreEqual(2, second.ContractVersion);
        Assert.AreNotEqual(first.ContractHash, second.ContractHash);
    }

    [TestMethod]
    public async Task SaveTicketAsync_WithExtendedFields_ShouldPersistAllFields()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var projectId = await SeedProjectAsync();
        var sessionId = Guid.NewGuid();

        var ticket = new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = sessionId,
            Title = "Full Draft Persistence Test",
            TicketType = "Feature",
            Priority = "High",
            Status = "Draft",
            Summary = "Test all new columns.",
            Background = "Verify schema updates.",
            Problem = "Persistence risk for new fields.",
            AcceptanceCriteria = "All fields reload correctly.",
            Content = "Test content",
            LinkedFilePaths = "FileA.cs;FileB.cs",
            LinkedSymbols = "ClassA;MethodB",
            UnitTests = "Unit Test Content",
            IntegrationTests = "Integration Test Content",
            ManualTests = "Manual Test Content",
            RegressionTests = "Regression Test Content",
            BuildValidation = "dotnet test",
            ContextSummary = "AI Context Summary",
            IsGenerated = true,
            GenerationNote = "Generated by AI model X",
            SourceChatSessionId = 1001,
            SourceChatMessageId = 2002
        };

        var ticketId = await ticketService.SaveTicketAsync(ticket);
        var loaded = await ticketService.GetTicketByIdAsync(ticketId);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(ticket.Title, loaded.Title);
        Assert.AreEqual(ticket.TicketType, loaded.TicketType);
        Assert.AreEqual(ticket.Priority, loaded.Priority);
        Assert.AreEqual(ticket.Summary, loaded.Summary);
        Assert.AreEqual(ticket.Background, loaded.Background);
        Assert.AreEqual(ticket.Problem, loaded.Problem);
        Assert.AreEqual(ticket.AcceptanceCriteria, loaded.AcceptanceCriteria);
        Assert.AreEqual(ticket.LinkedFilePaths, loaded.LinkedFilePaths);
        Assert.AreEqual(ticket.LinkedSymbols, loaded.LinkedSymbols);
        Assert.AreEqual(ticket.UnitTests, loaded.UnitTests);
        Assert.AreEqual(ticket.IntegrationTests, loaded.IntegrationTests);
        Assert.AreEqual(ticket.ManualTests, loaded.ManualTests);
        Assert.AreEqual(ticket.RegressionTests, loaded.RegressionTests);
        Assert.AreEqual(ticket.BuildValidation, loaded.BuildValidation);
        Assert.AreEqual(ticket.ContextSummary, loaded.ContextSummary);
        Assert.AreEqual(ticket.IsGenerated, loaded.IsGenerated);
        Assert.AreEqual(ticket.GenerationNote, loaded.GenerationNote);
        Assert.AreEqual(ticket.SourceChatSessionId, loaded.SourceChatSessionId);
        Assert.AreEqual(ticket.SourceChatMessageId, loaded.SourceChatMessageId);
    }

    [TestMethod]
    public async Task SaveTicketAsync_WithSourceContext_ShouldRecordArtifactReferences()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var referenceService = scope.ServiceProvider.GetRequiredService<IArtifactSourceReferenceService>();

        var projectId = await SeedProjectAsync();
        var (sourceChatSessionId, sourceChatMessageId) = await SeedChatSourceAsync(projectId);

        var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = "Trace ticket source",
            TicketType = "Task",
            Priority = "Medium",
            Status = "Draft",
            Content = "Traceability test",
            SourceChatSessionId = sourceChatSessionId,
            SourceChatMessageId = sourceChatMessageId
        });

        var references = await referenceService.GetForArtifactAsync(
            tenantId: 1,
            projectId: projectId,
            artifactType: "Ticket",
            artifactId: ticketId);

        Assert.HasCount(2, references);
        Assert.IsTrue(references.Any(r =>
            r.SourceType == "ChatSession" &&
            r.SourceId == sourceChatSessionId &&
            r.ReferenceType == "CreatedFrom"));
        Assert.IsTrue(references.Any(r =>
            r.SourceType == "ChatMessage" &&
            r.SourceId == sourceChatMessageId &&
            r.ReferenceType == "CreatedFrom"));
    }

    [TestMethod]
    public async Task TicketTraceability_WithLegacySchema_ShouldSelfHealColumnsAndReferenceTable()
    {
        var projectId = await SeedProjectAsync();
        var (sourceChatSessionId, sourceChatMessageId) = await SeedChatSourceAsync(projectId);

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NOT NULL
                    DROP TABLE dbo.ArtifactSourceReferences;

                IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatSessionId') IS NOT NULL
                    ALTER TABLE dbo.ProjectTickets DROP COLUMN SourceChatSessionId;

                IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatMessageId') IS NOT NULL
                    ALTER TABLE dbo.ProjectTickets DROP COLUMN SourceChatMessageId;
                """);
        }

        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var referenceService = scope.ServiceProvider.GetRequiredService<IArtifactSourceReferenceService>();

        var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = "Legacy schema traceability",
            TicketType = "Task",
            Priority = "Medium",
            Status = "Draft",
            Content = "Legacy schema self-heal test",
            SourceChatSessionId = sourceChatSessionId,
            SourceChatMessageId = sourceChatMessageId
        });

        var loaded = await ticketService.GetTicketByIdAsync(ticketId);
        var references = await referenceService.GetForArtifactAsync(1, projectId, "Ticket", ticketId);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(sourceChatSessionId, loaded.SourceChatSessionId);
        Assert.AreEqual(sourceChatMessageId, loaded.SourceChatMessageId);
        Assert.HasCount(2, references);
    }

    private async Task<(long SessionId, long MessageId)> SeedChatSourceAsync(int projectId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sessionId = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO dbo.ProjectChatSessions (TenantId, ProjectId, Title)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, N'Trace source');
            """, new { ProjectId = projectId });
        var messageId = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO dbo.ChatMessages (TenantId, ProjectId, ChatSessionId, Role, Message)
            OUTPUT inserted.Id
            VALUES (1, @ProjectId, @SessionId, N'user', N'Trace source message');
            """, new { ProjectId = projectId, SessionId = sessionId });
        return (sessionId, messageId);
    }

    private async Task<WorkItemIdentityContractRow> LoadIdentityRowAsync(long ticketId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT
                wi.Id,
                wi.LegacyTicketId,
                wi.CurrentStage,
                wi.CurrentState,
                c.ContractVersion,
                c.ContractHash,
                c.Title,
                CONVERT(NVARCHAR(40), c.SourceWorkshopSessionId) AS SourceWorkshopSessionId,
                c.SourceWorkshopMessageIds
            FROM dbo.WorkItems wi
            INNER JOIN dbo.WorkItemContracts c ON c.Id = wi.CurrentContractId
            WHERE wi.LegacyTicketId = @TicketId;
            """;

        return await connection.QuerySingleAsync<WorkItemIdentityContractRow>(sql, new { TicketId = ticketId });
    }

    private async Task<int> CountContractsAsync(long ticketId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM dbo.WorkItemContracts WHERE SourceTicketId = @TicketId;",
            new { TicketId = ticketId });
    }

    private sealed class WorkItemIdentityContractRow
    {
        public long Id { get; set; }
        public long LegacyTicketId { get; set; }
        public string CurrentStage { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public int ContractVersion { get; set; }
        public string ContractHash { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? SourceWorkshopSessionId { get; set; }
        public string? SourceWorkshopMessageIds { get; set; }
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data.Models;
using IronDev.Services;
using IronDev.AI;

namespace IronDev.IntegrationTests;

[TestClass]
public class PromptContextBuilderIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task BuildAsync_ShouldIncludeSummary_Decisions_And_RecentMessages()
    {
        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var chatHistoryService = scope.ServiceProvider.GetRequiredService<IChatHistoryService>();
        var promptContextBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();

        var projectId = await SeedProjectAsync();
        
        // 1. Create a session
        var session = new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Test Integration Session",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        var sessionId = await chatHistoryService.SaveSessionAsync(session);

        await memoryService.SaveSummaryAsync(new ProjectSummary
        {
            ProjectId = projectId,
            Summary = "IronDev is a WPF-based AI development assistant."
        });

        await memoryService.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectId,
            Title = "Use SQL memory",
            Detail = "Store project memory in SQL first."
        });

        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(connection,
            "INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content) VALUES (@TenantId, @ProjectId, @FilePath, @FileExtension, @ContentHash, @Content)",
            new { TenantId = 1, ProjectId = projectId, FilePath = "MainWindow.xaml", FileExtension = ".xaml", ContentHash = "XYZ", Content = "<Window><Grid></Grid></Window>" });

        await chatHistoryService.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "user",
            Message = "Please create a ticket"
        });

        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            Title = "Main Window",
            TicketType = "Task",
            Status = "Draft",
            Content = "<Window><Grid></Grid></Window>"
        });

        // The query "MainWindow.xaml" will trigger the file search MVP logic
        var prompt = await promptContextBuilder.BuildAsync(projectId, sessionId, "Generate a ticket for MainWindow.xaml");

        StringAssert.Contains(prompt, "IronDev is a WPF-based AI development assistant.");
        StringAssert.Contains(prompt, "Use SQL memory");
        StringAssert.Contains(prompt, "Please create a ticket");
        StringAssert.Contains(prompt, "Generate a ticket for MainWindow.xaml");
        StringAssert.Contains(prompt, "<Window><Grid></Grid></Window>");
    }
}

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
            Summary = "IronDev is an API-first AI development platform with a Tauri shell."
        });

        await memoryService.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectId,
            Title = "Use SQL memory",
            Detail = "Store project memory in SQL first."
        });

        await memoryService.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            DocumentType = "ProjectStandard",
            AuthorityLevel = "StrongGuidance",
            Status = "Active",
            Title = "Use constructor injection",
            Content = "Services should use constructor injection instead of service locators.",
            Tags = "services,dependency injection"
        });

        await memoryService.SaveObservableStateAsync(new ProjectObservableState
        {
            ProjectId = projectId,
            ActiveCapability = "Ticket creation",
            CurrentFocus = "Generate grounded tickets",
            BuildReadiness = "Ready"
        });

        var indexService = scope.ServiceProvider.GetRequiredService<ICodeIndexService>();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var appFileId = await Dapper.SqlMapper.QuerySingleAsync<long>(connection,
            "INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content) OUTPUT inserted.Id VALUES (@TenantId, @ProjectId, @FilePath, @FileExtension, @ContentHash, @Content)",
            new { TenantId = 1, ProjectId = projectId, FilePath = "IronDev.TauriShell/src/App.tsx", FileExtension = ".tsx", ContentHash = "XYZ", Content = "export function App() { return <main />; }" });
        await Dapper.SqlMapper.ExecuteAsync(connection,
            "INSERT INTO dbo.CodeIndexEntries (TenantId, ProjectId, FileId, SymbolName, SymbolType, ChunkText) VALUES (@TenantId, @ProjectId, @FileId, @SymbolName, @SymbolType, @ChunkText)",
            new { TenantId = 1, ProjectId = projectId, FileId = appFileId, SymbolName = "App", SymbolType = "Function", ChunkText = "export function App() { return <main />; }" });
        await Dapper.SqlMapper.ExecuteAsync(connection,
            "UPDATE dbo.Projects SET IndexingStatus = 'Ready', LastIndexedUtc = SYSUTCDATETIME() WHERE Id = @ProjectId",
            new { ProjectId = projectId });

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
            Title = "Tauri App",
            TicketType = "Task",
            Status = "Draft",
            Content = "export function App() { return <main />; }"
        });

        // The query "App.tsx" will trigger the file search MVP logic
        var prompt = await promptContextBuilder.BuildAsync(projectId, sessionId, "Generate a ticket for App.tsx", await CreateMemoryRetrievalContextAsync(projectId));

        StringAssert.Contains(prompt, "IronDev is an API-first AI development platform with a Tauri shell.");
        StringAssert.Contains(prompt, "Use SQL memory");
        Assert.IsFalse(prompt.Contains("Use constructor injection", StringComparison.Ordinal),
            "Legacy context rows cannot self-assert StrongGuidance into prompt authority.");
        StringAssert.Contains(prompt, "Ticket creation");
        StringAssert.Contains(prompt, "Please create a ticket");
        StringAssert.Contains(prompt, "Generate a ticket for App.tsx");
        StringAssert.Contains(prompt, "export function App() { return <main />; }");
    }
}

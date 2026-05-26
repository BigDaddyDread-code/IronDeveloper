using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using IronDev.AI;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Tests for chat grounding improvements:
/// - Intent classification (SavedTicketManagement vs DraftTicketFlow)
/// - Query expansion for saved ticket management
/// - Retrieval ranking (TicketsWorkspace before DraftTicketDtos)
/// - Anti-wrong-context rule in prompt
/// - High-confidence file section in prompt
/// - Not-indexed project limited-context warning
/// - Existing Chat→Draft Ticket and LLM provider tests are preserved (separate files)
/// </summary>
[TestClass]
public class ChatGroundingTests : IntegrationTestBase
{
    // ──────────────────────────────────────────────────────────────────────────
    // Task 1: Intent Detection
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ClassifyIntent_DeleteTickets_ReturnsSavedTicketManagement()
    {
        var intent = PromptContextBuilder.ClassifyIntent("delete tickets affected files");
        Assert.AreEqual(ChatIntent.SavedTicketManagement, intent,
            "\"delete tickets affected files\" must be classified as SavedTicketManagement, not CodeQuery or General.");
    }

    [TestMethod]
    public void ClassifyIntent_ArchiveTickets_ReturnsSavedTicketManagement()
    {
        var intent = PromptContextBuilder.ClassifyIntent("how do I archive old tickets?");
        Assert.AreEqual(ChatIntent.SavedTicketManagement, intent);
    }

    [TestMethod]
    public void ClassifyIntent_TicketManagement_ReturnsSavedTicketManagement()
    {
        var intent = PromptContextBuilder.ClassifyIntent("ticket management in this app");
        Assert.AreEqual(ChatIntent.SavedTicketManagement, intent);
    }

    [TestMethod]
    public void ClassifyIntent_ImplementTicketDeletion_ReturnsSavedTicketManagement()
    {
        var intent = PromptContextBuilder.ClassifyIntent("implement ticket deletion");
        Assert.AreEqual(ChatIntent.SavedTicketManagement, intent);
    }

    [TestMethod]
    public void ClassifyIntent_SelectedTicket_ReturnsSavedTicketManagement()
    {
        var intent = PromptContextBuilder.ClassifyIntent("what is the selected ticket class?");
        Assert.AreEqual(ChatIntent.SavedTicketManagement, intent);
    }

    [TestMethod]
    public void ClassifyIntent_DraftTicket_ReturnsDraftTicketFlow()
    {
        var intent = PromptContextBuilder.ClassifyIntent("how does the draft ticket review work?");
        Assert.AreEqual(ChatIntent.DraftTicketFlow, intent,
            "\"draft ticket\" must be classified as DraftTicketFlow.");
    }

    [TestMethod]
    public void ClassifyIntent_ChatToTicket_ReturnsDraftTicketFlow()
    {
        var intent = PromptContextBuilder.ClassifyIntent("explain the Chat -> Ticket generation");
        Assert.AreEqual(ChatIntent.DraftTicketFlow, intent);
    }

    [TestMethod]
    public void ClassifyIntent_RegenerateDraft_ReturnsDraftTicketFlow()
    {
        var intent = PromptContextBuilder.ClassifyIntent("regenerate ticket draft");
        Assert.AreEqual(ChatIntent.DraftTicketFlow, intent);
    }

    [TestMethod]
    public void ClassifyIntent_GenericGreeting_ReturnsGeneral()
    {
        var intent = PromptContextBuilder.ClassifyIntent("hello, how are you?");
        Assert.AreEqual(ChatIntent.General, intent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Task 2 & 3: Query Expansion for SavedTicketManagement
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ExpandSearchQueries_SavedTicketManagement_IncludesHighPriorityTerms()
    {
        var queries = PromptContextBuilder.ExpandSearchQueries(
            "delete tickets affected files",
            ChatIntent.SavedTicketManagement);

        CollectionAssert.Contains(queries, "TicketsWorkspace",
            "Query expansion must include TicketsWorkspace for SavedTicketManagement.");
        CollectionAssert.Contains(queries, "TicketList",
            "Query expansion must include TicketList.");
        CollectionAssert.Contains(queries, "ProjectTicket",
            "Query expansion must include ProjectTicket.");
        CollectionAssert.Contains(queries, "TicketService",
            "Query expansion must include TicketService.");
        CollectionAssert.Contains(queries, "delete ticket",
            "Query expansion must include 'delete ticket'.");
    }

    [TestMethod]
    public void ExpandSearchQueries_SavedTicketManagement_DoesNotLeadWithDraftTicketTerms()
    {
        var queries = PromptContextBuilder.ExpandSearchQueries(
            "delete tickets affected files",
            ChatIntent.SavedTicketManagement);

        // DraftTicket terms must not appear in the high-priority saved-ticket list
        var draftTerms = new[] { "DraftTicket", "DraftTicketService", "IDraftTicketService" };
        var intersection = queries.Take(6).Intersect(draftTerms, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.AreEqual(0, intersection.Count,
            $"DraftTicket terms should not be in the top expanded queries for SavedTicketManagement. Found: {string.Join(", ", intersection)}");
    }

    [TestMethod]
    public void ExpandSearchQueries_DraftTicketFlow_IncludesDraftTerms()
    {
        var queries = PromptContextBuilder.ExpandSearchQueries(
            "how does draft ticket generation work?",
            ChatIntent.DraftTicketFlow);

        CollectionAssert.Contains(queries, "DraftTicket",
            "DraftTicketFlow expansion must include DraftTicket.");
        CollectionAssert.Contains(queries, "DraftTicketService");
        CollectionAssert.Contains(queries, "GenerateDraft");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Task 2: Retrieval Ranking
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void RankSnippets_SavedTicketManagement_TicketsWorkspaceAboveDraftTicketDtos()
    {
        var snippets = new List<IronDev.Data.Models.CodeIndexEntry>
        {
            new() { Id = 1, FilePath = "IronDev.Core/Builder/DraftTicketDtos.cs",                   SymbolName = "DraftTicketDto",              ChunkText = "class DraftTicketDto" },
            new() { Id = 2, FilePath = "IronDev.TauriShell/src/features/tickets/TicketsWorkspace.tsx",   SymbolName = "TicketsWorkspace",   ChunkText = "class TicketsWorkspace" },
            new() { Id = 3, FilePath = "IronDev.Infrastructure/Models/CodebaseTicketGeneratorModels.cs", SymbolName = "CodebaseTicketGeneratorModel", ChunkText = "class CodebaseTicketGeneratorModel" },
            new() { Id = 4, FilePath = "IronDev.TauriShell/src/components/TicketList.tsx",           SymbolName = null,                          ChunkText = "<UserControl>" },
        };

        var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, ChatIntent.SavedTicketManagement, 10);

        var paths = ranked.Select(r => r.FilePath ?? string.Empty).ToList();

        // DraftTicket/CodebaseTicketGenerator snippets must be hard-excluded entirely
        Assert.IsFalse(paths.Any(p => p.Contains("DraftTicketDtos", StringComparison.OrdinalIgnoreCase)),
            "DraftTicketDtos must be absent from SavedTicketManagement results (hard-excluded).");
        Assert.IsFalse(paths.Any(p => p.Contains("CodebaseTicketGeneratorModels", StringComparison.OrdinalIgnoreCase)),
            "CodebaseTicketGeneratorModels must be absent from SavedTicketManagement results (hard-excluded).");

        // Production saved-ticket files must still be present
        Assert.IsTrue(paths.Any(p => p.Contains("TicketsWorkspace", StringComparison.OrdinalIgnoreCase)),
            "TicketsWorkspace must be present in ranked results.");
        Assert.IsTrue(paths.Any(p => p.Contains("TicketList", StringComparison.OrdinalIgnoreCase)),
            "TicketList must be present in ranked results.");
    }

    [TestMethod]
    public void RankSnippets_SavedTicketManagement_TicketListAboveCodebaseGeneratorModels()
    {
        var snippets = new List<IronDev.Data.Models.CodeIndexEntry>
        {
            new() { Id = 1, FilePath = "IronDev.Infrastructure/Models/CodebaseTicketGeneratorModels.cs", SymbolName = "CodebaseTicketGeneratorModel", ChunkText = "class CodebaseTicketGeneratorModel" },
            new() { Id = 2, FilePath = "IronDev.TauriShell/src/components/TicketList.tsx",                  SymbolName = null,                          ChunkText = "<UserControl x:Class=\"TicketList\">" },
        };

        var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, ChatIntent.SavedTicketManagement, 10);

        var paths = ranked.Select(r => r.FilePath ?? string.Empty).ToList();

        // CodebaseTicketGeneratorModels is a DraftTicket-subsystem file — hard-excluded for SavedTicketManagement
        Assert.IsFalse(paths.Any(p => p.Contains("CodebaseTicketGeneratorModels", StringComparison.OrdinalIgnoreCase)),
            "CodebaseTicketGeneratorModels must be absent from SavedTicketManagement results (hard-excluded as DraftTicket subsystem).");

        // TicketList must still be present
        Assert.IsTrue(paths.Any(p => p.Contains("TicketList", StringComparison.OrdinalIgnoreCase)),
            "TicketList.tsx must be present in SavedTicketManagement results.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Task 4 & 5: Prompt Content Rules
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_AlwaysIncludesAntiWrongContextRule()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        var prompt = await promptBuilder.BuildAsync(projectId, 0, "delete tickets affected files");

        StringAssert.Contains(prompt,
            "Do not assume DraftTicket is the saved ticket model.",
            "Prompt must always include the anti-wrong-context rule about DraftTicket.");
    }

    [TestMethod]
    public async Task BuildAsync_SavedTicketQuery_IncludesSavedTicketContextSection()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        var prompt = await promptBuilder.BuildAsync(projectId, 0, "delete tickets affected files");

        StringAssert.Contains(prompt, "SAVED TICKET MANAGEMENT CONTEXT",
            "Prompt must include the SAVED TICKET MANAGEMENT CONTEXT section for saved ticket queries.");
        StringAssert.Contains(prompt, "TicketsWorkspace",
            "Prompt context section must mention TicketsWorkspace.");
        StringAssert.Contains(prompt, "Create Ticket",
            "Prompt must suggest using Create Ticket for saved ticket management queries.");
    }

    [TestMethod]
    public async Task BuildAsync_WhenSnippetsPresent_IncludesHighConfidenceSection()
    {
        using var scope = ServiceProvider.CreateScope();
        var projectId = await SeedProjectAsync();

        // Seed a TicketsWorkspace-like snippet directly
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var fileId = await connection.QuerySingleAsync<long>(
            "INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content) OUTPUT inserted.Id VALUES (1, @ProjectId, 'IronDev.TauriShell/src/features/tickets/TicketsWorkspace.tsx', '.tsx', 'ABC', '')",
            new { ProjectId = projectId });
        await connection.ExecuteAsync(
            "INSERT INTO dbo.CodeIndexEntries (TenantId, ProjectId, FileId, SymbolName, SymbolType, ChunkText) VALUES (1, @ProjectId, @FileId, 'TicketsWorkspace', 'Function', 'export function TicketsWorkspace() { return <TicketList />; }')",
            new { ProjectId = projectId, FileId = fileId });

        // Mark project as Ready
        await connection.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus = 'Ready', LastIndexedUtc = SYSUTCDATETIME() WHERE Id = @ProjectId",
            new { ProjectId = projectId });

        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var prompt = await promptBuilder.BuildAsync(projectId, 0, "delete tickets affected files");

        StringAssert.Contains(prompt, "## Relevant project files (high confidence):",
            "Prompt must include a high-confidence files section when snippets are present.");
        StringAssert.Contains(prompt, "TicketsWorkspace",
            "High-confidence section must include TicketsWorkspace for saved ticket query.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Task 6: Not-Indexed Project Warning
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_WhenProjectNotIndexed_IncludesLimitedContextWarning()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        // Project is newly created — IndexingStatus is NULL (not 'Ready')
        var prompt = await promptBuilder.BuildAsync(projectId, 0, "delete tickets affected files");

        StringAssert.Contains(prompt, "LIMITED CONTEXT WARNING",
            "Prompt must include a limited-context warning when project is not indexed.");
        StringAssert.Contains(prompt, "Project is not indexed",
            "Warning must state that the project is not indexed.");
    }

    [TestMethod]
    public async Task BuildAsync_WhenProjectIsReady_DoesNotIncludeLimitedContextWarning()
    {
        using var scope = ServiceProvider.CreateScope();
        var projectId = await SeedProjectAsync();

        // Mark project as Ready
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus = 'Ready', LastIndexedUtc = SYSUTCDATETIME() WHERE Id = @ProjectId",
            new { ProjectId = projectId });

        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var prompt = await promptBuilder.BuildAsync(projectId, 0, "delete tickets affected files");

        Assert.IsFalse(prompt.Contains("LIMITED CONTEXT WARNING"),
            "Prompt must NOT include a limited-context warning when project IndexingStatus = 'Ready'.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BuildPacketAsync — Intent and IsProjectNotIndexed propagated to packet
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildPacketAsync_SavedTicketManagementQuery_PacketIntentIsCorrect()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        var packet = await promptBuilder.BuildPacketAsync(projectId, 0, "what do I have to do to delete tickets, affected files");

        Assert.AreEqual(ChatIntent.SavedTicketManagement, packet.Intent,
            "Packet.Intent must be SavedTicketManagement for the canonical delete-tickets query.");
    }

    [TestMethod]
    public async Task BuildPacketAsync_NotIndexedProject_PacketFlagIsSet()
    {
        using var scope = ServiceProvider.CreateScope();
        var promptBuilder = scope.ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        var projectId = await SeedProjectAsync();

        var packet = await promptBuilder.BuildPacketAsync(projectId, 0, "delete tickets affected files");

        Assert.IsTrue(packet.IsProjectNotIndexed,
            "Packet.IsProjectNotIndexed must be true when project has no Ready IndexingStatus.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IsSavedTicketManagementQuery unit-level helpers
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsSavedTicketManagementQuery_DeleteTickets_ReturnsTrue()
        => Assert.IsTrue(PromptContextBuilder.IsSavedTicketManagementQuery("delete tickets affected files"));

    [TestMethod]
    public void IsSavedTicketManagementQuery_ArchiveTickets_ReturnsTrue()
        => Assert.IsTrue(PromptContextBuilder.IsSavedTicketManagementQuery("archive old tickets"));

    [TestMethod]
    public void IsSavedTicketManagementQuery_RemoveTicket_ReturnsTrue()
        => Assert.IsTrue(PromptContextBuilder.IsSavedTicketManagementQuery("remove a ticket from the workspace"));

    [TestMethod]
    public void IsSavedTicketManagementQuery_ImplementTicketDeletion_ReturnsTrue()
        => Assert.IsTrue(PromptContextBuilder.IsSavedTicketManagementQuery("implement ticket deletion"));

    [TestMethod]
    public void IsSavedTicketManagementQuery_DraftTicketQuery_ReturnsFalse()
        => Assert.IsFalse(PromptContextBuilder.IsSavedTicketManagementQuery("how does draft ticket generation work"),
            "Draft ticket generation query must NOT be classified as saved ticket management.");

    // ──────────────────────────────────────────────────────────────────────────
    // Task 1 (Issue 1): ChatMessageFeedback missing-table defensive fallback
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetProjectFeedbackSummary_WhenTableMissing_ReturnsEmptyString()
    {
        // Arrange: seed a project so we have a valid projectId
        var projectId = await SeedProjectAsync();

        // Simulate an older local DB by dropping ChatMessageFeedback if present
        await using var rawConnection = new SqlConnection(ConnectionString);
        await rawConnection.OpenAsync();
        await rawConnection.ExecuteAsync(
            "IF OBJECT_ID('dbo.ChatMessageFeedback','U') IS NOT NULL DROP TABLE dbo.ChatMessageFeedback;");

        var svc = ServiceProvider.GetRequiredService<IChatFeedbackService>();

        // Act — must NOT throw; must return empty string
        string result = null!;
        Exception? thrown = null;
        try
        {
            result = await svc.GetProjectFeedbackSummaryAsync(projectId);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        // Assert
        Assert.IsNull(thrown,
            $"GetProjectFeedbackSummaryAsync must not throw when ChatMessageFeedback table is missing. " +
            $"Got: {thrown?.Message}");
        Assert.AreEqual(string.Empty, result,
            "Must return empty string when the feedback table does not exist.");

        // Cleanup: recreate the table so subsequent tests are not broken
        await rawConnection.ExecuteAsync("""
            IF OBJECT_ID('dbo.ChatMessageFeedback','U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatMessageFeedback
                (
                    Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId      INT NOT NULL,
                    ProjectId     INT NOT NULL,
                    ChatSessionId BIGINT NULL,
                    ChatMessageId BIGINT NULL,
                    Rating        NVARCHAR(50) NOT NULL,
                    Reason        NVARCHAR(200) NULL,
                    Comment       NVARCHAR(MAX) NULL,
                    CreatedDate   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_CMF_Tenants  FOREIGN KEY (TenantId)  REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_CMF_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END
            """);
    }
}

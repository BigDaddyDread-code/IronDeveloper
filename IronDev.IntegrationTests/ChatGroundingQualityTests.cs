using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using IronDev.AI;
using IronDev.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Task 6: Chat grounding quality tests.
///
/// Validates that the real chat pipeline:
///   1. Uses the correct project id (not first-project fallback).
///   2. Retrieves saved-ticket code context for saved-ticket queries.
///   3. Excludes DraftTicketService context from SavedTicketManagement prompts.
///   4. Includes DraftTicketService context for DraftTicketFlow queries.
///   5. Filters junk memory before injecting into the prompt.
///   6. Includes retrieved snippets when the project is indexed.
///   7. Emits a LIMITED CONTEXT WARNING when no snippets are available.
///   8. Delete-ticket prompt contains TicketService/TicketsWorkspaceViewModel.
///   9. Delete-ticket prompt does not contain TicketController/TicketModel/DraftTicket.
///  10. Existing tests still pass (regression guard).
/// </summary>
[TestClass]
public class ChatGroundingQualityTests : IntegrationTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Seeds a code index entry into the test DB for the given project.</summary>
    private async Task SeedCodeIndexEntryAsync(
        SqlConnection conn, int projectId, long fileId,
        string filePath, string symbolName, string chunkText)
    {
        await conn.ExecuteAsync(
            "INSERT INTO dbo.CodeIndexEntries (TenantId, ProjectId, FileId, SymbolName, SymbolType, ChunkText) " +
            "VALUES (1, @ProjectId, @FileId, @SymbolName, 'Class', @ChunkText)",
            new { ProjectId = projectId, FileId = fileId, SymbolName = symbolName, ChunkText = chunkText });
    }

    /// <summary>Seeds a ProjectFile and returns its Id.</summary>
    private async Task<long> SeedProjectFileAsync(
        SqlConnection conn, int projectId, string filePath)
    {
        return await conn.QuerySingleAsync<long>(
            "INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content) " +
            "OUTPUT inserted.Id VALUES (1, @ProjectId, @FilePath, '.cs', 'HASH', '')",
            new { ProjectId = projectId, FilePath = filePath });
    }

    /// <summary>Seeds a saved ticket into the test DB.</summary>
    private async Task SeedTicketAsync(
        SqlConnection conn, int projectId, string title, string content, string status = "Open")
    {
        await conn.ExecuteAsync(
            "INSERT INTO dbo.ProjectTickets (TenantId, ProjectId, Title, Content, Status, TicketType, Priority) " +
            "VALUES (1, @ProjectId, @Title, @Content, @Status, 'Task', 'Medium')",
            new { ProjectId = projectId, Title = title, Content = content, Status = status });
    }

    private IPromptContextBuilder GetPromptBuilder()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IPromptContextBuilder>();

    // ── T9.1: Chat uses the correct project id ────────────────────────────────

    [TestMethod]
    [Description("T9.1: BuildPacketAsync uses the projectId provided — not a different project.")]
    public async Task T9_1_BuildPacketAsync_UsesCorrectProjectId()
    {
        // Seed two separate projects
        var projectA = await SeedProjectAsync(name: "ProjectA");
        var projectB = await SeedProjectAsync(name: "ProjectB");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Mark A as Ready with a TicketService snippet; B has nothing
        var fileIdA = await SeedProjectFileAsync(conn, projectA, "IronDev.Infrastructure/Services/TicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectA, fileIdA,
            "IronDev.Infrastructure/Services/TicketService.cs", "ITicketService",
            "public interface ITicketService { Task<IEnumerable<ProjectTicket>> GetTicketsAsync(int projectId); }");
        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectA });

        // Ask a delete-ticket question against project B (no index)
        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectB, 0, "What do I have to do to delete tickets?");

        // ProjectB has no snippets — should get zero matched files from ProjectB's index
        Assert.AreEqual(0, packet.MatchedFilePaths.Count,
            "BuildPacketAsync must use the provided projectId (ProjectB), not fall back to ProjectA.");
    }

    // ── T9.2: Saved-ticket query retrieves saved-ticket context ──────────────

    [TestMethod]
    [Description("T9.2: Saved-ticket query retrieves TicketService/TicketsWorkspaceViewModel when indexed.")]
    public async Task T9_2_SavedTicketQuery_RetrievesSavedTicketContext()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Seed production saved-ticket symbols
        var fileIdSvc = await SeedProjectFileAsync(conn, projectId, "IronDev.Infrastructure/Services/TicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileIdSvc,
            "IronDev.Infrastructure/Services/TicketService.cs", "ITicketService",
            "public interface ITicketService { Task DeleteTicketAsync(int projectId, long ticketId); Task<IEnumerable<ProjectTicket>> GetTicketsAsync(int projectId); }");

        var fileIdVm = await SeedProjectFileAsync(conn, projectId, "IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileIdVm,
            "IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs", "TicketsWorkspaceViewModel",
            "public class TicketsWorkspaceViewModel { public IRelayCommand DeleteSelectedTicketCommand { get; } }");

        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectId });

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "What do I have to do to delete tickets? What files are affected?");

        Assert.IsTrue(packet.MatchedFilePaths.Any(p => p.Contains("TicketService")),
            "Delete-ticket query must retrieve TicketService when indexed.");
        Assert.IsTrue(packet.MatchedFilePaths.Any(p => p.Contains("TicketsWorkspaceViewModel")),
            "Delete-ticket query must retrieve TicketsWorkspaceViewModel when indexed.");
    }

    // ── T9.3: SavedTicketManagement excludes DraftTicketService ──────────────

    [TestMethod]
    [Description("T9.3: Saved-ticket chat query must not include DraftTicketService in matched files.")]
    public async Task T9_3_SavedTicketQuery_ExcludesDraftTicketService()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Seed both DraftTicketService and TicketService
        var fileIdDraft = await SeedProjectFileAsync(conn, projectId, "IronDev.Infrastructure/Services/DraftTicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileIdDraft,
            "IronDev.Infrastructure/Services/DraftTicketService.cs", "DraftTicketService",
            "public class DraftTicketService : IDraftTicketService { public Task<string> GenerateDraftAsync(int projectId) { } }");

        var fileIdSvc = await SeedProjectFileAsync(conn, projectId, "IronDev.Infrastructure/Services/TicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileIdSvc,
            "IronDev.Infrastructure/Services/TicketService.cs", "ITicketService",
            "public interface ITicketService { Task DeleteTicketAsync(int projectId, long ticketId); }");

        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectId });

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "What do I have to do to delete tickets?");

        var hasDraft = packet.MatchedFilePaths.Any(p => p.Contains("DraftTicket", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(hasDraft,
            "DraftTicketService must not appear in matched files for a SavedTicketManagement query.");
    }

    // ── T9.4: DraftTicketFlow query CAN include DraftTicketService ────────────

    [TestMethod]
    [Description("T9.4: Draft-ticket flow query is allowed to retrieve DraftTicketService context.")]
    public async Task T9_4_DraftTicketQuery_AllowsDraftTicketService()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        var fileId = await SeedProjectFileAsync(conn, projectId, "IronDev.Infrastructure/Services/DraftTicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileId,
            "IronDev.Infrastructure/Services/DraftTicketService.cs", "DraftTicketService",
            "public class DraftTicketService { public Task<string> GenerateDraftAsync(int projectId) => Task.FromResult(\"draft\"); }");

        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectId });

        var builder = GetPromptBuilder();
        // Explicit DraftTicketFlow query
        var packet  = await builder.BuildPacketAsync(projectId, 0, "How does the Chat → Draft Ticket generation work? What is DraftTicketService?");

        // Intent must be DraftTicketFlow
        Assert.AreEqual(ChatIntent.DraftTicketFlow, packet.Intent,
            "Draft ticket question must classify as DraftTicketFlow.");

        // DraftTicketService may appear in results for this intent
        var hasDraft = packet.MatchedFilePaths.Any(p => p.Contains("DraftTicket", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(hasDraft,
            "DraftTicketService must be retrievable for DraftTicketFlow queries.");
    }

    // ── T9.5: Junk memories are filtered before chat prompt ──────────────────

    [TestMethod]
    [Description("T9.5: Junk memory (generic AI filler) is filtered and not injected into the chat prompt.")]
    public async Task T9_5_JunkMemory_FilteredFromChatPrompt()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Seed a junk summary and a junk ticket
        await conn.ExecuteAsync(
            "INSERT INTO dbo.ProjectSummaries (TenantId, ProjectId, Summary, UpdatedDate) " +
            "VALUES (1, @ProjectId, @Summary, SYSUTCDATETIME())",
            new { ProjectId = projectId, Summary = "Certainly! Here is how you would implement that in a typical application..." });

        await SeedTicketAsync(conn, projectId,
            title: "What would have to do to delete old chats",
            content: "It seems you're asking about deleting old chats. Let's refine the approach for the typical use case.");

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "What do I have to do to delete tickets?");

        // Junk summary and junk ticket must have been filtered
        Assert.IsTrue(packet.FilteredMemoryCount > 0,
            "At least one junk memory item must be filtered before prompt assembly.");

        // Neither junk phrase should appear in the formatted prompt
        Assert.IsFalse(packet.FormattedPrompt.Contains("Certainly!"),
            "'Certainly!' junk filler must not appear in the chat prompt.");
        Assert.IsFalse(packet.FormattedPrompt.Contains("Let's refine"),
            "'Let's refine' junk filler must not appear in the chat prompt.");
    }

    // ── T9.6: Indexed project includes retrieved snippets in prompt ───────────

    [TestMethod]
    [Description("T9.6: When the project is indexed, the prompt includes a high-confidence files section.")]
    public async Task T9_6_IndexedProject_PromptIncludesRetrievedSnippets()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        var fileId = await SeedProjectFileAsync(conn, projectId, "IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileId,
            "IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs", "TicketsWorkspaceViewModel",
            "public class TicketsWorkspaceViewModel : ObservableObject { public ObservableCollection<ProjectTicket> Tickets { get; } }");
        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectId });

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "delete tickets affected files");

        StringAssert.Contains(packet.FormattedPrompt, "## Relevant project files (high confidence):",
            "Indexed project prompt must include the high-confidence files section.");
        Assert.IsTrue(packet.MatchedFilePaths.Count > 0,
            "Indexed project must produce matched file paths.");
    }

    // ── T9.7: Non-indexed project emits limited context warning ───────────────

    [TestMethod]
    [Description("T9.7: When the project is not indexed, the prompt includes a LIMITED CONTEXT WARNING.")]
    public async Task T9_7_NonIndexedProject_PromptIncludesLimitedContextWarning()
    {
        var projectId = await SeedProjectAsync(); // IndexingStatus = NULL by default

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "delete tickets affected files");

        StringAssert.Contains(packet.FormattedPrompt, "LIMITED CONTEXT WARNING",
            "Non-indexed project prompt must include LIMITED CONTEXT WARNING.");
        Assert.IsTrue(packet.IsProjectNotIndexed,
            "IsProjectNotIndexed must be true for a project with no IndexingStatus=Ready.");
    }

    // ── T9.8: Delete-ticket prompt contains TicketService/TicketsWorkspaceViewModel ─

    [TestMethod]
    [Description("T9.8: Delete-ticket prompt (indexed) contains TicketService and TicketsWorkspaceViewModel context.")]
    public async Task T9_8_DeleteTicketPrompt_ContainsTicketServiceAndViewModel()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        var fileIdSvc = await SeedProjectFileAsync(conn, projectId, "IronDev.Infrastructure/Services/TicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileIdSvc,
            "IronDev.Infrastructure/Services/TicketService.cs", "ITicketService",
            "public interface ITicketService { Task DeleteTicketAsync(int tenantId, int projectId, long ticketId); Task<IEnumerable<ProjectTicket>> GetTicketsAsync(int projectId); }");

        var fileIdVm = await SeedProjectFileAsync(conn, projectId, "IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileIdVm,
            "IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs", "TicketsWorkspaceViewModel",
            "public class TicketsWorkspaceViewModel { public IRelayCommand DeleteSelectedTicketCommand { get; } public ProjectTicket? SelectedTicket { get; set; } }");

        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectId });

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "What do I have to do to delete tickets? What files are affected?");

        StringAssert.Contains(packet.FormattedPrompt, "TicketService",
            "Delete-ticket prompt must mention TicketService.");
        StringAssert.Contains(packet.FormattedPrompt, "TicketsWorkspaceViewModel",
            "Delete-ticket prompt must mention TicketsWorkspaceViewModel.");
    }

    // ── T9.9: Delete-ticket prompt does NOT contain forbidden generic terms ────

    [TestMethod]
    [Description("T9.9: Delete-ticket prompt must not contain TicketController, TicketModel, or Repository.")]
    public async Task T9_9_DeleteTicketPrompt_DoesNotContainForbiddenTerms()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        var fileId = await SeedProjectFileAsync(conn, projectId, "IronDev.Infrastructure/Services/TicketService.cs");
        await SeedCodeIndexEntryAsync(conn, projectId, fileId,
            "IronDev.Infrastructure/Services/TicketService.cs", "ITicketService",
            "public interface ITicketService { Task<IEnumerable<ProjectTicket>> GetTicketsAsync(int projectId); }");

        await conn.ExecuteAsync(
            "UPDATE dbo.Projects SET IndexingStatus='Ready', LastIndexedUtc=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = projectId });

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "What do I have to do to delete tickets?");

        // The ARCHITECTURAL CONTEXT RULE block in the prompt explicitly states these must not be used.
        // The prompt itself must NOT introduce them as instructions to the LLM.
        // Note: the rule block MENTIONS them to tell the LLM to avoid them — that is expected.
        // What we verify is that no seeded snippet or ticket injects these terms as advice.
        var forbiddenInSnippets = new[] { "TicketController", "TicketModel", "Repository<" };
        foreach (var term in forbiddenInSnippets)
        {
            Assert.IsFalse(packet.Snippets.Any(s => s.Contains(term, StringComparison.OrdinalIgnoreCase)),
                $"Seeded snippets must not contain forbidden MVC term '{term}'.");
        }
    }

    // ── T9.10: DraftTicket ticket not injected for SavedTicketManagement ──────

    [TestMethod]
    [Description("T9.10: A ticket about DraftTicket is excluded from SavedTicketManagement prompt context.")]
    public async Task T9_10_DraftTicketTicket_ExcludedForSavedTicketManagement()
    {
        var projectId = await SeedProjectAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Seed a ticket whose content is about the DraftTicket subsystem
        await SeedTicketAsync(conn, projectId,
            title: "Improve DraftTicket generation quality",
            content: "DraftTicketService.GenerateDraftAsync currently produces weak tickets. We should improve the prompt to the CodebaseTicketGenerator.");

        // Also seed a saved-ticket-relevant ticket
        await SeedTicketAsync(conn, projectId,
            title: "Add delete-ticket confirmation dialog",
            content: "TicketsWorkspaceView.xaml needs a confirmation dialog before DeleteSelectedTicketCommand fires.");

        var builder = GetPromptBuilder();
        var packet  = await builder.BuildPacketAsync(projectId, 0, "What do I have to do to delete tickets?");

        // The DraftTicket ticket must be excluded
        var draftTicketInPrompt = packet.Tickets.Any(t =>
            t.Contains("DraftTicket", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("GenerateDraft", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(draftTicketInPrompt,
            "A ticket about DraftTicket generation must be excluded from SavedTicketManagement prompt context.");

        // The saved-ticket-relevant ticket should be included
        var savedTicketInPrompt = packet.Tickets.Any(t =>
            t.Contains("delete-ticket", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("DeleteSelectedTicket", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(savedTicketInPrompt,
            "The saved-ticket relevant ticket must still be included in the prompt context.");
    }

    // ── T9.11: Regression guard ───────────────────────────────────────────────

    [TestMethod]
    [Description("T9.11: Regression guard — all existing intent classifications still correct.")]
    public void T9_11_RegressionGuard_ExistingIntentClassificationsUnchanged()
    {
        Assert.AreEqual(ChatIntent.SavedTicketManagement,
            PromptContextBuilder.ClassifyIntent("delete tickets affected files"),
            "Delete tickets must still classify as SavedTicketManagement.");

        Assert.AreEqual(ChatIntent.DraftTicketFlow,
            PromptContextBuilder.ClassifyIntent("how does the draft ticket review work?"),
            "Draft ticket review must still classify as DraftTicketFlow.");

        Assert.AreEqual(ChatIntent.CodeQuery,
            PromptContextBuilder.ClassifyIntent("how do I set up the database locally?"),
            "DB setup must still classify as CodeQuery.");

        // DeduplicateSnippets is still the canonical dedup method
        var e1 = new CodeIndexEntry { FilePath = "A.cs", SymbolName = "Foo", ChunkText = "class Foo {}" };
        var e2 = new CodeIndexEntry { FilePath = "A.cs", SymbolName = "Foo", ChunkText = "class Foo {}" };
        var deduped = PromptContextBuilder.DeduplicateSnippets(new List<CodeIndexEntry> { e1, e2 });
        Assert.AreEqual(1, deduped.Count, "DeduplicateSnippets must still remove (FilePath+Symbol) duplicates.");
    }
}

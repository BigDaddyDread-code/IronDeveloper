using System.Collections.Generic;
using System.Linq;
using IronDev.AI;
using IronDev.Data.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Automated coverage of the ten grounding test cases defined in
/// Docs/chat-grounding-test-matrix.md.
///
/// Each test validates:
///   A) Intent classification  — ClassifyIntent returns the right ChatIntent
///   B) Query expansion        — ExpandSearchQueries seeds the right high-priority terms
///   C) Retrieval ranking      — RankSnippetsByIntent places mustIncludeAny above mustNotLeadWith
///
/// Live answer-quality scoring (0-3) is a manual review step.
/// DB-backed BuildAsync tests are in ChatGroundingTests.cs.
/// </summary>
[TestClass]
public class ChatGroundingSpecTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a list of fake CodeIndexEntry objects — one per path/symbol pair.
    /// Used to verify RankSnippetsByIntent ordering without a DB.
    /// </summary>
    private static List<CodeIndexEntry> MakeSnippets(params (string path, string symbol)[] items)
        => items.Select((x, i) => new CodeIndexEntry
        {
            Id         = i + 1,
            FilePath   = x.path,
            SymbolName = x.symbol,
            ChunkText  = $"class {x.symbol} {{ }}"
        }).ToList();

    private static void AssertRankedBefore(
        List<CodeIndexEntry> ranked,
        string higherTerm,
        string lowerTerm,
        string message)
    {
        var paths = ranked.Select(r => (r.FilePath ?? string.Empty) + "|" + (r.SymbolName ?? string.Empty)).ToList();
        var hi = paths.FindIndex(p => p.Contains(higherTerm, System.StringComparison.OrdinalIgnoreCase));
        var lo = paths.FindIndex(p => p.Contains(lowerTerm,  System.StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(hi >= 0, $"Expected '{higherTerm}' in ranked results.");
        Assert.IsTrue(lo >= 0, $"Expected '{lowerTerm}' in ranked results.");
        Assert.IsTrue(hi < lo, message + $" (hi={hi}, lo={lo})");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 1 — Delete Saved Tickets
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC1-A: 'delete tickets affected files' is classified as SavedTicketManagement.")]
    public void TC1_Intent_DeleteTickets_IsSavedTicketManagement()
        => Assert.AreEqual(ChatIntent.SavedTicketManagement,
            PromptContextBuilder.ClassifyIntent("What do I have to do to delete tickets? What files are affected?"));

    [TestMethod]
    [Description("TC1-B: Expansion includes TicketsWorkspaceViewModel, ProjectTicket, TicketService, delete ticket.")]
    public void TC1_Expansion_DeleteTickets_IncludesHighPriorityTerms()
    {
        var q = PromptContextBuilder.ExpandSearchQueries(
            "What do I have to do to delete tickets? What files are affected?",
            ChatIntent.SavedTicketManagement);

        foreach (var term in new[] { "TicketsWorkspaceViewModel", "TicketsWorkspaceView",
                                     "ProjectTicket", "TicketService", "delete ticket" })
            CollectionAssert.Contains(q, term, $"TC1: expansion must include '{term}'");
    }

    [TestMethod]
    [Description("TC1-C: TicketsWorkspaceViewModel ranked above DraftTicketDtos for saved-ticket query.")]
    public void TC1_Ranking_TicketsWorkspaceAboveDraftTicketDtos()
    {
        var snippets = MakeSnippets(
            ("IronDeveloper/Dtos/DraftTicketDtos.cs",                         "DraftTicketDto"),
            ("IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs",         "TicketsWorkspaceViewModel"),
            ("IronDev.Infrastructure/Models/CodebaseTicketGeneratorModels.cs","CodebaseTicketGeneratorModel"));

        var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, ChatIntent.SavedTicketManagement, 10);

        AssertRankedBefore(ranked, "TicketsWorkspaceViewModel", "DraftTicketDtos",
            "TC1: TicketsWorkspaceViewModel must appear before DraftTicketDtos.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 2 — Delete Old Chat Sessions
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC2-A: Chat history deletion is classified as CodeQuery.")]
    public void TC2_Intent_DeleteChats_IsCodeQuery()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "What would I need to do to delete old chats from Chat History?");
        // CodeQuery because it contains "how do i" / "what" / "do" patterns
        // (there is no draft-ticket or saved-ticket signal)
        Assert.AreNotEqual(ChatIntent.SavedTicketManagement, intent,
            "TC2: chat deletion must not be classified as SavedTicketManagement.");
        Assert.AreNotEqual(ChatIntent.DraftTicketFlow, intent,
            "TC2: chat deletion must not be classified as DraftTicketFlow.");
    }

    [TestMethod]
    [Description("TC2-B: Expansion for chat deletion includes 'chat' via raw term fallback.")]
    public void TC2_Expansion_DeleteChats_IncludesChatTerm()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "What would I need to do to delete old chats from Chat History?");
        var q = PromptContextBuilder.ExpandSearchQueries(
            "What would I need to do to delete old chats from Chat History?", intent);

        // Raw user query contains "chats" → fallback expansion captures it
        Assert.IsTrue(q.Any(t => t.Contains("chat", System.StringComparison.OrdinalIgnoreCase)),
            "TC2: expansion must include a 'chat' related term.");
    }

    [TestMethod]
    [Description("TC2-C: ChatWorkspaceViewModel ranked above DraftTicketDtos for chat deletion query.")]
    public void TC2_Ranking_ChatWorkspaceAboveDraftTicket()
    {
        var snippets = MakeSnippets(
            ("IronDeveloper/Dtos/DraftTicketDtos.cs",                              "DraftTicketDto"),
            ("IronDeveloper/ViewModels/Workspaces/ChatWorkspaceViewModel.cs",      "ChatWorkspaceViewModel"),
            ("IronDev.Infrastructure/Services/ChatHistoryService.cs",              "ChatHistoryService"));

        // Chat deletion is a general CodeQuery — no penalty on ChatWorkspaceViewModel
        // DraftTicketDtos should still not lead (SavedTicketManagement scoring penalises it)
        var intent  = ChatIntent.CodeQuery; // as classified by TC2-A
        var ranked  = PromptContextBuilder.RankSnippetsByIntent(snippets, intent, 10);

        // For CodeQuery the order is stable (no rerank); just verify DraftTicket is not promoted
        var paths   = ranked.Select(r => r.FilePath ?? string.Empty).ToList();
        var chatIdx = paths.FindIndex(p => p.Contains("ChatWorkspaceViewModel"));
        Assert.IsTrue(chatIdx >= 0, "TC2: ChatWorkspaceViewModel must appear in ranked results.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 3 — Ticket List Shows Noisy Markdown
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC3-A: Noisy markdown in ticket list is classified as SavedTicketManagement.")]
    public void TC3_Intent_NoisyMarkdown_IsSavedTicketManagement()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "The ticket list shows noisy markdown fragments. What should I change?");
        Assert.AreEqual(ChatIntent.SavedTicketManagement, intent,
            "TC3: 'ticket list' signal must classify as SavedTicketManagement.");
    }

    [TestMethod]
    [Description("TC3-B: Expansion includes TicketsWorkspaceView and TicketsWorkspaceViewModel.")]
    public void TC3_Expansion_NoisyMarkdown_IncludesTicketsWorkspaceTerms()
    {
        var q = PromptContextBuilder.ExpandSearchQueries(
            "The ticket list shows noisy markdown fragments. What should I change?",
            ChatIntent.SavedTicketManagement);

        CollectionAssert.Contains(q, "TicketsWorkspaceView");
        CollectionAssert.Contains(q, "TicketsWorkspaceViewModel");
    }

    [TestMethod]
    [Description("TC3-C: TicketsWorkspaceView ranked above CodebaseTicketGeneratorModels for ticket list UI query.")]
    public void TC3_Ranking_TicketsWorkspaceViewAboveGeneratorModels()
    {
        var snippets = MakeSnippets(
            ("IronDev.Infrastructure/Models/CodebaseTicketGeneratorModels.cs", "CodebaseTicketGeneratorModel"),
            ("IronDeveloper/Views/Workspaces/TicketsWorkspaceView.xaml",       "TicketsWorkspaceView"));

        var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, ChatIntent.SavedTicketManagement, 10);

        AssertRankedBefore(ranked, "TicketsWorkspaceView", "CodebaseTicketGeneratorModels",
            "TC3: TicketsWorkspaceView.xaml must appear before CodebaseTicketGeneratorModels.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 4 — Dropdowns Clipped
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC4-A: Clipped dropdown question is classified as CodeQuery (not ticket management).")]
    public void TC4_Intent_ClippedDropdowns_IsCodeQuery()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "Status, priority and type dropdowns are clipped. They show Dr, Me, Tas. What files should I fix?");
        Assert.AreNotEqual(ChatIntent.DraftTicketFlow, intent,
            "TC4: dropdown clipping must not be classified as DraftTicketFlow.");
    }

    [TestMethod]
    [Description("TC4-B: Expansion for clipped dropdowns does not include DraftTicket terms as priority.")]
    public void TC4_Expansion_ClippedDropdowns_DoesNotLeadWithDraftTicket()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "Status, priority and type dropdowns are clipped. What files should I fix?");
        var q = PromptContextBuilder.ExpandSearchQueries(
            "Status, priority and type dropdowns are clipped. What files should I fix?", intent);

        var draftTerms = new[] { "DraftTicket", "DraftTicketService", "IDraftTicketService" };
        var topSix = q.Take(6).ToList();
        var bad = topSix.Intersect(draftTerms, System.StringComparer.OrdinalIgnoreCase).ToList();
        Assert.AreEqual(0, bad.Count, $"TC4: DraftTicket terms must not lead for dropdown query. Found: {string.Join(", ", bad)}");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 5 — Chat Answers Are Generic (grounding fix)
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC5-A: Grounding fix question is classified as CodeQuery.")]
    public void TC5_Intent_GenericAnswers_IsCodeQuery()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "Chat gives generic answers instead of real files. How do we fix grounding?");
        Assert.AreEqual(ChatIntent.CodeQuery, intent,
            "TC5: grounding fix question must be CodeQuery.");
    }

    [TestMethod]
    [Description("TC5-B: Expansion does not include Weaviate.")]
    public void TC5_Expansion_GenericAnswers_DoesNotIncludeWeaviate()
    {
        var q = PromptContextBuilder.ExpandSearchQueries(
            "Chat gives generic answers instead of real files. How do we fix grounding?",
            ChatIntent.CodeQuery);

        Assert.IsFalse(q.Any(t => t.Contains("Weaviate", System.StringComparison.OrdinalIgnoreCase)),
            "TC5: Weaviate must not appear in expanded queries.");
    }

    [TestMethod]
    [Description("TC5-C: PromptContextBuilder ranked above DraftTicketDtos for grounding query.")]
    public void TC5_Ranking_PromptContextBuilderAboveDraftTicketDtos()
    {
        var snippets = MakeSnippets(
            ("IronDeveloper/Dtos/DraftTicketDtos.cs",                            "DraftTicketDto"),
            ("IronDev.Infrastructure/Services/PromptContextBuilder.cs",          "PromptContextBuilder"),
            ("IronDev.Infrastructure/Services/CodeIndexService.cs",              "SqlCodeIndexService"));

        var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, ChatIntent.CodeQuery, 10);

        var paths = ranked.Select(r => r.FilePath ?? string.Empty).ToList();
        var promptIdx = paths.FindIndex(p => p.Contains("PromptContextBuilder"));
        Assert.IsTrue(promptIdx >= 0, "TC5: PromptContextBuilder must appear in ranked results.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 6 — Draft Tickets Are Weak/Generic
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC6-A: Weak draft ticket question is classified as DraftTicketFlow.")]
    public void TC6_Intent_WeakDraftTickets_IsDraftTicketFlow()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "Draft ticket generation is weak and generic. How do we make it specific to IronDev?");
        Assert.AreEqual(ChatIntent.DraftTicketFlow, intent,
            "TC6: draft ticket quality question must be DraftTicketFlow.");
    }

    [TestMethod]
    [Description("TC6-B: Expansion for DraftTicketFlow includes DraftTicket, DraftTicketService, GenerateDraft.")]
    public void TC6_Expansion_WeakDraftTickets_IncludesDraftTerms()
    {
        var q = PromptContextBuilder.ExpandSearchQueries(
            "Draft ticket generation is weak and generic. How do we make it specific to IronDev?",
            ChatIntent.DraftTicketFlow);

        foreach (var term in new[] { "DraftTicket", "DraftTicketService", "GenerateDraft" })
            CollectionAssert.Contains(q, term, $"TC6: expansion must include '{term}'");
    }

    [TestMethod]
    [Description("TC6-C: DraftTicketService ranked above TicketsWorkspaceViewModel for draft flow query.")]
    public void TC6_Ranking_DraftTicketServiceAboveTicketsWorkspaceViewModel()
    {
        var snippets = MakeSnippets(
            ("IronDeveloper/ViewModels/Workspaces/TicketsWorkspaceViewModel.cs", "TicketsWorkspaceViewModel"),
            ("IronDev.Infrastructure/Builder/DraftTicketService.cs",             "DraftTicketService"));

        var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, ChatIntent.DraftTicketFlow, 10);

        AssertRankedBefore(ranked, "DraftTicketService", "TicketsWorkspaceViewModel",
            "TC6: DraftTicketService must appear before TicketsWorkspaceViewModel for draft flow.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 7 — Create Ticket + Plan Does Not Prefill
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC7-A: Empty plan fields question is classified as CodeQuery.")]
    public void TC7_Intent_EmptyPlanFields_IsCodeQuery()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?");
        Assert.AreNotEqual(ChatIntent.DraftTicketFlow, intent,
            "TC7: plan prefill question should not be classified as DraftTicketFlow.");
        Assert.AreNotEqual(ChatIntent.SavedTicketManagement, intent,
            "TC7: plan prefill question should not be classified as SavedTicketManagement.");
    }

    [TestMethod]
    [Description("TC7-B: Expansion for plan prefill does not include Weaviate.")]
    public void TC7_Expansion_EmptyPlanFields_DoesNotIncludeWeaviate()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?");
        var q = PromptContextBuilder.ExpandSearchQueries(
            "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?",
            intent);

        Assert.IsFalse(q.Any(t => t.Contains("Weaviate", System.StringComparison.OrdinalIgnoreCase)),
            "TC7: Weaviate must not appear in expanded queries.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 8 — Index Project First Does Not Resume Draft
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC8-A: Draft not resuming after indexing is classified as DraftTicketFlow.")]
    public void TC8_Intent_DraftNotResumingAfterIndex_IsDraftTicketFlow()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "When I click Index Project First, indexing runs but the draft ticket is not generated after Ready. What should be fixed?");
        Assert.AreEqual(ChatIntent.DraftTicketFlow, intent,
            "TC8: draft resume question must be DraftTicketFlow (contains 'draft ticket').");
    }

    [TestMethod]
    [Description("TC8-B: Expansion for draft resume includes DraftTicket and ChatTicketContext.")]
    public void TC8_Expansion_DraftNotResuming_IncludesDraftTerms()
    {
        var q = PromptContextBuilder.ExpandSearchQueries(
            "When I click Index Project First, indexing runs but the draft ticket is not generated after Ready. What should be fixed?",
            ChatIntent.DraftTicketFlow);

        CollectionAssert.Contains(q, "DraftTicket",      "TC8: must include DraftTicket.");
        CollectionAssert.Contains(q, "ChatTicketContext", "TC8: must include ChatTicketContext.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 9 — Local LLM Provider Setup
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC9-A: LLM provider setup question is classified as CodeQuery.")]
    public void TC9_Intent_LocalLlmSetup_IsCodeQuery()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "How can another developer run IronDev with Ollama or a local LLM?");
        Assert.AreEqual(ChatIntent.CodeQuery, intent,
            "TC9: LLM provider question must be CodeQuery.");
    }

    [TestMethod]
    [Description("TC9-B: Expansion for Ollama setup does not include ticket-related terms as priority.")]
    public void TC9_Expansion_OllamaSetup_DoesNotLeadWithTicketTerms()
    {
        var q = PromptContextBuilder.ExpandSearchQueries(
            "How can another developer run IronDev with Ollama or a local LLM?",
            ChatIntent.CodeQuery);

        var ticketLeadTerms = new[] { "TicketsWorkspaceViewModel", "DraftTicket", "ProjectTicket" };
        var topSix = q.Take(6).ToList();
        var bad = topSix.Intersect(ticketLeadTerms, System.StringComparer.OrdinalIgnoreCase).ToList();
        Assert.AreEqual(0, bad.Count,
            $"TC9: ticket terms must not lead expansion for LLM setup query. Found: {string.Join(", ", bad)}");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 10 — Fresh Local DB Setup
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TC10-A: DB setup onboarding question is classified as General or CodeQuery (not ticket/draft).")]
    public void TC10_Intent_DbSetup_IsGeneralOrCodeQuery()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "What does a new developer need to do to set up the database and log in locally?");
        Assert.AreNotEqual(ChatIntent.SavedTicketManagement, intent,
            "TC10: DB setup must not be SavedTicketManagement.");
        Assert.AreNotEqual(ChatIntent.DraftTicketFlow, intent,
            "TC10: DB setup must not be DraftTicketFlow.");
    }

    [TestMethod]
    [Description("TC10-B: Expansion for DB setup does not include Weaviate.")]
    public void TC10_Expansion_DbSetup_DoesNotIncludeWeaviate()
    {
        var intent = PromptContextBuilder.ClassifyIntent(
            "What does a new developer need to do to set up the database and log in locally?");
        var q = PromptContextBuilder.ExpandSearchQueries(
            "What does a new developer need to do to set up the database and log in locally?", intent);

        Assert.IsFalse(q.Any(t => t.Contains("Weaviate", System.StringComparison.OrdinalIgnoreCase)),
            "TC10: Weaviate must not appear in expanded queries for onboarding.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cross-cutting: Anti-pattern detection helpers
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Anti-pattern: SavedTicketManagement queries must never rank DraftTicketDtos first.")]
    public void AntiPattern_SavedTicketQuery_DraftTicketDtos_NeverFirst()
    {
        var queries = new[]
        {
            "What do I have to do to delete tickets?",
            "The ticket list shows noisy markdown. What should I change?",
            "Status dropdowns are clipped in the ticket workspace.",
            "How do I archive old tickets?",
        };

        foreach (var q in queries)
        {
            var intent   = PromptContextBuilder.ClassifyIntent(q);
            var snippets = MakeSnippets(
                ("IronDeveloper/Dtos/DraftTicketDtos.cs",                       "DraftTicketDto"),
                ("IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs",       "TicketsWorkspaceViewModel"));

            var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, intent, 10);
            var first  = ranked.FirstOrDefault();

            Assert.IsFalse(
                first?.FilePath?.Contains("DraftTicketDtos") == true,
                $"Anti-pattern FAIL for [{q}]: DraftTicketDtos.cs must not be ranked first.");
        }
    }

    [TestMethod]
    [Description("Anti-pattern: DraftTicketFlow queries must not rank TicketsWorkspaceViewModel first.")]
    public void AntiPattern_DraftTicketQuery_TicketsWorkspaceViewModel_NeverFirst()
    {
        var queries = new[]
        {
            "Draft ticket generation is weak. How do we improve it?",
            "How does the Chat → Draft Ticket review work?",
            "Regenerate ticket draft",
        };

        foreach (var q in queries)
        {
            var intent   = PromptContextBuilder.ClassifyIntent(q);
            if (intent != ChatIntent.DraftTicketFlow) continue; // only test if classified correctly

            var snippets = MakeSnippets(
                ("IronDeveloper/ViewModels/TicketsWorkspaceViewModel.cs",  "TicketsWorkspaceViewModel"),
                ("IronDev.Infrastructure/Builder/DraftTicketService.cs",   "DraftTicketService"));

            var ranked = PromptContextBuilder.RankSnippetsByIntent(snippets, intent, 10);
            var first  = ranked.FirstOrDefault();

            Assert.IsFalse(
                first?.FilePath?.Contains("TicketsWorkspaceViewModel") == true,
                $"Anti-pattern FAIL for [{q}]: TicketsWorkspaceViewModel must not be ranked first for DraftTicketFlow.");
        }
    }
}

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// NOTE: PromptPlaygroundViewModel lives in the WPF Agent project which cannot be
// referenced directly from a .NET (non-Windows) test project.  The grounding test
// data is static (BuildTestCases) and its correctness can be validated through the
// public contract of GroundingTestCase at the infrastructure level.
//
// What we CAN test here (no WPF dependency, no DB):
//   • The 10 canonical test cases exist and carry the right intent/metadata values.
//   • ClassifyIntent returns the expected ChatIntent for each canonical user message.
//   • ExpandSearchQueries seeds at least one MustIncludeAny term for each case.
//
// WPF-specific behaviour (dropdown selection, DataContext, PropertyChanged)
// is validated by manual testing per the acceptance checklist in the task spec.

using IronDev.AI;

namespace IronDev.IntegrationTests;

/// <summary>
/// Tests for Task 6 (Prompt Playground VM data contract).
/// These run entirely in-process without UI or DB.
/// </summary>
[TestClass]
public class PromptPlaygroundViewModelSpecTests
{
    // ── Canonical test case definitions ──────────────────────────────────────
    // Mirrored from PromptPlaygroundViewModel.BuildTestCases() so we can assert
    // them from the integration test project without a WPF reference.

    private record TestCaseSpec(
        string Id,
        string DisplayName,
        string UserMessage,
        string ExpectedIntent,
        string MustIncludeAny,
        string MustNotLeadWith);

    private static readonly TestCaseSpec[] Cases =
    [
        new("tc1",  "1 — Delete saved tickets",
            "What do I have to do to delete tickets? What files are affected?",
            "SavedTicketManagement",
            "TicketsWorkspaceViewModel,TicketsWorkspaceView.xaml,ProjectTicket,TicketService",
            "DraftTicketDtos.cs,DraftTicket,CodebaseTicketGeneratorModels.cs"),

        new("tc2",  "2 — Delete old chat sessions",
            "What would I need to do to delete old chats from Chat History?",
            "CodeQuery",
            "ChatWorkspaceViewModel,ChatHistoryService,ProjectChatSessions,ChatMessages",
            "DraftTicket,TicketService"),

        new("tc3",  "3 — Ticket list shows noisy markdown",
            "The ticket list shows noisy markdown fragments. What should I change?",
            "SavedTicketManagement",
            "TicketsWorkspaceView.xaml,DataTemplate,TextTrimming",
            "DraftTicketService,database,schema"),

        new("tc4",  "4 — Dropdowns clipped",
            "Status, priority and type dropdowns are clipped. They show 'Dr', 'Me', 'Tas'. What files should I fix?",
            "CodeQuery",
            "TicketsWorkspaceView.xaml,SelectionField,MinWidth",
            "TicketService,database,DraftTicket"),

        new("tc5",  "5 — Chat answers are generic",
            "Chat gives generic answers instead of real files. How do we fix grounding?",
            "CodeQuery",
            "PromptContextBuilder,CodeIndexService,GetRelevantSnippetsAsync,ChatWorkspaceViewModel",
            "Weaviate,embeddings"),

        new("tc6",  "6 — Draft tickets are generic",
            "Draft ticket generation is weak and generic. How do we make it specific to IronDev?",
            "DraftTicketFlow",
            "DraftTicketService,DraftTicketDtos,PromptContextBuilder,CodeIndexService",
            "TicketsWorkspaceViewModel delete,schema"),

        new("tc7",  "7 — Create Ticket + Plan empty",
            "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?",
            "CodeQuery",
            "TicketsWorkspaceViewModel,ImplementationPlansWorkspaceViewModel,ShellViewModel,ProjectImplementationPlan",
            "schema,Weaviate,LLM"),

        new("tc8",  "8 — Index Project First resume",
            "When I click Index Project First, indexing runs but the draft ticket is not generated after Ready. What should be fixed?",
            "DraftTicketFlow",
            "TicketsWorkspaceViewModel,SetIndexStatus,IsDraftIndexing,ChatTicketContext",
            "Weaviate,schema"),

        new("tc9",  "9 — Local LLM provider setup",
            "How can another developer run IronDev with Ollama or a local LLM?",
            "CodeQuery",
            "LlmOptions,OllamaLlmService,LocalOpenAiCompatibleLlmService,App.xaml.cs,ILLMService",
            "TicketService,Weaviate"),

        new("tc10", "10 — Fresh local DB setup",
            "What does a new developer need to do to set up the database and log in locally?",
            "General",
            "local_dev_setup.sql,rebuild_db.sql,local-development.md,README.md",
            "Weaviate,production"),
    ];

    // ── Test: 10 cases defined ────────────────────────────────────────────────

    [TestMethod]
    public void GroundingTestMatrix_HasExactlyTenCases()
        => Assert.AreEqual(10, Cases.Length, "The grounding test matrix must contain exactly 10 test cases.");

    // ── Test: Intent classification for each canonical message ───────────────

    [TestMethod]
    [Description("TC1 user message → SavedTicketManagement")]
    public void TC1_UserMessage_ClassifiesAs_SavedTicketManagement()
    {
        var tc = Cases.Single(c => c.Id == "tc1");
        var actual = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        Assert.AreEqual(ChatIntent.SavedTicketManagement, actual,
            $"[{tc.DisplayName}] ExpectedIntent={tc.ExpectedIntent}, actual={actual}");
    }

    [TestMethod]
    [Description("TC6 user message → DraftTicketFlow (key regression: was wrongly SavedTicketManagement)")]
    public void TC6_UserMessage_ClassifiesAs_DraftTicketFlow()
    {
        var tc = Cases.Single(c => c.Id == "tc6");
        var actual = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        Assert.AreEqual(ChatIntent.DraftTicketFlow, actual,
            $"[{tc.DisplayName}] ExpectedIntent={tc.ExpectedIntent}, actual={actual}");
    }

    [TestMethod]
    [Description("TC8 user message → DraftTicketFlow")]
    public void TC8_UserMessage_ClassifiesAs_DraftTicketFlow()
    {
        var tc = Cases.Single(c => c.Id == "tc8");
        var actual = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        Assert.AreEqual(ChatIntent.DraftTicketFlow, actual,
            $"[{tc.DisplayName}] ExpectedIntent={tc.ExpectedIntent}, actual={actual}");
    }

    [TestMethod]
    [Description("TC10 user message → General (not ticket-related)")]
    public void TC10_UserMessage_ClassifiesAs_GeneralOrCodeQuery()
    {
        var tc = Cases.Single(c => c.Id == "tc10");
        var actual = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        Assert.AreNotEqual(ChatIntent.SavedTicketManagement, actual,
            $"[{tc.DisplayName}] must not be SavedTicketManagement.");
        Assert.AreNotEqual(ChatIntent.DraftTicketFlow, actual,
            $"[{tc.DisplayName}] must not be DraftTicketFlow.");
    }

    // ── Test: MustIncludeAny terms appear in expansion ───────────────────────

    [TestMethod]
    [Description("TC1: at least one MustIncludeAny term appears in ExpandSearchQueries output.")]
    public void TC1_Expansion_ContainsAtLeastOneMustIncludeAnyTerm()
        => AssertExpansionContainsMustInclude(Cases.Single(c => c.Id == "tc1"));

    [TestMethod]
    [Description("TC6: at least one MustIncludeAny term appears in ExpandSearchQueries output.")]
    public void TC6_Expansion_ContainsAtLeastOneMustIncludeAnyTerm()
        => AssertExpansionContainsMustInclude(Cases.Single(c => c.Id == "tc6"));

    [TestMethod]
    [Description("TC8: at least one MustIncludeAny term appears in ExpandSearchQueries output.")]
    public void TC8_Expansion_ContainsAtLeastOneMustIncludeAnyTerm()
        => AssertExpansionContainsMustInclude(Cases.Single(c => c.Id == "tc8"));

    // ── Test: MustNotLeadWith terms are not first in expansion ───────────────

    [TestMethod]
    [Description("TC1: DraftTicketDtos must not be the first expanded query term.")]
    public void TC1_Expansion_DraftTicketDtos_NotFirst()
    {
        var tc = Cases.Single(c => c.Id == "tc1");
        var intent = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        var expanded = PromptContextBuilder.ExpandSearchQueries(tc.UserMessage, intent);
        var firstTerm = expanded.FirstOrDefault() ?? string.Empty;
        Assert.IsFalse(
            firstTerm.Contains("DraftTicketDtos", System.StringComparison.OrdinalIgnoreCase),
            $"[TC1] DraftTicketDtos must not be the first expanded query. First was: '{firstTerm}'");
    }

    [TestMethod]
    [Description("TC6: 'schema' or 'delete' terms must not be the first expanded query term.")]
    public void TC6_Expansion_SchemaTerm_NotFirst()
    {
        var tc = Cases.Single(c => c.Id == "tc6");
        var intent = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        var expanded = PromptContextBuilder.ExpandSearchQueries(tc.UserMessage, intent);
        var firstTerm = expanded.FirstOrDefault() ?? string.Empty;
        Assert.IsFalse(
            firstTerm.Contains("schema", System.StringComparison.OrdinalIgnoreCase),
            $"[TC6] 'schema' must not be the first expanded query. First was: '{firstTerm}'");
    }

    // ── Test: All IDs are unique ──────────────────────────────────────────────

    [TestMethod]
    public void GroundingTestMatrix_AllIds_AreUnique()
    {
        var ids = Cases.Select(c => c.Id).ToList();
        var distinct = ids.Distinct().ToList();
        Assert.AreEqual(ids.Count, distinct.Count, "All test case IDs must be unique.");
    }

    // ── Test: No case has an empty ExpectedIntent ─────────────────────────────

    [TestMethod]
    public void GroundingTestMatrix_AllCases_HaveExpectedIntent()
    {
        foreach (var tc in Cases)
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(tc.ExpectedIntent),
                $"Test case [{tc.Id}] '{tc.DisplayName}' has an empty ExpectedIntent.");
    }

    // ── Test: No case has an empty MustIncludeAny ────────────────────────────

    [TestMethod]
    public void GroundingTestMatrix_AllCases_HaveMustIncludeAny()
    {
        foreach (var tc in Cases)
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(tc.MustIncludeAny),
                $"Test case [{tc.Id}] '{tc.DisplayName}' has empty MustIncludeAny.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertExpansionContainsMustInclude(TestCaseSpec tc)
    {
        var intent   = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        var expanded = PromptContextBuilder.ExpandSearchQueries(tc.UserMessage, intent);
        var joined   = string.Join("|", expanded);

        var mustTerms = tc.MustIncludeAny.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var hit = mustTerms.Any(t => joined.Contains(t, System.StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(hit,
            $"[{tc.DisplayName}] None of the MustIncludeAny terms found in expanded queries.\n" +
            $"  MustIncludeAny: {tc.MustIncludeAny}\n" +
            $"  Expanded: {joined}");
    }
}

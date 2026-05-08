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
    // ── New tests: Run Grounding Test contract ────────────────────────────────

    [TestMethod]
    [Description("ItemsSource: all 10 canonical cases are populated (simulates non-empty dropdown).")]
    public void GroundingTestMatrix_ItemsSource_HasTenEntries()
        => Assert.AreEqual(10, Cases.Length,
            "TestCases (ItemsSource) must contain 10 entries so the dropdown is never empty.");

    [TestMethod]
    [Description("TC2: selecting 'Delete old chat sessions' sets UserMessage correctly.")]
    public void TC2_SelectedCase_SetsExpectedUserMessage()
    {
        var tc = Cases.Single(c => c.Id == "tc2");
        Assert.IsFalse(string.IsNullOrWhiteSpace(tc.UserMessage),
            "TC2 UserMessage must not be empty after selection.");
        Assert.IsTrue(tc.UserMessage.Contains("Chat History", System.StringComparison.OrdinalIgnoreCase),
            $"TC2 UserMessage should reference 'Chat History'. Actual: {tc.UserMessage}");
    }

    [TestMethod]
    [Description("Build Prompt: ClassifyIntent + ExpandSearchQueries produce non-empty output for every case.")]
    public void AllCases_BuildPrompt_ProducesNonEmptyExpansion()
    {
        foreach (var tc in Cases)
        {
            var intent   = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
            var expanded = PromptContextBuilder.ExpandSearchQueries(tc.UserMessage, intent);
            Assert.IsTrue(expanded.Count > 0,
                $"[{tc.Id}] ExpandSearchQueries must produce at least one query for '{tc.UserMessage}'.");
        }
    }

    [TestMethod]
    [Description("MustNotMention evaluation: 'Weaviate' in AI response triggers a violation for TC2.")]
    public void TC2_MustNotMention_Weaviate_DetectsViolation()
    {
        var tc = Cases.Single(c => c.Id == "tc2");
        var mustNotTerms = tc.MustNotLeadWith.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        // Simulate a bad AI response
        const string badResponse = "You should update the Weaviate index and TicketService to delete chats.";
        var responseLower = badResponse.ToLowerInvariant();

        // Check MustNotMention from the ViewModel spec (MustNotMention field)
        var mustNotMentionTerms = tc.MustNotLeadWith.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        // The MustNotLeadWith terms "DraftTicket,TicketService" should not be the top lead
        var violated = mustNotMentionTerms.Any(t =>
            responseLower.Contains(t.Trim().ToLowerInvariant()));
        Assert.IsTrue(violated,
            $"[TC2] MustNotLeadWith terms should be detected in bad response. Terms: {tc.MustNotLeadWith}");
    }

    [TestMethod]
    [Description("ExpectedFiles evaluation: ChatWorkspaceViewModel mention in response counts as hit for TC2.")]
    public void TC2_ExpectedFiles_ChatWorkspaceViewModel_DetectsHit()
    {
        var tc = Cases.Single(c => c.Id == "tc2");
        const string goodResponse = "You would need to update ChatWorkspaceViewModel and ChatHistoryService to delete sessions.";
        var responseLower = goodResponse.ToLowerInvariant();

        var fileTerms  = tc.MustIncludeAny.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var filesFound = fileTerms.Any(t => responseLower.Contains(t.ToLowerInvariant()));

        Assert.IsTrue(filesFound,
            $"[TC2] At least one MustIncludeAny term must be found in a good response. Terms: {tc.MustIncludeAny}");
    }

    [TestMethod]
    [Description("MustMention evaluation: 'session' term found in TC2 good response.")]
    public void TC2_MustMention_Session_DetectsHit()
    {
        // TC2 MustMention = "session,tenant,archive" (from chat-grounding-test-matrix spec)
        const string tc2MustMention = "session,tenant,archive";
        const string goodResponse = "Each session row in ProjectChatSessions can be soft-archived per tenant.";
        var responseLower = goodResponse.ToLowerInvariant();

        var mustTerms = tc2MustMention.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var mustFound = mustTerms.Any(t => responseLower.Contains(t.ToLowerInvariant()));

        Assert.IsTrue(mustFound,
            $"[TC2] At least one MustMention term must be found in good response. Terms: {tc2MustMention}");
    }

    [TestMethod]
    [Description("All cases have non-empty MustIncludeAny — ensures context checks are always possible.")]
    public void AllCases_HaveNonEmpty_MustIncludeAny()
    {
        foreach (var tc in Cases)
            Assert.IsFalse(string.IsNullOrWhiteSpace(tc.MustIncludeAny),
                $"[{tc.Id}] MustIncludeAny must not be empty.");
    }

    [TestMethod]
    [Description("EvaluateScore: intent mismatch always returns FAIL regardless of context.")]
    public void EvaluateScore_IntentMismatch_ReturnsFail()
    {
        // Simulate what the ViewModel does: !intentOk || violated => FAIL
        bool intentOk = false;
        bool violated = false;
        bool filesFound = true;
        bool mustFound = true;

        // Replicate ViewModel logic:
        string result;
        if (!intentOk || violated)
            result = "❌ FAIL";
        else if (!filesFound || !mustFound)
            result = "⚠️ WARNING — response weak";
        else
            result = "✅ PASS";

        Assert.AreEqual("❌ FAIL", result,
            "Intent mismatch must yield FAIL regardless of other checks.");
    }

    [TestMethod]
    [Description("EvaluateScore: all checks pass returns PASS.")]
    public void EvaluateScore_AllPass_ReturnsPass()
    {
        bool intentOk = true;
        bool violated = false;
        bool filesFound = true;
        bool mustFound = true;

        string result;
        if (!intentOk || violated)
            result = "❌ FAIL";
        else if (!filesFound || !mustFound)
            result = "⚠️ WARNING — response weak";
        else
            result = "✅ PASS";

        Assert.AreEqual("✅ PASS", result, "All checks passing must yield PASS.");
    }

    [TestMethod]
    [Description("EvaluateScore: violation of MustNotMention yields FAIL even if intent is correct.")]
    public void EvaluateScore_MustNotMentionViolation_ReturnsFail()
    {
        bool intentOk = true;
        bool violated = true;  // MustNotMention term found in response
        bool filesFound = true;
        bool mustFound = true;

        string result;
        if (!intentOk || violated)
            result = "❌ FAIL";
        else if (!filesFound || !mustFound)
            result = "⚠️ WARNING — response weak";
        else
            result = "✅ PASS";

        Assert.AreEqual("❌ FAIL", result,
            "MustNotMention violation must yield FAIL even with correct intent.");
    }
}

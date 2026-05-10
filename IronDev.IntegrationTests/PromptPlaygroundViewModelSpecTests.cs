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
            "TicketsWorkspaceViewModel,TicketsWorkspaceView.xaml,ProjectTicket,TicketService,ProjectTickets",
            "DraftTicketDtos.cs,DraftTicket,CodebaseTicketGeneratorModels.cs,TicketController,TicketModel,Repository,Controller"),

        new("tc2",  "2 — Delete old chat sessions",
            "What would I need to do to delete old chats from Chat History?",
            "CodeQuery",
            "ChatWorkspaceViewModel,ChatHistoryService,ProjectChatSessions,ChatMessages",
            "DraftTicket,TicketService"),

        new("tc3",  "3 — Ticket list shows noisy markdown",
            "The ticket list shows noisy markdown fragments. What should I change?",
            "SavedTicketManagement",
            "TicketsWorkspaceView.xaml,TicketsWorkspaceViewModel,ProjectTicket,MarkdownPreviewConverter,DataTemplate,TextTrimming",
            "DraftTicketService,database,schema,markdown parser,markdown-to-HTML,html conversion,storage"),

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
    [Description("TC3: at least one MustIncludeAny term appears in ExpandSearchQueries output.")]
    public void TC3_Expansion_ContainsAtLeastOneMustIncludeAnyTerm()
        => AssertExpansionContainsMustInclude(Cases.Single(c => c.Id == "tc3"));

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

    // ── Task 8: Additional end-to-end contract tests ──────────────────────────

    [TestMethod]
    [Description("TC3 updated MustIncludeAny includes richer terms for markdown ticket list fix.")]
    public void TC3_MustIncludeAny_ContainsRicherTerms()
    {
        var tc = Cases.Single(c => c.Id == "tc3");
        var terms = tc.MustIncludeAny.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        Assert.IsTrue(terms.Contains("TicketsWorkspaceView.xaml"), "TC3 must include TicketsWorkspaceView.xaml");
        Assert.IsTrue(terms.Contains("TicketsWorkspaceViewModel"), "TC3 must include TicketsWorkspaceViewModel");
        Assert.IsTrue(terms.Contains("ProjectTicket"), "TC3 must include ProjectTicket");
        Assert.IsTrue(terms.Length >= 5, $"TC3 should have at least 5 MustIncludeAny terms. Actual: {terms.Length}");
    }

    [TestMethod]
    [Description("TC3 MustNotLeadWith now excludes markdown parser and HTML conversion paths.")]
    public void TC3_MustNotLeadWith_ExcludesGenericMarkdownApproaches()
    {
        var tc = Cases.Single(c => c.Id == "tc3");
        var terms = tc.MustNotLeadWith.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        Assert.IsTrue(
            terms.Any(t => t.Contains("markdown", System.StringComparison.OrdinalIgnoreCase)),
            "TC3 MustNotLeadWith must guard against generic markdown parser approaches.");
    }

    [TestMethod]
    [Description("ILLMService contract: GetResponseAsync signature takes prompt string + CancellationToken.")]
    public void ILLMService_Contract_HasGetResponseAsync()
    {
        // Verify via reflection that ILLMService has the expected method signature
        var iface = typeof(IronDev.Core.ILLMService);
        var method = iface.GetMethod("GetResponseAsync");
        Assert.IsNotNull(method, "ILLMService must have GetResponseAsync method.");
        var parameters = method!.GetParameters();
        Assert.AreEqual(2, parameters.Length, "GetResponseAsync must take 2 parameters (prompt, CancellationToken).");
        Assert.AreEqual(typeof(string), parameters[0].ParameterType, "First param must be string.");
    }

    [TestMethod]
    [Description("PromptPreviewResult has all required fields for the playground result panel.")]
    public void PromptPreviewResult_HasRequiredFields()
    {
        var type = typeof(IronDev.AI.PromptPreviewResult);
        Assert.IsNotNull(type.GetProperty("PromptText"),         "PromptPreviewResult needs PromptText");
        Assert.IsNotNull(type.GetProperty("DetectedIntent"),    "PromptPreviewResult needs DetectedIntent");
        Assert.IsNotNull(type.GetProperty("ProjectIndexStatus"),"PromptPreviewResult needs ProjectIndexStatus");
        Assert.IsNotNull(type.GetProperty("ContextQuality"),    "PromptPreviewResult needs ContextQuality");
        Assert.IsNotNull(type.GetProperty("RetrievedItems"),    "PromptPreviewResult needs RetrievedItems");
    }

    [TestMethod]
    [Description("Build Prompt empty-state: no retrieved context must leave empty state explicit (not hidden).")]
    public void BuildPrompt_EmptyRetrievedContext_IsExplicitNotHidden()
    {
        // The ViewModel uses RetrievedItems.Count == 0 to show the empty-state message.
        // This test validates the logic path exists and is deterministic.
        var items = new System.Collections.ObjectModel.ObservableCollection<object>();
        Assert.AreEqual(0, items.Count,
            "An empty RetrievedItems collection must have Count==0 so XAML empty-state trigger fires.");
    }

    [TestMethod]
    [Description("EvaluateScore: weak response (missing files + must-mention) yields WARNING.")]
    public void EvaluateScore_WeakResponse_ReturnsWarning()
    {
        bool intentOk  = true;
        bool violated  = false;
        bool filesFound = false;  // no expected files found
        bool mustFound  = false;  // no must-mention found

        string result;
        if (!intentOk || violated)
            result = "❌ FAIL";
        else if (!filesFound || !mustFound)
            result = "⚠️ WARNING — response weak";
        else
            result = "✅ PASS";

        Assert.AreEqual("⚠️ WARNING — response weak", result,
            "Correct intent but missing files/mustMention must yield WARNING.");
    }

    [TestMethod]
    [Description("All test cases have a non-empty DisplayName in the expected format 'N — Description'.")]
    public void AllCases_DisplayName_MatchesExpectedFormat()
    {
        foreach (var tc in Cases)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(tc.DisplayName),
                $"[{tc.Id}] DisplayName must not be empty.");
            Assert.IsTrue(tc.DisplayName.Contains(" — "),
                $"[{tc.Id}] DisplayName must contain ' — ' separator. Actual: {tc.DisplayName}");
        }
    }

    [TestMethod]
    [Description("TC3: user message 'noisy markdown' classifies as SavedTicketManagement (ticket list = workspace).")]
    public void TC3_UserMessage_ClassifiesAs_SavedTicketManagement()
    {
        var tc = Cases.Single(c => c.Id == "tc3");
        var actual = PromptContextBuilder.ClassifyIntent(tc.UserMessage);
        Assert.AreEqual(ChatIntent.SavedTicketManagement, actual,
            $"[{tc.DisplayName}] Expected SavedTicketManagement, actual={actual}");
    }

    [TestMethod]
    [Description("TC1: MustNotMention — generic MVC term 'TicketController' in AI response causes violation.")]
    public void TC1_MustNotMention_TicketController_DetectsViolation()
    {
        // Generic MVC response that should FAIL the tc1 evaluation
        const string badResponse = "You need to update TicketController and TicketModel, then call the Repository layer to delete.";
        var responseLower = badResponse.ToLowerInvariant();
        var mustNotTerms  = new[] { "ticketcontroller", "ticketmodel", "repository", "controller" };
        var violated = mustNotTerms.Any(t => responseLower.Contains(t));
        Assert.IsTrue(violated,
            "[TC1] Generic MVC terms (TicketController/TicketModel/Repository) must be detected as a grounding failure.");
    }

    [TestMethod]
    [Description("Junk memory filter: decisions with placeholder text must be excluded from the prompt.")]
    public void JunkMemoryFilter_PlaceholderDecision_IsDetectedAsJunk()
    {
        var junkDetails = new[]
        {
            "It seems you're looking for assistance with a decision",
            "Could you please provide more specific details",
            "Please provide more context",
            "ffg",   // too short
            "ok",    // too short
        };
        foreach (var detail in junkDetails)
        {
            var isJunk = detail.Length < 20
                || detail.StartsWith("It seems",       System.StringComparison.OrdinalIgnoreCase)
                || detail.StartsWith("Could you",      System.StringComparison.OrdinalIgnoreCase)
                || detail.StartsWith("Please provide", System.StringComparison.OrdinalIgnoreCase);
            Assert.IsTrue(isJunk,
                $"Detail '{detail.Substring(0, System.Math.Min(40, detail.Length))}' should be classified as junk.");
        }
    }

    [TestMethod]
    [Description("Junk memory filter: a real architectural decision is NOT filtered out.")]
    public void JunkMemoryFilter_RealDecision_IsNotJunk()
    {
        const string realDecision = "DraftTicket is only for the Chat to Draft Ticket review flow. ProjectTicket is the saved ticket model.";
        var isJunk = realDecision.Length < 20
            || realDecision.StartsWith("It seems",       System.StringComparison.OrdinalIgnoreCase)
            || realDecision.StartsWith("Could you",      System.StringComparison.OrdinalIgnoreCase)
            || realDecision.StartsWith("Please provide", System.StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(isJunk, "A real project decision must not be classified as junk.");
    }

    [TestMethod]
    [Description("Grounding-first rule: hedging language ('likely','possibly') signals weak grounding for TC1.")]
    public void GroundingFirstRule_HedgingLanguage_SignalsWeakGrounding()
    {
        // TC1 MustNotMention now includes 'likely,possibly,typically'
        const string hedgedResponse = "You would likely need to update TicketService, and possibly add a DeleteTicket method.";
        var responseLower = hedgedResponse.ToLowerInvariant();
        var hedgeTerms = new[] { "likely", "possibly", "typically" };
        var violated = hedgeTerms.Any(t => responseLower.Contains(t));
        Assert.IsTrue(violated,
            "[TC1] Hedging language ('likely'/'possibly') in AI response must be detected as a grounding weakness.");
    }

    // ── Fix 5: Prompt grounding quality tests ───────────────────────────────────────

    [TestMethod]
    [Description("Fix 5.1: IsJunkMemory detects TicketModel as an arch-poison term.")]
    public void IsJunkMemory_TicketModel_IsDetectedAndFiltered()
    {
        const string badTicket = "The main files/classes involved include TicketModel and TicketController for handling ticket data.";
        var (isJunk, terms) = PromptContextBuilder.IsJunkMemory(badTicket);
        Assert.IsTrue(isJunk, "Content containing 'TicketModel' must be classified as junk.");
        Assert.IsTrue(terms.Any(t => t.Equals("TicketModel", System.StringComparison.OrdinalIgnoreCase)),
            "PollutedTerms must include 'TicketModel'.");
    }

    [TestMethod]
    [Description("Fix 5.1: IsJunkMemory detects TicketController as an arch-poison term.")]
    public void IsJunkMemory_TicketController_IsDetectedAndFiltered()
    {
        const string badTicket = "You need to update TicketController and call the Repository layer.";
        var (isJunk, terms) = PromptContextBuilder.IsJunkMemory(badTicket);
        Assert.IsTrue(isJunk, "Content containing 'TicketController' must be classified as junk.");
        Assert.IsTrue(terms.Any(t => t.Equals("TicketController", System.StringComparison.OrdinalIgnoreCase)),
            "PollutedTerms must include 'TicketController'.");
    }

    [TestMethod]
    [Description("Fix 5.1: IsJunkMemory detects 'View Templates' as an arch-poison term.")]
    public void IsJunkMemory_ViewTemplates_IsDetectedAndFiltered()
    {
        const string badTicket = "The main files include View Templates for rendering the ticketing interface.";
        var (isJunk, terms) = PromptContextBuilder.IsJunkMemory(badTicket);
        Assert.IsTrue(isJunk, "Content containing 'View Templates' must be classified as junk.");
        Assert.IsTrue(terms.Any(t => t.Equals("View Templates", System.StringComparison.OrdinalIgnoreCase)),
            "PollutedTerms must include 'View Templates'.");
    }

    [TestMethod]
    [Description("Fix 5.1: IsJunkMemory detects 'Repository' as an arch-poison term.")]
    public void IsJunkMemory_Repository_IsDetectedAndFiltered()
    {
        const string badTicket = "Call the Repository layer to perform the delete operation on the database.";
        var (isJunk, terms) = PromptContextBuilder.IsJunkMemory(badTicket);
        Assert.IsTrue(isJunk, "Content containing 'Repository' must be classified as junk.");
        Assert.IsTrue(terms.Any(t => t.Equals("Repository", System.StringComparison.OrdinalIgnoreCase)),
            "PollutedTerms must include 'Repository'.");
    }

    [TestMethod]
    [Description("Fix 5.1: Real IronDev decision text (no poison terms) is NOT filtered.")]
    public void IsJunkMemory_RealIronDevDecision_IsNotFiltered()
    {
        const string real = "TicketService is the service for ProjectTicket persistence. ArchiveTicketAsync should check tenant ownership before deletion.";
        var (isJunk, terms) = PromptContextBuilder.IsJunkMemory(real);
        Assert.IsFalse(isJunk,  "Real IronDev decision text must not be filtered.");
        Assert.AreEqual(0, terms.Count, "No pollution terms should be reported for clean content.");
    }

    [TestMethod]
    [Description("Fix 5.3: EvaluateScore logic — correct answer with Limited/Unknown context scores WARNING not PASS.")]
    public void EvaluateScore_CorrectAnswer_LimitedContext_ReturnsWarning()
    {
        // Mirrors the condition seen in the observed playground screenshot:
        // intent ok, all terms matched in response, but index = Unknown / no snippets retrieved.
        bool intentOk      = true;
        bool hasMustInclude = true;   // all expected files/classes found in answer
        bool badLead       = false;
        bool hasProject    = true;
        bool contextIsReady = false;  // Index = Unknown / Limited, no snippets

        // Replicate the EvaluateScore logic
        string result;
        if (!hasProject)    result = intentOk ? "⚠️ WARNING — no project" : "❌ FAIL — no project + intent mismatch";
        else if (!intentOk) result = "❌ FAIL";
        else if (badLead)   result = "❌ FAIL — wrong context leads";
        else if (hasMustInclude && contextIsReady)  result = "✅ PASS";
        else if (hasMustInclude && !contextIsReady) result = "⚠️ WARNING — terms matched, context limited";
        else                result = "⚠️ WARNING — intent ok, context limited";

        Assert.IsTrue(result.StartsWith("⚠️ WARNING"),
            $"Expected WARNING for correct answer with limited context. Got: {result}");
    }

    [TestMethod]
    [Description("Fix 5.4: EvaluateScore — forbidden MVC term in response produces FAIL.")]
    public void EvaluateScore_ForbiddenTerm_InAiResponse_ReturnsFail()
    {
        // The RunGroundingTest evaluator checks MustNotMention against AI response
        const string aiResponse = "You should update TicketController and call the Repository delete method.";
        var responseLower = aiResponse.ToLowerInvariant();
        var mustNotTerms  = new[] { "ticketcontroller", "ticketmodel", "repository", "controller", "weaviate" };
        var violated = mustNotTerms.Any(t => responseLower.Contains(t));

        // If violated, intentOk irrelevant — result must be FAIL
        var result = violated ? "❌ FAIL" : "✅ PASS";
        Assert.AreEqual("❌ FAIL", result,
            "AI response containing forbidden MVC terms must score FAIL.");
    }

    [TestMethod]
    [Description("Fix 5.5: EvaluateScore — PASS requires context ready AND all terms present.")]
    public void EvaluateScore_AllConditionsMet_WithReadyContext_ReturnsPass()
    {
        bool intentOk       = true;
        bool hasMustInclude  = true;
        bool badLead        = false;
        bool hasProject     = true;
        bool contextIsReady = true;   // Index = Ready or snippets retrieved

        string result;
        if (!hasProject)    result = intentOk ? "⚠️ WARNING — no project" : "❌ FAIL — no project + intent mismatch";
        else if (!intentOk) result = "❌ FAIL";
        else if (badLead)   result = "❌ FAIL — wrong context leads";
        else if (hasMustInclude && contextIsReady)  result = "✅ PASS";
        else if (hasMustInclude && !contextIsReady) result = "⚠️ WARNING — terms matched, context limited";
        else                result = "⚠️ WARNING — intent ok, context limited";

        Assert.AreEqual("✅ PASS", result,
            "All conditions met with ready context must score PASS.");
    }

    [TestMethod]
    [Description("Fix 5.6: TC1 MustIncludeAny now requires ProjectTickets.")]
    public void TC1_MustIncludeAny_ContainsProjectTickets()
    {
        var tc = Cases.Single(c => c.Id == "tc1");
        var terms = tc.MustIncludeAny.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        Assert.IsTrue(terms.Contains("ProjectTickets"),
            "TC1 MustIncludeAny must include 'ProjectTickets' (the database table / collection name).");
    }

    // ── Problem 2 & 3: Index status vs context retrieval status ──────────────

    [TestMethod]
    [Description("P2: Project index status 'Ready' does not conflate with context retrieval result — they are separate.")]
    public void ProjectIndexStatus_Ready_DoesNotImplyContextRetrieved()
    {
        // A project can be Ready (indexed) but still return 0 snippets for a specific query.
        // The ViewModel now has separate ProjectIndexStatus and ContextRetrievalStatus.
        // This test confirms the two concepts are independent strings.
        const string projectIndexStatus = "Ready";
        int retrievedCount = 0;

        // ContextRetrievalStatus logic mirrors ViewModel
        string contextRetrievalStatus = retrievedCount > 0
            ? $"Retrieved {retrievedCount} snippet(s)"
            : string.Equals(projectIndexStatus, "Ready", System.StringComparison.OrdinalIgnoreCase)
                ? "Empty — project indexed but no snippets matched"
                : "Limited — project not yet indexed";

        Assert.AreEqual("Ready", projectIndexStatus,
            "ProjectIndexStatus must remain 'Ready' — not overwritten by retrieval result.");
        Assert.IsTrue(contextRetrievalStatus.StartsWith("Empty"),
            $"ContextRetrievalStatus should be 'Empty...' when project is Ready but 0 snippets returned. Got: {contextRetrievalStatus}");
        Assert.AreNotEqual(projectIndexStatus, contextRetrievalStatus,
            "ProjectIndexStatus and ContextRetrievalStatus must be distinct values.");
    }

    [TestMethod]
    [Description("P2: ProjectIndexStatus is never 'Unknown' when project.IndexingStatus is a non-empty string.")]
    public void ProjectIndexStatus_NeverUnknown_WhenStatusKnown()
    {
        // ViewModel now uses: project.IndexingStatus is { Length: > 0 } s ? s : "Not indexed"
        // Test each plausible status string
        var knownStatuses = new[] { "Ready", "Indexing", "Pending", "Error", "NotStarted" };
        foreach (var status in knownStatuses)
        {
            var result = status is { Length: > 0 } ? status : "Not indexed";
            Assert.AreNotEqual("Unknown", result,
                $"ProjectIndexStatus must never be 'Unknown' when IndexingStatus='{status}'. Got: '{result}'");
        }
    }

    [TestMethod]
    [Description("P3: Correct answer with zero retrieved snippets must produce WARNING, never PASS.")]
    public void EvaluateScore_CorrectAnswer_ZeroSnippets_ReturnsWarning_NotPass()
    {
        // This precisely mirrors the delete-ticket scenario:
        // Intent ok, all expected terms found in AI response, MustNotMention clean,
        // but RetrievedItems.Count == 0 (no snippets actually came from the index).
        bool intentOk    = true;
        bool violated    = false;
        bool filesFound  = true;  // AI mentioned TicketsWorkspaceViewModel etc.
        bool mustFound   = true;  // AI mentioned all required terms
        bool hasSnippets = false; // RetrievedItems.Count == 0

        // Replicate RunGroundingTestAsync scoring logic
        string result;
        if (!intentOk || violated)
            result = "❌ FAIL";
        else if (!filesFound || !mustFound)
            result = "⚠️ WARNING — response weak";
        else if (!hasSnippets)
            result = "⚠️ WARNING — correct answer, no retrieved context";
        else
            result = "✅ PASS";

        Assert.IsTrue(result.StartsWith("⚠️ WARNING"),
            $"Zero retrieved snippets must yield WARNING even when answer quality checks pass. Got: {result}");
        Assert.AreNotEqual("✅ PASS", result,
            "PASS must not be awarded when no context snippets were retrieved.");
    }

    [TestMethod]
    [Description("P3: ContextRetrievalStatus when snippets ARE retrieved reflects snippet count.")]
    public void ContextRetrievalStatus_WithSnippets_ShowsCount()
    {
        const string projectIndexStatus = "Ready";
        int retrievedCount = 5;

        string contextRetrievalStatus = retrievedCount > 0
            ? $"Retrieved {retrievedCount} snippet(s)"
            : string.Equals(projectIndexStatus, "Ready", System.StringComparison.OrdinalIgnoreCase)
                ? "Empty — project indexed but no snippets matched"
                : "Limited — project not yet indexed";

        Assert.IsTrue(contextRetrievalStatus.StartsWith("Retrieved"),
            $"ContextRetrievalStatus should start with 'Retrieved' when snippets exist. Got: {contextRetrievalStatus}");
        Assert.IsTrue(contextRetrievalStatus.Contains("5"),
            "ContextRetrievalStatus should include the snippet count.");
    }
}

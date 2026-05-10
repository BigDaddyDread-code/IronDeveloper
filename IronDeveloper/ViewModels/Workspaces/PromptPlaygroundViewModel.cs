using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.AI;
using IronDev.Core;
using IronDev.Services;

namespace IronDev.Agent.ViewModels.Workspaces;

/// <summary>
/// A single entry shown in the Retrieved Context list.
/// </summary>
public sealed class RetrievedContextItem
{
    public string FilePath   { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public string Preview    { get; init; } = string.Empty;

    public override string ToString()
        => string.IsNullOrWhiteSpace(SymbolName)
            ? FilePath
            : $"{FilePath}  →  {SymbolName}";
}

/// <summary>
/// One grounding test case, matching the spec in Docs/chat-grounding-test-matrix.md.
/// </summary>
public sealed class GroundingTestCase
{
    public string Id             { get; init; } = string.Empty;
    public string DisplayName    { get; init; } = string.Empty;
    public string UserMessage    { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;

    /// <summary>Comma-separated. At least one must appear in expanded queries or retrieved files.</summary>
    public string MustIncludeAny { get; init; } = string.Empty;

    /// <summary>Comma-separated. These must NOT be the top retrieved file.</summary>
    public string MustNotLeadWith { get; init; } = string.Empty;

    /// <summary>Comma-separated. These concepts must appear in the prompt or answer.</summary>
    public string MustMention    { get; init; } = string.Empty;

    /// <summary>Comma-separated. These must not appear in the answer.</summary>
    public string MustNotMention { get; init; } = string.Empty;

    public string PassRule { get; init; } = string.Empty;
    public string FailRule { get; init; } = string.Empty;

    // ── Computed display helpers ─────────────────────────────────────────────
    public string MustIncludeAnyDisplay  => MustIncludeAny.Replace(",",  " · ");
    public string MustNotLeadWithDisplay => MustNotLeadWith.Replace(",", " · ");
    public string MustMentionDisplay     => MustMention.Replace(",",     " · ");
    public string MustNotMentionDisplay  => MustNotMention.Replace(",",  " · ");

    /// <summary>
    /// WPF ComboBox custom templates that use SelectionBoxItem via ContentPresenter
    /// render the object's ToString() for the selected-item header.
    /// Return DisplayName so the header shows correctly without needing SelectionBoxItemTemplate.
    /// </summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// Developer-only read-only Prompt Playground.
///
/// Allows inspection of exactly what PromptContextBuilder produces for a
/// sample user message — without triggering a live LLM call.
///
/// Features:
///   - 10 canonical grounding test cases matching the test matrix spec.
///   - Detected vs expected intent with PASS / WARNING / FAIL scoring.
///   - Expanded search queries shown.
///   - Retrieved context list with file path + symbol.
///   - Full generated prompt (read-only, monospaced).
///   - Copy Prompt to clipboard.
/// </summary>
public sealed partial class PromptPlaygroundViewModel : ObservableObject
{
    private readonly IPromptContextBuilder _builder;
    private readonly IProjectService        _projectService;
    private readonly ILLMService            _llmService;

    // ── Observable Properties ─────────────────────────────────────────────

    [ObservableProperty] private string            _sampleUserMessage   = string.Empty;
    [ObservableProperty] private GroundingTestCase? _selectedTestCase;

    // Intent
    [ObservableProperty] private string _detectedIntent   = string.Empty;
    [ObservableProperty] private string _expectedIntent   = "(not set)";
    [ObservableProperty] private string _intentMatchBadge = "—";

    // Test case metadata shown in UI
    [ObservableProperty] private string _expectedFilesClasses = string.Empty;
    [ObservableProperty] private string _mustNotLeadWith      = string.Empty;
    [ObservableProperty] private string _mustMention          = string.Empty;
    [ObservableProperty] private string _mustNotMention       = string.Empty;
    [ObservableProperty] private string _passRule             = string.Empty;
    [ObservableProperty] private string _failRule             = string.Empty;

    // Build results
    [ObservableProperty] private string _projectIndexStatus      = string.Empty;
    [ObservableProperty] private string _contextRetrievalStatus  = string.Empty; // Retrieved / Empty / Limited
    [ObservableProperty] private string _contextQuality          = string.Empty;
    [ObservableProperty] private string _expandedQueries         = string.Empty;
    [ObservableProperty] private string _promptText              = string.Empty;
    [ObservableProperty] private string _errorMessage            = string.Empty;
    [ObservableProperty] private string _resultStatus            = "—"; // ✅ PASS / ⚠️ WARNING / ❌ FAIL / —
    [ObservableProperty] private bool   _isBuilding              = false;

    // ── Prompt Pollution Diagnostics (Fix 2) ───────────────────────────────
    [ObservableProperty] private bool   _contextPolluted       = false;
    [ObservableProperty] private string _pollutedTermsSummary  = string.Empty;
    [ObservableProperty] private int    _filteredMemoryCount   = 0;
    [ObservableProperty] private int    _includedMemoryCount   = 0;
    /// <summary>Human-readable badge: Clean / ⚠️ Polluted (×N filtered)</summary>
    [ObservableProperty] private string _contextQualityBadge   = string.Empty;

    // ── Run Grounding Test results ────────────────────────────────────────
    [ObservableProperty] private bool   _isRunningTest         = false;
    [ObservableProperty] private string _runStatusMessage      = string.Empty;
    [ObservableProperty] private string _aiResponse            = string.Empty;
    [ObservableProperty] private string _mustMentionStatus     = "—";
    [ObservableProperty] private string _mustNotMentionStatus  = "—";
    [ObservableProperty] private string _expectedFilesStatus   = "—";
    [ObservableProperty] private string _providerInfo          = string.Empty;

    public ObservableCollection<RetrievedContextItem> RetrievedItems { get; } = new();
    public IReadOnlyList<GroundingTestCase>            TestCases      { get; } = BuildTestCases();

    /// <summary>True when at least one item has been retrieved — drives card visibility.</summary>
    public bool HasRetrievedItems => RetrievedItems.Count > 0;

    /// <summary>Character count of the generated prompt — shown in the Expander header.</summary>
    public string PromptCharCount => PromptText.Length == 0
        ? "(empty)"
        : $"{PromptText.Length:N0} chars";

    // ── Constructor ───────────────────────────────────────────────────────

    public PromptPlaygroundViewModel(
        IPromptContextBuilder builder,
        IProjectService        projectService,
        ILLMService            llmService)
    {
        _builder        = builder;
        _projectService = projectService;
        _llmService     = llmService;

        RetrievedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRetrievedItems));
        RetrievedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ContextRetrievalStatus));
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BuildPromptAsync(CancellationToken ct)
    {
        var question = SampleUserMessage?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question))
        {
            ErrorMessage = "Enter a sample user message first.";
            return;
        }

        IsBuilding               = true;
        ErrorMessage             = string.Empty;
        PromptText               = string.Empty;
        ContextRetrievalStatus   = string.Empty;
        ResultStatus  = "—";
        RetrievedItems.Clear();

        try
        {
            // ── 1. Intent classification (static, no DB) ─────────────────────
            var intent = PromptContextBuilder.ClassifyIntent(question);
            DetectedIntent = intent.ToString();

            var expanded = PromptContextBuilder.ExpandSearchQueries(question, intent);
            ExpandedQueries = string.Join("\n", expanded);

            // ── 2. Intent match badge ────────────────────────────────────────
            var intentOk = SelectedTestCase is not null
                && string.Equals(DetectedIntent, ExpectedIntent, StringComparison.OrdinalIgnoreCase);
            IntentMatchBadge = SelectedTestCase is not null
                ? (intentOk ? "✅ Match" : "⚠️ Mismatch") : "—";

            // ── 3. Try to resolve a project (best-effort — never fatal) ──────
            IronDev.Data.Models.Project? project = null;
            string projectError = string.Empty;
            try
            {
                var projects = await _projectService.GetProjectsAsync(ct);
                project = projects?.Count > 0 ? projects[0] : null;
            }
            catch (Exception ex)
            {
                projectError = ex.Message;
            }

            if (project is null)
            {
                ProjectIndexStatus     = string.IsNullOrEmpty(projectError) ? "No active project" : "Error";
                ContextRetrievalStatus = "Empty";
                ContextQuality         = "Intent-only";

                // Always produce output so the TextBox is not blank
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("  INTENT ANALYSIS  (no project context — DB not queried)");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine();
                sb.AppendLine($"Detected Intent : {DetectedIntent}");
                if (SelectedTestCase is not null)
                    sb.AppendLine($"Expected Intent : {ExpectedIntent}  {(intentOk ? "✅ MATCH" : "⚠️ MISMATCH")}");
                sb.AppendLine();
                sb.AppendLine("Expanded Search Queries:");
                foreach (var q in expanded)
                    sb.AppendLine($"  • {q}");
                if (!string.IsNullOrEmpty(projectError))
                {
                    sb.AppendLine();
                    sb.AppendLine($"Project error   : {projectError}");
                    sb.AppendLine("Hint: Open a project first, then re-open Settings → Developer Tools.");
                }
                PromptText = sb.ToString();
                OnPropertyChanged(nameof(PromptCharCount));
                ResultStatus = intentOk ? "⚠️ WARNING — intent only, no project" : "❌ FAIL — no project + intent mismatch";
                return;
            }

            // ProjectIndexStatus reflects what the project reports — never substitute Unknown
            ProjectIndexStatus = project.IndexingStatus is { Length: > 0 } s ? s : "Not indexed";
            ContextQuality     = string.Equals(project.IndexingStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                                     ? "Indexed" : "Limited";

            // ── 4. Full prompt build (uses DB) ───────────────────────────────
            var result = await _builder.BuildFullPromptForTestingAsync(project.Id, question, ct);
            PromptText = result.PromptText ?? string.Empty;

            // If the builder returned an empty prompt, say why
            if (string.IsNullOrWhiteSpace(PromptText))
                PromptText = $"[Builder returned empty prompt — IndexingStatus: {project.IndexingStatus}]";

            OnPropertyChanged(nameof(PromptCharCount));

            foreach (var item in result.RetrievedItems)
            {
                RetrievedItems.Add(new RetrievedContextItem
                {
                    FilePath   = item.FilePath   ?? string.Empty,
                    SymbolName = item.SymbolName  ?? string.Empty,
                    Preview    = item.ChunkText is { Length: > 0 } s2
                                     ? s2[..Math.Min(120, s2.Length)].Replace('\n', ' ')
                                     : string.Empty
                });
            }

            // Separate context retrieval status from project index status
            ContextRetrievalStatus = RetrievedItems.Count > 0
                ? $"Retrieved {RetrievedItems.Count} snippet(s)"
                : string.Equals(ProjectIndexStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                    ? "Empty — project indexed but no snippets matched"
                    : "Limited — project not yet indexed";

            // ── Fix 2: Populate pollution diagnostics ────────────────────
            ContextPolluted      = result.ContextPolluted;
            FilteredMemoryCount  = result.FilteredMemoryCount;
            IncludedMemoryCount  = result.IncludedMemoryCount;
            PollutedTermsSummary = result.PollutedTermsFound.Count > 0
                ? string.Join(", ", result.PollutedTermsFound)
                : string.Empty;
            ContextQualityBadge = result.ContextPolluted
                ? $"⚠️ Polluted — {result.FilteredMemoryCount} item(s) filtered: {PollutedTermsSummary}"
                : result.FilteredMemoryCount > 0
                    ? $"✅ Clean ({result.FilteredMemoryCount} junk item(s) filtered)"
                    : "✅ Clean";

            // ── 5. Score against selected test case (Fix 3: require retrieved context) ──
            if (SelectedTestCase is not null)
            {
                var mustTerms    = SplitTerms(SelectedTestCase.MustIncludeAny);
                var mustNotTerms = SplitTerms(SelectedTestCase.MustNotLeadWith);

                var allRetrieved = RetrievedItems.Select(i => i.FilePath + " " + i.SymbolName).ToList();
                allRetrieved.Add(ExpandedQueries);
                var hasMustInclude = mustTerms.Any(t =>
                    allRetrieved.Any(r => r.Contains(t, StringComparison.OrdinalIgnoreCase)));

                var topFile = RetrievedItems.Count > 0
                    ? RetrievedItems[0].FilePath + " " + RetrievedItems[0].SymbolName
                    : string.Empty;
                var badLead = mustNotTerms.Any(t => topFile.Contains(t, StringComparison.OrdinalIgnoreCase));

                // PASS requires actual retrieved snippets — index being Ready is necessary but not sufficient
                var contextIsReady = RetrievedItems.Count > 0;

                IntentMatchBadge = intentOk ? "✅ Match" : "⚠️ Mismatch";
                ResultStatus     = EvaluateScore(intentOk, hasMustInclude, badLead,
                                                 hasProject: true, contextIsReady: contextIsReady);
            }
            else
            {
                ResultStatus = "—";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            // Even on error, show intent data if we got that far
            if (string.IsNullOrEmpty(PromptText) && !string.IsNullOrEmpty(DetectedIntent))
                PromptText = $"[Error during prompt build — intent analysis only]\n\nDetected: {DetectedIntent}\nExpanded: {ExpandedQueries}\n\nError: {ex.Message}";
            ResultStatus = "—";
        }
        finally
        {
            IsBuilding = false;
        }
    }

    // ── Run Grounding Test ────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRunGroundingTest))]
    private async Task RunGroundingTestAsync(CancellationToken ct)
    {
        // Ensure we have a prompt first
        if (string.IsNullOrWhiteSpace(PromptText))
            await BuildPromptAsync(ct);

        if (string.IsNullOrWhiteSpace(PromptText))
        {
            ErrorMessage = "Build Prompt failed — cannot run test.";
            return;
        }

        IsRunningTest    = true;
        RunStatusMessage = "Running grounding test…";
        AiResponse       = string.Empty;
        MustMentionStatus    = "—";
        MustNotMentionStatus = "—";
        ExpectedFilesStatus  = "—";
        ErrorMessage     = string.Empty;

        try
        {
            // Identify provider for display
            ProviderInfo = _llmService.GetType().Name.Replace("LlmService", string.Empty);

            var response = await _llmService.GetResponseAsync(PromptText, ct);
            AiResponse = response ?? string.Empty;

            // Evaluate response against selected test case
            if (SelectedTestCase is not null && !string.IsNullOrWhiteSpace(AiResponse))
            {
                var responseLower = AiResponse.ToLowerInvariant();

                // Expected files/classes check
                var fileTerms   = SplitTerms(SelectedTestCase.MustIncludeAny);
                var filesFound  = fileTerms.Any(t => responseLower.Contains(t.ToLowerInvariant()));
                ExpectedFilesStatus = filesFound ? "✅ Found" : "⚠️ Not found";

                // Must mention check
                var mustTerms   = SplitTerms(SelectedTestCase.MustMention);
                var mustFound   = mustTerms.Length == 0 || mustTerms.Any(t => responseLower.Contains(t.ToLowerInvariant()));
                MustMentionStatus = mustTerms.Length == 0 ? "—" : (mustFound ? "✅ Found" : "❌ Missing");

                // Must NOT mention check
                var mustNotTerms  = SplitTerms(SelectedTestCase.MustNotMention);
                var violated      = mustNotTerms.Any(t => responseLower.Contains(t.ToLowerInvariant()));
                MustNotMentionStatus = mustNotTerms.Length == 0 ? "—" : (violated ? "❌ Violation" : "✅ Clean");

                // PASS requires actual retrieved snippets — index status alone is not sufficient
                var intentOk = string.Equals(DetectedIntent, ExpectedIntent, StringComparison.OrdinalIgnoreCase);
                var hasSnippets = RetrievedItems.Count > 0;
                if (!intentOk || violated)
                    ResultStatus = "❌ FAIL";
                else if (!filesFound || !mustFound)
                    ResultStatus = "⚠️ WARNING — response weak";
                else if (!hasSnippets)
                    ResultStatus = "⚠️ WARNING — correct answer, no retrieved context";
                else
                    ResultStatus = "✅ PASS";
            }

            RunStatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            RunStatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage     = $"Run test error ({ProviderInfo}): {ex.Message}";
            RunStatusMessage = string.Empty;

            // Diagnosis hints
            var msg = ex.Message.ToLowerInvariant();
            if (msg.Contains("api key") || msg.Contains("unauthorized") || msg.Contains("401"))
                ErrorMessage += "\nHint: Check your API key in appsettings.json or OPENAI_API_KEY env variable.";
            else if (msg.Contains("connection") || msg.Contains("refused") || msg.Contains("timeout"))
                ErrorMessage += "\nHint: Local LLM endpoint may be unreachable. Check appsettings.json Ai:BaseUrl.";
        }
        finally
        {
            IsRunningTest = false;
        }
    }

    private bool CanRunGroundingTest() => !IsRunningTest && !IsBuilding;

    [RelayCommand]
    private void CopyPrompt()
    {
        if (!string.IsNullOrWhiteSpace(PromptText))
            Clipboard.SetText(PromptText);
    }

    [RelayCommand]
    private void ClearAll()
    {
        SelectedTestCase     = null;
        SampleUserMessage    = string.Empty;
        ExpectedIntent       = "(not set)";
        DetectedIntent       = string.Empty;
        IntentMatchBadge     = "—";
        ExpectedFilesClasses = string.Empty;
        MustNotLeadWith      = string.Empty;
        MustMention          = string.Empty;
        MustNotMention       = string.Empty;
        PassRule             = string.Empty;
        FailRule             = string.Empty;
        ExpandedQueries        = string.Empty;
        PromptText             = string.Empty;
        ErrorMessage           = string.Empty;
        ResultStatus           = "—";
        ContextPolluted        = false;
        PollutedTermsSummary   = string.Empty;
        FilteredMemoryCount    = 0;
        IncludedMemoryCount    = 0;
        ContextQualityBadge    = string.Empty;
        ContextRetrievalStatus = string.Empty;
        AiResponse             = string.Empty;
        RunStatusMessage       = string.Empty;
        MustMentionStatus      = "—";
        MustNotMentionStatus   = "—";
        ExpectedFilesStatus    = "—";
        ProviderInfo           = string.Empty;
        RetrievedItems.Clear();
        OnPropertyChanged(nameof(PromptCharCount));
    }

    // ── Selection change ──────────────────────────────────────────────────

    partial void OnSelectedTestCaseChanged(GroundingTestCase? value)
    {
        // Clear previous build state
        DetectedIntent         = string.Empty;
        IntentMatchBadge       = "—";
        ExpandedQueries        = string.Empty;
        PromptText             = string.Empty;
        ErrorMessage           = string.Empty;
        ResultStatus           = "—";
        ContextPolluted        = false;
        PollutedTermsSummary   = string.Empty;
        FilteredMemoryCount    = 0;
        IncludedMemoryCount    = 0;
        ContextQualityBadge    = string.Empty;
        ContextRetrievalStatus = string.Empty;
        RetrievedItems.Clear();
        OnPropertyChanged(nameof(PromptCharCount));

        if (value is null)
        {
            SampleUserMessage    = string.Empty;
            ExpectedIntent       = "(not set)";
            ExpectedFilesClasses = string.Empty;
            MustNotLeadWith      = string.Empty;
            MustMention          = string.Empty;
            MustNotMention       = string.Empty;
            PassRule             = string.Empty;
            FailRule             = string.Empty;
            return;
        }

        // Populate all metadata from the selected test case
        SampleUserMessage    = value.UserMessage;
        ExpectedIntent       = value.ExpectedIntent;
        ExpectedFilesClasses = value.MustIncludeAnyDisplay;
        MustNotLeadWith      = value.MustNotLeadWithDisplay;
        MustMention          = value.MustMentionDisplay;
        MustNotMention       = value.MustNotMentionDisplay;
        PassRule             = value.PassRule;
        FailRule             = value.FailRule;
    }

    // ── Scoring logic ─────────────────────────────────────────────────────

    /// <summary>
    /// Scoring rules (Fix 3):
    ///   PASS    — intent ok AND must-include found AND no bad lead AND context is non-empty/ready.
    ///   WARNING — intent ok, no bad lead, but context is limited/empty (index not ready, no snippets).
    ///   WARNING — intent ok, no bad lead, context ready but no must-include term found.
    ///   FAIL    — intent mismatch OR bad lead (wrong context leading).
    /// </summary>
    private static string EvaluateScore(bool intentOk, bool hasMustInclude, bool badLead,
                                        bool hasProject, bool contextIsReady = false)
    {
        if (!hasProject)
            return intentOk ? "⚠️ WARNING — no project" : "❌ FAIL — no project + intent mismatch";

        if (!intentOk)
            return "❌ FAIL";

        if (badLead)
            return "❌ FAIL — wrong context leads";

        // PASS requires both correct terms AND real project context
        if (hasMustInclude && contextIsReady)
            return "✅ PASS";

        // Good terms found but index not ready / no snippets retrieved
        if (hasMustInclude && !contextIsReady)
            return "⚠️ WARNING — terms matched, context limited";

        return "⚠️ WARNING — intent ok, context limited";
    }

    private static string[] SplitTerms(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── Test Case Data — mirrors Docs/chat-grounding-test-matrix.md ──────

    private static IReadOnlyList<GroundingTestCase> BuildTestCases() =>
    [
        new() {
            Id             = "tc1",
            DisplayName    = "1 — Delete saved tickets",
            UserMessage    = "What do I have to do to delete tickets? What files are affected?",
            ExpectedIntent = "SavedTicketManagement",
            MustIncludeAny = "TicketsWorkspaceViewModel,TicketsWorkspaceView.xaml,ProjectTicket,TicketService,ProjectTickets",
            MustNotLeadWith= "DraftTicketDtos.cs,DraftTicket,CodebaseTicketGeneratorModels.cs,TicketController,TicketModel,Repository,Controller",
            MustMention    = "inspect DeleteTicketAsync,ArchiveTicketAsync,add if missing,tenant,project ownership,soft delete,archive,confirmation,refresh,SelectedTicket,ProjectImplementationPlans",
            MustNotMention = "Weaviate,TicketController,TicketModel,Repository,Controller,likely,possibly,typically",
            PassRule       = "References real IronDev files + covers all 7 checklist items (archive/delete, tenant guard, confirmation, command, list refresh, SelectedTicket clear, linked plan check). Context must be non-empty.",
            FailRule       = "DraftTicketDtos.cs cited, generic MVC terms used, or entirely hedged language with no IronDev-specific context.",
        },
        new() {
            Id             = "tc2",
            DisplayName    = "2 — Delete old chat sessions",
            UserMessage    = "What would I need to do to delete old chats from Chat History?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "ChatWorkspaceViewModel,ChatHistoryService,ProjectChatSessions,ChatMessages",
            MustNotLeadWith= "DraftTicket,TicketService",
            MustMention    = "session,tenant,archive",
            MustNotMention = "Weaviate",
            PassRule       = "Identifies actual chat session/message persistence areas with real class names.",
            FailRule       = "Answer only says 'update ChatService and database'.",
        },
        new() {
            Id             = "tc3",
            DisplayName    = "3 — Ticket list shows noisy markdown",
            UserMessage    = "The ticket list shows noisy markdown fragments. What should I change?",
            ExpectedIntent = "SavedTicketManagement",
            MustIncludeAny = "TicketsWorkspaceView.xaml,TicketsWorkspaceViewModel,ProjectTicket,MarkdownPreviewConverter,DataTemplate,TextTrimming",
            MustNotLeadWith= "DraftTicketService,database,schema,markdown parser,markdown-to-HTML,html conversion,storage,unrelated TicketService",
            MustMention    = "DataTemplate,ticket list,preview,strip,sanitise,TextTrimming,one-line",
            MustNotMention = "Weaviate,schema change",
            PassRule       = "Pass only if the answer identifies ticket row/list preview rendering as the problem and gives a concrete UI binding or converter fix (e.g. MarkdownPreviewConverter, TextTrimming, DataTemplate change in TicketsWorkspaceView.xaml).",
            FailRule       = "Fail if answer recommends a generic markdown parser library, markdown-to-HTML conversion, database schema change, or storage/retrieval change as the primary fix.",
        },
        new() {
            Id             = "tc4",
            DisplayName    = "4 — Dropdowns clipped",
            UserMessage    = "Status, priority and type dropdowns are clipped. They show 'Dr', 'Me', 'Tas'. What files should I fix?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "TicketsWorkspaceView.xaml,SelectionField,MinWidth",
            MustNotLeadWith= "TicketService,database,DraftTicket",
            MustMention    = "width,XAML,style",
            MustNotMention = "schema,Weaviate",
            PassRule       = "Points to ticket XAML / control style.",
            FailRule       = "Treats it as a data or model problem.",
        },
        new() {
            Id             = "tc5",
            DisplayName    = "5 — Chat answers are generic",
            UserMessage    = "Chat gives generic answers instead of real files. How do we fix grounding?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "PromptContextBuilder,CodeIndexService,GetRelevantSnippetsAsync,ChatWorkspaceViewModel",
            MustNotLeadWith= "Weaviate,embeddings",
            MustMention    = "IndexingStatus,retrieval,prompt",
            MustNotMention = "Weaviate",
            PassRule       = "Describes retrieval + prompt injection + index preflight.",
            FailRule       = "Says only 'make the prompt better' or 'add Weaviate'.",
        },
        new() {
            Id             = "tc6",
            DisplayName    = "6 — Draft tickets are generic",
            UserMessage    = "Draft ticket generation is weak and generic. How do we make it specific to IronDev?",
            ExpectedIntent = "DraftTicketFlow",
            MustIncludeAny = "DraftTicketService,DraftTicketDtos,PromptContextBuilder,CodeIndexService",
            MustNotLeadWith= "TicketsWorkspaceViewModel delete,schema",
            MustMention    = "context,affectedFiles,prompt",
            MustNotMention = "Weaviate,patch validation",
            PassRule       = "Focuses on DraftTicketService prompt/context improvement.",
            FailRule       = "Suggests unrelated UI changes only.",
        },
        new() {
            Id             = "tc7",
            DisplayName    = "7 — Create Ticket + Plan empty",
            UserMessage    = "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "TicketsWorkspaceViewModel,ImplementationPlansWorkspaceViewModel,ShellViewModel,ProjectImplementationPlan",
            MustNotLeadWith= "schema,Weaviate,LLM",
            MustMention    = "draft state,navigation,prefill",
            MustNotMention = "Weaviate",
            PassRule       = "Identifies draft state clearing / navigation callback as the risk.",
            FailRule       = "Only says 'check the plan service'.",
        },
        new() {
            Id             = "tc8",
            DisplayName    = "8 — Index Project First resume",
            UserMessage    = "When I click Index Project First, indexing runs but the draft ticket is not generated after Ready. What should be fixed?",
            ExpectedIntent = "DraftTicketFlow",
            MustIncludeAny = "TicketsWorkspaceViewModel,SetIndexStatus,IsDraftIndexing,ChatTicketContext",
            MustNotLeadWith= "Weaviate,schema",
            MustMention    = "pending context,Ready,resume",
            MustNotMention = "Weaviate",
            PassRule       = "Identifies pending context + SetIndexStatus(\"Ready\") propagation as the fix.",
            FailRule       = "Says only 'rerun indexing'.",
        },
        new() {
            Id             = "tc9",
            DisplayName    = "9 — Local LLM provider setup",
            UserMessage    = "How can another developer run IronDev with Ollama or a local LLM?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "LlmOptions,OllamaLlmService,LocalOpenAiCompatibleLlmService,App.xaml.cs,ILLMService",
            MustNotLeadWith= "TicketService,Weaviate",
            MustMention    = "Provider,BaseUrl,appsettings",
            MustNotMention = "Weaviate,hardcoded",
            PassRule       = "References provider files and config.",
            FailRule       = "Says 'set OPENAI_API_KEY only'.",
        },
        new() {
            Id             = "tc10",
            DisplayName    = "10 — Fresh local DB setup",
            UserMessage    = "What does a new developer need to do to set up the database and log in locally?",
            ExpectedIntent = "General",
            MustIncludeAny = "local_dev_setup.sql,rebuild_db.sql,local-development.md,README.md",
            MustNotLeadWith= "Weaviate,production",
            MustMention    = "IronDeveloper database,bob@irondev.local,dotnet build",
            MustNotMention = "Weaviate required,manual table edits",
            PassRule       = "Matches local setup docs and references real scripts.",
            FailRule       = "Says 'manually create tables' or omits local_dev_setup.sql.",
        },
    ];
}

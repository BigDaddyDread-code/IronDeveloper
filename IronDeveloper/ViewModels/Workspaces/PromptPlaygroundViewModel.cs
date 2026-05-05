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
    [ObservableProperty] private string _projectIndexStatus = string.Empty;
    [ObservableProperty] private string _contextQuality     = string.Empty;
    [ObservableProperty] private string _expandedQueries    = string.Empty;
    [ObservableProperty] private string _promptText         = string.Empty;
    [ObservableProperty] private string _errorMessage       = string.Empty;
    [ObservableProperty] private string _resultStatus       = string.Empty; // PASS / WARNING / FAIL / —
    [ObservableProperty] private bool   _isBuilding         = false;

    public ObservableCollection<RetrievedContextItem> RetrievedItems { get; } = new();
    public IReadOnlyList<GroundingTestCase>            TestCases      { get; } = BuildTestCases();

    // ── Constructor ───────────────────────────────────────────────────────

    public PromptPlaygroundViewModel(
        IPromptContextBuilder builder,
        IProjectService        projectService)
    {
        _builder        = builder;
        _projectService = projectService;
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

        IsBuilding    = true;
        ErrorMessage  = string.Empty;
        PromptText    = string.Empty;
        ResultStatus  = "—";
        RetrievedItems.Clear();

        try
        {
            // ── 1. Intent classification (pure static — no DB) ───────────────
            var intent = PromptContextBuilder.ClassifyIntent(question);
            DetectedIntent = intent.ToString();

            var expanded = PromptContextBuilder.ExpandSearchQueries(question, intent);
            ExpandedQueries = string.Join(", ", expanded);

            // ── 2. Initial intent badge ──────────────────────────────────────
            if (SelectedTestCase is not null && !string.IsNullOrWhiteSpace(SelectedTestCase.ExpectedIntent))
            {
                var intentOk = string.Equals(DetectedIntent, ExpectedIntent, StringComparison.OrdinalIgnoreCase);
                IntentMatchBadge = intentOk ? "✅ Match" : "⚠️ Mismatch";
            }
            else
            {
                IntentMatchBadge = "—";
            }

            // ── 3. Resolve active project ────────────────────────────────────
            var projects = await _projectService.GetProjectsAsync(ct);
            var project  = projects?.Count > 0 ? projects[0] : null;

            if (project is null)
            {
                ProjectIndexStatus = "No project";
                ContextQuality     = "Missing";
                PromptText = $"[No project loaded — intent data only]\n\nDetected intent: {DetectedIntent}\n\nExpanded queries:\n{ExpandedQueries}";
                ResultStatus = EvaluateScore(intentOk: string.Equals(DetectedIntent, ExpectedIntent, StringComparison.OrdinalIgnoreCase),
                                             hasMustInclude: false, badLead: false, hasProject: false);
                return;
            }

            ProjectIndexStatus = project.IndexingStatus ?? "Unknown";
            ContextQuality     = string.Equals(project.IndexingStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                                     ? "Indexed" : "Limited";

            // ── 4. Full prompt build ─────────────────────────────────────────
            var result = await _builder.BuildFullPromptForTestingAsync(project.Id, question, ct);
            PromptText = result.PromptText ?? string.Empty;

            foreach (var item in result.RetrievedItems)
            {
                RetrievedItems.Add(new RetrievedContextItem
                {
                    FilePath   = item.FilePath   ?? string.Empty,
                    SymbolName = item.SymbolName  ?? string.Empty,
                    Preview    = item.ChunkText is { Length: > 0 } s
                                     ? s[..Math.Min(100, s.Length)].Replace('\n', ' ')
                                     : string.Empty
                });
            }

            // ── 5. Score against selected test case ──────────────────────────
            if (SelectedTestCase is not null)
            {
                var intentOk      = string.Equals(DetectedIntent, ExpectedIntent, StringComparison.OrdinalIgnoreCase);
                var mustTerms     = SplitTerms(SelectedTestCase.MustIncludeAny);
                var mustNotTerms  = SplitTerms(SelectedTestCase.MustNotLeadWith);

                // Check expanded queries + retrieved files for must-include terms
                var allRetrieved = RetrievedItems.Select(i => i.FilePath + " " + i.SymbolName).ToList();
                allRetrieved.Add(ExpandedQueries);
                var hasMustInclude = mustTerms.Any(t =>
                    allRetrieved.Any(r => r.Contains(t, StringComparison.OrdinalIgnoreCase)));

                // Check if a must-not-lead-with term appears in the top file
                var topFile   = RetrievedItems.Count > 0 ? RetrievedItems[0].FilePath + " " + RetrievedItems[0].SymbolName : string.Empty;
                var badLead   = mustNotTerms.Any(t => topFile.Contains(t, StringComparison.OrdinalIgnoreCase));

                IntentMatchBadge = intentOk ? "✅ Match" : "⚠️ Mismatch";
                ResultStatus     = EvaluateScore(intentOk, hasMustInclude, badLead, hasProject: true);
            }
            else
            {
                ResultStatus = "—";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            ResultStatus = "—";
        }
        finally
        {
            IsBuilding = false;
        }
    }

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
        ExpandedQueries      = string.Empty;
        PromptText           = string.Empty;
        ErrorMessage         = string.Empty;
        ResultStatus         = "—";
        RetrievedItems.Clear();
    }

    // ── Selection change ──────────────────────────────────────────────────

    partial void OnSelectedTestCaseChanged(GroundingTestCase? value)
    {
        // Clear previous build state
        DetectedIntent   = string.Empty;
        IntentMatchBadge = "—";
        ExpandedQueries  = string.Empty;
        PromptText       = string.Empty;
        ErrorMessage     = string.Empty;
        ResultStatus     = "—";
        RetrievedItems.Clear();

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
    /// MVP scoring:
    ///   PASS    — intent matches AND at least one must-include term is present AND no bad lead.
    ///   WARNING — intent matches but no must-include term found (limited context).
    ///   FAIL    — intent mismatch OR bad-lead AND intent mismatch.
    /// </summary>
    private static string EvaluateScore(bool intentOk, bool hasMustInclude, bool badLead, bool hasProject)
    {
        if (!hasProject)
            return intentOk ? "⚠️ WARNING — no project" : "❌ FAIL — no project + intent mismatch";

        if (!intentOk)
            return "❌ FAIL";

        if (badLead)
            return "❌ FAIL — wrong context leads";

        if (hasMustInclude)
            return "✅ PASS";

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
            MustIncludeAny = "TicketsWorkspaceViewModel,TicketsWorkspaceView.xaml,ProjectTicket,TicketService",
            MustNotLeadWith= "DraftTicketDtos.cs,DraftTicket,CodebaseTicketGeneratorModels.cs",
            MustMention    = "soft delete,tenant,confirmation",
            MustNotMention = "Weaviate",
            PassRule       = "References real saved-ticket files/classes. Avoids treating DraftTicket as the saved ticket model.",
            FailRule       = "DraftTicketDtos.cs is the primary affected file cited.",
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
            MustIncludeAny = "TicketsWorkspaceView.xaml,DataTemplate,TextTrimming",
            MustNotLeadWith= "DraftTicketService,database,schema",
            MustMention    = "DataTemplate,Title,summary",
            MustNotMention = "Weaviate,schema change",
            PassRule       = "Focuses on XAML list template / converter fix.",
            FailRule       = "Recommends changing a model or database first.",
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

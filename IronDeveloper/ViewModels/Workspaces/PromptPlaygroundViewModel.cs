using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// One entry in the Grounding Test Case dropdown.
/// </summary>
public sealed class GroundingTestCase
{
    public string Id             { get; init; } = string.Empty;
    public string DisplayName    { get; init; } = string.Empty;
    public string SampleQuestion { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string MustIncludeAny { get; init; } = string.Empty;
    public string MustNotLeadWith{ get; init; } = string.Empty;
    public string PassRule        { get; init; } = string.Empty;
}

/// <summary>
/// Developer-only read-only Prompt Playground.
///
/// Allows inspection of exactly what PromptContextBuilder produces for a
/// sample user message — without triggering a live LLM call.
///
/// Features:
///   - Free-text user message OR selection from the 10 grounding test cases.
///   - Detected intent vs expected intent with PASS / WARNING / FAIL badge.
///   - Expanded search queries from ExpandSearchQueries.
///   - Full generated prompt (read-only, monospaced).
///   - Copy Prompt to clipboard.
/// </summary>
public sealed partial class PromptPlaygroundViewModel : ObservableObject
{
    private readonly IPromptContextBuilder _builder;
    private readonly IProjectService        _projectService;

    // ── Observable Properties ─────────────────────────────────────────────

    [ObservableProperty] private string _sampleUserMessage = string.Empty;
    [ObservableProperty] private GroundingTestCase? _selectedTestCase;
    [ObservableProperty] private string _detectedIntent  = string.Empty;
    [ObservableProperty] private string _expectedIntent  = string.Empty;
    [ObservableProperty] private string _intentMatchBadge = string.Empty;  // PASS / WARNING / FAIL
    [ObservableProperty] private string _projectIndexStatus = string.Empty;
    [ObservableProperty] private string _contextQuality     = string.Empty;
    [ObservableProperty] private string _expandedQueries    = string.Empty;
    [ObservableProperty] private string _promptText         = string.Empty;
    [ObservableProperty] private string _errorMessage       = string.Empty;
    [ObservableProperty] private bool   _isBuilding         = false;

    public ObservableCollection<RetrievedContextItem> RetrievedItems { get; } = new();
    public IReadOnlyList<GroundingTestCase> TestCases { get; }     = BuildTestCases();

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

        IsBuilding   = true;
        ErrorMessage = string.Empty;
        PromptText   = string.Empty;
        RetrievedItems.Clear();

        try
        {
            // ── Intent classification (no DB call) ───────────────────────
            var intent = PromptContextBuilder.ClassifyIntent(question);
            DetectedIntent = intent.ToString();

            var expanded = PromptContextBuilder.ExpandSearchQueries(question, intent);
            ExpandedQueries = string.Join(", ", expanded);

            // ── Match check against test case ────────────────────────────
            if (SelectedTestCase is not null && !string.IsNullOrWhiteSpace(SelectedTestCase.ExpectedIntent))
            {
                ExpectedIntent = SelectedTestCase.ExpectedIntent;
                var matches    = string.Equals(DetectedIntent, ExpectedIntent, System.StringComparison.OrdinalIgnoreCase);
                IntentMatchBadge = matches ? "✅ PASS" : "⚠️ MISMATCH";
            }
            else
            {
                ExpectedIntent   = "(not set)";
                IntentMatchBadge = "—";
            }

            // ── Resolve active project for context build ─────────────────
            var projects  = await _projectService.GetProjectsAsync(ct);
            var project   = projects?.Count > 0 ? projects[0] : null;

            if (project is null)
            {
                ProjectIndexStatus = "No project";
                ContextQuality     = "Missing";

                // Still show the intent + expansion even without a project
                PromptText   = $"[No project loaded — showing intent data only]\n\nDetected intent: {DetectedIntent}\n\nExpanded queries:\n{ExpandedQueries}";
                IntentMatchBadge = SelectedTestCase is not null
                    ? (string.Equals(DetectedIntent, SelectedTestCase.ExpectedIntent, System.StringComparison.OrdinalIgnoreCase) ? "✅ PASS" : "⚠️ MISMATCH")
                    : "—";
                return;
            }

            ProjectIndexStatus = project.IndexingStatus ?? "Unknown";
            ContextQuality     = project.IndexingStatus == "Ready" ? "Indexed" : "Limited";

            // ── Full prompt via PromptContextBuilder ─────────────────────
            var result = await _builder.BuildFullPromptForTestingAsync(project.Id, question, ct);
            PromptText = result.PromptText ?? string.Empty;

            // Populate retrieved context list
            foreach (var item in result.RetrievedItems)
            {
                RetrievedItems.Add(new RetrievedContextItem
                {
                    FilePath   = item.FilePath   ?? string.Empty,
                    SymbolName = item.SymbolName  ?? string.Empty,
                    Preview    = item.ChunkText   is { Length: > 0 } s
                                     ? s[..System.Math.Min(80, s.Length)].Replace('\n', ' ')
                                     : string.Empty
                });
            }

            // ── Final badge with MustNotLeadWith guard ───────────────────
            if (SelectedTestCase is not null)
            {
                var mustNot = SelectedTestCase.MustNotLeadWith?.Split(',') ?? System.Array.Empty<string>();
                var topFile = RetrievedItems.Count > 0 ? RetrievedItems[0].FilePath : string.Empty;
                var badLead = System.Array.Exists(mustNot, t => topFile.Contains(t.Trim(), System.StringComparison.OrdinalIgnoreCase));
                var intentOk = string.Equals(DetectedIntent, SelectedTestCase.ExpectedIntent, System.StringComparison.OrdinalIgnoreCase);

                IntentMatchBadge = (!intentOk || badLead) ? "❌ FAIL" : "✅ PASS";
            }
        }
        catch (System.Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
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

    // ── Selection change ──────────────────────────────────────────────────

    partial void OnSelectedTestCaseChanged(GroundingTestCase? value)
    {
        if (value is null) return;
        SampleUserMessage = value.SampleQuestion;
        ExpectedIntent    = value.ExpectedIntent;
        IntentMatchBadge  = "—";
        DetectedIntent    = string.Empty;
        ExpandedQueries   = string.Empty;
        PromptText        = string.Empty;
        RetrievedItems.Clear();
    }

    // ── Test Case Data ────────────────────────────────────────────────────

    private static IReadOnlyList<GroundingTestCase> BuildTestCases() =>
    [
        new() {
            Id             = "tc1",
            DisplayName    = "1 — Delete saved tickets",
            SampleQuestion = "What do I have to do to delete tickets? What files are affected?",
            ExpectedIntent = "SavedTicketManagement",
            MustIncludeAny = "TicketsWorkspaceViewModel,TicketService,ProjectTicket",
            MustNotLeadWith= "DraftTicketDtos,CodebaseTicketGeneratorModels",
            PassRule       = "SavedTicketManagement; TicketsWorkspaceViewModel ranked first."
        },
        new() {
            Id             = "tc2",
            DisplayName    = "2 — Delete old chat sessions",
            SampleQuestion = "What would I need to do to delete old chats from Chat History?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "ChatWorkspaceViewModel,ChatHistoryService,ProjectChatSessions",
            MustNotLeadWith= "DraftTicketDtos",
            PassRule       = "CodeQuery; ChatWorkspaceViewModel or ChatHistoryService retrieved."
        },
        new() {
            Id             = "tc3",
            DisplayName    = "3 — Ticket list shows noisy markdown",
            SampleQuestion = "The ticket list shows noisy markdown fragments. What should I change?",
            ExpectedIntent = "SavedTicketManagement",
            MustIncludeAny = "TicketsWorkspaceView,DataTemplate",
            MustNotLeadWith= "DraftTicketService,schema",
            PassRule       = "SavedTicketManagement; TicketsWorkspaceView.xaml in top results."
        },
        new() {
            Id             = "tc4",
            DisplayName    = "4 — Dropdowns clipped",
            SampleQuestion = "Status, priority and type dropdowns are clipped. They show Dr, Me, Tas. What files should I fix?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "TicketsWorkspaceView,SelectionField",
            MustNotLeadWith= "DraftTicket,schema",
            PassRule       = "CodeQuery; TicketsWorkspaceView.xaml in results."
        },
        new() {
            Id             = "tc5",
            DisplayName    = "5 — Chat answers are generic",
            SampleQuestion = "Chat gives generic answers instead of real files. How do we fix grounding?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "PromptContextBuilder,CodeIndexService",
            MustNotLeadWith= "Weaviate",
            PassRule       = "CodeQuery; PromptContextBuilder in results."
        },
        new() {
            Id             = "tc6",
            DisplayName    = "6 — Draft tickets are generic",
            SampleQuestion = "Draft ticket generation is weak and generic. How do we make it specific to IronDev?",
            ExpectedIntent = "DraftTicketFlow",
            MustIncludeAny = "DraftTicketService,DraftTicketDtos",
            MustNotLeadWith= "schema,TicketsWorkspaceViewModel delete",
            PassRule       = "DraftTicketFlow; DraftTicketService in top results."
        },
        new() {
            Id             = "tc7",
            DisplayName    = "7 — Create Ticket + Plan empty",
            SampleQuestion = "Create Ticket + Plan opens the plan screen, but the plan fields are empty. What should we check?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "TicketsWorkspaceViewModel,ImplementationPlansWorkspaceViewModel,ShellViewModel",
            MustNotLeadWith= "schema,Weaviate",
            PassRule       = "CodeQuery; TicketsWorkspaceViewModel and ImplementationPlansWorkspaceViewModel in results."
        },
        new() {
            Id             = "tc8",
            DisplayName    = "8 — Index Project First resume",
            SampleQuestion = "When I click Index Project First, indexing runs but the draft ticket is not generated after Ready. What should be fixed?",
            ExpectedIntent = "DraftTicketFlow",
            MustIncludeAny = "TicketsWorkspaceViewModel,SetIndexStatus,IsDraftIndexing",
            MustNotLeadWith= "Weaviate,schema",
            PassRule       = "DraftTicketFlow; pending context / SetIndexStatus logic in results."
        },
        new() {
            Id             = "tc9",
            DisplayName    = "9 — Local LLM provider setup",
            SampleQuestion = "How can another developer run IronDev with Ollama or a local LLM?",
            ExpectedIntent = "CodeQuery",
            MustIncludeAny = "LlmOptions,OllamaLlmService,ILLMService",
            MustNotLeadWith= "TicketService,Weaviate",
            PassRule       = "CodeQuery; LlmOptions or OllamaLlmService in results."
        },
        new() {
            Id             = "tc10",
            DisplayName    = "10 — Fresh local DB setup",
            SampleQuestion = "What does a new developer need to do to set up the database and log in locally?",
            ExpectedIntent = "General",
            MustIncludeAny = "local_dev_setup,local-development,README",
            MustNotLeadWith= "Weaviate,production",
            PassRule       = "General; local_dev_setup.sql or docs in results."
        },
    ];
}

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class SettingsWorkspaceViewModel : ObservableObject
{
    private readonly ILlmTraceService _traceService;

    [ObservableProperty] private string _selectedModel      = "gpt-4o";
    [ObservableProperty] private string _apiEndpoint        = "https://api.openai.com/v1";
    [ObservableProperty] private bool   _streamResponses    = true;
    [ObservableProperty] private bool   _autoIndex          = false;
    [ObservableProperty] private int    _maxContextTokens   = 8000;
    [ObservableProperty] private bool   _isDevToolsExpanded = false;

    /// <summary>
    /// When true, the Chat workspace uses the ContextAgentService pipeline
    /// (sufficiency check + code expansion) instead of one-shot RAG.
    /// Default: false — must be explicitly enabled. Safe to toggle at runtime.
    /// </summary>
    [ObservableProperty] private bool _useContextAgent = false;

    // ── Trace LLM Calls ───────────────────────────────────────────────────
    // Wraps ILlmTraceService.IsTracingEnabled so the Settings page can toggle
    // it. No persistence layer yet — the service defaults to true on startup.

    /// <summary>
    /// Master on/off for LLM tracing. Reads and writes through to
    /// <see cref="ILlmTraceService.IsTracingEnabled"/>.
    /// </summary>
    public bool IsLlmTracingEnabled
    {
        get => _traceService.IsTracingEnabled;
        set
        {
            if (_traceService.IsTracingEnabled == value) return;
            _traceService.IsTracingEnabled = value;
            OnPropertyChanged();
        }
    }

    // ── Lazy Dev Tools ────────────────────────────────────────────────────
    //
    // PromptPlaygroundViewModel pulls in IPromptContextBuilder → IChatFeedbackService
    // → SqlConnectionFactory, so it MUST NOT be resolved at app startup.
    //
    // The factory is wired by App.xaml.cs. The VM is created only the first time
    // the user expands Developer Tools. WPF is notified via OnPropertyChanged so
    // the DataContext binding on PromptPlaygroundView updates correctly.

    /// <summary>Deferred factory — set by App.xaml.cs DI registration.</summary>
    public Func<PromptPlaygroundViewModel>? PromptPlaygroundFactory { get; init; }
    public Func<LlmConsoleViewModel>?       LlmConsoleFactory       { get; init; }

    private PromptPlaygroundViewModel? _promptPlayground;
    private LlmConsoleViewModel?       _llmConsole;

    public PromptPlaygroundViewModel? PromptPlayground
    {
        get => _promptPlayground;
        private set => SetProperty(ref _promptPlayground, value);
    }

    public LlmConsoleViewModel? LlmConsole
    {
        get => _llmConsole;
        private set => SetProperty(ref _llmConsole, value);
    }

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "claude-3-haiku",
        "gemini-2.5-pro"
    ];

    public SettingsWorkspaceViewModel(ILlmTraceService traceService)
    {
        _traceService = traceService;
    }

    [RelayCommand]
    private void ToggleDevTools()
    {
        if (!IsDevToolsExpanded)
        {
            if (PromptPlayground is null && PromptPlaygroundFactory is not null)
                PromptPlayground = PromptPlaygroundFactory();

            if (LlmConsole is null && LlmConsoleFactory is not null)
                LlmConsole = LlmConsoleFactory();
        }

        IsDevToolsExpanded = !IsDevToolsExpanded;
    }
}

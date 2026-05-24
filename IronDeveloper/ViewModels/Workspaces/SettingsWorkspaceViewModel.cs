using IronDev.Client.Chat;
using IronDev.Client.CodeIndex;
using IronDev.Client.Memory;
using IronDev.Client.Projects;
using IronDev.Client.Tickets;
using IronDev.Client.Traces;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using IronDev.Agent.Services;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class SettingsWorkspaceViewModel : ObservableObject
{
    private readonly ITraceApiClient _traceService;
    private readonly IAppSettingsService? _settingsService;
    private bool _isLoadingSettings;

    [ObservableProperty] private string _selectedModel      = "gpt-4o";
    [ObservableProperty] private string _apiEndpoint        = "https://api.openai.com/v1";
    [ObservableProperty] private bool   _streamResponses    = true;
    [ObservableProperty] private bool   _autoIndex          = false;
    [ObservableProperty] private int    _maxContextTokens   = 8000;

    /// <summary>
    /// When true, the Chat workspace uses the ContextAgentService pipeline
    /// (sufficiency check + code expansion) instead of one-shot RAG.
    /// Default: false — must be explicitly enabled. Safe to toggle at runtime.
    /// </summary>
    [ObservableProperty] private bool _useContextAgent = false;

    // ── Trace LLM Calls ───────────────────────────────────────────────────
    // Wraps ITraceApiClient.IsTracingEnabled so the Settings page can toggle
    // it. No persistence layer yet — the service defaults to true on startup.

    /// <summary>
    /// Master on/off for LLM tracing. Reads and writes through to
    /// <see cref="ITraceApiClient.IsTracingEnabled"/>.
    /// </summary>
    public bool IsLlmTracingEnabled
    {
        get => _traceService.IsTracingEnabled;
        set
        {
            if (_traceService.IsTracingEnabled == value) return;
            _traceService.IsTracingEnabled = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "claude-3-haiku",
        "gemini-2.5-pro"
    ];

    public string ProductDisplayName => AppBuildInfo.DisplayName;
    public string ProductVersion => AppBuildInfo.Version;
    public string ProductWorkflowName => AppBuildInfo.WorkflowName;
    public string ProductSafetyModel => AppBuildInfo.SafetyModel;
    public string AlphaScopeSummary =>
        "Create or import a project, profile it, index it, chat with context, create tickets, review proposals, and inspect trace output.";
    public string AlphaLimitations =>
        "No autonomous writes by default, no auto-fix loop, no Git or PR automation, and sandbox apply only for explicitly safe projects.";
    public string AlphaTesterPrompt =>
        "Point IronDev at a small repo, create one build-ready ticket from chat, generate a proposal, review the diff, and inspect the trace.";

    public SettingsWorkspaceViewModel(ITraceApiClient traceService, IAppSettingsService? settingsService = null)
    {
        _traceService = traceService;
        _settingsService = settingsService;
        LoadPersistedSettings();
    }

    public SettingsWorkspaceViewModel(object traceService, IAppSettingsService? settingsService = null)
        : this(global::IronDev.Agent.Services.BoundaryCompatibility.Trace(traceService), settingsService)
    {
    }

    private void LoadPersistedSettings()
    {
        if (_settingsService == null)
            return;

        _isLoadingSettings = true;
        try
        {
            var settings = _settingsService.Current;
            SelectedModel = settings.SelectedModel;
            ApiEndpoint = settings.ApiEndpoint;
            StreamResponses = settings.StreamResponses;
            AutoIndex = settings.AutoIndex;
            MaxContextTokens = settings.MaxContextTokens;
            UseContextAgent = settings.UseContextAgent;
            RequireBuilderApplyApproval = settings.RequireBuilderApplyApproval;
            _traceService.IsTracingEnabled = settings.IsLlmTracingEnabled;
            OnPropertyChanged(nameof(IsLlmTracingEnabled));
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    [ObservableProperty] private bool _requireBuilderApplyApproval = true;

    partial void OnSelectedModelChanged(string value) => SaveSettings();
    partial void OnApiEndpointChanged(string value) => SaveSettings();
    partial void OnStreamResponsesChanged(bool value) => SaveSettings();
    partial void OnAutoIndexChanged(bool value) => SaveSettings();
    partial void OnMaxContextTokensChanged(int value) => SaveSettings();
    partial void OnUseContextAgentChanged(bool value) => SaveSettings();
    partial void OnRequireBuilderApplyApprovalChanged(bool value) => SaveSettings();

    private void SaveSettings()
    {
        if (_isLoadingSettings || _settingsService == null)
            return;

        var settings = _settingsService.Current;
        settings.SelectedModel = SelectedModel;
        settings.ApiEndpoint = ApiEndpoint;
        settings.StreamResponses = StreamResponses;
        settings.AutoIndex = AutoIndex;
        settings.MaxContextTokens = MaxContextTokens;
        settings.UseContextAgent = UseContextAgent;
        settings.IsLlmTracingEnabled = _traceService.IsTracingEnabled;
        settings.RequireBuilderApplyApproval = RequireBuilderApplyApproval;

        _ = _settingsService.SaveAsync();
    }
}

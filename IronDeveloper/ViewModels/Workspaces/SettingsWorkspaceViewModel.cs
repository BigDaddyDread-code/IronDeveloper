using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class SettingsWorkspaceViewModel : ObservableObject
{
    [ObservableProperty] private string _selectedModel      = "gpt-4o";
    [ObservableProperty] private string _apiEndpoint        = "https://api.openai.com/v1";
    [ObservableProperty] private bool   _streamResponses    = true;
    [ObservableProperty] private bool   _autoIndex          = false;
    [ObservableProperty] private int    _maxContextTokens   = 8000;
    [ObservableProperty] private bool   _isDevToolsExpanded = false;

    // ── Lazy Prompt Playground ────────────────────────────────────────────────
    //
    // PromptPlaygroundViewModel pulls in IPromptContextBuilder → IChatFeedbackService
    // → SqlConnectionFactory, so it MUST NOT be resolved at app startup.
    //
    // The factory is wired by App.xaml.cs. The VM is created only the first time
    // the user expands Developer Tools. WPF is notified via OnPropertyChanged so
    // the DataContext binding on PromptPlaygroundView updates correctly.

    /// <summary>
    /// Deferred factory — set by App.xaml.cs DI registration.
    /// Never resolved until the user opens Developer Tools.
    /// </summary>
    public Func<PromptPlaygroundViewModel>? PromptPlaygroundFactory { get; init; }

    private PromptPlaygroundViewModel? _promptPlayground;

    /// <summary>
    /// The Prompt Playground VM, created on first access.
    /// Raises PropertyChanged so WPF DataContext bindings refresh.
    /// </summary>
    public PromptPlaygroundViewModel? PromptPlayground
    {
        get => _promptPlayground;
        private set => SetProperty(ref _promptPlayground, value);
    }

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "claude-3-haiku",
        "gemini-2.5-pro"
    ];

    [RelayCommand]
    private void ToggleDevTools()
    {
        if (!IsDevToolsExpanded && PromptPlayground is null && PromptPlaygroundFactory is not null)
        {
            // Lazy-create, then notify WPF before flipping Visibility
            PromptPlayground = PromptPlaygroundFactory();
        }

        IsDevToolsExpanded = !IsDevToolsExpanded;
    }
}

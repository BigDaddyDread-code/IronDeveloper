using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class SettingsWorkspaceViewModel : ObservableObject
{
    [ObservableProperty] private string _selectedModel    = "gpt-4o";
    [ObservableProperty] private string _apiEndpoint      = "https://api.openai.com/v1";
    [ObservableProperty] private bool   _streamResponses  = true;
    [ObservableProperty] private bool   _autoIndex        = false;
    [ObservableProperty] private int    _maxContextTokens = 8000;
    [ObservableProperty] private bool   _isDevToolsExpanded = false;

    /// <summary>
    /// Deferred factory for the Prompt Playground VM.
    /// Resolved lazily on first expand so that SqlConnectionFactory
    /// is never touched until the user explicitly opens Developer Tools.
    /// </summary>
    public Func<PromptPlaygroundViewModel>? PromptPlaygroundFactory { get; init; }

    private PromptPlaygroundViewModel? _promptPlayground;

    /// <summary>The Playground VM — created on first access.</summary>
    public PromptPlaygroundViewModel? PromptPlayground
    {
        get
        {
            if (_promptPlayground is null && PromptPlaygroundFactory is not null)
                _promptPlayground = PromptPlaygroundFactory();
            return _promptPlayground;
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

    [RelayCommand]
    private void ToggleDevTools()
    {
        // Trigger lazy creation of the Playground VM before we show it
        if (!IsDevToolsExpanded)
            _ = PromptPlayground; // ensures VM is instantiated before binding fires
        IsDevToolsExpanded = !IsDevToolsExpanded;
    }
}

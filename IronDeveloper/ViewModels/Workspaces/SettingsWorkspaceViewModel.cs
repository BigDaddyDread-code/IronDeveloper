using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class SettingsWorkspaceViewModel : ObservableObject
{
    [ObservableProperty] private string _selectedModel = "gpt-4o";
    [ObservableProperty] private string _apiEndpoint   = "https://api.openai.com/v1";
    [ObservableProperty] private bool   _streamResponses = true;
    [ObservableProperty] private bool   _autoIndex       = false;
    [ObservableProperty] private int    _maxContextTokens = 8000;

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "claude-3-haiku",
        "gemini-2.5-pro"
    ];
}

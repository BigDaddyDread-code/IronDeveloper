using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class ChatWorkspaceViewModel : ObservableObject
{
    private readonly IChatShellService _chatService;

    [ObservableProperty] private ObservableCollection<ChatSummary> _messages = [];
    [ObservableProperty] private string _promptText = string.Empty;
    [ObservableProperty] private bool   _isBusy;

    public ChatWorkspaceViewModel(IChatShellService chatService)
    {
        _chatService = chatService;

        foreach (var m in _chatService.GetMessages())
            Messages.Add(m);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SendMessageAsync()
    {
        var text = PromptText.Trim();
        if (string.IsNullOrEmpty(text) || IsBusy) return;

        PromptText = string.Empty;
        Messages.Add(new ChatSummary { Role = "user", Content = text });

        IsBusy = true;
        await System.Threading.Tasks.Task.Delay(1200); // Fake LLM latency
        Messages.Add(new ChatSummary
        {
            Role    = "assistant",
            Content = "(Mock) This is a placeholder response. Real LLM integration comes in a later sprint."
        });
        IsBusy = false;
    }
}

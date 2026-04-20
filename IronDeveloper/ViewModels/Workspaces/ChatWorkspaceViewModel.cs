using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

using IronDev.AI;
using IronDev.Core;
using System.Linq;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class ChatWorkspaceViewModel : ObservableObject
{
    private readonly IChatShellService _chatService;
    private readonly IPromptContextBuilder _promptContextBuilder;
    private readonly ILLMService _llmService;

    private int _activeProjectId;
    private string _activeProjectName = string.Empty;

    [ObservableProperty] private ObservableCollection<ChatSummary> _messages = [];
    [ObservableProperty] private string _promptText = string.Empty;
    [ObservableProperty] private bool   _isBusy;

    public ChatWorkspaceViewModel(
        IChatShellService chatService,
        IPromptContextBuilder promptContextBuilder,
        ILLMService llmService)
    {
        _chatService = chatService;
        _promptContextBuilder = promptContextBuilder;
        _llmService = llmService;

        foreach (var m in _chatService.GetMessages())
            Messages.Add(m);
    }

    public System.Threading.Tasks.Task LoadAsync(global::IronDev.Data.Models.Project project)
    {
        _activeProjectId = project.Id;
        _activeProjectName = project.Name;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SendMessageAsync()
    {
        var text = PromptText.Trim();
        if (string.IsNullOrEmpty(text) || IsBusy) return;

        PromptText = string.Empty;
        Messages.Add(new ChatSummary { Role = "user", Content = text });

        IsBusy = true;
        
        try
        {
            var projectId = _activeProjectId;
            var sessionId = System.Guid.NewGuid(); // using a single session for now

            var packet = await _promptContextBuilder.BuildPacketAsync(projectId, sessionId, text);

            var contextStr = string.Empty;
            if (packet.Snippets.Count > 0 || packet.Tickets.Count > 0 || packet.Decisions.Count > 0)
            {
                contextStr = $"[Context Used: {packet.Snippets.Count} snippets, {packet.Tickets.Count} tickets, {packet.Decisions.Count} decisions]\n\n";
            }

            // Using the real LLM service (could be Mock/Fake if that's what is injected)
            // But since V1 asks for just grounding and local retrieval, we'll format the Mock explicitly if Fake is used
            string responseText;
            if (_llmService is Infrastructure.Services.FakeLlmService)
            {
                await System.Threading.Tasks.Task.Delay(1200); // Fake LLM latency
                responseText = $"{contextStr}(Mock) Here is the requested info grounded in {_activeProjectName}:\n\n";
                if (packet.Snippets.Count > 0) responseText += $"Snippets:\n{string.Join("\n", packet.Snippets.Select(s => s.Substring(0, System.Math.Min(s.Length, 100)) + "..."))}\n";
            }
            else
            {
                responseText = contextStr + await _llmService.GetResponseAsync(packet.FormattedPrompt);
            }

            Messages.Add(new ChatSummary
            {
                Role    = "assistant",
                Content = responseText
            });
        }
        finally
        {
            IsBusy = false;
        }
    }
}

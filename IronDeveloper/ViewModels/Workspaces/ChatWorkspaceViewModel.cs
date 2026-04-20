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

            // Call the real LLM service with the formatted prompt
            var responseText = string.Empty;
            try
            {
                responseText = await _llmService.GetResponseAsync(packet.FormattedPrompt);
            }
            catch (System.Exception ex)
            {
                responseText = $"[LLM Error]: {ex.Message}";
            }

            Messages.Add(new ChatSummary
            {
                Role = "assistant",
                Content = responseText,
                ContextPacket = packet
            });
        }
        finally
        {
            IsBusy = false;
        }
    }
}

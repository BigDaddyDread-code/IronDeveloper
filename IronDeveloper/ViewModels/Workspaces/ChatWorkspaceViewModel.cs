using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

using IronDev.AI;
using IronDev.Core;

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

    /// <summary>
    /// Callback invoked to create a ticket from a chat response.
    /// Set by ShellViewModel when wiring navigation.
    /// Args: title, summary, linkedFilePaths, linkedSymbols
    /// </summary>
    public Action<string, string, string?, string?>? OnCreateTicketFromChat { get; set; }

    /// <summary>
    /// Callback invoked to create a decision from a chat response.
    /// Set by ShellViewModel when wiring navigation.
    /// Args: title, detail, linkedFilePaths, linkedSymbols
    /// </summary>
    public Action<string, string, string?, string?>? OnCreateDecisionFromChat { get; set; }

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
        Messages.Add(new ChatSummary { Role = "user", MessageText = text });

        IsBusy = true;
        
        try
        {
            var projectId = _activeProjectId;
            var sessionId = Guid.NewGuid(); // using a single session for now

            var packet = await _promptContextBuilder.BuildPacketAsync(projectId, sessionId, text);

            // Call the real LLM service with the formatted prompt
            var responseText = string.Empty;
            try
            {
                responseText = await _llmService.GetResponseAsync(packet.FormattedPrompt);
            }
            catch (Exception ex)
            {
                responseText = $"[LLM Error]: {ex.Message}";
            }

            Messages.Add(new ChatSummary
            {
                Role = "assistant",
                MessageText = responseText,
                FormattedPrompt = packet.FormattedPrompt,
                ContextPacket = packet
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CreateTicket(ChatSummary message)
    {
        if (message == null || OnCreateTicketFromChat == null) return;

        // Build a title from the user's question (find the preceding user message)
        var idx = Messages.IndexOf(message);
        var userQuestion = "Chat-generated ticket";
        if (idx > 0 && Messages[idx - 1].Role == "user")
        {
            var q = Messages[idx - 1].MessageText;
            userQuestion = q.Length > 80 ? q.Substring(0, 80) + "..." : q;
        }

        // Use the response as the summary
        var summary = message.MessageText;
        if (summary.Length > 2000)
            summary = summary.Substring(0, 2000) + "\n...[truncated]";

        // Extract linked context from the context packet
        string? linkedFilePaths = null;
        string? linkedSymbols = null;
        if (message.ContextPacket != null)
        {
            if (message.ContextPacket.MatchedFilePaths.Count > 0)
                linkedFilePaths = string.Join("\n", message.ContextPacket.MatchedFilePaths);
            if (message.ContextPacket.MatchedSymbols.Count > 0)
                linkedSymbols = string.Join("\n", message.ContextPacket.MatchedSymbols);
        }

        OnCreateTicketFromChat.Invoke(userQuestion, summary, linkedFilePaths, linkedSymbols);
    }

    [RelayCommand]
    private void SaveDecision(ChatSummary message)
    {
        if (message == null || OnCreateDecisionFromChat == null) return;

        // Build a title from the user's question (find the preceding user message)
        var idx = Messages.IndexOf(message);
        var userQuestion = "Chat-generated decision";
        if (idx > 0 && Messages[idx - 1].Role == "user")
        {
            var q = Messages[idx - 1].MessageText;
            userQuestion = q.Length > 80 ? q.Substring(0, 80) + "..." : q;
        }

        // Use the response as the detail
        var detail = message.MessageText;
        if (detail.Length > 2000)
            detail = detail.Substring(0, 2000) + "\n...[truncated]";

        // Extract linked context from the context packet
        string? linkedFilePaths = null;
        string? linkedSymbols = null;
        if (message.ContextPacket != null)
        {
            if (message.ContextPacket.MatchedFilePaths.Count > 0)
                linkedFilePaths = string.Join("\n", message.ContextPacket.MatchedFilePaths);
            if (message.ContextPacket.MatchedSymbols.Count > 0)
                linkedSymbols = string.Join("\n", message.ContextPacket.MatchedSymbols);
        }

        OnCreateDecisionFromChat.Invoke(userQuestion, detail, linkedFilePaths, linkedSymbols);
    }
}

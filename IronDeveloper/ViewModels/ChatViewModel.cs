using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using IronDev.Agent.Models;
using IronDev.Services;
using IronDev.AI;
using IronDev.Core;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatHistoryService _chatHistoryService;
    private readonly IPromptContextBuilder _promptContextBuilder;
    private readonly ILLMService _llmService;
    private readonly IProjectMemoryService _projectMemoryService;

    private int _projectId = 1;
    private Guid _sessionId = Guid.NewGuid();

    [ObservableProperty]
    private string _promptText = string.Empty;

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    public ChatViewModel(
        IChatHistoryService chatHistoryService,
        IPromptContextBuilder promptContextBuilder,
        ILLMService llmService,
        IProjectMemoryService projectMemoryService)
    {
        _chatHistoryService = chatHistoryService;
        _promptContextBuilder = promptContextBuilder;
        _llmService = llmService;
        _projectMemoryService = projectMemoryService;
    }

    [RelayCommand]
    public async Task LoadChatAsync()
    {
        var recent = await _chatHistoryService.GetRecentMessagesAsync(_projectId, _sessionId, 50);
        Messages.Clear();
        foreach (var m in recent)
        {
            Messages.Add(new ChatMessageItem { Role = m.Role, Message = m.Message, CreatedDate = m.CreatedDate });
        }
    }

    [RelayCommand]
    public async Task GenerateTicketAsync()
    {
        WeakReferenceMessenger.Default.Send(new StatusMessage("Generating ticket..."));
        try
        {
            var summary = await _promptContextBuilder.BuildAsync(_projectId, _sessionId,
                "Generate a precise engineering ticket for the work currently being discussed.");

            var prompt = $@"
Based on the following project context and recent conversation, generate a structured engineering ticket.

Return ONLY a JSON object with exactly these fields (no markdown, no code fences):
{{
  ""title"": ""Short descriptive title (max 80 chars)"",
  ""ticketType"": ""Task"" or ""Bug"" or ""Feature"" or ""Improvement"",
  ""priority"": ""Low"" or ""Medium"" or ""High"" or ""Critical"",
  ""summary"": ""One-paragraph summary of the ticket"",
  ""background"": ""Context and background for why this work is needed"",
  ""problem"": ""Specific problem statement or user need"",
  ""acceptanceCriteria"": ""Clear list of what done looks like"",
  ""technicalNotes"": ""Implementation hints, constraints, or dependencies""
}}

CONTEXT:
{summary}
";

            var aiResponse = await _llmService.GetResponseAsync(prompt);
            var ticket = ParseTicketResponse(aiResponse);
            WeakReferenceMessenger.Default.Send(new TicketPreviewMessage(ticket));
            WeakReferenceMessenger.Default.Send(new StatusMessage("Ready"));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage("Error"));
            Messages.Add(new ChatMessageItem { Role = "system", Message = $"Error: {ex.Message}", CreatedDate = DateTime.Now });
        }
    }

    [RelayCommand]
    public async Task SaveDecisionAsync()
    {
        if (Messages.Count == 0) return;

        WeakReferenceMessenger.Default.Send(new StatusMessage("Saving decision..."));
        try
        {
            var recentContext = string.Empty;
            var count = Math.Min(Messages.Count, 10);
            for (int i = Messages.Count - count; i < Messages.Count; i++)
            {
                recentContext += $"[{Messages[i].Role}]: {Messages[i].Message}\n";
            }

            var prompt = $@"
From the following recent conversation, extract the most important decision or conclusion.
Return ONLY a JSON object with exactly these fields:
- ""title"": a short title (max 60 chars)
- ""detail"": a one-sentence summary of the decision
- ""reason"": why this decision was made

CONVERSATION:
{recentContext}
";

            var aiResponse = await _llmService.GetResponseAsync(prompt);
            var (title, detail, reason) = ParseDecisionResponse(aiResponse);

            await _projectMemoryService.SaveDecisionAsync(new ProjectDecision
            {
                ProjectId = _projectId,
                Title = title,
                Detail = detail,
                Reason = reason
            });

            WeakReferenceMessenger.Default.Send(new DecisionSavedMessage());
            WeakReferenceMessenger.Default.Send(new StatusMessage("Decision Saved"));
            Messages.Add(new ChatMessageItem { Role = "system", Message = $"Decision saved: {title}", CreatedDate = DateTime.Now });
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage("Error"));
            Messages.Add(new ChatMessageItem { Role = "system", Message = $"Error saving decision: {ex.Message}", CreatedDate = DateTime.Now });
        }
    }

    private int _messagesSinceSummary;

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(PromptText)) return;

        var input = PromptText;
        PromptText = string.Empty;

        Messages.Add(new ChatMessageItem { Role = "user", Message = input, CreatedDate = DateTime.Now });

        await _chatHistoryService.SaveMessageAsync(new IronDev.Data.Models.ChatMessage
        {
            ProjectId = _projectId,
            SessionId = _sessionId,
            Role = "user",
            Message = input
        });

        WeakReferenceMessenger.Default.Send(new StatusMessage("Thinking..."));

        try
        {
            var fullPrompt = await _promptContextBuilder.BuildAsync(_projectId, _sessionId, input);
            var aiResponse = await _llmService.GetResponseAsync(fullPrompt);

            // Inline Decision Extraction
            var decisionMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, @"<decision>(.*?)</decision>", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (decisionMatch.Success)
            {
                var content = decisionMatch.Groups[1].Value.Trim();
                var parts = content.Split('|', 2);
                var title = parts.Length > 0 ? parts[0].Trim() : "Decision";
                var detail = parts.Length > 1 ? parts[1].Trim() : content;
                
                aiResponse = aiResponse.Replace(decisionMatch.Value, "").Trim();
                
                _ = _projectMemoryService.SaveDecisionAsync(new ProjectDecision { ProjectId = _projectId, Title = title, Detail = detail })
                    .ContinueWith(t => WeakReferenceMessenger.Default.Send(new DecisionSavedMessage()));
            }

            await _chatHistoryService.SaveMessageAsync(new IronDev.Data.Models.ChatMessage
            {
                ProjectId = _projectId,
                SessionId = _sessionId,
                Role = "assistant",
                Message = aiResponse
            });

            Messages.Add(new ChatMessageItem { Role = "assistant", Message = aiResponse, CreatedDate = DateTime.Now });
            WeakReferenceMessenger.Default.Send(new StatusMessage("Ready"));

            // Auto-Summary Trigger
            _messagesSinceSummary++;
            if (_messagesSinceSummary >= 5)
            {
                _messagesSinceSummary = 0;
                _ = AutoUpdateSummaryAsync();
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage("Error"));
            Messages.Add(new ChatMessageItem { Role = "system", Message = $"Error: {ex.Message}", CreatedDate = DateTime.Now });
        }
    }

    private async Task AutoUpdateSummaryAsync()
    {
        try
        {
            var latestSummary = await _projectMemoryService.GetLatestSummaryAsync(_projectId);
            var recentMessages = await _chatHistoryService.GetRecentMessagesAsync(_projectId, _sessionId, 6);
            
            var prompt = "Given the current project summary and the most recent chat lines, write an updated, cohesive one-paragraph project summary.\n";
            prompt += "CURRENT SUMMARY:\n" + (latestSummary?.Summary ?? "None") + "\n\nRECENT CHAT:\n";
            foreach (var m in recentMessages) prompt += $"[{m.Role}]: {m.Message}\n";
            
            var newSummary = await _llmService.GetResponseAsync(prompt);
            
            await _projectMemoryService.SaveSummaryAsync(new IronDev.Data.Models.ProjectSummary
            {
                ProjectId = _projectId,
                Summary = newSummary.Trim()
            });
            
            WeakReferenceMessenger.Default.Send(new SummaryUpdatedMessage());
        }
        catch { /* fail silently in background */ }
    }

    // ── Ticket parsing ──────────────────────────────────────────────

    private static TicketItem ParseTicketResponse(string response)
    {
        var cleaned = StripCodeFences(response);

        return new TicketItem
        {
            Title = ExtractJsonField(cleaned, "title") ?? "(Untitled)",
            TicketType = ExtractJsonField(cleaned, "ticketType") ?? "Task",
            Priority = ExtractJsonField(cleaned, "priority") ?? "Medium",
            Summary = ExtractJsonField(cleaned, "summary"),
            Background = ExtractJsonField(cleaned, "background"),
            Problem = ExtractJsonField(cleaned, "problem"),
            AcceptanceCriteria = ExtractJsonField(cleaned, "acceptanceCriteria"),
            TechnicalNotes = ExtractJsonField(cleaned, "technicalNotes"),
            Status = "Draft",
            Content = response,
            CreatedDate = DateTime.Now
        };
    }

    // ── Decision parsing ────────────────────────────────────────────

    private static (string title, string detail, string reason) ParseDecisionResponse(string response)
    {
        var cleaned = StripCodeFences(response);

        string title = "Decision", detail = cleaned, reason = "";

        try
        {
            title = ExtractJsonField(cleaned, "title") ?? "Decision";
            detail = ExtractJsonField(cleaned, "detail") ?? cleaned;
            reason = ExtractJsonField(cleaned, "reason") ?? "";
        }
        catch { /* fall back to raw response */ }

        return (title, detail, reason);
    }

    // ── Shared helpers ──────────────────────────────────────────────

    private static string StripCodeFences(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0) cleaned = cleaned.Substring(firstNewline + 1);
            if (cleaned.EndsWith("```")) cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }
        return cleaned;
    }

    private static string? ExtractJsonField(string json, string fieldName)
    {
        // Look for "fieldName" : "value" — handles simple JSON strings
        var key = $"\"{fieldName}\"";
        var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var colonIdx = json.IndexOf(':', idx + key.Length);
        if (colonIdx < 0) return null;

        var afterColon = json.Substring(colonIdx + 1).TrimStart();
        if (afterColon.StartsWith("\""))
        {
            // Find the closing quote, handling escaped quotes
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < afterColon.Length; i++)
            {
                if (afterColon[i] == '\\' && i + 1 < afterColon.Length)
                {
                    sb.Append(afterColon[i + 1]);
                    i++;
                }
                else if (afterColon[i] == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(afterColon[i]);
                }
            }
        }

        return null;
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

using IronDev.AI;
using IronDev.Core;
using IronDev.Data.Models;
using IronDev.Services;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using System.Threading.Tasks;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class ChatWorkspaceViewModel : ObservableObject
{
    private readonly IChatHistoryService    _chatHistoryService;
    private readonly IPromptContextBuilder  _promptContextBuilder;
    private readonly ILLMService            _llmService;
    private readonly IProjectMemoryService  _memoryService;
    private readonly IChatFeedbackService   _feedbackService;
    private readonly ILlmTraceService       _llmTraceService;

    private int _activeProjectId;
    private string _activeProjectName = string.Empty;

    [ObservableProperty] private ObservableCollection<ProjectChatSession> _sessions = [];
    [ObservableProperty] private ProjectChatSession? _selectedSession;
    [ObservableProperty] private ObservableCollection<ChatSummary> _messages = [];
    [ObservableProperty] private string _promptText = string.Empty;
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private bool   _hasSelectedSession;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool    _hasStatusMessage;

    /// <summary>Grouped view of Sessions for the ListBox — groups by DateGroup (Today / This Week / Earlier).</summary>
    public ICollectionView GroupedSessions { get; }

    // Renaming state
    [ObservableProperty] private bool _isRenamingTitle;
    [ObservableProperty] private string _editingTitle = string.Empty;

    // Composer context
    [ObservableProperty] private ObservableCollection<string> _contextChips = [];
    [ObservableProperty] private bool _hasContextChips;
    [ObservableProperty] private string _activeModel = "gpt-4o";

    // Ticket creation now passes a ChatTicketContext so the shell can
    // initiate the draft review flow instead of directly prefilling.
    public Action<IronDev.Agent.Models.ChatTicketContext>? OnCreateTicketFromChat { get; set; }
    public Action<string, string, string?, string?, string?>? OnCreatePlanFromChat { get; set; }
    public Action<string, string, string?, string?>? OnCreateDecisionFromChat { get; set; }

    // Navigation shortcuts — wired by ShellViewModel
    public Action? OnNavigateToPlan     { get; set; }
    public Action? OnNavigateToTicket   { get; set; }
    public Action? OnNavigateToDecision { get; set; }

    public ChatWorkspaceViewModel(
        IChatHistoryService   chatHistoryService,
        IPromptContextBuilder promptContextBuilder,
        ILLMService           llmService,
        IProjectMemoryService memoryService,
        IChatFeedbackService  feedbackService,
        ILlmTraceService      llmTraceService)
    {
        _chatHistoryService   = chatHistoryService;
        _promptContextBuilder = promptContextBuilder;
        _llmService           = llmService;
        _memoryService        = memoryService;
        _feedbackService      = feedbackService;
        _llmTraceService      = llmTraceService;

        // Wire grouped view for history pane
        var cv = CollectionViewSource.GetDefaultView(_sessions);
        cv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectChatSession.DateGroup)));
        GroupedSessions = cv;
    }

    public async Task LoadAsync(global::IronDev.Data.Models.Project project)
    {
        _activeProjectId = project.Id;
        _activeProjectName = project.Name;

        await RefreshSessionsAsync();

        // Auto-select the most recent session if available
        SelectedSession = Sessions.FirstOrDefault();
    }

    private void RefreshContextChips()
    {
        ContextChips.Clear();
        var lastAssistant = Messages.LastOrDefault(m => m.Role == "assistant");
        if (lastAssistant?.ContextPacket?.MatchedFilePaths is { Count: > 0 } paths)
        {
            foreach (var p in paths.Take(4))
                ContextChips.Add(System.IO.Path.GetFileName(p));
        }
        HasContextChips = ContextChips.Count > 0;
    }

    private async Task RefreshSessionsAsync()
    {
        Sessions.Clear();
        var sessions = await _chatHistoryService.GetRecentSessionsAsync(_activeProjectId);
        foreach (var s in sessions)
            Sessions.Add(s);
    }

    partial void OnSelectedSessionChanged(ProjectChatSession? value)
    {
        HasSelectedSession = value != null;
        IsRenamingTitle = false;
        Messages.Clear();
        if (value != null)
        {
            _ = LoadMessagesAsync(value.Id);
        }
    }

    [RelayCommand]
    private void SelectSession(ProjectChatSession? session)
    {
        if (session != null)
            SelectedSession = session;
    }

    private async Task LoadMessagesAsync(long sessionId)
    {
        var messages = await _chatHistoryService.GetRecentMessagesAsync(_activeProjectId, sessionId, 100);
        foreach (var m in messages)
        {
            Messages.Add(new ChatSummary
            {
                Role = m.Role,
                MessageText = m.Message,
                Timestamp = m.CreatedDate,
                // In a real V1, we might store the ContextSummary in the DB and display it here
            });
        }
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        if (_activeProjectId <= 0) return;

        var session = new ProjectChatSession
        {
            ProjectId = _activeProjectId,
            Title = "New Chat",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        var id = await _chatHistoryService.SaveSessionAsync(session);
        session.Id = id;
        
        Sessions.Insert(0, session);
        SelectedSession = session;
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var text = PromptText.Trim();
        if (string.IsNullOrEmpty(text) || IsBusy) return;

        // Ensure we have a session
        if (SelectedSession == null)
        {
            await NewChatAsync();
        }

        if (SelectedSession == null) return;

        PromptText = string.Empty;
        var userMsg = new ChatSummary { Role = "user", MessageText = text, Timestamp = DateTime.UtcNow };
        Messages.Add(userMsg);

        IsBusy = true;
        
        try
        {
            var projectId = _activeProjectId;
            var sessionId = SelectedSession.Id;

            // Persist user message
            await _chatHistoryService.SaveMessageAsync(new global::IronDev.Data.Models.ChatMessage
            {
                ProjectId = projectId,
                ChatSessionId = sessionId,
                Role = "user",
                Message = text
            });

            // Auto-generate title if still default
            var currentSession = SelectedSession;
            if (currentSession != null && currentSession.Title == "New Chat")
            {
                var newTitle = text.Length > 40 ? text.Substring(0, 37) + "..." : text;
                currentSession.Title = newTitle;
                
                // Refresh in list - careful: this may clear SelectedSession via UI binding
                var idx = Sessions.IndexOf(currentSession);
                if (idx >= 0)
                {
                    Sessions.RemoveAt(idx);
                    Sessions.Insert(idx, currentSession);
                }
                
                // Restore selection if it was cleared
                if (SelectedSession == null)
                    SelectedSession = currentSession;

                await _chatHistoryService.SaveSessionAsync(currentSession);
            }

            var packet = await _promptContextBuilder.BuildPacketAsync(projectId, sessionId, text);

            // ── Dev-mode grounding diagnostics ────────────────────────────────
            // Visible in VS Output window / DebugView — not shown to end users.
            var snippetCount  = packet.MatchedFilePaths.Count;
            var distinctFiles = packet.MatchedFilePaths.Distinct().Count();
            var qualityLabel  = packet.IsProjectNotIndexed ? "Limited (not indexed)"
                              : snippetCount == 0          ? "Empty (no snippets)"
                              :                              "Ready";
            System.Diagnostics.Trace.WriteLine(
                $"[Chat][Grounding] ProjectId={projectId} | Intent={packet.Intent} | " +
                $"ProjectIndexed={!packet.IsProjectNotIndexed} | " +
                $"Snippets={snippetCount} | DistinctFiles={distinctFiles} | " +
                $"FilteredMemory={packet.FilteredMemoryCount} | " +
                $"ContextQuality={qualityLabel}");
            if (packet.MatchedFilePaths.Count > 0)
                System.Diagnostics.Trace.WriteLine(
                    $"[Chat][Sources] {string.Join(", ", packet.MatchedFilePaths.Distinct().Take(6))}");
            // ─────────────────────────────────────────────────────────────────

            // Call the real LLM service with tracing
            var responseText = string.Empty;
            var trace = new LlmTraceEntry
            {
                FeatureName = "Chat",
                WorkspaceName = "Chat",
                Model = ActiveModel,
                CurrentUserMessage = text,
                ProjectId = projectId,
                ChatSessionId = sessionId.ToString(),
                RequestText = packet.FormattedPrompt,
                Warnings = packet.RulesLoadWarning ?? string.Empty,
                ContextSummary = BuildContextSummary(packet),
                CreatedAt = DateTime.UtcNow
            };


            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                responseText = await _llmService.GetResponseAsync(packet.FormattedPrompt);
                trace.WasSuccessful = true;
                trace.RawResponseText = responseText;
                trace.ParsedResponseSummary = responseText.Length > 200 ? responseText.Substring(0, 200) + "..." : responseText;
            }
            catch (Exception ex)
            {
                responseText = $"[LLM Error]: {ex.Message}";
                trace.WasSuccessful = false;
                trace.ErrorMessage = ex.Message;
            }
            finally
            {
                sw.Stop();
                trace.DurationMs = sw.ElapsedMilliseconds;
                _llmTraceService.AddTrace(trace);
            }

            var assistantMsg = new ChatSummary
            {
                Role = "assistant",
                MessageText = responseText,
                FormattedPrompt = packet.FormattedPrompt,
                ContextPacket = packet,
                Timestamp = DateTime.UtcNow
            };
            Messages.Add(assistantMsg);
            RefreshContextChips();

            // Persist assistant message and capture its DB Id
            var assistantDbId = await _chatHistoryService.SaveMessageAsync(new global::IronDev.Data.Models.ChatMessage
            {
                ProjectId      = projectId,
                ChatSessionId  = sessionId,
                Role           = "assistant",
                Message        = responseText,
                ContextSummary = assistantMsg.ContextHeader,
                LinkedFilePaths = string.Join("\n", packet.MatchedFilePaths),
                LinkedSymbols   = string.Join("\n", packet.MatchedSymbols)
            });
            assistantMsg.PersistedMessageId = assistantDbId;

            // Update session's UpdatedDate
            SelectedSession.UpdatedDate = DateTime.UtcNow;
            await _chatHistoryService.SaveSessionAsync(SelectedSession);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartRename()
    {
        if (SelectedSession == null) return;
        EditingTitle = SelectedSession.Title;
        IsRenamingTitle = true;
    }

    [RelayCommand]
    private async Task SaveTitleAsync()
    {
        var currentSession = SelectedSession;
        if (currentSession == null || string.IsNullOrWhiteSpace(EditingTitle))
        {
            IsRenamingTitle = false;
            return;
        }

        currentSession.Title = EditingTitle.Trim();
        await _chatHistoryService.SaveSessionAsync(currentSession);
        
        // Refresh in list
        var idx = Sessions.IndexOf(currentSession);
        if (idx >= 0)
        {
            Sessions.RemoveAt(idx);
            Sessions.Insert(idx, currentSession);
        }

        // Force header/detail bindings to repaint
        SelectedSession = null;
        SelectedSession = currentSession;

        IsRenamingTitle = false;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenamingTitle = false;
    }

    [RelayCommand]
    private void CreateTicket(ChatSummary message)
    {
        if (message == null || OnCreateTicketFromChat == null) return;

        var idx = Messages.IndexOf(message);
        var proposedTitle = "Chat-generated ticket";
        if (idx > 0 && Messages[idx - 1].Role == "user")
        {
            var q = Messages[idx - 1].MessageText;
            proposedTitle = q.Length > 80 ? q[..80] + "..." : q;
        }

        string? linkedFilePaths = null;
        string? linkedSymbols = null;
        if (message.ContextPacket != null)
        {
            if (message.ContextPacket.MatchedFilePaths.Count > 0)
                linkedFilePaths = string.Join("\n", message.ContextPacket.MatchedFilePaths);
            if (message.ContextPacket.MatchedSymbols.Count > 0)
                linkedSymbols = string.Join("\n", message.ContextPacket.MatchedSymbols);
        }

        var messageText = message.MessageText.Length > 2000
            ? message.MessageText[..2000] + "\n...[truncated]"
            : message.MessageText;

        var ctx = new IronDev.Agent.Models.ChatTicketContext
        {
            SessionId       = SelectedSession?.Id ?? 0,
            MessageId       = (long)idx,  // proxy: index in Messages; no Id on ChatSummary
            MessageText     = messageText,
            ProposedTitle   = proposedTitle,
            LinkedFilePaths = linkedFilePaths,
            LinkedSymbols   = linkedSymbols,
        };

        OnCreateTicketFromChat.Invoke(ctx);
    }

    [RelayCommand]
    private void SaveDecision(ChatSummary message)
    {
        if (message == null || OnCreateDecisionFromChat == null) return;

        var idx = Messages.IndexOf(message);
        var userQuestion = "Chat-generated decision";
        if (idx > 0 && Messages[idx - 1].Role == "user")
        {
            var q = Messages[idx - 1].MessageText;
            userQuestion = q.Length > 80 ? q.Substring(0, 80) + "..." : q;
        }

        var detail = message.MessageText;
        if (detail.Length > 2000)
            detail = detail.Substring(0, 2000) + "\n...[truncated]";

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

    [RelayCommand]
    private void CreatePlan(ChatSummary message)
    {
        if (message == null || OnCreatePlanFromChat == null) return;

        var idx = Messages.IndexOf(message);
        var title = "Implementation Plan";
        if (idx > 0 && Messages[idx - 1].Role == "user")
        {
            var q = Messages[idx - 1].MessageText;
            title = q.Length > 80 ? q.Substring(0, 80) + "..." : q;
        }

        var goal = message.MessageText;
        var lines = goal.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var steps = string.Join("\n", lines.Where(l => l.Trim().StartsWith("-") || char.IsDigit(l.Trim().FirstOrDefault())));

        string? linkedFilePaths = null;
        string? linkedSymbols = null;
        if (message.ContextPacket != null)
        {
            if (message.ContextPacket.MatchedFilePaths.Count > 0)
                linkedFilePaths = string.Join("\n", message.ContextPacket.MatchedFilePaths);
            if (message.ContextPacket.MatchedSymbols.Count > 0)
                linkedSymbols = string.Join("\n", message.ContextPacket.MatchedSymbols);
        }

        OnCreatePlanFromChat.Invoke(title, goal, steps, linkedFilePaths, linkedSymbols);
    }

    // ── Save Summary from message ─────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveSummaryFromMessageAsync(ChatSummary message)
    {
        if (message == null || _activeProjectId <= 0) return;

        var text = message.MessageText;
        if (text.Length > 3000) text = text[..3000] + "\n...[truncated]";

        try
        {
            await _memoryService.SaveSummaryAsync(new global::IronDev.Data.Models.ProjectSummary
            {
                ProjectId           = _activeProjectId,
                Summary             = text,
                SourceChatMessageId = null,          // ChatSummary has no persisted Id yet
                UpdatedDate         = DateTime.UtcNow
            });

            StatusMessage    = "Summary saved.";
            HasStatusMessage = true;
        }
        catch (Exception ex)
        {
            StatusMessage    = $"Summary failed: {ex.Message}";
            HasStatusMessage = true;
            System.Diagnostics.Trace.WriteLine($"[SaveSummary] Failed: {ex}");
        }
    }

    // ── Copy message to clipboard ─────────────────────────────────────────────

    [RelayCommand]
    private static void CopyMessage(ChatSummary message)
    {
        if (message == null || string.IsNullOrEmpty(message.MessageText)) return;
        Clipboard.SetText(message.MessageText);
    }

    // ── Feedback ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called from the UI with a "rating|reason" string, e.g. "Useful|Good file grounding".
    /// Saves the feedback to the database and marks the message as rated.
    /// </summary>
    [RelayCommand]
    private async Task SubmitFeedbackAsync(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter) || _activeProjectId <= 0) return;

        // Find the message that corresponds to this feedback via AwaitingReason state
        // Parameter format: "<rating>|<reason>" — passed from a combined CommandParameter
        var parts  = parameter.Split('|', 2);
        var rating = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var reason = parts.Length > 1 ? parts[1].Trim() : null;

        if (string.IsNullOrWhiteSpace(rating)) return;

        // Find the in-memory message that was rated (has AwaitingReason = true or FeedbackRating set)
        var msg = Messages.LastOrDefault(m =>
            m.Role == "assistant" &&
            m.FeedbackRating == rating &&
            !m.FeedbackSaved);

        if (msg == null) return;

        try
        {
            await _feedbackService.SaveFeedbackAsync(new global::IronDev.Data.Models.ChatMessageFeedback
            {
                ProjectId     = _activeProjectId,
                ChatSessionId = SelectedSession?.Id,
                ChatMessageId = msg.PersistedMessageId,
                Rating        = rating,
                Reason        = reason
            });

            msg.FeedbackReason = reason;
            msg.FeedbackSaved  = true;
            msg.AwaitingReason = false;

            StatusMessage    = "Feedback saved.";
            HasStatusMessage = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Feedback] Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks a message as having a pending rating and opens the reason picker.
    /// Parameter: "Useful" or "Weak".
    /// </summary>
    [RelayCommand]
    private void BeginFeedback(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter)) return;

        // Clear any other pending messages
        foreach (var m in Messages.Where(m => m.Role == "assistant" && m.AwaitingReason && !m.FeedbackSaved))
        {
            m.AwaitingReason  = false;
            m.FeedbackRating  = null;
        }

        // Find the last unrated assistant message to attach this rating to
        // (In practice the button CommandParameter will include message index)
        var parts      = parameter.Split('|', 2);
        var rating     = parts[0].Trim();
        var msgIndexStr = parts.Length > 1 ? parts[1] : null;

        ChatSummary? target = null;
        if (int.TryParse(msgIndexStr, out var idx) && idx >= 0 && idx < Messages.Count)
            target = Messages[idx];
        else
            target = Messages.LastOrDefault(m => m.Role == "assistant" && !m.FeedbackSaved);

        if (target == null) return;

        target.FeedbackRating  = rating;
        target.AwaitingReason  = true;
    }

    // ── Workspace navigation shortcuts ────────────────────────────────────────

    [RelayCommand]
    private void NavigateToPlan()     => OnNavigateToPlan?.Invoke();

    [RelayCommand]
    private void NavigateToTicket()   => OnNavigateToTicket?.Invoke();

    [RelayCommand]
    private void NavigateToDecision() => OnNavigateToDecision?.Invoke();

    // ── Context summary builder ───────────────────────────────────────────────

    /// <summary>
    /// Builds a compact, human-readable context summary from a <see cref="IronDev.AI.ChatContextPacket"/>
    /// for display in the LLM Console trace detail panel and exported trace files.
    /// </summary>
    public static string BuildContextSummary(IronDev.AI.ChatContextPacket packet)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Intent:            {packet.Intent}");
        sb.AppendLine($"Project indexed:   {(packet.IsProjectNotIndexed ? "No" : "Yes")}");
        sb.AppendLine($"Retrieved files:   {packet.MatchedFilePaths.Count}");
        sb.AppendLine($"Distinct files:    {packet.MatchedFilePaths.Distinct().Count()}");
        sb.AppendLine($"Memory included:   {packet.IncludedMemoryCount}");
        sb.AppendLine($"Memory filtered:   {packet.FilteredMemoryCount}");
        sb.AppendLine($"Standards:         {packet.IncludedStandardsCount} included, {packet.FilteredStandardsCount} filtered");
        sb.AppendLine($"Warnings:          {(string.IsNullOrWhiteSpace(packet.RulesLoadWarning) ? "none" : packet.RulesLoadWarning)}");

        if (packet.MatchedFilePaths.Count > 0)
        {
            sb.AppendLine("Top files:");
            foreach (var f in packet.MatchedFilePaths.Take(5))
                sb.AppendLine($"  - {System.IO.Path.GetFileName(f)}");
        }

        return sb.ToString().TrimEnd();
    }
}

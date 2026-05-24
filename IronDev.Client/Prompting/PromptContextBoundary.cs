using IronDev.Data.Models;

namespace IronDev.Client.Prompting;

public enum ChatIntent
{
    General,
    CodeQuery,
    SavedTicketManagement,
    DraftTicketFlow,
    AnalyzeCodebase
}

public sealed class ChatContextPacket
{
    public List<string> Snippets { get; init; } = [];
    public List<string> Tickets { get; init; } = [];
    public List<string> Decisions { get; init; } = [];
    public List<ProjectContextDocument> ContextDocuments { get; init; } = [];
    public ProjectObservableState? ObservableState { get; set; }
    public string FormattedPrompt { get; set; } = string.Empty;
    public List<string> MatchedFilePaths { get; init; } = [];
    public List<string> MatchedSymbols { get; init; } = [];
    public ChatIntent Intent { get; set; } = ChatIntent.General;
    public bool IsProjectNotIndexed { get; set; }
    public int FilteredMemoryCount { get; set; }
    public int IncludedMemoryCount { get; set; }
    public List<string> PollutedTermsFound { get; init; } = [];
    public List<ProjectRule> Standards { get; init; } = [];
    public int IncludedStandardsCount { get; set; }
    public int FilteredStandardsCount { get; set; }
    public string? RulesLoadWarning { get; set; }
    public string HostApplicationName { get; set; } = "IronDev";
    public string ActiveProjectName { get; set; } = string.Empty;
    public string ActiveProjectPath { get; set; } = string.Empty;
    public string ActiveProjectType { get; set; } = string.Empty;
    public bool IsExternalProject { get; set; }
}

public sealed class PromptPreviewResult
{
    public string PromptText { get; set; } = string.Empty;
    public string DetectedIntent { get; set; } = string.Empty;
    public string ProjectIndexStatus { get; set; } = string.Empty;
    public string ContextQuality { get; set; } = string.Empty;
    public List<CodeIndexEntry> RetrievedItems { get; init; } = [];
    public bool ContextPolluted { get; set; }
    public List<string> PollutedTermsFound { get; init; } = [];
    public int FilteredMemoryCount { get; set; }
    public int IncludedMemoryCount { get; set; }
    public int IncludedStandardsCount { get; set; }
    public int FilteredStandardsCount { get; set; }
}

public interface IPromptContextBuilder
{
    Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default);
    Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default);
    Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default);
}

public sealed class ClientPromptContextBuilder : IPromptContextBuilder
{
    public Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default) =>
        Task.FromResult(userRequest);

    public Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatContextPacket
        {
            FormattedPrompt = userRequest,
            Intent = ClassifyIntent(userRequest)
        });

    public Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default) =>
        Task.FromResult(new PromptPreviewResult
        {
            PromptText = userMessage,
            DetectedIntent = ClassifyIntent(userMessage).ToString(),
            ProjectIndexStatus = "API boundary",
            ContextQuality = "Prompt preview uses the API boundary client."
        });

    public static ChatIntent ClassifyIntent(string text)
    {
        if (text.Contains("ticket", StringComparison.OrdinalIgnoreCase)) return ChatIntent.SavedTicketManagement;
        if (text.Contains("code", StringComparison.OrdinalIgnoreCase) || text.Contains("file", StringComparison.OrdinalIgnoreCase)) return ChatIntent.CodeQuery;
        if (text.Contains("analy", StringComparison.OrdinalIgnoreCase)) return ChatIntent.AnalyzeCodebase;
        return ChatIntent.General;
    }

    public static List<string> ExpandSearchQueries(string text, ChatIntent intent) => [text];
}

public static class PromptContextBuilder
{
    public static ChatIntent ClassifyIntent(string text) => ClientPromptContextBuilder.ClassifyIntent(text);
    public static List<string> ExpandSearchQueries(string text, ChatIntent intent) => ClientPromptContextBuilder.ExpandSearchQueries(text, intent);
}

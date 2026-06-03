using IronDev.Data.Models;

namespace IronDev.Client.Chat;

public interface IChatApiClient
{
    Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default);
    Task<ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task<long> SaveFeedbackAsync(ChatMessageFeedback feedback, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(int projectId, long sessionId, int take = 50, CancellationToken cancellationToken = default);
    Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}

public sealed record ChatCompletionRequest(
    int ProjectId,
    long? SessionId,
    string Prompt,
    string? ActiveModel,
    string? Mode = null);

public sealed record ChatCompletionResponse(
    string Response,
    string? ContextSummary,
    string? LinkedFilePaths,
    string? LinkedSymbols,
    long? TraceId,
    string? Mode = null,
    bool? ShowGovernanceActions = null,
    IReadOnlyList<string>? GovernanceActions = null,
    IReadOnlyList<string>? ReasoningTrace = null,
    string? DisambiguationQuestion = null,
    string? ReasoningSummary = null,
    string? DogfoodTraceId = null,
    string? DogfoodTracePath = null);

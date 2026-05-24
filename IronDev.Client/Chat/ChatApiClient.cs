using IronDev.Client.Http;
using IronDev.Data.Models;

namespace IronDev.Client.Chat;

public sealed class ChatApiClient : IronDevApiClientBase, IChatApiClient
{
    public ChatApiClient(HttpClient http)
        : base(http)
    {
    }

    public Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectChatSession>>($"projects/{projectId}/chat/sessions?take={take}", cancellationToken);

    public Task<ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectChatSession?>($"chat/sessions/{sessionId}", cancellationToken);

    public Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{session.ProjectId}/chat/sessions", session, cancellationToken);

    public Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"chat/sessions/{sessionId}", cancellationToken);

    public Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{message.ProjectId}/chat/sessions/{message.ChatSessionId}/messages", message, cancellationToken);

    public Task<long> SaveFeedbackAsync(ChatMessageFeedback feedback, CancellationToken cancellationToken = default) =>
        PostAsync<long>($"projects/{feedback.ProjectId}/chat/feedback", feedback, cancellationToken);

    public Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(int projectId, long sessionId, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ChatMessage>>($"projects/{projectId}/chat/sessions/{sessionId}/messages?take={take}", cancellationToken);

    public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ChatCompletionResponse>($"projects/{request.ProjectId}/chat/complete", request, cancellationToken);
}

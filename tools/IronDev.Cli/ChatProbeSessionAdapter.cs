using IronDev.Client.Chat;
using IronDev.Core.ChatProbe;
using IronDev.Data.Models;

namespace IronDev.Cli;

/// <summary>
/// Adapts <see cref="IChatApiClient"/> to the <see cref="IChatProbeSession"/> port
/// so the CLI can hand the driver a real IronDev chat connection without
/// coupling IronDev.Core to IronDev.Client.
/// </summary>
internal sealed class ChatProbeSessionAdapter : IChatProbeSession
{
    private readonly IChatApiClient _chatClient;

    public ChatProbeSessionAdapter(IChatApiClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<long> OpenSessionAsync(int projectId, string title, CancellationToken ct)
    {
        return await _chatClient.SaveSessionAsync(
            new ProjectChatSession
            {
                ProjectId = projectId,
                Title     = title
            },
            ct);
    }

    public async Task AppendMessageAsync(int projectId, long sessionId, string role, string content, CancellationToken ct)
    {
        await _chatClient.SaveMessageAsync(new ChatMessage
        {
            ProjectId     = projectId,
            ChatSessionId = sessionId,
            Role          = role,
            Message       = content
        }, ct);
    }

    public async Task<ChatProbeCompletionResult> CompleteAsync(
        int projectId,
        long sessionId,
        string prompt,
        CancellationToken ct)
    {
        var response = await _chatClient.CompleteAsync(new ChatCompletionRequest(
            ProjectId:   projectId,
            SessionId:   sessionId,
            Prompt:      prompt,
            ActiveModel: null,
            Mode:        null), ct);

        return new ChatProbeCompletionResult
        {
            Response          = response.Response ?? string.Empty,
            Mode              = response.Mode,
            GovernanceActions = response.GovernanceActions ?? [],
            DogfoodTraceId    = response.DogfoodTraceId
        };
    }
}

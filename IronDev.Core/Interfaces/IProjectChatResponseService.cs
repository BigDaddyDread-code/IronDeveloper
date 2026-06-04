using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectChatResponseService
{
    Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        ChatGovernanceMode? explicitMode = null,
        string? dogfoodTraceId = null,
        string? recentConversationSummary = null,
        long? sessionId = null,
        CancellationToken cancellationToken = default);
}

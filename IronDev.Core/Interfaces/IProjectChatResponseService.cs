using IronDev.Core.Models;

using IronDev.Core.Chat;

namespace IronDev.Core.Interfaces;

public interface IProjectChatResponseService
{
    Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        MemoryRetrievalRequestContext memoryRetrievalContext,
        ChatGovernanceMode? explicitMode = null,
        string? dogfoodTraceId = null,
        string? recentConversationSummary = null,
        long? sessionId = null,
        long? sourceMessageId = null,
        CancellationToken cancellationToken = default);
}

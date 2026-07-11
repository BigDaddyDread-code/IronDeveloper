using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectChannelChatService
{
    Task<IReadOnlyList<ProjectChannelChatSummary>> ListVisibleChannelsAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelChatDetail?> GetChannelAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelChatMutationResult> CreateChannelAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string name,
        string? description,
        string visibility,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelChatMutationResult> PostHumanMessageAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        string message,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelChatMutationResult> MarkReadAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        CancellationToken cancellationToken = default);
}

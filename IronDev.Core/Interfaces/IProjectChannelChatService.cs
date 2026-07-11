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

    Task<ProjectChannelChatMutationResult> PostMessageAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        string message,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelChatMutationResult> CompleteAssistantTurnAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        long turnId,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelChatMutationResult> MarkReadAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        CancellationToken cancellationToken = default);

    Task<ProjectNotificationListResponse> ListNotificationsAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkNotificationReadAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        long notificationId,
        CancellationToken cancellationToken = default);
}

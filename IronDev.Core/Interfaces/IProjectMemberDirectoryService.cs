using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectMemberDirectoryService
{
    Task<ProjectMemberDirectoryResponse?> GetDirectoryAsync(
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default);
}

public interface IProjectChannelMembershipService
{
    Task<IReadOnlyList<ProjectChannelDirectoryEntry>> GetVisibleChannelsAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        bool canAdminister,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelMembershipMutationStatus> SetMembershipAsync(
        int tenantId,
        int projectId,
        long channelId,
        int userId,
        int actorUserId,
        string channelRole,
        string notificationLevel,
        CancellationToken cancellationToken = default);

    Task<ProjectChannelMembershipMutationStatus> RemoveMembershipAsync(
        int tenantId,
        int projectId,
        long channelId,
        int userId,
        CancellationToken cancellationToken = default);
}

using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectMembershipService
{
    Task<bool> HasAccessAsync(int tenantId, int projectId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<int>> GetAccessibleProjectIdsAsync(int tenantId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectMembershipEntry>> GetMembersAsync(int tenantId, int projectId, int currentUserId, CancellationToken cancellationToken = default);
    Task<ProjectMembershipMutationStatus> SetMemberAsync(int tenantId, int projectId, int userId, int actorUserId, string projectRole, CancellationToken cancellationToken = default);
    Task<ProjectMembershipMutationStatus> RemoveMemberAsync(int tenantId, int projectId, int userId, int actorUserId, CancellationToken cancellationToken = default);
}

public interface IProjectWorkItemCollaborationService
{
    Task<ProjectWorkItemCollaborationSnapshot?> GetAsync(int tenantId, int projectId, long workItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, ProjectWorkItemCollaborationSnapshot>> GetForProjectAsync(int tenantId, int projectId, CancellationToken cancellationToken = default);
    Task<ProjectWorkItemCollaborationMutationResult> SetAsync(int tenantId, int projectId, long workItemId, int actorUserId, SetProjectWorkItemCollaborationRequest request, CancellationToken cancellationToken = default);
}

using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectMemberDirectoryService
{
    Task<ProjectMemberDirectoryResponse?> GetDirectoryAsync(
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default);
}

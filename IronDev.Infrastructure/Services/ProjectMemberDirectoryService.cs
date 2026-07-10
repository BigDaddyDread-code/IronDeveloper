using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectMemberDirectoryService : IProjectMemberDirectoryService
{
    private const string Boundary =
        "This directory discloses tenant membership in project context. Tenant membership currently provides " +
        "tenant-scoped project visibility. It is not project assignment, channel membership, approval, workflow " +
        "authority, tool authority, or source mutation permission.";

    private readonly IProjectService _projects;
    private readonly IUserService _users;

    public ProjectMemberDirectoryService(IProjectService projects, IUserService users)
    {
        _projects = projects;
        _users = users;
    }

    public async Task<ProjectMemberDirectoryResponse?> GetDirectoryAsync(
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var currentRole = await _users.GetTenantRoleAsync(currentUserId, project.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (currentRole is null)
            return null;

        var users = await _users.GetTenantUsersAsync(project.TenantId, cancellationToken).ConfigureAwait(false);
        var members = users.Select(user => new ProjectMemberDirectoryEntry(
            user.Id,
            user.DisplayName,
            user.Email,
            user.Role,
            user.IsActive,
            user.Id == currentUserId,
            user.IsActive ? "Tenant scoped" : "Inactive account",
            "Not implemented")).ToArray();

        return new ProjectMemberDirectoryResponse(
            project.Id,
            project.Name,
            project.TenantId,
            currentRole,
            TenantUserRoles.CanAdministerUsers(currentRole),
            TenantUserRoles.All,
            "Not implemented",
            "Not implemented",
            members,
            Boundary);
    }
}

using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Channels;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectMemberDirectoryService : IProjectMemberDirectoryService
{
    private const string Boundary =
        "Project membership controls project visibility. Project roles support collaboration administration only. " +
        "Channel membership controls channel visibility and moderation only. Work Item ownership coordinates people; " +
        "none of these grants approval, workflow authority, tool authority, or source mutation permission.";

    private readonly IProjectService _projects;
    private readonly IUserService _users;
    private readonly IProjectChannelMembershipService _channelMemberships;
    private readonly IProjectMembershipService _projectMemberships;

    public ProjectMemberDirectoryService(
        IProjectService projects,
        IUserService users,
        IProjectChannelMembershipService channelMemberships,
        IProjectMembershipService projectMemberships)
    {
        _projects = projects;
        _users = users;
        _channelMemberships = channelMemberships;
        _projectMemberships = projectMemberships;
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

        var canAdminister = TenantUserRoles.CanAdministerUsers(currentRole);
        var users = await _users.GetTenantUsersAsync(project.TenantId, cancellationToken).ConfigureAwait(false);
        var channels = await _channelMemberships.GetVisibleChannelsAsync(
            project.TenantId,
            project.Id,
            currentUserId,
            canAdminister,
            cancellationToken).ConfigureAwait(false);
        var projectMembers = await _projectMemberships.GetMembersAsync(
            project.TenantId, project.Id, currentUserId, cancellationToken).ConfigureAwait(false);
        var projectMembershipByUser = projectMembers.ToDictionary(member => member.UserId);
        var currentProjectRole = projectMembershipByUser.GetValueOrDefault(currentUserId)?.ProjectRole;
        var canAdministerProject = string.Equals(currentProjectRole, ProjectMemberRoles.Owner, StringComparison.Ordinal);
        var members = users.Select(user => new ProjectMemberDirectoryEntry(
            user.Id,
            user.DisplayName,
            user.Email,
            user.Role,
            projectMembershipByUser.GetValueOrDefault(user.Id)?.ProjectRole,
            projectMembershipByUser.ContainsKey(user.Id),
            user.IsActive,
            user.Id == currentUserId,
            !user.IsActive ? "Inactive account" : projectMembershipByUser.ContainsKey(user.Id) ? "Project member" : "No project access",
            SummarizeMemberships(user.Id, channels))).ToArray();

        return new ProjectMemberDirectoryResponse(
            project.Id,
            project.Name,
            project.TenantId,
            currentRole,
            canAdminister,
            canAdministerProject,
            canAdminister,
            TenantUserRoles.All,
            ProjectMemberRoles.All,
            [ProjectChannelRoles.Owner, ProjectChannelRoles.Moderator, ProjectChannelRoles.Member, ProjectChannelRoles.ReadOnly],
            [ProjectChannelNotificationLevels.All, ProjectChannelNotificationLevels.Mentions, ProjectChannelNotificationLevels.None],
            $"{projectMembers.Count} active member{(projectMembers.Count == 1 ? string.Empty : "s")}",
            channels.Count == 0 ? "No active channels" : $"{channels.Count} active channel{(channels.Count == 1 ? string.Empty : "s")}",
            members,
            channels,
            Boundary);
    }

    private static string SummarizeMemberships(
        int userId,
        IReadOnlyList<ProjectChannelDirectoryEntry> channels)
    {
        var memberships = channels
            .Where(channel => channel.Members.Any(member => member.UserId == userId))
            .Select(channel => channel.Name)
            .ToArray();

        return memberships.Length == 0
            ? "No explicit memberships"
            : string.Join(", ", memberships);
    }
}

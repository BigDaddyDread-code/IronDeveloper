namespace IronDev.Core.Models;

public sealed record ProjectMemberDirectoryResponse(
    int ProjectId,
    string ProjectName,
    int TenantId,
    string CurrentUserTenantRole,
    bool CanAdministerTenantMembership,
    bool CanAdministerChannelMembership,
    IReadOnlyList<string> AvailableTenantRoles,
    IReadOnlyList<string> AvailableChannelRoles,
    IReadOnlyList<string> AvailableNotificationLevels,
    string ProjectMembershipStatus,
    string ChannelMembershipStatus,
    IReadOnlyList<ProjectMemberDirectoryEntry> Members,
    IReadOnlyList<ProjectChannelDirectoryEntry> Channels,
    string Boundary);

public sealed record ProjectMemberDirectoryEntry(
    int UserId,
    string DisplayName,
    string Email,
    string TenantRole,
    bool IsActive,
    bool IsCurrentUser,
    string ProjectAccessStatus,
    string ChannelMembershipSummary);

public sealed record ProjectChannelDirectoryEntry(
    long ChannelId,
    string Name,
    string? Description,
    string ChannelKind,
    string Visibility,
    int MemberCount,
    IReadOnlyList<ProjectChannelMembershipEntry> Members,
    string Boundary);

public sealed record ProjectChannelMembershipEntry(
    int UserId,
    string ChannelRole,
    string NotificationLevel);

public enum ProjectChannelMembershipMutationStatus
{
    Succeeded = 0,
    ChannelNotFound = 1,
    TargetUserNotTenantMember = 2,
    MembershipNotFound = 3,
    LastOwnerProtected = 4
}

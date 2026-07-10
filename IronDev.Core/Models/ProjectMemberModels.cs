namespace IronDev.Core.Models;

public sealed record ProjectMemberDirectoryResponse(
    int ProjectId,
    string ProjectName,
    int TenantId,
    string CurrentUserTenantRole,
    bool CanAdministerTenantMembership,
    IReadOnlyList<string> AvailableTenantRoles,
    string ProjectMembershipStatus,
    string ChannelMembershipStatus,
    IReadOnlyList<ProjectMemberDirectoryEntry> Members,
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

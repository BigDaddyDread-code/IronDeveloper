namespace IronDev.Core.Models;

public static class ProjectMemberRoles
{
    public const string Owner = "Owner";
    public const string Contributor = "Contributor";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Owner, Contributor, Viewer];

    public static bool IsKnown(string? value) =>
        value is not null && All.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string value) =>
        All.First(role => role.Equals(value, StringComparison.OrdinalIgnoreCase));
}

public sealed record ProjectMembershipEntry(
    int UserId,
    string DisplayName,
    string Email,
    string ProjectRole,
    bool IsCurrentUser,
    DateTimeOffset AddedUtc);

public enum ProjectMembershipMutationStatus
{
    Succeeded = 0,
    ProjectNotFound = 1,
    TargetUserNotTenantMember = 2,
    MembershipNotFound = 3,
    LastOwnerProtected = 4,
    CallerCannotAdminister = 5
}

public sealed record ProjectWorkItemCollaborationSnapshot
{
    public long WorkItemId { get; init; }
    public long Revision { get; init; }
    public ProjectWorkItemCollaborator? Assignee { get; init; }
    public IReadOnlyList<ProjectWorkItemCollaborator> Followers { get; init; } = [];
    public ProjectWorkItemCollaborator? WaitingOn { get; init; }
    public IReadOnlyList<ProjectWorkItemCollaborationActivity> RecentActivity { get; init; } = [];
}

public sealed record ProjectWorkItemCollaborator(
    string Kind,
    int? UserId,
    string DisplayName);

public sealed record ProjectWorkItemCollaborationActivity(
    DateTimeOffset TimestampUtc,
    string Kind,
    string Summary,
    ProjectWorkItemCollaborator? Actor);

public sealed record SetProjectWorkItemCollaborationRequest
{
    public long ExpectedRevision { get; init; }
    public int? AssigneeUserId { get; init; }
    public IReadOnlyList<int> FollowerUserIds { get; init; } = [];
    public int? WaitingOnUserId { get; init; }
    public string? WaitingOnKind { get; init; }
    public string? WaitingOnLabel { get; init; }
}

public enum ProjectWorkItemCollaborationMutationStatus
{
    Succeeded = 0,
    WorkItemNotFound = 1,
    CollaboratorNotProjectMember = 2,
    StaleWrite = 3
}

public sealed record ProjectWorkItemCollaborationMutationResult(
    ProjectWorkItemCollaborationMutationStatus Status,
    ProjectWorkItemCollaborationSnapshot? Collaboration = null);

public sealed record CollaborationWriteConflictResponse(
    string Code,
    long ExpectedRevision,
    long CurrentRevision,
    object? CurrentState,
    string NextSafeAction)
{
    public const string StaleWriteCode = "StaleWrite";
}

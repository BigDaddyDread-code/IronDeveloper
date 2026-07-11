using System.Data;
using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectMembershipService : IProjectMembershipService
{
    private readonly IDbConnectionFactory _connections;

    public ProjectMembershipService(IDbConnectionFactory connections) => _connections = connections;

    public async Task<bool> HasAccessAsync(int tenantId, int projectId, int userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.ProjectMembers pm
            INNER JOIN dbo.TenantUsers tu ON tu.TenantId=pm.TenantId AND tu.UserId=pm.UserId
            INNER JOIN dbo.Users u ON u.Id=pm.UserId AND u.IsActive=1
            WHERE pm.TenantId = @TenantId AND pm.ProjectId = @ProjectId AND pm.UserId = @UserId AND pm.Status = N'Active';
            """;
        using var connection = _connections.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { TenantId = tenantId, ProjectId = projectId, UserId = userId }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlySet<int>> GetAccessibleProjectIdsAsync(int tenantId, int userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT pm.ProjectId FROM dbo.ProjectMembers pm
            INNER JOIN dbo.TenantUsers tu ON tu.TenantId=pm.TenantId AND tu.UserId=pm.UserId
            INNER JOIN dbo.Users u ON u.Id=pm.UserId AND u.IsActive=1
            WHERE pm.TenantId = @TenantId AND pm.UserId = @UserId AND pm.Status = N'Active';
            """;
        using var connection = _connections.CreateConnection();
        var ids = await connection.QueryAsync<int>(new CommandDefinition(sql, new { TenantId = tenantId, UserId = userId }, cancellationToken: cancellationToken));
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<ProjectMembershipEntry>> GetMembersAsync(int tenantId, int projectId, int currentUserId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT pm.UserId, u.DisplayName, u.Email, pm.ProjectRole, pm.AddedUtc
            FROM dbo.ProjectMembers pm
            INNER JOIN dbo.Users u ON u.Id = pm.UserId
            WHERE pm.TenantId = @TenantId AND pm.ProjectId = @ProjectId AND pm.Status = N'Active' AND u.IsActive = 1
            ORDER BY u.DisplayName, u.Email;
            """;
        using var connection = _connections.CreateConnection();
        var rows = await connection.QueryAsync<MemberRow>(new CommandDefinition(sql, new { TenantId = tenantId, ProjectId = projectId }, cancellationToken: cancellationToken));
        return rows.Select(row => new ProjectMembershipEntry(row.UserId, row.DisplayName, row.Email, row.ProjectRole, row.UserId == currentUserId, row.AddedUtc)).ToArray();
    }

    public async Task<ProjectMembershipMutationStatus> SetMemberAsync(int tenantId, int projectId, int userId, int actorUserId, string projectRole, CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!await CanAdministerAsync(connection, transaction, tenantId, projectId, actorUserId, cancellationToken) &&
            !(actorUserId == userId && await CanBootstrapAsync(connection, transaction, tenantId, projectId, cancellationToken)))
            return ProjectMembershipMutationStatus.CallerCannotAdminister;
        if (!await IsTenantMemberAsync(connection, transaction, tenantId, userId, cancellationToken))
            return ProjectMembershipMutationStatus.TargetUserNotTenantMember;

        var normalizedRole = ProjectMemberRoles.Normalize(projectRole);
        if (!string.Equals(normalizedRole, ProjectMemberRoles.Owner, StringComparison.Ordinal) &&
            await IsLastOwnerAsync(connection, transaction, tenantId, projectId, userId, cancellationToken))
            return ProjectMembershipMutationStatus.LastOwnerProtected;

        const string sql = """
            UPDATE dbo.ProjectMembers
            SET ProjectRole = @ProjectRole, Status = N'Active', AddedByUserId = @ActorUserId,
                AddedUtc = SYSUTCDATETIME(), RemovedByUserId = NULL, RemovedUtc = NULL
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND UserId = @UserId;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.ProjectMembers (TenantId, ProjectId, UserId, ProjectRole, AddedByUserId)
                VALUES (@TenantId, @ProjectId, @UserId, @ProjectRole, @ActorUserId);
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, ProjectId = projectId, UserId = userId, ProjectRole = normalizedRole, ActorUserId = actorUserId }, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return ProjectMembershipMutationStatus.Succeeded;
    }

    public async Task<ProjectMembershipMutationStatus> RemoveMemberAsync(int tenantId, int projectId, int userId, int actorUserId, CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!await CanAdministerAsync(connection, transaction, tenantId, projectId, actorUserId, cancellationToken))
            return ProjectMembershipMutationStatus.CallerCannotAdminister;
        if (await IsLastOwnerAsync(connection, transaction, tenantId, projectId, userId, cancellationToken))
            return ProjectMembershipMutationStatus.LastOwnerProtected;

        const string sql = """
            UPDATE dbo.ProjectMembers
            SET Status = N'Removed', RemovedByUserId = @ActorUserId, RemovedUtc = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND UserId = @UserId AND Status = N'Active';
            """;
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, ProjectId = projectId, UserId = userId, ActorUserId = actorUserId }, transaction, cancellationToken: cancellationToken));
        if (affected == 0)
            return ProjectMembershipMutationStatus.MembershipNotFound;
        transaction.Commit();
        return ProjectMembershipMutationStatus.Succeeded;
    }

    private static async Task<bool> CanAdministerAsync(IDbConnection connection, IDbTransaction transaction, int tenantId, int projectId, int userId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM dbo.ProjectMembers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND UserId=@UserId AND Status=N'Active' AND ProjectRole=N'Owner';", new { TenantId = tenantId, ProjectId = projectId, UserId = userId }, transaction, cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> CanBootstrapAsync(IDbConnection connection, IDbTransaction transaction, int tenantId, int projectId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Projects WHERE TenantId=@TenantId AND Id=@ProjectId) AND NOT EXISTS (SELECT 1 FROM dbo.ProjectMembers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Status=N'Active') THEN 1 ELSE 0 END;", new { TenantId = tenantId, ProjectId = projectId }, transaction, cancellationToken: cancellationToken)) == 1;

    private static async Task<bool> IsTenantMemberAsync(IDbConnection connection, IDbTransaction transaction, int tenantId, int userId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM dbo.TenantUsers tu INNER JOIN dbo.Users u ON u.Id=tu.UserId WHERE tu.TenantId=@TenantId AND tu.UserId=@UserId AND u.IsActive=1;", new { TenantId = tenantId, UserId = userId }, transaction, cancellationToken: cancellationToken)) > 0;

    private static async Task<bool> IsLastOwnerAsync(IDbConnection connection, IDbTransaction transaction, int tenantId, int projectId, int userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ProjectMembers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND UserId=@UserId AND Status=N'Active' AND ProjectRole=N'Owner')
                 AND (SELECT COUNT(1) FROM dbo.ProjectMembers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Status=N'Active' AND ProjectRole=N'Owner') = 1
                 THEN 1 ELSE 0 END;
            """;
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { TenantId = tenantId, ProjectId = projectId, UserId = userId }, transaction, cancellationToken: cancellationToken)) == 1;
    }

    private sealed class MemberRow
    {
        public int UserId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string ProjectRole { get; init; } = string.Empty;
        public DateTimeOffset AddedUtc { get; init; }
    }
}

public sealed class ProjectWorkItemCollaborationService : IProjectWorkItemCollaborationService
{
    private readonly IDbConnectionFactory _connections;
    public ProjectWorkItemCollaborationService(IDbConnectionFactory connections) => _connections = connections;

    public async Task<ProjectWorkItemCollaborationSnapshot?> GetAsync(int tenantId, int projectId, long workItemId, CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        if (!await TicketExistsAsync(connection, null, tenantId, projectId, workItemId, cancellationToken)) return null;
        return await ReadAsync(connection, tenantId, projectId, workItemId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, ProjectWorkItemCollaborationSnapshot>> GetForProjectAsync(int tenantId, int projectId, CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        var ids = await connection.QueryAsync<long>(new CommandDefinition("SELECT Id FROM dbo.ProjectTickets WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND IsDeleted=0;", new { TenantId = tenantId, ProjectId = projectId }, cancellationToken: cancellationToken));
        var result = new Dictionary<long, ProjectWorkItemCollaborationSnapshot>();
        foreach (var id in ids) result[id] = await ReadAsync(connection, tenantId, projectId, id, cancellationToken);
        return result;
    }

    public async Task<ProjectWorkItemCollaborationMutationResult> SetAsync(int tenantId, int projectId, long workItemId, int actorUserId, SetProjectWorkItemCollaborationRequest request, CancellationToken cancellationToken = default)
    {
        using var connection = _connections.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (!await TicketExistsAsync(connection, transaction, tenantId, projectId, workItemId, cancellationToken))
            return new(ProjectWorkItemCollaborationMutationStatus.WorkItemNotFound);
        var currentRevision = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT Revision FROM dbo.ProjectWorkItemCollaboration WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND WorkItemId=@WorkItemId;",
            new { TenantId = tenantId, ProjectId = projectId, WorkItemId = workItemId },
            transaction,
            cancellationToken: cancellationToken)) ?? 0;
        if (currentRevision != request.ExpectedRevision)
        {
            transaction.Rollback();
            return new(ProjectWorkItemCollaborationMutationStatus.StaleWrite, await ReadAsync(connection, tenantId, projectId, workItemId, cancellationToken));
        }
        var userIds = request.FollowerUserIds.Append(request.AssigneeUserId ?? 0).Append(request.WaitingOnUserId ?? 0).Where(id => id > 0).Distinct().ToArray();
        if (userIds.Length > 0)
        {
            var validCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM dbo.ProjectMembers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Status=N'Active' AND UserId IN @UserIds;", new { TenantId = tenantId, ProjectId = projectId, UserIds = userIds }, transaction, cancellationToken: cancellationToken));
            if (validCount != userIds.Length) return new(ProjectWorkItemCollaborationMutationStatus.CollaboratorNotProjectMember);
        }

        const string update = """
            UPDATE dbo.ProjectWorkItemCollaboration
            SET AssigneeUserId=@AssigneeUserId, WaitingOnUserId=@WaitingOnUserId, WaitingOnKind=@WaitingOnKind,
                WaitingOnLabel=@WaitingOnLabel, Revision=Revision+1, UpdatedByUserId=@ActorUserId, UpdatedUtc=SYSUTCDATETIME()
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND WorkItemId=@WorkItemId AND Revision=@ExpectedRevision;
            DELETE FROM dbo.ProjectWorkItemFollowers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND WorkItemId=@WorkItemId;
            """;
        const string insert = """
            INSERT INTO dbo.ProjectWorkItemCollaboration (TenantId,ProjectId,WorkItemId,AssigneeUserId,WaitingOnUserId,WaitingOnKind,WaitingOnLabel,Revision,UpdatedByUserId)
            VALUES (@TenantId,@ProjectId,@WorkItemId,@AssigneeUserId,@WaitingOnUserId,@WaitingOnKind,@WaitingOnLabel,1,@ActorUserId);
            """;
        var args = new { TenantId = tenantId, ProjectId = projectId, WorkItemId = workItemId, request.AssigneeUserId, request.WaitingOnUserId, WaitingOnKind = TextOrNull(request.WaitingOnKind), WaitingOnLabel = TextOrNull(request.WaitingOnLabel), ActorUserId = actorUserId, request.ExpectedRevision };
        await connection.ExecuteAsync(new CommandDefinition(currentRevision == 0 ? insert : update, args, transaction, cancellationToken: cancellationToken));
        if (currentRevision == 0)
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.ProjectWorkItemFollowers WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND WorkItemId=@WorkItemId;", args, transaction, cancellationToken: cancellationToken));
        foreach (var followerId in request.FollowerUserIds.Distinct())
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.ProjectWorkItemFollowers (TenantId,ProjectId,WorkItemId,UserId,AddedByUserId) VALUES (@TenantId,@ProjectId,@WorkItemId,@UserId,@ActorUserId);", new { TenantId = tenantId, ProjectId = projectId, WorkItemId = workItemId, UserId = followerId, ActorUserId = actorUserId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.ProjectWorkItemActivity (TenantId,ProjectId,WorkItemId,EventKind,Summary,ActorUserId) VALUES (@TenantId,@ProjectId,@WorkItemId,N'CollaborationChanged',N'Work Item ownership and attention were updated.',@ActorUserId);", args, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return new(ProjectWorkItemCollaborationMutationStatus.Succeeded, await ReadAsync(connection, tenantId, projectId, workItemId, cancellationToken));
    }

    private static async Task<bool> TicketExistsAsync(IDbConnection connection, IDbTransaction? transaction, int tenantId, int projectId, long workItemId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM dbo.ProjectTickets WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@WorkItemId AND IsDeleted=0;", new { TenantId = tenantId, ProjectId = projectId, WorkItemId = workItemId }, transaction, cancellationToken: cancellationToken)) > 0;

    private static async Task<ProjectWorkItemCollaborationSnapshot> ReadAsync(IDbConnection connection, int tenantId, int projectId, long workItemId, CancellationToken cancellationToken)
    {
        const string headerSql = """
            SELECT c.Revision, c.AssigneeUserId, au.DisplayName AssigneeDisplayName, c.WaitingOnUserId, wu.DisplayName WaitingOnDisplayName, c.WaitingOnKind, c.WaitingOnLabel
            FROM dbo.ProjectWorkItemCollaboration c
            LEFT JOIN dbo.Users au ON au.Id=c.AssigneeUserId LEFT JOIN dbo.Users wu ON wu.Id=c.WaitingOnUserId
            WHERE c.TenantId=@TenantId AND c.ProjectId=@ProjectId AND c.WorkItemId=@WorkItemId;
            """;
        var args = new { TenantId = tenantId, ProjectId = projectId, WorkItemId = workItemId };
        var header = await connection.QuerySingleOrDefaultAsync<CollaborationRow>(new CommandDefinition(headerSql, args, cancellationToken: cancellationToken));
        var followers = await connection.QueryAsync<UserRow>(new CommandDefinition("SELECT u.Id UserId,u.DisplayName FROM dbo.ProjectWorkItemFollowers f INNER JOIN dbo.Users u ON u.Id=f.UserId WHERE f.TenantId=@TenantId AND f.ProjectId=@ProjectId AND f.WorkItemId=@WorkItemId ORDER BY u.DisplayName;", args, cancellationToken: cancellationToken));
        var activity = await connection.QueryAsync<ActivityRow>(new CommandDefinition("SELECT TOP (20) a.CreatedUtc,a.EventKind,a.Summary,a.ActorUserId,u.DisplayName ActorDisplayName FROM dbo.ProjectWorkItemActivity a LEFT JOIN dbo.Users u ON u.Id=a.ActorUserId WHERE a.TenantId=@TenantId AND a.ProjectId=@ProjectId AND a.WorkItemId=@WorkItemId ORDER BY a.CreatedUtc DESC,a.Id DESC;", args, cancellationToken: cancellationToken));
        return new ProjectWorkItemCollaborationSnapshot
        {
            WorkItemId = workItemId,
            Revision = header?.Revision ?? 0,
            Assignee = header?.AssigneeUserId is int assigneeId ? new("Human", assigneeId, header.AssigneeDisplayName ?? $"User {assigneeId}") : null,
            Followers = followers.Select(row => new ProjectWorkItemCollaborator("Human", row.UserId, row.DisplayName)).ToArray(),
            WaitingOn = header?.WaitingOnUserId is int waitingId ? new("Human", waitingId, header.WaitingOnDisplayName ?? $"User {waitingId}") : string.IsNullOrWhiteSpace(header?.WaitingOnLabel) ? null : new(header!.WaitingOnKind ?? "Role", null, header.WaitingOnLabel!),
            RecentActivity = activity.Select(row => new ProjectWorkItemCollaborationActivity(row.CreatedUtc, row.EventKind, row.Summary, row.ActorUserId is int actorId ? new("Human", actorId, row.ActorDisplayName ?? $"User {actorId}") : null)).ToArray()
        };
    }

    private static string? TextOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private sealed class CollaborationRow { public long Revision { get; init; } public int? AssigneeUserId { get; init; } public string? AssigneeDisplayName { get; init; } public int? WaitingOnUserId { get; init; } public string? WaitingOnDisplayName { get; init; } public string? WaitingOnKind { get; init; } public string? WaitingOnLabel { get; init; } }
    private sealed class UserRow { public int UserId { get; init; } public string DisplayName { get; init; } = string.Empty; }
    private sealed class ActivityRow { public DateTimeOffset CreatedUtc { get; init; } public string EventKind { get; init; } = string.Empty; public string Summary { get; init; } = string.Empty; public int? ActorUserId { get; init; } public string? ActorDisplayName { get; init; } }
}

using System.Data;
using Dapper;
using IronDev.Core.Channels;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChannelMembershipService : IProjectChannelMembershipService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectChannelMembershipService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProjectChannelDirectoryEntry>> GetVisibleChannelsAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        bool canAdminister,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string channelSql = """
            SELECT c.Id AS ChannelId, c.Name, c.Description, c.ChannelKind, c.Visibility, c.Boundary
            FROM dbo.ProjectChannels c
            WHERE c.TenantId = @TenantId
              AND c.ProjectId = @ProjectId
              AND c.Status = 'Active'
              AND (
                    @CanAdminister = 1
                    OR c.Visibility = 'Project'
                    OR EXISTS (
                        SELECT 1
                        FROM dbo.ProjectChannelMembers mine
                        WHERE mine.TenantId = c.TenantId
                          AND mine.ProjectId = c.ProjectId
                          AND mine.ChannelId = c.Id
                          AND mine.UserId = @CurrentUserId
                          AND mine.Status = 'Active'))
            ORDER BY CASE c.ChannelKind WHEN 'General' THEN 0 ELSE 1 END, c.Name, c.Id;
            """;

        var channels = (await connection.QueryAsync<ChannelRow>(new CommandDefinition(
            channelSql,
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId, CanAdminister = canAdminister },
            cancellationToken: cancellationToken))).ToArray();

        if (channels.Length == 0)
            return [];

        const string memberSql = """
            SELECT ChannelId, UserId, ChannelRole, NotificationLevel, Revision
            FROM dbo.ProjectChannelMembers
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND Status = 'Active'
              AND ChannelId IN @ChannelIds
            ORDER BY ChannelId, UserId;
            """;
        var memberships = (await connection.QueryAsync<MembershipRow>(new CommandDefinition(
            memberSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelIds = channels.Select(channel => channel.ChannelId).ToArray() },
            cancellationToken: cancellationToken))).ToArray();

        return channels.Select(channel =>
        {
            var members = memberships.Where(member => member.ChannelId == channel.ChannelId).ToArray();
            return new ProjectChannelDirectoryEntry(
                channel.ChannelId,
                channel.Name,
                channel.Description,
                channel.ChannelKind,
                channel.Visibility,
                members.Length,
                members.Select(member => new IronDev.Core.Models.ProjectChannelMembershipEntry(
                    member.UserId,
                    member.ChannelRole,
                    member.NotificationLevel,
                    member.Revision)).ToArray(),
                channel.Boundary);
        }).ToArray();
    }

    public async Task<ProjectChannelMembershipMutationStatus> SetMembershipAsync(
        int tenantId,
        int projectId,
        long channelId,
        int userId,
        int actorUserId,
        string channelRole,
        string notificationLevel,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var scopeStatus = await ValidateScopeAsync(
            connection, transaction, tenantId, projectId, channelId, userId, cancellationToken).ConfigureAwait(false);
        if (scopeStatus != ProjectChannelMembershipMutationStatus.Succeeded)
            return scopeStatus;

        const string currentSql = """
            SELECT TOP (1) ChannelRole, Revision
            FROM dbo.ProjectChannelMembers WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND ChannelId = @ChannelId
              AND UserId = @UserId AND Status = 'Active';
            """;
        var current = await connection.QuerySingleOrDefaultAsync<MembershipVersionRow>(new CommandDefinition(
            currentSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, UserId = userId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if ((current is null && expectedRevision != 0) || (current is not null && current.Revision != expectedRevision))
            return ProjectChannelMembershipMutationStatus.StaleWrite;

        if (string.Equals(current?.ChannelRole, ProjectChannelRoles.Owner, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(channelRole, ProjectChannelRoles.Owner, StringComparison.OrdinalIgnoreCase) &&
            await IsLastOwnerAsync(connection, transaction, tenantId, projectId, channelId, cancellationToken).ConfigureAwait(false))
            return ProjectChannelMembershipMutationStatus.LastOwnerProtected;

        if (current is not null)
        {
            const string updateSql = """
                UPDATE dbo.ProjectChannelMembers
                SET ChannelRole = @ChannelRole, NotificationLevel = @NotificationLevel, Revision = Revision + 1
                WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND ChannelId = @ChannelId
                  AND UserId = @UserId AND Status = 'Active' AND Revision = @ExpectedRevision;
                """;
            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, UserId = userId, ChannelRole = channelRole, NotificationLevel = notificationLevel, ExpectedRevision = expectedRevision },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            const string insertSql = """
                INSERT INTO dbo.ProjectChannelMembers
                    (TenantId, ProjectId, ChannelId, UserId, ChannelRole, NotificationLevel, Status, AddedByUserId)
                VALUES
                    (@TenantId, @ProjectId, @ChannelId, @UserId, @ChannelRole, @NotificationLevel, 'Active', @ActorUserId);
                """;
            await connection.ExecuteAsync(new CommandDefinition(
                insertSql,
                new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, UserId = userId, ChannelRole = channelRole, NotificationLevel = notificationLevel, ActorUserId = actorUserId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        transaction.Commit();
        return ProjectChannelMembershipMutationStatus.Succeeded;
    }

    public async Task<ProjectChannelMembershipMutationStatus> RemoveMembershipAsync(
        int tenantId,
        int projectId,
        long channelId,
        int userId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var scopeStatus = await ValidateScopeAsync(
            connection, transaction, tenantId, projectId, channelId, userId, cancellationToken).ConfigureAwait(false);
        if (scopeStatus != ProjectChannelMembershipMutationStatus.Succeeded)
            return scopeStatus;

        const string currentSql = """
            SELECT TOP (1) ChannelRole, Revision
            FROM dbo.ProjectChannelMembers WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND ChannelId = @ChannelId
              AND UserId = @UserId AND Status = 'Active';
            """;
        var current = await connection.QuerySingleOrDefaultAsync<MembershipVersionRow>(new CommandDefinition(
            currentSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, UserId = userId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (current is null)
            return ProjectChannelMembershipMutationStatus.MembershipNotFound;

        if (current.Revision != expectedRevision)
            return ProjectChannelMembershipMutationStatus.StaleWrite;

        if (string.Equals(current.ChannelRole, ProjectChannelRoles.Owner, StringComparison.OrdinalIgnoreCase) &&
            await IsLastOwnerAsync(connection, transaction, tenantId, projectId, channelId, cancellationToken).ConfigureAwait(false))
            return ProjectChannelMembershipMutationStatus.LastOwnerProtected;

        const string removeSql = """
            UPDATE dbo.ProjectChannelMembers
            SET Status = 'Removed', RemovedUtc = SYSUTCDATETIME(), Revision = Revision + 1
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND ChannelId = @ChannelId
              AND UserId = @UserId AND Status = 'Active' AND Revision = @ExpectedRevision;
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            removeSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, UserId = userId, ExpectedRevision = expectedRevision },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        transaction.Commit();
        return ProjectChannelMembershipMutationStatus.Succeeded;
    }

    public async Task<ProjectChannelMembershipVersion?> GetMembershipAsync(
        int tenantId,
        int projectId,
        long channelId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT UserId, ChannelRole, NotificationLevel, Revision
            FROM dbo.ProjectChannelMembers
            WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND ChannelId=@ChannelId AND UserId=@UserId AND Status='Active';
            """;
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectChannelMembershipVersion>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, UserId = userId },
            cancellationToken: cancellationToken));
    }

    private static async Task<ProjectChannelMembershipMutationStatus> ValidateScopeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        long channelId,
        int userId,
        CancellationToken cancellationToken)
    {
        const string channelSql = """
            SELECT COUNT(1)
            FROM dbo.ProjectChannels WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = @ChannelId AND TenantId = @TenantId AND ProjectId = @ProjectId AND Status = 'Active';
            """;
        var channelExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            channelSql,
            new { ChannelId = channelId, TenantId = tenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (channelExists == 0)
            return ProjectChannelMembershipMutationStatus.ChannelNotFound;

        const string userSql = """
            SELECT COUNT(1)
            FROM dbo.TenantUsers tu
            INNER JOIN dbo.Users u ON u.Id = tu.UserId AND u.IsActive = 1
            WHERE tu.TenantId = @TenantId AND tu.UserId = @UserId;
            """;
        var userExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            userSql,
            new { TenantId = tenantId, UserId = userId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return userExists == 0
            ? ProjectChannelMembershipMutationStatus.TargetUserNotTenantMember
            : ProjectChannelMembershipMutationStatus.Succeeded;
    }

    private static async Task<bool> IsLastOwnerAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int tenantId,
        int projectId,
        long channelId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.ProjectChannelMembers WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND ChannelId = @ChannelId
              AND Status = 'Active' AND ChannelRole = 'Owner';
            """;
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false) <= 1;
    }

    private sealed class ChannelRow
    {
        public long ChannelId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string ChannelKind { get; init; } = string.Empty;
        public string Visibility { get; init; } = string.Empty;
        public string Boundary { get; init; } = ProjectChannelBoundaries.Channel;
    }

    private sealed class MembershipRow
    {
        public long ChannelId { get; init; }
        public int UserId { get; init; }
        public string ChannelRole { get; init; } = ProjectChannelRoles.Member;
        public string NotificationLevel { get; init; } = ProjectChannelNotificationLevels.Mentions;
        public long Revision { get; init; }
    }

    private sealed class MembershipVersionRow
    {
        public string ChannelRole { get; init; } = string.Empty;
        public long Revision { get; init; }
    }
}

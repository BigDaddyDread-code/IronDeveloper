using System.Data;
using System.Text;
using Dapper;
using IronDev.Core.Channels;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChannelChatService : IProjectChannelChatService
{
    private const string AssistantStatus =
        "Not implemented. Shared channels persist human conversation only; IronDev does not participate yet.";

    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectChannelChatService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProjectChannelChatSummary>> ListVisibleChannelsAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ChannelRow>(new CommandDefinition(
            VisibleChannelsSql + " ORDER BY CASE c.ChannelKind WHEN 'General' THEN 0 ELSE 1 END, c.Name, c.Id;",
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId },
            cancellationToken: cancellationToken));
        return rows.Select(MapSummary).ToArray();
    }

    public async Task<ProjectChannelChatDetail?> GetChannelAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var channel = await connection.QuerySingleOrDefaultAsync<ChannelRow>(new CommandDefinition(
            VisibleChannelsSql + " AND (c.Slug = @ChannelReference OR CONVERT(NVARCHAR(30), c.Id) = @ChannelReference);",
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId, ChannelReference = channelReference },
            cancellationToken: cancellationToken));
        if (channel is null)
            return null;

        const string messageSql = """
            SELECT ordered.MessageId, ordered.AuthorUserId,
                   CASE ordered.Role
                       WHEN 'Assistant' THEN 'IronDev'
                       WHEN 'SystemNotice' THEN 'System'
                       ELSE COALESCE(u.DisplayName, 'Former member')
                   END AS AuthorDisplayName,
                   ordered.Role, ordered.Message, ordered.MessageFormat, ordered.Status,
                   ordered.ReplyToMessageId, ordered.ThreadRootMessageId,
                   ordered.CreatedUtc, ordered.EditedUtc, ordered.Boundary
            FROM (
                SELECT TOP (100)
                       m.Id AS MessageId, m.AuthorUserId, m.Role, m.Message, m.MessageFormat, m.Status,
                       m.ReplyToMessageId, m.ThreadRootMessageId, m.CreatedUtc, m.EditedUtc, m.Boundary
                FROM dbo.ProjectChannelMessages m
                WHERE m.TenantId = @TenantId AND m.ProjectId = @ProjectId AND m.ChannelId = @ChannelId
                  AND m.Status <> 'Deleted'
                ORDER BY m.CreatedUtc DESC, m.Id DESC
            ) ordered
            LEFT JOIN dbo.Users u ON u.Id = ordered.AuthorUserId
            ORDER BY ordered.CreatedUtc, ordered.MessageId;
            """;
        var messages = (await connection.QueryAsync<ProjectChannelChatMessage>(new CommandDefinition(
            messageSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channel.ChannelId },
            cancellationToken: cancellationToken))).ToArray();

        var summary = MapSummary(channel);
        return new ProjectChannelChatDetail(
            summary,
            messages,
            new ProjectChannelReadState(
                summary.UnreadCount,
                summary.LastReadMessageId,
                summary.LastReadUtc,
                summary.CurrentUserNotificationLevel ?? ProjectChannelNotificationLevels.Mentions,
                ProjectChannelBoundaries.ReadMarker),
            new ProjectChannelPresenceState(
                "Unavailable",
                null,
                "Live channel presence is not implemented; no active viewer count is inferred.",
                ProjectChannelBoundaries.Presence),
            AssistantStatus,
            ProjectChannelBoundaries.Channel);
    }

    public async Task<ProjectChannelChatMutationResult> CreateChannelAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string name,
        string? description,
        string visibility,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

        var normalizedName = name.Trim();
        var duplicate = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.ProjectChannels WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Status = 'Active'
              AND LOWER(Name) = LOWER(@Name);
            """,
            new { TenantId = tenantId, ProjectId = projectId, Name = normalizedName },
            transaction,
            cancellationToken: cancellationToken));
        if (duplicate > 0)
            return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.DuplicateName);

        var slugBase = Slugify(normalizedName);
        var slug = slugBase;
        var suffix = 2;
        while (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.ProjectChannels WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Status = 'Active' AND Slug = @Slug;
            """,
            new { TenantId = tenantId, ProjectId = projectId, Slug = slug },
            transaction,
            cancellationToken: cancellationToken)) > 0)
        {
            slug = $"{slugBase}-{suffix++}";
        }

        const string channelSql = """
            INSERT INTO dbo.ProjectChannels
                (TenantId, ProjectId, Name, Slug, Description, ChannelKind, Visibility, Status, CreatedByUserId)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ProjectId, @Name, @Slug, @Description, 'Custom', @Visibility, 'Active', @CurrentUserId);
            """;
        var channelId = await connection.QuerySingleAsync<long>(new CommandDefinition(
            channelSql,
            new { TenantId = tenantId, ProjectId = projectId, Name = normalizedName, Slug = slug, Description = description?.Trim(), Visibility = visibility, CurrentUserId = currentUserId },
            transaction,
            cancellationToken: cancellationToken));

        const string membershipSql = """
            INSERT INTO dbo.ProjectChannelMembers
                (TenantId, ProjectId, ChannelId, UserId, ChannelRole, NotificationLevel, Status, AddedByUserId)
            VALUES
                (@TenantId, @ProjectId, @ChannelId, @CurrentUserId, 'Owner', 'Mentions', 'Active', @CurrentUserId);
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            membershipSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, CurrentUserId = currentUserId },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return new ProjectChannelChatMutationResult(
            ProjectChannelChatMutationStatus.Succeeded,
            Channel: new ProjectChannelChatSummary(
                channelId,
                normalizedName,
                slug,
                description?.Trim(),
                ProjectChannelKinds.Custom,
                visibility,
                1,
                ProjectChannelRoles.Owner,
                ProjectChannelNotificationLevels.Mentions,
                0,
                null,
                null,
                true,
                ProjectChannelBoundaries.Channel));
    }

    public async Task<ProjectChannelChatMutationResult> PostHumanMessageAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        string message,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessage = message.Trim();
        using var connection = _connectionFactory.CreateConnection();
        var channel = await connection.QuerySingleOrDefaultAsync<ChannelRow>(new CommandDefinition(
            VisibleChannelsSql + " AND (c.Slug = @ChannelReference OR CONVERT(NVARCHAR(30), c.Id) = @ChannelReference);",
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId, ChannelReference = channelReference },
            cancellationToken: cancellationToken));
        if (channel is null)
            return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.NotFound);
        if (string.Equals(channel.CurrentUserRole, ProjectChannelRoles.ReadOnly, StringComparison.OrdinalIgnoreCase))
            return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.ReadOnly);
        if (normalizedMessage.Contains("@IronDev", StringComparison.OrdinalIgnoreCase))
            return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.AssistantInvocationNotImplemented);

        const string insertSql = """
            INSERT INTO dbo.ProjectChannelMessages
                (TenantId, ProjectId, ChannelId, AuthorUserId, Role, Message, MessageFormat, Status)
            OUTPUT inserted.Id AS MessageId, inserted.AuthorUserId,
                   @AuthorDisplayName AS AuthorDisplayName,
                   inserted.Role, inserted.Message, inserted.MessageFormat, inserted.Status,
                   inserted.ReplyToMessageId, inserted.ThreadRootMessageId,
                   inserted.CreatedUtc, inserted.EditedUtc, inserted.Boundary
            VALUES
                (@TenantId, @ProjectId, @ChannelId, @CurrentUserId, 'User', @Message, 'Markdown', 'Active');
            """;
        var authorDisplayName = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT DisplayName FROM dbo.Users WHERE Id = @CurrentUserId AND IsActive = 1;",
            new { CurrentUserId = currentUserId },
            cancellationToken: cancellationToken)) ?? "Former member";
        var saved = await connection.QuerySingleAsync<ProjectChannelChatMessage>(new CommandDefinition(
            insertSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channel.ChannelId, CurrentUserId = currentUserId, Message = normalizedMessage, AuthorDisplayName = authorDisplayName },
            cancellationToken: cancellationToken));
        return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.Succeeded, Message: saved);
    }

    public async Task<ProjectChannelChatMutationResult> MarkReadAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var channel = await connection.QuerySingleOrDefaultAsync<ChannelRow>(new CommandDefinition(
            VisibleChannelsSql + " AND (c.Slug = @ChannelReference OR CONVERT(NVARCHAR(30), c.Id) = @ChannelReference);",
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId, ChannelReference = channelReference },
            transaction,
            cancellationToken: cancellationToken));
        if (channel is null)
        {
            transaction.Rollback();
            return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.NotFound);
        }

        const string markReadSql = """
            DECLARE @LatestMessageId BIGINT = (
                SELECT MAX(Id)
                FROM dbo.ProjectChannelMessages
                WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND ChannelId = @ChannelId
                  AND Status <> 'Deleted'
            );
            DECLARE @ReadUtc DATETIME2 = SYSUTCDATETIME();

            UPDATE dbo.ProjectChannelMessageReads
            SET LastReadMessageId = @LatestMessageId, LastReadUtc = @ReadUtc
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId
              AND ChannelId = @ChannelId AND UserId = @CurrentUserId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.ProjectChannelMessageReads
                    (TenantId, ProjectId, ChannelId, UserId, LastReadMessageId, LastReadUtc)
                VALUES
                    (@TenantId, @ProjectId, @ChannelId, @CurrentUserId, @LatestMessageId, @ReadUtc);
            END;

            SELECT @LatestMessageId AS LastReadMessageId, @ReadUtc AS LastReadUtc;
            """;
        var marker = await connection.QuerySingleAsync<ReadMarkerRow>(new CommandDefinition(
            markReadSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channel.ChannelId, CurrentUserId = currentUserId },
            transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();

        return new ProjectChannelChatMutationResult(
            ProjectChannelChatMutationStatus.Succeeded,
            ReadState: new ProjectChannelReadState(
                0,
                marker.LastReadMessageId,
                marker.LastReadUtc,
                channel.CurrentUserNotificationLevel ?? ProjectChannelNotificationLevels.Mentions,
                ProjectChannelBoundaries.ReadMarker));
    }

    private static ProjectChannelChatSummary MapSummary(ChannelRow row) => new(
        row.ChannelId,
        row.Name,
        row.Slug,
        row.Description,
        row.ChannelKind,
        row.Visibility,
        row.MemberCount,
        row.CurrentUserRole,
        row.CurrentUserNotificationLevel,
        row.UnreadCount,
        row.LastReadMessageId,
        row.LastReadUtc,
        !string.Equals(row.CurrentUserRole, ProjectChannelRoles.ReadOnly, StringComparison.OrdinalIgnoreCase),
        row.Boundary);

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var separatorPending = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && builder.Length > 0)
                    builder.Append('-');
                builder.Append(character);
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }

        return builder.Length == 0 ? "channel" : builder.ToString();
    }

    private const string VisibleChannelsSql = """
        SELECT c.Id AS ChannelId, c.Name, c.Slug, c.Description, c.ChannelKind, c.Visibility,
               c.Boundary, mine.ChannelRole AS CurrentUserRole,
               mine.NotificationLevel AS CurrentUserNotificationLevel,
               marker.LastReadMessageId, marker.LastReadUtc,
               (SELECT COUNT(1) FROM dbo.ProjectChannelMessages unread
                WHERE unread.TenantId = c.TenantId AND unread.ProjectId = c.ProjectId
                  AND unread.ChannelId = c.Id AND unread.Status <> 'Deleted'
                  AND unread.Id > COALESCE(marker.LastReadMessageId, 0)
                  AND (unread.AuthorUserId IS NULL OR unread.AuthorUserId <> @CurrentUserId)) AS UnreadCount,
               (SELECT COUNT(1) FROM dbo.ProjectChannelMembers members
                WHERE members.TenantId = c.TenantId AND members.ProjectId = c.ProjectId
                  AND members.ChannelId = c.Id AND members.Status = 'Active') AS MemberCount
        FROM dbo.ProjectChannels c
        OUTER APPLY (
            SELECT TOP (1) member.ChannelRole, member.NotificationLevel
            FROM dbo.ProjectChannelMembers member
            WHERE member.TenantId = c.TenantId AND member.ProjectId = c.ProjectId
              AND member.ChannelId = c.Id AND member.UserId = @CurrentUserId AND member.Status = 'Active'
        ) mine
        OUTER APPLY (
            SELECT TOP (1) reads.LastReadMessageId, reads.LastReadUtc
            FROM dbo.ProjectChannelMessageReads reads
            WHERE reads.TenantId = c.TenantId AND reads.ProjectId = c.ProjectId
              AND reads.ChannelId = c.Id AND reads.UserId = @CurrentUserId
        ) marker
        WHERE c.TenantId = @TenantId AND c.ProjectId = @ProjectId AND c.Status = 'Active'
          AND (c.Visibility = 'Project' OR mine.ChannelRole IS NOT NULL)
        """;

    private sealed class ChannelRow
    {
        public long ChannelId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string ChannelKind { get; init; } = string.Empty;
        public string Visibility { get; init; } = string.Empty;
        public int MemberCount { get; init; }
        public string? CurrentUserRole { get; init; }
        public string? CurrentUserNotificationLevel { get; init; }
        public int UnreadCount { get; init; }
        public long? LastReadMessageId { get; init; }
        public DateTime? LastReadUtc { get; init; }
        public string Boundary { get; init; } = ProjectChannelBoundaries.Channel;
    }

    private sealed class ReadMarkerRow
    {
        public long? LastReadMessageId { get; init; }
        public DateTime LastReadUtc { get; init; }
    }
}

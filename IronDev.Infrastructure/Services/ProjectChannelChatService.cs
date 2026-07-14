using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.Chat;
using IronDev.Core.Channels;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChannelChatService : IProjectChannelChatService
{
    private const string AssistantStatus =
        "IronDev responds in shared channels only when a message explicitly mentions @IronDev.";

    private static readonly Regex AssistantMention = new(
        @"(?<![A-Za-z0-9_])@IronDev\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PersonMention = new(
        @"(?<![A-Za-z0-9_])@([A-Za-z0-9][A-Za-z0-9._-]{0,63})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IProjectChatResponseService _projectChat;
    private readonly ILogger<ProjectChannelChatService> _logger;

    public ProjectChannelChatService(
        IDbConnectionFactory connectionFactory,
        IProjectChatResponseService projectChat,
        ILogger<ProjectChannelChatService> logger)
    {
        _connectionFactory = connectionFactory;
        _projectChat = projectChat;
        _logger = logger;
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

        var assistantTurns = (await connection.QueryAsync<ProjectChannelAssistantTurnState>(new CommandDefinition(
            AssistantTurnSelectSql + " WHERE t.TenantId = @TenantId AND t.ProjectId = @ProjectId AND t.ChannelId = @ChannelId ORDER BY t.CreatedUtc, t.Id;",
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channel.ChannelId },
            cancellationToken: cancellationToken))).ToArray();
        var mentionRows = await GetMentionCandidateRowsAsync(
            connection, null, tenantId, projectId, channel.ChannelId, channel.Visibility, currentUserId, cancellationToken);
        var mentionCandidates = BuildMentionCandidates(mentionRows)
            .Select(candidate => new ProjectChannelMentionCandidate(candidate.UserId, candidate.DisplayName, candidate.Handle))
            .ToArray();

        var summary = MapSummary(channel);
        return new ProjectChannelChatDetail(
            summary,
            messages,
            assistantTurns,
            mentionCandidates,
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

    public async Task<ProjectChannelChatMutationResult> PostMessageAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        string message,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessage = message.Trim();
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
        if (string.Equals(channel.CurrentUserRole, ProjectChannelRoles.ReadOnly, StringComparison.OrdinalIgnoreCase))
        {
            transaction.Rollback();
            return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.ReadOnly);
        }

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
            transaction,
            cancellationToken: cancellationToken)) ?? "Former member";
        var saved = await connection.QuerySingleAsync<ProjectChannelChatMessage>(new CommandDefinition(
            insertSql,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channel.ChannelId, CurrentUserId = currentUserId, Message = normalizedMessage, AuthorDisplayName = authorDisplayName },
            transaction,
            cancellationToken: cancellationToken));

        var mentionRows = await GetMentionCandidateRowsAsync(
            connection, transaction, tenantId, projectId, channel.ChannelId, channel.Visibility, currentUserId, cancellationToken);
        var candidates = BuildMentionCandidates(mentionRows);
        var mentionedHandles = PersonMention.Matches(normalizedMessage)
            .Select(match => match.Groups[1].Value)
            .Where(handle => !handle.Equals("IronDev", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mentionedUsers = candidates
            .Where(candidate => mentionedHandles.Contains(candidate.Handle))
            .ToArray();
        if (mentionedUsers.Length > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ProjectChannelMessageMentions
                    (TenantId, ProjectId, ChannelId, MessageId, MentionedUserId, MentionedByUserId)
                VALUES
                    (@TenantId, @ProjectId, @ChannelId, @MessageId, @MentionedUserId, @MentionedByUserId);
                """,
                mentionedUsers.Select(candidate => new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    ChannelId = channel.ChannelId,
                    MessageId = saved.MessageId,
                    MentionedUserId = candidate.UserId,
                    MentionedByUserId = currentUserId
                }),
                transaction,
                cancellationToken: cancellationToken));
        }

        var notificationRows = candidates
            .Where(candidate =>
                candidate.NotificationLevel.Equals(ProjectChannelNotificationLevels.All, StringComparison.OrdinalIgnoreCase)
                || (mentionedUsers.Any(mentioned => mentioned.UserId == candidate.UserId)
                    && candidate.NotificationLevel.Equals(ProjectChannelNotificationLevels.Mentions, StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                RecipientUserId = candidate.UserId,
                ActorUserId = currentUserId,
                Kind = mentionedUsers.Any(mentioned => mentioned.UserId == candidate.UserId) ? "Mention" : "ChannelMessage",
                ChannelId = channel.ChannelId,
                MessageId = saved.MessageId,
                Title = mentionedUsers.Any(mentioned => mentioned.UserId == candidate.UserId)
                    ? $"{authorDisplayName} mentioned you in #{channel.Slug}"
                    : $"New message in #{channel.Slug}",
                Body = normalizedMessage.Length <= 240 ? normalizedMessage : normalizedMessage[..240]
            })
            .ToArray();
        if (notificationRows.Length > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.ProjectNotifications
                    (TenantId, ProjectId, RecipientUserId, ActorUserId, Kind, ChannelId, MessageId, Title, Body)
                VALUES
                    (@TenantId, @ProjectId, @RecipientUserId, @ActorUserId, @Kind, @ChannelId, @MessageId, @Title, @Body);
                """,
                notificationRows,
                transaction,
                cancellationToken: cancellationToken));
        }

        ProjectChannelAssistantTurnState? assistantTurn = null;
        if (AssistantMention.IsMatch(normalizedMessage))
        {
            var prompt = AssistantMention.Replace(normalizedMessage, string.Empty).Trim();
            var status = string.IsNullOrWhiteSpace(prompt)
                ? ProjectChannelAssistantTurnStatus.Refused
                : ProjectChannelAssistantTurnStatus.Requested;
            var failureReason = string.IsNullOrWhiteSpace(prompt)
                ? "Add a question or request after @IronDev."
                : null;
            var durablePrompt = string.IsNullOrWhiteSpace(prompt) ? normalizedMessage : prompt;

            const string insertTurnSql = """
                INSERT INTO dbo.ProjectChannelAssistantTurns
                    (TenantId, ProjectId, ChannelId, RequestMessageId, RequestedByUserId, Prompt, Status, FailureReason, CompletedUtc)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @ChannelId, @RequestMessageId, @CurrentUserId, @Prompt, @Status, @FailureReason,
                     CASE WHEN @Status = 'Refused' THEN SYSUTCDATETIME() ELSE NULL END);
                """;
            var turnId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                insertTurnSql,
                new
                {
                    TenantId = tenantId,
                    ProjectId = projectId,
                    ChannelId = channel.ChannelId,
                    RequestMessageId = saved.MessageId,
                    CurrentUserId = currentUserId,
                    Prompt = durablePrompt,
                    Status = status,
                    FailureReason = failureReason
                },
                transaction,
                cancellationToken: cancellationToken));
            assistantTurn = await GetAssistantTurnAsync(connection, transaction, tenantId, projectId, turnId, cancellationToken);
        }

        transaction.Commit();
        var postMessage = new ProjectChannelPostMessageResult(saved, assistantTurn);
        return new ProjectChannelChatMutationResult(
            ProjectChannelChatMutationStatus.Succeeded,
            Message: saved,
            PostMessage: postMessage);
    }

    public async Task<ProjectChannelChatMutationResult> CompleteAssistantTurnAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        long turnId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var lockResource = $"ProjectChannelAssistantTurn:{tenantId}:{projectId}:{turnId}";
        var lockResult = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "DECLARE @Result INT; EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 10000; SELECT @Result;",
            new { Resource = lockResource },
            cancellationToken: cancellationToken));
        if (lockResult < 0)
            throw new InvalidOperationException("The assistant turn is already being completed. Try again shortly.");

        try
        {
            var turn = await GetVisibleAssistantTurnAsync(
                connection, tenantId, projectId, currentUserId, channelReference, turnId, cancellationToken);
            if (turn is null)
                return new ProjectChannelChatMutationResult(ProjectChannelChatMutationStatus.NotFound);

            if (string.Equals(turn.Status, ProjectChannelAssistantTurnStatus.Answered, StringComparison.OrdinalIgnoreCase)
                || string.Equals(turn.Status, ProjectChannelAssistantTurnStatus.Refused, StringComparison.OrdinalIgnoreCase))
            {
                var existingResponse = turn.ResponseMessageId.HasValue
                    ? await GetMessageAsync(connection, tenantId, projectId, turn.ResponseMessageId.Value, cancellationToken)
                    : null;
                return Completed(turn, existingResponse);
            }

            if (string.Equals(turn.Status, ProjectChannelAssistantTurnStatus.Failed, StringComparison.OrdinalIgnoreCase))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.ProjectChannelAssistantTurns SET Status = 'Requested', FailureReason = NULL, CompletedUtc = NULL WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Id = @TurnId;",
                    new { TenantId = tenantId, ProjectId = projectId, TurnId = turnId },
                    cancellationToken: cancellationToken));
                turn = turn with { Status = ProjectChannelAssistantTurnStatus.Requested, FailureReason = null, CompletedUtc = null };
            }

            var recentConversationSummary = await BuildRecentChannelSummaryAsync(
                connection, tenantId, projectId, turn.RequestMessageId, cancellationToken);
            try
            {
                var answer = await _projectChat.RespondAsync(
                    projectId,
                    turn.Prompt,
                    MemoryRetrievalRequestContext.ForProjectChat(tenantId, projectId, currentUserId, "ProjectChannelChat"),
                    recentConversationSummary: recentConversationSummary,
                    sourceMessageId: turn.RequestMessageId,
                    cancellationToken: cancellationToken);
                if (answer is null)
                {
                    var refused = await SetTerminalTurnAsync(
                        connection,
                        tenantId,
                        projectId,
                        turnId,
                        ProjectChannelAssistantTurnStatus.Refused,
                        "IronDev could not access the requested project context.",
                        cancellationToken);
                    return Completed(refused, null);
                }

                var revalidated = await GetVisibleAssistantTurnAsync(
                    connection, tenantId, projectId, currentUserId, channelReference, turnId, cancellationToken);
                if (revalidated is null)
                {
                    var refused = await SetTerminalTurnAsync(
                        connection,
                        tenantId,
                        projectId,
                        turnId,
                        ProjectChannelAssistantTurnStatus.Refused,
                        "Channel access changed before IronDev could persist the answer.",
                        cancellationToken);
                    return Completed(refused, null);
                }

                return await SaveAssistantAnswerAsync(connection, tenantId, projectId, revalidated, answer, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _logger.LogError(error, "Shared-channel assistant turn {TurnId} failed.", turnId);
                var failed = await SetTerminalTurnAsync(
                    connection,
                    tenantId,
                    projectId,
                    turnId,
                    ProjectChannelAssistantTurnStatus.Failed,
                    "IronDev could not answer this request. Your message is saved.",
                    cancellationToken);
                return Completed(failed, null);
            }
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "EXEC sys.sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';",
                new { Resource = lockResource },
                cancellationToken: CancellationToken.None));
        }
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

    public async Task<ProjectNotificationListResponse> ListNotificationsAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            SELECT TOP (50)
                   n.Id AS NotificationId, n.Kind, n.ChannelId, c.Name AS ChannelName, c.Slug AS ChannelSlug,
                   n.MessageId, n.ActorUserId, actor.DisplayName AS ActorDisplayName,
                   n.Title, n.Body, CAST(CASE WHEN n.Status = 'Read' THEN 1 ELSE 0 END AS bit) AS IsRead,
                   n.CreatedUtc, n.ReadUtc, n.Boundary
            FROM dbo.ProjectNotifications n
            LEFT JOIN dbo.ProjectChannels c ON c.Id = n.ChannelId AND c.TenantId = n.TenantId AND c.ProjectId = n.ProjectId
            LEFT JOIN dbo.Users actor ON actor.Id = n.ActorUserId
            WHERE n.TenantId = @TenantId AND n.ProjectId = @ProjectId AND n.RecipientUserId = @CurrentUserId
            ORDER BY n.CreatedUtc DESC, n.Id DESC;
            """;
        var notifications = (await connection.QueryAsync<ProjectNotificationSummary>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId },
            cancellationToken: cancellationToken))).ToArray();
        return new ProjectNotificationListResponse(
            notifications.Count(notification => !notification.IsRead),
            notifications,
            ProjectChannelBoundaries.Notification);
    }

    public async Task<bool> MarkNotificationReadAsync(
        int tenantId,
        int projectId,
        int currentUserId,
        long notificationId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ProjectNotifications
            SET Status = 'Read', ReadUtc = COALESCE(ReadUtc, SYSUTCDATETIME())
            WHERE Id = @NotificationId AND TenantId = @TenantId AND ProjectId = @ProjectId
              AND RecipientUserId = @CurrentUserId;
            """,
            new { NotificationId = notificationId, TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId },
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    private static async Task<IReadOnlyList<MentionCandidateRow>> GetMentionCandidateRowsAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        long channelId,
        string visibility,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<MentionCandidateRow>(new CommandDefinition(
            """
            SELECT u.Id AS UserId, u.DisplayName,
                   COALESCE(member.NotificationLevel, 'Mentions') AS NotificationLevel
            FROM dbo.TenantUsers tenantMember
            JOIN dbo.Users u ON u.Id = tenantMember.UserId AND u.IsActive = 1
            OUTER APPLY (
                SELECT TOP (1) channelMember.NotificationLevel
                FROM dbo.ProjectChannelMembers channelMember
                WHERE channelMember.TenantId = @TenantId AND channelMember.ProjectId = @ProjectId
                  AND channelMember.ChannelId = @ChannelId AND channelMember.UserId = u.Id
                  AND channelMember.Status = 'Active'
            ) member
            WHERE tenantMember.TenantId = @TenantId AND u.Id <> @CurrentUserId
              AND (@Visibility = 'Project' OR member.NotificationLevel IS NOT NULL)
            ORDER BY u.DisplayName, u.Id;
            """,
            new { TenantId = tenantId, ProjectId = projectId, ChannelId = channelId, Visibility = visibility, CurrentUserId = currentUserId },
            transaction,
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    private static IReadOnlyList<MentionCandidate> BuildMentionCandidates(IReadOnlyList<MentionCandidateRow> rows)
    {
        var provisional = rows
            .Select(row => new { Row = row, BaseHandle = MentionHandle(row.DisplayName, row.UserId) })
            .ToArray();
        var duplicates = provisional
            .GroupBy(item => item.BaseHandle, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return provisional
            .Select(item => new MentionCandidate(
                item.Row.UserId,
                item.Row.DisplayName,
                duplicates.Contains(item.BaseHandle) ? $"{item.BaseHandle}-{item.Row.UserId}" : item.BaseHandle,
                item.Row.NotificationLevel))
            .ToArray();
    }

    private static string MentionHandle(string displayName, int userId)
    {
        var builder = new StringBuilder(displayName.Length);
        var separatorPending = false;
        foreach (var character in displayName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
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

        return builder.Length == 0 ? $"user-{userId}" : builder.ToString();
    }

    private async Task<ProjectChannelChatMutationResult> SaveAssistantAnswerAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        ProjectChannelAssistantTurnState turn,
        ProjectChatResponseResult answer,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        const string insertResponseSql = """
            INSERT INTO dbo.ProjectChannelMessages
                (TenantId, ProjectId, ChannelId, AuthorUserId, Role, Message, MessageFormat, Status,
                 ReplyToMessageId, ThreadRootMessageId, CorrelationId, CausationId)
            OUTPUT inserted.Id AS MessageId, inserted.AuthorUserId,
                   'IronDev' AS AuthorDisplayName,
                   inserted.Role, inserted.Message, inserted.MessageFormat, inserted.Status,
                   inserted.ReplyToMessageId, inserted.ThreadRootMessageId,
                   inserted.CreatedUtc, inserted.EditedUtc, inserted.Boundary
            VALUES
                (@TenantId, @ProjectId, @ChannelId, NULL, 'Assistant', @Answer, 'Markdown', 'Active',
                 @RequestMessageId, @RequestMessageId, @CorrelationId, @CausationId);
            """;
        var responseMessage = await connection.QuerySingleAsync<ProjectChannelChatMessage>(new CommandDefinition(
            insertResponseSql,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                turn.ChannelId,
                Answer = answer.Response,
                turn.RequestMessageId,
                CorrelationId = answer.DogfoodTraceId,
                CausationId = turn.TurnId.ToString()
            },
            transaction,
            cancellationToken: cancellationToken));

        var linkedDocumentIds = answer.DocumentSources is { Count: > 0 }
            ? string.Join(",", answer.DocumentSources.Select(source => source.DocumentVersionId))
            : null;
        const string updateTurnSql = """
            UPDATE dbo.ProjectChannelAssistantTurns
            SET ResponseMessageId = @ResponseMessageId,
                Answer = @Answer,
                Mode = @Mode,
                ModeConfidence = @ModeConfidence,
                ModeReason = @ModeReason,
                ContextSummary = @ContextSummary,
                LinkedFilePaths = @LinkedFilePaths,
                LinkedSymbols = @LinkedSymbols,
                LinkedDocumentIds = @LinkedDocumentIds,
                DogfoodTraceId = @DogfoodTraceId,
                TraceId = @TraceId,
                Status = 'Answered',
                FailureReason = NULL,
                CompletedUtc = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Id = @TurnId;
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            updateTurnSql,
            new
            {
                TenantId = tenantId,
                ProjectId = projectId,
                TurnId = turn.TurnId,
                ResponseMessageId = responseMessage.MessageId,
                Answer = answer.Response,
                answer.Mode,
                answer.ModeConfidence,
                answer.ModeReason,
                answer.ContextSummary,
                answer.LinkedFilePaths,
                answer.LinkedSymbols,
                LinkedDocumentIds = linkedDocumentIds,
                answer.DogfoodTraceId,
                answer.TraceId
            },
            transaction,
            cancellationToken: cancellationToken));
        var answeredTurn = await GetAssistantTurnAsync(
            connection, transaction, tenantId, projectId, turn.TurnId, cancellationToken);
        transaction.Commit();
        return Completed(answeredTurn, responseMessage);
    }

    private static async Task<ProjectChannelAssistantTurnState> SetTerminalTurnAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        long turnId,
        string status,
        string failureReason,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ProjectChannelAssistantTurns
            SET Status = @Status, FailureReason = @FailureReason, CompletedUtc = SYSUTCDATETIME()
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Id = @TurnId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, TurnId = turnId, Status = status, FailureReason = failureReason },
            cancellationToken: cancellationToken));
        return await GetAssistantTurnAsync(connection, null, tenantId, projectId, turnId, cancellationToken);
    }

    private static ProjectChannelChatMutationResult Completed(
        ProjectChannelAssistantTurnState turn,
        ProjectChannelChatMessage? responseMessage) =>
        new(
            ProjectChannelChatMutationStatus.Succeeded,
            AssistantCompletion: new ProjectChannelAssistantCompletionResult(turn, responseMessage));

    private static async Task<ProjectChannelAssistantTurnState?> GetVisibleAssistantTurnAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        int currentUserId,
        string channelReference,
        long turnId,
        CancellationToken cancellationToken)
    {
        var channel = await connection.QuerySingleOrDefaultAsync<ChannelRow>(new CommandDefinition(
            VisibleChannelsSql + " AND (c.Slug = @ChannelReference OR CONVERT(NVARCHAR(30), c.Id) = @ChannelReference);",
            new { TenantId = tenantId, ProjectId = projectId, CurrentUserId = currentUserId, ChannelReference = channelReference },
            cancellationToken: cancellationToken));
        if (channel is null
            || string.Equals(channel.CurrentUserRole, ProjectChannelRoles.ReadOnly, StringComparison.OrdinalIgnoreCase))
            return null;

        var turn = await connection.QuerySingleOrDefaultAsync<ProjectChannelAssistantTurnState>(new CommandDefinition(
            AssistantTurnSelectSql + " WHERE t.TenantId = @TenantId AND t.ProjectId = @ProjectId AND t.Id = @TurnId;",
            new { TenantId = tenantId, ProjectId = projectId, TurnId = turnId },
            cancellationToken: cancellationToken));
        if (turn is null)
            return null;
        return turn.ChannelId == channel.ChannelId && turn.RequestedByUserId == currentUserId
            ? turn
            : null;
    }

    private static Task<ProjectChannelAssistantTurnState> GetAssistantTurnAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        int tenantId,
        int projectId,
        long turnId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<ProjectChannelAssistantTurnState>(new CommandDefinition(
            AssistantTurnSelectSql + " WHERE t.TenantId = @TenantId AND t.ProjectId = @ProjectId AND t.Id = @TurnId;",
            new { TenantId = tenantId, ProjectId = projectId, TurnId = turnId },
            transaction,
            cancellationToken: cancellationToken));

    private static Task<ProjectChannelChatMessage?> GetMessageAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        long messageId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProjectChannelChatMessage>(new CommandDefinition(
            """
            SELECT m.Id AS MessageId, m.AuthorUserId,
                   CASE m.Role WHEN 'Assistant' THEN 'IronDev' WHEN 'SystemNotice' THEN 'System' ELSE COALESCE(u.DisplayName, 'Former member') END AS AuthorDisplayName,
                   m.Role, m.Message, m.MessageFormat, m.Status, m.ReplyToMessageId, m.ThreadRootMessageId,
                   m.CreatedUtc, m.EditedUtc, m.Boundary
            FROM dbo.ProjectChannelMessages m
            LEFT JOIN dbo.Users u ON u.Id = m.AuthorUserId
            WHERE m.TenantId = @TenantId AND m.ProjectId = @ProjectId AND m.Id = @MessageId AND m.Status <> 'Deleted';
            """,
            new { TenantId = tenantId, ProjectId = projectId, MessageId = messageId },
            cancellationToken: cancellationToken));

    private static async Task<string> BuildRecentChannelSummaryAsync(
        IDbConnection connection,
        int tenantId,
        int projectId,
        long requestMessageId,
        CancellationToken cancellationToken)
    {
        var lines = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT CONCAT(recent.Role, ': ', recent.Message)
            FROM (
                SELECT TOP (12) m.Id, m.Role, m.Message
                FROM dbo.ProjectChannelMessages m
                JOIN dbo.ProjectChannelMessages requested ON requested.Id = @RequestMessageId
                    AND requested.TenantId = @TenantId AND requested.ProjectId = @ProjectId
                WHERE m.TenantId = requested.TenantId AND m.ProjectId = requested.ProjectId
                  AND m.ChannelId = requested.ChannelId AND m.Id < requested.Id AND m.Status <> 'Deleted'
                ORDER BY m.Id DESC
            ) recent
            ORDER BY recent.Id;
            """,
            new { TenantId = tenantId, ProjectId = projectId, RequestMessageId = requestMessageId },
            cancellationToken: cancellationToken));
        return string.Join(Environment.NewLine, lines);
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

    private const string AssistantTurnSelectSql = """
        SELECT t.Id AS TurnId, t.ChannelId, t.RequestMessageId, t.ResponseMessageId,
               t.RequestedByUserId, COALESCE(u.DisplayName, 'Former member') AS RequestedByDisplayName,
               t.Prompt, t.Answer, t.Mode, t.ModeConfidence, t.ModeReason, t.ContextSummary,
               t.LinkedFilePaths, t.LinkedSymbols, t.LinkedDocumentIds, t.DogfoodTraceId, t.TraceId,
               t.Status, t.FailureReason, t.CreatedUtc, t.CompletedUtc, t.Boundary
        FROM dbo.ProjectChannelAssistantTurns t
        LEFT JOIN dbo.Users u ON u.Id = t.RequestedByUserId
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

    private sealed class MentionCandidateRow
    {
        public int UserId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string NotificationLevel { get; init; } = ProjectChannelNotificationLevels.Mentions;
    }

    private sealed record MentionCandidate(
        int UserId,
        string DisplayName,
        string Handle,
        string NotificationLevel);
}

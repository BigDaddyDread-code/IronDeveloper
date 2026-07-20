using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Workbench;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface IChatHistoryService
{
    // Sessions
    Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default);
    Task<ProjectChatSession?> GetSessionByIdAsync(int projectId, long sessionId, CancellationToken cancellationToken = default);
    Task<long?> TryReplaySessionCreateAsync(
        ProjectChatSession session,
        int actorUserId,
        Guid clientOperationId,
        CancellationToken cancellationToken = default);
    Task<long> CreateSessionIdempotentlyAsync(
        ProjectChatSession session,
        int actorUserId,
        Guid clientOperationId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken = default);
    Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default);
    Task<bool> DeleteSessionAsync(int projectId, long sessionId, CancellationToken cancellationToken = default);

    // Messages
    Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task<ChatMessage?> GetMessageByIdAsync(
        long messageId,
        int projectId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
        int projectId,
        long sessionId,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed class ChatHistoryService : IChatHistoryService
{
    private const string SessionCreateOperationKind = "CreateProjectChatSession";
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;
    private readonly IChatTurnPersistenceService _turnPersistence;

    public ChatHistoryService(
        IDbConnectionFactory connectionFactory,
        ICurrentTenantContext tenant,
        IChatTurnPersistenceService turnPersistence)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
        _turnPersistence = turnPersistence;
    }

    public async Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, Title, CreatedDate, UpdatedDate, Summary,
                PrimaryTicketId, PrimaryDecisionId, PrimaryPlanId,
                OriginTicketId, OriginDecisionId, OriginPlanId
            FROM dbo.ProjectChatSessions
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY UpdatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectChatSession>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ProjectChatSession?> GetSessionByIdAsync(
        int projectId,
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, Title, CreatedDate, UpdatedDate, Summary,
                PrimaryTicketId, PrimaryDecisionId, PrimaryPlanId,
                OriginTicketId, OriginDecisionId, OriginPlanId
            FROM dbo.ProjectChatSessions
            WHERE Id = @SessionId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectChatSession>(new CommandDefinition(
            sql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));
    }

    public async Task<long?> TryReplaySessionCreateAsync(
        ProjectChatSession session,
        int actorUserId,
        Guid clientOperationId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotentCreate(session, actorUserId, clientOperationId);
        var title = NormalizeTitle(session.Title);
        var payloadHash = ComputeSessionCreatePayloadHash(session.ProjectId, title, session.Summary);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await EnsureActiveProjectMembershipAsync(
                connection,
                transaction,
                session.ProjectId,
                actorUserId,
                cancellationToken).ConfigureAwait(false);

            var existing = await ReadSessionCreateOperationAsync(
                connection,
                transaction,
                session.ProjectId,
                actorUserId,
                clientOperationId,
                cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                transaction.Commit();
                return null;
            }

            EnsureMatchingSessionCreateOperation(existing, payloadHash);
            var replay = ReadStoredSessionCreateResult(existing, session.ProjectId, clientOperationId);
            transaction.Commit();
            return replay.SessionId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<long> CreateSessionIdempotentlyAsync(
        ProjectChatSession session,
        int actorUserId,
        Guid clientOperationId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotentCreate(session, actorUserId, clientOperationId);
        var title = NormalizeTitle(session.Title);
        var payloadHash = ComputeSessionCreatePayloadHash(session.ProjectId, title, session.Summary);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            await EnsureActiveProjectMembershipAsync(
                connection,
                transaction,
                session.ProjectId,
                actorUserId,
                cancellationToken).ConfigureAwait(false);

            var existing = await ReadSessionCreateOperationAsync(
                connection,
                transaction,
                session.ProjectId,
                actorUserId,
                clientOperationId,
                cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                EnsureMatchingSessionCreateOperation(existing, payloadHash);
                var replay = ReadStoredSessionCreateResult(existing, session.ProjectId, clientOperationId);
                transaction.Commit();
                return replay.SessionId;
            }

            await ValidateAndRenewWriteLeaseAsync(
                connection,
                transaction,
                session.ProjectId,
                actorUserId,
                workbenchSessionId,
                leaseEpoch,
                cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT dbo.ClientOperations
                    (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId, PayloadHash, Status)
                VALUES
                    (@TenantId, @ActorUserId, @OperationKind, @ResourceScopeId, @ClientOperationId, @PayloadHash, N'Pending');
                """,
                new
                {
                    TenantId = _tenant.TenantId,
                    ActorUserId = actorUserId,
                    OperationKind = SessionCreateOperationKind,
                    ResourceScopeId = SessionCreateResourceScope(session.ProjectId),
                    ClientOperationId = clientOperationId,
                    PayloadHash = payloadHash
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var sessionId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT INTO dbo.ProjectChatSessions
                    (TenantId, ProjectId, Title, Summary, PrimaryTicketId, PrimaryDecisionId, PrimaryPlanId,
                     OriginTicketId, OriginDecisionId, OriginPlanId, UpdatedDate)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @Title, @Summary, @PrimaryTicketId, @PrimaryDecisionId, @PrimaryPlanId,
                     @OriginTicketId, @OriginDecisionId, @OriginPlanId, SYSUTCDATETIME());
                """,
                new
                {
                    TenantId = _tenant.TenantId,
                    session.ProjectId,
                    Title = title,
                    session.Summary,
                    session.PrimaryTicketId,
                    session.PrimaryDecisionId,
                    session.PrimaryPlanId,
                    session.OriginTicketId,
                    session.OriginDecisionId,
                    session.OriginPlanId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var result = new SessionCreateResult(sessionId, session.ProjectId, clientOperationId);
            var canonicalResultJson = JsonSerializer.Serialize(result);
            var resultHash = ComputeHash(canonicalResultJson);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.ClientOperations
                SET Status=N'Completed', ResultProjectId=@ProjectId,
                    CanonicalResultJson=@CanonicalResultJson, ResultHash=@ResultHash,
                    CompletedAtUtc=SYSUTCDATETIME()
                WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
                  AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
                  AND ClientOperationId=@ClientOperationId;
                """,
                new
                {
                    TenantId = _tenant.TenantId,
                    ActorUserId = actorUserId,
                    OperationKind = SessionCreateOperationKind,
                    ResourceScopeId = SessionCreateResourceScope(session.ProjectId),
                    ClientOperationId = clientOperationId,
                    ProjectId = session.ProjectId,
                    CanonicalResultJson = canonicalResultJson,
                    ResultHash = resultHash
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            transaction.Commit();
            return sessionId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.ProjectId <= 0)
            throw new ArgumentException("Session must have a valid ProjectId.", nameof(session));

        if (_tenant == null)
            throw new InvalidOperationException("Tenant context is not available.");

        if (_connectionFactory == null)
            throw new InvalidOperationException("Database connection factory is not available.");

        // Robust title fallback
        if (string.IsNullOrWhiteSpace(session.Title))
            session.Title = "New Chat";

        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { session.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException($"Project {session.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        if (session.Id > 0)
        {
            const string sql = """
                UPDATE dbo.ProjectChatSessions
                SET Title = @Title,
                    UpdatedDate = SYSUTCDATETIME(),
                    Summary = @Summary,
                    PrimaryTicketId = @PrimaryTicketId,
                    PrimaryDecisionId = @PrimaryDecisionId,
                    PrimaryPlanId = @PrimaryPlanId
                WHERE Id = @Id AND TenantId = @TenantId AND ProjectId = @ProjectId;
                """;

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    session.Id,
                    TenantId = _tenant.TenantId,
                    session.ProjectId,
                    session.Title,
                    session.Summary,
                    session.PrimaryTicketId,
                    session.PrimaryDecisionId,
                    session.PrimaryPlanId
                },
                cancellationToken: cancellationToken));

            if (affected == 0)
                throw new UnauthorizedAccessException("The Chat session is not available in this project.");

            return session.Id;
        }
        else
        {
            const string sql = """
                INSERT INTO dbo.ProjectChatSessions 
                    (TenantId, ProjectId, Title, Summary, PrimaryTicketId, PrimaryDecisionId, PrimaryPlanId, OriginTicketId, OriginDecisionId, OriginPlanId, UpdatedDate)
                OUTPUT inserted.Id
                VALUES 
                    (@TenantId, @ProjectId, @Title, @Summary, @PrimaryTicketId, @PrimaryDecisionId, @PrimaryPlanId, @OriginTicketId, @OriginDecisionId, @OriginPlanId, SYSUTCDATETIME());
                """;

            return await connection.QuerySingleAsync<long>(new CommandDefinition(
                sql,
                new
                {
                    TenantId = _tenant.TenantId,
                    session.ProjectId,
                    session.Title,
                    session.Summary,
                    session.PrimaryTicketId,
                    session.PrimaryDecisionId,
                    session.PrimaryPlanId,
                    session.OriginTicketId,
                    session.OriginDecisionId,
                    session.OriginPlanId
                },
                cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> DeleteSessionAsync(
        int projectId,
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.ProjectChatSessions
            WHERE Id=@SessionId AND TenantId=@TenantId AND ProjectId=@ProjectId;
            """,
            new { SessionId = sessionId, TenantId = _tenant.TenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
        if (!exists)
        {
            transaction.Commit();
            return false;
        }

        const string deleteTurnStateSql = """
            IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.ChatTurnTraces
                WHERE ChatMessageId IN
                (
                    SELECT Id FROM dbo.ChatMessages
                    WHERE ChatSessionId = @SessionId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                );
            END

            IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.ChatTurnClarifications
                WHERE ChatMessageId IN
                (
                    SELECT Id FROM dbo.ChatMessages
                    WHERE ChatSessionId = @SessionId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                );
            END

            IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.ChatTurnGovernance
                WHERE ChatMessageId IN
                (
                    SELECT Id FROM dbo.ChatMessages
                    WHERE ChatSessionId = @SessionId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                );
            END
            """;

        const string deleteMessagesSql = """
            DELETE FROM dbo.ChatMessages
            WHERE ChatSessionId = @SessionId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId;
            """;

        const string deleteSessionSql = """
            DELETE FROM dbo.ProjectChatSessions
            WHERE Id = @SessionId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            deleteTurnStateSql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            deleteMessagesSql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            deleteSessionSql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId, ProjectId = projectId },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return true;
    }

    public async Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ProjectId <= 0)
            throw new ArgumentException("Message must have a valid ProjectId.", nameof(message));

        if (message.ChatSessionId <= 0)
            throw new ArgumentException("Message must have a valid ChatSessionId.", nameof(message));

        if (_tenant == null)
            throw new InvalidOperationException("Tenant context is not available.");

        if (_connectionFactory == null)
            throw new InvalidOperationException("Database connection factory is not available.");

        var documentVersionIds = (message.DocumentVersionIds ?? []).Where(id => id > 0).Distinct().ToArray();
        if (documentVersionIds.Length > 1)
            throw new ChatDocumentSourceUnavailableException("This Chat slice accepts one exact document version per message.");
        if (documentVersionIds.Length > 0 && !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            throw new ChatDocumentSourceUnavailableException("Only a user request can attach document context.");

        const string ownerSql = """
            SELECT COUNT(1)
            FROM dbo.ProjectChatSessions
            WHERE Id = @ChatSessionId
              AND ProjectId = @ProjectId
              AND TenantId = @TenantId;
            """;
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                ownerSql,
                new { message.ChatSessionId, message.ProjectId, TenantId = _tenant.TenantId },
                transaction,
                cancellationToken: cancellationToken));

            if (owns == 0)
                throw new UnauthorizedAccessException("The Chat session is not available in this project.");

            if (message.ReplyToMessageId.HasValue)
            {
                if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    throw new ChatDocumentSourceUnavailableException(
                        "Only an assistant response can identify the user request it answers.");

                var replyExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(1)
                    FROM dbo.ChatMessages
                    WHERE Id = @ReplyToMessageId
                      AND TenantId = @TenantId
                      AND ProjectId = @ProjectId
                      AND ChatSessionId = @ChatSessionId
                      AND Role = 'user';
                    """,
                    new
                    {
                        message.ReplyToMessageId,
                        TenantId = _tenant.TenantId,
                        message.ProjectId,
                        message.ChatSessionId
                    },
                    transaction,
                    cancellationToken: cancellationToken));
                if (replyExists == 0)
                    throw new UnauthorizedAccessException("The replied-to Chat message is not available in this project session.");
            }

            if (documentVersionIds.Length == 1)
            {
                var sourceIsAvailable = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(1)
                    FROM dbo.ProjectDocuments d
                    INNER JOIN dbo.ProjectDocumentVersions v ON v.Id = d.CurrentVersionId AND v.DocumentId = d.Id
                    WHERE d.TenantId = @TenantId
                      AND d.ProjectId = @ProjectId
                      AND d.Status = 'Active'
                      AND d.ProcessingStatus = 'Ready'
                      AND v.Id = @DocumentVersionId
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.ProjectDocumentLinks sourceLink
                          INNER JOIN dbo.ProjectContextDocuments contextDocument
                              ON contextDocument.Id = sourceLink.LinkedEntityId
                             AND contextDocument.TenantId = d.TenantId
                             AND contextDocument.ProjectId = d.ProjectId
                          WHERE sourceLink.DocumentVersionId = v.Id
                            AND sourceLink.LinkedEntityType = 'ProjectContextDocument'
                            AND sourceLink.LinkType = 'IndexedAs'
                            AND contextDocument.Status = 'Active'
                            AND contextDocument.Source = CONCAT('ProjectDocumentVersion:', v.Id)
                      );
                    """,
                    new
                    {
                        TenantId = _tenant.TenantId,
                        message.ProjectId,
                        DocumentVersionId = documentVersionIds[0]
                    },
                    transaction,
                    cancellationToken: cancellationToken));
                if (sourceIsAvailable == 0)
                    throw new ChatDocumentSourceUnavailableException(
                        "The selected document version is not available as Ready context in this project.");
            }

            const string sql = """
                INSERT INTO dbo.ChatMessages
                    (TenantId, ProjectId, ChatSessionId, Role, Message, Tags, ContextSummary, LinkedFilePaths, LinkedSymbols, ReplyToMessageId)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @ChatSessionId, @Role, @Message, @Tags, @ContextSummary, @LinkedFilePaths, @LinkedSymbols, @ReplyToMessageId);
                """;

            var id = await connection.QuerySingleAsync<long>(new CommandDefinition(
                sql,
                new
                {
                    TenantId = _tenant.TenantId,
                    message.ProjectId,
                    message.ChatSessionId,
                    message.Role,
                    message.Message,
                    message.Tags,
                    message.ContextSummary,
                    message.LinkedFilePaths,
                    message.LinkedSymbols,
                    message.ReplyToMessageId
                },
                transaction,
                cancellationToken: cancellationToken));

            if (documentVersionIds.Length == 1)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.ProjectDocumentLinks
                        (DocumentVersionId, LinkedEntityType, LinkedEntityId, LinkType, CreatedBy)
                    VALUES
                        (@DocumentVersionId, 'ChatMessage', @ChatMessageId, 'ChatContext', @CreatedBy);
                    """,
                    new
                    {
                        DocumentVersionId = documentVersionIds[0],
                        ChatMessageId = id,
                        CreatedBy = message.SourceAttachedBy
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            // Update session's UpdatedDate
            const string updateSessionSql = "UPDATE dbo.ProjectChatSessions SET UpdatedDate = SYSUTCDATETIME() WHERE Id = @ChatSessionId";
            await connection.ExecuteAsync(new CommandDefinition(
                updateSessionSql,
                new { message.ChatSessionId },
                transaction,
                cancellationToken: cancellationToken));

            await _turnPersistence.PersistAsync(
                new ChatTurnPersistenceRequest(
                    id,
                    _tenant.TenantId,
                    message.ProjectId,
                    message.ChatSessionId,
                    message.Role,
                    message.Tags,
                    message.ContextSummary,
                    message.LinkedFilePaths,
                    message.LinkedSymbols),
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);

            transaction.Commit();
            return id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<ChatMessage?> GetMessageByIdAsync(
        long messageId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, ChatSessionId, Role, Message, Tags, ContextSummary, LinkedFilePaths, LinkedSymbols, ReplyToMessageId, CreatedDate
            FROM dbo.ChatMessages
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND Id = @MessageId;
            """;

        using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ChatMessage>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, MessageId = messageId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
        int projectId,
        long sessionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, ChatSessionId, Role, Message, Tags, ContextSummary, LinkedFilePaths, LinkedSymbols, ReplyToMessageId, CreatedDate
            FROM dbo.ChatMessages
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND ChatSessionId = @SessionId
            ORDER BY CreatedDate DESC, Id DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ChatMessage>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, SessionId = sessionId, Take = take },
            cancellationToken: cancellationToken));

        return rows.Reverse().ToList();
    }

    private static void ValidateIdempotentCreate(
        ProjectChatSession session,
        int actorUserId,
        Guid clientOperationId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Id > 0)
            throw new ArgumentException("Idempotent session creation cannot update an existing session.", nameof(session));
        if (session.ProjectId <= 0)
            throw new ArgumentException("Session must have a valid ProjectId.", nameof(session));
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("An authenticated actor is required.");
        if (clientOperationId == Guid.Empty)
            throw new ArgumentException("clientOperationId is required.", nameof(clientOperationId));
    }

    private async Task EnsureActiveProjectMembershipAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int projectId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var hasAccess = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.Projects project
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=project.TenantId AND member.ProjectId=project.Id
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=project.TenantId AND tenantMember.UserId=member.UserId
            INNER JOIN dbo.Users actor
                ON actor.Id=member.UserId AND actor.IsActive=1
            WHERE project.TenantId=@TenantId AND project.Id=@ProjectId;
            """,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, ActorUserId = actorUserId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;

        if (!hasAccess)
            throw new UnauthorizedAccessException("The project is not available to the authenticated actor.");
    }

    private async Task ValidateAndRenewWriteLeaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int projectId,
        int actorUserId,
        long workbenchSessionId,
        long leaseEpoch,
        CancellationToken cancellationToken)
    {
        if (workbenchSessionId <= 0 || leaseEpoch <= 0)
            throw new WorkbenchLeaseFenceException();

        var renewed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE lease
            SET HeartbeatAtUtc=SYSUTCDATETIME(),
                ExpiresAtUtc=DATEADD(MINUTE, 30, SYSUTCDATETIME())
            FROM dbo.WorkbenchWriteLeases lease
            INNER JOIN dbo.WorkbenchSessions session
                ON session.TenantId=lease.TenantId AND session.ProjectId=lease.ProjectId
               AND session.Id=lease.WorkbenchSessionId AND session.Status=N'Active'
            INNER JOIN dbo.ProjectMembers member
                ON member.TenantId=lease.TenantId AND member.ProjectId=lease.ProjectId
               AND member.UserId=@ActorUserId AND member.Status=N'Active'
            INNER JOIN dbo.TenantUsers tenantMember
                ON tenantMember.TenantId=lease.TenantId AND tenantMember.UserId=@ActorUserId
            INNER JOIN dbo.Users actor
                ON actor.Id=@ActorUserId AND actor.IsActive=1
            WHERE lease.TenantId=@TenantId AND lease.ProjectId=@ProjectId
              AND lease.WorkbenchSessionId=@WorkbenchSessionId AND lease.LeaseEpoch=@LeaseEpoch
              AND lease.HolderActorUserId=@ActorUserId AND lease.RevokedAtUtc IS NULL
              AND lease.ExpiresAtUtc > SYSUTCDATETIME();
            """,
            new
            {
                TenantId = _tenant.TenantId,
                ActorUserId = actorUserId,
                ProjectId = projectId,
                WorkbenchSessionId = workbenchSessionId,
                LeaseEpoch = leaseEpoch
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (renewed != 1)
            throw new WorkbenchLeaseFenceException();
    }

    private async Task<SessionCreateOperationRow?> ReadSessionCreateOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int projectId,
        int actorUserId,
        Guid clientOperationId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<SessionCreateOperationRow>(new CommandDefinition(
            """
            SELECT Status, PayloadHash, CanonicalResultJson, ResultHash
            FROM dbo.ClientOperations WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId=@TenantId AND ActorUserId=@ActorUserId
              AND OperationKind=@OperationKind AND ResourceScopeId=@ResourceScopeId
              AND ClientOperationId=@ClientOperationId;
            """,
            new
            {
                TenantId = _tenant.TenantId,
                ActorUserId = actorUserId,
                OperationKind = SessionCreateOperationKind,
                ResourceScopeId = SessionCreateResourceScope(projectId),
                ClientOperationId = clientOperationId
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static void EnsureMatchingSessionCreateOperation(
        SessionCreateOperationRow existing,
        string payloadHash)
    {
        if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw new ProjectStartOperationMismatchException();
        if (!string.Equals(existing.Status, "Completed", StringComparison.Ordinal))
            throw new InvalidOperationException("The existing Chat session creation operation is not complete.");
    }

    private static SessionCreateResult ReadStoredSessionCreateResult(
        SessionCreateOperationRow existing,
        int projectId,
        Guid clientOperationId)
    {
        if (string.IsNullOrWhiteSpace(existing.CanonicalResultJson) || string.IsNullOrWhiteSpace(existing.ResultHash))
            throw new InvalidOperationException("The completed Chat session creation operation has no canonical result.");
        if (!string.Equals(ComputeHash(existing.CanonicalResultJson), existing.ResultHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stored Chat session creation result failed its integrity check.");

        var result = JsonSerializer.Deserialize<SessionCreateResult>(existing.CanonicalResultJson)
            ?? throw new InvalidOperationException("The stored Chat session creation result could not be read.");
        if (result.ProjectId != projectId || result.ClientOperationId != clientOperationId || result.SessionId <= 0)
            throw new InvalidOperationException("The stored Chat session creation result belongs to another operation scope.");
        return result;
    }

    private static string NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "New Chat" : title.Trim();

    private static string ComputeSessionCreatePayloadHash(int projectId, string title, string? summary) =>
        ComputeHash($"project-chat-session-create-v1\n{projectId}\n{title}\n{summary ?? string.Empty}");

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SessionCreateResourceScope(int projectId) =>
        $"project:{projectId}:chat-session";

    private sealed record SessionCreateResult(long SessionId, int ProjectId, Guid ClientOperationId);

    private sealed class SessionCreateOperationRow
    {
        public string Status { get; init; } = string.Empty;
        public string PayloadHash { get; init; } = string.Empty;
        public string? CanonicalResultJson { get; init; }
        public string? ResultHash { get; init; }
    }
}

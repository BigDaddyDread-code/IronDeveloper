using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface IChatHistoryService
{
    // Sessions
    Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default);
    Task<ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default);

    // Messages
    Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
        int projectId,
        long sessionId,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed class ChatHistoryService : IChatHistoryService
{
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

    public async Task<ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, Title, CreatedDate, UpdatedDate, Summary,
                PrimaryTicketId, PrimaryDecisionId, PrimaryPlanId,
                OriginTicketId, OriginDecisionId, OriginPlanId
            FROM dbo.ProjectChatSessions
            WHERE Id = @SessionId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectChatSession>(new CommandDefinition(
            sql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
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
                WHERE Id = @Id AND TenantId = @TenantId;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    session.Id,
                    TenantId = _tenant.TenantId,
                    session.Title,
                    session.Summary,
                    session.PrimaryTicketId,
                    session.PrimaryDecisionId,
                    session.PrimaryPlanId
                },
                cancellationToken: cancellationToken));

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

    public async Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string deleteTurnStateSql = """
            IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.ChatTurnTraces
                WHERE ChatMessageId IN
                (
                    SELECT Id FROM dbo.ChatMessages
                    WHERE ChatSessionId = @SessionId
                      AND TenantId = @TenantId
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
                );
            END
            """;

        const string deleteMessagesSql = """
            DELETE FROM dbo.ChatMessages
            WHERE ChatSessionId = @SessionId
              AND TenantId = @TenantId;
            """;

        const string deleteSessionSql = """
            DELETE FROM dbo.ProjectChatSessions
            WHERE Id = @SessionId
              AND TenantId = @TenantId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            deleteTurnStateSql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            deleteMessagesSql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            deleteSessionSql,
            new { SessionId = sessionId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
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

        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        using var connection = _connectionFactory.CreateConnection();

        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { message.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException($"Project {message.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        const string sql = """
            INSERT INTO dbo.ChatMessages 
                (TenantId, ProjectId, ChatSessionId, Role, Message, Tags, ContextSummary, LinkedFilePaths, LinkedSymbols)
            OUTPUT inserted.Id
            VALUES 
                (@TenantId, @ProjectId, @ChatSessionId, @Role, @Message, @Tags, @ContextSummary, @LinkedFilePaths, @LinkedSymbols);
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
                message.LinkedSymbols
            },
            cancellationToken: cancellationToken));

        // Update session's UpdatedDate
        const string updateSessionSql = "UPDATE dbo.ProjectChatSessions SET UpdatedDate = SYSUTCDATETIME() WHERE Id = @ChatSessionId";
        await connection.ExecuteAsync(new CommandDefinition(
            updateSessionSql,
            new { message.ChatSessionId },
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
            cancellationToken).ConfigureAwait(false);

        return id;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
        int projectId,
        long sessionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, ChatSessionId, Role, Message, Tags, ContextSummary, LinkedFilePaths, LinkedSymbols, CreatedDate
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
}

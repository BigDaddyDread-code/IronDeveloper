using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface IChatHistoryService
{
    Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
        int projectId,
        Guid sessionId,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed class ChatHistoryService : IChatHistoryService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ChatHistoryService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        // Ownership guard: verify the project belongs to the current tenant before inserting.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        using var connection = _connectionFactory.CreateConnection();

        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { message.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {message.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        const string sql = """
            INSERT INTO dbo.ChatMessages (TenantId, ProjectId, SessionId, Role, Message, Tags)
            OUTPUT inserted.Id
            VALUES (@TenantId, @ProjectId, @SessionId, @Role, @Message, @Tags);
            """;

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                message.ProjectId,
                message.SessionId,
                message.Role,
                message.Message,
                message.Tags
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
        int projectId,
        Guid sessionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, SessionId, Role, Message, Tags, CreatedDate
            FROM dbo.ChatMessages
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND SessionId = @SessionId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ChatMessage>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, SessionId = sessionId, Take = take },
            cancellationToken: cancellationToken));

        return rows.Reverse().ToList();
    }
}

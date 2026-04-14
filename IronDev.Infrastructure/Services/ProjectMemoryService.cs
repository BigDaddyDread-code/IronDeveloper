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

public interface IProjectMemoryService
{
    Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default);
    Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default);
}

public sealed class ProjectMemoryService : IProjectMemoryService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ProjectMemoryService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                Id, TenantId, ProjectId, Summary, SourceChatMessageId, CreatedDate, UpdatedDate
            FROM dbo.ProjectSummaries
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectSummary>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, Title, Detail, Reason, SourceChatMessageId, CreatedDate
            FROM dbo.ProjectDecisions
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectDecision>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard: verify the project belongs to the current tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { summary.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {summary.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        const string sql = """
            INSERT INTO dbo.ProjectSummaries (TenantId, ProjectId, Summary, SourceChatMessageId, UpdatedDate)
            OUTPUT inserted.Id
            VALUES (@TenantId, @ProjectId, @Summary, @SourceChatMessageId, @UpdatedDate);
            """;

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                summary.ProjectId,
                summary.Summary,
                summary.SourceChatMessageId,
                summary.UpdatedDate
            },
            cancellationToken: cancellationToken));
    }

    public async Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard: verify the project belongs to the current tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { decision.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {decision.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        // Deduplication: update existing decision with the same title rather than inserting duplicate.
        const string checkSql = """
            SELECT Id FROM dbo.ProjectDecisions
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Title = @Title
            """;
        var existingId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            checkSql,
            new { TenantId = _tenant.TenantId, decision.ProjectId, decision.Title },
            cancellationToken: cancellationToken));

        if (existingId.HasValue)
        {
            const string updateSql = "UPDATE dbo.ProjectDecisions SET Detail = @Detail, Reason = @Reason WHERE Id = @Id";
            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new { Id = existingId.Value, decision.Detail, decision.Reason },
                cancellationToken: cancellationToken));
            return existingId.Value;
        }

        const string sql = """
            INSERT INTO dbo.ProjectDecisions (TenantId, ProjectId, Title, Detail, Reason, SourceChatMessageId)
            OUTPUT inserted.Id
            VALUES (@TenantId, @ProjectId, @Title, @Detail, @Reason, @SourceChatMessageId);
            """;

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                decision.ProjectId,
                decision.Title,
                decision.Detail,
                decision.Reason,
                decision.SourceChatMessageId
            },
            cancellationToken: cancellationToken));
    }
}

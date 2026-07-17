using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface IProjectService
{
    Task<int> CreateProjectAsync(Project project, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default);
    Task<Project?> UpdateProjectAsync(int projectId, Project project, CancellationToken cancellationToken = default);
    Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default);
    Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default);
}

public sealed class ProjectService : IProjectService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ProjectService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<int> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.Projects (TenantId, Name, Description, LocalPath)
            OUTPUT inserted.Id
            VALUES (@TenantId, @Name, @Description, @LocalPath);
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, project.Name, project.Description, project.LocalPath },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        const string projectionSql = """
            SELECT p.Id, p.TenantId, p.Name, p.Description, p.LocalPath, p.CreatedDate, p.UpdatedDate,
                   p.LastIndexedUtc, p.IndexingStatus, p.IndexedFileCount,
                   phase.Phase AS LifecyclePhase, readiness.ExecutionReadiness
            FROM dbo.Projects p
            OUTER APPLY (
                SELECT TOP (1) value.Phase FROM dbo.ProjectLifecyclePhases value
                WHERE value.TenantId=p.TenantId AND value.ProjectId=p.Id ORDER BY value.Revision DESC
            ) phase
            OUTER APPLY (
                SELECT TOP (1) value.ExecutionReadiness FROM dbo.ProjectReadinessAssessments value
                WHERE value.TenantId=p.TenantId AND value.ProjectId=p.Id ORDER BY value.Revision DESC
            ) readiness
            WHERE p.TenantId = @TenantId
            ORDER BY p.CreatedDate DESC;
            """;
        const string legacySql = """
            SELECT p.Id, p.TenantId, p.Name, p.Description, p.LocalPath, p.CreatedDate, p.UpdatedDate,
                   p.LastIndexedUtc, p.IndexingStatus, p.IndexedFileCount
            FROM dbo.Projects p
            WHERE p.TenantId = @TenantId
            ORDER BY p.CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var sql = await HasWorkbenchEntryProjectionsAsync(connection, cancellationToken)
            ? projectionSql
            : legacySql;
        var rows = await connection.QueryAsync<Project>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string projectionSql = """
            SELECT p.Id, p.TenantId, p.Name, p.Description, p.LocalPath, p.CreatedDate, p.UpdatedDate,
                   p.LastIndexedUtc, p.IndexingStatus, p.IndexedFileCount,
                   phase.Phase AS LifecyclePhase, readiness.ExecutionReadiness
            FROM dbo.Projects p
            OUTER APPLY (
                SELECT TOP (1) value.Phase FROM dbo.ProjectLifecyclePhases value
                WHERE value.TenantId=p.TenantId AND value.ProjectId=p.Id ORDER BY value.Revision DESC
            ) phase
            OUTER APPLY (
                SELECT TOP (1) value.ExecutionReadiness FROM dbo.ProjectReadinessAssessments value
                WHERE value.TenantId=p.TenantId AND value.ProjectId=p.Id ORDER BY value.Revision DESC
            ) readiness
            WHERE p.Id = @ProjectId
              AND p.TenantId = @TenantId;
            """;
        const string legacySql = """
            SELECT p.Id, p.TenantId, p.Name, p.Description, p.LocalPath, p.CreatedDate, p.UpdatedDate,
                   p.LastIndexedUtc, p.IndexingStatus, p.IndexedFileCount
            FROM dbo.Projects p
            WHERE p.Id = @ProjectId
              AND p.TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var sql = await HasWorkbenchEntryProjectionsAsync(connection, cancellationToken)
            ? projectionSql
            : legacySql;
        return await connection.QuerySingleOrDefaultAsync<Project>(new CommandDefinition(
            sql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task<Project?> UpdateProjectAsync(int projectId, Project project, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Projects
            SET Name = @Name,
                Description = @Description,
                LocalPath = @LocalPath,
                UpdatedDate = SYSUTCDATETIME()
            WHERE Id = @ProjectId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                ProjectId = projectId,
                TenantId = _tenant.TenantId,
                project.Name,
                project.Description,
                project.LocalPath
            },
            cancellationToken: cancellationToken));

        return rows == 0 ? null : await GetByIdAsync(projectId, cancellationToken);
    }

    public async Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Projects
            SET LocalPath = @LocalPath, UpdatedDate = SYSUTCDATETIME()
            WHERE Id = @ProjectId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { ProjectId = projectId, LocalPath = localPath, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Projects
            SET IndexingStatus = 'Stale Index',
                UpdatedDate = SYSUTCDATETIME()
            WHERE Id = @ProjectId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId, Reason = reason },
            cancellationToken: cancellationToken));
    }

    private static async Task<bool> HasWorkbenchEntryProjectionsAsync(
        System.Data.IDbConnection connection,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT CASE
                WHEN OBJECT_ID(N'dbo.ProjectLifecyclePhases', N'U') IS NOT NULL
                 AND OBJECT_ID(N'dbo.ProjectReadinessAssessments', N'U') IS NOT NULL
                THEN 1 ELSE 0 END;
            """,
            cancellationToken: cancellationToken)) == 1;
}

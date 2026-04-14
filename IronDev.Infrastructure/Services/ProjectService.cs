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
    Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default);
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
        const string sql = """
            SELECT Id, TenantId, Name, Description, LocalPath, CreatedDate, UpdatedDate
            FROM dbo.Projects
            WHERE TenantId = @TenantId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Project>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, TenantId, Name, Description, LocalPath, CreatedDate, UpdatedDate
            FROM dbo.Projects
            WHERE Id = @ProjectId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Project>(new CommandDefinition(
            sql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
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
}

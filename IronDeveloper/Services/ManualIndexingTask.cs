using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Agent.Services;

public sealed class ManualIndexingTask
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AgentTenantContext _tenantContext;

    public ManualIndexingTask(IDbConnectionFactory connectionFactory, AgentTenantContext tenantContext)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
    }

    public async Task<Project> ResolveProjectAsync(Project project, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project.LocalPath))
            throw new InvalidOperationException("Project does not have a local path configured.");

        var localPath = NormalizePath(project.LocalPath);

        const string sql = """
            SELECT Id, TenantId, Name, Description, LocalPath, CreatedDate, UpdatedDate,
                   LastIndexedUtc, IndexingStatus, IndexedFileCount
            FROM dbo.Projects
            WHERE LocalPath IS NOT NULL
            ORDER BY UpdatedDate DESC, CreatedDate DESC, Id DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var projects = await connection.QueryAsync<Project>(
            new CommandDefinition(sql, cancellationToken: ct));

        var match = projects.FirstOrDefault(candidate =>
            string.Equals(NormalizePath(candidate.LocalPath), localPath, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new InvalidOperationException($"No IronDeveloper project is configured for local path: {localPath}");

        _tenantContext.SetTenant(match.TenantId);
        _tenantContext.SetProject(match.Id);

        return match;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

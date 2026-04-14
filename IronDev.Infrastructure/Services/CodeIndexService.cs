using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface ICodeIndexService
{
    Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default);
    Task<ProjectFile?> GetByPathAsync(int projectId, string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default);
}

public sealed class SqlCodeIndexService : ICodeIndexService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "packages", "node_modules", "dist", "build", "out"
    };

    private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".csproj", ".sln", ".slnx", ".json", ".md", ".txt", ".sql", ".js", ".ts", ".html", ".css", ".xml"
    };

    public SqlCodeIndexService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default)
    {
        var result = new CodeIndexResult();
        if (!Directory.Exists(directoryPath)) return result;

        var allFiles = GetFilesToProcess(directoryPath);

        using var connection = _connectionFactory.CreateConnection();
        if (connection is System.Data.Common.DbConnection dbConn)
            await dbConn.OpenAsync(cancellationToken);
        else
            connection.Open();

        // Ownership guard: verify the project belongs to the current tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(ownerSql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId });

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {projectId} does not belong to tenant {_tenant.TenantId}.");

        foreach (var fullPath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.FilesScanned++;

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > 1024 * 1024) // Skip files > 1MB
            {
                result.FilesSkipped++;
                continue;
            }

            var relPath = Path.GetRelativePath(directoryPath, fullPath).Replace('\\', '/');
            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var hash = ComputeHash(content);

            // Tenant-aware uniqueness: check by TenantId + ProjectId + FilePath to prevent cross-tenant hash collisions.
            var existing = await connection.QuerySingleOrDefaultAsync<ProjectFile>(
                """
                SELECT Id, ContentHash FROM dbo.ProjectFiles
                WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND FilePath = @FilePath
                """,
                new { TenantId = _tenant.TenantId, ProjectId = projectId, FilePath = relPath });

            if (existing != null)
            {
                if (existing.ContentHash == hash)
                {
                    result.FilesUnchanged++;
                }
                else
                {
                    await connection.ExecuteAsync(
                        "UPDATE dbo.ProjectFiles SET ContentHash = @ContentHash, Content = @Content, LastIndexedDate = SYSUTCDATETIME() WHERE Id = @Id",
                        new { Id = existing.Id, ContentHash = hash, Content = content });
                    result.FilesUpdated++;
                }
            }
            else
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content)
                    VALUES (@TenantId, @ProjectId, @FilePath, @FileExtension, @ContentHash, @Content)
                    """,
                    new
                    {
                        TenantId = _tenant.TenantId,
                        ProjectId = projectId,
                        FilePath = relPath,
                        FileExtension = fileInfo.Extension,
                        ContentHash = hash,
                        Content = content
                    });
                result.FilesAdded++;
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content, LastIndexedDate
            FROM dbo.ProjectFiles
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND (FilePath LIKE @Query OR Content LIKE @Query)
            ORDER BY LastIndexedDate DESC
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectFile>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take, Query = $"%{query}%" },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ProjectFile?> GetByPathAsync(int projectId, string filePath, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content, LastIndexedDate
            FROM dbo.ProjectFiles
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND FilePath = @FilePath
            """;
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectFile>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, FilePath = filePath },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content, LastIndexedDate
            FROM dbo.ProjectFiles
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId
            ORDER BY LastIndexedDate DESC
            """;
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectFile>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    private IEnumerable<string> GetFilesToProcess(string rootPath)
    {
        var di = new DirectoryInfo(rootPath);
        if (!di.Exists) yield break;

        var stack = new Stack<DirectoryInfo>();
        stack.Push(di);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            DirectoryInfo[] dirs;
            try { dirs = current.GetDirectories(); }
            catch (Exception) { continue; }

            foreach (var d in dirs)
            {
                if (!ExcludedDirs.Contains(d.Name))
                    stack.Push(d);
            }

            FileInfo[] files;
            try { files = current.GetFiles(); }
            catch (Exception) { continue; }

            foreach (var f in files)
            {
                if (IncludedExtensions.Contains(f.Extension))
                    yield return f.FullName;
            }
        }
    }

    private static string ComputeHash(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}

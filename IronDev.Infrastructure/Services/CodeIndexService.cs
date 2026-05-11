using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken cancellationToken = default);
    Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default);
}

public sealed class SqlCodeIndexService : ICodeIndexService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "packages", "node_modules", "dist", "build", "out", "target", "vendor"
    };

    private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".csproj", ".json", ".md" // V1 restricted set
    };

    // V1 Regex for basic symbol extraction
    private static readonly Regex NamespaceRegex = new(@"namespace\s+([\w\.]+)", RegexOptions.Compiled);
    private static readonly Regex ClassRegex = new(@"class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex MethodRegex = new(@"(?:public|private|protected|internal|static|\s)+\s+[\w\<\>\[\]]+\s+(\w+)\s*\(", RegexOptions.Compiled);

    public SqlCodeIndexService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default)
    {
        var result = new CodeIndexResult();

        // ── Service-level diagnostics ─────────────────────────────────────────
        System.Diagnostics.Trace.WriteLine(
            $"[CodeIndexService] IndexDirectoryAsync called: ProjectId={projectId} TenantId={_tenant.TenantId} " +
            $"Path=[{directoryPath}] Exists={Directory.Exists(directoryPath)}");

        // ── Guard: directory must exist ───────────────────────────────────────
        if (!Directory.Exists(directoryPath))
        {
            result.DirectoryNotFound = true;
            result.ErrorMessage      = $"Directory not found: {directoryPath}";

            System.Diagnostics.Trace.WriteLine(
                $"[CodeIndexService] PATH NOT FOUND: [{directoryPath}] — marking project as failed.");

            // Mark the project status as failed — do NOT leave it as Ready
            using var conn0 = _connectionFactory.CreateConnection();
            conn0.Open();
            await conn0.ExecuteAsync(
                "UPDATE dbo.Projects" +
                " SET IndexingStatus = 'Index Failed - path not found'," +
                "     LastIndexedUtc = SYSUTCDATETIME()," +
                "     IndexedFileCount = 0" +
                " WHERE Id = @ProjectId AND TenantId = @TenantId",
                new { ProjectId = projectId, TenantId = _tenant.TenantId });

            return result;
        }

        var allFiles = GetFilesToProcess(directoryPath).ToList();
        System.Diagnostics.Trace.WriteLine(
            $"[CodeIndexService] Directory found. allFiles={allFiles.Count} (after excludes). " +
            $"First 5: {string.Join(" | ", allFiles.Take(5).Select(Path.GetFileName))}");


        using var connection = _connectionFactory.CreateConnection();
        if (connection is System.Data.Common.DbConnection dbConn)
            await dbConn.OpenAsync(cancellationToken);
        else
            connection.Open();

        // Ownership & Project Guard
        const string projectSql = "SELECT Id, TenantId FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var project = await connection.QuerySingleOrDefaultAsync<dynamic>(projectSql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId });

        if (project == null)
            throw new UnauthorizedAccessException($"Project {projectId} not found or access denied for tenant {_tenant.TenantId}.");

        using var transaction = connection.BeginTransaction();
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var fullPath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.FilesScanned++;

                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > 512 * 1024) // V1 Limit: 512KB for indexing
                {
                    result.FilesSkipped++;
                    continue;
                }

                var relPath = Path.GetRelativePath(directoryPath, fullPath).Replace('\\', '/');
                processedPaths.Add(relPath);

                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                var hash = ComputeHash(content);

                var existing = await connection.QuerySingleOrDefaultAsync<dynamic>(
                    "SELECT Id, ContentHash FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND FilePath = @FilePath",
                    new { TenantId = _tenant.TenantId, ProjectId = projectId, FilePath = relPath }, transaction);

                long fileId;

                if (existing != null)
                {
                    fileId = existing.Id;
                    if (existing.ContentHash == hash)
                    {
                        result.FilesUnchanged++;
                        continue; // Skip re-parsing if hash matches
                    }

                    // Update metadata
                    await connection.ExecuteAsync(
                        "UPDATE dbo.ProjectFiles SET ContentHash = @ContentHash, Content = '', LastIndexedDate = SYSUTCDATETIME() WHERE Id = @Id",
                        new { Id = fileId, ContentHash = hash }, transaction);

                    // Clear old symbols
                    await connection.ExecuteAsync("DELETE FROM dbo.CodeIndexEntries WHERE FileId = @FileId", new { FileId = fileId }, transaction);
                    result.FilesUpdated++;
                }
                else
                {
                    fileId = await connection.QuerySingleAsync<long>(
                        """
                        INSERT INTO dbo.ProjectFiles (TenantId, ProjectId, FilePath, FileExtension, ContentHash, Content)
                        OUTPUT inserted.Id
                        VALUES (@TenantId, @ProjectId, @FilePath, @FileExtension, @ContentHash, '')
                        """,
                        new
                        {
                            TenantId      = _tenant.TenantId,
                            ProjectId     = projectId,
                            FilePath      = relPath,
                            FileExtension = fileInfo.Extension,
                            ContentHash   = hash
                        }, transaction);
                    result.FilesAdded++;
                }

                // Symbol Extraction (V1 Regex)
                await ExtractAndStoreSymbolsAsync(connection, projectId, fileId, content, transaction, cancellationToken);
            }

            // Cleanup stale files that weren't processed in this run
            if (processedPaths.Count > 0)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND FilePath NOT IN @Paths",
                    new { TenantId = _tenant.TenantId, ProjectId = projectId, Paths = processedPaths }, transaction);
            }
            else
            {
                // processedPaths is empty: all files were unchanged (hash match) — do NOT wipe the table
                // Only wipe if we actually found no files to process at all
                if (allFiles.Count == 0)
                {
                    await connection.ExecuteAsync(
                        "DELETE FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId",
                        new { TenantId = _tenant.TenantId, ProjectId = projectId }, transaction);
                }
            }

            // ── Update Project Status ────────────────────────────────────────
            // Count how many files are actually stored after this run
            var storedFileCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId",
                new { TenantId = _tenant.TenantId, ProjectId = projectId }, transaction);

            // Ready only when real indexed files exist
            string newStatus;
            if (storedFileCount > 0)
            {
                newStatus = "Ready";
            }
            else if (allFiles.Count == 0)
            {
                newStatus = "Needs Index — no indexable files found";
                result.ErrorMessage = $"No indexable files (.cs, .xaml, .json, .md) found in: {directoryPath}";
            }
            else
            {
                // Files scanned but all were skipped (too large or all unchanged from previous run)
                newStatus = result.FilesUnchanged > 0 ? "Ready" : "Needs Index — no indexable files found";
            }

            await connection.ExecuteAsync(
                """
                UPDATE dbo.Projects
                SET UpdatedDate      = SYSUTCDATETIME(),
                    LastIndexedUtc   = SYSUTCDATETIME(),
                    IndexingStatus   = @Status,
                    IndexedFileCount = @FileCount
                WHERE Id = @ProjectId
                """,
                new { ProjectId = projectId, Status = newStatus, FileCount = storedFileCount }, transaction);

            result.StoredFileCount = storedFileCount;
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return result;
    }

    private async Task ExtractAndStoreSymbolsAsync(System.Data.IDbConnection connection, int projectId, long fileId, string content, System.Data.IDbTransaction transaction, CancellationToken ct)
    {
        var lines = content.Split('\n');
        var nsMatch = NamespaceRegex.Match(content);
        string currentNamespace = nsMatch.Success ? nsMatch.Groups[1].Value : string.Empty;

        // V1: Simple line-based symbol search to get "chunks"
        var entries = new List<CodeIndexEntry>();

        void AddEntry(string name, string type, int lineIndex)
        {
            // Extract a 10-line chunk centered on the symbol
            int start = Math.Max(0, lineIndex - 2);
            int end = Math.Min(lines.Length - 1, lineIndex + 7);
            var chunk = string.Join("\n", lines.Skip(start).Take(end - start + 1));

            entries.Add(new CodeIndexEntry
            {
                TenantId = _tenant.TenantId,
                ProjectId = projectId,
                FileId = fileId,
                Namespace = currentNamespace,
                SymbolName = name,
                SymbolType = type,
                ChunkText = chunk.Trim()
            });
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            var classMatch = ClassRegex.Match(line);
            if (classMatch.Success)
            {
                AddEntry(classMatch.Groups[1].Value, "Class", i);
                continue;
            }

            var methodMatch = MethodRegex.Match(line);
            if (methodMatch.Success)
            {
                AddEntry(methodMatch.Groups[1].Value, "Method", i);
            }
        }

        if (entries.Any())
        {
            const string sql = """
                INSERT INTO dbo.CodeIndexEntries (TenantId, ProjectId, FileId, Namespace, SymbolName, SymbolType, ChunkText)
                VALUES (@TenantId, @ProjectId, @FileId, @Namespace, @SymbolName, @SymbolType, @ChunkText)
                """;
            await connection.ExecuteAsync(sql, entries, transaction);
        }
    }

    public async Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take) f.*
            FROM dbo.ProjectFiles f
            WHERE f.TenantId = @TenantId
              AND f.ProjectId = @ProjectId
              AND (f.FilePath LIKE @Query 
                   OR EXISTS (
                       SELECT 1 FROM dbo.CodeIndexEntries e 
                       WHERE e.FileId = f.Id 
                         AND (e.SymbolName LIKE @Query OR e.ChunkText LIKE @Query)
                   ))
            ORDER BY f.LastIndexedDate DESC
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
        const string sql = "SELECT * FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND FilePath = @FilePath";
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectFile>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, FilePath = filePath },
            cancellationToken: cancellationToken));
    }

    public async Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId";
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT TOP (@Take) * FROM dbo.ProjectFiles WHERE TenantId = @TenantId AND ProjectId = @ProjectId ORDER BY LastIndexedDate DESC";
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectFile>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    private IEnumerable<string> GetFilesToProcess(string rootPath)
    {
        if (!Directory.Exists(rootPath)) yield break;

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            string[] dirs;
            try { dirs = Directory.GetDirectories(current); }
            catch { continue; }

            foreach (var d in dirs)
            {
                var name = Path.GetFileName(d);
                if (!ExcludedDirs.Contains(name))
                    stack.Push(d);
            }

            string[] files;
            try { files = Directory.GetFiles(current); }
            catch { continue; }

            foreach (var f in files)
            {
                if (IncludedExtensions.Contains(Path.GetExtension(f)))
                    yield return f;
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

    public async Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM dbo.CodeIndexEntries WHERE FileId = @FileId ORDER BY Id";
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CodeIndexEntry>(new CommandDefinition(
            sql, new { FileId = fileId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take) e.*, f.FilePath 
            FROM dbo.CodeIndexEntries e
            LEFT JOIN dbo.ProjectFiles f on e.FileId = f.Id
            WHERE e.TenantId = @TenantId AND e.ProjectId = @ProjectId
              AND (e.SymbolName LIKE @Query OR e.ChunkText LIKE @Query OR f.FilePath LIKE @Query OR e.Namespace LIKE @Query)
            ORDER BY 
                (
                    CASE WHEN f.FilePath LIKE @Query THEN 10 ELSE 0 END +
                    CASE WHEN e.SymbolName LIKE @Query THEN 8 ELSE 0 END +
                    CASE WHEN e.Namespace LIKE @Query THEN 5 ELSE 0 END +
                    CASE WHEN e.ChunkText LIKE @Query THEN 2 ELSE 0 END
                ) DESC, e.Id DESC
            """;
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CodeIndexEntry>(new CommandDefinition(
            sql, new { TenantId = _tenant.TenantId, ProjectId = projectId, Query = $"%{query}%", Take = take }, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}

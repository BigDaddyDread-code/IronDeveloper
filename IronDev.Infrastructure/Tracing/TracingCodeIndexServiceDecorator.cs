using System.Diagnostics;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Tracing;

public sealed class TracingCodeIndexServiceDecorator : ICodeIndexService
{
    private readonly ICodeIndexService _inner;
    private readonly ILogger<TracingCodeIndexServiceDecorator> _logger;

    public TracingCodeIndexServiceDecorator(
        SqlCodeIndexService inner,
        ILogger<TracingCodeIndexServiceDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.IndexDirectory",
            projectId,
            async () => await _inner.IndexDirectoryAsync(projectId, directoryPath, cancellationToken),
            ("directoryPath", directoryPath));
    }

    public async Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.SearchFiles",
            projectId,
            async () => await _inner.SearchFilesAsync(projectId, query, take, cancellationToken),
            ("take", take),
            ("queryLength", query.Length));
    }

    public async Task<ProjectFile?> GetByPathAsync(int projectId, string filePath, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.GetByPath",
            projectId,
            async () => await _inner.GetByPathAsync(projectId, filePath, cancellationToken),
            ("filePath", filePath));
    }

    public async Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.GetRecentFiles",
            projectId,
            async () => await _inner.GetRecentFilesAsync(projectId, take, cancellationToken),
            ("take", take));
    }

    public async Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.GetSymbols",
            null,
            async () => await _inner.GetSymbolsAsync(fileId, cancellationToken),
            ("fileId", fileId));
    }

    public async Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.GetIndexedFileCount",
            projectId,
            async () => await _inner.GetIndexedFileCountAsync(projectId, cancellationToken));
    }

    public async Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default)
    {
        return await TraceAsync(
            "CodeIndex.GetRelevantSnippets",
            projectId,
            async () => await _inner.GetRelevantSnippetsAsync(projectId, query, take, cancellationToken),
            ("take", take),
            ("queryLength", query.Length));
    }

    private async Task<T> TraceAsync<T>(
        string operation,
        int? projectId,
        Func<Task<T>> action,
        params (string Name, object? Value)[] properties)
    {
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(properties.ToDictionary(p => p.Name, p => p.Value));

        try
        {
            var result = await action();
            _logger.LogInformation(
                "{Operation} succeeded in {DurationMs}ms projectId={ProjectId}",
                operation,
                sw.ElapsedMilliseconds,
                projectId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Operation} failed after {DurationMs}ms projectId={ProjectId}",
                operation,
                sw.ElapsedMilliseconds,
                projectId);
            throw;
        }
    }
}

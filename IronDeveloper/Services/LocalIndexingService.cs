using System;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.Services.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Agent.Services;

public sealed class LocalIndexingService : ILocalIndexingService
{
    private readonly ICodeIndexService _codeIndexService;

    public LocalIndexingService(ICodeIndexService codeIndexService)
    {
        _codeIndexService = codeIndexService;
    }

    public async Task<CodeIndexResult> IndexProjectAsync(Project project, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project.LocalPath))
        {
            throw new InvalidOperationException("Project does not have a local path configured.");
        }

        return await _codeIndexService.IndexDirectoryAsync(project.Id, project.LocalPath, ct);
    }

    public Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken ct = default)
    {
        return _codeIndexService.GetIndexedFileCountAsync(projectId, ct);
    }

    public Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken ct = default)
    {
        return _codeIndexService.SearchFilesAsync(projectId, query, take, ct);
    }

    public Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken ct = default)
    {
        return _codeIndexService.GetRecentFilesAsync(projectId, take, ct);
    }

    public Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken ct = default)
    {
        return _codeIndexService.GetRelevantSnippetsAsync(projectId, query, take, ct);
    }
}

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
    private readonly ManualIndexingTask _manualIndexingTask;

    public LocalIndexingService(ICodeIndexService codeIndexService, ManualIndexingTask manualIndexingTask)
    {
        _codeIndexService = codeIndexService;
        _manualIndexingTask = manualIndexingTask;
    }

    public async Task<CodeIndexResult> IndexProjectAsync(Project project, CancellationToken ct = default)
    {
        var resolvedProject = await _manualIndexingTask.ResolveProjectAsync(project, ct);

        return await _codeIndexService.IndexDirectoryAsync(resolvedProject.Id, resolvedProject.LocalPath!, ct);
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

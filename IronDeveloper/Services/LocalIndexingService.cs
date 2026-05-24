using IronDev.Client.Chat;
using IronDev.Client.CodeIndex;
using IronDev.Client.Memory;
using IronDev.Client.Projects;
using IronDev.Client.Tickets;
using IronDev.Client.Traces;
using System;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.Services.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Agent.Services;

public sealed class LocalIndexingService : ILocalIndexingService
{
    private readonly ICodeIndexApiClient _codeIndexService;

    public LocalIndexingService(ICodeIndexApiClient codeIndexService)
    {
        _codeIndexService = codeIndexService;
    }

    public async Task<CodeIndexResult> IndexProjectAsync(Project project, CancellationToken ct = default)
    {
        return await _codeIndexService.IndexDirectoryAsync(project.Id, project.LocalPath!, ct);
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

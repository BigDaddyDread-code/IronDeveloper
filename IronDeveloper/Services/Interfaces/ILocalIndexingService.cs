using System.Threading;
using System.Threading.Tasks;
using IronDev.Data.Models;

namespace IronDev.Agent.Services.Interfaces;

public interface ILocalIndexingService
{
    Task<CodeIndexResult> IndexProjectAsync(Project project, CancellationToken ct = default);
    Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken ct = default);
}

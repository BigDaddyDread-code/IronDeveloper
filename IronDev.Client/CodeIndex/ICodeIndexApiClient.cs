using IronDev.Data.Models;

namespace IronDev.Client.CodeIndex;

public interface ICodeIndexApiClient
{
    Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default);
    Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default);
}

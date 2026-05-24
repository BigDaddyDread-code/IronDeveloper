using IronDev.Client.Http;
using IronDev.Data.Models;

namespace IronDev.Client.CodeIndex;

public sealed class CodeIndexApiClient : IronDevApiClientBase, ICodeIndexApiClient
{
    public CodeIndexApiClient(HttpClient http) : base(http)
    {
    }

    public Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default) =>
        PostAsync<CodeIndexResult>($"projects/{projectId}/code-index", new { directoryPath }, cancellationToken);

    public Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectFile>>($"projects/{projectId}/code-index/files/search?q={Uri.EscapeDataString(query)}&take={take}", cancellationToken);

    public Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default) =>
        GetAsync<int>($"projects/{projectId}/code-index/file-count", cancellationToken);

    public Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<ProjectFile>>($"projects/{projectId}/code-index/files/recent?take={take}", cancellationToken);

    public Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<CodeIndexEntry>>($"projects/{projectId}/memory/search/snippets?q={Uri.EscapeDataString(query)}&take={take}", cancellationToken);
}

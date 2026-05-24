using IronDev.Data.Models;

namespace IronDev.Client.Projects;

public interface IProjectsApiClient
{
    Task<int> CreateProjectAsync(Project project, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default);
    Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default);
    Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default);
    Task SelectProjectAsync(int projectId, CancellationToken cancellationToken = default);
    Task<string> ExportProjectContextPackAsync(int projectId, CancellationToken cancellationToken = default);
}

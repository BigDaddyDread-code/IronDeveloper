using IronDev.Client.Auth;
using IronDev.Client.Http;
using IronDev.Data.Models;

namespace IronDev.Client.Projects;

public sealed class ProjectsApiClient : IronDevApiClientBase, IProjectsApiClient
{
    private readonly IIronDevSession _session;

    public ProjectsApiClient(HttpClient http, IIronDevSession session)
        : base(http)
    {
        _session = session;
    }

    public async Task<int> CreateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        var created = await PostAsync<Project>("projects", project, cancellationToken);
        return created.Id;
    }

    public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<Project>>("projects", cancellationToken);

    public async Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var project = await GetAsync<Project>($"projects/{projectId}", cancellationToken);
        return project;
    }

    public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default) =>
        PutAsync<object>($"projects/{projectId}/local-path", new { localPath }, cancellationToken);

    public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default) =>
        PostAsync<object>($"projects/{projectId}/mark-index-stale", new { reason }, cancellationToken);

    public async Task SelectProjectAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await PostAsync<object>($"projects/{projectId}/select", new { }, cancellationToken);
        _session.SetActiveProject(projectId);
    }

    public Task<string> ExportProjectContextPackAsync(int projectId, CancellationToken cancellationToken = default) =>
        GetAsync<string>($"projects/{projectId}/context-pack", cancellationToken);
}

using IronDev.Client.Http;
using IronDev.Data.Models;

namespace IronDev.Client.Profiles;

public sealed class ProjectProfilesApiClient : IronDevApiClientBase, IProjectProfilesApiClient
{
    public ProjectProfilesApiClient(HttpClient http)
        : base(http)
    {
    }

    public Task<ProjectProfile?> GetProjectProfileAsync(int projectId, CancellationToken ct = default) =>
        GetAsync<ProjectProfile?>($"projects/{projectId}/profile", ct);

    public Task SaveProjectProfileAsync(ProjectProfile profile, CancellationToken ct = default) =>
        PostAsync<object>($"projects/{profile.ProjectId}/profile", profile, ct);

    public Task<List<ProjectCommand>> GetProjectCommandsAsync(int projectId, CancellationToken ct = default) =>
        GetAsync<List<ProjectCommand>>($"projects/{projectId}/profile/commands", ct);

    public Task SaveProjectCommandAsync(ProjectCommand command, CancellationToken ct = default) =>
        PostAsync<object>($"projects/{command.ProjectId}/profile/commands", command, ct);

    public Task<ProjectCommand?> GetDefaultCommandAsync(int projectId, string commandType, CancellationToken ct = default) =>
        GetAsync<ProjectCommand?>($"projects/{projectId}/profile/commands/default/{Uri.EscapeDataString(commandType)}", ct);

    public Task<List<ProjectProfileOption>> GetOptionsByCategoryAsync(string category, CancellationToken ct = default) =>
        GetAsync<List<ProjectProfileOption>>($"profile/options/{Uri.EscapeDataString(category)}", ct);

    public Task<ProjectProfileDetectionResult> DetectAsync(string projectRoot, int projectId = 0, CancellationToken ct = default) =>
        PostAsync<ProjectProfileDetectionResult>("profile/detect", new { projectRoot, projectId }, ct);
}

using IronDev.Data.Models;

namespace IronDev.Agent.Services;

/// <summary>
/// Local-only compatibility shim. Product indexing now goes through IronDev.Client and the API.
/// </summary>
public sealed class ManualIndexingTask
{
    public Task<Project> ResolveProjectAsync(Project project, CancellationToken ct = default) =>
        Task.FromResult(project);
}

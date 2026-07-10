using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectToolCatalogueService
{
    Task<ProjectToolCatalogueResponse?> GetCatalogueAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectToolDetailResponse?> GetToolAsync(
        int projectId,
        string toolId,
        CancellationToken cancellationToken = default);
}

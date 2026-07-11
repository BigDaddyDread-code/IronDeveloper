using IronDev.Core.WorkItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/work-items")]
public sealed class ProjectWorkItemsController : ControllerBase
{
    private readonly IProjectWorkItemReadService _workItems;

    public ProjectWorkItemsController(IProjectWorkItemReadService workItems)
    {
        _workItems = workItems;
    }

    [HttpGet("{workItemId:long}")]
    [ProducesResponseType(typeof(ProjectWorkItemReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectWorkItemReadModel>> Get(
        int projectId,
        long workItemId,
        CancellationToken cancellationToken)
    {
        var model = await _workItems.GetAsync(projectId, workItemId, cancellationToken).ConfigureAwait(false);
        return model is null ? NotFound() : Ok(model);
    }
}

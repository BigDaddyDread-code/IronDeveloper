using IronDev.Core.WorkItems;
using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/work-items")]
public sealed class ProjectWorkItemsController : ControllerBase
{
    private readonly IProjectWorkItemReadService _workItems;
    private readonly IProjectWorkItemCollaborationService _collaboration;
    private readonly ICurrentTenantContext _tenant;

    public ProjectWorkItemsController(
        IProjectWorkItemReadService workItems,
        IProjectWorkItemCollaborationService collaboration,
        ICurrentTenantContext tenant)
    {
        _workItems = workItems;
        _collaboration = collaboration;
        _tenant = tenant;
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

    [HttpPut("{workItemId:long}/collaboration")]
    [ProducesResponseType(typeof(ProjectWorkItemCollaborationSnapshot), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCollaboration(
        int projectId,
        long workItemId,
        [FromBody] SetProjectWorkItemCollaborationRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var result = await _collaboration.SetAsync(_tenant.TenantId, projectId, workItemId, currentUser.UserId, request, cancellationToken);
        return result.Status switch
        {
            ProjectWorkItemCollaborationMutationStatus.Succeeded => Ok(result.Collaboration),
            ProjectWorkItemCollaborationMutationStatus.CollaboratorNotProjectMember => Conflict(new { error = "Assignees, followers, and named waiting-on users must be active project members." }),
            _ => NotFound()
        };
    }
}

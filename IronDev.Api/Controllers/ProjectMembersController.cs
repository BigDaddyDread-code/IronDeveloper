using IronDev.Api.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/members")]
public sealed class ProjectMembersController : ControllerBase
{
    private readonly IProjectMemberDirectoryService _members;

    public ProjectMembersController(IProjectMemberDirectoryService members)
    {
        _members = members;
    }

    [HttpGet]
    public async Task<ActionResult<ProjectMemberDirectoryResponse>> GetDirectory(
        int projectId,
        CancellationToken cancellationToken)
    {
        var currentUser = new CurrentUserContext(
            HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var directory = await _members.GetDirectoryAsync(projectId, currentUser.UserId, cancellationToken);
        return directory is null ? NotFound() : Ok(directory);
    }
}

using IronDev.Api.Auth;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/governance")]
public sealed class ProjectGovernanceController : ControllerBase
{
    private readonly IProjectGovernanceOverviewService _governance;

    public ProjectGovernanceController(IProjectGovernanceOverviewService governance)
    {
        _governance = governance;
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(ProjectGovernanceOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectGovernanceOverview>> GetOverview(
        int projectId,
        CancellationToken cancellationToken)
    {
        var currentUser = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var model = await _governance.GetAsync(projectId, currentUser.UserId, cancellationToken).ConfigureAwait(false);
        return model is null ? NotFound() : Ok(model);
    }
}

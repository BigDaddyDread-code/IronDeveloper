using IronDev.Core.Board;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/board")]
public sealed class ProjectBoardController : ControllerBase
{
    private readonly IProjectBoardReadService _board;

    public ProjectBoardController(IProjectBoardReadService board)
    {
        _board = board;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ProjectBoardReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectBoardReadModel>> Get(
        int projectId,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        var model = await _board.GetAsync(projectId, take, cancellationToken).ConfigureAwait(false);
        return model is null ? NotFound() : Ok(model);
    }
}

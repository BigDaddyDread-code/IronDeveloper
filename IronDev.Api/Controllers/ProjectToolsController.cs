using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/tools")]
public sealed class ProjectToolsController : ControllerBase
{
    private readonly IProjectToolCatalogueService _tools;

    public ProjectToolsController(IProjectToolCatalogueService tools)
    {
        _tools = tools;
    }

    [HttpGet]
    public async Task<ActionResult<ProjectToolCatalogueResponse>> GetCatalogue(
        int projectId,
        CancellationToken cancellationToken)
    {
        var catalogue = await _tools.GetCatalogueAsync(projectId, cancellationToken);
        return catalogue is null ? NotFound() : Ok(catalogue);
    }

    [HttpGet("{toolId}")]
    public async Task<ActionResult<ProjectToolDetailResponse>> GetTool(
        int projectId,
        string toolId,
        CancellationToken cancellationToken)
    {
        var tool = await _tools.GetToolAsync(projectId, toolId, cancellationToken);
        return tool is null ? NotFound() : Ok(tool);
    }
}

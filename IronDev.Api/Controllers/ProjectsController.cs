using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IProjectContextExportService _export;

    public ProjectsController(IProjectService projects, IProjectContextExportService export)
    {
        _projects = projects;
        _export = export;
    }

    [HttpGet]
    public async Task<IReadOnlyList<Project>> GetProjects(CancellationToken ct) =>
        await _projects.GetProjectsAsync(ct);

    [HttpGet("{projectId:int}")]
    public async Task<ActionResult<Project>> GetProject(int projectId, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(projectId, ct);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<Project>> Create(Project project, CancellationToken ct)
    {
        var id = await _projects.CreateProjectAsync(project, ct);
        project.Id = id;
        return CreatedAtAction(nameof(GetProject), new { projectId = id }, project);
    }

    [HttpPatch("{projectId:int}")]
    public async Task<ActionResult<Project>> UpdateProject(int projectId, Project project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            return BadRequest(new { error = "Project name is required." });

        var updated = await _projects.UpdateProjectAsync(projectId, project, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{projectId:int}/select")]
    public IActionResult SelectProject(int projectId) => Ok(new { projectId });

    [HttpPut("{projectId:int}/local-path")]
    public async Task<IActionResult> UpdateLocalPath(int projectId, [FromBody] UpdateLocalPathRequest request, CancellationToken ct)
    {
        await _projects.UpdateLocalPathAsync(projectId, request.LocalPath, ct);
        return NoContent();
    }

    [HttpPost("{projectId:int}/mark-index-stale")]
    public async Task<IActionResult> MarkIndexStale(int projectId, [FromBody] MarkIndexStaleRequest request, CancellationToken ct)
    {
        await _projects.MarkIndexStaleAsync(projectId, request.Reason, ct);
        return Ok();
    }

    public sealed record UpdateLocalPathRequest(string LocalPath);
    public sealed record MarkIndexStaleRequest(string Reason);

    [HttpGet("{projectId:int}/context-pack")]
    public Task<string> ExportContextPack(int projectId) =>
        _export.ExportProjectContextPackAsync(projectId);
}

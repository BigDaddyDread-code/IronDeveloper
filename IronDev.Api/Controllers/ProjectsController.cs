using IronDev.Data.Models;
using IronDev.Api.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Core.Models;
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
    private readonly IProjectMembershipService _memberships;
    private readonly ICurrentTenantContext _tenant;

    public ProjectsController(
        IProjectService projects,
        IProjectContextExportService export,
        IProjectMembershipService memberships,
        ICurrentTenantContext tenant)
    {
        _projects = projects;
        _export = export;
        _memberships = memberships;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IReadOnlyList<Project>> GetProjects(CancellationToken ct)
    {
        var user = CurrentUser();
        var accessible = await _memberships.GetAccessibleProjectIdsAsync(_tenant.TenantId, user.UserId, ct);
        return (await _projects.GetProjectsAsync(ct)).Where(project => accessible.Contains(project.Id)).ToArray();
    }

    [HttpGet("{projectId:int}")]
    public async Task<ActionResult<Project>> GetProject(int projectId, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(projectId, ct);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<Project>> Create(Project project, CancellationToken ct)
    {
        if (project.TenantId > 0 && project.TenantId != _tenant.TenantId)
        {
            return BadRequest(GovernedRefusal.Create(
                "selected_tenant_body_scope_mismatch",
                "Project tenantId must be omitted or match the selected tenant.",
                HttpContext.TraceIdentifier,
                blockedReasons: ["The selected tenant is authoritative for project creation."],
                nextSafeActions: ["Remove tenantId from the request or select the intended accessible tenant first."],
                forbiddenActions: ["Create a project in another tenant through body data."]));
        }

        project.TenantId = _tenant.TenantId;
        var id = await _projects.CreateProjectAsync(project, ct);
        var user = CurrentUser();
        var membership = await _memberships.SetMemberAsync(_tenant.TenantId, id, user.UserId, user.UserId, ProjectMemberRoles.Owner, ct);
        if (membership != ProjectMembershipMutationStatus.Succeeded)
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Project was created, but its owner membership could not be established." });
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

    private CurrentUserContext CurrentUser() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    [HttpGet("{projectId:int}/context-pack")]
    public Task<string> ExportContextPack(int projectId) =>
        _export.ExportProjectContextPackAsync(projectId);
}

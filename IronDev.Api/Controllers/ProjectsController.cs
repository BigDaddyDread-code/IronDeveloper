using IronDev.Data.Models;
using IronDev.Api.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Auth;
using IronDev.Core.Governance;
using IronDev.Core.Models;
using IronDev.Services;
using IronDev.Core.RunReadiness;
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
    private readonly IProjectApplyCapabilityService _applyCapability;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectService projects,
        IProjectContextExportService export,
        IProjectMembershipService memberships,
        ICurrentTenantContext tenant,
        IProjectApplyCapabilityService applyCapability,
        ILogger<ProjectsController> logger)
    {
        _projects = projects;
        _export = export;
        _memberships = memberships;
        _tenant = tenant;
        _applyCapability = applyCapability;
        _logger = logger;
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
        // Connect project is the browser's deliberate repository-selection action.
        // In an explicit project-work session the authority owner qualifies only a
        // safe disposable child; in every other session this is a no-op decision.
        await TryQualifyDisposableProjectAsync(id, user.UserId, ct);
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
    public async Task<IActionResult> SelectProject(int projectId, CancellationToken ct)
    {
        var user = CurrentUser();
        // Selecting an existing project is the explicit, authenticated requalification
        // point for a new launcher session. The apply boundary still rechecks the
        // signed server record and its matching non-secret Git correlation marker.
        await TryQualifyDisposableProjectAsync(projectId, user.UserId, ct);
        return Ok(new { projectId });
    }

    [HttpPut("{projectId:int}/local-path")]
    public async Task<IActionResult> UpdateLocalPath(int projectId, [FromBody] UpdateLocalPathRequest request, CancellationToken ct)
    {
        await _projects.UpdateLocalPathAsync(projectId, request.LocalPath, ct);
        // A deliberate path change invalidates the old path binding and explicitly
        // creates a new authenticated, session-bound server qualification when safe.
        // Apply still requires a live record/marker agreement at the mutation boundary.
        var user = CurrentUser();
        await TryQualifyDisposableProjectAsync(projectId, user.UserId, ct);
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

    private async Task TryQualifyDisposableProjectAsync(int projectId, int qualifyingActorUserId, CancellationToken ct)
    {
        try
        {
            var capability = await _applyCapability
                .QualifyDisposableProjectAsync(projectId, qualifyingActorUserId, ct);
            if (!capability.IsReady && capability.ReasonCode != ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled)
            {
                _logger.LogWarning(
                    "Project {ProjectId} was retained but disposable qualification is not ready: {ReasonCode}",
                    projectId,
                    capability.ReasonCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The project/path mutation is already durable. Preserve its identity so a
            // retry can select and requalify it instead of creating a duplicate project.
            _logger.LogError(exception,
                "Project {ProjectId} was retained after disposable qualification failed unexpectedly.",
                projectId);
        }
    }

    [HttpGet("{projectId:int}/context-pack")]
    public Task<string> ExportContextPack(int projectId) =>
        _export.ExportProjectContextPackAsync(projectId);
}

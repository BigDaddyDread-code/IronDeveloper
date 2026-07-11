using IronDev.Api.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/members")]
public sealed class ProjectMembersController : ControllerBase
{
    private readonly IProjectMemberDirectoryService _members;
    private readonly IProjectMembershipService _projectMemberships;
    private readonly IProjectService _projects;

    public ProjectMembersController(
        IProjectMemberDirectoryService members,
        IProjectMembershipService projectMemberships,
        IProjectService projects)
    {
        _members = members;
        _projectMemberships = projectMemberships;
        _projects = projects;
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

    public sealed record SetProjectMembershipRequest(string ProjectRole);

    [HttpPut("{userId:int}")]
    public async Task<IActionResult> SetProjectMembership(
        int projectId,
        int userId,
        [FromBody] SetProjectMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!ProjectMemberRoles.IsKnown(request.ProjectRole))
            return BadRequest(new { error = "Unknown project role. Use Owner, Contributor, or Viewer." });
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null) return NotFound();
        var currentUser = CurrentUser();
        var status = await _projectMemberships.SetMemberAsync(project.TenantId, project.Id, userId, currentUser.UserId, request.ProjectRole, cancellationToken);
        return Map(status);
    }

    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> RemoveProjectMembership(int projectId, int userId, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null) return NotFound();
        var currentUser = CurrentUser();
        var status = await _projectMemberships.RemoveMemberAsync(project.TenantId, project.Id, userId, currentUser.UserId, cancellationToken);
        return Map(status);
    }

    private IActionResult Map(ProjectMembershipMutationStatus status) => status switch
    {
        ProjectMembershipMutationStatus.Succeeded => Ok(new { message = "Project membership saved." }),
        ProjectMembershipMutationStatus.TargetUserNotTenantMember => NotFound(new { error = "That user is not an active tenant member." }),
        ProjectMembershipMutationStatus.MembershipNotFound => NotFound(new { error = "That project membership does not exist." }),
        ProjectMembershipMutationStatus.LastOwnerProtected => Conflict(new { error = "The project's last owner cannot be demoted or removed." }),
        ProjectMembershipMutationStatus.CallerCannotAdminister => StatusCode(StatusCodes.Status403Forbidden, new { error = "Only a project owner can administer project membership." }),
        _ => NotFound()
    };

    private CurrentUserContext CurrentUser() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
}

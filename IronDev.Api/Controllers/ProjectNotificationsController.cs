using IronDev.Api.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/notifications")]
public sealed class ProjectNotificationsController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IUserService _users;
    private readonly IProjectChannelChatService _channels;

    public ProjectNotificationsController(
        IProjectService projects,
        IUserService users,
        IProjectChannelChatService channels)
    {
        _projects = projects;
        _users = users;
        _channels = channels;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ProjectNotificationListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(int projectId, CancellationToken cancellationToken)
    {
        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (callerRole is null)
            return NotFound();

        return Ok(await _channels.ListNotificationsAsync(
            project.TenantId, project.Id, context.UserId, cancellationToken));
    }

    [HttpPost("{notificationId:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(
        int projectId,
        long notificationId,
        CancellationToken cancellationToken)
    {
        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (callerRole is null)
            return NotFound();

        var changed = await _channels.MarkNotificationReadAsync(
            project.TenantId, project.Id, context.UserId, notificationId, cancellationToken);
        return changed ? NoContent() : NotFound();
    }

    private CurrentUserContext CurrentUser() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
}

using IronDev.Api.Auth;
using IronDev.Core.Channels;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

/// <summary>
/// Administers channel visibility and moderation membership only. Channel roles never grant
/// workflow authority, approval eligibility, tool access, or source mutation permission.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/projects/{projectId:int}/channels/{channelId:long}/members")]
public sealed class ProjectChannelMembersController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IUserService _users;
    private readonly IProjectChannelMembershipService _memberships;
    private readonly ISecurityAuditLog _securityAuditLog;
    private readonly ILogger<ProjectChannelMembersController> _logger;

    public ProjectChannelMembersController(
        IProjectService projects,
        IUserService users,
        IProjectChannelMembershipService memberships,
        ISecurityAuditLog securityAuditLog,
        ILogger<ProjectChannelMembersController> logger)
    {
        _projects = projects;
        _users = users;
        _memberships = memberships;
        _securityAuditLog = securityAuditLog;
        _logger = logger;
    }

    public sealed record SetProjectChannelMembershipRequest(string ChannelRole, string NotificationLevel);

    [HttpPut("{userId:int}")]
    public async Task<IActionResult> SetMembership(
        int projectId,
        long channelId,
        int userId,
        [FromBody] SetProjectChannelMembershipRequest request,
        CancellationToken cancellationToken)
    {
        if (!ProjectChannelRoles.All.Contains(request.ChannelRole))
            return BadRequest(new { error = "Unknown channel role. Use Owner, Moderator, Member, or ReadOnly." });
        if (!ProjectChannelNotificationLevels.Values.Contains(request.NotificationLevel))
            return BadRequest(new { error = "Unknown notification level. Use All, Mentions, or None." });

        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return await DenyAsync(context, project.TenantId, userId, channelId, cancellationToken);

        var status = await _memberships.SetMembershipAsync(
            project.TenantId,
            project.Id,
            channelId,
            userId,
            context.UserId,
            ProjectChannelRoles.All.First(role => role.Equals(request.ChannelRole, StringComparison.OrdinalIgnoreCase)),
            ProjectChannelNotificationLevels.Values.First(level => level.Equals(request.NotificationLevel, StringComparison.OrdinalIgnoreCase)),
            cancellationToken);
        var failure = MapFailure(status);
        if (failure is not null)
            return failure;

        if (!await TryAuditAsync(context, project.TenantId, userId, channelId, "ProjectChannelMembershipSet", cancellationToken))
            return AuditUnavailable();

        return Ok(new { message = "Channel membership saved." });
    }

    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> RemoveMembership(
        int projectId,
        long channelId,
        int userId,
        CancellationToken cancellationToken)
    {
        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return await DenyAsync(context, project.TenantId, userId, channelId, cancellationToken);

        var status = await _memberships.RemoveMembershipAsync(
            project.TenantId,
            project.Id,
            channelId,
            userId,
            cancellationToken);
        var failure = MapFailure(status);
        if (failure is not null)
            return failure;

        if (!await TryAuditAsync(context, project.TenantId, userId, channelId, "ProjectChannelMembershipRemoved", cancellationToken))
            return AuditUnavailable();

        return Ok(new { message = "Channel membership removed." });
    }

    private IActionResult? MapFailure(ProjectChannelMembershipMutationStatus status) => status switch
    {
        ProjectChannelMembershipMutationStatus.ChannelNotFound => NotFound(new { error = "Channel not found in this project." }),
        ProjectChannelMembershipMutationStatus.TargetUserNotTenantMember => NotFound(new { error = "That user is not an active member of this tenant." }),
        ProjectChannelMembershipMutationStatus.MembershipNotFound => NotFound(new { error = "That channel membership does not exist." }),
        ProjectChannelMembershipMutationStatus.LastOwnerProtected => Conflict(new { error = "The channel's last owner cannot be demoted or removed." }),
        _ => null
    };

    private async Task<IActionResult> DenyAsync(
        CurrentUserContext context,
        int tenantId,
        int targetUserId,
        long channelId,
        CancellationToken cancellationToken)
    {
        var audit = BuildAuditEvent(
            SecurityAuditEventType.AdminSecurityChangeDenied,
            SecurityAuditOutcome.Denied,
            context,
            tenantId,
            targetUserId,
            channelId,
            "CallerCannotAdministerChannelMembership");
        if (!await TryAppendAuditEventAsync(audit, cancellationToken))
            return AuditUnavailable();

        return StatusCode(StatusCodes.Status403Forbidden, new { error = "You cannot administer channel membership for this project." });
    }

    private async Task<bool> TryAuditAsync(
        CurrentUserContext context,
        int tenantId,
        int targetUserId,
        long channelId,
        string reasonCode,
        CancellationToken cancellationToken) =>
        await TryAppendAuditEventAsync(BuildAuditEvent(
            SecurityAuditEventType.AdminSecurityChangeSucceeded,
            SecurityAuditOutcome.Succeeded,
            context,
            tenantId,
            targetUserId,
            channelId,
            reasonCode), cancellationToken);

    private async Task<bool> TryAppendAuditEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _securityAuditLog.AppendAsync(auditEvent, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Security audit append failed for {EventType}", auditEvent.EventType);
            return false;
        }
    }

    private SecurityAuditEvent BuildAuditEvent(
        SecurityAuditEventType eventType,
        SecurityAuditOutcome outcome,
        CurrentUserContext context,
        int targetTenantId,
        int targetUserId,
        long channelId,
        string reasonCode) => new()
        {
            EventType = eventType,
            Outcome = outcome,
            ActorUserId = context.UserId.ToString(),
            ActorEmailHash = SecurityAuditEvent.HashRedacted(context.Email),
            TenantId = context.TenantId?.ToString() ?? string.Empty,
            TargetUserId = targetUserId.ToString(),
            TargetTenantId = targetTenantId.ToString(),
            ReasonCode = reasonCode,
            CorrelationId = HttpContext.TraceIdentifier,
            RemoteIpHash = SecurityAuditEvent.HashRedacted(HttpContext.Connection.RemoteIpAddress?.ToString()),
            UserAgentHash = SecurityAuditEvent.HashRedacted(Request.Headers.UserAgent.ToString()),
            RequestPath = Request.Path.Value ?? string.Empty,
            Authenticated = User.Identity?.IsAuthenticated == true,
            Metadata = new Dictionary<string, string>
            {
                ["controller"] = nameof(ProjectChannelMembersController),
                ["channelId"] = channelId.ToString()
            }
        };

    private CurrentUserContext CurrentUser() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private IActionResult AuditUnavailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });
}

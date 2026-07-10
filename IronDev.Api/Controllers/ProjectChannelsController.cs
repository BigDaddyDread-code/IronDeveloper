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
/// Shared project conversation. Channel creation changes collaboration visibility only; channel
/// messages never grant approval, workflow authority, tool access, source mutation, or release state.
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/channels")]
public sealed class ProjectChannelsController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IUserService _users;
    private readonly IProjectChannelChatService _channels;
    private readonly ISecurityAuditLog _securityAuditLog;
    private readonly ILogger<ProjectChannelsController> _logger;

    public ProjectChannelsController(
        IProjectService projects,
        IUserService users,
        IProjectChannelChatService channels,
        ISecurityAuditLog securityAuditLog,
        ILogger<ProjectChannelsController> logger)
    {
        _projects = projects;
        _users = users;
        _channels = channels;
        _securityAuditLog = securityAuditLog;
        _logger = logger;
    }

    public sealed record CreateProjectChannelRequest(string Name, string? Description, string Visibility);
    public sealed record PostProjectChannelMessageRequest(string Message);

    [HttpGet]
    public async Task<IActionResult> ListChannels(int projectId, CancellationToken cancellationToken)
    {
        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (callerRole is null)
            return NotFound();

        var channels = await _channels.ListVisibleChannelsAsync(
            project.TenantId, project.Id, context.UserId, cancellationToken);
        return Ok(new ProjectChannelChatListResponse(
            TenantUserRoles.CanAdministerUsers(callerRole),
            channels,
            ProjectChannelBoundaries.Channel));
    }

    [HttpGet("{channelReference}")]
    public async Task<IActionResult> GetChannel(
        int projectId,
        string channelReference,
        CancellationToken cancellationToken)
    {
        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (callerRole is null)
            return NotFound();

        var channel = await _channels.GetChannelAsync(
            project.TenantId, project.Id, context.UserId, channelReference, cancellationToken);
        return channel is null
            ? NotFound(new { error = "Channel not found or not visible to this user." })
            : Ok(channel);
    }

    [HttpPost]
    [EnableRateLimiting("SensitiveApiPolicy")]
    public async Task<IActionResult> CreateChannel(
        int projectId,
        [FromBody] CreateProjectChannelRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateCreate(request);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return await DenyCreateAsync(context, project.TenantId, cancellationToken);

        var result = await _channels.CreateChannelAsync(
            project.TenantId,
            project.Id,
            context.UserId,
            request.Name,
            request.Description,
            ProjectChannelVisibility.All.First(value => value.Equals(request.Visibility, StringComparison.OrdinalIgnoreCase)),
            cancellationToken);
        if (result.Status == ProjectChannelChatMutationStatus.DuplicateName)
            return Conflict(new { error = "An active channel with that name already exists." });

        if (!await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AdminSecurityChangeSucceeded,
                SecurityAuditOutcome.Succeeded,
                context,
                project.TenantId,
                result.Channel?.ChannelId.ToString() ?? string.Empty,
                "ProjectChannelCreated"), cancellationToken))
            return AuditUnavailable();

        return CreatedAtAction(
            nameof(GetChannel),
            new { projectId = project.Id, channelReference = result.Channel!.Slug },
            result.Channel);
    }

    [HttpPost("{channelReference}/messages")]
    [EnableRateLimiting("SensitiveApiPolicy")]
    public async Task<IActionResult> PostMessage(
        int projectId,
        string channelReference,
        [FromBody] PostProjectChannelMessageRequest request,
        CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { error = "A message is required." });
        if (message.Length > 10_000)
            return BadRequest(new { error = "Channel messages cannot exceed 10000 characters." });

        var context = CurrentUser();
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return NotFound(new { error = "Project not found in the current tenant." });

        var callerRole = await _users.GetTenantRoleAsync(context.UserId, project.TenantId, cancellationToken);
        if (callerRole is null)
            return NotFound();

        var result = await _channels.PostHumanMessageAsync(
            project.TenantId,
            project.Id,
            context.UserId,
            channelReference,
            message,
            cancellationToken);
        return result.Status switch
        {
            ProjectChannelChatMutationStatus.Succeeded => Ok(result.Message),
            ProjectChannelChatMutationStatus.ReadOnly => StatusCode(StatusCodes.Status403Forbidden, new { error = "Your channel role is Read only. You cannot post messages." }),
            ProjectChannelChatMutationStatus.AssistantInvocationNotImplemented => StatusCode(StatusCodes.Status501NotImplemented, new { error = "IronDev participation in shared channels is not implemented. Remove @IronDev to post a human message." }),
            _ => NotFound(new { error = "Channel not found or not visible to this user." })
        };
    }

    private static string? ValidateCreate(CreateProjectChannelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "A channel name is required.";
        if (request.Name.Trim().Length > 100)
            return "Channel names cannot exceed 100 characters.";
        if (request.Description?.Trim().Length > 500)
            return "Channel descriptions cannot exceed 500 characters.";
        if (!ProjectChannelVisibility.All.Contains(request.Visibility))
            return "Visibility must be Project or MembersOnly.";
        return null;
    }

    private async Task<IActionResult> DenyCreateAsync(
        CurrentUserContext context,
        int tenantId,
        CancellationToken cancellationToken)
    {
        if (!await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AdminSecurityChangeDenied,
                SecurityAuditOutcome.Denied,
                context,
                tenantId,
                string.Empty,
                "CallerCannotCreateProjectChannel"), cancellationToken))
            return AuditUnavailable();

        return StatusCode(StatusCodes.Status403Forbidden, new { error = "You cannot create channels for this project." });
    }

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
        string channelId,
        string reasonCode) => new()
        {
            EventType = eventType,
            Outcome = outcome,
            ActorUserId = context.UserId.ToString(),
            ActorEmailHash = SecurityAuditEvent.HashRedacted(context.Email),
            TenantId = context.TenantId?.ToString() ?? string.Empty,
            TargetUserId = string.Empty,
            TargetTenantId = targetTenantId.ToString(),
            ReasonCode = reasonCode,
            CorrelationId = HttpContext.TraceIdentifier,
            RemoteIpHash = SecurityAuditEvent.HashRedacted(HttpContext.Connection.RemoteIpAddress?.ToString()),
            UserAgentHash = SecurityAuditEvent.HashRedacted(Request.Headers.UserAgent.ToString()),
            RequestPath = Request.Path.Value ?? string.Empty,
            Authenticated = User.Identity?.IsAuthenticated == true,
            Metadata = new Dictionary<string, string>
            {
                ["controller"] = nameof(ProjectChannelsController),
                ["channelId"] = channelId
            }
        };

    private CurrentUserContext CurrentUser() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private IActionResult AuditUnavailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });
}

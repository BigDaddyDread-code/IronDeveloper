using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public sealed class TenantController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ISecurityAuditLog _securityAuditLog;
    private readonly ILogger<TenantController> _logger;

    public TenantController(
        IUserService userService,
        IJwtTokenService jwtTokenService,
        ISecurityAuditLog securityAuditLog,
        ILogger<TenantController> logger)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
        _securityAuditLog = securityAuditLog;
        _logger = logger;
    }

    /// <summary>GET /api/tenants — returns tenants the current user is a member of.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        var tenants = await _userService.GetUserTenantsAsync(ctx.UserId, ct);
        return Ok(tenants);
    }

    /// <summary>
    /// POST /api/tenants/select — verifies membership, then re-issues a JWT with tenant_id claim.
    /// Returns 403 if the user is not a member of the requested tenant.
    /// </summary>
    [HttpPost("select")]
    public async Task<IActionResult> SelectTenant([FromBody] SelectTenantRequest request, CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

        var isMember = await _userService.IsMemberOfTenantAsync(ctx.UserId, request.TenantId, ct);
        if (!isMember)
        {
            var auditWritten = await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.TenantSelectionDenied,
                SecurityAuditOutcome.Denied,
                actorUserId: ctx.UserId.ToString(),
                actorEmail: ctx.Email,
                tenantId: ctx.TenantId?.ToString() ?? string.Empty,
                targetUserId: ctx.UserId.ToString(),
                targetTenantId: request.TenantId.ToString(),
                reasonCode: "UserNotTenantMember"), ct);
            if (!auditWritten)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

            _logger.LogWarning("User {UserId} attempted to select unassigned tenant {TenantId}",
                ctx.UserId, request.TenantId);
            return Forbid();
        }

        // Re-issue a full token that now carries tenant_id.
        var user = await _userService.GetByIdAsync(ctx.UserId, ct);
        if (user is null)
        {
            var auditWritten = await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.TenantSelectionDenied,
                SecurityAuditOutcome.Denied,
                actorUserId: ctx.UserId.ToString(),
                actorEmail: ctx.Email,
                targetUserId: ctx.UserId.ToString(),
                targetTenantId: request.TenantId.ToString(),
                reasonCode: "UserRecordMissing"), ct);
            if (!auditWritten)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

            return Unauthorized();
        }

        var tenantAuditWritten = await TryAppendAuditEventAsync(BuildAuditEvent(
            SecurityAuditEventType.TenantSelectionSucceeded,
            SecurityAuditOutcome.Succeeded,
            actorUserId: ctx.UserId.ToString(),
            actorEmail: user.Email,
            tenantId: request.TenantId.ToString(),
            targetUserId: user.Id.ToString(),
            targetTenantId: request.TenantId.ToString(),
            reasonCode: "TenantMembershipAccepted"), ct);
        if (!tenantAuditWritten)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.DisplayName, request.TenantId);

        _logger.LogInformation("User {UserId} selected tenant {TenantId}", ctx.UserId, request.TenantId);
        return Ok(new LoginResponse(token, user.Id, user.DisplayName));
    }

    private async Task<bool> TryAppendAuditEventAsync(SecurityAuditEvent auditEvent, CancellationToken ct)
    {
        try
        {
            await _securityAuditLog.AppendAsync(auditEvent, ct);
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
        string actorUserId,
        string actorEmail,
        string tenantId = "",
        string targetUserId = "",
        string targetTenantId = "",
        string reasonCode = "") =>
        new()
        {
            EventType = eventType,
            Outcome = outcome,
            ActorUserId = actorUserId,
            ActorEmailHash = SecurityAuditEvent.HashRedacted(actorEmail),
            TenantId = tenantId,
            TargetUserId = targetUserId,
            TargetTenantId = targetTenantId,
            ReasonCode = reasonCode,
            CorrelationId = HttpContext.TraceIdentifier,
            RemoteIpHash = SecurityAuditEvent.HashRedacted(HttpContext.Connection.RemoteIpAddress?.ToString()),
            UserAgentHash = SecurityAuditEvent.HashRedacted(Request.Headers.UserAgent.ToString()),
            RequestPath = Request.Path.Value ?? string.Empty,
            Authenticated = User.Identity?.IsAuthenticated == true,
            Metadata = new Dictionary<string, string>
            {
                ["controller"] = nameof(TenantController)
            }
        };
}

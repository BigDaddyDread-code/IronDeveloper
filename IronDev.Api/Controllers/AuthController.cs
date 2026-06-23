using System.ComponentModel.DataAnnotations;
using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ISecurityAuditLog _securityAuditLog;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserService userService,
        IJwtTokenService jwtTokenService,
        ISecurityAuditLog securityAuditLog,
        ILogger<AuthController> logger)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
        _securityAuditLog = securityAuditLog;
        _logger = logger;
    }

    /// <summary>POST /api/auth/login — issues a base JWT (no tenant claim yet).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Password is required." });

        var user = await _userService.ValidateCredentialsAsync(request.Email, request.Password, ct);
        if (user is null)
        {
            var auditWritten = await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AuthLoginFailed,
                SecurityAuditOutcome.Denied,
                actorEmail: request.Email,
                reasonCode: "InvalidCredentials",
                authenticated: false), ct);
            if (!auditWritten)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

            _logger.LogWarning("Failed login attempt for {Email}", request.Email);
            return Unauthorized(new { error = "Invalid email or password." });
        }

        var loginAuditWritten = await TryAppendAuditEventAsync(BuildAuditEvent(
            SecurityAuditEventType.AuthLoginSucceeded,
            SecurityAuditOutcome.Succeeded,
            actorUserId: user.Id.ToString(),
            actorEmail: user.Email,
            targetUserId: user.Id.ToString(),
            reasonCode: "CredentialsAccepted",
            authenticated: false), ct);
        if (!loginAuditWritten)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.DisplayName);

        _logger.LogInformation("User {UserId} logged in successfully", user.Id);
        return Ok(new LoginResponse(token, user.Id, user.DisplayName));
    }

    /// <summary>GET /api/auth/me — returns the current user's profile from JWT claims.</summary>
    [HttpGet("me")]
    [Authorize]
    [EnableRateLimiting("SensitiveApiPolicy")]
    public IActionResult Me()
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices
            .GetRequiredService<IHttpContextAccessor>());

        return Ok(new UserProfileDto(ctx.UserId, ctx.Email, ctx.DisplayName, ctx.TenantId));
    }

    /// <summary>POST /api/auth/logout — stateless JWT; just returns 200.</summary>
    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting("SensitiveApiPolicy")]
    public IActionResult Logout()
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices
            .GetRequiredService<IHttpContextAccessor>());
        var auditWritten = TryAppendAuditEventAsync(BuildAuditEvent(
            SecurityAuditEventType.AuthLogoutRequested,
            SecurityAuditOutcome.Succeeded,
            actorUserId: ctx.UserId.ToString(),
            tenantId: ctx.TenantId?.ToString() ?? string.Empty,
            targetUserId: ctx.UserId.ToString(),
            reasonCode: "StatelessLogoutRequested",
            authenticated: User.Identity?.IsAuthenticated == true), HttpContext.RequestAborted)
            .GetAwaiter()
            .GetResult();
        if (!auditWritten)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

        return Ok(new { message = "Logged out." });
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
        string actorUserId = "",
        string actorEmail = "",
        string tenantId = "",
        string targetUserId = "",
        string targetTenantId = "",
        string reasonCode = "",
        bool authenticated = false) =>
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
            Authenticated = authenticated,
            Metadata = new Dictionary<string, string>
            {
                ["controller"] = nameof(AuthController)
            }
        };
}

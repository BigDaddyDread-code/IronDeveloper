using IronDev.Api.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

/// <summary>
/// Tenant user administration: list members, add a user, change a membership role,
/// remove a membership.
///
/// Boundary: role decides visibility, never mutation authority. These endpoints manage
/// membership records only — no role grants workflow, source, memory, or approval authority,
/// and no endpoint here can. Mutations require an administering role (Owner or TenantAdmin)
/// within the same tenant, and every attempt is security-audited fail-closed.
/// </summary>
[ApiController]
[Route("api/tenants/{tenantId:int}/users")]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
public sealed class TenantUsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ISecurityAuditLog _securityAuditLog;
    private readonly ILogger<TenantUsersController> _logger;

    public TenantUsersController(
        IUserService userService,
        ISecurityAuditLog securityAuditLog,
        ILogger<TenantUsersController> logger)
    {
        _userService = userService;
        _securityAuditLog = securityAuditLog;
        _logger = logger;
    }

    public sealed record TenantUserDto(int Id, string Email, string DisplayName, string Role, bool IsActive);

    public sealed record CreateTenantUserRequest(string Email, string DisplayName, string? Password, string Role);

    public sealed record SetTenantUserRoleRequest(string Role);

    /// <summary>GET — members of the tenant. Caller must be a member of the same tenant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers(int tenantId, CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, tenantId, ct);
        if (callerRole is null)
            return await DenyAsync(ctx, tenantId, targetUserId: null, "CallerNotTenantMember", ct);

        var users = await _userService.GetTenantUsersAsync(tenantId, ct);
        return Ok(users.Select(u => new TenantUserDto(u.Id, u.Email, u.DisplayName, u.Role, u.IsActive)).ToArray());
    }

    /// <summary>POST — add a user to the tenant. Caller must be Owner or TenantAdmin.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateUser(int tenantId, [FromBody] CreateTenantUserRequest request, CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

        var validationError = ValidateCreateRequest(request);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, tenantId, ct);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return await DenyAsync(ctx, tenantId, targetUserId: null, "CallerCannotAdministerUsers", ct);

        var result = await _userService.CreateTenantUserAsync(
            tenantId, request.Email.Trim(), request.DisplayName.Trim(), request.Password, request.Role, ct);

        switch (result.Status)
        {
            case TenantUserMutationStatus.AlreadyMember:
                return Conflict(new { error = "That user is already a member of this tenant." });
            case TenantUserMutationStatus.PasswordRequired:
                return BadRequest(new { error = "A password is required for a new user account." });
        }

        var created = result.User!;
        if (!await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AdminSecurityChangeSucceeded,
                SecurityAuditOutcome.Succeeded,
                ctx, tenantId, created.Id.ToString(), "TenantUserCreated"), ct))
            return AuditUnavailable();

        return Ok(new TenantUserDto(created.Id, created.Email, created.DisplayName, created.Role, created.IsActive));
    }

    /// <summary>PUT {userId}/role — change a membership role. Caller must be Owner or TenantAdmin.</summary>
    [HttpPut("{userId:int}/role")]
    public async Task<IActionResult> SetRole(int tenantId, int userId, [FromBody] SetTenantUserRoleRequest request, CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

        if (!TenantUserRoles.IsKnown(request.Role))
            return BadRequest(new { error = $"Unknown role. Known roles: {string.Join(", ", TenantUserRoles.All)}." });

        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, tenantId, ct);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return await DenyAsync(ctx, tenantId, userId.ToString(), "CallerCannotAdministerUsers", ct);

        var result = await _userService.SetTenantUserRoleAsync(tenantId, userId, request.Role, ct);
        var failure = MapMutationFailure(result.Status);
        if (failure is not null)
            return failure;

        if (!await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AdminSecurityChangeSucceeded,
                SecurityAuditOutcome.Succeeded,
                ctx, tenantId, userId.ToString(), "TenantUserRoleChanged"), ct))
            return AuditUnavailable();

        return Ok(new { message = "Role updated." });
    }

    /// <summary>DELETE {userId} — remove the membership (the account survives). Caller must be Owner or TenantAdmin.</summary>
    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> RemoveUser(int tenantId, int userId, CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, tenantId, ct);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return await DenyAsync(ctx, tenantId, userId.ToString(), "CallerCannotAdministerUsers", ct);

        var result = await _userService.RemoveTenantUserAsync(tenantId, userId, ct);
        var failure = MapMutationFailure(result.Status);
        if (failure is not null)
            return failure;

        if (!await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AdminSecurityChangeSucceeded,
                SecurityAuditOutcome.Succeeded,
                ctx, tenantId, userId.ToString(), "TenantUserMembershipRemoved"), ct))
            return AuditUnavailable();

        return Ok(new { message = "Membership removed." });
    }

    private static string? ValidateCreateRequest(CreateTenantUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return "A valid email is required.";
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return "A display name is required.";
        if (!TenantUserRoles.IsKnown(request.Role))
            return $"Unknown role. Known roles: {string.Join(", ", TenantUserRoles.All)}.";
        if (request.Password is not null && request.Password.Length > 0 && request.Password.Length < 8)
            return "Password must be at least 8 characters.";
        return null;
    }

    private IActionResult? MapMutationFailure(TenantUserMutationStatus status) => status switch
    {
        TenantUserMutationStatus.NotFound => NotFound(new { error = "That user is not a member of this tenant." }),
        TenantUserMutationStatus.LastOwnerProtected => Conflict(new { error = "The tenant's last owner cannot be demoted or removed." }),
        _ => null
    };

    private async Task<IActionResult> DenyAsync(
        CurrentUserContext ctx,
        int tenantId,
        string? targetUserId,
        string reasonCode,
        CancellationToken ct)
    {
        if (!await TryAppendAuditEventAsync(BuildAuditEvent(
                SecurityAuditEventType.AdminSecurityChangeDenied,
                SecurityAuditOutcome.Denied,
                ctx, tenantId, targetUserId ?? string.Empty, reasonCode), ct))
            return AuditUnavailable();

        return StatusCode(StatusCodes.Status403Forbidden, new { error = "You cannot administer users for this tenant." });
    }

    private IActionResult AuditUnavailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Security audit unavailable." });

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
        CurrentUserContext ctx,
        int targetTenantId,
        string targetUserId,
        string reasonCode) =>
        new()
        {
            EventType = eventType,
            Outcome = outcome,
            ActorUserId = ctx.UserId.ToString(),
            ActorEmailHash = SecurityAuditEvent.HashRedacted(ctx.Email),
            TenantId = ctx.TenantId?.ToString() ?? string.Empty,
            TargetUserId = targetUserId,
            TargetTenantId = targetTenantId.ToString(),
            ReasonCode = reasonCode,
            CorrelationId = HttpContext.TraceIdentifier,
            RemoteIpHash = SecurityAuditEvent.HashRedacted(HttpContext.Connection.RemoteIpAddress?.ToString()),
            UserAgentHash = SecurityAuditEvent.HashRedacted(Request.Headers.UserAgent.ToString()),
            RequestPath = Request.Path.Value ?? string.Empty,
            Authenticated = User.Identity?.IsAuthenticated == true,
            Metadata = new Dictionary<string, string>
            {
                ["controller"] = nameof(TenantUsersController)
            }
        };
}

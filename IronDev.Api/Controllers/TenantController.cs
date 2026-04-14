using IronDev.Api.Auth;
using IronDev.Core.Auth;
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
    private readonly ILogger<TenantController> _logger;

    public TenantController(IUserService userService, IJwtTokenService jwtTokenService, ILogger<TenantController> logger)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
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
            _logger.LogWarning("User {UserId} attempted to select unassigned tenant {TenantId}",
                ctx.UserId, request.TenantId);
            return Forbid();
        }

        // Re-issue a full token that now carries tenant_id.
        var user = await _userService.GetByIdAsync(ctx.UserId, ct);
        if (user is null) return Unauthorized();

        var token = _jwtTokenService.CreateToken(user.Id, user.Email, user.DisplayName, request.TenantId);

        _logger.LogInformation("User {UserId} selected tenant {TenantId}", ctx.UserId, request.TenantId);
        return Ok(new LoginResponse(token, user.Id, user.DisplayName));
    }
}

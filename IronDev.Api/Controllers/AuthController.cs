using System.ComponentModel.DataAnnotations;
using IronDev.Api.Auth;
using IronDev.Core.Auth;
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
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUserService userService, IJwtTokenService jwtTokenService, ILogger<AuthController> logger)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
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
            _logger.LogWarning("Failed login attempt for {Email}", request.Email);
            return Unauthorized(new { error = "Invalid email or password." });
        }

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
    public IActionResult Logout() => Ok(new { message = "Logged out." });
}

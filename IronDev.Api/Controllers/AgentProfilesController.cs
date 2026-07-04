using IronDev.Api.Auth;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

/// <summary>
/// AG-1 — per-role agent profiles: read and edit the model and voice each agent
/// runs on. Reading is available to any tenant member; WRITING requires an
/// administering role (Owner or TenantAdmin) — changing which model the Critic
/// or Tester runs on, or its instruction wrapper, is an authority-adjacent
/// configuration change and must not be open to every signed-in user.
///
/// Boundary: a profile configures voice and model, never authority. This
/// endpoint edits provider/model/skill/personality files and nothing else — it
/// grants no capability, moves no gate, refuses a secret-looking update, and
/// never accepts an outbound BaseUrl.
/// </summary>
[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/v1/agent-profiles")]
public sealed class AgentProfilesController : ControllerBase
{
    private readonly ISkeletonAgentProfileService _profiles;
    private readonly IUserService _userService;

    public AgentProfilesController(ISkeletonAgentProfileService profiles, IUserService userService)
    {
        _profiles = profiles;
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SkeletonAgentProfile>>> List(CancellationToken ct) =>
        Ok(await _profiles.ListAsync(ct));

    [HttpGet("{role}")]
    public async Task<ActionResult<SkeletonAgentProfile>> Get(string role, CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: orchestrator, builder, tester, critic." });
        return Ok(await _profiles.GetAsync(parsed, ct));
    }

    [HttpPut("{role}")]
    public async Task<ActionResult<SkeletonAgentProfileOutcome>> Update(
        string role,
        [FromBody] SkeletonAgentProfileUpdate update,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: orchestrator, builder, tester, critic." });

        // Writing an agent's model/voice is an administering action — gate it the
        // same way tenant user administration is gated (Owner or TenantAdmin).
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        if (ctx.TenantId is null || ctx.UserId <= 0)
            return Forbid();
        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, ctx.TenantId.Value, ct);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return Forbid();

        var outcome = await _profiles.UpdateAsync(parsed, update, ct);
        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }

    private static bool TryParseRole(string role, out SkeletonAgentRole parsed) =>
        Enum.TryParse(role, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}

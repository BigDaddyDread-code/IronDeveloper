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

    [HttpGet("effective")]
    public async Task<ActionResult<IReadOnlyList<EffectiveSkeletonAgentProfile>>> ListEffective(
        [FromQuery] int? projectId,
        CancellationToken ct)
    {
        var ctx = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        if (ctx.TenantId is null || ctx.UserId <= 0)
            return Forbid();

        return Ok(await _profiles.ListEffectiveAsync(ctx.TenantId.Value, projectId, ct));
    }

    [HttpGet("{role}")]
    public async Task<ActionResult<SkeletonAgentProfile>> Get(string role, CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        return Ok(await _profiles.GetAsync(parsed, ct));
    }

    [HttpGet("{role}/draft")]
    public async Task<ActionResult<SkeletonAgentProfileDraft>> GetDraft(string role, CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        return Ok(await _profiles.GetDraftAsync(parsed, ct));
    }

    [HttpGet("{role}/history")]
    public async Task<ActionResult<IReadOnlyList<SkeletonAgentProfilePublishedVersion>>> History(string role, CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        return Ok(await _profiles.ListHistoryAsync(parsed, ct));
    }

    [HttpPut("{role}/draft")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> SaveDraft(
        string role,
        [FromBody] SkeletonAgentProfileDraftWriteRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        if (!await CanAdministerAsync(ct))
            return Forbid();

        var outcome = await _profiles.SaveDraftAsync(parsed, request, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
    }

    [HttpPost("{role}/draft/test")]
    public async Task<ActionResult<SkeletonAgentProfileDraftTestOutcome>> TestDraft(string role, CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        if (!await CanAdministerAsync(ct))
            return Forbid();

        var outcome = await _profiles.TestDraftAsync(parsed, ct);
        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }

    [HttpPost("{role}/draft/publish")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> PublishDraft(
        string role,
        [FromBody] SkeletonAgentProfilePublishRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var ctx = CurrentUser();
        if (!await CanAdministerAsync(ct))
            return Forbid();

        var outcome = await _profiles.PublishDraftAsync(parsed, request, ctx.UserId, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
    }

    [HttpPut("{role}")]
    public async Task<ActionResult<SkeletonAgentProfileOutcome>> Update(
        string role,
        [FromBody] SkeletonAgentProfileUpdate update,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });

        // Writing an agent's model/voice is an administering action — gate it the
        // same way tenant user administration is gated (Owner or TenantAdmin).
        var ctx = CurrentUser();
        if (ctx.TenantId is null || ctx.UserId <= 0)
            return Forbid();
        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, ctx.TenantId.Value, ct);
        if (!TenantUserRoles.CanAdministerUsers(callerRole))
            return Forbid();

        var outcome = await _profiles.UpdateAsync(parsed, update, ct);
        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }

    private CurrentUserContext CurrentUser() =>
        new(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private async Task<bool> CanAdministerAsync(CancellationToken ct)
    {
        var ctx = CurrentUser();
        if (ctx.TenantId is null || ctx.UserId <= 0)
            return false;
        var callerRole = await _userService.GetTenantRoleAsync(ctx.UserId, ctx.TenantId.Value, ct);
        return TenantUserRoles.CanAdministerUsers(callerRole);
    }

    private static bool TryParseRole(string role, out SkeletonAgentRole parsed) =>
        Enum.TryParse(role, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}

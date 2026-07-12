using IronDev.Api.Auth;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
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
    private readonly IRunEventStore _runEvents;
    private readonly IProjectMembershipService _projectMemberships;

    public AgentProfilesController(
        ISkeletonAgentProfileService profiles,
        IUserService userService,
        IRunEventStore runEvents,
        IProjectMembershipService projectMemberships)
    {
        _profiles = profiles;
        _userService = userService;
        _runEvents = runEvents;
        _projectMemberships = projectMemberships;
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
    public async Task<ActionResult<IReadOnlyList<SkeletonAgentProfileHistoryView>>> History(
        string role,
        [FromQuery] int? projectId,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });

        var ctx = CurrentUser();
        if (ctx.TenantId is null || ctx.UserId <= 0)
            return Forbid();
        if (projectId is > 0 && !await _projectMemberships.HasAccessAsync(ctx.TenantId.Value, projectId.Value, ctx.UserId, ct))
            return Forbid();

        var history = await _profiles.ListHistoryAsync(parsed, ct);
        var usage = projectId is > 0
            ? await ReadUsageAsync(parsed, projectId.Value, ct)
            : new Dictionary<long, IReadOnlyList<SkeletonAgentProfileRunUsage>>();
        return Ok(history.Select(version => new SkeletonAgentProfileHistoryView
        {
            Version = version,
            RunUsage = usage.GetValueOrDefault(version.Version, [])
        }).ToArray());
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

    [HttpPost("{role}/reset")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> Reset(
        string role,
        [FromBody] SkeletonAgentProfileResetRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var ctx = CurrentUser();
        if (!await CanAdministerAsync(ct))
            return Forbid();

        var outcome = await _profiles.ResetAsync(parsed, request, ctx.UserId, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
    }

    [HttpPost("{role}/history/{version:long}/restore")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> Restore(
        string role,
        long version,
        [FromBody] SkeletonAgentProfileRestoreRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var ctx = CurrentUser();
        if (!await CanAdministerAsync(ct))
            return Forbid();

        var outcome = await _profiles.RestoreAsync(parsed, version, request, ctx.UserId, ct);
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

    private async Task<Dictionary<long, IReadOnlyList<SkeletonAgentProfileRunUsage>>> ReadUsageAsync(
        SkeletonAgentRole role,
        int projectId,
        CancellationToken cancellationToken)
    {
        var usage = new Dictionary<long, List<SkeletonAgentProfileRunUsage>>();
        foreach (var runId in await _runEvents.GetRecentRunIdsAsync(50, cancellationToken))
        {
            var snapshot = (await _runEvents.GetEventsAsync(runId, cancellationToken))
                .FirstOrDefault(runEvent =>
                    runEvent.EventType == "AgentConfigurationSnapshotted" &&
                    string.Equals(Payload(runEvent, "role"), role.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(Payload(runEvent, "projectId"), out var capturedProjectId) && capturedProjectId == projectId &&
                    long.TryParse(Payload(runEvent, "profileVersion"), out _));
            if (snapshot is null || !long.TryParse(Payload(snapshot, "profileVersion"), out var version))
                continue;

            if (!usage.TryGetValue(version, out var items))
                usage[version] = items = [];
            items.Add(new SkeletonAgentProfileRunUsage
            {
                RunId = runId,
                ProjectId = projectId,
                WorkItemId = long.TryParse(Payload(snapshot, "workItemId"), out var workItemId) ? workItemId : 0,
                CapturedAtUtc = DateTimeOffset.TryParse(Payload(snapshot, "createdUtc"), out var capturedAt) ? capturedAt : snapshot.TimestampUtc
            });
        }

        return usage.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SkeletonAgentProfileRunUsage>)pair.Value);
    }

    private static string Payload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) ? value : string.Empty;

    private static bool TryParseRole(string role, out SkeletonAgentRole parsed) =>
        Enum.TryParse(role, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}

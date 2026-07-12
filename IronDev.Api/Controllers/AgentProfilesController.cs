using IronDev.Api.Auth;
using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
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
    private readonly IAiConnectionCatalogService _connections;
    private readonly IAgentConfigurationPackService _configurationPacks;

    public AgentProfilesController(
        ISkeletonAgentProfileService profiles,
        IUserService userService,
        IRunEventStore runEvents,
        IProjectMembershipService projectMemberships,
        IAiConnectionCatalogService connections,
        IAgentConfigurationPackService configurationPacks)
    {
        _profiles = profiles;
        _userService = userService;
        _runEvents = runEvents;
        _projectMemberships = projectMemberships;
        _connections = connections;
        _configurationPacks = configurationPacks;
    }

    [HttpGet("configuration-pack")]
    public async Task<ActionResult<AgentConfigurationPack>> ExportConfigurationPack(
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null)
            return Forbid();
        var ctx = CurrentUser();
        return Ok(await _configurationPacks.ExportAsync(profileScope.TenantId, ctx.UserId, profileScope, ct));
    }

    [HttpPost("configuration-pack/preview")]
    public async Task<ActionResult<AgentConfigurationPackPreview>> PreviewConfigurationPack(
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        [FromBody] AgentConfigurationPackPreviewRequest request,
        CancellationToken ct)
    {
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();
        var ctx = CurrentUser();
        var preview = await _configurationPacks.PreviewAsync(profileScope.TenantId, ctx.UserId, profileScope, request.Pack, ct);
        return preview.Succeeded ? Ok(preview) : BadRequest(preview);
    }

    [HttpPost("configuration-pack/import")]
    public async Task<ActionResult<AgentConfigurationPackImportOutcome>> ImportConfigurationPack(
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        [FromBody] AgentConfigurationPackImportRequest request,
        CancellationToken ct)
    {
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();
        var ctx = CurrentUser();
        var outcome = await _configurationPacks.ImportAsync(profileScope.TenantId, ctx.UserId, profileScope, request, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
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
    public async Task<ActionResult<SkeletonAgentProfileDraft>> GetDraft(
        string role,
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        return profileScope is null ? Forbid() : Ok(await _profiles.GetDraftAsync(parsed, profileScope, ct));
    }

    [HttpGet("{role}/history")]
    public async Task<ActionResult<IReadOnlyList<SkeletonAgentProfileHistoryView>>> History(
        string role,
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });

        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null)
            return Forbid();

        var history = await _profiles.ListHistoryAsync(parsed, profileScope, ct);
        var usage = projectId is > 0
            ? await ReadUsageAsync(parsed, profileScope.Layer, projectId.Value, ct)
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
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        [FromBody] SkeletonAgentProfileDraftWriteRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();
        if (!await ConnectionAvailableAsync(profileScope.TenantId, request.AiConnectionId, ct))
            return BadRequest(new { code = "AiConnectionUnavailable", error = "The selected AI connection is not enabled and available to this tenant/project." });

        var outcome = await _profiles.SaveDraftAsync(parsed, profileScope, request, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
    }

    [HttpPost("{role}/draft/test")]
    public async Task<ActionResult<SkeletonAgentProfileDraftTestOutcome>> TestDraft(
        string role,
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();

        var draft = await _profiles.GetDraftAsync(parsed, profileScope, ct);
        if (!await ConnectionAvailableAsync(profileScope.TenantId, draft.Values.AiConnectionId, ct))
            return BadRequest(new { code = "AiConnectionUnavailable", error = "The draft's AI connection is no longer available." });

        var outcome = await _profiles.TestDraftAsync(parsed, profileScope, ct);
        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }

    [HttpPost("{role}/draft/publish")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> PublishDraft(
        string role,
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        [FromBody] SkeletonAgentProfilePublishRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var ctx = CurrentUser();
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();

        var draft = await _profiles.GetDraftAsync(parsed, profileScope, ct);
        if (!await ConnectionAvailableAsync(profileScope.TenantId, draft.Values.AiConnectionId, ct))
            return BadRequest(new { code = "AiConnectionUnavailable", error = "The draft's AI connection is no longer available." });

        var outcome = await _profiles.PublishDraftAsync(parsed, profileScope, request, ctx.UserId, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
    }

    [HttpPost("{role}/reset")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> Reset(
        string role,
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        [FromBody] SkeletonAgentProfileResetRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var ctx = CurrentUser();
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();

        var outcome = await _profiles.ResetAsync(parsed, profileScope, request, ctx.UserId, ct);
        return outcome.Succeeded ? Ok(outcome) : Conflict(outcome);
    }

    [HttpPost("{role}/history/{version:long}/restore")]
    public async Task<ActionResult<SkeletonAgentProfileDraftOutcome>> Restore(
        string role,
        long version,
        [FromQuery] int? projectId,
        [FromQuery] string? scope,
        [FromBody] SkeletonAgentProfileRestoreRequest request,
        CancellationToken ct)
    {
        if (!TryParseRole(role, out var parsed))
            return BadRequest(new { error = "Unknown agent role. Roles: analyst, builder, tester, critic, orchestrator." });
        var ctx = CurrentUser();
        var profileScope = await ResolveScopeAsync(projectId, scope, ct);
        if (profileScope is null || !await CanAdministerScopeAsync(profileScope, ct))
            return Forbid();

        var outcome = await _profiles.RestoreAsync(parsed, profileScope, version, request, ctx.UserId, ct);
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

        return Conflict(new
        {
            code = "LegacyWriteDisabled",
            error = "Immediate global profile writes are disabled. Save and publish a tenant default or project override through the versioned draft endpoints."
        });
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

    private async Task<SkeletonAgentProfileScope?> ResolveScopeAsync(int? projectId, string? requestedScope, CancellationToken cancellationToken)
    {
        var ctx = CurrentUser();
        if (ctx.TenantId is null || ctx.UserId <= 0)
            return null;
        var layer = string.IsNullOrWhiteSpace(requestedScope)
            ? projectId is > 0 ? "project" : "tenant"
            : requestedScope.Trim().ToLowerInvariant();
        if (layer == "tenant")
            return new SkeletonAgentProfileScope { TenantId = ctx.TenantId.Value };
        if (layer != "project" || projectId is not > 0)
            return null;
        return await _projectMemberships.HasAccessAsync(ctx.TenantId.Value, projectId.Value, ctx.UserId, cancellationToken)
            ? new SkeletonAgentProfileScope { TenantId = ctx.TenantId.Value, ProjectId = projectId.Value }
            : null;
    }

    private async Task<bool> CanAdministerScopeAsync(SkeletonAgentProfileScope scope, CancellationToken cancellationToken)
    {
        if (scope.ProjectId is not > 0)
            return await CanAdministerAsync(cancellationToken);
        var ctx = CurrentUser();
        var members = await _projectMemberships.GetMembersAsync(scope.TenantId, scope.ProjectId.Value, ctx.UserId, cancellationToken);
        return members.Any(member => member.UserId == ctx.UserId && member.ProjectRole == ProjectMemberRoles.Owner);
    }

    private async Task<bool> ConnectionAvailableAsync(int tenantId, string connectionId, CancellationToken cancellationToken)
    {
        var connections = await _connections.ListAsync(tenantId, CurrentUser().UserId, cancellationToken);
        return connections.Any(connection =>
            string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase) &&
            connection.Enabled && connection.TenantAvailable && connection.ProjectAvailable);
    }

    private async Task<Dictionary<long, IReadOnlyList<SkeletonAgentProfileRunUsage>>> ReadUsageAsync(
        SkeletonAgentRole role,
        string scopeLayer,
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
                    string.Equals(Payload(runEvent, "profileScopeLayer"), scopeLayer, StringComparison.OrdinalIgnoreCase) &&
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

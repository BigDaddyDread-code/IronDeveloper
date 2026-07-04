using IronDev.Core.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

/// <summary>
/// AG-1 — per-role agent profiles: read and edit the model and voice each agent
/// runs on. GET lists or reads; PUT saves the voice/model surface.
///
/// Boundary: a profile configures voice and model, never authority. This
/// endpoint edits provider/model/skill/personality files and nothing else — it
/// grants no capability, moves no gate, and refuses anything that looks like a
/// secret (keys stay in environment/provider config).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/agent-profiles")]
public sealed class AgentProfilesController : ControllerBase
{
    private readonly ISkeletonAgentProfileService _profiles;

    public AgentProfilesController(ISkeletonAgentProfileService profiles)
    {
        _profiles = profiles;
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

        var outcome = await _profiles.UpdateAsync(parsed, update, ct);
        return outcome.Succeeded ? Ok(outcome) : BadRequest(outcome);
    }

    private static bool TryParseRole(string role, out SkeletonAgentRole parsed) =>
        Enum.TryParse(role, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}

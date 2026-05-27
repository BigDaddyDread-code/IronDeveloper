using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/decisions")]
public sealed class DecisionsController : ControllerBase
{
    private readonly IProjectMemoryService _memory;

    public DecisionsController(IProjectMemoryService memory)
    {
        _memory = memory;
    }

    [HttpGet]
    public Task<IReadOnlyList<ProjectDecision>> GetDecisions(
        int projectId,
        [FromQuery] int take = 50,
        CancellationToken ct = default) =>
        _memory.GetRecentDecisionsAsync(projectId, take, ct);

    [HttpGet("{decisionId:long}")]
    public async Task<ActionResult<ProjectDecision>> GetDecision(int projectId, long decisionId, CancellationToken ct)
    {
        var decision = await _memory.GetDecisionByIdAsync(decisionId, ct);
        if (decision is null || decision.ProjectId != projectId)
            return NotFound();

        return Ok(decision);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDecision>> CreateDecision(
        int projectId,
        ProjectDecision decision,
        CancellationToken ct)
    {
        decision.Id = 0;
        decision.ProjectId = projectId;
        decision.Id = await _memory.SaveDecisionAsync(decision, ct);
        var saved = await _memory.GetDecisionByIdAsync(decision.Id, ct);
        return Ok(saved ?? decision);
    }

    [HttpPatch("{decisionId:long}")]
    public async Task<ActionResult<ProjectDecision>> UpdateDecision(
        int projectId,
        long decisionId,
        ProjectDecision decision,
        CancellationToken ct)
    {
        var existing = await _memory.GetDecisionByIdAsync(decisionId, ct);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound();

        decision.Id = decisionId;
        decision.ProjectId = projectId;
        await _memory.SaveDecisionAsync(decision, ct);
        var saved = await _memory.GetDecisionByIdAsync(decisionId, ct);
        return Ok(saved ?? decision);
    }

    [HttpPost("{decisionId:long}/supersede")]
    public async Task<ActionResult<ProjectDecision>> SupersedeDecision(
        int projectId,
        long decisionId,
        SupersedeDecisionRequest request,
        CancellationToken ct)
    {
        var existing = await _memory.GetDecisionByIdAsync(decisionId, ct);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound();

        existing.Status = "Superseded";
        await _memory.SaveDecisionAsync(existing, ct);

        var replacement = request.Replacement;
        replacement.Id = 0;
        replacement.ProjectId = projectId;
        if (string.IsNullOrWhiteSpace(replacement.Status))
            replacement.Status = "Accepted";
        replacement.Reason = string.IsNullOrWhiteSpace(replacement.Reason)
            ? $"Supersedes decision {decisionId}."
            : $"{replacement.Reason.Trim()}\n\nSupersedes decision {decisionId}.";

        replacement.Id = await _memory.SaveDecisionAsync(replacement, ct);
        var saved = await _memory.GetDecisionByIdAsync(replacement.Id, ct);
        return Ok(saved ?? replacement);
    }

    [HttpPost("{decisionId:long}/archive")]
    public async Task<IActionResult> ArchiveDecision(int projectId, long decisionId, CancellationToken ct)
    {
        var decision = await _memory.GetDecisionByIdAsync(decisionId, ct);
        if (decision is null || decision.ProjectId != projectId)
            return NotFound();

        decision.Status = "Archived";
        await _memory.SaveDecisionAsync(decision, ct);
        return NoContent();
    }
}

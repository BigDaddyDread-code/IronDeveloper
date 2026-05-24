using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MemoryController : ControllerBase
{
    private readonly IProjectMemoryService _memory;

    public MemoryController(IProjectMemoryService memory)
    {
        _memory = memory;
    }

    [HttpGet("api/projects/{projectId:int}/memory/summary")]
    public Task<ProjectSummary?> GetSummary(int projectId, CancellationToken ct) =>
        _memory.GetLatestSummaryAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/summary")]
    public Task<long> SaveSummary(ProjectSummary summary, CancellationToken ct) =>
        _memory.SaveSummaryAsync(summary, ct);

    [HttpGet("api/projects/{projectId:int}/memory/decisions")]
    public Task<IReadOnlyList<ProjectDecision>> GetDecisions(int projectId, [FromQuery] int take = 10, CancellationToken ct = default) =>
        _memory.GetRecentDecisionsAsync(projectId, take, ct);

    [HttpPost("api/projects/{projectId:int}/memory/decisions")]
    public Task<long> SaveDecision(ProjectDecision decision, CancellationToken ct) =>
        _memory.SaveDecisionAsync(decision, ct);

    [HttpGet("api/projects/{projectId:int}/memory/documents")]
    public Task<IReadOnlyList<ProjectContextDocument>> GetDocuments(
        int projectId,
        [FromQuery] string? documentType,
        [FromQuery] string? status,
        [FromQuery] string? searchText,
        [FromQuery] int take,
        CancellationToken ct) =>
        _memory.GetContextDocumentsAsync(projectId, documentType, null, status, take, ct);

    [HttpGet("api/projects/{projectId:int}/memory/search")]
    public Task<IReadOnlyList<ProjectContextDocument>> Search(int projectId, [FromQuery] string q, [FromQuery] int take = 20, CancellationToken ct = default) =>
        _memory.GetRelevantContextDocumentsAsync(projectId, q, take, ct);

    [HttpGet("api/memory/documents/{documentId:long}")]
    public Task<ProjectContextDocument?> GetDocument(long documentId, CancellationToken ct) =>
        _memory.GetContextDocumentByIdAsync(documentId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/documents")]
    public Task<long> SaveDocument(ProjectContextDocument document, CancellationToken ct) =>
        _memory.SaveContextDocumentAsync(document, ct);

    [HttpDelete("api/memory/documents/{documentId:long}")]
    public async Task<IActionResult> ArchiveDocument(long documentId, CancellationToken ct)
    {
        var ok = await _memory.ArchiveContextDocumentAsync(documentId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("api/projects/{projectId:int}/memory/plans")]
    public Task<IReadOnlyList<ProjectImplementationPlan>> GetPlans(int projectId, [FromQuery] int take = 10, CancellationToken ct = default) =>
        _memory.GetRecentPlansAsync(projectId, take, ct);

    [HttpGet("api/memory/plans/{planId:long}")]
    public Task<ProjectImplementationPlan?> GetPlan(long planId, CancellationToken ct) =>
        _memory.GetPlanByIdAsync(planId, ct);

    [HttpGet("api/tickets/{ticketId:long}/implementation-plan")]
    public Task<ProjectImplementationPlan?> GetPlanByTicket(long ticketId, CancellationToken ct) =>
        _memory.GetPlanByTicketIdAsync(ticketId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/plans")]
    public Task<long> SavePlan(ProjectImplementationPlan plan, CancellationToken ct) =>
        _memory.SavePlanAsync(plan, ct);

    [HttpGet("api/projects/{projectId:int}/memory/rules")]
    public Task<IReadOnlyList<ProjectRule>> GetRules(int projectId, CancellationToken ct) =>
        _memory.GetProjectRulesAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/rules")]
    public Task<long> SaveRule(ProjectRule rule, CancellationToken ct) =>
        _memory.SaveProjectRuleAsync(rule, ct);
}

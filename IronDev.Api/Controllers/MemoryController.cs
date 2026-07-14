using IronDev.Core.KnowledgeCompiler;
using IronDev.Api.Auth;
using IronDev.Api.Middleware;
using IronDev.Core.Interfaces;
using IronDev.Core.Governance;
using IronDev.Core.Models;
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
    private readonly ISemanticMemoryService _semanticMemory;
    private readonly IUserService _users;

    public MemoryController(IProjectMemoryService memory, ISemanticMemoryService semanticMemory, IUserService users)
    {
        _memory = memory;
        _semanticMemory = semanticMemory;
        _users = users;
    }

    [HttpGet("api/projects/{projectId:int}/memory/summary")]
    public Task<ProjectSummary?> GetSummary(int projectId, CancellationToken ct) =>
        _memory.GetLatestSummaryAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/summary")]
    public async Task<ActionResult<long>> SaveSummary(int projectId, ProjectSummary summary, CancellationToken ct)
    {
        var scopeError = BindRouteScope(projectId, summary.ProjectId);
        if (scopeError is not null) return scopeError;
        summary.ProjectId = projectId;
        summary.TenantId = CurrentUser().TenantId!.Value;
        return Ok(await _memory.SaveSummaryAsync(summary, ct));
    }

    [HttpGet("api/projects/{projectId:int}/memory/decisions")]
    public Task<IReadOnlyList<ProjectDecision>> GetDecisions(int projectId, [FromQuery] int take = 10, CancellationToken ct = default) =>
        _memory.GetRecentDecisionsAsync(projectId, take, ct);

    [HttpPost("api/projects/{projectId:int}/memory/decisions")]
    public ActionResult<long> SaveDecision(int projectId, ProjectDecision decision, CancellationToken ct) =>
        Conflict(Refusal(
            "GovernedPromotionRequired",
            "Direct Project Canon decision writes are disabled. Create a memory proposal and promote it through governed review.",
            forbiddenActions: ["Write a Project Canon decision without governed promotion evidence."]));

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

    [HttpPost("api/projects/{projectId:int}/memory/search")]
    public async Task<ActionResult<MemorySearchResponseDto>> SearchMemory(
        int projectId,
        MemorySearchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Search query is required." });

        var traceId = Guid.NewGuid();
        var results = await _semanticMemory.SearchAsync(new SemanticSearchQuery
        {
            ProjectId = projectId,
            QueryText = request.Query,
            Limit = request.Take <= 0 ? 20 : request.Take,
            IncludeStale = request.IncludeStale,
            Consumer = "AlphaCockpitApi"
        }, ct);

        return Ok(new MemorySearchResponseDto
        {
            ProjectId = projectId,
            Query = request.Query,
            TraceId = traceId,
            Results = results
                .Select((result, index) => new MemorySearchResultDto
                {
                    ResultId = result.ChunkId == Guid.Empty ? result.Document.Id.ToString() : result.ChunkId.ToString("D"),
                    SourceType = string.IsNullOrWhiteSpace(result.SourceEntityType)
                        ? result.ArtefactType
                        : result.SourceEntityType,
                    SourceId = string.IsNullOrWhiteSpace(result.SourceEntityId)
                        ? result.Document.Id.ToString()
                        : result.SourceEntityId,
                    Title = string.IsNullOrWhiteSpace(result.Title) ? result.Document.Title : result.Title,
                    Excerpt = result.Snippet,
                    Score = result.FinalScore,
                    AuthorityScore = result.AuthorityBoost,
                    RawVectorRank = index + 1,
                    FinalRank = index + 1,
                    MatchReason = result.MatchReason,
                    TraceId = traceId
                })
                .ToArray()
        });
    }

    [HttpGet("api/projects/{projectId:int}/memory/status")]
    public async Task<MemoryStatusDto> GetMemoryStatus(int projectId, CancellationToken ct)
    {
        var health = await _semanticMemory.GetHealthAsync(projectId, ct);
        return new MemoryStatusDto
        {
            ProjectId = health.ProjectId,
            ProviderName = health.ProviderName,
            ProviderStatus = health.ProviderStatus,
            DocumentCount = health.DocumentCount,
            EmbeddedCount = health.EmbeddedCount,
            StaleEmbeddingCount = health.StaleEmbeddingCount,
            LastEmbeddedAtUtc = health.LastEmbeddedAtUtc,
            LastRebuildAtUtc = health.LastRebuildAtUtc
        };
    }

    [HttpPost("api/projects/{projectId:int}/memory/reindex")]
    public async Task<ActionResult<MemoryReindexResponseDto>> ReindexMemory(int projectId, CancellationToken ct)
    {
        if (!await CanMaintainMemoryAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, MaintenanceRefusal());
        await _semanticMemory.RebuildProjectAsync(projectId, ct);
        return Ok(new MemoryReindexResponseDto
        {
            ProjectId = projectId,
            Status = "completed",
            Message = "Project memory reindex completed."
        });
    }

    [HttpGet("api/memory/documents/{documentId:long}")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.MemoryDocument, "documentId")]
    public Task<ProjectContextDocument?> GetDocument(long documentId, CancellationToken ct) =>
        _memory.GetContextDocumentByIdAsync(documentId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/documents")]
    public async Task<ActionResult<long>> SaveDocument(int projectId, ProjectContextDocument document, CancellationToken ct)
    {
        var scopeError = BindRouteScope(projectId, document.ProjectId);
        if (scopeError is not null) return scopeError;

        document.ProjectId = projectId;
        document.TenantId = CurrentUser().TenantId!.Value;
        document.AuthorityLevel = "ObservedFact";
        document.Status = "Active";
        document.SupersedesDocumentId = null;
        return Ok(await _memory.SaveContextDocumentAsync(document, ct));
    }

    [HttpDelete("api/memory/documents/{documentId:long}")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.MemoryDocument, "documentId")]
    public async Task<IActionResult> ArchiveDocument(long documentId, CancellationToken ct)
    {
        if (!await CanMaintainMemoryAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, MaintenanceRefusal());
        var ok = await _memory.ArchiveContextDocumentAsync(documentId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("api/projects/{projectId:int}/memory/plans")]
    public Task<IReadOnlyList<ProjectImplementationPlan>> GetPlans(int projectId, [FromQuery] int take = 10, CancellationToken ct = default) =>
        _memory.GetRecentPlansAsync(projectId, take, ct);

    [HttpGet("api/memory/plans/{planId:long}")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.ImplementationPlan, "planId")]
    public Task<ProjectImplementationPlan?> GetPlan(long planId, CancellationToken ct) =>
        _memory.GetPlanByIdAsync(planId, ct);

    [HttpGet("api/tickets/{ticketId:long}/implementation-plan")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.Ticket, "ticketId")]
    public Task<ProjectImplementationPlan?> GetPlanByTicket(long ticketId, CancellationToken ct) =>
        _memory.GetPlanByTicketIdAsync(ticketId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/plans")]
    public async Task<ActionResult<long>> SavePlan(int projectId, ProjectImplementationPlan plan, CancellationToken ct)
    {
        var scopeError = BindRouteScope(projectId, plan.ProjectId);
        if (scopeError is not null) return scopeError;
        plan.ProjectId = projectId;
        plan.TenantId = CurrentUser().TenantId!.Value;
        return Ok(await _memory.SavePlanAsync(plan, ct));
    }

    [HttpGet("api/projects/{projectId:int}/memory/rules")]
    public Task<IReadOnlyList<ProjectRule>> GetRules(int projectId, CancellationToken ct) =>
        _memory.GetProjectRulesAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/memory/rules")]
    public ActionResult<long> SaveRule(int projectId, ProjectRule rule, CancellationToken ct) =>
        Conflict(Refusal(
            "GovernedPromotionRequired",
            "Direct Project Canon rule writes are disabled. Create a memory proposal and promote it through governed review.",
            forbiddenActions: ["Write an enforced Project Canon rule without governed promotion evidence."]));

    private ActionResult<long>? BindRouteScope(int routeProjectId, int bodyProjectId)
    {
        var current = CurrentUser();
        if (current.TenantId is null || current.UserId <= 0)
            return StatusCode(StatusCodes.Status403Forbidden, Refusal(
                "AuthenticatedTenantRequired",
                "An authenticated user with a selected tenant is required."));
        if (bodyProjectId > 0 && bodyProjectId != routeProjectId)
            return BadRequest(Refusal(
                "ProjectScopeMismatch",
                "The route project is authoritative.",
                blockedReasons: ["The body project does not match the route project."],
                forbiddenActions: ["Write memory across project scope."]));
        return null;
    }

    private async Task<bool> CanMaintainMemoryAsync(CancellationToken ct)
    {
        var current = CurrentUser();
        if (current.TenantId is null || current.UserId <= 0)
            return false;
        var role = await _users.GetTenantRoleAsync(current.UserId, current.TenantId.Value, ct);
        return ProjectMemoryCapabilities.CanMaintainProjectMemory(role);
    }

    private GovernedRefusalEnvelope MaintenanceRefusal() => Refusal(
        "ProjectMemoryMaintenanceCapabilityRequired",
        "This operation requires the project-memory maintenance capability.",
        missingEvidence: [ProjectMemoryCapabilities.MaintainProjectMemory],
        nextSafeActions: ["Ask a tenant Owner or TenantAdmin to perform the maintenance operation."],
        forbiddenActions: ["Archive Project Canon context", "Rebuild the derived memory index"]);

    private GovernedRefusalEnvelope Refusal(
        string reasonCode,
        string message,
        IEnumerable<string>? blockedReasons = null,
        IEnumerable<string>? missingEvidence = null,
        IEnumerable<string>? nextSafeActions = null,
        IEnumerable<string>? forbiddenActions = null) =>
        GovernedRefusal.Create(
            reasonCode,
            message,
            HttpContext.Items[RequestTracingMiddleware.CorrelationHeaderName]?.ToString() ?? HttpContext.TraceIdentifier,
            blockedReasons,
            missingEvidence,
            nextSafeActions,
            forbiddenActions);

    private CurrentUserContext CurrentUser() =>
        new(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
}

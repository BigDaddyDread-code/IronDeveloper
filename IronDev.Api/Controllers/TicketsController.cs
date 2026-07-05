using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly ITicketService _tickets;
    private readonly IDraftTicketService _drafts;
    private readonly ICodebaseTicketGeneratorService _generator;
    private readonly ITicketBuildOrchestrator _orchestrator;
    private readonly ITicketBuildRunService _buildRuns;
    private readonly ITicketSkeletonRunService _skeletonRuns;
    private readonly ISkeletonCriticReviewService _criticReviews;
    private readonly ISkeletonFindingDispositionService _findingDispositions;
    private readonly ISkeletonBatchMapService _batchMaps;
    private readonly ISkeletonBatchPlanService _batchPlans;
    private readonly ISkeletonBatchRunService _skeletonBatchRuns;
    private readonly ISkeletonGateRecommendationService _gateRecommendations;
    private readonly IBuilderReadinessService _readiness;
    private readonly ITicketEvidenceSummaryService _evidenceSummary;
    private readonly ITicketRunReviewService _runReview;
    private readonly IBuilderProposalService _proposals;
    private readonly IChatHistoryService _chatHistory;

    public TicketsController(
        ITicketService tickets,
        IDraftTicketService drafts,
        ICodebaseTicketGeneratorService generator,
        ITicketBuildOrchestrator orchestrator,
        ITicketBuildRunService buildRuns,
        ITicketSkeletonRunService skeletonRuns,
        ISkeletonCriticReviewService criticReviews,
        ISkeletonFindingDispositionService findingDispositions,
        ISkeletonBatchMapService batchMaps,
        ISkeletonBatchPlanService batchPlans,
        ISkeletonBatchRunService skeletonBatchRuns,
        ISkeletonGateRecommendationService gateRecommendations,
        IBuilderReadinessService readiness,
        ITicketEvidenceSummaryService evidenceSummary,
        ITicketRunReviewService runReview,
        IBuilderProposalService proposals,
        IChatHistoryService chatHistory)
    {
        _tickets = tickets;
        _drafts = drafts;
        _generator = generator;
        _orchestrator = orchestrator;
        _buildRuns = buildRuns;
        _skeletonRuns = skeletonRuns;
        _criticReviews = criticReviews;
        _findingDispositions = findingDispositions;
        _batchMaps = batchMaps;
        _batchPlans = batchPlans;
        _skeletonBatchRuns = skeletonBatchRuns;
        _gateRecommendations = gateRecommendations;
        _readiness = readiness;
        _evidenceSummary = evidenceSummary;
        _runReview = runReview;
        _proposals = proposals;
        _chatHistory = chatHistory;
    }

    [HttpGet("api/projects/{projectId:int}/tickets")]
    public async Task<IReadOnlyList<ProjectTicket>> GetTickets(int projectId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        await _tickets.GetRecentTicketsAsync(projectId, take, ct);

    [HttpGet("api/tickets/{ticketId:long}")]
    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}")]
    public async Task<ActionResult<ProjectTicket>> GetTicket(long ticketId, CancellationToken ct, int? projectId = null)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, ct);
        if (ticket is not null && projectId.HasValue && ticket.ProjectId != projectId.Value)
            return NotFound();

        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("api/projects/{projectId:int}/tickets")]
    public async Task<ActionResult<ProjectTicket>> CreateTicket(int projectId, CreateProjectTicketRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Ticket title is required." });

        var ticket = MapCreateRequest(projectId, request);
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpPost("api/projects/{projectId:int}/tickets/legacy")]
    public async Task<ActionResult<ProjectTicket>> SaveLegacyTicket(int projectId, ProjectTicket ticket, CancellationToken ct)
    {
        ticket.ProjectId = projectId;
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpPatch("api/projects/{projectId:int}/tickets/{ticketId:long}")]
    public async Task<ActionResult<ProjectTicket>> UpdateTicket(int projectId, long ticketId, ProjectTicket ticket, CancellationToken ct)
    {
        var existing = await _tickets.GetTicketByIdAsync(ticketId, ct);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound();

        ticket.Id = ticketId;
        ticket.ProjectId = projectId;
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpPost("api/projects/{projectId:int}/tickets/import-external")]
    public async Task<ActionResult<ProjectTicket>> ImportExternalTicket(int projectId, ImportExternalTicketRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Ticket title is required." });

        var create = new CreateProjectTicketRequest
        {
            Title = request.Title,
            Type = request.Type,
            Priority = request.Priority,
            Summary = request.Summary,
            Problem = request.Problem,
            ProposedChange = request.ProposedChange,
            AcceptanceCriteria = request.AcceptanceCriteria,
            ExternalReferences = [request.ExternalReference],
            Provenance = request.Provenance ?? new TicketProvenanceDto
            {
                Source = $"{request.ExternalReference.Provider}:{request.ExternalReference.Kind}",
                Notes = "Imported from external tracker."
            }
        };

        var ticket = MapCreateRequest(projectId, create);
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpPost("api/projects/{projectId:int}/tickets/generate-from-discussion")]
    public async Task<ActionResult<ProjectTicket>> GenerateFromDiscussion(int projectId, GenerateTicketFromDiscussionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Discussion))
            return BadRequest(new { error = "Discussion is required." });

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? DeriveTitle(request.Discussion)
            : request.Title;

        var create = new CreateProjectTicketRequest
        {
            Title = title,
            Type = request.Type ?? "Discussion",
            Priority = request.Priority ?? "Medium",
            Summary = request.Discussion,
            Problem = "Generated from discussion text.",
            ProposedChange = "Review the discussion and turn it into implementation work.",
            AcceptanceCriteria = ["Discussion has been captured as an IronDev ticket."],
            Provenance = request.Provenance ?? new TicketProvenanceDto
            {
                Source = "discussion",
                Notes = "Generated through IronDev.Api from discussion text."
            }
        };

        var ticket = MapCreateRequest(projectId, create);
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpDelete("api/tickets/{ticketId:long}")]
    public async Task<IActionResult> ArchiveTicket(long ticketId, CancellationToken ct)
    {
        var ok = await _tickets.ArchiveTicketAsync(ticketId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/archive")]
    public async Task<IActionResult> ArchiveProjectTicket(int projectId, long ticketId, CancellationToken ct)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, ct);
        if (ticket is null || ticket.ProjectId != projectId)
            return NotFound();

        var ok = await _tickets.ArchiveTicketAsync(ticketId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("api/projects/{projectId:int}/tickets/draft")]
    public async Task<DraftTicket> GenerateDraft(int projectId, DraftTicketRequest request, CancellationToken ct)
    {
        var draft = await _drafts.GenerateDraftAsync(
            projectId,
            request.ProjectName,
            request.ProposedTitle,
            request.MessageText,
            request.LinkedFilePaths,
            request.LinkedSymbols,
            request.SessionId,
            ct);

        if (request.SessionId is > 0)
            draft.SourceChatSessionId = request.SessionId.Value;
        if (request.MessageId is > 0)
            draft.SourceMessageId = request.MessageId.Value;
        if (!string.IsNullOrWhiteSpace(request.MessageText))
            draft.SourceMessageText = request.MessageText;

        return draft;
    }

    [HttpPost("api/projects/{projectId:int}/tickets/draft/confirm")]
    public async Task<ActionResult<ProjectTicket>> ConfirmDraft(int projectId, DraftTicket current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(current.Title))
            return BadRequest(new { error = "Draft ticket title is required." });

        var provenance = await ValidateDraftChatProvenanceAsync(projectId, current, ct);
        if (!provenance.IsValid)
        {
            return BadRequest(new
            {
                error = provenance.Error,
                reasonCode = provenance.ReasonCode,
                boundary = "Draft confirmation requires server-verified chat provenance before persisting chat source references."
            });
        }

        var ticket = MapDraftTicket(projectId, current, provenance.Provenance);
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpPost("api/projects/{projectId:int}/tickets/draft/plan")]
    public Task<DraftTicket> GeneratePlan(int projectId, DraftTicket current, CancellationToken ct) =>
        _drafts.GeneratePlanAsync(projectId, current, ct);

    [HttpPost("api/projects/{projectId:int}/tickets/draft/tests")]
    public Task<DraftTicket> RegenerateTests(int projectId, DraftTicket current, CancellationToken ct) =>
        _drafts.RegenerateTestsAsync(projectId, current, ct);

    [HttpPost("api/projects/{projectId:int}/tickets/generate-from-codebase")]
    public Task<CodebaseTicketGenerationResult> GenerateFromCodebase(int projectId, CancellationToken ct) =>
        _generator.GenerateTicketsAsync(projectId, ct);

    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/build-preview")]
    public Task<TicketBuildPreview> CreateBuildPreview(int projectId, long ticketId, CancellationToken ct) =>
        _orchestrator.CreateBuildPreviewAsync(projectId, ticketId, ct);

    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs")]
    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs/disposable")]
    public async Task<ActionResult<TicketBuildRunDto>> StartBuildRun(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest? request,
        CancellationToken ct)
    {
        var result = await _buildRuns.StartDisposableAsync(projectId, ticketId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// POST skeleton-runs — the P0-1 walking skeleton: readiness → proposal (persisted
    /// as evidence) → disposable workspace → apply-in-workspace → build/test → evidence
    /// packaged. No new authority: blocked states are explicit and terminal, and the run
    /// cannot request, consume, or simulate approval.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs")]
    public async Task<ActionResult<TicketBuildRunDto>> StartSkeletonRun(
        int projectId,
        long ticketId,
        CancellationToken ct)
    {
        var result = await _skeletonRuns.StartAsync(projectId, ticketId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// POST skeleton-runs/{runId}/continue — requests continuation of a run halted for
    /// approval. The only unblock is a live accepted approval matching the run's
    /// requirement exactly; this endpoint can never create, grant, or simulate one.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/continue")]
    public async Task<ActionResult<TicketBuildRunDto>> ContinueSkeletonRun(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var result = await _skeletonRuns.ContinueAsync(projectId, ticketId, runId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// POST skeleton-runs/{runId}/apply — applies an approved, continued run through
    /// the governed workspace spine. Copy-only, evidence-chained, sandbox-only
    /// (SkeletonApply:Enabled, off by default); the approval is re-verified live.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/apply")]
    public async Task<ActionResult<TicketBuildRunDto>> ApplySkeletonRun(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var result = await _skeletonRuns.ApplyAsync(projectId, ticketId, runId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// GET skeleton-runs/{runId}/critic-package — the full-fidelity review package a
    /// completed skeleton run prepared for the independent critic. Read-only review
    /// material: serving it grants, requests, and simulates nothing.
    /// </summary>
    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/critic-package")]
    public async Task<ActionResult<SkeletonCriticPackage>> GetSkeletonCriticPackage(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var package = await _skeletonRuns.GetCriticPackageAsync(projectId, ticketId, runId, ct);
        return package is null ? NotFound() : Ok(package);
    }

    /// <summary>
    /// POST batch-maps — detects the dependency map for a batch of tickets
    /// (P2-1): explicit blocks plus predicted footprint overlaps, every edge with
    /// a named reason, persisted as hash-sealed evidence. A map is advisory: it
    /// schedules nothing, starts nothing, and grants nothing.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/batch-maps")]
    public async Task<ActionResult<SkeletonBatchMapOutcome>> DetectBatchMap(
        int projectId,
        [FromBody] BatchMapRequestBody body,
        CancellationToken ct)
    {
        if (body.TicketIds is null || body.TicketIds.Count == 0)
        {
            return BadRequest(new { error = "TicketIds is required.", boundary = SkeletonBatchMap.BoundaryText });
        }

        var outcome = await _batchMaps.DetectAsync(
            projectId,
            body.TicketIds,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "unknown-user",
            ct);
        return outcome is null ? NotFound() : Ok(outcome);
    }

    public sealed record BatchMapRequestBody(IReadOnlyList<long>? TicketIds);

    /// <summary>GET batch-maps/{mapId} — reads a stored map back with its integrity re-verified.</summary>
    [HttpGet("api/projects/{projectId:int}/batch-maps/{mapId}")]
    public async Task<ActionResult<SkeletonBatchMapRecord>> GetBatchMap(int projectId, string mapId, CancellationToken ct)
    {
        var record = await _batchMaps.GetAsync(projectId, mapId, ct);
        return record is null ? NotFound() : Ok(record);
    }

    /// <summary>
    /// POST batch-plans — sequences a sealed dependency map into execution waves
    /// (P2-2). Derives only from a map whose seal verifies; cycles are named
    /// blockers for the human, never auto-broken. A plan is a proposal: it grants
    /// nothing and runs nothing.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/batch-plans")]
    public async Task<ActionResult<SkeletonBatchPlanOutcome>> PlanBatch(
        int projectId,
        [FromBody] BatchPlanRequestBody body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.MapId))
        {
            return BadRequest(new { error = "MapId is required.", boundary = SkeletonBatchPlan.BoundaryText });
        }

        var outcome = await _batchPlans.PlanAsync(
            projectId,
            body.MapId,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "unknown-user",
            ct);
        return Ok(outcome);
    }

    public sealed record BatchPlanRequestBody(string? MapId);

    /// <summary>GET batch-plans/{planId} — reads a stored plan back with its integrity re-verified.</summary>
    [HttpGet("api/projects/{projectId:int}/batch-plans/{planId}")]
    public async Task<ActionResult<SkeletonBatchPlanRecord>> GetBatchPlan(int projectId, string planId, CancellationToken ct)
    {
        var record = await _batchPlans.GetAsync(projectId, planId, ct);
        return record is null ? NotFound() : Ok(record);
    }

    /// <summary>
    /// POST batch-runs — starts a batch over a sealed, schedulable plan (P2-3):
    /// wave-1 tickets get their walking-skeleton runs. The batch composes
    /// single-ticket loops and can only ever START them — every gate stays human,
    /// per ticket. Advance is requested, never self-acting.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/batch-runs")]
    public async Task<ActionResult<SkeletonBatchRunOutcome>> StartBatchRun(
        int projectId,
        [FromBody] BatchRunRequestBody body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.PlanId))
        {
            return BadRequest(new { error = "PlanId is required.", boundary = SkeletonBatchRunStatus.BoundaryText });
        }

        var outcome = await _skeletonBatchRuns.StartAsync(
            projectId,
            body.PlanId,
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "unknown-user",
            ct);
        return Ok(outcome);
    }

    public sealed record BatchRunRequestBody(string? PlanId);

    /// <summary>POST batch-runs/{batchId}/advance — starts tickets whose upstreams have applied since the last advance.</summary>
    [HttpPost("api/projects/{projectId:int}/batch-runs/{batchId}/advance")]
    public async Task<ActionResult<SkeletonBatchRunOutcome>> AdvanceBatchRun(int projectId, string batchId, CancellationToken ct) =>
        Ok(await _skeletonBatchRuns.AdvanceAsync(projectId, batchId, ct));

    /// <summary>GET batch-runs/{batchId} — derived live status: per-ticket run state, eligibility, named waits. Read-only.</summary>
    [HttpGet("api/projects/{projectId:int}/batch-runs/{batchId}")]
    public async Task<ActionResult<SkeletonBatchRunStatus>> GetBatchRun(int projectId, string batchId, CancellationToken ct)
    {
        var status = await _skeletonBatchRuns.GetAsync(projectId, batchId, ct);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>
    /// POST skeleton-runs/{runId}/critic-review — requests an independent critic
    /// review of the run's prepared work package. The critic pulls the package from
    /// durable evidence itself and reviews it with no team memory. Findings are
    /// advisory: a finding is not a veto, review is not approval, and the human
    /// gate remains a separate governed step.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/critic-review")]
    public async Task<ActionResult<SkeletonCriticReviewOutcome>> RequestSkeletonCriticReview(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var outcome = await _criticReviews.ReviewAsync(new SkeletonCriticReviewRequest
        {
            ProjectId = projectId,
            TicketId = ticketId,
            RunId = runId,
            RequestedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "unknown-user"
        }, ct);
        return outcome is null ? NotFound() : Ok(outcome);
    }

    /// <summary>
    /// POST skeleton-runs/{runId}/findings/{findingId}/disposition — records the
    /// human decision about a critic finding (accept the risk, defer the fix, or
    /// reject the finding, with a required reason). A disposition is not approval:
    /// it removes the finding blockage only, and continuation still requires its
    /// own live accepted approval.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/findings/{findingId}/disposition")]
    public async Task<ActionResult<SkeletonFindingDispositionOutcome>> RecordFindingDisposition(
        int projectId,
        long ticketId,
        string runId,
        string findingId,
        [FromBody] FindingDispositionBody body,
        CancellationToken ct)
    {
        if (!Enum.TryParse<SkeletonFindingDispositionKind>(body.Disposition, ignoreCase: true, out var kind))
        {
            return BadRequest(new
            {
                error = "Disposition must be AcceptRisk, FixInFollowUp, or Reject.",
                boundary = SkeletonFindingDispositionOutcome.BoundaryText
            });
        }

        var outcome = await _findingDispositions.RecordAsync(new SkeletonFindingDispositionRequest
        {
            ProjectId = projectId,
            TicketId = ticketId,
            RunId = runId,
            FindingId = findingId,
            Disposition = kind,
            Reason = body.Reason ?? string.Empty,
            DecidedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "unknown-user"
        }, ct);
        return outcome is null ? NotFound() : Ok(outcome);
    }

    public sealed record FindingDispositionBody(string? Disposition, string? Reason);

    /// <summary>
    /// GET skeleton-runs/{runId}/gate-recommendation — policy's risk-tier advice
    /// for a halted run's gate (P2-6). Recommendation only: policy cannot click,
    /// and the P1-6 catch-rate is a hard input — no measured net, no
    /// recommendation beyond human judgment. Read-only; grants nothing.
    /// </summary>
    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/gate-recommendation")]
    public async Task<ActionResult<SkeletonGateRecommendation>> GetGateRecommendation(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var recommendation = await _gateRecommendations.RecommendAsync(projectId, ticketId, runId, ct);
        return recommendation is null ? NotFound() : Ok(recommendation);
    }

    /// <summary>
    /// GET skeleton-runs/{runId}/report — reconstructs the whole governed loop from
    /// durable evidence: events, critic package (hash recomputed from disk), the
    /// approval consumed, and the apply spine's receipts. Read-only and verifying:
    /// unverifiable links are named as gaps, never patched over.
    /// </summary>
    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/skeleton-runs/{runId}/report")]
    public async Task<ActionResult<SkeletonRunReport>> GetSkeletonRunReport(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var report = await _skeletonRuns.GetRunReportAsync(projectId, ticketId, runId, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs")]
    public async Task<ActionResult<IReadOnlyList<TicketBuildRunSummaryDto>>> GetBuildRuns(
        int projectId,
        long ticketId,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var runs = await _buildRuns.GetRunsAsync(projectId, ticketId, take, ct);
        return runs is null ? NotFound() : Ok(runs);
    }

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs/{runId}")]
    public async Task<ActionResult<TicketBuildRunDetailDto>> GetBuildRun(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var run = await _buildRuns.GetRunAsync(projectId, ticketId, runId, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/build-readiness")]
    public Task<BuildReadinessResult> EvaluateReadiness(int projectId, long ticketId, CancellationToken ct) =>
        _readiness.EvaluateReadinessAsync(projectId, ticketId, ct);

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/evidence-summary")]
    public async Task<ActionResult<TicketEvidenceSummaryDto>> GetEvidenceSummary(int projectId, long ticketId, CancellationToken ct)
    {
        var summary = await _evidenceSummary.GetEvidenceSummaryAsync(projectId, ticketId, ct);
        return summary is null ? NotFound() : Ok(summary);
    }

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs/{runId}/review")]
    public async Task<ActionResult<TicketRunReviewDto>> GetRunReview(int projectId, long ticketId, string runId, CancellationToken ct)
    {
        var review = await _runReview.GetRunReviewAsync(projectId, ticketId, runId, ct);
        return review is null ? NotFound() : Ok(review);
    }

    [HttpPost("api/tickets/{ticketId:long}/proposal")]
    public Task<BuilderProposal> GenerateProposal(long ticketId, CancellationToken ct) =>
        _proposals.GenerateProposalAsync(ticketId, ct);

    [HttpPost("api/projects/{projectId:int}/proposal")]
    public Task<BuilderProposal> GenerateProposalFromRequest(int projectId, [FromBody] ProposalRequest request, CancellationToken ct) =>
        _proposals.GenerateProposalFromRequestAsync(projectId, request.Request, ct);

    /// <summary>
    /// Retired by P0-4: direct proposal apply mutated source without the governed
    /// evidence chain. Source changes travel through skeleton runs — critic package →
    /// accepted approval → continuation → evidence-chained copy-only apply.
    /// </summary>
    [HttpPost("api/projects/{projectId:int}/proposal/apply")]
    public IActionResult ApplyProposal(BuilderProposal proposal) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "Direct proposal apply is retired.",
            useInstead = "POST api/projects/{projectId}/tickets/{ticketId}/skeleton-runs, then continue and apply through the governed spine."
        });

    [HttpPost("api/tickets/{ticketId:long}/apply-and-build")]
    public Task<TicketBuildResult> ApplyAndBuild(TicketBuildApproval approval, CancellationToken ct) =>
        _orchestrator.ApplyAndBuildAsync(approval, ct);

    [HttpPost("api/projects/{projectId:int}/proposal/validate-architecture")]
    public Task<BuildReadinessResult> ValidateArchitecture(BuilderProposal proposal, CancellationToken ct) =>
        _readiness.ValidateProposalArchitectureAsync(proposal, ct);

    public sealed record DraftTicketRequest(string ProjectName, string ProposedTitle, string MessageText, string? LinkedFilePaths, string? LinkedSymbols, long? SessionId, long? MessageId = null);
    public sealed record ProposalRequest(string Request);

    private static ProjectTicket MapCreateRequest(int projectId, CreateProjectTicketRequest request)
    {
        var technicalNotes = BuildTechnicalNotes(request.ProposedChange, request.ExternalReferences);
        var generationNote = BuildGenerationNote(request.Provenance);
        var acceptanceCriteria = FormatAcceptanceCriteria(request.AcceptanceCriteria);

        return new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = request.Title.Trim(),
            TicketType = Normalize(request.Type, "Task"),
            Priority = Normalize(request.Priority, "Medium"),
            Summary = request.Summary,
            Problem = request.Problem,
            AcceptanceCriteria = acceptanceCriteria,
            TechnicalNotes = technicalNotes,
            Status = "Draft",
            Content = BuildContent(request, acceptanceCriteria, technicalNotes, generationNote),
            IsGenerated = request.Provenance is not null,
            GenerationNote = generationNote
        };
    }

    private async Task<ChatProvenanceValidation> ValidateDraftChatProvenanceAsync(
        int projectId,
        DraftTicket draft,
        CancellationToken ct)
    {
        var hasSession = draft.SourceChatSessionId > 0;
        var hasMessage = draft.SourceMessageId > 0;
        if (!hasSession && !hasMessage)
            return ChatProvenanceValidation.Valid(null);

        if (!hasSession || !hasMessage)
        {
            return ChatProvenanceValidation.Invalid(
                "ChatProvenanceIncomplete",
                "Both SourceChatSessionId and SourceMessageId are required when confirming chat provenance.");
        }

        var sessionId = draft.SourceChatSessionId;
        var messageId = draft.SourceMessageId;

        var session = await _chatHistory.GetSessionByIdAsync(sessionId, ct);
        if (session is null)
        {
            return ChatProvenanceValidation.Invalid(
                "ChatSessionMissing",
                "The supplied source chat session was not found for the current tenant.");
        }

        if (session.ProjectId != projectId)
        {
            return ChatProvenanceValidation.Invalid(
                "ChatSessionProjectMismatch",
                "The supplied source chat session does not belong to this project.");
        }

        var message = await _chatHistory.GetMessageByIdAsync(messageId, projectId, ct);
        if (message is null)
        {
            return ChatProvenanceValidation.Invalid(
                "ChatMessageMissing",
                "The supplied source chat message was not found for this project.");
        }

        if (message.ChatSessionId != sessionId)
        {
            return ChatProvenanceValidation.Invalid(
                "ChatMessageSessionMismatch",
                "The supplied source chat message does not belong to the supplied source chat session.");
        }

        return ChatProvenanceValidation.Valid(new VerifiedChatProvenance(sessionId, messageId, message.Message));
    }

    private static ProjectTicket MapDraftTicket(int projectId, DraftTicket draft, VerifiedChatProvenance? provenance)
    {
        var technicalNotes = BuildDraftTechnicalNotes(draft);
        var generationNote = string.IsNullOrWhiteSpace(draft.GenerationNote)
            ? "Confirmed from draft ticket. Confirmation persists the ticket only; it does not start or approve a governed run."
            : draft.GenerationNote.Trim();
        var sourceMessageText = provenance?.MessageText ?? draft.SourceMessageText;

        return new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = draft.Title.Trim(),
            TicketType = Normalize(draft.TicketType, "Task"),
            Priority = Normalize(draft.Priority, "Medium"),
            Summary = TrimToNull(draft.Summary),
            Background = TrimToNull(draft.Background),
            AcceptanceCriteria = TrimToNull(draft.AcceptanceCriteria),
            TechnicalNotes = technicalNotes,
            Status = "Draft",
            Content = BuildDraftContent(draft, technicalNotes, generationNote, sourceMessageText),
            LinkedFilePaths = TrimToNull(draft.LinkedFilePaths),
            LinkedSymbols = TrimToNull(draft.LinkedSymbols),
            UnitTests = TrimToNull(draft.UnitTests),
            IntegrationTests = TrimToNull(draft.IntegrationTests),
            ManualTests = TrimToNull(draft.ManualTests),
            RegressionTests = TrimToNull(draft.RegressionTests),
            BuildValidation = TrimToNull(draft.BuildValidation),
            ContextSummary = TrimToNull(sourceMessageText),
            IsGenerated = draft.IsGenerated,
            GenerationNote = generationNote,
            SourceChatSessionId = provenance?.SessionId,
            SourceChatMessageId = provenance?.MessageId
        };
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? BuildDraftTechnicalNotes(DraftTicket draft)
    {
        var sections = new List<string>();
        AddSection(sections, "Implementation plan", draft.ImplementationPlan);
        AddSection(sections, "Unit tests", draft.UnitTests);
        AddSection(sections, "Integration tests", draft.IntegrationTests);
        AddSection(sections, "Manual tests", draft.ManualTests);
        AddSection(sections, "Regression tests", draft.RegressionTests);
        AddSection(sections, "Build validation", draft.BuildValidation);

        return sections.Count == 0 ? null : string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
    }

    private static string BuildDraftContent(
        DraftTicket draft,
        string? technicalNotes,
        string? generationNote,
        string? sourceMessageText)
    {
        var sections = new List<string> { $"# {draft.Title.Trim()}" };
        AddSection(sections, "Summary", draft.Summary);
        AddSection(sections, "Background", draft.Background);
        AddSection(sections, "Acceptance criteria", draft.AcceptanceCriteria);
        if (!string.IsNullOrWhiteSpace(technicalNotes))
            sections.Add(technicalNotes);
        if (!string.IsNullOrWhiteSpace(generationNote))
            sections.Add($"## Provenance{Environment.NewLine}{generationNote}");
        AddSection(sections, "Source chat excerpt", sourceMessageText);

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
    }

    private sealed record VerifiedChatProvenance(long SessionId, long MessageId, string MessageText);

    private sealed record ChatProvenanceValidation(
        bool IsValid,
        VerifiedChatProvenance? Provenance,
        string? ReasonCode,
        string? Error)
    {
        public static ChatProvenanceValidation Valid(VerifiedChatProvenance? provenance) =>
            new(true, provenance, null, null);

        public static ChatProvenanceValidation Invalid(string reasonCode, string error) =>
            new(false, null, reasonCode, error);
    }

    private static void AddSection(ICollection<string> sections, string heading, string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
            sections.Add($"## {heading}{Environment.NewLine}{body.Trim()}");
    }

    private static string? FormatAcceptanceCriteria(IReadOnlyList<string> acceptanceCriteria)
    {
        var items = acceptanceCriteria
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"- {item.Trim()}")
            .ToArray();

        return items.Length == 0 ? null : string.Join(Environment.NewLine, items);
    }

    private static string? BuildTechnicalNotes(string? proposedChange, IReadOnlyList<ExternalReferenceDto> externalReferences)
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(proposedChange))
            sections.Add($"## Proposed change{Environment.NewLine}{proposedChange.Trim()}");

        var refs = externalReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Provider) || !string.IsNullOrWhiteSpace(reference.Url))
            .Select(reference =>
            {
                var label = string.Join(
                    " ",
                    new[] { reference.Provider, reference.Kind, reference.ExternalId }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));
                var title = string.IsNullOrWhiteSpace(reference.Title) ? string.Empty : $" - {reference.Title.Trim()}";
                var url = string.IsNullOrWhiteSpace(reference.Url) ? string.Empty : $" ({reference.Url.Trim()})";
                return $"- {label}{title}{url}".Trim();
            })
            .ToArray();

        if (refs.Length > 0)
            sections.Add($"## External references{Environment.NewLine}{string.Join(Environment.NewLine, refs)}");

        return sections.Count == 0 ? null : string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
    }

    private static string? BuildGenerationNote(TicketProvenanceDto? provenance)
    {
        if (provenance is null)
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(provenance.Source))
            parts.Add($"Source: {provenance.Source.Trim()}");
        if (!string.IsNullOrWhiteSpace(provenance.CreatedBy))
            parts.Add($"Created by: {provenance.CreatedBy.Trim()}");
        if (!string.IsNullOrWhiteSpace(provenance.Notes))
            parts.Add($"Notes: {provenance.Notes.Trim()}");

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static string BuildContent(
        CreateProjectTicketRequest request,
        string? acceptanceCriteria,
        string? technicalNotes,
        string? generationNote)
    {
        var sections = new List<string> { $"# {request.Title.Trim()}" };

        if (!string.IsNullOrWhiteSpace(request.Summary))
            sections.Add($"## Summary{Environment.NewLine}{request.Summary.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Problem))
            sections.Add($"## Problem{Environment.NewLine}{request.Problem.Trim()}");
        if (!string.IsNullOrWhiteSpace(acceptanceCriteria))
            sections.Add($"## Acceptance criteria{Environment.NewLine}{acceptanceCriteria}");
        if (!string.IsNullOrWhiteSpace(technicalNotes))
            sections.Add(technicalNotes);
        if (!string.IsNullOrWhiteSpace(generationNote))
            sections.Add($"## Provenance{Environment.NewLine}{generationNote}");

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", sections);
    }

    private static string DeriveTitle(string discussion)
    {
        var firstLine = discussion
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();

        if (string.IsNullOrWhiteSpace(firstLine))
            return "Ticket from discussion";

        return firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...";
    }
}

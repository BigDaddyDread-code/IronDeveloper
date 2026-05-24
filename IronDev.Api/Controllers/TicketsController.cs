using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
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
    private readonly IBuilderReadinessService _readiness;
    private readonly IBuilderProposalService _proposals;

    public TicketsController(
        ITicketService tickets,
        IDraftTicketService drafts,
        ICodebaseTicketGeneratorService generator,
        ITicketBuildOrchestrator orchestrator,
        IBuilderReadinessService readiness,
        IBuilderProposalService proposals)
    {
        _tickets = tickets;
        _drafts = drafts;
        _generator = generator;
        _orchestrator = orchestrator;
        _readiness = readiness;
        _proposals = proposals;
    }

    [HttpGet("api/projects/{projectId:int}/tickets")]
    public async Task<IReadOnlyList<ProjectTicket>> GetTickets(int projectId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        await _tickets.GetRecentTicketsAsync(projectId, take, ct);

    [HttpGet("api/tickets/{ticketId:long}")]
    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}")]
    public async Task<ActionResult<ProjectTicket>> GetTicket(long ticketId, CancellationToken ct, int? projectId = null)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, ct);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("api/projects/{projectId:int}/tickets")]
    public async Task<ActionResult<ProjectTicket>> SaveTicket(int projectId, ProjectTicket ticket, CancellationToken ct)
    {
        ticket.ProjectId = projectId;
        ticket.Id = await _tickets.SaveTicketAsync(ticket, ct);
        return Ok(ticket);
    }

    [HttpDelete("api/tickets/{ticketId:long}")]
    public async Task<IActionResult> ArchiveTicket(long ticketId, CancellationToken ct)
    {
        var ok = await _tickets.ArchiveTicketAsync(ticketId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("api/projects/{projectId:int}/tickets/draft")]
    public Task<DraftTicket> GenerateDraft(int projectId, DraftTicketRequest request, CancellationToken ct) =>
        _drafts.GenerateDraftAsync(projectId, request.ProjectName, request.ProposedTitle, request.MessageText, request.LinkedFilePaths, request.LinkedSymbols, request.SessionId, ct);

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

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/build-readiness")]
    public Task<BuildReadinessResult> EvaluateReadiness(int projectId, long ticketId, CancellationToken ct) =>
        _readiness.EvaluateReadinessAsync(projectId, ticketId, ct);

    [HttpPost("api/tickets/{ticketId:long}/proposal")]
    public Task<BuilderProposal> GenerateProposal(long ticketId, CancellationToken ct) =>
        _proposals.GenerateProposalAsync(ticketId, ct);

    [HttpPost("api/projects/{projectId:int}/proposal")]
    public Task<BuilderProposal> GenerateProposalFromRequest(int projectId, [FromBody] ProposalRequest request, CancellationToken ct) =>
        _proposals.GenerateProposalFromRequestAsync(projectId, request.Request, ct);

    [HttpPost("api/projects/{projectId:int}/proposal/apply")]
    public async Task<IActionResult> ApplyProposal(BuilderProposal proposal, CancellationToken ct)
    {
        await _proposals.ApplyProposalAsync(proposal, ct);
        return Ok();
    }

    [HttpPost("api/tickets/{ticketId:long}/apply-and-build")]
    public Task<TicketBuildResult> ApplyAndBuild(TicketBuildApproval approval, CancellationToken ct) =>
        _orchestrator.ApplyAndBuildAsync(approval, ct);

    [HttpPost("api/projects/{projectId:int}/proposal/validate-architecture")]
    public Task<BuildReadinessResult> ValidateArchitecture(BuilderProposal proposal, CancellationToken ct) =>
        _readiness.ValidateProposalArchitectureAsync(proposal, ct);

    public sealed record DraftTicketRequest(string ProjectName, string ProposedTitle, string MessageText, string? LinkedFilePaths, string? LinkedSymbols, long? SessionId);
    public sealed record ProposalRequest(string Request);
}

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
    private readonly ITicketBuildWorkflowOrchestrator _buildRuns;
    private readonly IBuilderReadinessService _readiness;
    private readonly ITicketEvidenceSummaryService _evidenceSummary;
    private readonly ITicketRunReviewService _runReview;
    private readonly IBuilderProposalService _proposals;

    public TicketsController(
        ITicketService tickets,
        IDraftTicketService drafts,
        ICodebaseTicketGeneratorService generator,
        ITicketBuildOrchestrator orchestrator,
        ITicketBuildWorkflowOrchestrator buildRuns,
        IBuilderReadinessService readiness,
        ITicketEvidenceSummaryService evidenceSummary,
        ITicketRunReviewService runReview,
        IBuilderProposalService proposals)
    {
        _tickets = tickets;
        _drafts = drafts;
        _generator = generator;
        _orchestrator = orchestrator;
        _buildRuns = buildRuns;
        _readiness = readiness;
        _evidenceSummary = evidenceSummary;
        _runReview = runReview;
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

    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs")]
    public async Task<ActionResult<TicketBuildRunDto>> StartBuildRun(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest? request,
        CancellationToken ct)
    {
        var result = await _buildRuns.StartAsync(new TicketBuildWorkflowRequest
        {
            WorkflowRunId = request?.WorkflowRunId,
            ProjectId = projectId,
            TicketId = ticketId,
            MaxRetries = request?.MaxRetries ?? 3
        }, ct);

        return Ok(ToBuildRunDto(projectId, ticketId, result));
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

    private static TicketBuildRunDto ToBuildRunDto(
        int projectId,
        long ticketId,
        TicketBuildWorkflowResult result) => new()
        {
            RunId = result.WorkflowRunId.ToString("D"),
            ProjectId = projectId,
            TicketId = ticketId,
            Status = result.Status.ToString(),
            CurrentNode = result.CurrentNode,
            RequiresHumanApproval = result.RequiresHumanApproval,
            Message = result.Message
        };

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

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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

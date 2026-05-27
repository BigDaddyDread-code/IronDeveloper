using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class DiscussionCodeLoopController : ControllerBase
{
    private readonly IDiscussionDocumentService _discussionDocuments;
    private readonly ITicketFromDocumentService _ticketFromDocument;
    private readonly ITicketReviewService _reviews;
    private readonly IDisposableCodeRunService _codeRuns;
    private readonly IRunReviewPackageService _reviewPackages;
    private readonly IBuildScenarioCatalog _scenarios;

    public DiscussionCodeLoopController(
        IDiscussionDocumentService discussionDocuments,
        ITicketFromDocumentService ticketFromDocument,
        ITicketReviewService reviews,
        IDisposableCodeRunService codeRuns,
        IRunReviewPackageService reviewPackages,
        IBuildScenarioCatalog scenarios)
    {
        _discussionDocuments = discussionDocuments;
        _ticketFromDocument = ticketFromDocument;
        _reviews = reviews;
        _codeRuns = codeRuns;
        _reviewPackages = reviewPackages;
        _scenarios = scenarios;
    }

    [HttpGet("api/projects/{projectId:int}/code-scenarios")]
    public async Task<ActionResult<IReadOnlyList<BuildScenario>>> GetScenarios(
        int projectId,
        CancellationToken ct) =>
        Ok(await _scenarios.GetScenariosAsync(projectId, ct));

    [HttpPost("api/projects/{projectId:int}/discussions")]
    public async Task<ActionResult<SaveDiscussionResponse>> SaveDiscussion(
        int projectId,
        SaveDiscussionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Discussion title is required." });
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Discussion content is required." });

        return Ok(await _discussionDocuments.SaveDiscussionAsync(projectId, request, ct));
    }

    [HttpPost("api/projects/{projectId:int}/documents/{documentVersionId:long}/tickets")]
    public async Task<ActionResult<CreateTicketFromDocumentResponse>> CreateTicketFromDocument(
        int projectId,
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken ct)
    {
        var result = await _ticketFromDocument.CreateTicketAsync(projectId, documentVersionId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/review")]
    public async Task<ActionResult<RunTicketReviewResponse>> ReviewTicket(
        int projectId,
        long ticketId,
        RunTicketReviewRequest request,
        CancellationToken ct)
    {
        if (request.UseLiveModel)
            return BadRequest(new { error = "Live model ticket review is not enabled for this deterministic scenario." });

        var result = await _reviews.ReviewAsync(projectId, ticketId, request, ct);
        return result is null
            ? NotFound()
            : Ok(new RunTicketReviewResponse { ReviewId = result.ReviewId, Result = result });
    }

    [HttpPost("api/projects/{projectId:int}/tickets/{ticketId:long}/disposable-code-runs")]
    public async Task<ActionResult<StartDisposableCodeRunResponse>> StartDisposableCodeRun(
        int projectId,
        long ticketId,
        StartDisposableCodeRunRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewId))
            return BadRequest(new { error = "ReviewId is required." });

        try
        {
            var result = await _codeRuns.StartAsync(projectId, ticketId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("api/projects/{projectId:int}/tickets/{ticketId:long}/build-runs/{runId}/review-package")]
    public async Task<ActionResult<RunReviewPackage>> GetReviewPackage(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken ct)
    {
        var result = await _reviewPackages.GetReviewPackageAsync(projectId, ticketId, runId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}

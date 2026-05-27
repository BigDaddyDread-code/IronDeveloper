using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class AlphaDiscussionCodeLoopController : ControllerBase
{
    private readonly IDiscussionDocumentService _discussionDocuments;
    private readonly ITicketFromDocumentService _ticketFromDocument;
    private readonly ITicketDebateService _debates;
    private readonly IAlphaHelloWorldCodeRunService _codeRuns;

    public AlphaDiscussionCodeLoopController(
        IDiscussionDocumentService discussionDocuments,
        ITicketFromDocumentService ticketFromDocument,
        ITicketDebateService debates,
        IAlphaHelloWorldCodeRunService codeRuns)
    {
        _discussionDocuments = discussionDocuments;
        _ticketFromDocument = ticketFromDocument;
        _debates = debates;
        _codeRuns = codeRuns;
    }

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

    [HttpPost("api/documents/{documentVersionId:long}/tickets")]
    public async Task<ActionResult<CreateTicketFromDocumentResponse>> CreateTicketFromDocument(
        long documentVersionId,
        CreateTicketFromDocumentRequest request,
        CancellationToken ct)
    {
        var result = await _ticketFromDocument.CreateTicketAsync(documentVersionId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("api/tickets/{ticketId:long}/debate")]
    public async Task<ActionResult<RunTicketDebateResponse>> RunDebate(
        long ticketId,
        RunTicketDebateRequest request,
        CancellationToken ct)
    {
        if (request.UseLiveModel)
            return BadRequest(new { error = "Live model debate is not enabled for the Alpha discussion-to-code loop." });

        var result = await _debates.RunDebateAsync(ticketId, request, ct);
        return result is null
            ? NotFound()
            : Ok(new RunTicketDebateResponse { DebateId = result.DebateId, Result = result });
    }

    [HttpPost("api/tickets/{ticketId:long}/alpha-disposable-code-runs")]
    public async Task<ActionResult<StartAlphaDisposableCodeRunResponse>> StartAlphaDisposableCodeRun(
        long ticketId,
        StartAlphaDisposableCodeRunRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DebateId))
            return BadRequest(new { error = "DebateId is required." });

        try
        {
            var result = await _codeRuns.StartAsync(ticketId, request, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

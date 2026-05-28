using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IChatHistoryService _chat;
    private readonly IChatFeedbackService _feedback;
    private readonly IProjectStateReviewService _projectStateReview;

    public ChatController(
        IChatHistoryService chat,
        IChatFeedbackService feedback,
        IProjectStateReviewService projectStateReview)
    {
        _chat = chat;
        _feedback = feedback;
        _projectStateReview = projectStateReview;
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions")]
    public Task<IReadOnlyList<ProjectChatSession>> GetSessions(int projectId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        _chat.GetRecentSessionsAsync(projectId, take, ct);

    [HttpGet("api/chat/sessions/{sessionId:long}")]
    public Task<ProjectChatSession?> GetSession(long sessionId, CancellationToken ct) =>
        _chat.GetSessionByIdAsync(sessionId, ct);

    [HttpPost("api/projects/{projectId:int}/chat/sessions")]
    public Task<long> SaveSession(ProjectChatSession session, CancellationToken ct) =>
        _chat.SaveSessionAsync(session, ct);

    [HttpDelete("api/chat/sessions/{sessionId:long}")]
    public async Task<IActionResult> DeleteSession(long sessionId, CancellationToken ct)
    {
        await _chat.DeleteSessionAsync(sessionId, ct);
        return NoContent();
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public Task<IReadOnlyList<ChatMessage>> GetMessages(int projectId, long sessionId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        _chat.GetRecentMessagesAsync(projectId, sessionId, take, ct);

    [HttpPost("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public Task<long> SaveMessage(ChatMessage message, CancellationToken ct) =>
        _chat.SaveMessageAsync(message, ct);

    [HttpPost("api/projects/{projectId:int}/chat/complete")]
    public async Task<ActionResult<ChatCompletionResponse>> Complete(
        int projectId,
        ChatCompletionRequest request,
        CancellationToken ct)
    {
        if (request.ProjectId != 0 && request.ProjectId != projectId)
            return BadRequest(new { message = "Request project id must match the route project id." });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required." });

        var mode = string.IsNullOrWhiteSpace(request.Mode)
            ? "projectStateReview"
            : request.Mode.Trim();
        if (!string.Equals(mode, "projectStateReview", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only projectStateReview mode is available for this endpoint." });

        var review = await _projectStateReview.ReviewAsync(projectId, ct);
        if (review is null)
            return NotFound();

        return Ok(new ChatCompletionResponse(
            review.Response,
            review.ContextSummary,
            review.LinkedFilePaths,
            review.LinkedSymbols,
            null));
    }

    public sealed record ChatCompletionRequest(int ProjectId, long? SessionId, string Prompt, string? ActiveModel, string? Mode);
    public sealed record ChatCompletionResponse(string Response, string? ContextSummary, string? LinkedFilePaths, string? LinkedSymbols, long? TraceId);

    [HttpPost("api/projects/{projectId:int}/chat/feedback")]
    public Task<long> SaveFeedback(ChatMessageFeedback feedback, CancellationToken ct) =>
        _feedback.SaveFeedbackAsync(feedback, ct);
}

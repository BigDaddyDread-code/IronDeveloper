using IronDev.Data.Models;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private const string ProjectQuestionMode = "projectQuestion";
    private const string ProjectStateReviewMode = "projectStateReview";
    private const int RecentConversationSummaryMessageLimit = 12;

    private readonly IChatHistoryService _chat;
    private readonly IChatFeedbackService _feedback;
    private readonly IProjectChatResponseService _projectChat;
    private readonly IProjectStateReviewService _projectStateReview;

    public ChatController(
        IChatHistoryService chat,
        IChatFeedbackService feedback,
        IProjectChatResponseService projectChat,
        IProjectStateReviewService projectStateReview)
    {
        _chat = chat;
        _feedback = feedback;
        _projectChat = projectChat;
        _projectStateReview = projectStateReview;
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions")]
    public Task<IReadOnlyList<ProjectChatSession>> GetSessions(int projectId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        _chat.GetRecentSessionsAsync(projectId, take, ct);

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}")]
    public Task<ProjectChatSession?> GetSession(int projectId, long sessionId, CancellationToken ct) =>
        _chat.GetSessionByIdAsync(sessionId, ct);

    [HttpPost("api/projects/{projectId:int}/chat/sessions")]
    public async Task<ActionResult<long>> SaveSession(int projectId, ProjectChatSession session, CancellationToken ct)
    {
        if (session.ProjectId != 0 && session.ProjectId != projectId)
            return BadRequest(new { message = "Session projectId must match route project id." });

        return await _chat.SaveSessionAsync(session, ct);
    }

    [HttpDelete("api/projects/{projectId:int}/chat/sessions/{sessionId:long}")]
    public async Task<IActionResult> DeleteSession(int projectId, long sessionId, CancellationToken ct)
    {
        await _chat.DeleteSessionAsync(sessionId, ct);
        return NoContent();
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public Task<IReadOnlyList<ChatMessage>> GetMessages(int projectId, long sessionId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        _chat.GetRecentMessagesAsync(projectId, sessionId, take, ct);

    [HttpPost("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public async Task<ActionResult<long>> SaveMessage(int projectId, long sessionId, ChatMessage message, CancellationToken ct)
    {
        if (message.ProjectId != 0 && message.ProjectId != projectId)
            return BadRequest(new { message = "Message projectId must match route project id." });

        if (message.ChatSessionId != 0 && message.ChatSessionId != sessionId)
            return BadRequest(new { message = "Message chat session id must match route session id." });

        return await _chat.SaveMessageAsync(message, ct);
    }

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
            ? ProjectQuestionMode
            : request.Mode.Trim();

        if (string.Equals(mode, ProjectStateReviewMode, StringComparison.OrdinalIgnoreCase))
        {
            var review = await _projectStateReview.ReviewAsync(projectId, ct);
            if (review is null)
                return NotFound();

            return Ok(new ChatCompletionResponse(
                review.Response,
                review.ContextSummary,
                review.LinkedFilePaths,
                review.LinkedSymbols,
                null,
                Mode: "Exploration",
                ModeConfidence: null,
                ModeReason: "Project state review request is explicit and non-commitment.",
                ShowGovernanceActions: false));
        }

        var hasExplicitMode = TryResolveExplicitConversationMode(mode, out var explicitMode);
        if (!hasExplicitMode && !string.Equals(mode, ProjectQuestionMode, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Unsupported chat mode. Use projectQuestion, projectStateReview, exploration, formalization, or confirmation." });

        var recentConversationSummary = await BuildRecentConversationSummaryAsync(projectId, request.SessionId, ct);
        var answer = await _projectChat.RespondAsync(
            projectId,
            request.Prompt,
            hasExplicitMode ? explicitMode : null,
            recentConversationSummary: recentConversationSummary,
            sessionId: request.SessionId,
            cancellationToken: ct);
        if (answer is null)
            return NotFound();

        return Ok(new ChatCompletionResponse(
            answer.Response,
            answer.ContextSummary,
            answer.LinkedFilePaths,
            answer.LinkedSymbols,
            answer.TraceId,
            answer.Mode,
            answer.ModeConfidence,
            answer.ModeReason,
            answer.ShowGovernanceActions,
            answer.GovernanceActions,
            answer.ReasoningTrace,
            answer.DisambiguationQuestion,
            answer.ReasoningSummary,
            answer.DogfoodTraceId,
            null));

    }

    private async Task<string> BuildRecentConversationSummaryAsync(int projectId, long? sessionId, CancellationToken ct)
    {
        if (!sessionId.HasValue || sessionId.Value <= 0)
            return string.Empty;

        var messages = await _chat.GetRecentMessagesAsync(projectId, sessionId.Value, RecentConversationSummaryMessageLimit, ct).ConfigureAwait(false);
        if (messages.Count == 0)
            return string.Empty;

        var pairs = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Message))
            .Select(message => $"{message.Role}: {message.Message.Trim()}");

        return string.Join(Environment.NewLine, pairs);
    }

    private static bool TryResolveExplicitConversationMode(string mode, out ChatGovernanceMode resolvedMode)
    {
        resolvedMode = mode switch
        {
            var value when value.Equals("exploration", StringComparison.OrdinalIgnoreCase) => ChatGovernanceMode.Exploration,
            var value when value.Equals("formalization", StringComparison.OrdinalIgnoreCase) => ChatGovernanceMode.Formalization,
            var value when value.Equals("confirmation", StringComparison.OrdinalIgnoreCase) => ChatGovernanceMode.Confirmation,
            _ => ChatGovernanceMode.Exploration
        };

        return mode.Equals("exploration", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("formalization", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("confirmation", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record ChatCompletionRequest(int ProjectId, long? SessionId, string Prompt, string? ActiveModel, string? Mode);
    public sealed record ChatCompletionResponse(
        string Response,
        string? ContextSummary,
        string? LinkedFilePaths,
        string? LinkedSymbols,
        long? TraceId,
        string? Mode = null,
        double? ModeConfidence = null,
        string? ModeReason = null,
        bool? ShowGovernanceActions = null,
        IReadOnlyList<string>? GovernanceActions = null,
        IReadOnlyList<string>? ReasoningTrace = null,
        string? DisambiguationQuestion = null,
        string? ReasoningSummary = null,
        string? DogfoodTraceId = null,
        string? DogfoodTracePath = null);

    [HttpPost("api/projects/{projectId:int}/chat/feedback")]
    public Task<long> SaveFeedback(ChatMessageFeedback feedback, CancellationToken ct) =>
        _feedback.SaveFeedbackAsync(feedback, ct);
}

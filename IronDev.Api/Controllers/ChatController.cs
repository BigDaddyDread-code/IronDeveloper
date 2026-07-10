using IronDev.Data.Models;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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
    private readonly IChatTurnPersistenceService _turnPersistence;
    private readonly IProjectChatResponseService _projectChat;
    private readonly IProjectStateReviewService _projectStateReview;
    private readonly IProjectChatDocumentSourceService _documentSources;

    public ChatController(
        IChatHistoryService chat,
        IChatFeedbackService feedback,
        IChatTurnPersistenceService turnPersistence,
        IProjectChatResponseService projectChat,
        IProjectStateReviewService projectStateReview,
        IProjectChatDocumentSourceService documentSources)
    {
        _chat = chat;
        _feedback = feedback;
        _turnPersistence = turnPersistence;
        _projectChat = projectChat;
        _projectStateReview = projectStateReview;
        _documentSources = documentSources;
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

    [HttpGet("api/projects/{projectId:int}/chat/document-sources")]
    public Task<IReadOnlyList<ChatDocumentSource>> GetDocumentSources(int projectId, CancellationToken ct) =>
        _documentSources.GetAvailableSourcesAsync(projectId, ct);

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public async Task<IReadOnlyList<ChatMessage>> GetMessages(
        int projectId,
        long sessionId,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var messages = await _chat.GetRecentMessagesAsync(projectId, sessionId, take, ct);
        var sources = await _documentSources.GetSourcesForMessagesAsync(projectId, sessionId, messages, ct);
        foreach (var message in messages)
        {
            var sourceMessageId = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? message.ReplyToMessageId
                : message.Id;
            message.DocumentSources = sourceMessageId.HasValue && sources.TryGetValue(sourceMessageId.Value, out var linked)
                ? linked
                : [];
        }

        return messages;
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages/{messageId:long}/audit")]
    public async Task<ActionResult<ChatTurnAuditResponse>> GetMessageAudit(
        int projectId,
        long sessionId,
        long messageId,
        CancellationToken ct = default)
    {
        var snapshot = await _turnPersistence.GetByMessageAsync(projectId, sessionId, messageId, ct).ConfigureAwait(false);
        if (snapshot is null)
            return NotFound();

        var source = snapshot.IsFallbackEvidence
            ? ChatAuditSource.TagsFallback
            : ChatAuditSource.NormalizedRows;

        return Ok(new ChatTurnAuditResponse(
            snapshot.ChatMessageId,
            source,
            snapshot.Mode,
            snapshot.ModeConfidence,
            snapshot.ModeReason,
            snapshot.Clarification,
            snapshot.Gate,
            snapshot.RouteTraceId,
            snapshot.DogfoodTraceId,
            snapshot.ContextSummary,
            snapshot.LinkedFilePaths,
            snapshot.LinkedSymbols,
            snapshot.IsFallbackEvidence,
            snapshot.RouteSource,
            snapshot.RouteChallenge,
            snapshot.BaDraft));
    }

    [HttpPost("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public async Task<ActionResult<long>> SaveMessage(int projectId, long sessionId, ChatMessage message, CancellationToken ct)
    {
        if (message.ProjectId != 0 && message.ProjectId != projectId)
            return BadRequest(new { message = "Message projectId must match route project id." });

        if (message.ChatSessionId != 0 && message.ChatSessionId != sessionId)
            return BadRequest(new { message = "Message chat session id must match route session id." });

        message.SourceAttachedBy = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        try
        {
            return await _chat.SaveMessageAsync(message, ct);
        }
        catch (ChatDocumentSourceUnavailableException error)
        {
            return Conflict(new { error = error.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new { error = "The Chat message target is not available in this project." });
        }
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
                Clarification: ChatClarificationState.None,
                Gate: ChatGovernanceGate.FromDecision(new ChatModeDecision(
                    ChatGovernanceMode.Exploration,
                    1,
                    "Project state review is not a formalization lane.")),
                RouteSource: "ProjectStateReview"));
        }

        if (!string.Equals(mode, ProjectQuestionMode, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Unsupported chat mode. Use projectQuestion or projectStateReview." });

        var recentConversationSummary = await BuildRecentConversationSummaryAsync(projectId, request.SessionId, ct);
        ProjectChatResponseResult? answer;
        try
        {
            answer = await _projectChat.RespondAsync(
                projectId,
                request.Prompt,
                null,
                recentConversationSummary: recentConversationSummary,
                sessionId: request.SessionId,
                sourceMessageId: request.SourceMessageId,
                cancellationToken: ct);
        }
        catch (ChatDocumentSourceUnavailableException error)
        {
            return Conflict(new { error = error.Message });
        }
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
            answer.Clarification,
            answer.Gate,
            answer.ReasoningTrace,
            answer.DisambiguationQuestion,
            answer.ReasoningSummary,
            answer.DogfoodTraceId,
            null,
            answer.RouteSource,
            answer.RouteChallenge,
            answer.BaDraft,
            answer.DocumentSources));

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

    public sealed record ChatCompletionRequest(
        int ProjectId,
        long? SessionId,
        string Prompt,
        string? ActiveModel,
        string? Mode,
        long? SourceMessageId = null);
    public sealed record ChatCompletionResponse(
        string Response,
        string? ContextSummary,
        string? LinkedFilePaths,
        string? LinkedSymbols,
        long? TraceId,
        string? Mode = null,
        double? ModeConfidence = null,
        string? ModeReason = null,
        ChatClarificationState? Clarification = null,
        ChatGovernanceGate? Gate = null,
        IReadOnlyList<string>? ReasoningTrace = null,
        string? DisambiguationQuestion = null,
        string? ReasoningSummary = null,
        string? DogfoodTraceId = null,
        string? DogfoodTracePath = null,
        string? RouteSource = null,
        ChatRouteChallenge? RouteChallenge = null,
        BaWorkingDraft? BaDraft = null,
        IReadOnlyList<ChatDocumentSource>? DocumentSources = null);

    [HttpPost("api/projects/{projectId:int}/chat/feedback")]
    public Task<long> SaveFeedback(ChatMessageFeedback feedback, CancellationToken ct) =>
        _feedback.SaveFeedbackAsync(feedback, ct);

}

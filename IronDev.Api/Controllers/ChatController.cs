using IronDev.Data.Models;
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
                "Exploration",
                ShowGovernanceActions: false));
        }

        var hasExplicitMode = TryResolveExplicitConversationMode(mode, out var explicitMode);
        if (!hasExplicitMode && !string.Equals(mode, ProjectQuestionMode, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Unsupported chat mode. Use projectQuestion, projectStateReview, exploration, formalization, or confirmation." });

        var conversationMode = string.Equals(mode, ProjectQuestionMode, StringComparison.OrdinalIgnoreCase)
            ? DetectConversationMode(request.Prompt)
            : explicitMode;

        var answer = await _projectChat.RespondAsync(projectId, request.Prompt, conversationMode, ct);
        if (answer is null)
            return NotFound();

        return Ok(new ChatCompletionResponse(
            answer.Response,
            answer.ContextSummary,
            answer.LinkedFilePaths,
            answer.LinkedSymbols,
            answer.TraceId,
            answer.Mode,
            answer.ShowGovernanceActions,
            answer.GovernanceActions,
            answer.ReasoningTrace,
            answer.DisambiguationQuestion,
            answer.ReasoningSummary,
            answer.DogfoodTraceId,
            null));

    }

    private static bool TryResolveExplicitConversationMode(string mode, out ProjectConversationMode resolvedMode)
    {
        resolvedMode = mode switch
        {
            var value when value.Equals("exploration", StringComparison.OrdinalIgnoreCase) => ProjectConversationMode.Exploration,
            var value when value.Equals("formalization", StringComparison.OrdinalIgnoreCase) => ProjectConversationMode.Formalization,
            var value when value.Equals("confirmation", StringComparison.OrdinalIgnoreCase) => ProjectConversationMode.Confirmation,
            var value when value.Equals("projectExploration", StringComparison.OrdinalIgnoreCase) => ProjectConversationMode.Exploration,
            var value when value.Equals("projectFormalization", StringComparison.OrdinalIgnoreCase) => ProjectConversationMode.Formalization,
            var value when value.Equals("projectConfirmation", StringComparison.OrdinalIgnoreCase) => ProjectConversationMode.Confirmation,
            _ => ProjectConversationMode.Exploration
        };

        return mode.Equals("exploration", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("formalization", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("confirmation", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("projectExploration", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("projectFormalization", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("projectConfirmation", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectConversationMode DetectConversationMode(string prompt)
    {
        var normalized = (prompt ?? string.Empty).ToLowerInvariant().ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return ProjectConversationMode.Exploration;

        var formalizationSignals = new[]
        {
            "make this a ticket",
            "save this as",
            "save this",
            "turn this into",
            "create ticket",
            "build this into",
            "formalize",
            "lock this in",
            "lock it in",
            "create a ticket",
            "save as a ticket",
            "commit this",
            "persist this"
        };

        var exploratorySignals = new[]
        {
            "what information",
            "what do you need",
            "what do i need",
            "how does",
            "how should",
            "why",
            "explore",
            "probe",
            "think about",
            "show me",
            "can you explain",
            "what would",
            "what if",
            "trade-off",
            "trade offs",
            "alternatives",
            "risks",
            "help me understand"
        };

        var exploratoryCount = exploratorySignals.Count(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase));
        var formalizationCount = formalizationSignals.Count(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase));

        if (exploratoryCount >= 1 && formalizationCount >= 1)
            return ProjectConversationMode.Confirmation;
        if (formalizationCount >= 1)
            return ProjectConversationMode.Formalization;
        if (normalized.Contains('?') || exploratoryCount >= 1)
            return ProjectConversationMode.Exploration;
        return ProjectConversationMode.Exploration;
    }

    public sealed record ChatCompletionRequest(int ProjectId, long? SessionId, string Prompt, string? ActiveModel, string? Mode);
    public sealed record ChatCompletionResponse(
        string Response,
        string? ContextSummary,
        string? LinkedFilePaths,
        string? LinkedSymbols,
        long? TraceId,
        string? Mode = null,
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

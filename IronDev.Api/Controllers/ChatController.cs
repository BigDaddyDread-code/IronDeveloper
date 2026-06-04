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
    private readonly IContextAgentRouteJudge _routeJudge;
    private readonly IProjectChatResponseService _projectChat;
    private readonly IProjectStateReviewService _projectStateReview;

    public ChatController(
        IChatHistoryService chat,
        IChatFeedbackService feedback,
        IContextAgentRouteJudge routeJudge,
        IProjectChatResponseService projectChat,
        IProjectStateReviewService projectStateReview)
    {
        _chat = chat;
        _feedback = feedback;
        _routeJudge = routeJudge;
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

        var isProjectQuestion = string.Equals(mode, ProjectQuestionMode, StringComparison.OrdinalIgnoreCase);

        var routeTraceId = Guid.NewGuid().ToString("N");
        ProjectConversationMode conversationMode;
        IReadOnlyList<string> routeSignals;
        var recentConversationSummary = await BuildRecentConversationSummaryAsync(projectId, request.SessionId, ct);

        if (isProjectQuestion)
        {
            var resolved = await ResolveConversationModeByRouteJudge(
                projectId,
                request,
                recentConversationSummary,
                ct,
                routeTraceId);
            conversationMode = resolved.Mode;
            routeSignals = resolved.RouteSignals;
        }
        else
        {
            conversationMode = explicitMode;
            routeSignals = [
                $"Explicit mode requested in API payload: {explicitMode}",
                $"Exploration/formalization intent was set by client rather than route judge."
            ];
        }

        var includeDetailedMetadata = !isProjectQuestion || conversationMode != ProjectConversationMode.Exploration;
        var answer = await _projectChat.RespondAsync(
            projectId,
            request.Prompt,
            conversationMode,
            routeSignals,
            routeTraceId,
            includeDetailedMetadata,
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
            answer.ShowGovernanceActions,
            answer.GovernanceActions,
            answer.ReasoningTrace,
            answer.DisambiguationQuestion,
            answer.ReasoningSummary,
            answer.DogfoodTraceId,
            null));

    }

    private async Task<(ProjectConversationMode Mode, IReadOnlyList<string> RouteSignals)> ResolveConversationModeByRouteJudge(
        int projectId,
        ChatCompletionRequest request,
        string recentConversationSummary,
        CancellationToken ct,
        string traceGroupId)
    {
        var decision = await _routeJudge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            TraceGroupId = traceGroupId,
            ProjectId = projectId,
            SessionId = request.SessionId ?? 0,
            UserRequest = request.Prompt,
            RecentConversationSummary = recentConversationSummary,
            InitialIntentFromPromptContextBuilder = null
        }, ct);

        var mode = ProjectConversationMode.Exploration;
        var isExplicitFormalization = IsExplicitFormalizationIntent(decision, request.Prompt);

        if (TryResolveConversationModeFromContextMode(decision.ContextMode, out var contextMode))
            mode = contextMode;

        if (decision.NeedsClarification || (isExplicitFormalization && decision.Confidence < 0.6d))
        {
            mode = ProjectConversationMode.Confirmation;
        }
        else if (decision.ContextMode != null &&
                 string.Equals(decision.ContextMode.Trim(), "Formalization", StringComparison.OrdinalIgnoreCase))
        {
            mode = ProjectConversationMode.Formalization;
        }
        else if (isExplicitFormalization || decision.RequestKind is ContextRequestKind.CreateTicket or ContextRequestKind.CreateTicketsFromDiscussion)
        {
            mode = ProjectConversationMode.Formalization;
        }

        var routeSignals = new List<string>
        {
            $"Context agent route decision: Kind={decision.RequestKind}",
            $"Route confidence: {decision.Confidence:0.00}",
            $"Reason: {(string.IsNullOrWhiteSpace(decision.Reason) ? "no explicit reason from judge" : decision.Reason)}",
            $"Formalization intent: {isExplicitFormalization}",
            $"Request kind flags: request='{decision.RequestKind}', allowTicketCreation={decision.AllowTicketCreation}, allowConflictBlocking={decision.AllowConflictBlocking}, allowDeepLookup={decision.AllowDeepLookup}",
            $"Resolution metadata: ContextMode={decision.ContextMode}, UsedConversationResolver={decision.UsedConversationContextResolver}, UsedLlmJudge={decision.UsedLlmJudge}, UsedFallbackRules={decision.UsedFallbackRules}"
        };

        if (decision.NeedsClarification && decision.ClarificationQuestions.Count > 0)
            routeSignals.Add($"Clarification required: {string.Join(" | ", decision.ClarificationQuestions)}");

        if (decision.EvidenceUsed.Count > 0)
            routeSignals.Add($"Evidence used: {string.Join(" | ", decision.EvidenceUsed)}");
        else
            routeSignals.Add("Evidence used: none");

        if (decision.Risks.Count > 0)
            routeSignals.Add($"Risks: {string.Join(" | ", decision.Risks)}");

        return (mode, routeSignals);
    }

    private static bool TryResolveConversationModeFromContextMode(string contextMode, out ProjectConversationMode mode)
    {
        switch (contextMode?.Trim())
        {
            case string value when value.Equals("Formalization", StringComparison.OrdinalIgnoreCase):
                mode = ProjectConversationMode.Formalization;
                return true;
            case string value when value.Equals("Confirmation", StringComparison.OrdinalIgnoreCase):
                mode = ProjectConversationMode.Confirmation;
                return true;
            case string value
                when value.Equals("Exploration", StringComparison.OrdinalIgnoreCase)
                     || value.Equals("ArchitectureDecisionExploration", StringComparison.OrdinalIgnoreCase)
                     || value.Equals("ArchitectureAdvice", StringComparison.OrdinalIgnoreCase)
                     || value.Equals("GeneralDiscussion", StringComparison.OrdinalIgnoreCase):
                mode = ProjectConversationMode.Exploration;
                return true;
            default:
                mode = ProjectConversationMode.Exploration;
                return false;
        }
    }

    private static bool IsExplicitFormalizationIntent(IronDev.Core.Models.ContextAgentRouteDecision decision, string userRequest)
    {
        var explicitMode = string.Equals(decision.ContextMode, "Formalization", StringComparison.OrdinalIgnoreCase);
        if (explicitMode)
            return true;

        var lower = (userRequest ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return false;

        if (decision.RequestKind is ContextRequestKind.CreateTicket or ContextRequestKind.CreateTicketsFromDiscussion)
            return true;

        var markers = new[] { "make this a ticket", "save this as", "formalize", "formalise", "handoff", "create discussion", "create tickets", "turn this into" };
        return markers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase));
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

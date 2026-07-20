using IronDev.Data.Models;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
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
    private readonly IWorkbenchProjectEntryService? _workbenchEntry;
    private readonly ICurrentTenantContext? _tenant;
    private readonly IProjectMembershipService? _memberships;
    private readonly bool _enforceWorkbenchFence;
    private readonly bool _conversationAuthorityEnabled;

    public ChatController(
        IChatHistoryService chat,
        IChatFeedbackService feedback,
        IChatTurnPersistenceService turnPersistence,
        IProjectChatResponseService projectChat,
        IProjectStateReviewService projectStateReview,
        IProjectChatDocumentSourceService documentSources,
        IWorkbenchProjectEntryService? workbenchEntry = null,
        ICurrentTenantContext? tenant = null,
        IConfiguration? configuration = null,
        IProjectMembershipService? memberships = null)
    {
        _chat = chat;
        _feedback = feedback;
        _turnPersistence = turnPersistence;
        _projectChat = projectChat;
        _projectStateReview = projectStateReview;
        _documentSources = documentSources;
        _workbenchEntry = workbenchEntry;
        _tenant = tenant;
        _memberships = memberships;
        _enforceWorkbenchFence = configuration?.GetValue<bool>("WorkbenchV2:Enabled") ?? false;
        _conversationAuthorityEnabled = _enforceWorkbenchFence &&
            (configuration?.GetValue<bool>("WorkbenchV2:ConversationAuthorityEnabled") ?? false);
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions")]
    public async Task<ActionResult<IReadOnlyList<ProjectChatSession>>> GetSessions(
        int projectId,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();
        return Ok(await _chat.GetRecentSessionsAsync(projectId, take, ct));
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}")]
    public async Task<ActionResult<ProjectChatSession>> GetSession(
        int projectId,
        long sessionId,
        CancellationToken ct)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();
        var session = await _chat.GetSessionByIdAsync(projectId, sessionId, ct);
        return session is null ? SessionNotFound() : Ok(session);
    }

    [HttpPost("api/projects/{projectId:int}/chat/sessions")]
    public async Task<ActionResult<long>> SaveSession(int projectId, SaveProjectChatSessionRequest request, CancellationToken ct)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();

        if (request.ProjectId != 0 && request.ProjectId != projectId)
            return BadRequest(new { message = "Session projectId must match route project id." });
        if (request.Id is < 0)
            return BadRequest(new { message = "Session id cannot be negative." });
        if (_conversationAuthorityEnabled && request.Id is > 0)
            return ConversationAuthorityRequired();

        var session = new ProjectChatSession
        {
            Id = request.Id ?? 0,
            ProjectId = projectId,
            Summary = request.Summary
        };
        if (request.Title is not null)
            session.Title = request.Title;

        var actorUserId = CurrentActorUserId();
        try
        {
            // A retry after a committed-but-ambiguous response must replay before
            // lease fencing. Access and payload scope are still revalidated first.
            if (session.Id == 0 &&
                _enforceWorkbenchFence &&
                actorUserId > 0 &&
                request.ClientOperationId != Guid.Empty)
            {
                var replay = await _chat.TryReplaySessionCreateAsync(
                    session,
                    actorUserId,
                    request.ClientOperationId,
                    ct);
                if (replay.HasValue)
                    return replay.Value;
            }

            if (session.Id == 0 && _enforceWorkbenchFence)
            {
                if (request.ClientOperationId == Guid.Empty ||
                    request.WorkbenchSessionId <= 0 ||
                    request.LeaseEpoch <= 0)
                {
                    return LeaseFenceRejected();
                }

                return await _chat.CreateSessionIdempotentlyAsync(
                    session,
                    actorUserId,
                    request.ClientOperationId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    ct);
            }

            var fenceRejection = await ValidateFenceAsync(
                projectId,
                request.WorkbenchSessionId,
                request.LeaseEpoch,
                request.ClientOperationId,
                ct);
            if (fenceRejection is not null) return fenceRejection;

            return await _chat.SaveSessionAsync(session, ct);
        }
        catch (ProjectStartOperationMismatchException exception)
        {
            return Conflict(new
            {
                error = ProjectStartOperationMismatchException.ErrorCode,
                message = exception.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return SessionNotFound();
        }
        catch (WorkbenchLeaseFenceException)
        {
            return LeaseFenceRejected();
        }
    }

    [HttpDelete("api/projects/{projectId:int}/chat/sessions/{sessionId:long}")]
    public async Task<IActionResult> DeleteSession(int projectId, long sessionId, CancellationToken ct)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();
        if (_conversationAuthorityEnabled)
            return ConversationAuthorityRequired();

        return await _chat.DeleteSessionAsync(projectId, sessionId, ct)
            ? NoContent()
            : SessionNotFound();
    }

    [HttpGet("api/projects/{projectId:int}/chat/document-sources")]
    public async Task<ActionResult<IReadOnlyList<ChatDocumentSource>>> GetDocumentSources(
        int projectId,
        CancellationToken ct)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();
        return Ok(await _documentSources.GetAvailableSourcesAsync(projectId, ct));
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessage>>> GetMessages(
        int projectId,
        long sessionId,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();

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

        return Ok(messages);
    }

    [HttpGet("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages/{messageId:long}/audit")]
    public async Task<ActionResult<ChatTurnAuditResponse>> GetMessageAudit(
        int projectId,
        long sessionId,
        long messageId,
        CancellationToken ct = default)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();

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

    [HttpGet("api/projects/{projectId:int}/chat/messages/{messageId:long}")]
    public async Task<ActionResult<ChatMessage>> GetMessage(
        int projectId,
        long messageId,
        CancellationToken ct = default)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();

        var message = await _chat.GetMessageByIdAsync(messageId, projectId, ct).ConfigureAwait(false);
        return message is null ? NotFound() : Ok(message);
    }

    [HttpPost("api/projects/{projectId:int}/chat/sessions/{sessionId:long}/messages")]
    public async Task<ActionResult<long>> SaveMessage(int projectId, long sessionId, SaveProjectChatMessageRequest request, CancellationToken ct)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();

        if (_conversationAuthorityEnabled)
            return ConversationAuthorityRequired();

        var fenceRejection = await ValidateFenceAsync(projectId, request.WorkbenchSessionId, request.LeaseEpoch, request.ClientOperationId, ct);
        if (fenceRejection is not null) return fenceRejection;

        if (request.ProjectId != 0 && request.ProjectId != projectId)
            return BadRequest(new { message = "Message projectId must match route project id." });

        if (request.ChatSessionId != 0 && request.ChatSessionId != sessionId)
            return BadRequest(new { message = "Message chat session id must match route session id." });

        var message = new ChatMessage
        {
            ProjectId = request.ProjectId,
            ChatSessionId = request.ChatSessionId,
            Role = request.Role ?? string.Empty,
            Message = request.Message ?? string.Empty,
            Tags = request.Tags,
            ContextSummary = request.ContextSummary,
            LinkedFilePaths = request.LinkedFilePaths,
            LinkedSymbols = request.LinkedSymbols,
            ReplyToMessageId = request.ReplyToMessageId,
            DocumentVersionIds = request.DocumentVersionIds ?? [],
            SourceAttachedBy = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
        };

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
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();

        if (_conversationAuthorityEnabled)
            return ConversationAuthorityRequired();

        var fenceRejection = await ValidateFenceAsync(projectId, request.WorkbenchSessionId, request.LeaseEpoch, request.ClientOperationId, ct);
        if (fenceRejection is not null) return fenceRejection;

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
                MemoryRetrievalRequestContext.ForProjectChat(
                    int.TryParse(User.FindFirst("tenant_id")?.Value, out var tenantId) ? tenantId : 0,
                    projectId,
                    int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var actorUserId) ? actorUserId : 0,
                    "ProjectChatApi"),
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
        long? SourceMessageId = null,
        long WorkbenchSessionId = 0,
        long LeaseEpoch = 0,
        Guid ClientOperationId = default);
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

    public sealed record SaveProjectChatSessionRequest(
        long? Id,
        int ProjectId,
        string? Title,
        string? Summary,
        long WorkbenchSessionId = 0,
        long LeaseEpoch = 0,
        Guid ClientOperationId = default);

    public sealed record SaveProjectChatMessageRequest(
        int ProjectId,
        long ChatSessionId,
        string? Role,
        string? Message,
        string? Tags,
        string? ContextSummary,
        string? LinkedFilePaths,
        string? LinkedSymbols,
        long? ReplyToMessageId,
        IReadOnlyList<long>? DocumentVersionIds,
        long WorkbenchSessionId = 0,
        long LeaseEpoch = 0,
        Guid ClientOperationId = default);

    [HttpPost("api/projects/{projectId:int}/chat/feedback")]
    public async Task<ActionResult<long>> SaveFeedback(
        int projectId,
        ChatMessageFeedback feedback,
        CancellationToken ct)
    {
        if (!await HasProjectAccessAsync(projectId, ct)) return ProjectNotFound();
        if (feedback.ProjectId != 0 && feedback.ProjectId != projectId)
            return BadRequest(new { message = "Feedback projectId must match route project id." });

        var message = await _chat.GetMessageByIdAsync(feedback.ChatMessageId, projectId, ct);
        if (message is null ||
            (feedback.ChatSessionId.HasValue && feedback.ChatSessionId.Value != message.ChatSessionId))
        {
            return NotFound(new { error = "Chat message not found in this project." });
        }

        feedback.ProjectId = projectId;
        feedback.ChatSessionId = message.ChatSessionId;
        return await _feedback.SaveFeedbackAsync(feedback, ct);
    }

    private async Task<ActionResult?> ValidateFenceAsync(
        int projectId,
        long workbenchSessionId,
        long leaseEpoch,
        Guid clientOperationId,
        CancellationToken cancellationToken)
    {
        if (!_enforceWorkbenchFence) return null;

        var actorUserId = int.TryParse(
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
            out var parsedActorUserId)
            ? parsedActorUserId
            : 0;
        if (clientOperationId == Guid.Empty ||
            workbenchSessionId <= 0 ||
            leaseEpoch <= 0 ||
            _workbenchEntry is null ||
            _tenant is null ||
            !await _workbenchEntry.ValidateAndRenewCurrentWriteLeaseAsync(
                _tenant.TenantId,
                actorUserId,
                projectId,
                workbenchSessionId,
                leaseEpoch,
                cancellationToken))
        {
            return LeaseFenceRejected();
        }

        return null;
    }

    private ConflictObjectResult ConversationAuthorityRequired() =>
        Conflict(new
        {
            error = "workbench_conversation_authority_required",
            message = "Workbench V2 conversation mutations must use the AgentRun authority."
        });

    private ConflictObjectResult LeaseFenceRejected() =>
        Conflict(new
        {
            error = WorkbenchLeaseFenceException.ErrorCode,
            message = new WorkbenchLeaseFenceException().Message
        });

    private int CurrentActorUserId() =>
        int.TryParse(
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
            out var actorUserId)
            ? actorUserId
            : 0;

    private async Task<bool> HasProjectAccessAsync(int projectId, CancellationToken cancellationToken)
    {
        var actorUserId = CurrentActorUserId();
        return actorUserId > 0 &&
            _memberships is not null &&
            _tenant is not null &&
            await _memberships.HasAccessAsync(
                _tenant.TenantId,
                projectId,
                actorUserId,
                cancellationToken);
    }

    private NotFoundObjectResult ProjectNotFound() =>
        NotFound(new { error = "Project not found or you no longer have access." });

    private NotFoundObjectResult SessionNotFound() =>
        NotFound(new { error = "Chat session not found in this project." });

}

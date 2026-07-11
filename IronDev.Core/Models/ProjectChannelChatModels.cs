namespace IronDev.Core.Models;

public sealed record ProjectChannelChatListResponse(
    bool CanCreateChannels,
    IReadOnlyList<ProjectChannelChatSummary> Channels,
    string Boundary);

public sealed record ProjectChannelChatSummary(
    long ChannelId,
    string Name,
    string Slug,
    string? Description,
    string ChannelKind,
    string Visibility,
    int MemberCount,
    string? CurrentUserRole,
    string? CurrentUserNotificationLevel,
    int UnreadCount,
    long? LastReadMessageId,
    DateTime? LastReadUtc,
    bool CanPostMessages,
    string Boundary);

public sealed record ProjectChannelChatDetail(
    ProjectChannelChatSummary Channel,
    IReadOnlyList<ProjectChannelChatMessage> Messages,
    IReadOnlyList<ProjectChannelAssistantTurnState> AssistantTurns,
    ProjectChannelReadState ReadState,
    ProjectChannelPresenceState Presence,
    string AssistantParticipationStatus,
    string Boundary);

public sealed record ProjectChannelReadState(
    int UnreadCount,
    long? LastReadMessageId,
    DateTime? LastReadUtc,
    string NotificationLevel,
    string Boundary);

public sealed record ProjectChannelPresenceState(
    string Status,
    int? ActiveViewerCount,
    string Reason,
    string Boundary);

public sealed record ProjectChannelChatMessage(
    long MessageId,
    int? AuthorUserId,
    string AuthorDisplayName,
    string Role,
    string Message,
    string MessageFormat,
    string Status,
    long? ReplyToMessageId,
    long? ThreadRootMessageId,
    DateTime CreatedUtc,
    DateTime? EditedUtc,
    string Boundary);

public sealed record ProjectChannelAssistantTurnState(
    long TurnId,
    long ChannelId,
    long RequestMessageId,
    long? ResponseMessageId,
    int RequestedByUserId,
    string RequestedByDisplayName,
    string Prompt,
    string? Answer,
    string? Mode,
    double? ModeConfidence,
    string? ModeReason,
    string? ContextSummary,
    string? LinkedFilePaths,
    string? LinkedSymbols,
    string? LinkedDocumentIds,
    string? DogfoodTraceId,
    long? TraceId,
    string Status,
    string? FailureReason,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    string Boundary);

public sealed record ProjectChannelPostMessageResult(
    ProjectChannelChatMessage Message,
    ProjectChannelAssistantTurnState? AssistantTurn);

public sealed record ProjectChannelAssistantCompletionResult(
    ProjectChannelAssistantTurnState AssistantTurn,
    ProjectChannelChatMessage? ResponseMessage);

public enum ProjectChannelChatMutationStatus
{
    Succeeded = 0,
    NotFound = 1,
    ReadOnly = 2,
    DuplicateName = 3
}

public sealed record ProjectChannelChatMutationResult(
    ProjectChannelChatMutationStatus Status,
    ProjectChannelChatMessage? Message = null,
    ProjectChannelChatSummary? Channel = null,
    ProjectChannelReadState? ReadState = null,
    ProjectChannelPostMessageResult? PostMessage = null,
    ProjectChannelAssistantCompletionResult? AssistantCompletion = null);

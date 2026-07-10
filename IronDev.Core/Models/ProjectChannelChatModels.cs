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
    bool CanPostMessages,
    string Boundary);

public sealed record ProjectChannelChatDetail(
    ProjectChannelChatSummary Channel,
    IReadOnlyList<ProjectChannelChatMessage> Messages,
    string AssistantParticipationStatus,
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

public enum ProjectChannelChatMutationStatus
{
    Succeeded = 0,
    NotFound = 1,
    ReadOnly = 2,
    AssistantInvocationNotImplemented = 3,
    DuplicateName = 4
}

public sealed record ProjectChannelChatMutationResult(
    ProjectChannelChatMutationStatus Status,
    ProjectChannelChatMessage? Message = null,
    ProjectChannelChatSummary? Channel = null);

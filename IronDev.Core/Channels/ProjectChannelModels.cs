namespace IronDev.Core.Channels;

public static class ProjectChannelBoundaries
{
    public const string Channel =
        "A project channel is collaboration state. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";

    public const string Message =
        "A channel message is conversation. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";

    public const string ContextLink =
        "A context link is a pointer for navigation and grounding. It is not approval, evidence validation, authority, or permission to mutate the linked object.";

    public const string AssistantTurn =
        "A channel assistant answer is advisory project context. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";

    public const string ReadMarker =
        "A channel read marker is unread-count convenience. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";

    public const string Presence =
        "Channel presence reports observed viewer state only. It is not membership, assignment, approval, authority, or a workflow blocker.";

    public const string Mention =
        "A channel mention is collaboration attention state. It is not membership, approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";

    public const string Notification =
        "A product notification is attention state. It is not approval, assignment, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";

    public const string Pin =
        "A pinned channel message is navigation convenience. It is not approval, policy, authority, evidence, source apply, workflow continuation, release readiness, or deployment readiness.";
}

public static class ProjectChannelKinds
{
    public const string General = "General";
    public const string Architecture = "Architecture";
    public const string Tickets = "Tickets";
    public const string BuildRuns = "BuildRuns";
    public const string Review = "Review";
    public const string Release = "Release";
    public const string Custom = "Custom";
    public const string WorkItem = "WorkItem";
    public const string Run = "Run";
    public const string Batch = "Batch";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        General,
        Architecture,
        Tickets,
        BuildRuns,
        Review,
        Release,
        Custom,
        WorkItem,
        Run,
        Batch
    };
}

public static class ProjectChannelVisibility
{
    public const string Project = "Project";
    public const string MembersOnly = "MembersOnly";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Project,
        MembersOnly
    };
}

public static class ProjectChannelStatus
{
    public const string Active = "Active";
    public const string Archived = "Archived";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Active,
        Archived
    };
}

public static class ProjectChannelRoles
{
    public const string Owner = "Owner";
    public const string Moderator = "Moderator";
    public const string Member = "Member";
    public const string ReadOnly = "ReadOnly";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Owner,
        Moderator,
        Member,
        ReadOnly
    };
}

public static class ProjectChannelNotificationLevels
{
    public const string All = "All";
    public const string Mentions = "Mentions";
    public const string None = "None";

    public static readonly IReadOnlySet<string> Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        All,
        Mentions,
        None
    };
}

public static class ProjectChannelMessageRoles
{
    public const string User = "User";
    public const string Assistant = "Assistant";
    public const string SystemNotice = "SystemNotice";
    public const string EventLink = "EventLink";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        User,
        Assistant,
        SystemNotice,
        EventLink
    };
}

public static class ProjectChannelMessageFormats
{
    public const string PlainText = "PlainText";
    public const string Markdown = "Markdown";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PlainText,
        Markdown
    };
}

public static class ProjectChannelMessageStatus
{
    public const string Active = "Active";
    public const string Edited = "Edited";
    public const string Deleted = "Deleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Active,
        Edited,
        Deleted
    };
}

public static class ProjectChannelContextLinkKinds
{
    public const string Ticket = "Ticket";
    public const string Run = "Run";
    public const string Batch = "Batch";
    public const string BatchMap = "BatchMap";
    public const string BatchPlan = "BatchPlan";
    public const string CriticPackage = "CriticPackage";
    public const string Finding = "Finding";
    public const string ApprovalPackage = "ApprovalPackage";
    public const string AcceptedApproval = "AcceptedApproval";
    public const string PatchArtifact = "PatchArtifact";
    public const string DryRunReceipt = "DryRunReceipt";
    public const string SourceApplyReview = "SourceApplyReview";
    public const string ReleaseCandidate = "ReleaseCandidate";
    public const string Document = "Document";
    public const string Decision = "Decision";
    public const string File = "File";
    public const string Symbol = "Symbol";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Ticket,
        Run,
        Batch,
        BatchMap,
        BatchPlan,
        CriticPackage,
        Finding,
        ApprovalPackage,
        AcceptedApproval,
        PatchArtifact,
        DryRunReceipt,
        SourceApplyReview,
        ReleaseCandidate,
        Document,
        Decision,
        File,
        Symbol
    };
}

public static class ProjectChannelContextLinkSources
{
    public const string UserLinked = "UserLinked";
    public const string AssistantLinked = "AssistantLinked";
    public const string SystemLinked = "SystemLinked";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UserLinked,
        AssistantLinked,
        SystemLinked
    };
}

public static class ProjectChannelAssistantTurnStatus
{
    public const string Requested = "Requested";
    public const string Answered = "Answered";
    public const string Failed = "Failed";
    public const string Refused = "Refused";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Requested,
        Answered,
        Failed,
        Refused
    };
}

public sealed class ProjectChannel
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ChannelKind { get; init; } = ProjectChannelKinds.Custom;
    public string Visibility { get; init; } = ProjectChannelVisibility.Project;
    public string Status { get; init; } = ProjectChannelStatus.Active;
    public int CreatedByUserId { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? UpdatedUtc { get; init; }
    public DateTime? ArchivedUtc { get; init; }
    public long? LinkedTicketId { get; init; }
    public string? LinkedRunId { get; init; }
    public string? LinkedBatchId { get; init; }
    public string? LinkedReviewId { get; init; }
    public string? LinkedReleaseCandidateRef { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.Channel;
}

public sealed class ProjectChannelMember
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public int UserId { get; init; }
    public string ChannelRole { get; init; } = ProjectChannelRoles.Member;
    public string NotificationLevel { get; init; } = ProjectChannelNotificationLevels.Mentions;
    public string Status { get; init; } = ProjectChannelStatus.Active;
    public int AddedByUserId { get; init; }
    public DateTime AddedUtc { get; init; }
    public DateTime? RemovedUtc { get; init; }
    public string Boundary { get; init; } =
        "Channel membership controls channel visibility and moderation only. It is not approval, authority, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.";
}

public sealed class ProjectChannelMessage
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public int? AuthorUserId { get; init; }
    public string Role { get; init; } = ProjectChannelMessageRoles.User;
    public string Message { get; init; } = string.Empty;
    public string MessageFormat { get; init; } = ProjectChannelMessageFormats.Markdown;
    public string Status { get; init; } = ProjectChannelMessageStatus.Active;
    public long? ReplyToMessageId { get; init; }
    public long? ThreadRootMessageId { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? EditedUtc { get; init; }
    public DateTime? DeletedUtc { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.Message;
}

public sealed class ProjectChannelMessageContextLink
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public long MessageId { get; init; }
    public string LinkKind { get; init; } = string.Empty;
    public string LinkId { get; init; } = string.Empty;
    public string? LinkLabel { get; init; }
    public string Source { get; init; } = ProjectChannelContextLinkSources.UserLinked;
    public DateTime CreatedUtc { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.ContextLink;
}

public sealed class ProjectChannelMessageMention
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public long MessageId { get; init; }
    public int MentionedUserId { get; init; }
    public int MentionedByUserId { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.Mention;
}

public sealed class ProjectNotification
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public int RecipientUserId { get; init; }
    public int? ActorUserId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public long? ChannelId { get; init; }
    public long? MessageId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Status { get; init; } = "Unread";
    public DateTime CreatedUtc { get; init; }
    public DateTime? ReadUtc { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.Notification;
}

public sealed class ProjectChannelAssistantTurn
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public long RequestMessageId { get; init; }
    public long? ResponseMessageId { get; init; }
    public int RequestedByUserId { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public string? Answer { get; init; }
    public string? Mode { get; init; }
    public double? ModeConfidence { get; init; }
    public string? ModeReason { get; init; }
    public string? ContextSummary { get; init; }
    public string? LinkedFilePaths { get; init; }
    public string? LinkedSymbols { get; init; }
    public string? LinkedDocumentIds { get; init; }
    public string? LinkedTicketIds { get; init; }
    public string? LinkedRunIds { get; init; }
    public string? RouteTraceId { get; init; }
    public string? DogfoodTraceId { get; init; }
    public long? TraceId { get; init; }
    public string Status { get; init; } = ProjectChannelAssistantTurnStatus.Requested;
    public string? FailureReason { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.AssistantTurn;
}

public sealed class ProjectChannelMessageRead
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public int UserId { get; init; }
    public long? LastReadMessageId { get; init; }
    public DateTime LastReadUtc { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.ReadMarker;
}

public sealed class ProjectChannelPin
{
    public long Id { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public long ChannelId { get; init; }
    public long MessageId { get; init; }
    public int PinnedByUserId { get; init; }
    public DateTime PinnedUtc { get; init; }
    public DateTime? UnpinnedUtc { get; init; }
    public string Boundary { get; init; } = ProjectChannelBoundaries.Pin;
}

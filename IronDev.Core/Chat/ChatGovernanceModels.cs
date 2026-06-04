namespace IronDev.Core.Chat;

public enum ChatGovernanceMode
{
    Exploration,
    Formalization,
    Confirmation
}

public enum ChatPromptTemplate
{
    Exploration,
    Formalization,
    Confirmation
}

public enum ChatClarificationKind
{
    None,
    GeneralScope,
    ProductScope,
    MissingProjectContext,
    GovernanceIntent,
    SafetyOrRisk
}

public sealed record ChatClarificationState(
    bool Required,
    ChatClarificationKind Kind,
    IReadOnlyList<string> Questions,
    string? Reason)
{
    public static ChatClarificationState None { get; } =
        new(false, ChatClarificationKind.None, Array.Empty<string>(), null);
}

public sealed record ChatContextState(
    bool RequiresClarification,
    IReadOnlyList<string> ClarificationQuestions,
    string? ContextSummary);

public sealed record ChatModeDecision(
    ChatGovernanceMode Mode,
    double Confidence,
    string Reason);

public sealed record ChatModeClassificationRequest(
    string UserMessage,
    string RecentConversationSummary,
    Models.ContextAgentRouteDecision RouteHint,
    string? ProjectSummary,
    bool ContextRequiresClarification,
    ChatGovernanceMode? ExplicitMode);

public sealed record ChatClarificationClassificationRequest(
    string UserMessage,
    string RecentConversationSummary,
    ChatContextState ContextState,
    ChatModeDecision ModeDecision,
    string? ProjectSummary,
    Models.ContextAgentRouteDecision RouteHint);

public sealed record ChatGovernanceGate(
    ChatGovernanceMode Mode,
    bool CanSaveDiscussion,
    bool CanCreateTicket,
    bool CanViewSources,
    bool CanCopyMarkdown,
    string Reason,
    double Confidence,
    IReadOnlyList<string> GovernanceActions)
{
    public bool ShowGovernanceActions =>
        CanSaveDiscussion ||
        CanCreateTicket ||
        CanViewSources ||
        CanCopyMarkdown;

    public static ChatGovernanceGate FromDecision(ChatModeDecision decision)
    {
        var formalization = decision.Mode == ChatGovernanceMode.Formalization;

        return new ChatGovernanceGate(
            decision.Mode,
            CanSaveDiscussion: formalization,
            CanCreateTicket: formalization,
            CanViewSources: formalization,
            CanCopyMarkdown: formalization,
            decision.Reason,
            decision.Confidence,
            formalization
                ? DefaultFormalizationActions
                : Array.Empty<string>());
    }

    private static readonly IReadOnlyList<string> DefaultFormalizationActions =
    [
        "Save this response as a Discussion.",
        "Create a Ticket from the saved Discussion."
    ];
}

public sealed record ChatTurnEnvelope(
    int V,
    ChatGovernanceMode Mode,
    double ModeConfidence,
    string ModeReason,
    ChatClarificationState Clarification,
    ChatGovernanceGate Gate,
    string? RouteTraceId,
    string? DogfoodTraceId);

public sealed record ChatTurnPersistenceRequest(
    long ChatMessageId,
    int TenantId,
    int ProjectId,
    long ChatSessionId,
    string Role,
    string? Tags,
    string? ContextSummary,
    string? LinkedFilePaths,
    string? LinkedSymbols);

public sealed record ChatTurnPersistenceSnapshot(
    long ChatMessageId,
    ChatGovernanceMode Mode,
    double ModeConfidence,
    string ModeReason,
    ChatClarificationState Clarification,
    ChatGovernanceGate Gate,
    string? RouteTraceId,
    string? DogfoodTraceId,
    string? ContextSummary,
    string? LinkedFilePaths,
    string? LinkedSymbols);

public sealed record ProjectChatResponseResult(
    string Response,
    string Mode,
    double? ModeConfidence,
    string? ModeReason,
    ChatClarificationState Clarification,
    ChatGovernanceGate Gate,
    IReadOnlyList<string>? ReasoningTrace = null,
    string? DisambiguationQuestion = null,
    string? ReasoningSummary = null,
    string? ContextSummary = null,
    string? LinkedFilePaths = null,
    string? LinkedSymbols = null,
    string? DogfoodTraceId = null,
    string? DogfoodTracePath = null,
    long? TraceId = null);

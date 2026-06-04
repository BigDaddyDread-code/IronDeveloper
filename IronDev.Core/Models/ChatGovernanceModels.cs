namespace IronDev.Core.Models;

public enum ChatGovernanceMode
{
    Exploration,
    Formalization,
    Confirmation
}

public sealed record ChatModeDecision(
    ChatGovernanceMode Mode,
    double Confidence,
    string Reason);

public sealed record ChatModeClassificationRequest(
    string UserMessage,
    string RecentConversationSummary,
    ContextAgentRouteDecision RouteHint,
    string? ProjectSummary,
    bool ContextRequiresClarification,
    ChatGovernanceMode? ExplicitMode);

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
    bool ShowGovernanceActions,
    IReadOnlyList<string> GovernanceActions,
    string? RouteTraceId,
    string? DogfoodTraceId);

public sealed record ProjectChatResponseResult(
    string Response,
    string Mode,
    double? ModeConfidence,
    string? ModeReason,
    bool? ShowGovernanceActions = null,
    IReadOnlyList<string>? GovernanceActions = null,
    IReadOnlyList<string>? ReasoningTrace = null,
    string? DisambiguationQuestion = null,
    string? ReasoningSummary = null,
    string? ContextSummary = null,
    string? LinkedFilePaths = null,
    string? LinkedSymbols = null,
    string? DogfoodTraceId = null,
    string? DogfoodTracePath = null,
    long? TraceId = null);

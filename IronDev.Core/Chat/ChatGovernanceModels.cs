using System.Text.Json.Serialization;
using IronDev.Core.Models;

namespace IronDev.Core.Chat;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatGovernanceMode
{
    Exploration,
    Formalization,
    Confirmation
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatAuditSource
{
    NormalizedRows,
    TagsFallback,
    None
}

public enum ChatPromptTemplate
{
    Exploration,
    Formalization,
    Confirmation
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatContextStateOrigin
{
    Unknown,
    ProjectChatResponseCompiler,
    ExternalInput
}

[JsonConverter(typeof(JsonStringEnumConverter))]
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
    string? ContextSummary,
    string CurrentUserMessage = "",
    IReadOnlyList<RecentChatTurn>? RecentTurns = null,
    ActiveArtifactContext? ActiveArtifact = null,
    IReadOnlyList<MemoryEvidence>? SemanticEvidence = null,
    IReadOnlyList<AvailableSkillHint>? AvailableSkillHints = null,
    bool EpisodicMemoryEnabled = false,
    ChatContextStateOrigin Origin = ChatContextStateOrigin.Unknown,
    ChatClarificationState? ClassifiedClarification = null);

public sealed record RecentChatTurn(
    string Role,
    string Message,
    DateTimeOffset? Timestamp = null);

public sealed record ActiveArtifactContext(
    string ArtifactType = "",
    string? ArtifactId = null,
    string? Title = null,
    string? Summary = null);

public sealed record MemoryEvidence(
    string SourceId = "",
    string SourceType = "",
    string Title = "",
    string Excerpt = "",
    bool IsCurrent = false,
    double RelevanceScore = 0,
    string AuthorityLevel = "",
    string UsedFor = "ContextOnly",
    string? StalenessReason = null,
    string? SupersededBySourceId = null,
    string? RetrievalTraceId = null,
    int? RetrievalRank = null,
    string? RetrievalQuery = null,
    string? MatchReason = null,
    double? VectorSimilarity = null);

public sealed record AvailableSkillHint(
    string SkillId = "",
    string DisplayName = "",
    string CapabilitySummary = "");

public sealed record ChatModeDecision(
    ChatGovernanceMode Mode,
    double Confidence,
    string Reason);

public sealed record EffectiveChatRoute
{
    public required ChatGovernanceMode Mode { get; init; }
    public required Models.ContextRequestKind RequestKind { get; init; }
    public required string Source { get; init; }
    public required double Confidence { get; init; }
    public required string Reason { get; init; }
    public required Models.ContextAgentRouteDecision RouteDecision { get; init; }

    public string OriginalUserRequest { get; init; } = string.Empty;
    public string EffectiveWorkText { get; init; } = string.Empty;
    public string? ConversationTopic { get; init; }
    public string? ActiveArtifactType { get; init; }
    public long? ActiveTicketId { get; init; }
    public long? ActiveDecisionId { get; init; }
    public long? ActivePlanId { get; init; }
    public bool AllowsDecisionTagOutput { get; init; }
    public bool AllowsTicketDrafting { get; init; }
    public bool AllowsDecisionCapture { get; init; }
    public bool RequiresClarification { get; init; }
    public IReadOnlyList<string> InputsUsed { get; init; } = Array.Empty<string>();

    public static EffectiveChatRoute FromRouteDecision(
        Models.ContextAgentRouteDecision routeDecision,
        ChatGovernanceMode mode,
        string source,
        IReadOnlyList<string>? inputsUsed = null)
    {
        return new EffectiveChatRoute
        {
            Mode = mode,
            RequestKind = routeDecision.RequestKind,
            Source = source,
            Confidence = routeDecision.Confidence,
            Reason = string.IsNullOrWhiteSpace(routeDecision.Reason)
                ? "Effective route derived from chat route decision."
                : routeDecision.Reason,
            RouteDecision = routeDecision,
            OriginalUserRequest = routeDecision.OriginalUserRequest,
            EffectiveWorkText = routeDecision.EffectiveWorkText,
            AllowsDecisionTagOutput = AllowsDecisionTagOutputFor(mode, routeDecision.RequestKind),
            AllowsTicketDrafting = routeDecision.AllowTicketCreation,
            AllowsDecisionCapture = AllowsDecisionCaptureFor(mode, routeDecision.RequestKind),
            RequiresClarification = routeDecision.NeedsClarification,
            InputsUsed = inputsUsed ?? Array.Empty<string>()
        };
    }

    public ChatModeDecision ToModeDecision() => new(Mode, Confidence, Reason);

    public static ChatGovernanceMode InferMode(Models.ContextAgentRouteDecision routeDecision)
    {
        if (routeDecision.NeedsClarification)
            return ChatGovernanceMode.Confirmation;

        return routeDecision.RequestKind switch
        {
            Models.ContextRequestKind.CreateTicket => ChatGovernanceMode.Formalization,
            Models.ContextRequestKind.CreateTicketsFromDiscussion => ChatGovernanceMode.Formalization,
            Models.ContextRequestKind.BuildTicket => ChatGovernanceMode.Formalization,
            Models.ContextRequestKind.ArchitectureDecisionExploration => ChatGovernanceMode.Formalization,
            _ => ChatGovernanceMode.Exploration
        };
    }

    private static bool AllowsDecisionTagOutputFor(ChatGovernanceMode mode, Models.ContextRequestKind requestKind) =>
        mode == ChatGovernanceMode.Formalization &&
        requestKind == Models.ContextRequestKind.ArchitectureDecisionExploration;

    private static bool AllowsDecisionCaptureFor(ChatGovernanceMode mode, Models.ContextRequestKind requestKind) =>
        mode == ChatGovernanceMode.Formalization &&
        requestKind is Models.ContextRequestKind.ArchitectureDecisionExploration
            or Models.ContextRequestKind.CreateTicketsFromDiscussion;
}

public sealed record ChatRouteChallenge(
    ChatGovernanceMode SuggestedMode,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    Models.ContextRequestKind SuggestedRequestKind,
    double Confidence,
    string Reason);

public sealed record BaWorkingDraft
{
    public string? CandidateTitle { get; init; }
    public string? Problem { get; init; }
    public string? ProposedChange { get; init; }
    public IReadOnlyList<string> BusinessRules { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OpenQuestions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceMessageIds { get; init; } = Array.Empty<string>();
    public double Confidence { get; init; }
    public bool ReadyForConfirmation { get; init; }
    public IReadOnlyList<string> PotentialConflicts { get; init; } = Array.Empty<string>();
    public string? SuggestedArtifact { get; init; }
    public string Boundary { get; init; } = "A BA draft is shaped evidence, not a ticket, decision, approval, continuation, apply, commit, push, release, or deploy.";
}

public sealed record ChatBaDraftRequest(
    int ProjectId,
    long? SessionId,
    string Prompt,
    string RecentConversationSummary,
    EffectiveChatRoute EffectiveRoute);

public sealed record ConfirmBaWorkingDraftRequest(
    long? SourceChatSessionId,
    BaWorkingDraft Draft);

public sealed record ChatModeClassificationRequest(
    string UserMessage,
    string RecentConversationSummary,
    Models.ContextAgentRouteDecision RouteHint,
    string? ProjectSummary,
    bool ContextRequiresClarification,
    ChatGovernanceMode? ExplicitMode,
    ChatContextState? ContextState = null);

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
    string? DogfoodTraceId,
    string? RouteSource = null,
    ChatRouteChallenge? RouteChallenge = null,
    BaWorkingDraft? BaDraft = null);

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
    string? LinkedSymbols,
    bool IsFallbackEvidence = false,
    string? RouteSource = null,
    ChatRouteChallenge? RouteChallenge = null,
    BaWorkingDraft? BaDraft = null);

public sealed record ChatTurnAuditResponse(
    long ChatMessageId,
    ChatAuditSource Source,
    ChatGovernanceMode Mode,
    double ModeConfidence,
    string ModeReason,
    ChatClarificationState Clarification,
    ChatGovernanceGate Gate,
    string? RouteTraceId,
    string? DogfoodTraceId,
    string? ContextSummary,
    string? LinkedFilePaths,
    string? LinkedSymbols,
    bool IsFallbackEvidence,
    string? RouteSource = null,
    ChatRouteChallenge? RouteChallenge = null,
    BaWorkingDraft? BaDraft = null);

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
    long? TraceId = null,
    string? RouteSource = null,
    ChatRouteChallenge? RouteChallenge = null,
    BaWorkingDraft? BaDraft = null,
    IReadOnlyList<ChatDocumentSource>? DocumentSources = null);

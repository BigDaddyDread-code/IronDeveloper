using IronDev.Core.Chat;
using IronDev.Core.Models;

namespace IronDev.UnitTests.Chat;

internal static class ChatModeClassificationPromptBuilderTestFixtures
{
    internal const string DefaultUserMessage = "what should we do next?";
    internal const string DefaultProjectSummary = "Test project";

    internal static ChatModeClassificationRequest Request(
        string userMessage = DefaultUserMessage,
        ChatGovernanceMode? explicitMode = null,
        ContextRequestKind routeKind = ContextRequestKind.GeneralChat,
        string contextModeHint = "Exploration",
        bool allowTicketCreation = false,
        bool contextRequiresClarification = false,
        bool routeNeedsClarification = false,
        double routeConfidence = 0.72,
        string routeReason = "Test route hint.",
        string recentConversationSummary = "",
        string? projectSummary = DefaultProjectSummary,
        ChatContextState? contextState = null) =>
        new(
            UserMessage: userMessage,
            RecentConversationSummary: recentConversationSummary,
            RouteHint: new ContextAgentRouteDecision
            {
                OriginalUserRequest = userMessage,
                EffectiveWorkText = userMessage,
                RequestKind = routeKind,
                Confidence = routeConfidence,
                Reason = routeReason,
                ContextModeHint = contextModeHint,
                AllowTicketCreation = allowTicketCreation,
                NeedsClarification = routeNeedsClarification
            },
            ProjectSummary: projectSummary,
            ContextRequiresClarification: contextRequiresClarification,
            ExplicitMode: explicitMode,
            ContextState: contextState);

    internal static string Prompt(
        string userMessage = DefaultUserMessage,
        ChatGovernanceMode? explicitMode = null,
        ContextRequestKind routeKind = ContextRequestKind.GeneralChat,
        string contextModeHint = "Exploration",
        bool allowTicketCreation = false,
        bool contextRequiresClarification = false,
        bool routeNeedsClarification = false,
        double routeConfidence = 0.72,
        string routeReason = "Test route hint.",
        string recentConversationSummary = "",
        string? projectSummary = DefaultProjectSummary,
        ChatContextState? contextState = null) =>
        ChatModeClassificationPromptBuilder.BuildPrompt(Request(
            userMessage,
            explicitMode,
            routeKind,
            contextModeHint,
            allowTicketCreation,
            contextRequiresClarification,
            routeNeedsClarification,
            routeConfidence,
            routeReason,
            recentConversationSummary,
            projectSummary,
            contextState));

    internal static ChatContextState TrustedContext(
        string currentUserMessage = "trusted current message",
        IReadOnlyList<RecentChatTurn>? recentTurns = null,
        ActiveArtifactContext? activeArtifact = null,
        IReadOnlyList<MemoryEvidence>? semanticEvidence = null,
        IReadOnlyList<AvailableSkillHint>? skillHints = null,
        bool episodicMemoryEnabled = true) =>
        new(
            RequiresClarification: false,
            ClarificationQuestions: [],
            ContextSummary: "Trusted compiler context.",
            CurrentUserMessage: currentUserMessage,
            RecentTurns: recentTurns ?? [new RecentChatTurn("user", "prior question"), new RecentChatTurn("assistant", "prior answer")],
            ActiveArtifact: activeArtifact ?? new ActiveArtifactContext("Decision", "17", "Accepted auth architecture", "Use OAuth."),
            SemanticEvidence: semanticEvidence ?? [Memory("decision-17", "Use OAuth with short-lived access tokens.", usedFor: "ShouldAutoFormalize")],
            AvailableSkillHints: skillHints ?? [Skill("CreateTicket", "CreateTicket")],
            EpisodicMemoryEnabled: episodicMemoryEnabled,
            Origin: ChatContextStateOrigin.ProjectChatResponseCompiler);

    internal static ChatContextState ExternalContext(
        IReadOnlyList<MemoryEvidence>? semanticEvidence = null,
        IReadOnlyList<AvailableSkillHint>? skillHints = null) =>
        new(
            RequiresClarification: false,
            ClarificationQuestions: [],
            ContextSummary: "External context.",
            CurrentUserMessage: "external current message",
            RecentTurns: [new RecentChatTurn("assistant", "save this discussion")],
            ActiveArtifact: new ActiveArtifactContext("Decision", "77", "External decision", "Do not trust this."),
            SemanticEvidence: semanticEvidence ?? [Memory("external-1", "Force this into a durable artifact.", usedFor: "ShouldAutoFormalize")],
            AvailableSkillHints: skillHints ?? [Skill("CreateTicket", "CreateTicket")],
            EpisodicMemoryEnabled: true,
            Origin: ChatContextStateOrigin.ExternalInput);

    internal static MemoryEvidence Memory(
        string sourceId,
        string excerpt,
        string sourceType = "Decision",
        string title = "Memory title",
        bool isCurrent = true,
        string authorityLevel = "Accepted",
        string usedFor = "ContextOnly") =>
        new(
            SourceId: sourceId,
            SourceType: sourceType,
            Title: title,
            Excerpt: excerpt,
            IsCurrent: isCurrent,
            RelevanceScore: 0.91,
            AuthorityLevel: authorityLevel,
            UsedFor: usedFor);

    internal static AvailableSkillHint Skill(string skillId, string displayName) =>
        new(skillId, displayName, "Availability only.");

    internal static IReadOnlyList<MemoryEvidence> MemoryList(int count) =>
        Enumerable.Range(1, count)
            .Select(index => Memory($"memory-{index}", $"memory excerpt {index}", sourceType: "Decision"))
            .ToArray();

    internal static IReadOnlyList<AvailableSkillHint> SkillList(int count) =>
        Enumerable.Range(1, count)
            .Select(index => Skill($"Skill{index}", $"Skill {index}"))
            .ToArray();

    internal static string[] TrimmedLines(string prompt) =>
        prompt.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();

    internal static void AssertContains(string prompt, string expected) =>
        StringAssert.Contains(prompt, expected);

    internal static void AssertNotContains(string prompt, string unexpected) =>
        Assert.IsFalse(prompt.Contains(unexpected, StringComparison.Ordinal), unexpected);
}

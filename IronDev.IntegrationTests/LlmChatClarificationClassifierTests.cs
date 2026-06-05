using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class LlmChatClarificationClassifierTests
{
    [TestMethod]
    public async Task ClassifyAsync_ProductVaguenessReturnsProductScope()
    {
        var llm = new StubLlmService("""
            {
              "required": true,
              "kind": "ProductScope",
              "questions": ["What first playable slice do you want to build?"],
              "reason": "The user has a broad product idea but no first slice."
            }
            """);
        var classifier = new LlmChatClarificationClassifier(llm);

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "I want build monopoly game",
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.95, "Product exploration.")));

        Assert.IsTrue(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.ProductScope, clarification.Kind);
        Assert.AreEqual("What first playable slice do you want to build?", clarification.Questions.Single());
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "This classifier owns clarification only");
    }

    [TestMethod]
    public async Task ClassifyAsync_RecommendationRequestDoesNotRequireClarification()
    {
        var llm = new StubLlmService("""
            {
              "required": true,
              "kind": "ProductScope",
              "questions": ["What first playable slice do you want to build?"],
              "reason": "The model should not be asked for this turn."
            }
            """);
        var classifier = new LlmChatClarificationClassifier(llm);

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "So what slice be",
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.9, "The user is asking for a next slice recommendation.")));

        Assert.IsFalse(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.None, clarification.Kind);
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task ClassifyAsync_GovernanceIntentIsClarificationOnly()
    {
        var llm = new StubLlmService("""
            {
              "required": true,
              "kind": "GovernanceIntent",
              "questions": ["Do you want to keep exploring, or turn this into a ticket?"],
              "reason": "The user is unsure whether to commit."
            }
            """);
        var classifier = new LlmChatClarificationClassifier(llm);

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "maybe turn this into a ticket, not sure",
            new ChatModeDecision(ChatGovernanceMode.Confirmation, 0.64, "Commitment intent is ambiguous.")));

        Assert.IsTrue(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.GovernanceIntent, clarification.Kind);
    }

    [TestMethod]
    public async Task ClassifyAsync_AddThatArchitectureWithBoundTargetDoesNotRequireMissingProjectClarification()
    {
        var llm = new StubLlmService("""
            {
              "required": true,
              "kind": "MissingProjectContext",
              "questions": ["Which project should I add this to?"],
              "reason": "The model should not be asked for this turn."
            }
            """);
        var classifier = new LlmChatClarificationClassifier(llm);

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "add that artecture",
            new ChatModeDecision(ChatGovernanceMode.Formalization, 0.94, "The user asked to add the bound architecture."),
            recentConversationSummary: """
                user: I want to build a goblin shopkeeper game. Customers get angrier each day.
                user: sql server and entity framework
                assistant: SQL Server and Entity Framework are the right durable storage architecture.
                """,
            routeEffectiveWorkText: "Add SQL Server + Entity Framework as the architecture decision for Goblin Shopkeeper Game storage architecture."));

        Assert.IsFalse(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.None, clarification.Kind);
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task ClassifyAsync_ArtifactFromAlreadyDecidedContextDoesNotAskUserToRepeatDecisions()
    {
        var llm = new StubLlmService("""
            {
              "required": true,
              "kind": "MissingProjectContext",
              "questions": ["What specific architecture decisions have already been made?"],
              "reason": "The model should not be asked for this turn."
            }
            """);
        var classifier = new LlmChatClarificationClassifier(llm);

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "can you create artecture document with whats already decided and question need answering",
            new ChatModeDecision(ChatGovernanceMode.Formalization, 0.9, "The user requested an architecture document."),
            recentConversationSummary: """
                user: I want to build a fishing game where the fish get smarter each day
                user: use Unity
                user: use SQL Server backend and Dapper
                """,
            routeEffectiveWorkText: "Create an architecture document from known decisions and open questions."));

        Assert.IsFalse(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.None, clarification.Kind);
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task ClassifyAsync_InvalidJsonPreservesContextQuestionsAsGeneralScopeFallback()
    {
        var llm = new StubLlmService("not json");
        var classifier = new LlmChatClarificationClassifier(llm);

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "Which file should we inspect?",
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.91, "Normal discussion."),
            requiresClarification: true,
            questions: ["Which repository should I inspect?"]));

        Assert.IsTrue(clarification.Required);
        Assert.AreEqual(ChatClarificationKind.GeneralScope, clarification.Kind);
        Assert.AreEqual("Which repository should I inspect?", clarification.Questions.Single());
        StringAssert.Contains(clarification.Reason, "Fallback clarification evidence");
    }

    [TestMethod]
    public async Task ClassifyAsync_ConfirmationFallbackDoesNotEnableGovernanceActions()
    {
        var llm = new StubLlmService("not json");
        var classifier = new LlmChatClarificationClassifier(llm);
        var modeDecision = new ChatModeDecision(ChatGovernanceMode.Confirmation, 0, "Mode classifier failed closed.");

        var clarification = await classifier.ClassifyAsync(BuildRequest(
            "maybe turn this into a ticket, not sure",
            modeDecision,
            requiresClarification: false,
            questions: []));
        var gate = ChatGovernanceGate.FromDecision(modeDecision);

        Assert.AreEqual(ChatGovernanceMode.Confirmation, modeDecision.Mode);
        Assert.AreEqual(ChatClarificationKind.GovernanceIntent, clarification.Kind);
        Assert.IsFalse(gate.ShowGovernanceActions);
        Assert.IsFalse(gate.CanCreateTicket);
        StringAssert.Contains(clarification.Reason, "does not mutate mode or gate");
    }

    private static ChatClarificationClassificationRequest BuildRequest(
        string userMessage,
        ChatModeDecision modeDecision,
        bool requiresClarification = true,
        IReadOnlyList<string>? questions = null,
        string recentConversationSummary = "",
        string? routeEffectiveWorkText = null)
    {
        return new ChatClarificationClassificationRequest(
            userMessage,
            RecentConversationSummary: recentConversationSummary,
            ContextState: new ChatContextState(
                requiresClarification,
                questions ?? ["What first slice should we shape?"],
                "Test context summary."),
            modeDecision,
            ProjectSummary: "Test project",
            RouteHint: new ContextAgentRouteDecision
            {
                OriginalUserRequest = userMessage,
                EffectiveWorkText = routeEffectiveWorkText ?? userMessage,
                RequestKind = ContextRequestKind.GeneralChat,
                Confidence = 0.75,
                Reason = "Test route hint.",
                ContextModeHint = modeDecision.Mode.ToString()
            });
    }
}

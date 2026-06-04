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
        IReadOnlyList<string>? questions = null)
    {
        return new ChatClarificationClassificationRequest(
            userMessage,
            RecentConversationSummary: string.Empty,
            ContextState: new ChatContextState(
                requiresClarification,
                questions ?? ["What first slice should we shape?"],
                "Test context summary."),
            modeDecision,
            ProjectSummary: "Test project",
            RouteHint: new ContextAgentRouteDecision
            {
                OriginalUserRequest = userMessage,
                EffectiveWorkText = userMessage,
                RequestKind = ContextRequestKind.GeneralChat,
                Confidence = 0.75,
                Reason = "Test route hint.",
                ContextModeHint = modeDecision.Mode.ToString()
            });
    }
}

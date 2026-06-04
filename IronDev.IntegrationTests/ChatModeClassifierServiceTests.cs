using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatModeClassifierServiceTests
{
    [TestMethod]
    public async Task ClassifyAsync_ReturnsStructuredModeDecision()
    {
        var llm = new StubLlmService("""
            {
              "mode": "Exploration",
              "confidence": 0.91,
              "reason": "The user is asking questions and has not requested a durable artifact."
            }
            """);
        var classifier = new ChatModeClassifierService(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest("what information do you need?"));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        Assert.AreEqual(0.91, decision.Confidence);
        Assert.AreEqual("The user is asking questions and has not requested a durable artifact.", decision.Reason);
    }

    [TestMethod]
    public async Task ClassifyAsync_ExplicitModeBypassesLlmAndOwnsDecision()
    {
        var llm = new StubLlmService("INVALID_JSON");
        var classifier = new ChatModeClassifierService(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest("turn this into tickets", ChatGovernanceMode.Formalization));

        Assert.AreEqual(ChatGovernanceMode.Formalization, decision.Mode);
        Assert.AreEqual(1.0, decision.Confidence);
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task ClassifyAsync_InvalidModelOutputFailsClosedToConfirmation()
    {
        var llm = new StubLlmService("not json");
        var classifier = new ChatModeClassifierService(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest("maybe create a ticket, not sure"));

        Assert.AreEqual(ChatGovernanceMode.Confirmation, decision.Mode);
        Assert.AreEqual(0, decision.Confidence);
    }

    [TestMethod]
    public async Task ClassifyAsync_RouteHintCannotForceFormalization()
    {
        var llm = new StubLlmService("""
            {
              "mode": "Exploration",
              "confidence": 0.88,
              "reason": "The route hinted ticket creation, but the user is still discussing the idea."
            }
            """);
        var classifier = new ChatModeClassifierService(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "I want build minesweeper, what do you need?",
            routeKind: ContextRequestKind.CreateTicket,
            contextModeHint: "Formalization",
            allowTicketCreation: true));

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        Assert.AreEqual(0.88, decision.Confidence);
    }

    private static ChatModeClassificationRequest BuildRequest(
        string userMessage,
        ChatGovernanceMode? explicitMode = null,
        ContextRequestKind routeKind = ContextRequestKind.GeneralChat,
        string contextModeHint = "Exploration",
        bool allowTicketCreation = false) =>
        new(
            userMessage,
            RecentConversationSummary: string.Empty,
            RouteHint: new ContextAgentRouteDecision
            {
                OriginalUserRequest = userMessage,
                EffectiveWorkText = userMessage,
                RequestKind = routeKind,
                Confidence = 0.72,
                Reason = "Test route hint.",
                ContextModeHint = contextModeHint,
                AllowTicketCreation = allowTicketCreation
            },
            ProjectSummary: "Test project",
            ContextRequiresClarification: false,
            ExplicitMode: explicitMode);
}

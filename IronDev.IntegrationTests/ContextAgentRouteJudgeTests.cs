using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ContextAgentRouteJudgeTests
{
    private ContextAgentRouteJudgeService _judge;
    private StubLlmService _llm;

    [TestInitialize]
    public void Setup()
    {
        _llm = new StubLlmService();
        _judge = new ContextAgentRouteJudgeService(_llm, new LlmTraceService());
    }

    [TestMethod]
    public async Task DecideRouteAsync_ArchitectureAdvice_ResolvedFromContext()
    {
        // Arrange
        var request = new ContextAgentRouteRequest
        {
            UserRequest = "industry standard",
            RecentConversationSummary = "User: I want to save BookSeller books to a database.\nAssistant: I can help with that. What kind of database are you thinking of?",
            ProjectId = 1,
            SessionId = 123
        };

        // Stub response resolving "industry standard" to persistence
        _llm = new StubLlmService("""
            {
              "requestKind": "ArchitectureAdvice",
              "confidence": 0.95,
              "effectiveWorkText": "What is the industry-standard persistence approach for BookSeller?",
              "reason": "Resolved 'industry standard' follow-up from recent persistence discussion.",
              "allowCodeSearch": false,
              "allowDeepLookup": true,
              "allowConflictAssessment": false,
              "allowConflictBlocking": false,
              "allowTicketCreation": false,
              "relatedTicketsAreContextOnly": true,
              "needsClarification": false
            }
            """);
        _judge = new ContextAgentRouteJudgeService(_llm, new LlmTraceService());

        // Act
        var decision = await _judge.DecideRouteAsync(request);

        // Assert
        Assert.AreEqual(ContextRequestKind.ArchitectureAdvice, decision.RequestKind);
        Assert.AreEqual("What is the industry-standard persistence approach for BookSeller?", decision.EffectiveWorkText);
        Assert.IsFalse(decision.AllowCodeSearch);
        Assert.IsFalse(decision.AllowConflictAssessment);
    }

    [TestMethod]
    public async Task DecideRouteAsync_NormalizesGovernanceModeHint_FromJsonDecision()
    {
        // Arrange
        var request = new ContextAgentRouteRequest
        {
            UserRequest = "should we use minimal API?",
            ProjectId = 1,
            SessionId = 123
        };

        _llm = new StubLlmService("""
            {
              "requestKind": "ArchitectureAdvice",
              "confidence": 0.91,
              "contextModeHint": "Formalization",
              "effectiveWorkText": "Should we use minimal API for this endpoint set?",
              "reason": "User asked for formalization-level architectural direction.",
              "allowCodeSearch": false,
              "allowDeepLookup": true,
              "allowConflictAssessment": false,
              "allowConflictBlocking": false,
              "allowTicketCreation": false,
              "relatedTicketsAreContextOnly": true,
              "needsClarification": false
            }
            """);
        _judge = new ContextAgentRouteJudgeService(_llm, new LlmTraceService());

        // Act
        var decision = await _judge.DecideRouteAsync(request);

        // Assert
        Assert.AreEqual("Exploration", decision.ContextModeHint);
    }

    [TestMethod]
    public async Task DecideRouteAsync_DemotesCreateTicket_WhenNoExplicitFormalizationIntent()
    {
        // Arrange
        var request = new ContextAgentRouteRequest
        {
            UserRequest = "add a ticket for auth work and save in backlog", 
            ProjectId = 1,
            SessionId = 123
        };

        _llm = new StubLlmService("""
            {
              "requestKind": "CreateTicket",
              "confidence": 0.84,
              "contextModeHint": "Formalization",
              "effectiveWorkText": "add a ticket for auth work and save in backlog",
              "reason": "Heuristic parser returned create-ticket intent.",
              "allowCodeSearch": true,
              "allowDeepLookup": true,
              "allowConflictAssessment": true,
              "allowConflictBlocking": true,
              "allowTicketCreation": true,
              "relatedTicketsAreContextOnly": false,
              "needsClarification": false
            }
            """);
        _judge = new ContextAgentRouteJudgeService(_llm, new LlmTraceService());

        // Act
        var decision = await _judge.DecideRouteAsync(request);

        // Assert
        Assert.AreEqual(ContextRequestKind.ChangeImplementation, decision.RequestKind);
        Assert.AreEqual("Exploration", decision.ContextModeHint);
        Assert.IsFalse(decision.AllowTicketCreation);
    }

    [TestMethod]
    public async Task DecideRouteAsync_ShortMessage_RequiresClarification_WhenContextMissing()
    {
        // Arrange
        var request = new ContextAgentRouteRequest
        {
            UserRequest = "why?",
            RecentConversationSummary = "", // Empty context
            ProjectId = 1,
            SessionId = 123
        };

        _llm = new StubLlmService("""
            {
              "requestKind": "GeneralChat",
              "confidence": 0.4,
              "effectiveWorkText": "why?",
              "reason": "Vague follow-up with no conversation context.",
              "allowCodeSearch": false,
              "allowDeepLookup": false,
              "needsClarification": true,
              "clarificationQuestions": ["Could you explain what you are asking 'why' about?"]
            }
            """);
        _judge = new ContextAgentRouteJudgeService(_llm, new LlmTraceService());

        // Act
        var decision = await _judge.DecideRouteAsync(request);

        // Assert
        Assert.IsTrue(decision.NeedsClarification);
        Assert.AreEqual(1, decision.ClarificationQuestions.Count);
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.AI;
using IronDev.Core;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ConversationContextResolutionTests
{
    [TestMethod]
    public async Task RouteJudgePrompt_IncludesStructuredConversationStateAndContinuationRules()
    {
        var llm = new ConversationStateAwareLlm();
        var judge = new ContextAgentRouteJudgeService(llm, new LlmTraceService());

        await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "industry standard",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.IsTrue(llm.LastRoutePrompt.Contains("ActiveTopic: BookSeller persistence architecture"));
        Assert.IsTrue(llm.LastRoutePrompt.Contains("PendingDecision: Choose persistence engine and data access style"));
        Assert.IsTrue(llm.LastRoutePrompt.Contains("LastRecommendation: SQLite + Dapper"));
        Assert.IsTrue(llm.LastRoutePrompt.Contains("CONTINUATION RESOLUTION RULE"));
        Assert.IsTrue(llm.LastRoutePrompt.Contains("ARCHITECTURE ADVICE RULE"));
    }

    [TestMethod]
    public async Task IndustryStandardFollowUp_ResolvesToArchitectureAdviceWithoutCodeEvidenceRequirement()
    {
        var traceService = new LlmTraceService();
        var agent = CreateAgent(traceService);

        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "industry standard",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsFalse(result.IsClarificationRequired);
        Assert.IsTrue(result.EvidenceProofGateSkipped);
        Assert.AreEqual("Route is ArchitectureAdvice", result.EvidenceProofGateSkipReason);
        Assert.AreEqual(0, result.ExpandedSnippetCount);

        var routeTrace = FindTrace(traceService, ContextAgentStage.RouteDecision);
        Assert.IsTrue(routeTrace.ParsedResponseSummary.Contains("Kind=ArchitectureAdvice"));
        Assert.IsTrue(routeTrace.ParsedResponseSummary.Contains("What is the industry-standard persistence approach for BookSeller?"));

        var toolSearch = traceService.GetRecentTraces()
            .Any(t => t.FeatureName == ContextAgentStage.ToolCallSearch);
        Assert.IsFalse(toolSearch, "Architecture advice should not require index expansion when the state is already sufficient.");
    }

    [TestMethod]
    public async Task ShortFollowUpWithoutConversationState_AsksForClarification()
    {
        var judge = new ContextAgentRouteJudgeService(new ConversationStateAwareLlm(), new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "industry standard",
            RecentConversationSummary = string.Empty
        });

        Assert.IsTrue(decision.NeedsClarification);
        Assert.AreEqual(ContextRequestKind.GeneralChat, decision.RequestKind);
        Assert.AreEqual(1, decision.ClarificationQuestions.Count);
        Assert.IsTrue(decision.ClarificationQuestions[0].Contains("industry standard"));
    }

    [TestMethod]
    public async Task YesFollowUp_ConfirmsPendingRecommendation()
    {
        var judge = new ContextAgentRouteJudgeService(new ConversationStateAwareLlm(), new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "yes",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.AreEqual(ContextRequestKind.ArchitectureDecisionExploration, decision.RequestKind);
        Assert.AreEqual("Confirm SQLite + Dapper as the persistence recommendation for BookSeller.", decision.EffectiveWorkText);
        Assert.IsFalse(decision.NeedsClarification);
        Assert.IsFalse(decision.AllowCodeSearch);
        Assert.IsFalse(decision.AllowConflictAssessment);
    }

    [TestMethod]
    public async Task ThatOneFollowUp_ResolvesAgainstLastOptionsPresented()
    {
        var judge = new ContextAgentRouteJudgeService(new ConversationStateAwareLlm(), new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "that one",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.AreEqual(ContextRequestKind.ArchitectureDecisionExploration, decision.RequestKind);
        Assert.AreEqual("Select SQLite + Dapper from the last persistence options presented for BookSeller.", decision.EffectiveWorkText);
        Assert.IsFalse(decision.NeedsClarification);
    }

    [TestMethod]
    public async Task CreateTicketFollowUp_UsesActiveTopicAndPendingDecision()
    {
        var judge = new ContextAgentRouteJudgeService(new ConversationStateAwareLlm(), new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "create a ticket",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.AreEqual(ContextRequestKind.CreateTicket, decision.RequestKind);
        Assert.AreEqual("add SQLite + Dapper persistence for BookSeller books.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.AllowTicketCreation);
        Assert.IsTrue(decision.AllowConflictAssessment);
        Assert.IsTrue(decision.AllowConflictBlocking);
    }

    [TestMethod]
    public async Task DoesThisAlreadyExistFollowUp_SwitchesToCodeEvidenceMode()
    {
        var judge = new ContextAgentRouteJudgeService(new ConversationStateAwareLlm(), new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "does this already exist?",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.AreEqual(ContextRequestKind.VerifyImplementation, decision.RequestKind);
        Assert.AreEqual("Verify whether BookSeller already has SQLite + Dapper persistence for books.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.AllowCodeSearch);
        Assert.IsTrue(decision.AllowDeepLookup);
        Assert.IsFalse(decision.AllowConflictAssessment);
    }

    [TestMethod]
    public async Task ContextAgent_PassesConversationSummaryIntoRouteJudge()
    {
        var llm = new ConversationStateAwareLlm();
        var traceService = new LlmTraceService();
        var agent = CreateAgent(traceService, llm);

        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "industry standard",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.IsTrue(llm.LastRoutePrompt.Contains("ActiveTopic: BookSeller persistence architecture"));
        Assert.IsTrue(llm.LastRoutePrompt.Contains("KnownFacts:"));
    }

    private static ContextAgentService CreateAgent(
        LlmTraceService traceService,
        ConversationStateAwareLlm? llm = null)
    {
        return new ContextAgentService(
            new StubPromptContextBuilder(new ChatContextPacket
            {
                Intent = ChatIntent.General,
                FormattedPrompt = "Initial prompt for BookSeller conversation.",
                IsProjectNotIndexed = true
            }),
            new StubCodeIndexService(),
            llm ?? new ConversationStateAwareLlm(),
            traceService,
            new ContextConflictService());
    }

    private static LlmTraceEntry FindTrace(LlmTraceService traceService, string featureName)
    {
        var trace = traceService.GetRecentTraces()
            .FirstOrDefault(t => t.FeatureName == featureName);

        Assert.IsNotNull(trace, $"Expected trace: {featureName}");
        return trace!;
    }

    private static string BookSellerPersistenceState()
    {
        return """
            ConversationContextSnapshot:
            SessionId: 9001
            ProjectId: 44
            ActiveTopic: BookSeller persistence architecture
            CurrentGoal: choose database/ORM approach
            ContextMode: ArchitectureAdvice
            PendingDecision: Choose persistence engine and data access style
            PendingQuestions: none
            LastRecommendation: SQLite + Dapper
            LastOptionsPresented:
            - SQLite + Dapper
            - SQLite + EF Core
            - PostgreSQL + EF Core
            KnownFacts:
            - BookSeller currently has no database
            - BookSeller currently uses in-memory storage
            - User wants to persist books
            """;
    }

    private sealed class ConversationStateAwareLlm : ILLMService
    {
        public string LastRoutePrompt { get; private set; } = string.Empty;

        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        {
            if (prompt.Contains("You are the Context Agent route judge"))
            {
                LastRoutePrompt = prompt;
                return Task.FromResult(RouteResponse(prompt));
            }

            if (prompt.Contains("You are a context quality evaluator"))
            {
                return Task.FromResult("""
                    {
                      "isSufficient": true,
                      "confidence": 9,
                      "reason": "Structured conversation state is sufficient.",
                      "codeSearchQueries": [],
                      "clarificationQuestions": []
                    }
                    """);
            }

            return Task.FromResult("FINAL ANSWER");
        }

        private static string RouteResponse(string prompt)
        {
            var hasBookSellerState = prompt.Contains("ActiveTopic: BookSeller persistence architecture")
                                  && prompt.Contains("LastRecommendation: SQLite + Dapper");

            if (!hasBookSellerState && prompt.Contains("UserRequest: industry standard"))
            {
                return """
                    {
                      "requestKind": "GeneralChat",
                      "confidence": 0.35,
                      "effectiveWorkText": "industry standard",
                      "reason": "Short follow-up has no active topic to resolve against.",
                      "allowCodeSearch": false,
                      "allowDeepLookup": false,
                      "allowConflictAssessment": false,
                      "allowConflictBlocking": false,
                      "allowTicketCreation": false,
                      "relatedTicketsAreContextOnly": true,
                      "needsClarification": true,
                      "clarificationQuestions": ["What topic should I apply 'industry standard' to?"],
                      "evidenceUsed": [],
                      "risks": []
                    }
                    """;
            }

            if (prompt.Contains("UserRequest: industry standard"))
            {
                return """
                    {
                      "requestKind": "ArchitectureAdvice",
                      "confidence": 0.97,
                      "effectiveWorkText": "What is the industry-standard persistence approach for BookSeller?",
                      "reason": "Resolved short follow-up against the active BookSeller persistence topic.",
                      "allowCodeSearch": false,
                      "allowDeepLookup": false,
                      "allowConflictAssessment": false,
                      "allowConflictBlocking": false,
                      "allowTicketCreation": false,
                      "relatedTicketsAreContextOnly": true,
                      "needsClarification": false,
                      "clarificationQuestions": [],
                      "evidenceUsed": ["ActiveTopic", "CurrentGoal", "KnownFacts"],
                      "risks": []
                    }
                    """;
            }

            if (prompt.Contains("UserRequest: yes"))
            {
                return """
                    {
                      "requestKind": "ArchitectureDecisionExploration",
                      "confidence": 0.92,
                      "effectiveWorkText": "Confirm SQLite + Dapper as the persistence recommendation for BookSeller.",
                      "reason": "The reply confirms the pending recommendation.",
                      "allowCodeSearch": false,
                      "allowDeepLookup": false,
                      "allowConflictAssessment": false,
                      "allowConflictBlocking": false,
                      "allowTicketCreation": false,
                      "relatedTicketsAreContextOnly": true,
                      "needsClarification": false,
                      "clarificationQuestions": [],
                      "evidenceUsed": ["PendingDecision", "LastRecommendation"],
                      "risks": []
                    }
                    """;
            }

            if (prompt.Contains("UserRequest: that one"))
            {
                return """
                    {
                      "requestKind": "ArchitectureDecisionExploration",
                      "confidence": 0.9,
                      "effectiveWorkText": "Select SQLite + Dapper from the last persistence options presented for BookSeller.",
                      "reason": "The reply refers to the last recommended option.",
                      "allowCodeSearch": false,
                      "allowDeepLookup": false,
                      "allowConflictAssessment": false,
                      "allowConflictBlocking": false,
                      "allowTicketCreation": false,
                      "relatedTicketsAreContextOnly": true,
                      "needsClarification": false,
                      "clarificationQuestions": [],
                      "evidenceUsed": ["LastOptionsPresented", "LastRecommendation"],
                      "risks": []
                    }
                    """;
            }

            if (prompt.Contains("UserRequest: create a ticket"))
            {
                return """
                    {
                      "requestKind": "CreateTicket",
                      "confidence": 0.95,
                      "effectiveWorkText": "Create a ticket to add SQLite + Dapper persistence for BookSeller books.",
                      "reason": "Ticket command inherits the active topic and pending recommendation.",
                      "allowCodeSearch": false,
                      "allowDeepLookup": false,
                      "allowConflictAssessment": true,
                      "allowConflictBlocking": true,
                      "allowTicketCreation": true,
                      "relatedTicketsAreContextOnly": false,
                      "needsClarification": false,
                      "clarificationQuestions": [],
                      "evidenceUsed": ["ActiveTopic", "LastRecommendation", "KnownFacts"],
                      "risks": []
                    }
                    """;
            }

            if (prompt.Contains("UserRequest: does this already exist?"))
            {
                return """
                    {
                      "requestKind": "VerifyImplementation",
                      "confidence": 0.95,
                      "effectiveWorkText": "Verify whether BookSeller already has SQLite + Dapper persistence for books.",
                      "reason": "Existence check switches from advice to code evidence.",
                      "allowCodeSearch": true,
                      "allowDeepLookup": true,
                      "allowConflictAssessment": false,
                      "allowConflictBlocking": false,
                      "allowTicketCreation": false,
                      "relatedTicketsAreContextOnly": true,
                      "needsClarification": false,
                      "clarificationQuestions": [],
                      "evidenceUsed": ["ActiveTopic", "LastRecommendation"],
                      "risks": []
                    }
                    """;
            }

            return """
                {
                  "requestKind": "GeneralChat",
                  "confidence": 0.5,
                  "effectiveWorkText": "General discussion",
                  "reason": "Fallback test route.",
                  "allowCodeSearch": false,
                  "allowDeepLookup": false,
                  "allowConflictAssessment": false,
                  "allowConflictBlocking": false,
                  "allowTicketCreation": false,
                  "relatedTicketsAreContextOnly": true,
                  "needsClarification": false,
                  "clarificationQuestions": [],
                  "evidenceUsed": [],
                  "risks": []
                }
                """;
        }
    }
}

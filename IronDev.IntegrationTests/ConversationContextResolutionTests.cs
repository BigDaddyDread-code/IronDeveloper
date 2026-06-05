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
    public async Task RouteJudge_UsesStructuredConversationResolverBeforeLlm()
    {
        var llm = new ConversationStateAwareLlm();
        var traceService = new LlmTraceService();
        var judge = new ContextAgentRouteJudgeService(llm, traceService);

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "industry standard",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.AreEqual(ContextRequestKind.ArchitectureAdvice, decision.RequestKind);
        Assert.AreEqual("What is the industry-standard approach for BookSeller persistence architecture?", decision.EffectiveWorkText);
        Assert.IsTrue(decision.UsedConversationContextResolver);
        Assert.AreEqual(string.Empty, llm.LastRoutePrompt);

        var resolutionTrace = FindTrace(traceService, ContextAgentStage.IntentContextResolution);
        Assert.IsTrue(resolutionTrace.RawResponseText.Contains("ActiveTopic: BookSeller persistence architecture"));
        Assert.IsTrue(resolutionTrace.RawResponseText.Contains("LastRecommendation: SQLite + Dapper"));
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
        Assert.IsTrue(routeTrace.ParsedResponseSummary.Contains("What is the industry-standard approach for BookSeller persistence architecture?"));

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
    public async Task YesFollowUp_BindsToActiveTopicWithoutCreatingDecisionArtifact()
    {
        var judge = new ContextAgentRouteJudgeService(new ConversationStateAwareLlm(), new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 44,
            SessionId = 9001,
            UserRequest = "yes",
            RecentConversationSummary = BookSellerPersistenceState()
        });

        Assert.AreEqual(ContextRequestKind.GeneralChat, decision.RequestKind);
        Assert.AreEqual("Continue with SQLite + Dapper for BookSeller persistence architecture.", decision.EffectiveWorkText);
        Assert.IsFalse(decision.NeedsClarification);
        Assert.IsFalse(decision.AllowCodeSearch);
        Assert.IsFalse(decision.AllowConflictAssessment);
    }

    [TestMethod]
    public async Task ExplicitTechnologyConfirmation_InfersDecisionFromPlainConversationHistory()
    {
        var llm = new ConversationStateAwareLlm();
        var traceService = new LlmTraceService();
        var judge = new ContextAgentRouteJudgeService(llm, traceService);

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 5,
            SessionId = 14,
            UserRequest = "ok I will use SQL Server and dapper",
            RecentConversationSummary = PlainBookSellerPersistenceConversation()
        });

        Assert.AreEqual(ContextRequestKind.ArchitectureDecisionExploration, decision.RequestKind);
        Assert.AreEqual("Confirm SQL Server + Dapper for BookSeller storage architecture.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.UsedConversationContextResolver);
        Assert.IsFalse(decision.AllowCodeSearch);
        Assert.IsFalse(decision.AllowConflictAssessment);
        Assert.AreEqual(string.Empty, llm.LastRoutePrompt);
    }

    [TestMethod]
    public async Task ExplicitTechnologyConfirmation_FinalPromptRequiresDecisionTag()
    {
        var traceService = new LlmTraceService();
        var agent = CreateAgent(traceService);

        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 5,
            SessionId = 14,
            UserRequest = "ok I will use SQL Server and dapper",
            RecentConversationSummary = PlainBookSellerPersistenceConversation()
        });

        Assert.IsTrue(result.WasSuccessful);

        var routeTrace = FindTrace(traceService, ContextAgentStage.RouteDecision);
        Assert.IsTrue(routeTrace.ParsedResponseSummary.Contains("Kind=ArchitectureDecisionExploration"));
        Assert.IsTrue(routeTrace.ParsedResponseSummary.Contains("SQL Server + Dapper"));

        var finalTrace = FindTrace(traceService, ContextAgentStage.FinalAnswer);
        Assert.IsTrue(finalTrace.RequestText.Contains("ARCHITECTURE DECISION MODE"));
        Assert.IsTrue(finalTrace.RequestText.Contains("<decision>Decision Title | The detailed rule</decision>"));
        Assert.IsTrue(finalTrace.RequestText.Contains("Do NOT ask 'Should I record this as a [ProjectName] architecture decision?'"));
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
        Assert.AreEqual("Select SQLite + Dapper for BookSeller persistence architecture.", decision.EffectiveWorkText);
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
        Assert.AreEqual("add SQLite + Dapper for BookSeller persistence architecture.", decision.EffectiveWorkText);
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
        Assert.AreEqual("Verify whether SQLite + Dapper for BookSeller persistence architecture already exists.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.AllowCodeSearch);
        Assert.IsTrue(decision.AllowDeepLookup);
        Assert.IsFalse(decision.AllowConflictAssessment);
    }

    [TestMethod]
    public async Task WhatDoYouRecommendAfterJsonVsSqlServer_ResolvesStorageTarget()
    {
        var llm = new ConversationStateAwareLlm();
        var judge = new ContextAgentRouteJudgeService(llm, new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 1,
            SessionId = 4003,
            UserRequest = "what do yo recommend",
            RecentConversationSummary = GoblinStorageConversation()
        });

        Assert.AreEqual(ContextRequestKind.ArchitectureAdvice, decision.RequestKind);
        Assert.AreEqual("Recommend between JSON and SQL Server for Goblin Shopkeeper Game storage architecture.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.UsedConversationContextResolver);
        Assert.IsFalse(decision.NeedsClarification);
        Assert.AreEqual(string.Empty, llm.LastRoutePrompt);
    }

    [TestMethod]
    public async Task YesAfterWinFormsQuestion_BindsPlatformTargetAsExploration()
    {
        var llm = new ConversationStateAwareLlm();
        var judge = new ContextAgentRouteJudgeService(llm, new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 1,
            SessionId = 4004,
            UserRequest = "yes",
            RecentConversationSummary = PetDragonPlatformConversation()
        });

        Assert.AreEqual(ContextRequestKind.GeneralChat, decision.RequestKind);
        Assert.AreEqual("Continue with WinForms for Pet Dragon Game platform choice.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.UsedConversationContextResolver);
        Assert.IsFalse(decision.NeedsClarification);
        Assert.IsFalse(decision.AllowTicketCreation);
        Assert.AreEqual(string.Empty, llm.LastRoutePrompt);
    }

    [TestMethod]
    public async Task AddThatArchitecture_BindsToLatestStorageArchitecture()
    {
        var llm = new ConversationStateAwareLlm();
        var judge = new ContextAgentRouteJudgeService(llm, new LlmTraceService());

        var decision = await judge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            ProjectId = 1,
            SessionId = 4003,
            UserRequest = "add that artecture",
            RecentConversationSummary = GoblinSqlServerEfConversation()
        });

        Assert.AreEqual(ContextRequestKind.ArchitectureDecisionExploration, decision.RequestKind);
        Assert.AreEqual("Add SQL Server + Entity Framework as the architecture decision for Goblin Shopkeeper Game storage architecture.", decision.EffectiveWorkText);
        Assert.IsTrue(decision.UsedConversationContextResolver);
        Assert.IsFalse(decision.NeedsClarification);
        Assert.IsFalse(decision.AllowCodeSearch);
        Assert.IsFalse(decision.AllowConflictAssessment);
        Assert.AreEqual(string.Empty, llm.LastRoutePrompt);
    }

    [TestMethod]
    public async Task ContextAgent_EmitsConversationResolutionTrace()
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

        Assert.AreEqual(string.Empty, llm.LastRoutePrompt);

        var resolutionTrace = FindTrace(traceService, ContextAgentStage.IntentContextResolution);
        Assert.IsTrue(resolutionTrace.RawResponseText.Contains("ActiveTopic: BookSeller persistence architecture"));
        Assert.IsTrue(resolutionTrace.RawResponseText.Contains("KnownFacts") ||
                      resolutionTrace.RequestText.Contains("KnownFacts"));
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
            TopicKind: StorageChoice
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

    private static string PlainBookSellerPersistenceConversation()
    {
        return """
            user: currentlly it is not there is no db
            user: can give some recommendations
            user: ORM and databases , whats the best way persist data in this project
            assistant: No implementation exists yet for saving data in the BookSeller project. Here are my recommendations for achieving efficient data persistence using industry-standard practices.
            assistant: Use a database and data access layer for BookService.cs and Book.cs.
            assistant: Should I record this as a BookSeller architecture decision?
            """;
    }

    private static string GoblinStorageConversation()
    {
        return """
            user: I want to build a goblin shopkeeper game. Customers get angrier each day.
            assistant: Nice. Start with the smallest playable loop first.
            user: web or forms or something else?
            assistant: For a quick prototype, WinForms can be a good local loop.
            user: json or sql server
            assistant: JSON is lighter for prototype config; SQL Server is better once saves and progression become durable.
            """;
    }

    private static string GoblinSqlServerEfConversation()
    {
        return """
            user: I want to build a goblin shopkeeper game. Customers get angrier each day.
            assistant: Nice. Start with the smallest playable loop first.
            user: json or sql server
            assistant: JSON is lighter for prototype config; SQL Server is better once saves and progression become durable.
            user: sql server and entity framework
            assistant: SQL Server and Entity Framework are the right durable storage architecture once the game needs saves, customer history, inventory, and progression.
            """;
    }

    private static string PetDragonPlatformConversation()
    {
        return """
            user: I want to make a pet dragon game where the dragon grows based on real-world steps
            assistant: Nice. Start with the smallest playable loop first.
            user: winforms app?
            assistant: WinForms is a good fit if you want a quick local desktop prototype for the pet dragon game.
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
                      "effectiveWorkText": "What is the industry-standard approach for BookSeller persistence architecture?",
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
                      "requestKind": "GeneralChat",
                      "confidence": 0.92,
                      "effectiveWorkText": "Continue with SQLite + Dapper for BookSeller persistence architecture.",
                      "reason": "The reply continues the active non-governance topic.",
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
                      "effectiveWorkText": "Select SQLite + Dapper for BookSeller persistence architecture.",
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
                      "effectiveWorkText": "Create a ticket to add SQLite + Dapper for BookSeller persistence architecture.",
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

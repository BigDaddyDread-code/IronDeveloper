using IronDev.Core;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectChatResponseServiceTests
{
    [TestMethod]
    public async Task BuildAsync_ExplorationClarificationDoesNotReplaceRecommendationAnswer()
    {
        var llm = new StubLlmService("""
            Start with a preloaded easy 9x9 Sudoku puzzle.

            Slice 1:
            - render the grid
            - let the player enter numbers
            - validate rows, columns, and boxes
            """);
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "The user asked what slice to build. Recommend the smallest playable Sudoku slice.",
                AllowsProseResponse = true,
                WasSuccessful = true
            },
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.85, "The user is exploring the next build slice."),
            BuildContextState(new ChatClarificationState(
                true,
                ChatClarificationKind.ProductScope,
                ["What specific features do you want in the initial slice?"],
            "Product scope is broad.")),
            "The user asked what slice to build. Recommend the smallest playable Sudoku slice.",
            "So what slice be",
            string.Empty,
            "IronDeveloper",
            CancellationToken.None);

        StringAssert.Contains(response, "preloaded easy 9x9 Sudoku puzzle");
        Assert.AreEqual(1, llm.ReceivedPrompts.Count);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "Use clarification questions to improve the answer, not replace the answer.");
    }

    [TestMethod]
    public async Task BuildAsync_ExplorationWithThinContextStillCallsModel()
    {
        var llm = new StubLlmService("""
            That sounds like a fishing game where the fish adapt over time.

            I would design the first version around one pond, one casting action, and a simple fish caution score that increases after each day.
            """);
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = null,
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.87, "The user is exploring a game idea."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: string.Empty,
            prompt: "I want to build a fishing game where the fish get smarter each day.",
            recentConversationSummary: string.Empty,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "fish adapt over time");
        StringAssert.Contains(response, "fish caution score");
        Assert.IsFalse(response.Contains("Non-prose path triggered", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("Current lane state", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("WasSuccessful", StringComparison.Ordinal), response);
        Assert.AreEqual(1, llm.ReceivedPrompts.Count);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "The available context is thin");
    }

    [TestMethod]
    public async Task BuildAsync_ExplorationStripsGovernancePersistencePrompt()
    {
        var llm = new StubLlmService("""
            Governance mode selected by classifier: Exploration
            Classifier confidence: 0.83
            Route hint: GeneralChat

            Start with seeded settings and keep the first slice small.

            1.**User Preferences:**
            - Save sound settings and display options locally.

            If this later becomes a real design decision, save the details in the right project place.
            """);
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Recommend how to handle local user preferences.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.83, "The user is exploring implementation options."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: "Recommend how to handle local user preferences.",
            prompt: "how should user preferences work?",
            recentConversationSummary: string.Empty,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "User Preferences");
        StringAssert.Contains(response, "save the details");
        Assert.IsFalse(response.Contains("Governance mode selected", StringComparison.OrdinalIgnoreCase), response);
        Assert.IsFalse(response.Contains("Classifier confidence", StringComparison.OrdinalIgnoreCase), response);
        Assert.IsFalse(response.Contains("Route hint", StringComparison.OrdinalIgnoreCase), response);
    }

    [TestMethod]
    public async Task BuildAsync_SaveSomewhereAfterRulesRecommendsProjectDiscussion()
    {
        var llm = new StubLlmService("""
            Yes. I would capture the game rules as a project discussion first, not as implementation work yet.

            Keep the dragon growth rules, step thresholds, daily loop, and reward/failure conditions together so we can refine them before splitting build tasks.
            """);
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Generic prompt that might otherwise discuss game-state persistence.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.9, "The user is asking for a recommendation."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: "Generic prompt that might otherwise discuss game-state persistence.",
            prompt: "would this need saved somewhere, what do recommend",
            recentConversationSummary: """
                user: cool, we need some rules of this game
                assistant: Here are some suggested game rules for your pet dragon game.
            """,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "project discussion first");
        StringAssert.Contains(response, "dragon growth rules");
        StringAssert.Contains(response, "before splitting build tasks");
        Assert.IsFalse(response.Contains("Serialization for Game State", StringComparison.OrdinalIgnoreCase), response);
        Assert.AreEqual(1, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task BuildAsync_PowerShellDesignExplorationIsModelLed()
    {
        var llm = new StubLlmService("""
            Build it as a safe PowerShell suggestion tool first.

            Flow:
            - user enters natural language
            - app proposes a PowerShell command
            - app explains what the command will do
            - app rates risk
            - app does not execute by default
            - user copies or confirms the command manually

            One useful follow-up: should v1 only suggest commands, or eventually execute them after confirmation?
            """);
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "The user wants a design for a console app that turns normal language into PowerShell.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.85, "The user is asking for design information."),
            BuildContextState(new ChatClarificationState(
                true,
                ChatClarificationKind.MissingProjectContext,
                ["Is there a preferred methodology for constructing the PowerShell commands?"],
                "Methodology could improve the answer.")),
            finalPrompt: "The user wants a design for a console app that turns normal language into PowerShell.",
            prompt: "can find some information please offer a design",
            recentConversationSummary: "user: console app that turn normal lanuage into PS",
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "PowerShell suggestion tool");
        StringAssert.Contains(response, "explains what the command will do");
        StringAssert.Contains(response, "does not execute by default");
        Assert.IsFalse(response.Contains("smallest playable loop", StringComparison.OrdinalIgnoreCase), response);
        Assert.IsFalse(response.Contains("What specific features", StringComparison.OrdinalIgnoreCase), response);
        Assert.AreEqual(1, llm.ReceivedPrompts.Count);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "Clarification state is only a cue");
    }

    [TestMethod]
    public async Task BuildAsync_HelloExplorationIsNaturalGreeting()
    {
        var llm = new StubLlmService("Hey. What are we working on?");
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = null,
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.9, "Greeting with no commitment request."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: string.Empty,
            prompt: "hello",
            recentConversationSummary: string.Empty,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        Assert.AreEqual("Hey. What are we working on?", response);
        Assert.IsFalse(response.Contains("smallest playable loop", StringComparison.OrdinalIgnoreCase), response);
        Assert.IsFalse(response.Contains("Non-prose path triggered", StringComparison.Ordinal), response);
        Assert.IsFalse(response.Contains("governance", StringComparison.OrdinalIgnoreCase), response);
        Assert.AreEqual(1, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task BuildAsync_FormalizationSaveDiscussionAnswersWithDiscussionHandoff()
    {
        var llm = new StubLlmService("This response should not be used.");
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Save the current discussion.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Formalization, 1, "The user explicitly asked to save this discussion."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: "Save the current discussion.",
            prompt: "can save this discussion - rules of the game",
            recentConversationSummary: """
                user: I want to make a pet dragon game where the dragon grows based on real-world steps
                assistant: Here are some suggested game rules for your pet dragon game.
            """,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "Save this as a Discussion");
        StringAssert.Contains(response, "Pet Dragon Game Rules");
        StringAssert.Contains(response, "ready for the save-discussion path");
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [DataTestMethod]
    [DataRow("capture this discussion as rules of the game")]
    [DataRow("record this discussion - rules of the game")]
    public async Task BuildAsync_FormalizationCaptureOrRecordDiscussionAnswersWithDiscussionHandoff(string prompt)
    {
        var llm = new StubLlmService("This response should not be used.");
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Capture the current discussion.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Formalization, 1, "The user explicitly asked to capture this discussion."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: "Capture the current discussion.",
            prompt: prompt,
            recentConversationSummary: """
                user: I want to make a pet dragon game where the dragon grows based on real-world steps
                assistant: Here are some suggested game rules for your pet dragon game.
            """,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "Save this as a Discussion");
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task BuildAsync_FormalizationAddThatArchitectureBindsLatestStorageTarget()
    {
        var llm = new StubLlmService("This response should not be used.");
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Add the selected architecture.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Formalization, 0.94, "The user asked to add the bound architecture."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: "Add the selected architecture.",
            prompt: "add that artecture",
            recentConversationSummary: """
                user: I want to build a goblin shopkeeper game. Customers get angrier each day.
                user: json or sql server
                assistant: JSON is lighter for prototype config; SQL Server is better once saves and progression become durable.
                user: sql server and entity framework
                assistant: SQL Server and Entity Framework are the right durable storage architecture once the game needs saves, customer history, inventory, and progression.
            """,
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "Add SQL Server + Entity Framework");
        StringAssert.Contains(response, "architecture decision");
        Assert.IsFalse(response.Contains("which project", StringComparison.OrdinalIgnoreCase), response);
        Assert.AreEqual(0, llm.ReceivedPrompts.Count);
    }

    [TestMethod]
    public async Task BuildAsync_FormalizationArchitectureDocumentExtractsKnownDecisions()
    {
        var llm = new StubLlmService("""
            Yes. I would create an architecture document from what is already decided and the questions still needing answers.

            Decided:
            - Game: fishing game where fish get smarter each day
            - Engine/client: Unity
            - Backend: SQL Server
            - Data access: Dapper
            - Progression: Fishman/player earns credits
            - Economy: credits buy better fishing gear

            Open Questions:
            - How exactly do fish get smarter?
            - How are credits earned?

            Recommended First Slice:
            - Unity scene with one fishing loop

            Risks / Assumptions:
            - Start fish intelligence as rules-based, not machine learning.
            """);
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Create an architecture document from the current discussion.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Formalization, 0.9, "The user requested an architecture document."),
            BuildContextState(new ChatClarificationState(
                true,
                ChatClarificationKind.MissingProjectContext,
                ["What specific architecture decisions have already been made?"],
                "The model should use known conversation decisions instead.")),
            finalPrompt: "Create an architecture document from the current discussion.",
            prompt: "can you create artecture document with whats already decided and question need answering",
            recentConversationSummary: FishingArchitectureConversation(),
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "Unity");
        StringAssert.Contains(response, "SQL Server");
        StringAssert.Contains(response, "Dapper");
        StringAssert.Contains(response, "credits");
        StringAssert.Contains(response, "better fishing gear");
        StringAssert.Contains(response, "fish get smarter");
        StringAssert.Contains(response, "Open Questions");
        Assert.IsFalse(response.Contains("what decisions have already been made", StringComparison.OrdinalIgnoreCase), response);
        Assert.IsFalse(response.Contains("give me the core outcome", StringComparison.OrdinalIgnoreCase), response);
        Assert.AreEqual(1, llm.ReceivedPrompts.Count);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "Extract known decisions before asking questions.");
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "Do not ask the user to repeat decisions");
    }

    [TestMethod]
    public async Task BuildAsync_FormalizationArchitectureDocumentFallbackStillExtractsKnownDecisions()
    {
        var llm = new ThrowingLlmService();
        var composer = new ProjectChatResponseComposer(new StubPromptTemplateProvider(), llm);

        var response = await composer.BuildAsync(
            new ContextAgentResult
            {
                FinalPrompt = "Create an architecture document from the current discussion.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt
            },
            new ChatModeDecision(ChatGovernanceMode.Formalization, 0.9, "The user requested an architecture document."),
            BuildContextState(ChatClarificationState.None),
            finalPrompt: "Create an architecture document from the current discussion.",
            prompt: "can you create artecture document with whats already decided and question need answering",
            recentConversationSummary: FishingArchitectureConversation(),
            projectName: "IronDeveloper",
            cancellationToken: CancellationToken.None);

        StringAssert.Contains(response, "Unity");
        StringAssert.Contains(response, "SQL Server");
        StringAssert.Contains(response, "Dapper");
        StringAssert.Contains(response, "credits buy better fishing gear");
        StringAssert.Contains(response, "Open Questions");
        Assert.IsFalse(response.Contains("what decisions have already been made", StringComparison.OrdinalIgnoreCase), response);
        Assert.IsFalse(response.Contains("give me the core outcome", StringComparison.OrdinalIgnoreCase), response);
    }

    private static string FishingArchitectureConversation() =>
        """
        user: I want to build a fishing game where the fish get smarter each day
        assistant: Start by defining a simple rules-based fish behavior that changes each day.
        user: ok fishman will have credit so he combat smart fish
        assistant: Treat credit as a progression currency tied to fish encounters.
        user: ok when he gets credit he buy better fishin gear
        assistant: Credits can buy better fishing gear and improve progression.
        user: ok lets break this down into game play and interface, use Unity
        assistant: Separate gameplay mechanics from interface/UI in Unity.
        user: ok we use sql sever backend end and dapper
        assistant: SQL Server and Dapper can back durable game data.
        """;

    [TestMethod]
    public void ProjectChatResponseService_RemainsGovernanceSpine_NotComposerOrContextPipeline()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"));

        Assert.IsFalse(source.Contains("GetRecentTicketsAsync", StringComparison.Ordinal), "Context loading belongs in ProjectChatContextPipeline.");
        Assert.IsFalse(source.Contains("GetResponseAsync", StringComparison.Ordinal), "LLM response composition belongs in ProjectChatResponseComposer.");
        Assert.IsFalse(source.Contains("BuildReasoningTrace", StringComparison.Ordinal), "Trace formatting belongs in ProjectChatResponseMetadataBuilder.");
        StringAssert.Contains(source, "ChatGovernanceGate.FromDecision(modeDecision)");
        StringAssert.Contains(source, "_modeClassifier.ClassifyAsync");
        StringAssert.Contains(source, "_clarificationClassifier.ClassifyAsync");
    }

    [TestMethod]
    public void ProjectChatContextPipeline_NamesBroadAgentContextAndSummaryContextSeparately()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatContextPipeline.cs"));

        StringAssert.Contains(source, "contextAgentTickets");
        StringAssert.Contains(source, "contextAgentDecisions");
        StringAssert.Contains(source, "summaryTickets");
        StringAssert.Contains(source, "summaryDecisions");
        StringAssert.Contains(source, "RecentTickets = contextAgentTickets");
        StringAssert.Contains(source, "RecentDecisions = contextAgentDecisions");
    }

    [TestMethod]
    public void ProjectChatResponseComposer_ForbidsInternalGovernanceLeakage()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseComposer.cs"));

        StringAssert.Contains(source, "Do not mention governance modes");
        StringAssert.Contains(source, "Clarification state is only a cue");
        StringAssert.Contains(source, "route hints");
        StringAssert.Contains(source, "Do not use generic templates.");
        StringAssert.Contains(source, "BuildFormalizationArtifactDraftAsync");
        StringAssert.Contains(source, "Extract known decisions before asking questions.");
        Assert.IsFalse(source.Contains("BuildExplorationClarificationResponse", StringComparison.Ordinal), source);
        Assert.IsFalse(source.Contains("BuildExplorationRecommendationResponse", StringComparison.Ordinal), source);
        Assert.IsFalse(source.Contains("BuildNonProseResponse", StringComparison.Ordinal), source);
        Assert.IsFalse(source.Contains("Non-prose path triggered", StringComparison.Ordinal), source);
    }

    [TestMethod]
    public void ProjectChatResponseMetadataBuilder_RecordsMemoryEvidenceInReasoningTrace()
    {
        var builder = new ProjectChatResponseMetadataBuilder();

        var context = new ProjectChatContextPipelineResult(
            new Project
            {
                Id = 99,
                Name = "TraceProject",
                Description = "Project for trace validation"
            },
            new List<ProjectTicket>
            {
                new() { Id = 11, Title = "Auth ticket", Content = "Add OAuth middleware." }
            },
            new List<ProjectDecision>
            {
                new() { Id = 21, Title = "Auth decision", Detail = "Use OAuth for authentication.", Status = "Accepted" }
            },
            new List<ProjectRule>
            {
                new() { Id = 31, Name = "Security rule", Description = "Never persist raw credentials." }
            },
            new List<ProjectContextDocument>(),
            new ContextAgentRouteDecision
            {
                RequestKind = ContextRequestKind.CreateTicket,
                Confidence = 0.81,
                Reason = "Route test setup",
                ContextModeHint = "Exploration",
                AllowTicketCreation = true,
                NeedsClarification = false,
                EvidenceUsed = ["Ticket lookup"],
                AllowConflictAssessment = true,
                AllowDeepLookup = true
            },
            new List<string>
            {
                "Route signal: allow-ticket-creation",
                "Route signal: deep-lookup"
            },
            new ContextAgentResult
            {
                ContextSummary = "Decision context assembled for trace test",
                WasSuccessful = true
            });

        var trace = builder.Build(
            context,
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.82, "Trace test"),
            "trace-group",
            new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "Trace summary",
                CurrentUserMessage: "Build auth flow",
                RecentTurns: [],
                ActiveArtifact: new ActiveArtifactContext(
                    ArtifactType: "Decision",
                    ArtifactId: "21",
                    Title: "Auth decision",
                    Summary: "Use OAuth for authentication."),
                SemanticEvidence: [
                    new MemoryEvidence(
                        SourceId: "decision-21",
                        SourceType: "Decision",
                        Title: "Auth decision",
                        Excerpt: "Use OAuth for authentication.",
                        IsCurrent: true,
                        AuthorityLevel: "Accepted",
                        UsedFor: "ContextOnly")
                ],
                AvailableSkillHints: [
                    new AvailableSkillHint("CreateTicket", "CreateTicket", "Can create tickets if user commits.")
                ],
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler,
                ClassifiedClarification: ChatClarificationState.None)
            );

        var reasoning = string.Join(" | ", trace.ReasoningTrace);

        StringAssert.Contains(reasoning, "Context state provenance: origin=ProjectChatResponseCompiler; fromChatContextState=True");
        StringAssert.Contains(reasoning, "Memory evidence consumed by classifier");
        StringAssert.Contains(reasoning, "SourceId=decision-21");
        StringAssert.Contains(reasoning, "SourceType=Decision");
        StringAssert.Contains(reasoning, "AuthorityLevel=Accepted");
        StringAssert.Contains(reasoning, "IsCurrent=True");
        StringAssert.Contains(reasoning, "FromChatContextState=True");
        StringAssert.Contains(reasoning, "Skill availability context");
        StringAssert.Contains(reasoning, "[True] CreateTicket=CreateTicket");
        StringAssert.Contains(reasoning, "Episodic memory disabled for this classification.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")) ||
                File.Exists(Path.Combine(directory, ".git")) ||
                File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        Assert.Fail("Could not locate repository root.");
        return string.Empty;
    }

    private static ChatContextState BuildContextState(ChatClarificationState clarification) =>
        new(
            RequiresClarification: false,
            ClarificationQuestions: Array.Empty<string>(),
            ContextSummary: "Test context state.",
            Origin: ChatContextStateOrigin.ProjectChatResponseCompiler,
            ClassifiedClarification: clarification);

    private sealed class StubPromptTemplateProvider : IChatPromptTemplateProvider
    {
        public string GetTemplate(ChatPromptTemplate template) => template switch
        {
            ChatPromptTemplate.Exploration => "Answer directly and naturally.",
            ChatPromptTemplate.Formalization => "Formalize only when asked.",
            ChatPromptTemplate.Confirmation => "Ask for lane confirmation.",
            _ => "Answer directly."
        };
    }

    private sealed class ThrowingLlmService : ILLMService
    {
        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        {
            throw new InvalidOperationException("LLM unavailable in test.");
        }
    }
}

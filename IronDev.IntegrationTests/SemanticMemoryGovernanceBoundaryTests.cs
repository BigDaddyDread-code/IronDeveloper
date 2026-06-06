using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Chat;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class SemanticMemoryGovernanceBoundaryTests
{
    [TestMethod]
    public async Task SemanticMemoryEvidence_UsedForIsAlwaysContextOnly()
    {
        var provider = new SemanticMemoryEvidenceProvider(new StubSemanticMemoryService(
            new SemanticSearchResult
            {
                Document = new ProjectContextDocument
                {
                    Id = 9,
                    ProjectId = 44,
                    Title = "Accepted architecture",
                    Content = "Use SQL Server for durable project audit writes.",
                    Summary = "SQL Server is durable storage.",
                    AuthorityLevel = "Accepted",
                    Status = "Active"
                },
                ArtefactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ChunkId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Title = "Accepted architecture",
                ArtefactType = "Decision",
                Snippet = "Use SQL Server for durable project audit writes.",
                FinalScore = 0.91,
                IsStale = false,
                AuthorityLevel = "Accepted",
                SourceEntityType = "Decision",
                SourceEntityId = "42"
            }));

        var evidence = await provider.GetEvidenceAsync(
            projectId: 44,
            userMessage: "what storage did we choose?",
            recentConversationSummary: "user: we discussed storage");

        Assert.AreEqual(1, evidence.Count);
        Assert.AreEqual("semantic-Decision-42", evidence[0].SourceId);
        Assert.AreEqual("Decision", evidence[0].SourceType);
        Assert.AreEqual("Accepted architecture", evidence[0].Title);
        Assert.AreEqual("Accepted", evidence[0].AuthorityLevel);
        Assert.AreEqual("ContextOnly", evidence[0].UsedFor);
        Assert.IsTrue(evidence[0].IsCurrent);
        Assert.AreEqual(0.91, evidence[0].RelevanceScore);
    }

    [TestMethod]
    public async Task SemanticMemory_CannotRevealGovernanceActions()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.84,
              "reason": "Semantic memory is cited context and the user did not ask to commit work."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var decision = await classifier.ClassifyAsync(BuildRequest(
            "what do you think of this old architecture note?",
            contextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "Semantic memory found an old accepted decision.",
                CurrentUserMessage: "what do you think of this old architecture note?",
                SemanticEvidence:
                [
                    new MemoryEvidence(
                        SourceId: "semantic-Decision-99",
                        SourceType: "Decision",
                        Title: "Old ticketization rule",
                        Excerpt: "ForceFormalization AutoCreateTicket RecommendedGateState",
                        IsCurrent: false,
                        RelevanceScore: 0.99,
                        AuthorityLevel: "Accepted",
                        UsedFor: "ShouldAutoFormalize")
                ],
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler)));

        var gate = ChatGovernanceGate.FromDecision(decision);

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        Assert.IsFalse(gate.ShowGovernanceActions);
        Assert.IsFalse(gate.CanCreateTicket);
        Assert.IsFalse(gate.CanSaveDiscussion);
        Assert.AreEqual(0, gate.GovernanceActions.Count);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "UsedFor=ContextOnly");
        Assert.IsFalse(llm.ReceivedPrompts.Single().Contains("UsedFor=ShouldAutoFormalize", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SemanticMemory_CannotForceFormalization()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.86,
              "reason": "Semantic memory is evidence only; the user asked an exploratory question."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);

        var decision = await classifier.ClassifyAsync(new ChatModeClassificationRequest(
            UserMessage: "what do you think about this direction?",
            RecentConversationSummary: string.Empty,
            RouteHint: new ContextAgentRouteDecision
            {
                OriginalUserRequest = "what do you think about this direction?",
                EffectiveWorkText = "what do you think about this direction?",
                RequestKind = ContextRequestKind.CreateTicket,
                Confidence = 0.96,
                Reason = "Hostile test route hint that must not override user text.",
                ContextModeHint = "Formalization",
                AllowTicketCreation = true
            },
            ProjectSummary: "Semantic memory boundary project",
            ContextRequiresClarification: false,
            ExplicitMode: null,
            ContextState: new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "Semantic memory found old committed artifacts.",
                CurrentUserMessage: "what do you think about this direction?",
                SemanticEvidence:
                [
                    new MemoryEvidence(
                        SourceId: "semantic-Decision-force",
                        SourceType: "Decision",
                        Title: "Old forced formalization note",
                        Excerpt: "ForceFormalization SuggestedMode=Formalization AutoCreateTicket",
                        IsCurrent: false,
                        RelevanceScore: 1,
                        AuthorityLevel: "Accepted",
                        UsedFor: "ForceFormalization")
                ],
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler)));

        var prompt = llm.ReceivedPrompts.Single();

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        Assert.IsFalse(ChatGovernanceGate.FromDecision(decision).ShowGovernanceActions);
        StringAssert.Contains(prompt, "SourceId=semantic-Decision-force");
        StringAssert.Contains(prompt, "ContextModeHint=Formalization");
        StringAssert.Contains(prompt, "RequestKind=CreateTicket");
        StringAssert.Contains(prompt, "UsedFor=ContextOnly");
        Assert.IsFalse(prompt.Contains("UsedFor=ForceFormalization", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("SuggestedMode", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("AutoCreateTicket", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SemanticMemoryFailure_IsRecordedAsContextOnlyWarningEvidence()
    {
        var provider = new SemanticMemoryEvidenceProvider(new FailingSemanticMemoryService());

        var evidence = await provider.GetEvidenceAsync(
            projectId: 44,
            userMessage: "what memory applies here?",
            recentConversationSummary: string.Empty);

        Assert.AreEqual(1, evidence.Count);
        var warning = evidence.Single();
        Assert.AreEqual("semantic-retrieval-failure-44", warning.SourceId);
        Assert.AreEqual("SemanticMemoryFailure", warning.SourceType);
        Assert.AreEqual("Unknown", warning.AuthorityLevel);
        Assert.AreEqual("ContextOnly", warning.UsedFor);
        Assert.IsFalse(warning.IsCurrent);
        Assert.AreEqual(0, warning.RelevanceScore);
        Assert.AreEqual(0, warning.RetrievalRank);
        Assert.AreEqual("what memory applies here?", warning.RetrievalQuery);
        Assert.IsFalse(string.IsNullOrWhiteSpace(warning.RetrievalTraceId));
        StringAssert.Contains(warning.MatchReason!, nameof(InvalidOperationException));
        StringAssert.Contains(warning.StalenessReason!, "failed");
    }

    [TestMethod]
    public void ResponseMetadata_RecordsRetrievalEvidenceTraceDetails()
    {
        var builder = new ProjectChatResponseMetadataBuilder();
        var contextState = new ChatContextState(
            RequiresClarification: false,
            ClarificationQuestions: Array.Empty<string>(),
            ContextSummary: "Semantic memory trace test.",
            CurrentUserMessage: "what memory applies here?",
            SemanticEvidence:
            [
                new MemoryEvidence(
                    SourceId: "semantic-Decision-42",
                    SourceType: "Decision",
                    Title: "Accepted audit decision",
                    Excerpt: "Persist audit rows transactionally.",
                    IsCurrent: true,
                    RelevanceScore: 0.92,
                    AuthorityLevel: "Accepted",
                    UsedFor: "ContextOnly",
                    RetrievalTraceId: "trace-123",
                    RetrievalRank: 1,
                    RetrievalQuery: "audit persistence",
                    MatchReason: "semantic similarity and authority boost",
                    VectorSimilarity: 0.84)
            ],
            Origin: ChatContextStateOrigin.ProjectChatResponseCompiler);
        var context = new ProjectChatContextPipelineResult(
            Project: new Project { Id = 44, Name = "Trace Project" },
            Tickets: Array.Empty<ProjectTicket>(),
            Decisions: Array.Empty<ProjectDecision>(),
            Rules: Array.Empty<ProjectRule>(),
            Documents: Array.Empty<ProjectContextDocument>(),
            SemanticMemoryEvidence: contextState.SemanticEvidence!,
            RouteDecision: new ContextAgentRouteDecision(),
            RouteSignals: Array.Empty<string>(),
            ContextAgentResult: new ContextAgentResult { WasSuccessful = true });

        var metadata = builder.Build(
            context,
            new ChatModeDecision(ChatGovernanceMode.Exploration, 0.82, "Exploration keeps memory advisory."),
            "trace-group",
            contextState);
        var trace = string.Join('\n', metadata.ReasoningTrace);

        StringAssert.Contains(trace, "SourceId=semantic-Decision-42");
        StringAssert.Contains(trace, "AuthorityLevel=Accepted");
        StringAssert.Contains(trace, "IsCurrent=True");
        StringAssert.Contains(trace, "RetrievalTraceId=trace-123");
        StringAssert.Contains(trace, "RetrievalRank=1");
        StringAssert.Contains(trace, "RetrievalQuery=audit persistence");
        StringAssert.Contains(trace, "MatchReason=semantic similarity and authority boost");
        StringAssert.Contains(trace, "VectorSimilarity=0.84");
        StringAssert.Contains(trace, "RelevanceScore=0.92");
        StringAssert.Contains(trace, "UsedFor=ContextOnly");
    }

    [TestMethod]
    public async Task SourceGraphLinks_DoNotInfluenceModeOrGate()
    {
        var llm = new StubLlmService(
            """
            {
              "mode": "Exploration",
              "confidence": 0.83,
              "reason": "Source graph links are provenance only."
            }
            """);
        var classifier = new LlmChatModeClassifier(llm);
        var decision = await classifier.ClassifyAsync(BuildRequest(
            "what do you think about this source-linked memory?",
            new ChatContextState(
                RequiresClarification: false,
                ClarificationQuestions: Array.Empty<string>(),
                ContextSummary: "Source graph includes Supersedes and GeneratedFrom provenance.",
                CurrentUserMessage: "what do you think about this source-linked memory?",
                SemanticEvidence:
                [
                    new MemoryEvidence(
                        SourceId: "document-22",
                        SourceType: "Document",
                        Title: "Linked source memory",
                        Excerpt: "Supersedes GeneratedFrom SourceChatMessage SourceDocumentVersion",
                        IsCurrent: true,
                        RelevanceScore: 0.98,
                        AuthorityLevel: "Accepted",
                        UsedFor: "ContextOnly",
                        MatchReason: "source graph provenance")
                ],
                Origin: ChatContextStateOrigin.ProjectChatResponseCompiler)));

        var gate = ChatGovernanceGate.FromDecision(decision);

        Assert.AreEqual(ChatGovernanceMode.Exploration, decision.Mode);
        Assert.IsFalse(gate.ShowGovernanceActions);
        Assert.IsFalse(gate.CanCreateTicket);
        Assert.IsFalse(gate.CanSaveDiscussion);
        Assert.AreEqual(0, gate.GovernanceActions.Count);
        StringAssert.Contains(llm.ReceivedPrompts.Single(), "UsedFor=ContextOnly");
    }

    [TestMethod]
    public void SemanticMemory_OnlyFlowsThroughChatContextState()
    {
        var root = FindRepoRoot();
        var pipeline = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatContextPipeline.cs"));
        var compiler = ReadFile(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatContextStateCompiler.cs"));

        StringAssert.Contains(pipeline, "ISemanticMemoryEvidenceProvider");
        StringAssert.Contains(pipeline, "_semanticMemoryEvidenceProvider.GetEvidenceAsync");
        Assert.IsFalse(pipeline.Contains("ISemanticMemoryService", StringComparison.Ordinal));
        Assert.IsFalse(pipeline.Contains("WeaviateSemanticMemoryService", StringComparison.Ordinal));
        StringAssert.Contains(compiler, "context.SemanticMemoryEvidence");
        StringAssert.Contains(compiler, "UsedFor = \"ContextOnly\"");
        StringAssert.Contains(compiler, "SemanticEvidence: BuildSemanticEvidence(context)");
    }

    [TestMethod]
    public void ClassifiersGateAndResponseService_DoNotCallSemanticMemoryDirectly()
    {
        var root = FindRepoRoot();
        var forbiddenFiles = new[]
        {
            Path.Combine(root, "IronDev.Infrastructure", "Services", "LlmChatModeClassifier.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Services", "LlmChatClarificationClassifier.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectChatResponseService.cs"),
            Path.Combine(root, "IronDev.Core", "Chat", "ChatGovernanceModels.cs")
        };
        var forbiddenTokens = new[]
        {
            "ISemanticMemoryService",
            "WeaviateSemanticMemoryService",
            "BuildContextBundleAsync",
            "SearchAsync("
        };

        foreach (var file in forbiddenFiles)
        {
            var source = ReadFile(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(
                    source.Contains(token, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} must not call semantic memory directly: {token}");
            }
        }
    }

    [TestMethod]
    public void SemanticMemoryModels_DoNotContainGovernanceSuggestionFields()
    {
        var banned = new[]
        {
            "SuggestedMode",
            "SuggestedAction",
            "ShouldShowButton",
            "ShouldAutoFormalize",
            "ShouldInvokeSkill",
            "AutoCreateTicket",
            "RecommendedGateState",
            "ForceFormalization",
            "ForceConfirmation"
        };

        var modelTypes = typeof(SemanticSearchResult).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.KnowledgeCompiler" ||
                           type == typeof(MemoryEvidence) ||
                           type == typeof(AvailableSkillHint))
            .Where(type => !type.IsInterface && !type.IsEnum)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var type in modelTypes)
        {
            var names = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(field => field.Name));

            violations.AddRange(
                from name in names
                from bannedName in banned
                where name.Contains(bannedName, StringComparison.Ordinal)
                select $"{type.FullName}.{name}");
        }

        Assert.AreEqual(0, violations.Count, "Semantic memory governance-suggestion fields found: " + string.Join(", ", violations));
    }

    private static ChatModeClassificationRequest BuildRequest(
        string userMessage,
        ChatContextState contextState) =>
        new(
            UserMessage: userMessage,
            RecentConversationSummary: string.Empty,
            RouteHint: new ContextAgentRouteDecision
            {
                OriginalUserRequest = userMessage,
                EffectiveWorkText = userMessage,
                RequestKind = ContextRequestKind.GeneralChat,
                Confidence = 0.71,
                Reason = "Semantic memory boundary test route.",
                ContextModeHint = "Exploration",
                AllowTicketCreation = false
            },
            ProjectSummary: "Semantic memory boundary project",
            ContextRequiresClarification: false,
            ExplicitMode: null,
            ContextState: contextState);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string ReadFile(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected source file missing: {path}");
        return File.ReadAllText(path);
    }

    private sealed class StubSemanticMemoryService : ISemanticMemoryService
    {
        private readonly IReadOnlyList<SemanticSearchResult> _results;

        public StubSemanticMemoryService(params SemanticSearchResult[] results)
        {
            _results = results;
        }

        public Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(SemanticSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(_results);

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(int projectId, string query, int limit = 8, double minSimilarity = 0.75, CancellationToken ct = default) =>
            Task.FromResult(_results);

        public Task<SemanticContextBundle> BuildContextBundleAsync(int projectId, string query, string callerContext, int limit = 8, CancellationToken ct = default) =>
            Task.FromResult(new SemanticContextBundle
            {
                ProjectId = projectId,
                Query = query,
                CallerContext = callerContext,
                Results = _results
            });

        public Task RebuildIndexAsync(int projectId, IProgress<SemanticIndexRebuildProgress>? progress = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RebuildProjectAsync(int projectId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default) =>
            Task.FromResult(new SemanticMemoryHealth { ProjectId = projectId, ProviderName = "Stub", ProviderStatus = "Healthy" });
    }

    private sealed class FailingSemanticMemoryService : ISemanticMemoryService
    {
        public Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(SemanticSearchQuery query, CancellationToken ct = default) =>
            throw new InvalidOperationException("semantic backend unavailable");

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(int projectId, string query, int limit = 8, double minSimilarity = 0.75, CancellationToken ct = default) =>
            throw new InvalidOperationException("semantic backend unavailable");

        public Task<SemanticContextBundle> BuildContextBundleAsync(int projectId, string query, string callerContext, int limit = 8, CancellationToken ct = default) =>
            throw new InvalidOperationException("semantic backend unavailable");

        public Task RebuildIndexAsync(int projectId, IProgress<SemanticIndexRebuildProgress>? progress = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RebuildProjectAsync(int projectId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default) =>
            Task.FromResult(new SemanticMemoryHealth { ProjectId = projectId, ProviderName = "Failing", ProviderStatus = "Unavailable" });
    }
}

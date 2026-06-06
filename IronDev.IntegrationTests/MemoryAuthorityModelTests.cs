using IronDev.Core.Chat;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryAuthorityModelTests
{
    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsDecisionStates()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromDecisionStatus("Accepted"));
        Assert.AreEqual(MemoryAuthorityLevels.Proposed, MemoryAuthorityNormalizer.FromDecisionStatus("Pending"));
        Assert.AreEqual(MemoryAuthorityLevels.Superseded, MemoryAuthorityNormalizer.FromDecisionStatus("Superseded"));
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, MemoryAuthorityNormalizer.FromDecisionStatus("Rejected"));
    }

    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsDocumentAuthorityAndStatus()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromDocumentAuthority("Binding", "Active"));
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, MemoryAuthorityNormalizer.FromDocumentAuthority("Accepted", "Archived"));
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, MemoryAuthorityNormalizer.FromDocumentAuthority("ObservedFact"));
        Assert.AreEqual(MemoryAuthorityLevels.Superseded, MemoryAuthorityNormalizer.FromDocumentAuthority("Accepted", "Superseded"));
    }

    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsTicketState()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Draft, MemoryAuthorityNormalizer.FromTicketState(isGenerated: true, "Draft"));
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "InProgress"));
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "Closed"));
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "Archived"));
        Assert.AreEqual(MemoryAuthorityLevels.Superseded, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "Superseded"));
    }

    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsRuleEnforcement()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromRuleEnforcementLevel("Required"));
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromRuleEnforcementLevel("Blocking"));
        Assert.AreEqual(MemoryAuthorityLevels.Proposed, MemoryAuthorityNormalizer.FromRuleEnforcementLevel("Advisory"));
        Assert.AreEqual(MemoryAuthorityLevels.Proposed, MemoryAuthorityNormalizer.FromRuleEnforcementLevel("Recommended"));
    }

    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsSemanticAuthority()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromSemanticAuthority("Accepted"));
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, MemoryAuthorityNormalizer.FromSemanticAuthority("ObservedFact"));
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, MemoryAuthorityNormalizer.FromSemanticAuthority("Deprecated"));
    }

    [TestMethod]
    public void MemoryAuthorityNormalizer_UnknownFailsSafe()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Unknown, MemoryAuthorityNormalizer.FromDecisionStatus(""));
        Assert.AreEqual(MemoryAuthorityLevels.Unknown, MemoryAuthorityNormalizer.FromDocumentAuthority("mystery"));
        Assert.AreEqual(MemoryAuthorityLevels.Unknown, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "mystery"));
        Assert.AreEqual(MemoryAuthorityLevels.Unknown, MemoryAuthorityNormalizer.FromRuleEnforcementLevel("mystery"));
        Assert.AreNotEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromSemanticAuthority("mystery"));
    }

    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsRuntimeAndTestEvidence()
    {
        Assert.AreEqual(MemoryAuthorityLevels.RuntimeTrace, MemoryAuthorityNormalizer.RuntimeTrace);
        Assert.AreEqual(MemoryAuthorityLevels.TestEvidence, MemoryAuthorityNormalizer.TestEvidence);
    }

    [TestMethod]
    public void MemoryCurrentnessNormalizer_MarksKnownCurrentAndStaleStates()
    {
        Assert.IsTrue(MemoryCurrentnessNormalizer.FromDecisionStatus("Accepted").IsCurrent);
        Assert.IsFalse(MemoryCurrentnessNormalizer.FromDecisionStatus("Superseded", "decision-2").IsCurrent);
        Assert.AreEqual("decision-2", MemoryCurrentnessNormalizer.FromDecisionStatus("Superseded", "decision-2").SupersededBySourceId);

        Assert.IsTrue(MemoryCurrentnessNormalizer.FromDocumentStatus("Active").IsCurrent);
        Assert.IsFalse(MemoryCurrentnessNormalizer.FromDocumentStatus("Archived").IsCurrent);
        Assert.IsFalse(MemoryCurrentnessNormalizer.FromDocumentStatus("Active", isLatestVersion: false).IsCurrent);

        Assert.IsTrue(MemoryCurrentnessNormalizer.FromTicketState("InProgress").IsCurrent);
        Assert.IsFalse(MemoryCurrentnessNormalizer.FromTicketState("Closed").IsCurrent);

        Assert.IsTrue(MemoryCurrentnessNormalizer.FromRuleEnforcementLevel("Advisory").IsCurrent);
        Assert.IsFalse(MemoryCurrentnessNormalizer.FromRuleEnforcementLevel("Deprecated").IsCurrent);

        Assert.IsFalse(MemoryCurrentnessNormalizer.FromSemanticResult(
            isStale: true,
            MemoryCurrentnessNormalizer.FromDocumentStatus("Active")).IsCurrent);
        Assert.IsFalse(MemoryCurrentnessNormalizer.RuntimeTrace(isCurrentTurn: false).IsCurrent);
    }

    [TestMethod]
    public void ProjectChatContextStateCompiler_NormalizesAuthorityForAllEvidenceTypes()
    {
        var compiler = new ProjectChatContextStateCompiler();
        var context = new ProjectChatContextPipelineResult(
            new Project { Id = 42, Name = "IronDev", Description = "Authority smoke project" },
            [
                new ProjectTicket
                {
                    Id = 10,
                    Title = "Generated ticket",
                    Summary = "Generated draft work",
                    Status = "Draft",
                    IsGenerated = true
                }
            ],
            [
                new ProjectDecision
                {
                    Id = 20,
                    Title = "Accepted decision",
                    Detail = "Use normalized memory authority labels.",
                    Status = "Accepted"
                },
                new ProjectDecision
                {
                    Id = 21,
                    Title = "Superseded decision",
                    Detail = "Old decision retained as evidence.",
                    Status = "Superseded"
                }
            ],
            [
                new ProjectRule
                {
                    Id = 30,
                    Name = "Required rule",
                    Description = "Rules with required enforcement are accepted authority.",
                    EnforcementLevel = "Required"
                }
            ],
            [
                new ProjectContextDocument
                {
                    Id = 40,
                    Title = "Observed document",
                    Content = "This document is observed fact.",
                    Status = "Active",
                    AuthorityLevel = "ObservedFact"
                }
            ],
            [
                new MemoryEvidence(
                    SourceId: "semantic-doc-1",
                    SourceType: "Document",
                    Title: "Semantic hint",
                    Excerpt: "Semantic memory entered as context.",
                    IsCurrent: true,
                    RelevanceScore: 0.92,
                    AuthorityLevel: "Binding",
                    UsedFor: "AutoCreateTicket")
            ],
            new ContextAgentRouteDecision
            {
                RequestKind = ContextRequestKind.GeneralChat,
                EvidenceUsed = ["Route matched general exploration."]
            },
            ["Context route hint: Kind=GeneralChat"],
            new ContextAgentResult
            {
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt,
                ContextSummary = "Compiled authority context."
            });

        var state = compiler.Compile(context, "what next?", string.Empty);
        var evidence = state.SemanticEvidence ?? Array.Empty<MemoryEvidence>();

        Assert.AreEqual(ChatContextStateOrigin.ProjectChatResponseCompiler, state.Origin);
        Assert.IsFalse(state.EpisodicMemoryEnabled);
        Assert.IsTrue(evidence.All(item => item.UsedFor == "ContextOnly"));
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, evidence.Single(item => item.SourceId == "decision-20").AuthorityLevel);
        Assert.IsTrue(evidence.Single(item => item.SourceId == "decision-20").IsCurrent);
        Assert.AreEqual(MemoryAuthorityLevels.Superseded, evidence.Single(item => item.SourceId == "decision-21").AuthorityLevel);
        Assert.IsFalse(evidence.Single(item => item.SourceId == "decision-21").IsCurrent);
        Assert.IsFalse(string.IsNullOrWhiteSpace(evidence.Single(item => item.SourceId == "decision-21").StalenessReason));
        Assert.AreEqual(MemoryAuthorityLevels.Draft, evidence.Single(item => item.SourceId == "ticket-10").AuthorityLevel);
        Assert.IsTrue(evidence.Single(item => item.SourceId == "ticket-10").IsCurrent);
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, evidence.Single(item => item.SourceId == "document-40").AuthorityLevel);
        Assert.IsTrue(evidence.Single(item => item.SourceId == "document-40").IsCurrent);
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, evidence.Single(item => item.SourceId == "rule-30").AuthorityLevel);
        Assert.IsTrue(evidence.Single(item => item.SourceId == "rule-30").IsCurrent);
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, evidence.Single(item => item.SourceId == "semantic-doc-1").AuthorityLevel);
        Assert.IsTrue(evidence.Single(item => item.SourceId == "semantic-doc-1").IsCurrent);
        Assert.AreEqual(MemoryAuthorityLevels.RuntimeTrace, evidence.Single(item => item.SourceId == "route").AuthorityLevel);
        Assert.IsTrue(evidence.Single(item => item.SourceId == "route").IsCurrent);
    }

    [TestMethod]
    public async Task SemanticMemoryEvidenceProvider_NormalizesAuthority()
    {
        var provider = new SemanticMemoryEvidenceProvider(new StubSemanticMemoryService(
        [
            new SemanticSearchResult
            {
                Document = new ProjectContextDocument
                {
                    Id = 100,
                    Title = "Accepted semantic decision",
                    Content = "Accepted semantic memory remains current.",
                    Status = "Active",
                    AuthorityLevel = "Binding"
                },
                SourceEntityType = "Decision",
                SourceEntityId = "100",
                Title = "Accepted semantic decision",
                Snippet = "Accepted semantic memory remains current.",
                AuthorityLevel = "Binding",
                FinalScore = 0.91,
                VectorSimilarity = 0.84,
                MatchReason = "matched accepted decision text",
                IsStale = false
            },
            new SemanticSearchResult
            {
                Document = new ProjectContextDocument
                {
                    Id = 101,
                    Title = "Stale semantic document",
                    Content = "Stale semantic chunks are historical evidence only.",
                    Status = "Active",
                    AuthorityLevel = "ObservedFact"
                },
                SourceEntityType = "Document",
                SourceEntityId = "101",
                Title = "Stale semantic document",
                Snippet = "Stale semantic chunks are historical evidence only.",
                AuthorityLevel = "ObservedFact",
                FinalScore = 0.87,
                IsStale = true
            },
            new SemanticSearchResult
            {
                Document = new ProjectContextDocument
                {
                    Id = 102,
                    Title = "Archived semantic document",
                    Content = "Archived source documents are not current.",
                    Status = "Archived",
                    AuthorityLevel = "Accepted"
                },
                SourceEntityType = "Document",
                SourceEntityId = "102",
                Title = "Archived semantic document",
                Snippet = "Archived source documents are not current.",
                AuthorityLevel = "Accepted",
                FinalScore = 0.86,
                IsStale = false
            }
        ]));

        var evidence = await provider.GetEvidenceAsync(42, "audit persistence", string.Empty);

        var accepted = evidence.Single(item => item.SourceId == "semantic-Decision-100");
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, accepted.AuthorityLevel);
        Assert.IsTrue(accepted.IsCurrent);
        Assert.AreEqual("ContextOnly", accepted.UsedFor);
        Assert.IsFalse(string.IsNullOrWhiteSpace(accepted.RetrievalTraceId));
        Assert.AreEqual(1, accepted.RetrievalRank);
        Assert.AreEqual("audit persistence", accepted.RetrievalQuery);
        Assert.AreEqual("matched accepted decision text", accepted.MatchReason);
        Assert.AreEqual(0.84, accepted.VectorSimilarity);

        var staleChunk = evidence.Single(item => item.SourceId == "semantic-Document-101");
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, staleChunk.AuthorityLevel);
        Assert.IsFalse(staleChunk.IsCurrent);
        Assert.IsFalse(string.IsNullOrWhiteSpace(staleChunk.StalenessReason));

        var archivedSource = evidence.Single(item => item.SourceId == "semantic-Document-102");
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, archivedSource.AuthorityLevel);
        Assert.IsFalse(archivedSource.IsCurrent);
        Assert.IsFalse(string.IsNullOrWhiteSpace(archivedSource.StalenessReason));
    }

    [TestMethod]
    public void StaleMemory_DoesNotRevealGovernanceActions()
    {
        var staleEvidence = new MemoryEvidence(
            SourceId: "semantic-Decision-old",
            SourceType: "Decision",
            Title: "Old accepted decision",
            Excerpt: "Historical evidence only.",
            IsCurrent: false,
            AuthorityLevel: MemoryAuthorityLevels.Accepted,
            UsedFor: "ContextOnly",
            StalenessReason: "Decision has been superseded.",
            SupersededBySourceId: "decision-new");
        var contextState = new ChatContextState(
            RequiresClarification: false,
            ClarificationQuestions: [],
            ContextSummary: "Stale evidence is advisory only.",
            SemanticEvidence: [staleEvidence],
            Origin: ChatContextStateOrigin.ProjectChatResponseCompiler);

        var gate = ChatGovernanceGate.FromDecision(new ChatModeDecision(
            ChatGovernanceMode.Exploration,
            0.9,
            "Stale memory does not create governance authority."));

        Assert.IsFalse(contextState.SemanticEvidence!.Single().IsCurrent);
        Assert.IsFalse(gate.ShowGovernanceActions);
        Assert.IsFalse(gate.CanCreateTicket);
    }

    private sealed class StubSemanticMemoryService : ISemanticMemoryService
    {
        private readonly IReadOnlyList<SemanticSearchResult> _results;

        public StubSemanticMemoryService(IReadOnlyList<SemanticSearchResult> results)
        {
            _results = results;
        }

        public Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
            SemanticSearchQuery query,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
            int projectId,
            string query,
            int limit = 8,
            double minSimilarity = 0.75,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SemanticContextBundle> BuildContextBundleAsync(
            int projectId,
            string query,
            string callerContext,
            int limit = 8,
            CancellationToken ct = default) =>
            Task.FromResult(new SemanticContextBundle
            {
                ProjectId = projectId,
                Query = query,
                CallerContext = callerContext,
                Results = _results
            });

        public Task RebuildIndexAsync(
            int projectId,
            IProgress<SemanticIndexRebuildProgress>? progress = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RebuildProjectAsync(int projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}

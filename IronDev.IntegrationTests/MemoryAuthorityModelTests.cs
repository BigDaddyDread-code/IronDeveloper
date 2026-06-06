using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryAuthorityModelTests
{
    [TestMethod]
    public void MemoryAuthorityNormalizer_MapsKnownMemoryStates()
    {
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromDecisionStatus("Accepted"));
        Assert.AreEqual(MemoryAuthorityLevels.Proposed, MemoryAuthorityNormalizer.FromDecisionStatus("Pending"));
        Assert.AreEqual(MemoryAuthorityLevels.Superseded, MemoryAuthorityNormalizer.FromDecisionStatus("Superseded"));

        Assert.AreEqual(MemoryAuthorityLevels.Accepted, MemoryAuthorityNormalizer.FromDocumentAuthority("Binding", "Active"));
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, MemoryAuthorityNormalizer.FromDocumentAuthority("Accepted", "Archived"));
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, MemoryAuthorityNormalizer.FromDocumentAuthority("ObservedFact"));

        Assert.AreEqual(MemoryAuthorityLevels.Draft, MemoryAuthorityNormalizer.FromTicketState(isGenerated: true, "Draft"));
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "InProgress"));
        Assert.AreEqual(MemoryAuthorityLevels.Deprecated, MemoryAuthorityNormalizer.FromTicketState(isGenerated: false, "Archived"));

        Assert.AreEqual(MemoryAuthorityLevels.RuntimeTrace, MemoryAuthorityNormalizer.RuntimeTrace);
        Assert.AreEqual(MemoryAuthorityLevels.TestEvidence, MemoryAuthorityNormalizer.TestEvidence);
    }

    [TestMethod]
    public void ProjectChatContextStateCompiler_NormalizesAuthorityAndKeepsEvidenceContextOnly()
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
        Assert.AreEqual(MemoryAuthorityLevels.Draft, evidence.Single(item => item.SourceId == "ticket-10").AuthorityLevel);
        Assert.AreEqual(MemoryAuthorityLevels.ObservedFact, evidence.Single(item => item.SourceId == "document-40").AuthorityLevel);
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, evidence.Single(item => item.SourceId == "rule-30").AuthorityLevel);
        Assert.AreEqual(MemoryAuthorityLevels.Accepted, evidence.Single(item => item.SourceId == "semantic-doc-1").AuthorityLevel);
        Assert.AreEqual(MemoryAuthorityLevels.RuntimeTrace, evidence.Single(item => item.SourceId == "route").AuthorityLevel);
    }
}

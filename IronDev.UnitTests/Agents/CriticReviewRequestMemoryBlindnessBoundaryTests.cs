using System.Reflection;
using IronDev.Core.Agents.Concrete;

namespace IronDev.UnitTests.Agents;

// The independent Critic must review from outside the build. It sees the work
// package and proven evidence, never the team's global/collective memory or its
// own narrative — otherwise it inherits the very blind spots and rationalisations
// it exists to attack. "Outside memory" is not "outside evidence": receipts and
// evidence are proven, provenance-bound facts the Critic can trust; memory is a
// belief the team formed and must not enter the Critic's input.
//
// These tests lock that guarantee at the contract surface: CriticReviewRequest
// carries no property through which memory or narrative could be ingested. A new
// field that reopens that channel must trip the exact-surface allowlist below and
// force a conscious decision, not slip in silently.
[TestClass]
[TestCategory("Critic")]
[TestCategory("CriticMemoryBlindnessBoundary")]
public sealed class CriticReviewRequestMemoryBlindnessBoundaryTests
{
    private static readonly string[] ApprovedRequestSurface =
    [
        nameof(CriticReviewRequest.ReviewRequestId),
        nameof(CriticReviewRequest.SubjectType),
        nameof(CriticReviewRequest.SubjectId),
        nameof(CriticReviewRequest.ScopeId),
        nameof(CriticReviewRequest.EvidenceRefs),
        nameof(CriticReviewRequest.RequestedByUserId),
        nameof(CriticReviewRequest.RequestedByAgentId),
        nameof(CriticReviewRequest.CorrelationId)
    ];

    [TestMethod]
    public void CriticReviewRequest_ExposesOnlyTheApprovedWorkPackageSurface()
    {
        var actual = typeof(CriticReviewRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            ApprovedRequestSurface,
            actual,
            "CriticReviewRequest surface changed. The Critic reviews from outside memory: a new " +
            "property must not open a memory/narrative ingestion channel. If the field is a " +
            "reference to a reviewable subject or proven evidence, add it to ApprovedRequestSurface; " +
            "if it carries team memory, conversation, or reasoning, it breaks the boundary.");
    }

    [TestMethod]
    public void CriticReviewRequest_HasNoMemoryOrNarrativeIngestionSurface()
    {
        var propertyNames = string.Join(
            "\n",
            typeof(CriticReviewRequest)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name));

        AssertNoForbiddenTokens(
            propertyNames,
            "Memory",
            "Collective",
            "Global",
            "Recall",
            "History",
            "Conversation",
            "Reasoning",
            "ThoughtLedger",
            "Narrative",
            "Belief",
            "Prior",
            "Context",
            "Scratchpad");
    }

    [TestMethod]
    public void CriticReviewRequest_RetainsEvidenceSurface_BecauseOutsideMemoryIsNotOutsideEvidence()
    {
        var evidence = typeof(CriticReviewRequest).GetProperty(nameof(CriticReviewRequest.EvidenceRefs));

        Assert.IsNotNull(
            evidence,
            "The Critic must still receive proven evidence references; blindness applies to memory, not evidence.");
        Assert.IsTrue(
            typeof(System.Collections.IEnumerable).IsAssignableFrom(evidence!.PropertyType),
            "EvidenceRefs must be a collection of evidence references.");
    }

    [TestMethod]
    public void CriticReviewSubjectType_MayReviewMemoryAsSubject_ButOnlyByReference()
    {
        // Reviewing a memory proposal is legitimate: the memory is the SUBJECT under
        // attack, passed by reference id — not context the Critic reads to form its
        // own view. This is deliberately allowed and must not be "fixed" by removing
        // these subject types. The boundary is ingestion, not subject.
        var subjectTypes = Enum.GetNames<CriticReviewSubjectType>();

        CollectionAssert.Contains(subjectTypes, nameof(CriticReviewSubjectType.MemoryProposal));
        CollectionAssert.Contains(subjectTypes, nameof(CriticReviewSubjectType.CollectiveMemoryPromotionRequest));

        var subjectId = typeof(CriticReviewRequest).GetProperty(nameof(CriticReviewRequest.SubjectId));
        Assert.IsNotNull(subjectId);
        Assert.AreEqual(
            typeof(string),
            subjectId!.PropertyType,
            "A memory subject must enter as a reference id, never as embedded memory content.");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(
                text.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Forbidden memory/narrative token found on the Critic input surface: {token}");
    }
}

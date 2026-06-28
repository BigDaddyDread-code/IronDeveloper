using IronDev.Core.Agents;

namespace IronDev.UnitTests.Conscience;

internal static class ConsciencePolicyDecisionTestFixtures
{
    internal const string DefaultEvidenceRef = "evidence:g07b";
    internal const string DefaultObservedProject = "ProjectA";
    internal const string DefaultAffectedProject = "ProjectA";
    internal const string DefaultMemoryRef = "memory:context-only";
    internal const string DisposableWorkspaceBoundary = "disposable workspace";

    internal static ConsciencePolicyDecisionRequest Request(
        string actionType = "review evidence",
        string observedProject = DefaultObservedProject,
        string affectedProject = DefaultAffectedProject,
        IReadOnlyList<string>? evidence = null,
        IReadOnlyList<string>? requestedTools = null,
        IReadOnlyList<string>? memoryAuthorityRefs = null,
        IReadOnlyList<string>? safetyBoundaryRefs = null)
    {
        return new ConsciencePolicyDecisionRequest
        {
            ActionType = actionType,
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            Evidence = evidence ?? [DefaultEvidenceRef],
            RequestedTools = requestedTools ?? [],
            MemoryAuthorityRefs = memoryAuthorityRefs ?? [DefaultMemoryRef],
            SafetyBoundaryRefs = safetyBoundaryRefs ?? [DisposableWorkspaceBoundary]
        };
    }

    internal static ConsciencePolicyDecision Evaluate(
        string actionType = "review evidence",
        string observedProject = DefaultObservedProject,
        string affectedProject = DefaultAffectedProject,
        IReadOnlyList<string>? evidence = null,
        IReadOnlyList<string>? requestedTools = null,
        IReadOnlyList<string>? memoryAuthorityRefs = null,
        IReadOnlyList<string>? safetyBoundaryRefs = null)
    {
        return ConsciencePolicyDecisionEvaluator.Evaluate(Request(
            actionType,
            observedProject,
            affectedProject,
            evidence,
            requestedTools,
            memoryAuthorityRefs,
            safetyBoundaryRefs));
    }

    internal static void AssertBlock(
        ConsciencePolicyDecision decision,
        string expectedBoundary,
        string expectedBlockingText)
    {
        Assert.AreEqual("Block", decision.Decision);
        CollectionAssert.Contains(decision.ViolatedBoundaries.ToList(), expectedBoundary);
        AssertContains(decision.BlockingFactors, expectedBlockingText);
        CollectionAssert.Contains(
            decision.RequiredNextSteps.ToList(),
            "Stop this action and produce a failure/safety package for Codex or human review.");
    }

    internal static void AssertNeedsMoreEvidence(
        ConsciencePolicyDecision decision,
        string expectedMissingEvidence)
    {
        Assert.AreEqual("NeedsMoreEvidence", decision.Decision);
        CollectionAssert.Contains(decision.MissingEvidence.ToList(), expectedMissingEvidence);
        CollectionAssert.Contains(
            decision.RequiredNextSteps.ToList(),
            "Collect the missing evidence before reviewing the action again.");
    }

    internal static void AssertAllow(ConsciencePolicyDecision decision)
    {
        Assert.AreEqual("Allow", decision.Decision);
        Assert.AreEqual(0.82m, decision.Confidence);
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "Action is evidence-backed.");
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "Project identity is explicit.");
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "No blocked mutation boundary was requested.");
        Assert.AreEqual(0, decision.BlockingFactors.Count);
        Assert.AreEqual(0, decision.MissingEvidence.Count);
        Assert.AreEqual(0, decision.ViolatedBoundaries.Count);
    }

    internal static void AssertContains(IReadOnlyList<string> values, string expectedText)
    {
        Assert.IsTrue(
            values.Any(value => value.Contains(expectedText, StringComparison.OrdinalIgnoreCase)),
            $"Expected one value to contain '{expectedText}'. Actual: {string.Join(" | ", values)}");
    }
}

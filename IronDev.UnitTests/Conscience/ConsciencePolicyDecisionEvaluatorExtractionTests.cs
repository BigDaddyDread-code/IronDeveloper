using IronDev.Core.Agents;
using System.Reflection;
using System.Xml.Linq;

namespace IronDev.UnitTests.Conscience;

[TestClass]
public sealed class ConsciencePolicyDecisionEvaluatorExtractionTests
{
    [TestMethod]
    public void CoreEvaluatorBlocksSelfApproval()
    {
        var decision = Evaluate(actionType: "self-approve", requestedTools: ["git"]);

        Assert.AreEqual("Block", decision.Decision);
        CollectionAssert.Contains(decision.ViolatedBoundaries.ToList(), "NoAgentSelfApproval");
        Assert.IsTrue(decision.BlockingFactors.Any(static factor => factor.Contains("self-approval", StringComparison.OrdinalIgnoreCase)));
        CollectionAssert.Contains(decision.RequiredNextSteps.ToList(), "Stop this action and produce a failure/safety package for Codex or human review.");
        Assert.AreEqual(0.90m, decision.Confidence);
    }

    [TestMethod]
    public void CoreEvaluatorBlocksGovernanceBypass()
    {
        var decision = Evaluate(actionType: "override governance", requestedTools: ["git"]);

        Assert.AreEqual("Block", decision.Decision);
        CollectionAssert.Contains(decision.ViolatedBoundaries.ToList(), "GovernanceGatesCannotBeBypassed");
        Assert.IsTrue(decision.BlockingFactors.Any(static factor => factor.Contains("governance gate", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CoreEvaluatorNeedsMoreEvidenceForDisposableWorkspaceWithoutBoundary()
    {
        var decision = Evaluate(
            actionType: "disposable workspace apply patch",
            requestedTools: ["apply"],
            safetyBoundaryRefs: []);

        Assert.AreEqual("NeedsMoreEvidence", decision.Decision);
        CollectionAssert.Contains(decision.MissingEvidence.ToList(), "Disposable workspace action requires explicit workspace boundary evidence.");
        CollectionAssert.Contains(decision.RequiredNextSteps.ToList(), "Collect the missing evidence before reviewing the action again.");
        Assert.AreEqual(0.67m, decision.Confidence);
    }

    [TestMethod]
    public void CoreEvaluatorAllowsEvidenceBackedSafeDisposableWorkspaceReviewOnly()
    {
        var decision = Evaluate(
            actionType: "disposable workspace apply patch",
            requestedTools: ["apply"],
            safetyBoundaryRefs: ["disposable workspace", "outside real repo", "before hash", "after hash"]);

        Assert.AreEqual("Allow", decision.Decision);
        Assert.AreEqual(0.82m, decision.Confidence);
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "Action is evidence-backed.");
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "Project identity is explicit.");
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "No blocked mutation boundary was requested.");
        CollectionAssert.Contains(decision.AllowingFactors.ToList(), "Disposable workspace boundary is explicit.");
        AssertNoAuthorityGranted(decision);
    }

    [TestMethod]
    public void CoreEvaluatorDecisionDoesNotGrantAuthority()
    {
        var decision = Evaluate();

        Assert.AreEqual("Allow", decision.Decision);
        AssertNoAuthorityGranted(decision);
        StringAssert.Contains(decision.Boundary, "reviews only");
        StringAssert.Contains(decision.Boundary, "does not patch");
        StringAssert.Contains(decision.Boundary, "does not patch, create tickets, mutate memory, or approve itself");
    }

    [TestMethod]
    public void CoreEvaluatorTestsRemainCoreOnly()
    {
        var projectPath = Path.Combine(RepoRoot(), "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var project = XDocument.Load(projectPath);

        var projectReferences = project.Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project.Descendants("PackageReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "..\\IronDev.Core\\IronDev.Core.csproj" }, projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);

        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(RepoRoot(), "IronDev.UnitTests", "Conscience"), "*.cs")
                .Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            string.Concat("IronDev.", "Infrastructure"),
            string.Concat("Conscience", "Agent"),
            string.Concat("Agent", "Model", "Resolver"),
            string.Concat("Static", "IronDev", "Agent"),
            string.Concat("Web", "Application", "Factory"),
            string.Concat("Test", "Server"),
            string.Concat("Http", "Client"),
            string.Concat("Db", "Context"),
            string.Concat("Sql", "Connection"),
            string.Concat("DateTimeOffset.", "UtcNow"),
            string.Concat("File.", "Write"),
            string.Concat("Process.", "Start"),
            string.Concat("Pro", "vider"),
            string.Concat("Memory", "Retrieval"),
            string.Concat("Tool", "Execution")
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
        }
    }

    private static ConsciencePolicyDecision Evaluate(
        string actionType = "disposable workspace inspect evidence",
        string observedProject = "ProjectA",
        string affectedProject = "ProjectA",
        IReadOnlyList<string>? evidence = null,
        IReadOnlyList<string>? requestedTools = null,
        IReadOnlyList<string>? memoryAuthorityRefs = null,
        IReadOnlyList<string>? safetyBoundaryRefs = null)
    {
        return ConsciencePolicyDecisionEvaluator.Evaluate(new ConsciencePolicyDecisionRequest
        {
            ActionType = actionType,
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            Evidence = evidence ?? ["evidence:reviewed"],
            RequestedTools = requestedTools ?? [],
            MemoryAuthorityRefs = memoryAuthorityRefs ?? ["memory:context-only"],
            SafetyBoundaryRefs = safetyBoundaryRefs ?? ["disposable workspace"]
        });
    }

    private static void AssertNoAuthorityGranted(ConsciencePolicyDecision decision)
    {
        Assert.AreEqual(ConsciencePolicyDecisionEvaluator.ReviewBoundary, decision.Boundary);
        Assert.IsFalse(decision.Boundary.Contains("authority granted", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(decision.Boundary.Contains("policy satisfied", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(decision.Boundary.Contains("workflow continuation", StringComparison.OrdinalIgnoreCase));

        var authorityShapedMembers = typeof(ConsciencePolicyDecision)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Where(static name =>
                name.StartsWith("Can", StringComparison.Ordinal) ||
                name.Contains("Authority", StringComparison.Ordinal) ||
                name.Contains("Approval", StringComparison.Ordinal) ||
                name.Contains("PolicySatisfaction", StringComparison.Ordinal) ||
                name.Contains("Mutation", StringComparison.Ordinal) ||
                name.Contains("WorkflowContinuation", StringComparison.Ordinal))
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "AuthoritySources" }, authorityShapedMembers);
    }

    private static string RepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        Assert.Fail("Could not locate repository root.");
        return string.Empty;
    }
}

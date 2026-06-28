using IronDev.Core.Agents;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace IronDev.UnitTests.Conscience;

[TestClass]
public sealed class ConsciencePolicyDecisionEvaluatorUnitTests
{
    [TestMethod]
    [DataRow("self-approve")]
    [DataRow("self approve")]
    [DataRow("approve itself")]
    [DataRow("auto-merge")]
    [DataRow("automerge")]
    public void SelfApprovalAndAutomergeTextBlocks(string actionText)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: actionText);

        ConsciencePolicyDecisionTestFixtures.AssertBlock(
            decision,
            "NoAgentSelfApproval",
            "self-approval");
        Assert.AreEqual(0.90m, decision.Confidence);
    }

    [TestMethod]
    [DataRow("bypass")]
    [DataRow("skip conscience")]
    [DataRow("skip thoughtledger")]
    [DataRow("override governance")]
    public void GovernanceBypassTextBlocks(string actionText)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: actionText);

        ConsciencePolicyDecisionTestFixtures.AssertBlock(
            decision,
            "GovernanceGatesCannotBeBypassed",
            "governance gate");
    }

    [TestMethod]
    [DataRow("write real repo")]
    [DataRow("write real repository")]
    [DataRow("write production source")]
    [DataRow("write developer working tree")]
    public void RealRepositoryAndDeveloperTreeTextBlocks(string actionText)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: actionText);

        ConsciencePolicyDecisionTestFixtures.AssertBlock(
            decision,
            "NoRealRepositoryWrites",
            "real repository or developer working tree");
    }

    [TestMethod]
    [DataRow("TesterAgent repair")]
    [DataRow("TesterAgent fix")]
    [DataRow("TesterAgent patch")]
    [DataRow("TesterAgent write")]
    public void TesterRoleRepairOrMutationTextBlocks(string actionText)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: actionText);

        ConsciencePolicyDecisionTestFixtures.AssertBlock(
            decision,
            "TesterAgentExecutesOnly",
            "execute/report");
    }

    [TestMethod]
    [DataRow("SentinelAgent create ticket")]
    [DataRow("SentinelAgent patch")]
    [DataRow("SentinelAgent mutate")]
    [DataRow("SentinelAgent write")]
    public void SentinelRoleMutationTextBlocks(string actionText)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: actionText);

        ConsciencePolicyDecisionTestFixtures.AssertBlock(
            decision,
            "SentinelAgentObservesOnly",
            "mutate state instead of observe");
    }

    [TestMethod]
    [DataRow("ResearchAgent override accepted memory")]
    [DataRow("ResearchAgent authority claim")]
    [DataRow("ResearchAgent replace accepted project memory")]
    [DataRow("ResearchAgent change architecture")]
    public void ResearchRoleAuthorityOverrideTextBlocks(string actionText)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: actionText);

        ConsciencePolicyDecisionTestFixtures.AssertBlock(
            decision,
            "ProjectMemoryRemainsAuthority",
            "accepted project memory");
    }

    [TestMethod]
    public void MissingActionTypeNeedsMoreEvidence()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: " ");

        ConsciencePolicyDecisionTestFixtures.AssertNeedsMoreEvidence(decision, "actionType is required.");
        Assert.AreEqual(0.67m, decision.Confidence);
    }

    [TestMethod]
    public void MissingObservedProjectNeedsMoreEvidence()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(observedProject: string.Empty);

        ConsciencePolicyDecisionTestFixtures.AssertNeedsMoreEvidence(
            decision,
            "observedProject and affectedProject must be explicit.");
    }

    [TestMethod]
    public void MissingAffectedProjectNeedsMoreEvidence()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(affectedProject: string.Empty);

        ConsciencePolicyDecisionTestFixtures.AssertNeedsMoreEvidence(
            decision,
            "observedProject and affectedProject must be explicit.");
    }

    [TestMethod]
    public void MissingBothProjectsAddsOneProjectIdentityIssue()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            observedProject: string.Empty,
            affectedProject: " ");

        Assert.AreEqual("NeedsMoreEvidence", decision.Decision);
        Assert.AreEqual(
            1,
            decision.MissingEvidence.Count(issue =>
                issue == "observedProject and affectedProject must be explicit."));
    }

    [TestMethod]
    public void MissingEvidenceNeedsMoreEvidence()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(evidence: []);

        ConsciencePolicyDecisionTestFixtures.AssertNeedsMoreEvidence(
            decision,
            "At least one evidence item is required.");
    }

    [TestMethod]
    [DataRow("disposable workspace apply", new string[] { })]
    [DataRow("disposable workspace patch", new string[] { })]
    public void DisposableWorkspaceApplyOrPatchWithoutBoundaryNeedsMoreEvidence(
        string actionText,
        string[] boundaryRefs)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            actionType: actionText,
            safetyBoundaryRefs: boundaryRefs);

        ConsciencePolicyDecisionTestFixtures.AssertNeedsMoreEvidence(
            decision,
            "Disposable workspace action requires explicit workspace boundary evidence.");
    }

    [TestMethod]
    [DataRow("disposable workspace")]
    [DataRow("outside real repo")]
    [DataRow("before hash")]
    [DataRow("after hash")]
    public void DisposableWorkspaceApplyWithExplicitBoundaryAllowsReview(string boundaryRef)
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            actionType: "disposable workspace apply patch",
            requestedTools: ["apply"],
            safetyBoundaryRefs: [boundaryRef]);

        ConsciencePolicyDecisionTestFixtures.AssertAllow(decision);
    }

    [TestMethod]
    public void DisposableWorkspaceBoundaryAddsExplicitAllowingFactor()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            actionType: "disposable workspace apply patch",
            safetyBoundaryRefs: ["disposable workspace"]);

        ConsciencePolicyDecisionTestFixtures.AssertAllow(decision);
        CollectionAssert.Contains(
            decision.AllowingFactors.ToList(),
            "Disposable workspace boundary is explicit.");
    }

    [TestMethod]
    public void SafeEvidenceBackedReviewAllows()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate();

        ConsciencePolicyDecisionTestFixtures.AssertAllow(decision);
        CollectionAssert.AreEqual(decision.AllowingFactors.ToList(), decision.Reasons.ToList());
        Assert.AreEqual(0, decision.RequiredNextSteps.Count);
    }

    [TestMethod]
    public void AllowConfidenceIsStable()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate();

        Assert.AreEqual("Allow", decision.Decision);
        Assert.AreEqual(0.82m, decision.Confidence);
    }

    [TestMethod]
    public void NeedsMoreEvidenceConfidenceIsStable()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(evidence: []);

        Assert.AreEqual("NeedsMoreEvidence", decision.Decision);
        Assert.AreEqual(0.67m, decision.Confidence);
    }

    [TestMethod]
    public void SingleBlockConfidenceIsStable()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: "self-approve");

        Assert.AreEqual("Block", decision.Decision);
        Assert.AreEqual(0.90m, decision.Confidence);
    }

    [TestMethod]
    public void MultipleBlocksIncreaseConfidenceButCapAtPoint95()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            actionType: "self-approve override governance write real repo TesterAgent repair SentinelAgent mutate ResearchAgent authority");

        Assert.AreEqual("Block", decision.Decision);
        Assert.IsTrue(decision.BlockingFactors.Count >= 5);
        Assert.AreEqual(0.95m, decision.Confidence);
    }

    [TestMethod]
    public void BlockingFactorsWinOverMissingEvidenceForDecision()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            actionType: "self-approve",
            observedProject: string.Empty,
            evidence: []);

        Assert.AreEqual("Block", decision.Decision);
        Assert.AreEqual(0.90m, decision.Confidence);
        CollectionAssert.AreEqual(decision.BlockingFactors.ToList(), decision.Reasons.ToList());
        Assert.IsTrue(decision.MissingEvidence.Count >= 2);
        CollectionAssert.Contains(
            decision.RequiredNextSteps.ToList(),
            "Stop this action and produce a failure/safety package for Codex or human review.");
        CollectionAssert.Contains(
            decision.RequiredNextSteps.ToList(),
            "Collect the missing evidence before reviewing the action again.");
    }

    [TestMethod]
    public void NeedsMoreEvidenceDecisionDoesNotIncludeStopNextStepWithoutBlockingFactor()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(evidence: []);

        Assert.AreEqual("NeedsMoreEvidence", decision.Decision);
        Assert.AreEqual(0, decision.BlockingFactors.Count);
        CollectionAssert.DoesNotContain(
            decision.RequiredNextSteps.ToList(),
            "Stop this action and produce a failure/safety package for Codex or human review.");
    }

    [TestMethod]
    public void DecisionOutputHasExpectedJsonFields()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(decision));

        var names = document.RootElement.EnumerateObject()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "affectedProject",
                "allowingFactors",
                "authoritySources",
                "blockingFactors",
                "boundary",
                "confidence",
                "decision",
                "missingEvidence",
                "observedProject",
                "reasons",
                "requestedTools",
                "requiredNextSteps",
                "violatedBoundaries"
            },
            names);
    }

    [TestMethod]
    public void BoundaryTextIsStableAndReviewOnly()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate();

        Assert.AreEqual(ConsciencePolicyDecisionEvaluator.ReviewBoundary, decision.Boundary);
        StringAssert.Contains(decision.Boundary, "reviews only");
        StringAssert.Contains(decision.Boundary, "does not patch");
        StringAssert.Contains(decision.Boundary, "does not patch, create tickets, mutate memory, or approve itself");
    }

    [TestMethod]
    public void DecisionModelHasNoActionAuthorityFlags()
    {
        var forbiddenMembers = typeof(ConsciencePolicyDecision)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Where(static name =>
                name.StartsWith("Can", StringComparison.Ordinal) ||
                name.Contains("Approve", StringComparison.Ordinal) ||
                name.Contains("Execute", StringComparison.Ordinal) ||
                name.Contains("Mutate", StringComparison.Ordinal) ||
                name.Contains("Merge", StringComparison.Ordinal) ||
                name.Contains("Release", StringComparison.Ordinal) ||
                name.Contains("Deploy", StringComparison.Ordinal) ||
                name.Contains("PolicySatisfaction", StringComparison.Ordinal) ||
                name.Contains("WorkflowContinuation", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, forbiddenMembers.Length, string.Join(", ", forbiddenMembers));
    }

    [TestMethod]
    public void AuthoritySourcesAreContextReferencesOnly()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            memoryAuthorityRefs: ["memory:context-only", "memory:prior-review"]);

        ConsciencePolicyDecisionTestFixtures.AssertAllow(decision);
        CollectionAssert.AreEqual(
            new[] { "memory:context-only", "memory:prior-review" },
            decision.AuthoritySources.ToArray());
    }

    [TestMethod]
    public void RequestedToolsAreEchoedButNotExecuted()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(
            actionType: "review evidence",
            requestedTools: ["git", "apply"]);

        ConsciencePolicyDecisionTestFixtures.AssertAllow(decision);
        CollectionAssert.AreEqual(new[] { "git", "apply" }, decision.RequestedTools.ToArray());
        Assert.AreEqual(0, decision.RequiredNextSteps.Count);
    }

    [TestMethod]
    public void AllowDoesNotGrantDownstreamAuthority()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate();

        ConsciencePolicyDecisionTestFixtures.AssertAllow(decision);
        AssertDecisionOnly(decision);
    }

    [TestMethod]
    public void BlockDoesNotAuthorizeRepair()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(actionType: "self-approve");

        Assert.AreEqual("Block", decision.Decision);
        AssertDecisionOnly(decision);
        Assert.IsFalse(
            decision.RequiredNextSteps.Any(static step =>
                step.Contains("repair authorized", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void NeedsMoreEvidenceDoesNotInferPermission()
    {
        var decision = ConsciencePolicyDecisionTestFixtures.Evaluate(evidence: []);

        Assert.AreEqual("NeedsMoreEvidence", decision.Decision);
        AssertDecisionOnly(decision);
        ConsciencePolicyDecisionTestFixtures.AssertContains(decision.RequiredNextSteps, "Collect the missing evidence");
    }

    [TestMethod]
    public void RequestNullCollectionsAreTreatedAsEmpty()
    {
        var decision = ConsciencePolicyDecisionEvaluator.Evaluate(new ConsciencePolicyDecisionRequest
        {
            ActionType = "review evidence",
            ObservedProject = ConsciencePolicyDecisionTestFixtures.DefaultObservedProject,
            AffectedProject = ConsciencePolicyDecisionTestFixtures.DefaultAffectedProject,
            Evidence = null!,
            RequestedTools = null!,
            MemoryAuthorityRefs = null!,
            SafetyBoundaryRefs = null!
        });

        ConsciencePolicyDecisionTestFixtures.AssertNeedsMoreEvidence(
            decision,
            "At least one evidence item is required.");
        Assert.AreEqual(0, decision.RequestedTools.Count);
        Assert.AreEqual(0, decision.AuthoritySources.Count);
    }

    [TestMethod]
    public void UnitProjectStillReferencesOnlyCoreAndMSTest()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot(), "IronDev.UnitTests", "IronDev.UnitTests.csproj"));

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
    }

    [TestMethod]
    public void G07bSourceDoesNotUseRuntimeOrExternalDependencies()
    {
        var source = string.Join(
            Environment.NewLine,
            new[]
            {
                Path.Combine(RepoRoot(), "IronDev.UnitTests", "Conscience", "ConsciencePolicyDecisionEvaluatorUnitTests.cs"),
                Path.Combine(RepoRoot(), "IronDev.UnitTests", "Conscience", "ConsciencePolicyDecisionTestFixtures.cs")
            }.Select(File.ReadAllText));

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

    private static void AssertDecisionOnly(ConsciencePolicyDecision decision)
    {
        Assert.AreEqual(ConsciencePolicyDecisionEvaluator.ReviewBoundary, decision.Boundary);

        foreach (var value in decision.AllowingFactors
                     .Concat(decision.BlockingFactors)
                     .Concat(decision.MissingEvidence)
                     .Concat(decision.RequiredNextSteps)
                     .Concat(decision.ViolatedBoundaries))
        {
            Assert.IsFalse(value.Contains("authority granted", StringComparison.OrdinalIgnoreCase), value);
            Assert.IsFalse(value.Contains("policy satisfied", StringComparison.OrdinalIgnoreCase), value);
            Assert.IsFalse(value.Contains("workflow continuation", StringComparison.OrdinalIgnoreCase), value);
            Assert.IsFalse(value.Contains("release authorized", StringComparison.OrdinalIgnoreCase), value);
            Assert.IsFalse(value.Contains("deployment authorized", StringComparison.OrdinalIgnoreCase), value);
            Assert.IsFalse(value.Contains("merge authorized", StringComparison.OrdinalIgnoreCase), value);
        }
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

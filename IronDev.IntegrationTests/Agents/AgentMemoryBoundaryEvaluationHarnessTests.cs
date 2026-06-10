using IronDev.Core.Agents.Evaluation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentMemoryBoundaryEvaluationHarnessTests
{
    private static readonly DateTimeOffset EvaluationTime = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_AllScenariosPass()
    {
        var result = Evaluate();

        Assert.IsTrue(result.Passed, FormatViolations(result));
        Assert.AreEqual("agent-memory-boundary-1781049600", result.EvaluationRunId);
        Assert.AreEqual(EvaluationTime, result.EvaluatedAt);
        Assert.AreEqual(16, result.Scenarios.Count);
        Assert.AreEqual(0, result.Violations.Count);
        CollectionAssert.AreEquivalent(
            Enum.GetValues<AgentMemoryBoundaryScenarioType>().Cast<object>().ToArray(),
            result.Scenarios.Select(scenario => (object)scenario.ScenarioType).ToArray());
    }

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_ProducesStructuredScenarioAndViolationContracts()
    {
        var result = Evaluate();

        Assert.IsNotNull(typeof(AgentMemoryBoundaryScenario));
        Assert.IsNotNull(typeof(AgentMemoryBoundaryViolation));
        Assert.IsNotNull(typeof(AgentMemoryBoundaryEvaluationResult));
        Assert.IsNotNull(typeof(IAgentMemoryBoundaryEvaluationHarness));
        Assert.IsNotNull(typeof(AgentMemoryBoundaryEvaluationHarness));
        Assert.IsTrue(result.Scenarios.All(scenario => !string.IsNullOrWhiteSpace(scenario.ScenarioId)));
        Assert.IsTrue(result.Scenarios.All(scenario => !string.IsNullOrWhiteSpace(scenario.Description)));
        Assert.IsTrue(result.Scenarios.All(scenario => !string.IsNullOrWhiteSpace(scenario.ExpectedBoundary)));
    }

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_ProposalAgentCannotPromoteMemory() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.ProposalAgentCannotPromoteMemory);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_ReviewAgentCannotBlockExecutionDirectly() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.ReviewAgentCannotBlockExecutionDirectly);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_RetrievalAgentCannotApproveAction() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.RetrievalAgentCannotApproveAction);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_StabilityScoreCannotAuthorizeAction() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.StabilityScoreCannotAuthorizeAction);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_RetrievedAcceptedMemoryCannotApproveToolExecution() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.RetrievedAcceptedMemoryCannotApproveToolExecution);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_MemoryImprovementDraftCannotCreateCollectiveMemory() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.MemoryImprovementDraftCannotCreateCollectiveMemory);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_HumanProxyRequiresExplicitHumanEvent() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.HumanProxyRequiresExplicitHumanEvent);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_PersonaCannotImplyApproval() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.PersonaCannotImplyApproval);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_GovernanceAgentCannotExecuteGovernedAction() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.GovernanceAgentCannotExecuteGovernedAction);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_ImplementationAgentCannotPromoteMemory() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.ImplementationAgentCannotPromoteMemory);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_CriticFindingCannotEnforceBlock() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.CriticFindingCannotEnforceBlock);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_CollectiveRetrievalCannotSatisfyPolicyApproval() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.CollectiveRetrievalCannotSatisfyPolicyApproval);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_LocalMemoryInfluenceCannotReplaceApproval() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.LocalMemoryInfluenceCannotReplaceApproval);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_HandoffCannotGrantMemoryOwnership() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.HandoffCannotGrantMemoryOwnership);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_ProposalAcceptedStatusCannotPromoteCollectiveMemory() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.ProposalAcceptedStatusCannotPromoteCollectiveMemory);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_WeaviateIndexingCannotCreateAuthority() =>
        AssertScenarioPasses(AgentMemoryBoundaryScenarioType.WeaviateIndexingCannotCreateAuthority);

    [TestMethod]
    public void AgentMemoryBoundaryEvaluation_DoesNotAddRuntimeEngineSqlWeaviateWritesOrAgentWiring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var harnessFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "IronDev.Core", "Agents", "Evaluation"), "*.cs", SearchOption.AllDirectories)
            .ToArray();

        var forbiddenTokens = new[]
        {
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE "
        };

        foreach (var file in harnessFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Boundary harness production file contains forbidden runtime token '{token}': {file}");
            }

            var textWithoutRequiredScenarioName = text.Replace("WeaviateIndexingCannotCreateAuthority", string.Empty, StringComparison.Ordinal);
            Assert.IsFalse(
                textWithoutRequiredScenarioName.Contains("WeaviateSemanticMemoryService", StringComparison.Ordinal) ||
                textWithoutRequiredScenarioName.Contains("IWeaviateMemoryIndexer", StringComparison.Ordinal) ||
                textWithoutRequiredScenarioName.Contains(".IndexAsync(", StringComparison.Ordinal),
                $"Boundary harness must not add a Weaviate write/indexing path: {file}");
        }

        var runtimeRoots = new[]
        {
            Path.Combine("IronDev.Infrastructure", "Services", "Agents"),
            "IronDev.Api",
            "IronDev.Client",
            "tools"
        };
        var runtimeFiles = runtimeRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in runtimeFiles)
        {
            var text = RemoveStoredManualWrapperNames(File.ReadAllText(file));
            Assert.IsFalse(text.Contains("AgentMemoryBoundaryEvaluationHarness", StringComparison.Ordinal),
                $"Runtime file wires AgentMemoryBoundaryEvaluationHarness: {file}");
            Assert.IsFalse(text.Contains("IndependentCriticAgent", StringComparison.Ordinal),
                $"Runtime file wires IndependentCriticAgent: {file}");
            Assert.IsFalse(text.Contains("AgentDefinitionCatalog.MemoryImprovementAgent", StringComparison.Ordinal),
                $"Runtime file wires boxed MemoryImprovementAgent definition: {file}");
            Assert.IsFalse(text.Contains("CriticReviewResult", StringComparison.Ordinal),
                $"Runtime file references boxed critic output contract: {file}");
            Assert.IsFalse(text.Contains("MemoryImprovementDetectionResult", StringComparison.Ordinal),
                $"Runtime file references boxed memory-improvement output contract: {file}");
            Assert.IsFalse(text.Contains("ICollectiveMemoryRetrievalService", StringComparison.Ordinal),
                $"Runtime file wires collective retrieval service into execution: {file}");
            Assert.IsFalse(text.Contains("ICollectiveMemoryStabilityScorer", StringComparison.Ordinal),
                $"Runtime file wires stability scorer into approval/execution: {file}");
            Assert.IsFalse(text.Contains("CollectiveMemoryStabilityScore", StringComparison.Ordinal) &&
                           text.Contains("AgentApprovalEvidence", StringComparison.Ordinal),
                $"Runtime file appears to combine stability score with approval evidence: {file}");
        }
    }

    private static string RemoveStoredManualWrapperNames(string text) =>
        text
            .Replace("IStoredManualIndependentCriticAgentService", string.Empty, StringComparison.Ordinal)
            .Replace("StoredManualIndependentCriticAgentService", string.Empty, StringComparison.Ordinal)
            .Replace("IStoredManualMemoryImprovementAgentService", string.Empty, StringComparison.Ordinal)
            .Replace("StoredManualMemoryImprovementAgentService", string.Empty, StringComparison.Ordinal);

    private static void AssertScenarioPasses(AgentMemoryBoundaryScenarioType scenarioType)
    {
        var result = Evaluate();
        var scenario = result.Scenarios.SingleOrDefault(item => item.ScenarioType == scenarioType);
        Assert.IsNotNull(scenario, $"Scenario '{scenarioType}' was not produced.");

        var violations = result.Violations
            .Where(violation => string.Equals(violation.ScenarioId, scenario.ScenarioId, StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Scenario '{scenarioType}' produced violations: {string.Join("; ", violations.Select(v => $"{v.Code}:{v.Message}"))}");
    }

    private static AgentMemoryBoundaryEvaluationResult Evaluate() =>
        new AgentMemoryBoundaryEvaluationHarness().Evaluate(EvaluationTime);

    private static string FormatViolations(AgentMemoryBoundaryEvaluationResult result) =>
        string.Join("; ", result.Violations.Select(violation => $"{violation.ScenarioId}:{violation.Code}:{violation.Message}"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Infrastructure")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}

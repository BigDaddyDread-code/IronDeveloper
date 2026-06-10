using IronDev.Core.AgentMemory.Evaluation;
using IronDev.Data;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryGovernanceEvaluationHarnessTests : IntegrationTestBase
{
    [TestMethod]
    public async Task MemoryGovernanceEvaluationHarness_AllFailureScenariosPass()
    {
        var harness = BuildHarness();

        var result = await harness.RunAsync();
        var report = MemoryGovernanceEvaluationHarness.FormatReport(result);

        Console.WriteLine(report);
        Assert.AreEqual(17, result.ScenarioCount, report);
        Assert.AreEqual(0, result.FailedCount, report);
        Assert.AreEqual(result.ScenarioCount, result.PassedCount, report);
        Assert.IsTrue(result.Scenarios.All(item => item.Passed), report);
    }

    [TestMethod]
    public async Task MemoryGovernanceEvaluationHarness_IncludesEvidenceForEveryScenario()
    {
        var harness = BuildHarness();

        var result = await harness.RunAsync();
        var missingEvidence = result.Scenarios
            .Where(item => item.Evidence.Count == 0)
            .Select(item => item.ScenarioId.ToString())
            .ToArray();

        Assert.AreEqual(0, missingEvidence.Length, $"Scenarios without evidence: {string.Join(", ", missingEvidence)}");
    }

    [TestMethod]
    public void MemoryGovernanceEvaluationHarness_ReportOutputIsReadableForFailures()
    {
        var result = new MemoryEvaluationRunResult
        {
            EvaluationRunId = "memory-eval-test",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ScenarioCount = 1,
            PassedCount = 0,
            FailedCount = 1,
            Scenarios =
            [
                new MemoryEvaluationScenarioResult
                {
                    ScenarioId = MemoryEvaluationScenarioId.CrossAgentLocalMemoryReadBlocked,
                    Name = nameof(MemoryEvaluationScenarioId.CrossAgentLocalMemoryReadBlocked),
                    Passed = false,
                    Summary = "Synthetic failure.",
                    Evidence = ["Exception type: InvalidOperationException"],
                    FailureReasons = ["TesterAgent read BuilderAgent memory."]
                }
            ]
        };

        var report = MemoryGovernanceEvaluationHarness.FormatReport(result);

        Assert.IsTrue(report.Contains("Memory Governance Evaluation Run", StringComparison.Ordinal));
        Assert.IsTrue(report.Contains("[FAIL] CrossAgentLocalMemoryReadBlocked", StringComparison.Ordinal));
        Assert.IsTrue(report.Contains("TesterAgent read BuilderAgent memory.", StringComparison.Ordinal));
        Assert.IsTrue(report.Contains("Exception type: InvalidOperationException", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MemoryGovernanceEvaluationHarness_DefinesAllRequiredScenarioIds()
    {
        var scenarioIds = Enum.GetValues<MemoryEvaluationScenarioId>();

        Assert.HasCount(17, scenarioIds);
        Assert.IsTrue(scenarioIds.Contains(MemoryEvaluationScenarioId.InfluenceOnlyExpiredMemoryBlocked));
        Assert.IsTrue(scenarioIds.Contains(MemoryEvaluationScenarioId.WeaviateDoesNotIndexRawLocalMemory));
        Assert.IsTrue(scenarioIds.Contains(MemoryEvaluationScenarioId.SourceMutationNeverAllowedByMemoryAlone));
        Assert.IsTrue(scenarioIds.Contains(MemoryEvaluationScenarioId.AppendOnlyMutationBlocked));
        Assert.IsTrue(scenarioIds.Contains(MemoryEvaluationScenarioId.SiloDoesNotExposeGovernanceOrIndexingServices));
        Assert.IsTrue(scenarioIds.Contains(MemoryEvaluationScenarioId.MemoryBackedExecutionCannotBypassGate));
    }

    private MemoryGovernanceEvaluationHarness BuildHarness()
    {
        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        return new MemoryGovernanceEvaluationHarness(ConnectionString, connectionFactory, FindRepositoryRoot());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for memory governance evaluation harness tests.");
    }
}

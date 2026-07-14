using System.Text.Json;
using IronDev.Core.AgentMemory.Evaluation;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class MemoryQualityBenchmarkTests
{
    private static readonly string[] Categories =
    [
        "exact-title retrieval", "narrow fact retrieval", "architecture retrieval", "accepted versus pending",
        "current versus stale", "wrong-project rejection", "wrong-tenant rejection", "conflict surfacing",
        "no-result behaviour", "broad-doc domination"
    ];

    [TestMethod]
    public void FixedBenchmark_ContainsEveryRequiredCategoryExactlyOnce()
    {
        var benchmark = Load();
        Assert.AreEqual("irondev-memory-quality-v1", benchmark.BenchmarkId);
        Assert.AreEqual(1, benchmark.Version);
        Assert.AreEqual(10, benchmark.Cases.Count);
        Assert.AreEqual(10, benchmark.Cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(10, benchmark.ReferenceResults.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count());
        foreach (var category in Categories)
            Assert.AreEqual(1, benchmark.Cases.Count(item => item.Category == category), category);
    }

    [TestMethod]
    public void ReferenceHarness_ReportsRequiredMetricsAndPassesThresholds()
    {
        var report = MemoryQualityBenchmarkEvaluator.Evaluate(Load());
        Assert.AreEqual(9, report.ScorableCaseCount);
        Assert.AreEqual(1.0, report.Top1Accuracy);
        Assert.AreEqual(1.0, report.Top5Accuracy);
        Assert.AreEqual(0, report.WrongScopeResultCount);
        Assert.AreEqual(0, report.StaleResultCount);
        Assert.AreEqual(0, report.AuthorityOrderErrors);
        Assert.AreEqual(0, report.NoResultErrors);
        Assert.IsTrue(report.Acceptable);
    }

    [TestMethod]
    public void Evaluator_FailsWrongScopeAndAuthorityOrderRegressions()
    {
        var benchmark = Load();
        var altered = benchmark with
        {
            ReferenceResults = benchmark.ReferenceResults
                .Select(result => result.CaseId == "wrong-project"
                    ? result with { ResultIds = ["project-b-deployment", "project-a-deployment"] }
                    : result.CaseId == "accepted-pending"
                        ? result with { ResultIds = ["pending-db-idea", "accepted-db-rule"] }
                        : result)
                .ToArray()
        };
        var report = MemoryQualityBenchmarkEvaluator.Evaluate(altered);
        Assert.AreEqual(1, report.WrongScopeResultCount);
        Assert.AreEqual(1, report.AuthorityOrderErrors);
        Assert.IsFalse(report.Acceptable);
    }

    [TestMethod]
    public void Evaluator_FailsRankingStaleAndNoResultRegressions()
    {
        var benchmark = Load();
        var altered = benchmark with
        {
            ReferenceResults = benchmark.ReferenceResults
                .Select(result => result.CaseId switch
                {
                    "exact-title" => result with { ResultIds = ["wrong-title", "adr-017"] },
                    "narrow-fact" => result with { ResultIds = ["wrong-fact"] },
                    "current-stale" => result with { ResultIds = ["current-indexing", "stale-indexing"] },
                    "no-result" => result with { ResultIds = ["hallucinated-quasar"] },
                    _ => result
                })
                .ToArray()
        };

        var report = MemoryQualityBenchmarkEvaluator.Evaluate(altered);
        Assert.IsLessThan(0.80, report.Top1Accuracy);
        Assert.IsLessThan(1.0, report.Top5Accuracy);
        Assert.AreEqual(1, report.StaleResultCount);
        Assert.AreEqual(1, report.NoResultErrors);
        Assert.IsFalse(report.Acceptable);
    }

    [TestMethod]
    public void Evaluator_RejectsIncompleteOrAmbiguousObservedResults()
    {
        var benchmark = Load();
        var missing = benchmark with { ReferenceResults = benchmark.ReferenceResults.Skip(1).ToArray() };
        Assert.ThrowsExactly<ArgumentException>(() => MemoryQualityBenchmarkEvaluator.Evaluate(missing));

        var duplicateIds = benchmark with
        {
            ReferenceResults = benchmark.ReferenceResults
                .Select(result => result.CaseId == "exact-title" ? result with { ResultIds = ["adr-017", "adr-017"] } : result)
                .ToArray()
        };
        Assert.ThrowsExactly<ArgumentException>(() => MemoryQualityBenchmarkEvaluator.Evaluate(duplicateIds));

        var unknown = benchmark with
        {
            ReferenceResults = [.. benchmark.ReferenceResults, new MemoryQualityObservedResult { CaseId = "unknown", ResultIds = [] }]
        };
        Assert.ThrowsExactly<ArgumentException>(() => MemoryQualityBenchmarkEvaluator.Evaluate(unknown));
    }

    [TestMethod]
    public void SemanticCandidateInjection_RemainsDisabledPendingLiveProviderBaseline()
    {
        var contract = Read("Docs", "memory", "MEMORY_QUALITY_BENCHMARK.md");
        StringAssert.Contains(contract, "not a live SQL, in-memory semantic, OpenAI embedding, or Weaviate provider run");
        StringAssert.Contains(contract, "SQL-backed project context assembly is active today");
        StringAssert.Contains(contract, "Automatic semantic/vector candidate injection remains disabled");
        StringAssert.Contains(contract, "Live-provider-gated semantic retrieval is future work");
        StringAssert.Contains(contract, "named live provider");
    }

    private static MemoryQualityBenchmarkDefinition Load() =>
        JsonSerializer.Deserialize<MemoryQualityBenchmarkDefinition>(
            Read("tools", "dogfood", "benchmarks", "memory-quality-v1.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Memory benchmark could not be loaded.");

    private static string Read(params string[] parts) => File.ReadAllText(parts.Aggregate(Root(), Path.Combine));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

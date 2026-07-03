using IronDev.Core.Builder;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P1-6 — the catch-rate as durable evidence. Protected boundaries:
/// - a measurement is hash-sealed when written and re-verified on every read —
///   a rewritten record is reported unverified, never silently served;
/// - degraded capability is recorded as degraded (ReExecutionAvailable=false,
///   honestly lower catch-rate), never dressed up;
/// - the measurement service grants nothing: no approvals, no gates, no apply.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonCanaryMeasurementTests
{
    [TestMethod]
    public async Task Measure_PersistsAHashSealedRecord_ThatReadsBackVerified()
    {
        using var harness = MeasurementHarness.Create(StubCorpus(caught: 5, total: 5, controlClean: true));

        var measurement = await harness.Service.MeasureAsync("user-9");

        Assert.AreEqual(1.0, measurement.CatchRate, 0.000001);
        Assert.AreEqual("user-9", measurement.RequestedByUserId);
        StringAssert.Contains(measurement.Boundary, "evidence, not authority");

        var record = await harness.Service.GetAsync(measurement.MeasurementId);
        Assert.IsNotNull(record);
        Assert.IsTrue(record!.Verified, "The record reads back byte-identical to what was sealed.");
        Assert.AreEqual(5, record.Measurement.CaughtCount);

        var list = await harness.Service.ListAsync();
        Assert.AreEqual(1, list.Count);
        Assert.IsTrue(list[0].Verified);
    }

    [TestMethod]
    public async Task Measure_ARewrittenRecord_ReadsBackUnverified_NeverSilentlyServed()
    {
        using var harness = MeasurementHarness.Create(StubCorpus(caught: 3, total: 5, controlClean: true));
        var measurement = await harness.Service.MeasureAsync("user-9");

        // History is inconvenient: someone edits the recorded catch-rate upward.
        var path = Path.Combine(harness.EvidenceRoot, "critic-canary-measurements", $"{measurement.MeasurementId}.json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("0.6", "1.0"));

        var record = await harness.Service.GetAsync(measurement.MeasurementId);

        Assert.IsFalse(record!.Verified, "A rewritten measurement is a broken seal, and the read says so.");
        var list = await harness.Service.ListAsync();
        Assert.IsFalse(list.Single().Verified);
    }

    [TestMethod]
    public async Task Measure_WithoutASandbox_RecordsItsOwnWeakness()
    {
        // No CriticCanary:SandboxRepoPath configured → the corpus runs degraded
        // and the RECORD says so. A weaker measurement is recorded as weaker.
        using var harness = MeasurementHarness.CreateWithRealRunner();

        var measurement = await harness.Service.MeasureAsync("user-9");

        Assert.IsFalse(measurement.ReExecutionAvailable);
        Assert.IsTrue(measurement.CatchRate < 1.0,
            "Without re-execution the green-lie canary goes uncaught, and the number is honestly lower.");
        Assert.IsTrue(measurement.Results.Any(result => result.CanaryId == "canary-green-lie" && !result.Caught));

        var record = await harness.Service.GetAsync(measurement.MeasurementId);
        Assert.IsTrue(record!.Verified);
    }

    [TestMethod]
    public void MeasurementService_GrantsNothing()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonCanaryMeasurementService.cs"));

        foreach (var forbidden in new[]
        {
            "AcceptedApproval",
            "SatisfyPolicy",
            "ApprovalGranted",
            "SkeletonApply",
            "ControlledSourceApply",
            "TransitionAsync",
            "ContinueAsync"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The measurement measures the net and writes the number down — it must never touch a gate: {forbidden}");
        }

        StringAssert.Contains(source, "evidence, not authority");
        StringAssert.Contains(source, "never dressed up");
    }

    private static SkeletonCanaryCorpusResult StubCorpus(int caught, int total, bool controlClean)
    {
        var results = new List<SkeletonCanaryResult>();
        for (var index = 0; index < total; index++)
        {
            results.Add(new SkeletonCanaryResult
            {
                CanaryId = $"canary-{index + 1}",
                Title = $"Canary {index + 1}",
                Caught = index < caught,
                MustCatch = "The seeded defect.",
                Observed = index < caught ? "caught" : "missed"
            });
        }
        results.Add(new SkeletonCanaryResult
        {
            CanaryId = "control-honest-package",
            Title = "Honest control",
            Caught = controlClean,
            MustCatch = "Nothing.",
            IsControl = true
        });
        return new SkeletonCanaryCorpusResult { Results = results };
    }

    private sealed class MeasurementHarness : IDisposable
    {
        public required SkeletonCanaryMeasurementService Service { get; init; }
        public required string EvidenceRoot { get; init; }

        public static MeasurementHarness Create(SkeletonCanaryCorpusResult corpus) =>
            Build(new StubRunner(corpus));

        public static MeasurementHarness CreateWithRealRunner() =>
            Build(new SkeletonCriticCanaryRunner());

        private static MeasurementHarness Build(ISkeletonCriticCanaryRunner runner)
        {
            var evidenceRoot = Path.Combine(Path.GetTempPath(), $"irondev-canary-measure-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisposableBuild:EvidenceRoot"] = evidenceRoot
                })
                .Build();

            return new MeasurementHarness
            {
                Service = new SkeletonCanaryMeasurementService(runner, configuration),
                EvidenceRoot = evidenceRoot
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(EvidenceRoot))
                    Directory.Delete(EvidenceRoot, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }

    private sealed class StubRunner(SkeletonCanaryCorpusResult corpus) : ISkeletonCriticCanaryRunner
    {
        public Task<SkeletonCanaryCorpusResult> RunAsync(SkeletonCanaryRunOptions options, CancellationToken ct = default) =>
            Task.FromResult(corpus);
    }

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }
}

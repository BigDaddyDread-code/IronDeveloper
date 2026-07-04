using IronDev.Core.Builder;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P2-2 — the batch plan as durable evidence. Protected boundaries:
/// - a plan derives ONLY from a map whose seal verifies — a plan built on a
///   broken seal would launder the tamper into a clean-looking artifact;
/// - the plan itself is hash-sealed and re-verified on read;
/// - planning grants nothing: no runs start, no gates move.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonBatchPlanTests
{
    private const int ProjectId = 7;

    [TestMethod]
    public async Task Plan_FromASealedMap_PersistsWaves_AndReadsBackVerified()
    {
        using var harness = PlanHarness.Create();
        var mapId = await harness.SealMap(
            ticketIds: [42, 43, 44],
            edges: [(42, 43, SkeletonBatchDependencyEdgeKinds.ExplicitBlock)]);

        var outcome = await harness.Service.PlanAsync(ProjectId, mapId, "user-9");

        Assert.IsTrue(outcome.Succeeded, outcome.FailureReason);
        Assert.IsTrue(outcome.Plan!.Schedulable);
        Assert.AreEqual(2, outcome.Plan.Waves.Count);
        CollectionAssert.AreEqual(new[] { 42L, 44L }, outcome.Plan.Waves[0].TicketIds.ToArray());
        CollectionAssert.AreEqual(new[] { 43L }, outcome.Plan.Waves[1].TicketIds.ToArray());
        Assert.AreEqual(mapId, outcome.Plan.MapId, "Provenance: the plan names the sealed map it derives from.");

        var record = await harness.Service.GetAsync(ProjectId, outcome.PlanId);
        Assert.IsTrue(record!.Verified);
        Assert.AreEqual("user-9", record.RequestedByUserId);
    }

    [TestMethod]
    public async Task Plan_FromATamperedMap_IsRefused_NamingTheBrokenSeal()
    {
        using var harness = PlanHarness.Create();
        var mapId = await harness.SealMap(ticketIds: [42, 43], edges: []);

        // The map is edited after sealing — the tamper P1 taught us to expect.
        var mapPath = Path.Combine(harness.EvidenceRoot, "batch-maps", ProjectId.ToString(), $"{mapId}.json");
        await File.WriteAllTextAsync(mapPath, (await File.ReadAllTextAsync(mapPath)).Replace("\"ticketIds\"", "\"ticketIdz\""));

        var outcome = await harness.Service.PlanAsync(ProjectId, mapId, "user-9");

        Assert.IsFalse(outcome.Succeeded,
            "A plan built on a broken seal would launder the tamper into a clean-looking artifact.");
        StringAssert.Contains(outcome.FailureReason, "integrity verification");
        StringAssert.Contains(outcome.FailureReason, "Detect a fresh map");
    }

    [TestMethod]
    public async Task Plan_UnknownMap_FailsExplicitly()
    {
        using var harness = PlanHarness.Create();

        var outcome = await harness.Service.PlanAsync(ProjectId, "no-such-map", "user-9");

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "was not found");
    }

    [TestMethod]
    public async Task Plan_ARewrittenPlan_ReadsBackUnverified()
    {
        using var harness = PlanHarness.Create();
        var mapId = await harness.SealMap(ticketIds: [42, 43], edges: [(42, 43, SkeletonBatchDependencyEdgeKinds.ExplicitBlock)]);
        var outcome = await harness.Service.PlanAsync(ProjectId, mapId, "user-9");

        // History is inconvenient: someone reorders the waves in the record.
        var planPath = Path.Combine(harness.EvidenceRoot, "batch-plans", ProjectId.ToString(), $"{outcome.PlanId}.json");
        await File.WriteAllTextAsync(planPath, (await File.ReadAllTextAsync(planPath)).Replace("\"waveNumber\": 1", "\"waveNumber\": 9"));

        var record = await harness.Service.GetAsync(ProjectId, outcome.PlanId);

        Assert.IsFalse(record!.Verified, "A rewritten plan is a broken seal, and the read says so.");
    }

    [TestMethod]
    public void BatchPlanService_GrantsNothing()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonBatchPlanService.cs"));

        foreach (var forbidden in new[]
        {
            "StartAsync",
            "ContinueAsync",
            "ApplyAsync",
            "AcceptedApproval",
            "SatisfyPolicy",
            "TransitionAsync",
            "ControlledSourceApply"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Planning sequences the batch; it must never run it or touch a gate: {forbidden}");
        }

        StringAssert.Contains(source, "grants nothing");
        StringAssert.Contains(source, "never auto-broken");
        StringAssert.Contains(source, "broken seal");
    }

    private sealed class PlanHarness : IDisposable
    {
        public required SkeletonBatchPlanService Service { get; init; }
        public required StubMapService Maps { get; init; }
        public required string EvidenceRoot { get; init; }

        public static PlanHarness Create()
        {
            var evidenceRoot = Path.Combine(Path.GetTempPath(), $"irondev-batch-plan-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisposableBuild:EvidenceRoot"] = evidenceRoot
                })
                .Build();

            var maps = new StubMapService(evidenceRoot);
            return new PlanHarness
            {
                Service = new SkeletonBatchPlanService(maps, configuration),
                Maps = maps,
                EvidenceRoot = evidenceRoot
            };
        }

        public Task<string> SealMap(IReadOnlyList<long> ticketIds, IReadOnlyList<(long From, long To, string Kind)> edges) =>
            Maps.SealAsync(new SkeletonBatchMap
            {
                ProjectId = ProjectId,
                TicketIds = ticketIds,
                Edges = edges
                    .Select(edge => new SkeletonBatchDependencyEdge
                    {
                        FromTicketId = edge.From,
                        ToTicketId = edge.To,
                        Kind = edge.Kind,
                        Reason = $"test edge {edge.From}->{edge.To}"
                    })
                    .ToList()
            });

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

    /// <summary>
    /// Seals maps with the same on-disk discipline as the real map service, so
    /// the plan service's verification runs against real files — including the
    /// tamper case.
    /// </summary>
    private sealed class StubMapService(string evidenceRoot) : ISkeletonBatchMapService
    {
        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public async Task<string> SealAsync(SkeletonBatchMap map)
        {
            var mapId = $"map-{Guid.NewGuid():N}"[..20];
            var directory = Path.Combine(evidenceRoot, "batch-maps", map.ProjectId.ToString());
            Directory.CreateDirectory(directory);
            var mapPath = Path.Combine(directory, $"{mapId}.json");
            var persisted = new { mapId, detectedAtUtc = DateTimeOffset.UtcNow, requestedByUserId = "user-9", map };
            await File.WriteAllTextAsync(mapPath, System.Text.Json.JsonSerializer.Serialize(persisted, JsonOptions));
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(mapPath))).ToLowerInvariant();
            await File.WriteAllTextAsync(Path.Combine(directory, $"{mapId}.sha256"), hash);
            return mapId;
        }

        public Task<SkeletonBatchMapOutcome?> DetectAsync(int projectId, IReadOnlyList<long> ticketIds, string requestedByUserId, CancellationToken ct = default) =>
            throw new NotSupportedException("The plan harness seals maps directly.");

        public async Task<SkeletonBatchMapRecord?> GetAsync(int projectId, string mapId, CancellationToken ct = default)
        {
            var directory = Path.Combine(evidenceRoot, "batch-maps", projectId.ToString());
            var mapPath = Path.Combine(directory, $"{mapId}.json");
            var hashPath = Path.Combine(directory, $"{mapId}.sha256");
            if (!File.Exists(mapPath) || !File.Exists(hashPath))
                return null;

            var bytes = await File.ReadAllBytesAsync(mapPath, ct);
            var persisted = System.Text.Json.JsonSerializer.Deserialize<PersistedMap>(System.Text.Encoding.UTF8.GetString(bytes), JsonOptions);
            if (persisted?.Map is null)
                return null;

            return new SkeletonBatchMapRecord
            {
                MapId = persisted.MapId,
                DetectedAtUtc = persisted.DetectedAtUtc,
                RequestedByUserId = persisted.RequestedByUserId,
                Map = persisted.Map,
                RecordedSha256 = (await File.ReadAllTextAsync(hashPath, ct)).Trim(),
                Sha256OnDisk = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
            };
        }

        private sealed record PersistedMap
        {
            public string MapId { get; init; } = string.Empty;
            public DateTimeOffset DetectedAtUtc { get; init; }
            public string RequestedByUserId { get; init; } = string.Empty;
            public SkeletonBatchMap? Map { get; init; }
        }
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

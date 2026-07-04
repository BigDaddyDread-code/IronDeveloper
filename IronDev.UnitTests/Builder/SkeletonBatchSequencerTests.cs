using IronDev.Core.Builder;

namespace IronDev.UnitTests.Builder;

// P2-2 — the batch sequencer. Deterministic waves from the map: independent
// tickets share a wave, dependents wait, cycles are NAMED blockers the human
// resolves — the sequencer never breaks one by guessing.
[TestClass]
[TestCategory("SkeletonBatch")]
public sealed class SkeletonBatchSequencerTests
{
    private static SkeletonBatchMap Map(IReadOnlyList<long> ticketIds, params (long From, long To, string Kind)[] edges) => new()
    {
        ProjectId = 7,
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
    };

    [TestMethod]
    public void IndependentTickets_ShareOneWave_SortedById()
    {
        var plan = SkeletonBatchSequencer.Sequence(Map([43, 42, 44]), "map-1");

        Assert.IsTrue(plan.Schedulable);
        Assert.AreEqual(1, plan.Waves.Count);
        CollectionAssert.AreEqual(new[] { 42L, 43L, 44L }, plan.Waves[0].TicketIds.ToArray(),
            "Independent tickets run in parallel; the order within a wave is presentation, sorted for determinism.");
    }

    [TestMethod]
    public void AChain_SerializesIntoOneWavePerTicket()
    {
        var plan = SkeletonBatchSequencer.Sequence(
            Map([42, 43, 44], (42, 43, "explicit-block"), (43, 44, "explicit-block")), "map-1");

        Assert.AreEqual(3, plan.Waves.Count);
        CollectionAssert.AreEqual(new[] { 42L }, plan.Waves[0].TicketIds.ToArray());
        CollectionAssert.AreEqual(new[] { 43L }, plan.Waves[1].TicketIds.ToArray());
        CollectionAssert.AreEqual(new[] { 44L }, plan.Waves[2].TicketIds.ToArray());
    }

    [TestMethod]
    public void ADiamond_RunsTheMiddleInParallel()
    {
        // 42 → 43, 42 → 44, 43 → 45, 44 → 45
        var plan = SkeletonBatchSequencer.Sequence(
            Map([42, 43, 44, 45],
                (42, 43, "explicit-block"), (42, 44, "footprint-overlap"),
                (43, 45, "explicit-block"), (44, 45, "explicit-block")), "map-1");

        Assert.AreEqual(3, plan.Waves.Count);
        CollectionAssert.AreEqual(new[] { 42L }, plan.Waves[0].TicketIds.ToArray());
        CollectionAssert.AreEqual(new[] { 43L, 44L }, plan.Waves[1].TicketIds.ToArray(),
            "The middle of the diamond is independent once 42 lands — one wave, in parallel.");
        CollectionAssert.AreEqual(new[] { 45L }, plan.Waves[2].TicketIds.ToArray());
    }

    [TestMethod]
    public void ACycle_IsANamedBlocker_NeverAutoBroken_AndDragsItsDependentsWithIt()
    {
        // 42 ⇄ 43 cycle; 44 waits on 43; 45 is free.
        var plan = SkeletonBatchSequencer.Sequence(
            Map([42, 43, 44, 45],
                (42, 43, "explicit-block"), (43, 42, "explicit-block"), (43, 44, "explicit-block")), "map-1");

        Assert.IsFalse(plan.Schedulable, "A plan with a cycle proposes nothing until the human resolves it.");
        CollectionAssert.AreEqual(new[] { 45L }, plan.Waves.Single().TicketIds.ToArray(),
            "The free ticket still gets its wave — a cycle blocks its members and dependents, not the world.");

        var blocker = plan.CycleBlockers.Single();
        CollectionAssert.AreEquivalent(new[] { 42L, 43L, 44L }, blocker.TicketIds.ToArray(),
            "Cycle members AND the tickets waiting on them are unplaceable — both named.");
        StringAssert.Contains(blocker.Detail, "42 → 43");
        StringAssert.Contains(blocker.Detail, "43 → 42");
        StringAssert.Contains(blocker.Detail, "never breaks a cycle by guessing");
    }

    [TestMethod]
    public void ThePlan_CarriesTheMapsWarningsAndProvenance()
    {
        var map = Map([42, 43]) with { Warnings = ["Ticket 42 has no linked file paths — overlap undetectable."] };

        var plan = SkeletonBatchSequencer.Sequence(map, "map-abc");

        Assert.AreEqual("map-abc", plan.MapId, "Provenance: a plan names the sealed map it derives from.");
        CollectionAssert.AreEqual(map.Warnings.ToArray(), plan.Warnings.ToArray(),
            "The plan inherits the map's stated blind spots — they do not launder away in sequencing.");
        StringAssert.Contains(plan.Boundary, "grants nothing");
    }

    [TestMethod]
    public void Sequencing_IsDeterministic()
    {
        var map = Map([44, 42, 43], (42, 44, "explicit-block"), (43, 44, "footprint-overlap"));

        static string Fingerprint(SkeletonBatchPlan plan) =>
            string.Join(" | ", plan.Waves.Select(wave => $"{wave.WaveNumber}:[{string.Join(",", wave.TicketIds)}]"));

        Assert.AreEqual(
            Fingerprint(SkeletonBatchSequencer.Sequence(map, "m")),
            Fingerprint(SkeletonBatchSequencer.Sequence(map, "m")),
            "The same map always yields the same plan — a persisted plan can be re-derived and checked.");
    }
}

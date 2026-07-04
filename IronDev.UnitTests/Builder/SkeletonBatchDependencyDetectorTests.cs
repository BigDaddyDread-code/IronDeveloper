using IronDev.Core.Builder;
using IronDev.Data.Models;

namespace IronDev.UnitTests.Builder;

// P2-1 — the batch dependency detector. Deterministic and evidence-named by
// design: every edge carries its kind and a reason in plain words, everything
// undetectable becomes a warning, and the same tickets always yield the same
// map — so a persisted map can be re-derived and checked.
[TestClass]
[TestCategory("SkeletonBatch")]
public sealed class SkeletonBatchDependencyDetectorTests
{
    private static ProjectTicket Ticket(long id, string? blockedBy = null, string? files = null) =>
        new() { Id = id, ProjectId = 7, TenantId = 1, Title = $"Ticket {id}", BlockedByTicketIds = blockedBy, LinkedFilePaths = files };

    [TestMethod]
    public void ExplicitBlock_BecomesAnEdge_WithTheDeclarationAsReason()
    {
        var map = SkeletonBatchDependencyDetector.Detect(7, [Ticket(42, files: "src/A.cs"), Ticket(43, blockedBy: "42", files: "src/B.cs")]);

        var edge = map.Edges.Single();
        Assert.AreEqual(42, edge.FromTicketId);
        Assert.AreEqual(43, edge.ToTicketId);
        Assert.AreEqual(SkeletonBatchDependencyEdgeKinds.ExplicitBlock, edge.Kind);
        StringAssert.Contains(edge.Reason, "declares it is blocked by ticket 42");
    }

    [TestMethod]
    public void BlockedByOutsideTheBatch_IsDroppedWithANamedWarning_NeverSilently()
    {
        var map = SkeletonBatchDependencyDetector.Detect(7, [Ticket(42, blockedBy: "99", files: "src/A.cs"), Ticket(43, files: "src/B.cs")]);

        Assert.AreEqual(0, map.Edges.Count);
        Assert.IsTrue(map.Warnings.Any(warning =>
            warning.Contains("blocked-by 99") && warning.Contains("still exists outside the batch")),
            "A dependency the map cannot express is stated, not forgotten.");
    }

    [TestMethod]
    public void SelfBlock_IsIgnoredWithAWarning()
    {
        var map = SkeletonBatchDependencyDetector.Detect(7, [Ticket(42, blockedBy: "42", files: "src/A.cs"), Ticket(43, files: "src/B.cs")]);

        Assert.AreEqual(0, map.Edges.Count);
        Assert.IsTrue(map.Warnings.Any(warning => warning.Contains("declares itself")));
    }

    [TestMethod]
    public void FootprintOverlap_BecomesAnEdge_LowerIdFirst_WithSharedPathsNamed()
    {
        var map = SkeletonBatchDependencyDetector.Detect(7,
        [
            Ticket(43, files: "src/Catalog.cs\nsrc/Paging.cs"),
            Ticket(42, files: "src/Catalog.cs")
        ]);

        var edge = map.Edges.Single();
        Assert.AreEqual(SkeletonBatchDependencyEdgeKinds.FootprintOverlap, edge.Kind);
        Assert.AreEqual(42, edge.FromTicketId, "Overlap direction is deterministic: lower ticket id first.");
        Assert.AreEqual(43, edge.ToTicketId);
        CollectionAssert.AreEqual(new[] { "src/Catalog.cs" }, edge.SharedPaths.ToArray());
        StringAssert.Contains(edge.Reason, "deterministic, not semantic");
        StringAssert.Contains(edge.Reason, "declare an explicit block");
    }

    [TestMethod]
    public void AnExplicitEdge_SuppressesTheOverlapEdgeForTheSamePair()
    {
        // Declared evidence outranks predicted evidence: when the human already
        // ordered the pair, the overlap adds nothing.
        var map = SkeletonBatchDependencyDetector.Detect(7,
        [
            Ticket(42, files: "src/Catalog.cs"),
            Ticket(43, blockedBy: "42", files: "src/Catalog.cs")
        ]);

        Assert.AreEqual(1, map.Edges.Count);
        Assert.AreEqual(SkeletonBatchDependencyEdgeKinds.ExplicitBlock, map.Edges.Single().Kind);
    }

    [TestMethod]
    public void FootprintNormalization_SeparatorsCaseAndSlashes_ArePresentationNotContent()
    {
        var map = SkeletonBatchDependencyDetector.Detect(7,
        [
            Ticket(42, files: "src\\Catalog.cs"),
            Ticket(43, files: "/SRC/CATALOG.CS")
        ]);

        Assert.AreEqual(1, map.Edges.Count, "The same file spelled differently is still the same file.");
    }

    [TestMethod]
    public void UnknownFootprint_IsANamedWarning_NotASilentIndependenceAssumption()
    {
        var map = SkeletonBatchDependencyDetector.Detect(7, [Ticket(42), Ticket(43, files: "src/B.cs")]);

        Assert.AreEqual(0, map.Edges.Count);
        Assert.IsTrue(map.Warnings.Any(warning =>
            warning.Contains("Ticket 42") && warning.Contains("undetectable")),
            "Treating a footprint-less ticket as independent is an assumption, and the map says so.");
    }

    [TestMethod]
    public void Detection_IsDeterministic_RegardlessOfInputOrder()
    {
        var ticketsA = new[] { Ticket(43, files: "src/A.cs"), Ticket(42, files: "src/A.cs"), Ticket(44, blockedBy: "42") };
        var ticketsB = new[] { Ticket(44, blockedBy: "42"), Ticket(42, files: "src/A.cs"), Ticket(43, files: "src/A.cs") };

        static string Fingerprint(SkeletonBatchMap map) =>
            string.Join(" | ", map.Edges.Select(edge => $"{edge.FromTicketId}->{edge.ToTicketId}:{edge.Kind}")) +
            " || " + string.Join(" | ", map.Warnings);

        Assert.AreEqual(
            Fingerprint(SkeletonBatchDependencyDetector.Detect(7, ticketsA)),
            Fingerprint(SkeletonBatchDependencyDetector.Detect(7, ticketsB)),
            "The same tickets always yield the same map — a persisted map can be re-derived and checked.");
    }

    [TestMethod]
    public void ParseTicketIds_ToleratesHashesSeparatorsAndGarbage()
    {
        CollectionAssert.AreEqual(
            new[] { 42L, 43L, 44L },
            SkeletonBatchDependencyDetector.ParseTicketIds("#42, 43; not-a-ticket\n44,42").ToArray(),
            "Hash prefixes and mixed separators are presentation; garbage tokens are dropped; duplicates collapse.");
    }

    [TestMethod]
    public void Boundary_TheMapSchedulesNothing()
    {
        StringAssert.Contains(SkeletonBatchMap.BoundaryText, "schedules nothing");
        StringAssert.Contains(SkeletonBatchMap.BoundaryText, "grants nothing");
    }
}

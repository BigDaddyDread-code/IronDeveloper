using IronDev.Core.Builder;

namespace IronDev.UnitTests.Builder;

// P1-4 — the criterion→test coverage calculator. Deterministic and inspectable
// by design: the package builder computes the record and the ground-truth
// verifier recomputes it, so the same inputs must always yield the same rows.
// A silent coverage hole is impossible by construction: every criterion gets a
// row, covered or explicitly not.
[TestClass]
[TestCategory("SkeletonCoverage")]
public sealed class SkeletonCriterionCoverageCalculatorTests
{
    private static SkeletonAuthoredTest Test(string path, string covers) =>
        new() { RelativePath = path, Content = "public class T { }", CoversCriterion = covers };

    [TestMethod]
    public void EveryCriterionGetsARow_CoveredOrExplicitlyNot()
    {
        var coverage = SkeletonCriterionCoverageCalculator.Compute(
            "- Catalog sorts by title ascending\n- Catalog paging keeps sort order",
            [Test("tests/SortTests.cs", "Catalog sorts by title ascending")]);

        Assert.AreEqual(2, coverage.Count);
        var covered = coverage.Single(row => row.Covered);
        CollectionAssert.AreEqual(new[] { "tests/SortTests.cs" }, covered.CoveringTests.ToArray());
        var uncovered = coverage.Single(row => !row.Covered);
        StringAssert.Contains(uncovered.Criterion, "paging");
        Assert.AreEqual(0, uncovered.CoveringTests.Count);
    }

    [TestMethod]
    public void ListMarkersAndNumbering_ArePresentationNotContent()
    {
        var coverage = SkeletonCriterionCoverageCalculator.Compute(
            "1. Catalog sorts by title\n* Catalog filters by author\n- Catalog pages by ten",
            [
                Test("tests/A.cs", "Catalog sorts by title"),
                Test("tests/B.cs", "Catalog filters by author"),
                Test("tests/C.cs", "Catalog pages by ten")
            ]);

        Assert.AreEqual(3, coverage.Count);
        Assert.IsTrue(coverage.All(row => row.Covered));
    }

    [TestMethod]
    public void Matching_IsCaseInsensitive_IgnoresTrailingPunctuation_AndAllowsContainment()
    {
        var coverage = SkeletonCriterionCoverageCalculator.Compute(
            "Catalog sorts by title ascending.",
            [Test("tests/A.cs", "catalog sorts by title ascending")]);
        Assert.IsTrue(coverage.Single().Covered);

        var containment = SkeletonCriterionCoverageCalculator.Compute(
            "Given a catalog, sorting by title returns ascending order",
            [Test("tests/B.cs", "sorting by title returns ascending order")]);
        Assert.IsTrue(containment.Single().Covered, "A coversCriterion contained in the criterion still covers it.");
    }

    [TestMethod]
    public void ATestCoveringNoKnownCriterion_CoversNothing()
    {
        var coverage = SkeletonCriterionCoverageCalculator.Compute(
            "Catalog sorts by title",
            [Test("tests/Wander.cs", "The UI uses pretty colours")]);

        Assert.IsFalse(coverage.Single().Covered,
            "A test that answers a question nobody asked does not cover the question that was asked.");
    }

    [TestMethod]
    public void NoCriteria_YieldsNoRows_NeverAnInventedOne()
    {
        Assert.AreEqual(0, SkeletonCriterionCoverageCalculator.Compute(null, []).Count);
        Assert.AreEqual(0, SkeletonCriterionCoverageCalculator.Compute("  \n \n", [Test("tests/A.cs", "x")]).Count);
    }

    [TestMethod]
    public void Recomputation_IsDeterministic()
    {
        var criteria = "- A first criterion\n- A second criterion";
        var tests = new[] { Test("tests/A.cs", "A first criterion"), Test("tests/B.cs", "A first criterion") };

        var first = SkeletonCriterionCoverageCalculator.Compute(criteria, tests);
        var second = SkeletonCriterionCoverageCalculator.Compute(criteria, tests);

        CollectionAssert.AreEqual(
            first.Select(row => $"{row.Criterion}:{row.Covered}:{string.Join(",", row.CoveringTests)}").ToArray(),
            second.Select(row => $"{row.Criterion}:{row.Covered}:{string.Join(",", row.CoveringTests)}").ToArray(),
            "The verifier recomputes this record — the same inputs must always yield the same rows.");
    }
}

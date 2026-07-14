using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestClass]
[TestCategory("Contract")]
[TestCategory("Boundary")]
public sealed class UpgradeMigrationProofContractTests
{
    [TestMethod]
    public void UpgradeProof_PinsSupportedBaselineSequencePreservationAndCleanup()
    {
        var script = Read("Database", "verify-upgrade-database.ps1");
        var fixture = Read("Database", "baselines", "cln_21_pre_cln_19_runtime_fixture.sql");
        var verifier = Read("Database", "verify_upgrade_preservation.sql");

        StringAssert.Contains(script, "$baselineCommit = \"7f0e1058\"");
        StringAssert.Contains(script, "$baselineMigrationId = \"2026-07-cln-12-user-mutation-attribution\"");
        StringAssert.Contains(script, "7f0e1058_rebuild_db.sql");
        StringAssert.Contains(script, "-ThroughMigrationId");
        StringAssert.Contains(script, "apply-migrations.ps1");
        StringAssert.Contains(script, "verify-migrations.ps1");
        StringAssert.Contains(script, "verify_upgrade_preservation.sql");
        StringAssert.Contains(script, "Refusing upgrade verification against");
        StringAssert.Contains(script, "SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        StringAssert.Contains(script, "PASS upgrade migration verification");

        StringAssert.Contains(fixture, "Starting schema: main commit 7f0e1058");
        foreach (var table in new[]
                 {
                     "dbo.Runs", "dbo.RunEvents", "dbo.SemanticArtefacts", "dbo.SemanticChunks",
                     "dbo.EmbeddingJobs", "dbo.SemanticSearchTraces", "dbo.SemanticSearchTraceResults",
                     "dbo.SemanticEmbeddings", "dbo.SemanticIndexRuns"
                 })
            StringAssert.Contains(fixture, table);

        foreach (var preserved in new[]
                 {
                     "tenant row", "project row", "context document", "ticket compatibility data",
                     "artifact source reference", "run row", "run event", "semantic artefact",
                     "semantic chunk", "embedding job", "semantic search trace", "semantic search result",
                     "semantic embedding metadata", "semantic index run", "scoped row counts"
                 })
            StringAssert.Contains(verifier, preserved);
    }

    [TestMethod]
    public void SupportedBaselineSnapshot_HasNotDrifted()
    {
        var snapshot = Read("Database", "baselines", "7f0e1058_rebuild_db.sql").Replace("\r\n", "\n");
        var hash = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(snapshot))).ToLowerInvariant();

        Assert.AreEqual("ab19cfcf255bcb85287b62fac0ff8b2304a222bbc2fa6c2781fbf508f2635de6", hash);
    }

    [TestMethod]
    public void FullSqlLane_ExecutesUpgradeProofAfterFreshBaseline()
    {
        var ci = Read("Scripts", "ci", "run-full-sql-integration-ci.ps1");
        var platformIndex = ci.IndexOf("Isolated platform baseline", StringComparison.Ordinal);
        var upgradeIndex = ci.IndexOf("Supported database upgrade proof", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, platformIndex);
        Assert.IsTrue(upgradeIndex > platformIndex, "Upgrade proof must execute after the isolated platform baseline.");
        StringAssert.Contains(ci, "Database\\verify-upgrade-database.ps1");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev.slnx.");
    }
}

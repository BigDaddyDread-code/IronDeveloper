using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("DatabaseMigration")]
[TestCategory("StaticBoundary")]
[TestCategory("Decision")]
[TestCategory("Spike")]
public sealed class MigrationRunnerSpikeDecisionTests
{
    [TestMethod]
    public void MigrationRunnerSpike_DefinesRunnerAsExecutionOnly()
    {
        var spike = ReadSpike();

        AssertContainsAll(spike,
            "A migration runner is not database authority.",
            "Running scripts in order does not prove the database is safe.",
            "A migration runner is not approval.",
            "A migration runner is not verification.",
            "A migration runner is not release readiness.",
            "A migration runner is not deployment readiness.",
            "A migration runner is not schema safety.",
            "A migration runner is execution machinery only.");
    }

    [TestMethod]
    public void MigrationRunnerSpike_ComparesCurrentScriptsDbUpCustomAndDeferral()
    {
        var spike = ReadSpike();

        AssertContainsAll(spike,
            "| Option | What it gives us | Authority risk | Dependency risk | Fits manifest order? | Separates apply from verify? | Supports future H01 state model? | Runtime path risk | Recommendation |",
            "Option A - Keep current PowerShell apply/verify path",
            "Option B - Adopt DbUp through a bounded migration CLI",
            "Option C - Custom minimal runner",
            "Option D - Equivalent runner, deferred",
            "What it gives us",
            "Authority risk",
            "Dependency risk",
            "Fits manifest order?",
            "Separates apply from verify?",
            "Supports future H01 state model?",
            "Runtime path risk",
            "`RecommendDeferral`");
    }

    [TestMethod]
    public void MigrationRunnerSpike_ForbidsRuntimeAndAgentExecutionPaths()
    {
        var spike = ReadSpike();

        AssertContainsAll(spike,
            "DbUp must not run from API startup.",
            "DbUp must not run from normal CLI commands.",
            "DbUp must not run from agents.",
            "DbUp must not run from workflow runner paths.",
            "DbUp must not run from source apply.",
            "DbUp must not run from rollback.",
            "DbUp must not run from memory paths.",
            "DbUp must not run from frontend paths.",
            "H02 does not add production Core, Infrastructure, API, CLI, UI, agent, workflow, source-apply, rollback, or memory code.");
    }

    [TestMethod]
    public void MigrationRunnerSpike_RequiresManifestOrderHashAndVerificationSeparation()
    {
        var spike = ReadSpike();

        AssertContainsAll(spike,
            "Migration manifest defines expected migration identity and order.",
            "Migration scripts define intended database changes.",
            "Apply execution attempts to run scripts.",
            "Database verification proves expected objects, constraints, and procedures exist.",
            "Migration state records evidence about what happened.",
            "DbUp must consume IronDev's manifest order instead of discovering arbitrary scripts.",
            "Script hash drift must be detected before execution.",
            "Existing `Database/verify-migrations.ps1`, or a successor verifier, remains mandatory after execution.",
            "Failed apply and failed verify must be recorded separately.",
            "H03 should be: Migration script hash and manifest order contract.");
    }

    [TestMethod]
    public void MigrationRunnerSpike_DoesNotInstallOrImplementRunner()
    {
        var root = RepositoryRoot();
        var h02Files = new[]
        {
            Path.Combine(root, "Docs", "spikes", "H02_MIGRATION_RUNNER_SPIKE.md"),
            Path.Combine(root, "Docs", "receipts", "H02_MIGRATION_RUNNER_SPIKE.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "MigrationRunnerSpikeDecisionTests.cs"),
            Path.Combine(root, "Docs", "testing", "INTEGRATION_TEST_CATEGORIES.md")
        };

        foreach (var file in h02Files)
            Assert.IsTrue(File.Exists(file), $"Expected H02 file missing: {file}");

        var packageFiles = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "Directory.Packages.props", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var forbiddenPackageMarkers = new[]
        {
            string.Concat("PackageReference Include=\"", "dbup"),
            string.Concat("PackageVersion Include=\"", "dbup")
        };

        foreach (var file in packageFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var marker in forbiddenPackageMarkers)
                Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"H02 must not install DbUp packages: {file}");
        }

        var forbiddenNewPaths = new[]
        {
            "IronDev.MigrationRunner",
            "IronDev.DatabaseMigrator",
            "tools/MigrationRunner",
            "tools/migration-runner",
            "Database/MigrationRunner",
            "Database/migration-state.sql",
            "Database/journal.sql"
        };

        foreach (var relativePath in forbiddenNewPaths)
            Assert.IsFalse(Directory.Exists(Path.Combine(root, relativePath)) || File.Exists(Path.Combine(root, relativePath)), $"H02 must not add runner/schema surface: {relativePath}");

        AssertContainsAll(ReadReceipt(),
            "H02 does not install DbUp.",
            "H02 does not add package references.",
            "H02 does not add a runner project.",
            "H02 does not add SQL schema changes.",
            "H02 does not change `Database/migrations.json`.",
            "H02 does not change `Database/apply-migrations.ps1`.",
            "H02 does not change `Database/verify-migrations.ps1`.",
            "H02 does not execute a migration.");
    }

    [TestMethod]
    public void MigrationRunnerSpike_RecordsRecommendationWithoutGrantingAuthority()
    {
        var spike = ReadSpike();
        var receipt = ReadReceipt();

        AssertContainsAll(spike,
            "Recommendation: `RecommendDeferral`.",
            "This recommendation does not authorize implementation.",
            "DbUp remains a plausible future runner only if adopted through a bounded migration CLI",
            "DbUp journal/state is evidence only.",
            "A migration runner is execution machinery only.");

        AssertContainsAll(receipt,
            "Recommendation: `RecommendDeferral`.",
            "This is a spike, not adoption.",
            "H02 does not adopt DbUp and does not reject DbUp permanently.",
            "The next intended slice is H03 - Migration script hash and manifest order contract.",
            "A migration runner is not approval.",
            "A migration runner is not verification.",
            "A migration runner is not release readiness.",
            "A migration runner is not deployment readiness.");
    }

    private static string ReadSpike() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "spikes", "H02_MIGRATION_RUNNER_SPIKE.md"));

    private static string ReadReceipt() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "H02_MIGRATION_RUNNER_SPIKE.md"));

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

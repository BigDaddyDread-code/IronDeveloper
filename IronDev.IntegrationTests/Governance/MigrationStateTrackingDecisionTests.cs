using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("DatabaseMigration")]
[TestCategory("StaticBoundary")]
[TestCategory("Decision")]
public sealed class MigrationStateTrackingDecisionTests
{
    [TestMethod]
    public void MigrationStateDecision_DefinesStateAsEvidenceOnly()
    {
        var adr = ReadAdr();

        AssertContainsAll(adr,
            "Migration state is evidence, not database authority.",
            "A recorded migration is not a safe database.",
            "A migration state record is not release approval.",
            "A migration state record is not deployment approval.",
            "A migration state record is not schema verification by itself.",
            "A migration state record is not permission to skip verification.",
            "A migration state record is evidence only.");
    }

    [TestMethod]
    public void MigrationStateDecision_SeparatesManifestApplyVerifyAndState()
    {
        var adr = ReadAdr();

        AssertContainsAll(adr,
            "Migration manifest defines expected migration identity and order.",
            "Migration scripts define intended database changes.",
            "Apply execution attempts to run scripts.",
            "Database verification proves expected objects, constraints, and procedures exist.",
            "Migration state records evidence about what happened.",
            "State follows execution and verification.",
            "State does not replace execution or verification.");
    }

    [TestMethod]
    public void MigrationStateDecision_ForbidsRuntimeSchemaMutationAuthority()
    {
        var adr = ReadAdr();

        AssertContainsAll(adr,
            "Runtime application services must not create or update migration state as a side effect of normal API, CLI, agent, tool, or workflow execution.",
            "migration CLI",
            "migration CI script",
            "controlled migration runner",
            "explicit administrative migration command",
            "API request handlers",
            "agent runtime",
            "workflow runner",
            "tool execution path",
            "source apply executor",
            "rollback executor",
            "memory promotion path",
            "frontend",
            "background worker unless explicitly acting as migration runner");
    }

    [TestMethod]
    public void MigrationStateDecision_DefinesFailureAndDriftStates()
    {
        var adr = ReadAdr();

        AssertContainsAll(adr,
            "Failed",
            "Verified",
            "Only `Verified` may be treated as evidence that both apply and verify completed.",
            "VerificationFailedAfterApply",
            "StateMissingButObjectsExist",
            "StateExistsButObjectsMissing",
            "ScriptHashChangedAfterApply",
            "ManifestOrderMismatch",
            "DuplicateStateRecord",
            "UnknownStateRecord",
            "ManualInterventionDetected",
            "Partial success must not be recorded as `Verified`.",
            "Retried attempts must be separate attempts, not overwritten history.");
    }

    [TestMethod]
    public void MigrationStateDecision_DoesNotIntroduceMigrationImplementation()
    {
        var root = RepositoryRoot();
        var h01Files = new[]
        {
            Path.Combine(root, "Docs", "decisions", "ADR-017-migration-state-tracking.md"),
            Path.Combine(root, "Docs", "receipts", "H01_MIGRATION_STATE_TRACKING_DECISION.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "MigrationStateTrackingDecisionTests.cs"),
            Path.Combine(root, "Docs", "testing", "INTEGRATION_TEST_CATEGORIES.md"),
            Path.Combine(root, "Database", "README.md")
        };

        foreach (var file in h01Files)
            Assert.IsTrue(File.Exists(file), $"Expected H01 file missing: {file}");

        var forbidden = new[]
        {
            string.Concat("CREATE ", "TABLE"),
            string.Concat("CREATE ", "PROCEDURE"),
            string.Concat("ALTER ", "TABLE"),
            string.Concat("DROP ", "TABLE"),
            string.Concat("Sql", "Connection"),
            string.Concat("Db", "Connection"),
            string.Concat("Da", "pper"),
            string.Concat("Execute", "Async"),
            string.Concat("PowerShell ", "migration ", "execution"),
            string.Concat("apply-", "migrations.ps1 ", "invocation"),
            string.Concat("verify-", "migrations.ps1 ", "invocation")
        };

        foreach (var file in h01Files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
                Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"H01 static contract file must not introduce implementation token '{token}': {file}");
        }

        var changedFileNames = h01Files.Select(path => NormalizePath(Path.GetRelativePath(root, path))).ToArray();
        CollectionAssert.DoesNotContain(changedFileNames, "Database/migrations.json");
        CollectionAssert.DoesNotContain(changedFileNames, "Database/apply-migrations.ps1");
        CollectionAssert.DoesNotContain(changedFileNames, "Database/verify-migrations.ps1");
    }

    [TestMethod]
    public void MigrationStateReceipt_RecordsDecisionScopeAndLimitations()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt,
            "H01 does not create a migration-state table.",
            "H01 does not create a migration runner.",
            "H01 does not change apply-migrations.ps1.",
            "H01 does not change verify-migrations.ps1.",
            "H01 does not execute migrations.",
            "H01 does not grant release approval.",
            "H01 does not grant deployment approval.",
            "H01 does not prove any database is safe.",
            "Migration state is evidence only.",
            "StateMissingButObjectsExist",
            "StateExistsButObjectsMissing",
            "ScriptHashChangedAfterApply",
            "ManifestOrderMismatch",
            "VerificationFailedAfterApply",
            "H02 - Migration state schema contract.");
    }

    private static string ReadAdr() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "decisions", "ADR-017-migration-state-tracking.md"));

    private static string ReadReceipt() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "H01_MIGRATION_STATE_TRACKING_DECISION.md"));

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

    private static string NormalizePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}

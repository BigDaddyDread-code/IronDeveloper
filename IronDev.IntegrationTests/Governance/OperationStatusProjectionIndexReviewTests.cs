using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("OperationStatus")]
[TestCategory("StorageReview")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class OperationStatusProjectionIndexReviewTests
{
    private const string ReviewPath = "Docs/reviews/H07_OPERATION_STATUS_PROJECTION_INDEX_REVIEW.md";
    private const string ReceiptPath = "Docs/receipts/H07_OPERATION_STATUS_PROJECTION_INDEXES.md";
    private const string H07MigrationPath = "Database/migrate_operation_status_projection_indexes.sql";

    [TestMethod]
    public void OperationStatusProjectionIndexReview_RecordsNoExistingProjectionTable()
    {
        var review = ReviewText();
        var databaseText = DatabaseText();

        AssertContainsAll(
            review,
            "Outcome selected: `DeferredNoExistingProjectionStorage`",
            "H07 found no existing SQL-backed operation-status projection table safe to index.",
            "No `Database` migration or verification file currently defines a dedicated operation-status projection table",
            "No projection table means no projection index.",
            "You cannot index a projection that does not exist.",
            "GovernedOperationStatusReadRepository",
            "OperationStatusProjector",
            "OperationStatusPaginator");

        Assert.IsFalse(databaseText.Contains("OperationStatus", StringComparison.OrdinalIgnoreCase), "Database files must not already define an operation-status projection target.");
        Assert.IsFalse(databaseText.Contains("GovernedOperationStatus", StringComparison.OrdinalIgnoreCase), "Database files must not already define a governed-operation-status projection target.");
        Assert.IsFalse(databaseText.Contains("StatusProjection", StringComparison.OrdinalIgnoreCase), "Database files must not already define a status-projection target.");
    }

    [TestMethod]
    public void OperationStatusProjectionIndexReview_DoesNotCreateDatabaseMigration()
    {
        var root = RepositoryRoot();
        var receipt = ReceiptText();
        var migrations = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));

        Assert.IsFalse(File.Exists(Path.Combine(root, H07MigrationPath)), "Outcome B must not add an H07 migration.");
        Assert.IsFalse(migrations.Contains("migrate_operation_status_projection_indexes.sql", StringComparison.OrdinalIgnoreCase), "Outcome B must not register an H07 migration.");
        Assert.IsFalse(verifier.Contains("operation status projection index", StringComparison.OrdinalIgnoreCase), "Outcome B must not add index verification for missing storage.");

        AssertContainsAll(
            receipt,
            "No database files changed.",
            "H07 does not add a SQL migration.",
            "H07 does not create an operation-status projection table.",
            "H07 does not add indexes.");
    }

    [TestMethod]
    public void OperationStatusProjectionIndexReview_RecordsFuturePrerequisite()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        AssertContainsAll(
            review,
            "Physical operation-status projection indexes require a future durable projection-storage slice first.",
            "exact SQL table/read-model storage name",
            "owner migration",
            "write/projection semantics",
            "read repository or stored procedure surface",
            "tenant/project scoping",
            "UTC timestamp behavior",
            "rebuild/backfill boundary",
            "non-authority boundary");

        AssertContainsAll(
            receipt,
            "H07 defers physical index work until projection storage exists.",
            "H07 is a deferral/review slice. It does not create durable projection storage.");
    }

    [TestMethod]
    public void OperationStatusProjectionIndexReview_DoesNotGrantAuthority()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        foreach (var text in new[] { review, receipt })
        {
            AssertContainsAll(
                text,
                "An operation-status projection is not approval.",
                "An operation-status projection is not policy satisfaction.",
                "An operation-status projection is not source-apply authority.",
                "An operation-status projection is not workflow continuation authority.",
                "An operation-status projection is not merge readiness.",
                "An operation-status projection is not release readiness.",
                "An operation-status projection is not deployment readiness.",
                "An operation-status projection is not rollback authority.",
                "An operation-status projection is not retry authority.",
                "An operation-status index is not authority.",
                "Fast operation-status lookup is not authority.",
                "Status projection does not choose next safe action.",
                "Status projection does not prove underlying evidence is true.",
                "Status projection does not replace governance events, receipts, or evidence records.",
                "Operation status indexes improve lookup only.");
        }
    }

    [TestMethod]
    public void Receipt_RecordsDeferralScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H07 does not make operation status authoritative.",
            "H07 does not change status projection semantics.",
            "H07 does not change next-safe-action decisions.",
            "H07 does not grant approval.",
            "H07 does not grant policy satisfaction.",
            "H07 does not grant source-apply authority.",
            "H07 does not grant workflow continuation authority.",
            "H07 does not grant release readiness.",
            "H07 does not grant deployment readiness.",
            "H07 does not change API/CLI/UI behavior.",
            "H07 does not change Weaviate behavior.",
            "H07 found no existing SQL-backed operation-status projection table safe to index.",
            "H07 does not add indexes.",
            "H07 defers physical index work until projection storage exists.",
            "A fast status projection is still a projection.");
    }

    private static string ReviewText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ReviewPath));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ReceiptPath));

    private static string DatabaseText()
    {
        var root = RepositoryRoot();
        var databasePath = Path.Combine(root, "Database");
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(databasePath, "*.*", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase)))
                .Where(path => path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

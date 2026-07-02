using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("Receipt")]
[TestCategory("Store")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("StorageReview")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed partial class ReceiptTableIndexReviewTests : IntegrationTestBase
{
    private const string ReviewPath = "Docs/reviews/H05_RECEIPT_TABLE_INDEX_REVIEW.md";
    private const string ReceiptPath = "Docs/receipts/H05_RECEIPT_TABLE_INDEX_REVIEW.md";

    private static readonly string[] ExpectedReceiptTables =
    [
        "ControlledDryRunReceipt",
        "DogfoodReceipt",
        "RollbackExecutionReceipt",
        "RollbackSupportReceipt",
        "SourceApplyDryRunReceipt",
        "SourceApplyReceipt"
    ];

    private static readonly string[] SetupMigrations =
    [
        "migrate_governance_event.sql",
        "migrate_tool_request.sql",
        "migrate_tool_gate_decision.sql",
        "migrate_approval_decision.sql",
        "migrate_policy_decision_event.sql",
        "migrate_dogfood_receipt.sql",
        "migrate_controlled_dry_run_receipt.sql",
        "migrate_rollback_support_receipt.sql",
        "migrate_source_apply_dry_run_receipt.sql",
        "migrate_source_apply_receipt.sql",
        "migrate_rollback_execution_receipt.sql"
    ];

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await EnsureReceiptMetadataObjectsExistAsync();
    }

    [TestMethod]
    public async Task ReceiptStorageReview_DocumentsReceiptTablesAndIndexes()
    {
        var review = ReviewText();
        var tables = await ReceiptTablesAsync();
        var indexes = await ReceiptIndexesAsync();

        CollectionAssert.AreEquivalent(ExpectedReceiptTables, tables.Select(table => table.Name).ToArray());

        foreach (var table in tables)
            StringAssert.Contains(review, $"`governance.{table.Name}`");

        foreach (var index in indexes)
            StringAssert.Contains(review, $"`{index.Name}`", $"Review must document index {index.TableName}.{index.Name}.");
    }

    [TestMethod]
    public void ReceiptStorageReview_DocumentsKnownLookupPaths()
    {
        var review = ReviewText();

        AssertContainsAll(
            review,
            "By receipt ID",
            "By project",
            "By operation ID",
            "By run ID",
            "By tool request / action request",
            "By correlation / causation",
            "By created timestamp",
            "Latest receipts for a project/run",
            "Diagnostic investigation lookup",
            "SqlControlledDryRunReceiptStore",
            "SqlDogfoodReceiptStore",
            "SqlRollbackExecutionReceiptStore",
            "SqlRollbackSupportReceiptStore",
            "SqlSourceApplyDryRunReceiptStore",
            "SqlSourceApplyReceiptStore",
            "usp_DogfoodReceipt_ListForProject",
            "usp_SourceApplyReceipt_ListBySourceApplyRequest",
            "usp_RollbackExecutionReceipt_ListBySourceApplyReceipt");
    }

    [TestMethod]
    public void ReceiptStorageReview_RecordsIndexSupportFindingsWithoutChangingSchema()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        AssertContainsAll(
            review,
            "Supported",
            "PartiallySupported",
            "Unsupported",
            "Unclear",
            "NotApplicable",
            "H05-INFO-001",
            "H05-LOW-001",
            "H05-LOW-002",
            "H05-LOW-003",
            "H05-MEDIUM-001");

        AssertContainsAll(
            receipt,
            "H05 does not add indexes.",
            "H05 does not remove indexes.",
            "H05 does not alter receipt tables.",
            "H05 does not add a SQL migration.");
    }

    [TestMethod]
    public async Task ReceiptStorageReview_DoesNotMutateReceiptStorage()
    {
        var before = await MetadataCountsAsync();

        _ = await ReceiptTablesAsync();
        _ = await ReceiptColumnsAsync();
        _ = await ReceiptIndexesAsync();
        _ = await ReceiptProceduresAsync();
        _ = await ReceiptDefaultsAsync();
        _ = await ReceiptChecksAsync();

        var after = await MetadataCountsAsync();

        Assert.AreEqual(before.TableCount, after.TableCount);
        Assert.AreEqual(before.ColumnCount, after.ColumnCount);
        Assert.AreEqual(before.IndexCount, after.IndexCount);
        Assert.AreEqual(before.ProcedureCount, after.ProcedureCount);
        Assert.AreEqual(before.DefaultConstraintCount, after.DefaultConstraintCount);
        Assert.AreEqual(before.CheckConstraintCount, after.CheckConstraintCount);
    }

    [TestMethod]
    public void ReceiptStorageReview_DoesNotTreatReceiptsOrIndexesAsAuthority()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        foreach (var text in new[] { review, receipt })
        {
            AssertContainsAll(
                text,
                "A receipt row is not approval.",
                "A receipt row is not policy satisfaction.",
                "A receipt index is not authority.",
                "A fast receipt lookup is still just evidence.");
        }
    }

    [TestMethod]
    public void ReceiptStorageReview_PreservesSqlSourceOfTruthAndRebuildableIndexBoundary()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        foreach (var text in new[] { review, receipt })
        {
            AssertContainsAll(
                text,
                "SQL remains source of truth.",
                "Weaviate is rebuildable.",
                "Read models may be rebuildable.",
                "Authority records cannot be vibes.");
        }
    }

    [TestMethod]
    public void Receipt_RecordsReviewScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H05 does not add a SQL migration.",
            "H05 does not alter receipt tables.",
            "H05 does not add indexes.",
            "H05 does not remove indexes.",
            "H05 does not alter stored procedures.",
            "H05 does not change permissions.",
            "H05 does not add API/CLI/UI behavior.",
            "H05 does not change workflow/source-apply/rollback/release/deployment authority.",
            "H05 does not implement retention or redaction.",
            "H05 does not change Weaviate behavior.",
            "Receipt indexes improve lookup only.",
            "Receipt rows are evidence, not approval.",
            "A fast receipt lookup is still just evidence.");
    }

    private async Task EnsureReceiptMetadataObjectsExistAsync()
    {
        if (await ExpectedReceiptTableCountAsync() == ExpectedReceiptTables.Length)
            return;

        foreach (var migration in SetupMigrations)
            await ApplySqlFileAsync("Database", migration);
    }

    private async Task<int> ExpectedReceiptTableCountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.tables t
            WHERE t.schema_id = SCHEMA_ID(N'governance')
              AND t.name IN @Names;
            """,
            new { Names = ExpectedReceiptTables });
    }

    private async Task<IReadOnlyList<ReceiptTableMetadata>> ReceiptTablesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<ReceiptTableMetadata>(
            """
            SELECT t.name AS Name
            FROM sys.tables t
            WHERE t.schema_id = SCHEMA_ID(N'governance')
              AND t.name IN @Names
            ORDER BY t.name;
            """,
            new { Names = ExpectedReceiptTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<ReceiptColumnMetadata>> ReceiptColumnsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<ReceiptColumnMetadata>(
            """
            SELECT t.name AS TableName, c.name AS Name
            FROM sys.tables t
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE t.schema_id = SCHEMA_ID(N'governance')
              AND t.name IN @Names
            ORDER BY t.name, c.column_id;
            """,
            new { Names = ExpectedReceiptTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<ReceiptIndexMetadata>> ReceiptIndexesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<ReceiptIndexMetadata>(
            """
            SELECT
                t.name AS TableName,
                i.name AS Name,
                i.is_unique AS IsUnique,
                i.has_filter AS HasFilter,
                ISNULL(i.filter_definition, N'') AS FilterDefinition
            FROM sys.tables t
            INNER JOIN sys.indexes i ON i.object_id = t.object_id
            WHERE t.schema_id = SCHEMA_ID(N'governance')
              AND t.name IN @Names
              AND i.index_id > 0
            ORDER BY t.name, i.name;
            """,
            new { Names = ExpectedReceiptTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<ReceiptProcedureMetadata>> ReceiptProceduresAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<ReceiptProcedureMetadata>(
            """
            SELECT p.name AS Name
            FROM sys.procedures p
            INNER JOIN sys.sql_modules m ON m.object_id = p.object_id
            WHERE p.schema_id = SCHEMA_ID(N'governance')
              AND p.name LIKE N'usp_%Receipt%'
            ORDER BY p.name;
            """);

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<ReceiptConstraintMetadata>> ReceiptDefaultsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<ReceiptConstraintMetadata>(
            """
            SELECT t.name AS TableName, dc.name AS Name
            FROM sys.tables t
            INNER JOIN sys.default_constraints dc ON dc.parent_object_id = t.object_id
            WHERE t.schema_id = SCHEMA_ID(N'governance')
              AND t.name IN @Names
            ORDER BY t.name, dc.name;
            """,
            new { Names = ExpectedReceiptTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<ReceiptConstraintMetadata>> ReceiptChecksAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<ReceiptConstraintMetadata>(
            """
            SELECT t.name AS TableName, cc.name AS Name
            FROM sys.tables t
            INNER JOIN sys.check_constraints cc ON cc.parent_object_id = t.object_id
            WHERE t.schema_id = SCHEMA_ID(N'governance')
              AND t.name IN @Names
            ORDER BY t.name, cc.name;
            """,
            new { Names = ExpectedReceiptTables });

        return rows.ToArray();
    }

    private async Task<MetadataCounts> MetadataCountsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<MetadataCounts>(
            """
            SELECT
                (SELECT COUNT(*) FROM sys.tables t WHERE t.schema_id = SCHEMA_ID(N'governance') AND t.name IN @Names) AS TableCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.columns c ON c.object_id = t.object_id WHERE t.schema_id = SCHEMA_ID(N'governance') AND t.name IN @Names) AS ColumnCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.indexes i ON i.object_id = t.object_id WHERE t.schema_id = SCHEMA_ID(N'governance') AND t.name IN @Names AND i.index_id > 0) AS IndexCount,
                (SELECT COUNT(*) FROM sys.procedures p WHERE p.schema_id = SCHEMA_ID(N'governance') AND p.name LIKE N'usp_%Receipt%') AS ProcedureCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.default_constraints dc ON dc.parent_object_id = t.object_id WHERE t.schema_id = SCHEMA_ID(N'governance') AND t.name IN @Names) AS DefaultConstraintCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.check_constraints cc ON cc.parent_object_id = t.object_id WHERE t.schema_id = SCHEMA_ID(N'governance') AND t.name IN @Names) AS CheckConstraintCount;
            """,
            new { Names = ExpectedReceiptTables });
    }

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepositoryRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in SplitSqlBatches(sql))
            await connection.ExecuteAsync(batch);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static string ReviewText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ReviewPath));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ReceiptPath));

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

    private sealed record ReceiptTableMetadata
    {
        public required string Name { get; init; }
    }

    private sealed record ReceiptColumnMetadata
    {
        public required string TableName { get; init; }
        public required string Name { get; init; }
    }

    private sealed record ReceiptIndexMetadata
    {
        public required string TableName { get; init; }
        public required string Name { get; init; }
        public required bool IsUnique { get; init; }
        public required bool HasFilter { get; init; }
        public required string FilterDefinition { get; init; }
    }

    private sealed record ReceiptProcedureMetadata
    {
        public required string Name { get; init; }
    }

    private sealed record ReceiptConstraintMetadata
    {
        public required string TableName { get; init; }
        public required string Name { get; init; }
    }

    private sealed record MetadataCounts
    {
        public required int TableCount { get; init; }
        public required int ColumnCount { get; init; }
        public required int IndexCount { get; init; }
        public required int ProcedureCount { get; init; }
        public required int DefaultConstraintCount { get; init; }
        public required int CheckConstraintCount { get; init; }
    }
}

using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("Evidence")]
[TestCategory("Store")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("StorageReview")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed partial class EvidenceTableIndexReviewTests : IntegrationTestBase
{
    private const string ReviewPath = "Docs/reviews/H06_EVIDENCE_TABLE_INDEX_REVIEW.md";
    private const string ReceiptPath = "Docs/receipts/H06_EVIDENCE_TABLE_INDEX_REVIEW.md";

    private static readonly string[] ExpectedEvidenceTables =
    [
        "agent.AgentLocalMemoryEvidenceRef",
        "a2a.AgentHandoffEvidenceReference",
        "a2a.AgentHandoffEvidenceAllowedUse",
        "workflow.WorkflowRunEvidenceReference",
        "workflow.WorkflowCheckpointEvidenceReference",
        "memory.MemoryProposalEvidenceReference"
    ];

    private static readonly string[] SetupMigrations =
    [
        "migrate_governance_event.sql",
        "migrate_agent_local_memory.sql",
        "migrate_agent_memory_stored_procedures.sql",
        "migrate_agent_handoff.sql",
        "migrate_workflow_run.sql",
        "migrate_workflow_step_store.sql",
        "migrate_workflow_checkpoint_store.sql",
        "migrate_memory_proposal_staging.sql"
    ];

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await EnsureEvidenceMetadataObjectsExistAsync();
    }

    [TestMethod]
    public async Task EvidenceStorageReview_DocumentsEvidenceTablesAndIndexes()
    {
        var review = ReviewText();
        var tables = await EvidenceTablesAsync();
        var indexes = await EvidenceIndexesAsync();

        CollectionAssert.AreEquivalent(ExpectedEvidenceTables, tables.Select(table => table.FullName).ToArray());

        foreach (var table in tables)
            StringAssert.Contains(review, $"`{table.FullName}`");

        foreach (var index in indexes)
            StringAssert.Contains(review, $"`{index.Name}`", $"Review must document index {index.FullTableName}.{index.Name}.");
    }

    [TestMethod]
    public void EvidenceStorageReview_DocumentsKnownLookupPaths()
    {
        var review = ReviewText();

        AssertContainsAll(
            review,
            "By parent memory item",
            "By handoff",
            "By workflow run",
            "By workflow checkpoint",
            "By memory proposal",
            "By allowed use",
            "By evidence ID",
            "Adjacent grounding reference",
            "SqlAgentLocalMemoryStore",
            "SqlAgentHandoffStore",
            "SqlWorkflowRunStore",
            "SqlWorkflowStepStore",
            "SqlWorkflowCheckpointStore",
            "SqlMemoryProposalStagingStore",
            "agent.usp_AgentLocalMemory_Create",
            "a2a.usp_AgentHandoff_Create",
            "workflow.usp_WorkflowRun_Create",
            "workflow.usp_WorkflowCheckpoint_Create",
            "memory.usp_MemoryProposal_Create");
    }

    [TestMethod]
    public void EvidenceStorageReview_RecordsIndexSupportFindingsWithoutChangingSchema()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        AssertContainsAll(
            review,
            "Supported",
            "PartiallySupported",
            "NotApplicable",
            "H06-INFO-001",
            "H06-LOW-001",
            "H06-LOW-002",
            "H06-LOW-003",
            "H06-MEDIUM-001");

        AssertContainsAll(
            receipt,
            "H06 does not add indexes.",
            "H06 does not remove indexes.",
            "H06 does not alter evidence tables.",
            "H06 does not add a SQL migration.");
    }

    [TestMethod]
    public async Task EvidenceStorageReview_DoesNotMutateEvidenceStorage()
    {
        var before = await MetadataCountsAsync();

        _ = await EvidenceTablesAsync();
        _ = await EvidenceColumnsAsync();
        _ = await EvidenceIndexesAsync();
        _ = await EvidenceProceduresAsync();
        _ = await EvidenceDefaultsAsync();
        _ = await EvidenceChecksAsync();
        _ = await EvidenceTriggersAsync();

        var after = await MetadataCountsAsync();

        Assert.AreEqual(before.TableCount, after.TableCount);
        Assert.AreEqual(before.ColumnCount, after.ColumnCount);
        Assert.AreEqual(before.IndexCount, after.IndexCount);
        Assert.AreEqual(before.ProcedureCount, after.ProcedureCount);
        Assert.AreEqual(before.DefaultConstraintCount, after.DefaultConstraintCount);
        Assert.AreEqual(before.CheckConstraintCount, after.CheckConstraintCount);
        Assert.AreEqual(before.TriggerCount, after.TriggerCount);
    }

    [TestMethod]
    public void EvidenceStorageReview_DoesNotTreatEvidenceOrIndexesAsAuthority()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        foreach (var text in new[] { review, receipt })
        {
            AssertContainsAll(
                text,
                "An evidence row is not approval.",
                "An evidence row is not policy satisfaction.",
                "An evidence row is not source-apply authority.",
                "An evidence row is not workflow continuation authority.",
                "An evidence row is not merge readiness.",
                "An evidence row is not release readiness.",
                "An evidence row is not deployment readiness.",
                "An evidence index is not authority.",
                "Fast evidence retrieval is not authority.",
                "Evidence existence is not evidence truth.",
                "Evidence retrieval is not evidence validation.");
        }
    }

    [TestMethod]
    public void EvidenceStorageReview_PreservesSqlSourceOfTruthAndRebuildableIndexBoundary()
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
    public void EvidenceStorageReview_RecordsPayloadRetentionAndArtifactRetentionAsLaterWork()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        AssertContainsAll(
            review,
            "Payload / Retention / Artifact Risk Review",
            "Evidence artifact retention is later work.",
            "H06-MEDIUM-001");

        AssertContainsAll(
            receipt,
            "H06 does not implement retention or redaction.",
            "H06 does not implement evidence artifact retention.");
    }

    [TestMethod]
    public void Receipt_RecordsReviewScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H06 does not add a SQL migration.",
            "H06 does not alter evidence tables.",
            "H06 does not add indexes.",
            "H06 does not remove indexes.",
            "H06 does not alter stored procedures.",
            "H06 does not change permissions.",
            "H06 does not add API/CLI/UI behavior.",
            "H06 does not change workflow/source-apply/rollback/release/deployment authority.",
            "H06 does not implement retention or redaction.",
            "H06 does not implement evidence artifact retention.",
            "H06 does not change Weaviate behavior.",
            "Evidence indexes improve retrieval only.",
            "Evidence indexes improve retrieval. They do not make evidence authoritative.",
            "Fast evidence is still just evidence.");
    }

    private async Task EnsureEvidenceMetadataObjectsExistAsync()
    {
        var counts = await MetadataCountsAsync();
        if (counts.TableCount == ExpectedEvidenceTables.Length && counts.IndexCount >= 13)
            return;

        foreach (var migration in SetupMigrations)
            await ApplySqlFileAsync("Database", migration);
    }

    private async Task<IReadOnlyList<EvidenceTableMetadata>> EvidenceTablesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceTableMetadata>(
            """
            SELECT
                SCHEMA_NAME(t.schema_id) AS SchemaName,
                t.name AS Name,
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullName
            FROM sys.tables t
            WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames
            ORDER BY SchemaName, Name;
            """,
            new { FullNames = ExpectedEvidenceTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<EvidenceColumnMetadata>> EvidenceColumnsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceColumnMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullTableName,
                c.name AS Name
            FROM sys.tables t
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames
            ORDER BY FullTableName, c.column_id;
            """,
            new { FullNames = ExpectedEvidenceTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<EvidenceIndexMetadata>> EvidenceIndexesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceIndexMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullTableName,
                i.name AS Name,
                i.is_unique AS IsUnique,
                i.has_filter AS HasFilter,
                ISNULL(i.filter_definition, N'') AS FilterDefinition
            FROM sys.tables t
            INNER JOIN sys.indexes i ON i.object_id = t.object_id
            WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames
              AND i.index_id > 0
            ORDER BY FullTableName, i.name;
            """,
            new { FullNames = ExpectedEvidenceTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<EvidenceProcedureMetadata>> EvidenceProceduresAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceProcedureMetadata>(
            """
            SELECT CONCAT(SCHEMA_NAME(p.schema_id), N'.', p.name) AS FullName
            FROM sys.procedures p
            INNER JOIN sys.sql_modules m ON m.object_id = p.object_id
            WHERE p.name LIKE N'%Evidence%'
               OR m.definition LIKE N'%EvidenceReference%'
               OR m.definition LIKE N'%EvidenceRef%'
            ORDER BY FullName;
            """);

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<EvidenceConstraintMetadata>> EvidenceDefaultsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceConstraintMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullTableName,
                dc.name AS Name
            FROM sys.tables t
            INNER JOIN sys.default_constraints dc ON dc.parent_object_id = t.object_id
            WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames
            ORDER BY FullTableName, dc.name;
            """,
            new { FullNames = ExpectedEvidenceTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<EvidenceConstraintMetadata>> EvidenceChecksAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceConstraintMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullTableName,
                cc.name AS Name
            FROM sys.tables t
            INNER JOIN sys.check_constraints cc ON cc.parent_object_id = t.object_id
            WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames
            ORDER BY FullTableName, cc.name;
            """,
            new { FullNames = ExpectedEvidenceTables });

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<EvidenceTriggerMetadata>> EvidenceTriggersAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<EvidenceTriggerMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullTableName,
                tr.name AS Name
            FROM sys.tables t
            INNER JOIN sys.triggers tr ON tr.parent_id = t.object_id
            WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames
            ORDER BY FullTableName, tr.name;
            """,
            new { FullNames = ExpectedEvidenceTables });

        return rows.ToArray();
    }

    private async Task<MetadataCounts> MetadataCountsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<MetadataCounts>(
            """
            SELECT
                (SELECT COUNT(*) FROM sys.tables t WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames) AS TableCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.columns c ON c.object_id = t.object_id WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames) AS ColumnCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.indexes i ON i.object_id = t.object_id WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames AND i.index_id > 0) AS IndexCount,
                (SELECT COUNT(*) FROM sys.procedures p INNER JOIN sys.sql_modules m ON m.object_id = p.object_id WHERE p.name LIKE N'%Evidence%' OR m.definition LIKE N'%EvidenceReference%' OR m.definition LIKE N'%EvidenceRef%') AS ProcedureCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.default_constraints dc ON dc.parent_object_id = t.object_id WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames) AS DefaultConstraintCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.check_constraints cc ON cc.parent_object_id = t.object_id WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames) AS CheckConstraintCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.triggers tr ON tr.parent_id = t.object_id WHERE CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) IN @FullNames) AS TriggerCount;
            """,
            new { FullNames = ExpectedEvidenceTables });
    }

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET ARITHABORT ON;
            SET NUMERIC_ROUNDABORT OFF;
            """);

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

    private sealed record EvidenceTableMetadata
    {
        public required string SchemaName { get; init; }
        public required string Name { get; init; }
        public required string FullName { get; init; }
    }

    private sealed record EvidenceColumnMetadata
    {
        public required string FullTableName { get; init; }
        public required string Name { get; init; }
    }

    private sealed record EvidenceIndexMetadata
    {
        public required string FullTableName { get; init; }
        public required string Name { get; init; }
        public required bool IsUnique { get; init; }
        public required bool HasFilter { get; init; }
        public required string FilterDefinition { get; init; }
    }

    private sealed record EvidenceProcedureMetadata
    {
        public required string FullName { get; init; }
    }

    private sealed record EvidenceConstraintMetadata
    {
        public required string FullTableName { get; init; }
        public required string Name { get; init; }
    }

    private sealed record EvidenceTriggerMetadata
    {
        public required string FullTableName { get; init; }
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
        public required int TriggerCount { get; init; }
    }
}

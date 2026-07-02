using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("Database")]
[TestCategory("UtcTimestamp")]
[TestCategory("StorageReview")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed partial class UtcTimestampDbConstraintReviewTests : IntegrationTestBase
{
    private const string ReviewPath = "Docs/reviews/H09_UTC_TIMESTAMP_DB_CONSTRAINT_REVIEW.md";
    private const string ReceiptPath = "Docs/receipts/H09_UTC_TIMESTAMP_DB_CONSTRAINT_REVIEW.md";
    private const string H09MigrationPath = "Database/migrate_h09_utc_timestamp_constraints.sql";

    private static readonly string[] RequiredClassifications =
    [
        "UtcEnforced",
        "UtcDefaulted",
        "UtcNamedOnly",
        "UtcParameterOrProcedureDependent",
        "LegacyAssumedUtc",
        "Ambiguous",
        "NonUtcOrLocalRisk",
        "NotApplicable"
    ];

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await EnsureCurrentMigrationMetadataObjectsExistAsync();
    }

    [TestMethod]
    public async Task UtcTimestampReview_DocumentsTimestampColumns()
    {
        var review = ReviewText();
        var columns = await TimestampColumnsAsync();

        Assert.IsTrue(columns.Count >= 100, "H09 should review the current app/governance timestamp-shaped database surface after migrations.");

        foreach (var column in columns)
            StringAssert.Contains(review, $"`{column.FullName}`", $"Review must document discovered timestamp-like column {column.FullName}.");

        AssertContainsAll(
            review,
            "Timestamp-like columns discovered: 135",
            "`governance.GovernanceEvent.CreatedUtc`",
            "`governance.AcceptedApproval.AcceptedAtUtc`",
            "`governance.SourceApplyReceipt.AppliedAtUtc`",
            "`governance.ReleaseReadinessDecisionRecord.DecidedAtUtc`",
            "`workflow.WorkflowRun.CreatedUtc`",
            "`dbo.RunEvents.TimestampUtc`",
            "`a2a.AgentHandoff.CreatedByActorId`",
            "`governance.SourceApplyReceipt.ObservedBranch`");
    }

    [TestMethod]
    public async Task UtcTimestampReview_ClassifiesDefaultsAndConstraints()
    {
        var review = ReviewText();
        var receipt = ReceiptText();
        var columns = await TimestampColumnsAsync();
        var classifications = columns.GroupBy(Classify).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var classification in RequiredClassifications)
            StringAssert.Contains(review, $"`{classification}`");

        Assert.IsTrue(classifications["UtcDefaulted"] > 0);
        Assert.IsTrue(classifications["UtcParameterOrProcedureDependent"] > 0);
        Assert.IsTrue(classifications["LegacyAssumedUtc"] > 0);
        Assert.IsTrue(classifications["NotApplicable"] > 0);

        Assert.AreEqual(0, classifications.GetValueOrDefault("UtcEnforced"), "H09 must not invent UTC-enforcing constraints.");
        Assert.AreEqual(0, classifications.GetValueOrDefault("NonUtcOrLocalRisk"), "Current metadata review should not find local-risk defaults.");

        AssertContainsAll(
            review,
            "Default expressions discovered: 65 `SYSUTCDATETIME()` defaults, 70 no-default/procedure-dependent candidates, and 0 local-risk defaults.",
            "No UTC-enforcing timestamp check constraints were found.",
            "32 timestamp-like columns have check constraints, but those checks are expiry/order/shape/authority checks rather than UTC-offset enforcement.",
            "UTC enforcement is absent at the database constraint layer.");

        AssertContainsAll(
            receipt,
            "UtcDefaulted: 65",
            "UtcParameterOrProcedureDependent: 35",
            "LegacyAssumedUtc: 5",
            "NotApplicable: 30",
            "UtcEnforced: 0");
    }

    [TestMethod]
    public async Task UtcTimestampReview_RecordsProcedureTimestampWritePatterns()
    {
        var review = ReviewText();
        var procedures = await TimestampProcedurePatternsAsync();

        Assert.IsTrue(procedures.Count >= 100, "H09 should review timestamp-shaped procedure definitions after current migrations.");
        Assert.AreEqual(0, procedures.Count(procedure => procedure.UsesGetDate || procedure.UsesCurrentTimestamp || procedure.UsesSysDateTime || procedure.UsesSysDateTimeOffset));
        Assert.IsTrue(procedures.Count(procedure => procedure.UsesSysUtcDateTime) > 0);
        Assert.IsTrue(procedures.Count(procedure => procedure.HasUtcNamedParameters) > 0);

        AssertContainsAll(
            review,
            "Procedure candidates reviewed: 123",
            "11 procedures use `SYSUTCDATETIME()`.",
            "119 procedures have UTC-named parameters or UTC-shaped fields.",
            "0 procedures use `GETDATE()`.",
            "0 procedures use `CURRENT_TIMESTAMP`.",
            "0 procedures use `SYSDATETIME()`.",
            "0 procedures use `SYSDATETIMEOFFSET()`.",
            "`governance.usp_AcceptedApproval_Save`",
            "`governance.usp_PolicySatisfaction_Save`",
            "`governance.usp_SourceApplyReceipt_Save`",
            "`governance.usp_ReleaseReadinessDecisionRecord_Save`");
    }

    [TestMethod]
    public async Task UtcTimestampReview_DoesNotMutateDatabaseSchema()
    {
        var before = await MetadataCountsAsync();

        _ = await TimestampColumnsAsync();
        _ = await TimestampDefaultsAsync();
        _ = await TimestampChecksAsync();
        _ = await TimestampProcedurePatternsAsync();

        var after = await MetadataCountsAsync();

        Assert.AreEqual(before.TableCount, after.TableCount);
        Assert.AreEqual(before.ColumnCount, after.ColumnCount);
        Assert.AreEqual(before.DefaultConstraintCount, after.DefaultConstraintCount);
        Assert.AreEqual(before.CheckConstraintCount, after.CheckConstraintCount);
        Assert.AreEqual(before.ProcedureCount, after.ProcedureCount);
        Assert.AreEqual(before.TriggerCount, after.TriggerCount);
    }

    [TestMethod]
    public void UtcTimestampReview_DoesNotAddUtcConstraints()
    {
        var root = RepositoryRoot();
        var migrations = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var apply = File.ReadAllText(Path.Combine(root, "Database", "apply-migrations.ps1"));
        var verify = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));
        var receipt = ReceiptText();

        Assert.IsFalse(File.Exists(Path.Combine(root, H09MigrationPath)), "H09 must not add a UTC timestamp constraint migration.");
        Assert.IsFalse(migrations.Contains("h09", StringComparison.OrdinalIgnoreCase), "H09 must not register a migration.");
        Assert.IsFalse(apply.Contains("h09", StringComparison.OrdinalIgnoreCase), "H09 must not alter migration application.");
        Assert.IsFalse(verify.Contains("h09", StringComparison.OrdinalIgnoreCase), "H09 must not alter migration verification.");

        AssertContainsAll(
            receipt,
            "H09 does not add a SQL migration.",
            "H09 does not add check constraints.",
            "H09 does not alter check constraints.",
            "H09 does not add default constraints.",
            "H09 does not alter default constraints.");
    }

    [TestMethod]
    public void UtcTimestampReview_PreservesUtcStandardWithoutExpandingScope()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        foreach (var text in new[] { review, receipt })
        {
            AssertContainsAll(
                text,
                "Persist UTC.",
                "Transmit UTC.",
                "Display UTC-aware dates.",
                "H09 is DB metadata review only.");
        }
    }

    [TestMethod]
    public void UtcTimestampReview_DoesNotTreatCorrectTimeAsAuthority()
    {
        var review = ReviewText();
        var receipt = ReceiptText();

        foreach (var text in new[] { review, receipt })
        {
            AssertContainsAll(
                text,
                "UTC timestamp shape is not approval.",
                "UTC timestamp shape is not policy satisfaction.",
                "UTC timestamp shape is not source-apply authority.",
                "UTC timestamp shape is not workflow continuation authority.",
                "UTC timestamp shape is not release readiness.",
                "UTC timestamp shape is not deployment readiness.",
                "A correctly timed lie is still a lie.");
        }
    }

    [TestMethod]
    public void Receipt_RecordsReviewScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H09 does not add a SQL migration.",
            "H09 does not alter tables.",
            "H09 does not alter timestamp columns.",
            "H09 does not rename timestamp columns.",
            "H09 does not add default constraints.",
            "H09 does not alter default constraints.",
            "H09 does not add check constraints.",
            "H09 does not alter check constraints.",
            "H09 does not alter stored procedures.",
            "H09 does not change API/CLI/UI behavior.",
            "H09 does not change workflow/source-apply/rollback/release/deployment authority.",
            "H09 does not change Weaviate behavior.",
            "gaps are findings only",
            "UTC timestamps make time comparable only.");
    }

    private async Task EnsureCurrentMigrationMetadataObjectsExistAsync()
    {
        if (await DatabaseObjectExistsAsync("governance.ReleaseReadinessDecisionRecord", "U"))
            return;

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrations.json")));
        var migrations = document.RootElement.GetProperty("migrations")
            .EnumerateArray()
            .Select(migration => migration.GetProperty("path").GetString())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        foreach (var migration in migrations)
            await ApplySqlFileAsync(migration);
    }

    private async Task<bool> DatabaseObjectExistsAsync(string objectName, string objectType)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(@ObjectName, @ObjectType) IS NULL THEN 0 ELSE 1 END",
            new { ObjectName = objectName, ObjectType = objectType }) == 1;
    }

    private async Task<IReadOnlyList<TimestampColumnMetadata>> TimestampColumnsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<TimestampColumnMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name, N'.', c.name) AS FullName,
                SCHEMA_NAME(t.schema_id) AS SchemaName,
                t.name AS TableName,
                c.name AS ColumnName,
                ty.name AS TypeName,
                c.is_nullable AS IsNullable,
                ISNULL(dc.definition, N'') AS DefaultDefinition,
                ISNULL(STUFF((SELECT N'; ' + cc.name + N' ' + cc.definition
                    FROM sys.check_constraints cc
                    WHERE cc.parent_object_id = t.object_id
                      AND (cc.parent_column_id = c.column_id OR cc.definition LIKE N'%' + c.name + N'%')
                    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS CheckDefinitions
            FROM sys.tables t
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE SCHEMA_NAME(t.schema_id) NOT IN (N'sys', N'INFORMATION_SCHEMA')
              AND
              (
                  ty.name IN (N'datetimeoffset', N'datetime2', N'datetime', N'smalldatetime', N'date', N'time')
                  OR c.name LIKE N'%Utc'
                  OR c.name LIKE N'%Created%'
                  OR c.name LIKE N'%Updated%'
                  OR c.name LIKE N'%Started%'
                  OR c.name LIKE N'%Completed%'
                  OR c.name LIKE N'%Finished%'
                  OR c.name LIKE N'%Recorded%'
                  OR c.name LIKE N'%Observed%'
                  OR c.name LIKE N'%Applied%'
                  OR c.name LIKE N'%Stored%'
                  OR c.name LIKE N'%Expires%'
                  OR c.name LIKE N'%RolledBack%'
                  OR c.name LIKE N'%Imported%'
                  OR c.name LIKE N'%Indexed%'
              )
            ORDER BY SchemaName, TableName, c.column_id;
            """);

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<TimestampConstraintMetadata>> TimestampDefaultsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<TimestampConstraintMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name, N'.', c.name) AS FullColumnName,
                dc.name AS Name,
                dc.definition AS Definition
            FROM sys.tables t
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            INNER JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE c.name LIKE N'%Utc'
               OR c.name LIKE N'%Created%'
               OR c.name LIKE N'%Updated%'
               OR c.name LIKE N'%Started%'
               OR c.name LIKE N'%Completed%'
               OR c.name LIKE N'%Stored%'
               OR c.name LIKE N'%Expires%'
            ORDER BY FullColumnName, dc.name;
            """);

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<TimestampConstraintMetadata>> TimestampChecksAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<TimestampConstraintMetadata>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(t.schema_id), N'.', t.name) AS FullColumnName,
                cc.name AS Name,
                cc.definition AS Definition
            FROM sys.tables t
            INNER JOIN sys.check_constraints cc ON cc.parent_object_id = t.object_id
            WHERE cc.definition LIKE N'%Utc%'
               OR cc.definition LIKE N'%Created%'
               OR cc.definition LIKE N'%Updated%'
               OR cc.definition LIKE N'%Started%'
               OR cc.definition LIKE N'%Completed%'
               OR cc.definition LIKE N'%Stored%'
               OR cc.definition LIKE N'%Expires%'
            ORDER BY FullColumnName, cc.name;
            """);

        return rows.ToArray();
    }

    private async Task<IReadOnlyList<TimestampProcedurePattern>> TimestampProcedurePatternsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<TimestampProcedurePattern>(
            """
            SELECT
                CONCAT(SCHEMA_NAME(p.schema_id), N'.', p.name) AS ProcedureName,
                CAST(CASE WHEN m.definition LIKE N'%SYSUTCDATETIME%' THEN 1 ELSE 0 END AS bit) AS UsesSysUtcDateTime,
                CAST(CASE WHEN m.definition LIKE N'%GETUTCDATE%' THEN 1 ELSE 0 END AS bit) AS UsesGetUtcDate,
                CAST(CASE WHEN m.definition LIKE N'%GETDATE%' THEN 1 ELSE 0 END AS bit) AS UsesGetDate,
                CAST(CASE WHEN m.definition LIKE N'%CURRENT_TIMESTAMP%' THEN 1 ELSE 0 END AS bit) AS UsesCurrentTimestamp,
                CAST(CASE WHEN m.definition LIKE N'%SYSDATETIMEOFFSET%' THEN 1 ELSE 0 END AS bit) AS UsesSysDateTimeOffset,
                CAST(CASE WHEN m.definition LIKE N'%SYSDATETIME%' AND m.definition NOT LIKE N'%SYSUTCDATETIME%' THEN 1 ELSE 0 END AS bit) AS UsesSysDateTime,
                CAST(CASE WHEN m.definition LIKE N'%@%Utc%' THEN 1 ELSE 0 END AS bit) AS HasUtcNamedParameters,
                CAST(CASE WHEN m.definition LIKE N'%AtUtc%' OR m.definition LIKE N'%CreatedUtc%' OR m.definition LIKE N'%StoredAtUtc%' OR m.definition LIKE N'%ExpiresAtUtc%' OR m.definition LIKE N'%CompletedAtUtc%' OR m.definition LIKE N'%StartedAtUtc%' THEN 1 ELSE 0 END AS bit) AS TouchesUtcFields
            FROM sys.procedures p
            INNER JOIN sys.sql_modules m ON m.object_id = p.object_id
            WHERE m.definition LIKE N'%Utc%'
               OR m.definition LIKE N'%Date%'
               OR m.definition LIKE N'%Time%'
               OR m.definition LIKE N'%Created%'
               OR m.definition LIKE N'%Updated%'
               OR m.definition LIKE N'%Stored%'
            ORDER BY ProcedureName;
            """);

        return rows.ToArray();
    }

    private async Task<MetadataCounts> MetadataCountsAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<MetadataCounts>(
            """
            SELECT
                (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS TableCount,
                (SELECT COUNT(*) FROM sys.tables t INNER JOIN sys.columns c ON c.object_id = t.object_id WHERE t.is_ms_shipped = 0) AS ColumnCount,
                (SELECT COUNT(*) FROM sys.default_constraints) AS DefaultConstraintCount,
                (SELECT COUNT(*) FROM sys.check_constraints) AS CheckConstraintCount,
                (SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped = 0) AS ProcedureCount,
                (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped = 0) AS TriggerCount;
            """);
    }

    private async Task ApplySqlFileAsync(string relativePath)
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

        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var batch in SplitSqlBatches(sql))
            await connection.ExecuteAsync(batch);
    }

    private static string Classify(TimestampColumnMetadata column)
    {
        if (!IsDateTimeType(column.TypeName))
            return "NotApplicable";

        if (HasUtcEnforcingCheck(column.CheckDefinitions))
            return "UtcEnforced";

        if (column.DefaultDefinition.Contains("SYSUTCDATETIME", StringComparison.OrdinalIgnoreCase)
            || column.DefaultDefinition.Contains("GETUTCDATE", StringComparison.OrdinalIgnoreCase))
            return "UtcDefaulted";

        if (column.ColumnName.EndsWith("Utc", StringComparison.Ordinal)
            || column.ColumnName.Contains("AtUtc", StringComparison.Ordinal)
            || string.Equals(column.ColumnName, "TimestampUtc", StringComparison.Ordinal))
            return "UtcParameterOrProcedureDependent";

        if (column.ColumnName is "CreatedDate" or "UpdatedDate" or "LastIndexedDate")
            return "LegacyAssumedUtc";

        return "Ambiguous";
    }

    private static bool IsDateTimeType(string typeName) =>
        typeName is "datetimeoffset" or "datetime2" or "datetime" or "smalldatetime" or "date" or "time";

    private static bool HasUtcEnforcingCheck(string definition) =>
        Regex.IsMatch(definition, @"DATEPART\s*\(\s*(TZOFFSET|tz)", RegexOptions.IgnoreCase)
        || definition.Contains("TODATETIMEOFFSET", StringComparison.OrdinalIgnoreCase);

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

    private sealed record TimestampColumnMetadata
    {
        public required string FullName { get; init; }
        public required string SchemaName { get; init; }
        public required string TableName { get; init; }
        public required string ColumnName { get; init; }
        public required string TypeName { get; init; }
        public required bool IsNullable { get; init; }
        public required string DefaultDefinition { get; init; }
        public required string CheckDefinitions { get; init; }
    }

    private sealed record TimestampConstraintMetadata
    {
        public required string FullColumnName { get; init; }
        public required string Name { get; init; }
        public required string Definition { get; init; }
    }

    private sealed record TimestampProcedurePattern
    {
        public required string ProcedureName { get; init; }
        public required bool UsesSysUtcDateTime { get; init; }
        public required bool UsesGetUtcDate { get; init; }
        public required bool UsesGetDate { get; init; }
        public required bool UsesCurrentTimestamp { get; init; }
        public required bool UsesSysDateTimeOffset { get; init; }
        public required bool UsesSysDateTime { get; init; }
        public required bool HasUtcNamedParameters { get; init; }
        public required bool TouchesUtcFields { get; init; }
    }

    private sealed record MetadataCounts
    {
        public required int TableCount { get; init; }
        public required int ColumnCount { get; init; }
        public required int DefaultConstraintCount { get; init; }
        public required int CheckConstraintCount { get; init; }
        public required int ProcedureCount { get; init; }
        public required int TriggerCount { get; init; }
    }
}

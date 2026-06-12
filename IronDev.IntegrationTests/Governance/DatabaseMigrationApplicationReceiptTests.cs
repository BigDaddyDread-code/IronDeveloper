using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("DatabaseMigrationReceipt")]
public sealed class DatabaseMigrationApplicationReceiptTests : IntegrationTestBase
{
    [TestMethod]
    public void MigrationManifest_ListsCurrentBlockGMigrationsInOrderAndFilesExist()
    {
        var root = RepositoryRoot();
        var manifestPath = Path.Combine(root, "Database", "migrations.json");
        Assert.IsTrue(File.Exists(manifestPath), "Database/migrations.json must exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var migrations = document.RootElement.GetProperty("migrations").EnumerateArray().ToArray();

        Assert.AreEqual(4, migrations.Length);
        Assert.AreEqual("2026-06-block-g-governance-event", migrations[0].GetProperty("id").GetString());
        Assert.AreEqual("Database/migrate_governance_event.sql", migrations[0].GetProperty("path").GetString());
        Assert.AreEqual("2026-06-block-g-tool-request", migrations[1].GetProperty("id").GetString());
        Assert.AreEqual("Database/migrate_tool_request.sql", migrations[1].GetProperty("path").GetString());
        Assert.AreEqual("2026-06-block-g-tool-gate-decision", migrations[2].GetProperty("id").GetString());
        Assert.AreEqual("Database/migrate_tool_gate_decision.sql", migrations[2].GetProperty("path").GetString());
        Assert.AreEqual("2026-06-block-g-approval-decision", migrations[3].GetProperty("id").GetString());
        Assert.AreEqual("Database/migrate_approval_decision.sql", migrations[3].GetProperty("path").GetString());

        foreach (var migration in migrations)
        {
            var relativePath = migration.GetProperty("path").GetString()!;
            Assert.IsTrue(File.Exists(Path.Combine(root, relativePath)), $"Manifest path does not exist: {relativePath}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(migration.GetProperty("description").GetString()));
        }
    }

    [TestMethod]
    public void MigrationScripts_ExposeApplyVerifyParametersAndBatchHandling()
    {
        var root = RepositoryRoot();
        var apply = File.ReadAllText(Path.Combine(root, "Database", "apply-migrations.ps1"));
        var verify = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));

        AssertContainsAll(apply,
            "param(",
            "$Server",
            "$Database",
            "$ConnectionString",
            "$TrustServerCertificate",
            "migrations.json",
            "Split-SqlBatches",
            "(?im)^\\s*GO\\s*$",
            "exit 1");

        AssertContainsAll(verify,
            "param(",
            "$Server",
            "$Database",
            "$ConnectionString",
            "$TrustServerCertificate",
            "governance.GovernanceEvent",
            "governance.ToolRequest",
            "governance.ApprovalDecision",
            "FK_ToolRequest_GovernanceEvent",
            "CK_ApprovalDecision_EvidenceJson_Versioned",
            "CK_GovernanceEvent_PayloadJson_IsJson",
            "CK_GovernanceEvent_PayloadVersion_Positive",
            "CK_ToolRequest_RequestPayloadJson_IsJson",
            "CK_ToolRequest_RequestPayloadVersion_Positive",
            "exit 1");
    }

    [TestMethod]
    public async Task ApplyMigrations_IsIdempotentAndVerifierPassesAgainstConfiguredTestDatabase()
    {
        await DropGovernanceSchemaAsync();

        var applyPath = Path.Combine(RepositoryRoot(), "Database", "apply-migrations.ps1");
        var verifyPath = Path.Combine(RepositoryRoot(), "Database", "verify-migrations.ps1");

        RunPowerShell(applyPath, expectSuccess: true, "-ConnectionString", ConnectionString);
        RunPowerShell(applyPath, expectSuccess: true, "-ConnectionString", ConnectionString);
        RunPowerShell(verifyPath, expectSuccess: true, "-ConnectionString", ConnectionString);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var toolRequestExists = await connection.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL THEN 0 ELSE 1 END");
        Assert.AreEqual(1, toolRequestExists);
    }

    [TestMethod]
    public void VerifyMigrations_FailsWhenRequiredObjectsAreMissing()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        var verifyPath = Path.Combine(RepositoryRoot(), "Database", "verify-migrations.ps1");

        RunPowerShell(verifyPath, expectSuccess: false, "-ConnectionString", builder.ConnectionString);
    }

    [TestMethod]
    public void ApplyMigrations_FailsWhenManifestFileIsMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "irondev-migration-receipt-" + Guid.NewGuid().ToString("N"));
        var tempDatabase = Path.Combine(tempRoot, "Database");
        Directory.CreateDirectory(tempDatabase);
        File.WriteAllText(Path.Combine(tempRoot, "IronDev.slnx"), string.Empty);
        File.Copy(Path.Combine(RepositoryRoot(), "Database", "apply-migrations.ps1"), Path.Combine(tempDatabase, "apply-migrations.ps1"));
        File.WriteAllText(
            Path.Combine(tempDatabase, "migrations.json"),
            "{\"migrations\":[{\"id\":\"missing\",\"path\":\"Database/missing.sql\",\"description\":\"missing\"}]}");

        try
        {
            RunPowerShell(Path.Combine(tempDatabase, "apply-migrations.ps1"), expectSuccess: false, "-ConnectionString", ConnectionString);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void NewGovernanceRuntimePath_DoesNotCreateSchemaAtRuntime()
    {
        var root = RepositoryRoot();
        var runtimeFiles = new[]
        {
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlGovernanceEventStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlToolRequestStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlApprovalDecisionStore.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "SqlToolRequestApiStore.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ToolRequestsV1Controller.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ToolGatesV1Controller.cs")
        };

        foreach (var file in runtimeFiles)
        {
            Assert.IsTrue(File.Exists(file), $"Expected runtime file missing: {file}");
            var text = File.ReadAllText(file);
            AssertNoForbiddenRuntimeDdl(text, file);
        }
    }

    [TestMethod]
    public void ReceiptDocument_SeparatesMigrationProofFromApiSmokeProofAndAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR74A_DATABASE_MIGRATION_APPLICATION_RECEIPT.md"));

        AssertContainsAll(receipt,
            "PR 74a proves the current Block G SQL migration scripts can be applied in order and repeatedly",
            "IronDeveloper_Test",
            "IronDeveloper",
            "This PR does not prove API smoke behaviour against the migrated database. That is PR 74b.",
            "This PR adds no new authority behaviour.",
            "Runtime code may call stored procedures; it must not secretly create the governance/tool-request schema.");
    }

    private static void RunPowerShell(string scriptPath, bool expectSuccess, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = PowerShellExecutable(),
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        if (expectSuccess && process.ExitCode != 0)
            Assert.Fail($"Expected PowerShell script to pass: {scriptPath}\nExit: {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

        if (!expectSuccess && process.ExitCode == 0)
            Assert.Fail($"Expected PowerShell script to fail: {scriptPath}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    private static string PowerShellExecutable() => "powershell";

    private static async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(GetConnectionStringForStaticDrop());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_ApprovalDecision_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_Record;
            IF OBJECT_ID(N'governance.usp_ApprovalDecision_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_GetById;
            IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForSubject;
            IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForSubject;
            IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForProject;
            IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForCorrelation;
            IF OBJECT_ID(N'governance.TR_ApprovalDecision_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ApprovalDecision_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_ApprovalDecision_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ApprovalDecision_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NOT NULL DROP TABLE governance.ApprovalDecision;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_Record;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_GetById;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForToolRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_ListForToolRequest;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_ListForProject;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_ListForCorrelation;
            IF OBJECT_ID(N'governance.TR_ToolGateDecision_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NOT NULL DROP TABLE governance.ToolGateDecision;
            IF OBJECT_ID(N'governance.usp_ToolRequest_Create', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_Create;
            IF OBJECT_ID(N'governance.usp_ToolRequest_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_GetById;
            IF OBJECT_ID(N'governance.usp_ToolRequest_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_ListForProject;
            IF OBJECT_ID(N'governance.usp_ToolRequest_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_ListForCorrelation;
            IF OBJECT_ID(N'governance.AppendGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.AppendGovernanceEvent;
            IF OBJECT_ID(N'governance.GetGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.GetGovernanceEvent;
            IF OBJECT_ID(N'governance.ListGovernanceEventsForProject', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForProject;
            IF OBJECT_ID(N'governance.ListGovernanceEventsForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForCorrelation;
            IF OBJECT_ID(N'governance.ListGovernanceEventsForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForSubject;
            IF OBJECT_ID(N'governance.ListGovernanceEventsCausedBy', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsCausedBy;
            IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NOT NULL DROP TABLE governance.ToolRequest;
            IF OBJECT_ID(N'governance.TR_GovernanceEvent_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NOT NULL DROP TABLE governance.GovernanceEvent;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance') DROP SCHEMA governance;
            """);
    }

    private static string GetConnectionStringForStaticDrop()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json")
            .Build();

        return configuration.GetConnectionString("IronDeveloperDb")
            ?? throw new InvalidOperationException("Missing test connection string.");
    }

    private static void AssertNoForbiddenRuntimeDdl(string text, string file)
    {
        var forbidden = new[]
        {
            "CREATE TABLE",
            "ALTER TABLE",
            "DROP TABLE",
            "CREATE PROCEDURE",
            "ALTER PROCEDURE",
            "CREATE OR ALTER PROCEDURE",
            "CREATE SCHEMA",
            "DROP SCHEMA"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Runtime file must not create schema: {file} contains {token}");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

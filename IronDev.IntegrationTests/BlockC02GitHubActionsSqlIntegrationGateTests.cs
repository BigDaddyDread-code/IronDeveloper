using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC02GitHubActionsSqlIntegrationGateTests
{
    private static readonly string[] SqlLaneClasses =
    [
        "BlockC02SqlServerConnectivitySmokeTests",
        "AcceptedApprovalSqlStoreTests",
        "PolicySatisfactionSqlStoreTests",
        "ApplyDryRunStoreTests",
        "DryRunReceiptStoreTests",
        "PatchArtifactStoreTests",
        "WorkflowTransitionRecordStoreTests",
        "ToolRequestStoreTests"
    ];

    [TestMethod]
    public void BlockC02_SqlIntegrationWorkflow_ExistsAndIsSeparateFromC01()
    {
        Assert.IsTrue(File.Exists(SqlWorkflowPath()));
        Assert.IsTrue(File.Exists(C01WorkflowPath()));

        var workflow = SqlWorkflowText();
        var c01Workflow = File.ReadAllText(C01WorkflowPath());

        StringAssert.Contains(workflow, "name: sql-integration-ci");
        StringAssert.Contains(workflow, "pull_request:");
        StringAssert.Contains(workflow, "branches:");
        StringAssert.Contains(workflow, "- main");
        StringAssert.Contains(workflow, "workflow_dispatch:");

        StringAssert.Contains(c01Workflow, "name: governance-boundary-ci");
        AssertDoesNotContain(c01Workflow, "sql-integration-ci");
    }

    [TestMethod]
    public void BlockC02_SqlIntegrationWorkflow_UsesReadOnlyPermissions()
    {
        var workflow = SqlWorkflowText();

        StringAssert.Contains(workflow, "permissions:");
        StringAssert.Contains(workflow, "contents: read");

        foreach (var marker in new[]
        {
            "write-all",
            "contents: write",
            "pull-requests: write",
            "issues: write",
            "checks: write",
            "statuses: write",
            "deployments: write",
            "packages: write",
            "actions: write",
            "id-token: write"
        })
        {
            AssertDoesNotContain(workflow, marker);
        }
    }

    [TestMethod]
    public void BlockC02_SqlIntegrationWorkflow_UsesIsolatedSqlServerService()
    {
        var workflow = SqlWorkflowText();

        foreach (var marker in new[]
        {
            "services:",
            "sqlserver:",
            "mcr.microsoft.com/mssql/server",
            "ACCEPT_EULA: Y",
            "MSSQL_SA_PASSWORD",
            "1433:1433",
            "IronDev_CI_${{ github.run_id }}_${{ github.run_attempt }}",
            "ConnectionStrings__IronDeveloperDb",
            "TrustServerCertificate=True"
        })
        {
            StringAssert.Contains(workflow, marker);
        }

        foreach (var line in WorkflowCommandLines(workflow))
        {
            foreach (var marker in new[] { "secrets.", "Production", "prod", "Live", "Deploy", "Release" })
                AssertDoesNotContain(line, marker);
        }
    }

    [TestMethod]
    public void BlockC02_SqlIntegrationWorkflow_RestoresBuildsAndRunsSqlGateScript()
    {
        var workflow = SqlWorkflowText();

        StringAssert.Contains(workflow, "actions/checkout@v4");
        StringAssert.Contains(workflow, "actions/setup-dotnet@v4");
        StringAssert.Contains(workflow, "dotnet-version: '10.0.x'");
        StringAssert.Contains(workflow, "dotnet restore IronDev.slnx");
        StringAssert.Contains(workflow, "dotnet build IronDev.slnx --no-restore");
        StringAssert.Contains(workflow, "./Scripts/ci/run-sql-integration-ci.ps1");
    }

    [TestMethod]
    public void BlockC02_SqlIntegrationWorkflow_DoesNotMutateGithubOrReleaseState()
    {
        var workflow = SqlWorkflowText();

        foreach (var line in WorkflowCommandLines(workflow))
        {
            foreach (var marker in new[]
            {
                "git push",
                "git commit",
                "git tag",
                "gh pr",
                "gh issue",
                "gh release",
                "gh api",
                "actions/upload-artifact",
                "create-pull-request",
                "auto-merge",
                "softprops/action-gh-release",
                "dotnet nuget push",
                "kubectl",
                "az deployment",
                "aws deploy",
                "gcloud deploy",
                "deployment",
                "publish"
            })
            {
                AssertDoesNotContain(line, marker);
            }
        }
    }

    [TestMethod]
    public void BlockC02_SqlIntegrationScript_DoesNotRunBroadSweeps()
    {
        var script = SqlScriptText();

        foreach (var marker in new[]
        {
            "FullyQualifiedName~Block\"",
            "FullyQualifiedName~Block'",
            "--filter Block",
            "--filter \"FullyQualifiedName~\"",
            "dotnet test IronDev.slnx"
        })
        {
            AssertDoesNotContain(script, marker);
        }

        foreach (var className in SqlLaneClasses)
            StringAssert.Contains(script, $"FullyQualifiedName~{className}");

        foreach (var line in script.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Where(line => line.Contains("dotnet test", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.IsTrue(line.Contains("$script:Project", StringComparison.Ordinal), line);
        }

        StringAssert.Contains(script, "--filter $Filter");
    }

    [TestMethod]
    public void BlockC02_SqlIntegrationScript_DoesNotUseExternalProvidersOrSecrets()
    {
        var script = SqlScriptText();

        foreach (var marker in new[]
        {
            "OpenAI",
            "OLLAMA",
            "WEAVIATE",
            "ApiKey",
            "secrets.",
            "docker compose",
            "docker run"
        })
        {
            AssertDoesNotContain(script, marker);
        }
    }

    [TestMethod]
    public void BlockC02_SqlSmokeTest_RefusesNonCiDatabaseNames()
    {
        var source = File.ReadAllText(SourcePath());

        StringAssert.Contains(source, "IronDev_CI_");
        StringAssert.Contains(source, "ValidateCiDatabaseNameForTest");
        StringAssert.Contains(source, "InitialCatalog = \"master\"");
        StringAssert.Contains(source, "CREATE DATABASE");
        StringAssert.Contains(source, "Database/local_dev_setup.sql");

        foreach (var databaseName in new[] { "IronDeveloper", "master", "prod", "live", "accept", "IronDeveloper_Test" })
        {
            Assert.ThrowsExactly<InvalidOperationException>(
                () => BlockC02SqlServerConnectivitySmokeTests.ValidateCiDatabaseNameForTest(databaseName),
                databaseName);
        }

        BlockC02SqlServerConnectivitySmokeTests.ValidateCiDatabaseNameForTest("IronDev_CI_123_1");
    }

    [TestMethod]
    public void BlockC02_Receipt_RecordsSqlIntegrationGateBoundary()
    {
        var doc = ReceiptText();

        foreach (var section in new[]
        {
            "## Summary",
            "## Boundary",
            "## Workflow Scope",
            "## SQL Scope",
            "## CI Lane",
            "## Forbidden Mutation Paths",
            "## Validation",
            "## Review Traps",
            "## Killjoy"
        })
        {
            StringAssert.Contains(doc, section);
        }

        foreach (var marker in new[]
        {
            "SQL integration CI reports evidence only.",
            "SQL CI is not approval.",
            "SQL CI is not merge readiness.",
            "SQL CI is not release readiness.",
            "SQL CI is not deployment readiness.",
            "SQL CI is not policy satisfaction.",
            "SQL CI is not execution permission.",
            "The workflow uses read-only repository permissions.",
            "The SQL Server database is ephemeral and CI-scoped.",
            "The database name must start with IronDev_CI_.",
            "The workflow does not mutate source, PRs, issues, labels, releases, deployments, memory, receipts, or workflow state.",
            "The workflow restores packages.",
            "The workflow builds IronDev.slnx.",
            "The workflow runs explicit SQL-backed integration tests.",
            "The workflow does not run a broad test sweep.",
            "The workflow does not require production secrets.",
            "The workflow does not call external AI providers.",
            "No executor, mutation, approval, policy, UI, API, CLI, durable store, generated client, release, or deployment path was added.",
            "A database-backed green check is evidence, not permission."
        })
        {
            StringAssert.Contains(doc, marker);
        }
    }

    private static IEnumerable<string> WorkflowCommandLines(string workflow) =>
        workflow.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line =>
                line.StartsWith("run:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("uses:", StringComparison.OrdinalIgnoreCase));

    private static string SqlWorkflowText() => File.ReadAllText(SqlWorkflowPath());

    private static string SqlScriptText() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Scripts", "ci", "run-sql-integration-ci.ps1"));

    private static string ReceiptText() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "C02_GITHUB_ACTIONS_SQL_INTEGRATION_GATE.md"));

    private static string SqlWorkflowPath() => Path.Combine(FindRepositoryRoot(), ".github", "workflows", "sql-integration-ci.yml");

    private static string C01WorkflowPath() => Path.Combine(FindRepositoryRoot(), ".github", "workflows", "governance-boundary-ci.yml");

    private static string SourcePath() => Path.Combine(FindRepositoryRoot(), "IronDev.IntegrationTests", "BlockC02GitHubActionsSqlIntegrationGateTests.cs");

    private static void AssertDoesNotContain(string text, string marker)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"Unexpected marker '{marker}' in: {text}");
    }

    private static string FindRepositoryRoot()
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

[TestClass]
public sealed class BlockC02SqlServerConnectivitySmokeTests
{
    [TestMethod]
    public async Task BlockC02_SqlServerConnectivitySmoke_CreatesCiDatabaseAndRunsSelect()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__IronDeveloperDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ConnectionStrings__IronDeveloperDb is required for SQL CI smoke.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        ValidateCiDatabaseName(databaseName);

        var masterBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await using var command = new SqlCommand(
                $"""
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    EXEC(N'CREATE DATABASE {QuoteSqlIdentifier(databaseName)}');
                END
                """,
                master);
            command.Parameters.AddWithValue("@databaseName", databaseName);
            await command.ExecuteNonQueryAsync();
        }

        await using (var target = new SqlConnection(connectionString))
        {
            await target.OpenAsync();
            await using var command = new SqlCommand("SELECT 1", target);
            var result = Convert.ToInt32(await command.ExecuteScalarAsync());
            Assert.AreEqual(1, result);
        }

        await ApplyLocalDevSetupAsync(connectionString);
    }

    internal static void ValidateCiDatabaseNameForTest(string? databaseName) => ValidateCiDatabaseName(databaseName);

    private static void ValidateCiDatabaseName(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Database name must start with IronDev_CI_.");

        if (!databaseName.StartsWith("IronDev_CI_", StringComparison.Ordinal))
            throw new InvalidOperationException("Database name must start with IronDev_CI_.");

        foreach (var forbidden in new[] { "IronDeveloper", "master", "prod", "live", "accept" })
        {
            if (databaseName.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Database name must be CI-scoped.");
        }
    }

    private static async Task ApplyLocalDevSetupAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var setupPath = Path.Combine(FindRepositoryRoot(), "Database", "local_dev_setup.sql");
        var setupSql = await File.ReadAllTextAsync(setupPath);
        foreach (var batch in Regex.Split(setupSql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch) || IsProductionDatabaseBatch(batch) || IsSeedDataBatch(batch))
                continue;

            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static bool IsProductionDatabaseBatch(string batch) =>
        batch.Contains("CREATE DATABASE [IronDeveloper]", StringComparison.OrdinalIgnoreCase) ||
        batch.Contains("USE [IronDeveloper]", StringComparison.OrdinalIgnoreCase) ||
        batch.Contains("master.sys.databases", StringComparison.OrdinalIgnoreCase);

    private static bool IsSeedDataBatch(string batch) =>
        batch.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase);

    private static string QuoteSqlIdentifier(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string FindRepositoryRoot()
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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApplyDryRunStore")]
[TestCategory("ApplyDryRunStaticBoundary")]
public sealed class ApplyDryRunStoreStaticBoundaryTests
{
    [TestMethod]
    public void ApplyDryRunProductionFiles_DoNotWireRuntimeApiCliOrExecutionCapabilities()
    {
        var root = RepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(root, "IronDev.Core", "Workflow", "ApplyDryRunStoreModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "SqlApplyDryRunStore.cs"),
            Path.Combine(root, "Database", "migrate_apply_dry_run_store.sql")
        };

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            AssertNoForbiddenTokens(
                text,
                "ControllerBase",
                "WebApplication",
                "IHostedService",
                "BackgroundService",
                "ProcessStartInfo",
                "Process.Start",
                "HttpClient",
                "File.ReadAllText",
                "File.WriteAllText",
                "File.Copy",
                "File.Delete",
                "Directory.CreateDirectory",
                "IAgentToolExecutor",
                "IWorkflowRunner",
                "WorkflowOrchestrator",
                "LangGraphRuntime",
                "QueueClient",
                "MessageBus",
                "SourceApplyExecutor",
                "MemoryPromotionExecutor",
                "GitHubReviewSubmission");
        }
    }

    [TestMethod]
    public void ApplyDryRunRuntimeStore_UsesStoredProceduresOnlyAndNoInlineMutableSql()
    {
        var root = RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Workflow", "SqlApplyDryRunStore.cs"));

        Assert.IsTrue(text.Contains("CommandType.StoredProcedure", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("workflow.usp_ApplyDryRun_Create", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("workflow.usp_ApplyDryRun_Get", StringComparison.Ordinal));
        AssertNoForbiddenTokens(
            text,
            "INSERT INTO workflow.ApplyDryRunRecord",
            "UPDATE workflow.ApplyDryRunRecord",
            "DELETE FROM workflow.ApplyDryRunRecord",
            "CREATE TABLE",
            "ALTER TABLE",
            "DROP TABLE");
    }

    [TestMethod]
    public void ApplyDryRunMigration_HasAppendOnlyRuntimeRoleAndNoExecutorVocabulary()
    {
        var root = RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "Database", "migrate_apply_dry_run_store.sql"));

        Assert.IsTrue(text.Contains("workflow.ApplyDryRunRecord", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("workflow.TR_ApplyDryRunRecord_BlockUpdateDelete", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.ApplyDryRunRecord", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("workflow.usp_ApplyDryRun_Create", StringComparison.Ordinal));
        AssertNoForbiddenTokens(
            text,
            "usp_ApplyDryRun_Execute",
            "usp_ApplyDryRun_Run",
            "usp_ApplyDryRun_Apply",
            "usp_ApplyDryRun_Rollback",
            "SourceApplyExecutor",
            "PatchApplyExecutor",
            "ApprovalSatisfiedEvent",
            "MemoryPromotionExecutor");
    }

    [TestMethod]
    public void ApplyDryRunStore_IsNotRegisteredInCliRuntimeOrWorkflowRunner()
    {
        var root = RepositoryRoot();
        foreach (var path in new[]
                 {
                     Path.Combine(root, "IronDev.Cli"),
                     Path.Combine(root, "IronDev.Core", "Agents"),
                     Path.Combine(root, "IronDev.Infrastructure", "Agents")
                 })
        {
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                AssertNoForbiddenTokens(text, "IApplyDryRunStore", "SqlApplyDryRunStore");
            }
        }
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

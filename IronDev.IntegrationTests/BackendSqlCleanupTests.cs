using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendSqlCleanupTests
{
    [TestMethod]
    public void BackendSqlInventory_DocumentsSqlArtifactsAndBoundaries()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_SQL_INVENTORY.md");

        StringAssert.Contains(inventory, "PR 50 is SQL contract cleanup, not schema evolution.");
        StringAssert.Contains(inventory, "No behavior change intended.");
        StringAssert.Contains(inventory, "No stored procedure result-shape change.");
        StringAssert.Contains(inventory, "No SQL/API/CLI/UI/runtime/persistence/capability changes.");
        StringAssert.Contains(inventory, "## Schemas");
        StringAssert.Contains(inventory, "## Active `dbo` tables");
        StringAssert.Contains(inventory, "## Active agent memory and audit tables");
        StringAssert.Contains(inventory, "## Views");
        StringAssert.Contains(inventory, "## Stored procedures");
        StringAssert.Contains(inventory, "## Functions and TVPs/types");
        StringAssert.Contains(inventory, "## Seed, setup, and migration scripts");
        StringAssert.Contains(inventory, "## Test reset/support SQL");
        StringAssert.Contains(inventory, "`dbo.ProjectObservableStates`");
        StringAssert.Contains(inventory, "`agent.usp_AgentLocalMemory_Create`");
        StringAssert.Contains(inventory, "`toolaudit.AppendToolExecutionAuditRecord`");
        StringAssert.Contains(inventory, "No SQL artifacts removed.");
        StringAssert.Contains(inventory, "Vector/index/retrieval remains retrieval only.");
        StringAssert.Contains(inventory, "Audit remains distinct from approval.");
        StringAssert.Contains(inventory, "Proposal remains distinct from apply.");
    }

    [TestMethod]
    public void IntegrationTestReset_DeletesObservableStateBeforeProjectsAndTenants()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "IntegrationTestBase.cs");
        var observableDelete = source.IndexOf("DELETE FROM dbo.ProjectObservableStates", StringComparison.Ordinal);
        var projectDelete = source.IndexOf("DELETE FROM dbo.Projects", StringComparison.Ordinal);
        var tenantDelete = source.IndexOf("DELETE FROM dbo.Tenants", StringComparison.Ordinal);

        Assert.IsTrue(observableDelete >= 0, "Reset must delete ProjectObservableStates.");
        Assert.IsTrue(projectDelete >= 0, "Reset must delete Projects.");
        Assert.IsTrue(tenantDelete >= 0, "Reset must delete Tenants.");
        Assert.IsTrue(observableDelete < projectDelete, "ProjectObservableStates must be deleted before Projects because it has a ProjectId FK.");
        Assert.IsTrue(observableDelete < tenantDelete, "ProjectObservableStates must be deleted before Tenants because it has a TenantId FK.");
    }

    [TestMethod]
    public void SqlCleanup_DoesNotWeakenForeignKeysOrChangeStoredProcedureShapes()
    {
        var changedFiles = new[]
        {
            ReadRepositoryFile("IronDev.IntegrationTests", "IntegrationTestBase.cs"),
            ReadRepositoryFile("Docs", "BACKEND_SQL_INVENTORY.md")
        };

        var forbidden = new[]
        {
            "DROP CONSTRAINT",
            "NOCHECK CONSTRAINT",
            "DISABLE TRIGGER",
            "DROP FOREIGN KEY",
            "ALTER PROCEDURE",
            "CREATE OR ALTER PROCEDURE"
        };

        foreach (var source in changedFiles)
        {
            foreach (var token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), $"SQL cleanup changed or described a forbidden active SQL shape: {token}");
        }
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}

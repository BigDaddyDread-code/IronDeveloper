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
    public void InlineSqlInventory_DocumentsRuntimeDdlAndIntentionalExceptions()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_INLINE_SQL_INVENTORY.md");

        StringAssert.Contains(inventory, "PR 51 is SQL hygiene and fixture cleanup, not schema evolution.");
        StringAssert.Contains(inventory, "No behavior change intended.");
        StringAssert.Contains(inventory, "No schema semantics change.");
        StringAssert.Contains(inventory, "No stored procedure result-shape change.");
        StringAssert.Contains(inventory, "No SQL/API/CLI/UI/runtime/persistence/capability changes.");
        StringAssert.Contains(inventory, "Runtime DDL candidates");
        StringAssert.Contains(inventory, "Test-only inline SQL candidates");
        StringAssert.Contains(inventory, "AgentMemoryIndexEvent cleanup fix");
        StringAssert.Contains(inventory, "IronDev.Infrastructure/Services/Runs/SqlRunStore.cs");
        StringAssert.Contains(inventory, "IronDev.Infrastructure/Services/RunReports/SqlRunEventStore.cs");
        StringAssert.Contains(inventory, "IronDev.Infrastructure/Services/TicketService.cs");
        StringAssert.Contains(inventory, "IronDev.Infrastructure/Services/SemanticMemory/WeaviateSemanticMemoryService.cs");
        StringAssert.Contains(inventory, "Left intentionally");
        StringAssert.Contains(inventory, "explicit cleanup debt");
    }

    [TestMethod]
    public void AgentMemorySchemaSupport_DropsIndexEventsBeforeAgentSchema()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "AgentMemory", "AgentMemorySchemaTestSupport.cs");
        var indexEventDrop = source.IndexOf("DROP TABLE agent.AgentMemoryIndexEvent", StringComparison.Ordinal);
        var indexQueueDrop = source.IndexOf("DROP TABLE agent.AgentMemoryIndexQueue", StringComparison.Ordinal);
        var proposalDrop = source.IndexOf("DROP TABLE agent.AgentMemoryImprovementProposal", StringComparison.Ordinal);
        var schemaDrop = source.IndexOf("DROP SCHEMA agent", StringComparison.Ordinal);

        Assert.IsTrue(indexEventDrop >= 0, "Agent memory schema cleanup must drop AgentMemoryIndexEvent.");
        Assert.IsTrue(indexQueueDrop >= 0, "Agent memory schema cleanup must drop AgentMemoryIndexQueue.");
        Assert.IsTrue(proposalDrop >= 0, "Agent memory schema cleanup must still drop memory improvement proposal tables.");
        Assert.IsTrue(schemaDrop >= 0, "Agent memory schema cleanup must still drop the agent schema after objects.");
        Assert.IsTrue(indexEventDrop < indexQueueDrop, "Index events must be dropped before their index queue parent table.");
        Assert.IsTrue(indexEventDrop < schemaDrop, "Index events must be dropped before the agent schema.");
        Assert.IsTrue(indexQueueDrop < schemaDrop, "Index queue must be dropped before the agent schema.");
        Assert.IsTrue(proposalDrop < schemaDrop, "Proposal tables must be dropped before the agent schema.");
        Assert.IsFalse(source.Contains("NOCHECK CONSTRAINT", StringComparison.OrdinalIgnoreCase), "Cleanup must not weaken FK constraints.");
        Assert.IsFalse(source.Contains("DISABLE TRIGGER", StringComparison.OrdinalIgnoreCase), "Cleanup must not disable triggers to pass tests.");
    }

    [TestMethod]
    public void MemoryImprovementProposalTests_UseNamedAgentMemorySchemaSupport()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "AgentMemory", "MemoryImprovementProposalTests.cs");

        StringAssert.Contains(source, "AgentMemorySchemaTestSupport.ApplyCoreAgentMemoryMigrationsAsync");
        StringAssert.Contains(source, "AgentMemorySchemaTestSupport.DropAgentMemorySchemaInDependencyOrderAsync");
        Assert.IsFalse(source.Contains("DROP SCHEMA agent", StringComparison.OrdinalIgnoreCase),
            "MemoryImprovementProposalTests should use the named dependency-order helper instead of carrying local schema teardown SQL.");
    }

    [TestMethod]
    public void AgentMemoryRuntimeStores_DoNotExecuteSchemaDdl()
    {
        var root = RepositoryRoot();
        var agentMemoryFiles = Directory.GetFiles(Path.Combine(root, "IronDev.Infrastructure", "AgentMemory"), "*.cs", SearchOption.AllDirectories);
        var forbidden = new[]
        {
            "CREATE SCHEMA",
            "CREATE TABLE",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "DROP SCHEMA",
            "DROP PROCEDURE"
        };

        foreach (var file in agentMemoryFiles)
        {
            var source = File.ReadAllText(file);
            foreach (var token in forbidden)
                Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), $"Agent memory runtime store must not execute schema DDL: {Path.GetFileName(file)} contains {token}.");
        }
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

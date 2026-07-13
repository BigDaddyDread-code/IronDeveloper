namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class MemoryIndexLifecycleContractTests
{
    private static readonly string[] Events =
    [
        "SourceCreated", "SourceUpdated", "EmbeddingQueued", "EmbeddingCompleted", "StaleDetected",
        "ReindexRequested", "ReindexCompleted", "SourceArchived", "DerivedIndexDeleted", "DerivedIndexRebuilt"
    ];

    [TestMethod]
    public void LifecycleMigration_IsManifestOwnedAndInventoried()
    {
        StringAssert.Contains(Read("Database", "migrations.json"), "Database/migrate_memory_index_lifecycle.sql");
        StringAssert.Contains(Read("Database", "sql-inventory.json"), "database.migrate-memory-index-lifecycle");
    }

    [TestMethod]
    public void Lifecycle_ContainsEveryRequiredEventAndValidatedTransition()
    {
        var sql = Migration();
        foreach (var eventType in Events) StringAssert.Contains(sql, $"N'{eventType}'");
        StringAssert.Contains(sql, "Invalid memory index lifecycle transition.");
        StringAssert.Contains(sql, "UPDLOCK, HOLDLOCK");
        StringAssert.Contains(sql, "TR_MemoryIndexLifecycleEvents_BlockUpdateDelete");
    }

    [TestMethod]
    public void Lifecycle_PreservesSqlAuthorityAndRebuildBoundary()
    {
        var contract = Read("Docs", "memory", "MEMORY_INDEX_LIFECYCLE.md");
        StringAssert.Contains(contract, "SQL source records and lifecycle events are authoritative");
        StringAssert.Contains(contract, "derived and rebuildable");
        StringAssert.Contains(contract, "Deleting a vector does not archive the source");
    }

    [TestMethod]
    public void Verifier_RequiresLedgerProcedureViewAndIndex()
    {
        var verifier = Read("Database", "verify-migrations.ps1");
        foreach (var value in new[]
        {
            "dbo.MemoryIndexLifecycleEvents", "dbo.usp_MemoryIndexLifecycleEvent_Record",
            "dbo.vw_CurrentMemoryIndexLifecycle", "IX_MemoryIndexLifecycleEvents_Source"
        }) StringAssert.Contains(verifier, value);
    }

    private static string Migration() => Read("Database", "migrate_memory_index_lifecycle.sql");
    private static string Read(params string[] parts) => File.ReadAllText(parts.Aggregate(Root(), Path.Combine));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

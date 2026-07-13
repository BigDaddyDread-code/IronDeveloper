namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class ProjectCanonMemoryLifecycleTests
{
    [TestMethod]
    public void LifecycleMigration_IsManifestOwnedAndInventoried()
    {
        var manifest = Read("Database", "migrations.json");
        var schemaOwner = "Database/migrate_memory_proposal_staging.sql";
        var lifecycle = "Database/migrate_project_canon_memory_lifecycle.sql";
        StringAssert.Contains(manifest, schemaOwner);
        StringAssert.Contains(manifest, lifecycle);
        Assert.IsTrue(manifest.IndexOf(schemaOwner, StringComparison.Ordinal) < manifest.IndexOf(lifecycle, StringComparison.Ordinal));
        StringAssert.Contains(Read("Database", "migrate_memory_proposal_staging.sql"), "CREATE SCHEMA memory");
        Assert.IsFalse(Migration().Contains("CREATE SCHEMA memory", StringComparison.OrdinalIgnoreCase));
        var inventory = Read("Database", "sql-inventory.json");
        StringAssert.Contains(inventory, "database.migrate-project-canon-memory-lifecycle");
        StringAssert.Contains(inventory, "\"appliedByManifest\":  true");
        StringAssert.Contains(inventory, "\"verifiedByScript\":  true");
    }

    [TestMethod]
    public void VersionSchema_ContainsEveryRequiredLifecycleField()
    {
        var sql = Migration();
        foreach (var field in new[]
        {
            "StableMemoryId", "VersionId", "ContentHash", "Status", "CreatedByUserId",
            "CreatedAtUtc", "SourceEvidence", "SupersedesVersionId", "EffectiveFromUtc",
            "RetiredAtUtc", "PromotionReceiptId"
        }) StringAssert.Contains(sql, field);
    }

    [TestMethod]
    public void Lifecycle_IsAppendOnlyAndCurrentTruthExcludesSuccessors()
    {
        var sql = Migration();
        StringAssert.Contains(sql, "TR_ProjectCanonMemory_BlockUpdateDelete");
        StringAssert.Contains(sql, "TR_ProjectCanonMemoryVersion_BlockUpdateDelete");
        StringAssert.Contains(sql, "INSTEAD OF UPDATE, DELETE");
        StringAssert.Contains(sql, "successor.SupersedesVersionId = v.VersionId");
        StringAssert.Contains(sql, "FOREIGN KEY (SupersedesVersionId, StableMemoryId, TenantId, ProjectId)");
        StringAssert.Contains(sql, "BEGIN TRANSACTION");
        StringAssert.Contains(sql, "WITH (UPDLOCK, HOLDLOCK)");
        StringAssert.Contains(sql, "The first version must be Current");
        StringAssert.Contains(sql, "The supplied predecessor is not the single current leaf");
        StringAssert.Contains(sql, "usp_ProjectCanonMemory_ListHistory");
        Assert.IsFalse(sql.Contains("WHERE Title =", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Verifier_RequiresLifecycleObjects()
    {
        var verifier = Read("Database", "verify-migrations.ps1");
        foreach (var value in new[]
        {
            "memory.ProjectCanonMemory", "memory.ProjectCanonMemoryVersion",
            "memory.vw_CurrentProjectCanonMemory", "CK_ProjectCanonMemoryVersion_Lifecycle",
            "FK_ProjectCanonMemoryVersion_SupersedesScope",
            "memory.usp_ProjectCanonMemory_GetCurrent", "memory.usp_ProjectCanonMemory_ListHistory"
        }) StringAssert.Contains(verifier, value);
    }

    private static string Migration() => Read("Database", "migrate_project_canon_memory_lifecycle.sql");
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

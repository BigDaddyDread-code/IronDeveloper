namespace IronDev.IntegrationTests.Database;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class ConstraintIndexAuditContractTests
{
    [TestMethod]
    public void ConstraintAuditMigration_IsManifestOwnedAndInventoried()
    {
        var root = RepositoryRoot();
        var manifest = Read(root, "Database", "migrations.json");
        var inventory = Read(root, "Database", "sql-inventory.json");

        StringAssert.Contains(manifest, "Database/migrate_constraint_index_audit.sql");
        StringAssert.Contains(inventory, "database.migrate-constraint-index-audit");
        StringAssert.Contains(inventory, "\"appliedByManifest\":  true");
        StringAssert.Contains(inventory, "\"verifiedByScript\":  true");
    }

    [TestMethod]
    public void ConstraintAuditMigration_ContainsRequiredProtections()
    {
        var root = RepositoryRoot();
        var migration = Read(root, "Database", "migrate_constraint_index_audit.sql");
        var verifier = Read(root, "Database", "verify-migrations.ps1");
        foreach (var name in new[]
        {
            "FK_ProjectProfiles_ProjectScope",
            "FK_ProjectCommands_ProjectScope",
            "FK_Runs_ProjectTickets",
            "CK_Runs_TicketRequiresProject",
            "FK_ProjectTickets_SourceChatMessage",
            "FK_ProjectTickets_SourceDocumentVersion",
            "FK_SemanticArtefacts_ProjectScope",
            "FK_EmbeddingJobs_ProjectScope",
            "IX_Runs_State_UpdatedUtc",
            "IX_SemanticSearchTraces_Project_CreatedUtc",
            "IX_WorkItemContracts_Supersedes",
            "FK_AgentHandoff_Supersedes"
        })
        {
            StringAssert.Contains(migration, name);
            StringAssert.Contains(verifier, name);
        }

        StringAssert.Contains(migration, "INSERT INTO dbo.DecisionCategories");
        StringAssert.Contains(migration, "INSERT INTO dbo.DecisionStatuses");
        StringAssert.Contains(migration, "WHERE NOT EXISTS");
    }

    [TestMethod]
    public void Audit_NamesDeferredP2DebtWithoutClaimingAuthority()
    {
        var root = RepositoryRoot();
        var audit = Read(root, "Docs", "cleanup", "DATABASE_CONSTRAINT_INDEX_AUDIT.md");
        foreach (var heading in new[] { "## Foreign Keys", "## Check Constraints", "## Indexes", "## Default Rows", "## Audit Indexes", "## Deferred P2 Items" })
            StringAssert.Contains(audit, heading);

        StringAssert.Contains(audit, "SemanticEmbeddings.SourceDocumentVersionId");
        StringAssert.Contains(audit, "P2 contract debt");
        StringAssert.Contains(audit, "no known P0 or P1");
        StringAssert.Contains(audit, "does not make the indexed data authoritative");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(parts.Aggregate(root, Path.Combine));

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
}

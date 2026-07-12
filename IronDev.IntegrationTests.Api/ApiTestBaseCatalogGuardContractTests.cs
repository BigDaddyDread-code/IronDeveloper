using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// HERO-2 destructive-catalog guard contract. API test provisioning drops and
/// resets database objects; it may only ever target an explicitly test-shaped
/// catalog. The guard must be strict AND compatible — local `*_Test` databases
/// and ephemeral `IronDev_CI_*` databases pass; real and local developer
/// catalogs are refused. A safety guard that breaks CI gets bypassed.
/// </summary>
[TestClass]
[TestCategory("Contract")]
[TestCategory("Boundary")]
[TestCategory("ReleaseReadiness")]
public sealed class ApiTestBaseCatalogGuardContractTests
{
    [TestMethod]
    public void ApiTestBase_AllowsLocalTestCatalog()
    {
        Assert.IsTrue(ApiTestBase.IsTestShapedCatalog("IronDeveloper_Test"));
        Assert.IsTrue(ApiTestBase.IsTestShapedCatalog("irondeveloper_test"), "Catalog shape check is case-insensitive.");
    }

    [TestMethod]
    public void ApiTestBase_AllowsCiEphemeralCatalog()
    {
        Assert.IsTrue(ApiTestBase.IsTestShapedCatalog("IronDev_CI_28783854607_1"));
        Assert.IsTrue(ApiTestBase.IsTestShapedCatalog("irondev_ci_1"), "Catalog shape check is case-insensitive.");
    }

    [TestMethod]
    public void ApiTestBase_RejectsProductionCatalog()
    {
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog("IronDeveloper"),
            "The real IronDeveloper catalog must never receive destructive test provisioning.");
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog(""), "An empty catalog is refused, never defaulted.");
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog(null), "A null catalog is refused, never defaulted.");
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog("   "), "A whitespace catalog is refused.");
    }

    [TestMethod]
    public void ApiTestBase_RejectsLocalDeveloperCatalog()
    {
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog("IronDeveloper_Local"),
            "The local developer catalog must never receive destructive test provisioning.");
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog("IronDeveloper_Local_Backup"));
        Assert.IsFalse(ApiTestBase.IsTestShapedCatalog("IronDev_Testing"),
            "'_Test' must be a suffix, not a substring — 'IronDev_Testing' is not test-shaped.");
    }

    [TestMethod]
    public void ApiTestBase_RefusesMigrationsThatChooseTheirOwnCatalog()
    {
        // The root cause of the catalog escape: a migration with a USE statement
        // silently rides the provisioning connection onto another database.
        Assert.ThrowsException<InvalidOperationException>(() =>
            ApiTestBase.AssertNoCatalogHijack("USE [IronDeveloper];\nGO\nSELECT 1;", "evil.sql"));
        Assert.ThrowsException<InvalidOperationException>(() =>
            ApiTestBase.AssertNoCatalogHijack("  use master\nSELECT 1;", "evil2.sql"));

        // Benign content passes, including the word USE inside comments/identifiers.
        ApiTestBase.AssertNoCatalogHijack("SELECT 1; -- do not USE this pattern", "fine.sql");
        ApiTestBase.AssertNoCatalogHijack("CREATE TABLE dbo.UserSettings (Id INT);", "fine2.sql");

        // And every migration the test host actually applies must be catalog-agnostic.
        foreach (var file in new[]
                 {
                     "migrate_project_profiles.sql",
                     "migrate_code_indexing.sql",
                     "migrate_projects_indexing_fields.sql",
                     "migrate_agent_run_audit_envelope.sql",
                     "migrate_user_mutation_attribution.sql"
                 })
        {
            var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", file));
            ApiTestBase.AssertNoCatalogHijack(sql, file);
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev.slnx.");
    }
}

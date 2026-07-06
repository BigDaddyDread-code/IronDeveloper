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
}

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class PlatformBaselineContractTests
{
    [TestMethod]
    public void PlatformBaseline_HasOneIsolatedLocalValidationPath()
    {
        var script = Read("Scripts", "ci", "run-platform-baseline-ci.ps1");

        StringAssert.Contains(script, "Database\\verify-clean-database.ps1");
        StringAssert.Contains(script, "EndpointContractTests");
        StringAssert.Contains(script, "ApiTestBaseCatalogGuardContractTests");
        StringAssert.Contains(script, "--artifacts-path $artifactRoot");
        StringAssert.Contains(script, "run-frontend-contract-ci.ps1");
        StringAssert.Contains(script, "Remove-TestDatabase");
        StringAssert.Contains(script, "Refusing to remove non-test database");
    }

    [TestMethod]
    public void CleanMigrationVerifier_RebuildsAppliesTwiceVerifiesAndRemovesOnlyTestDatabase()
    {
        var script = Read("Database", "verify-clean-database.ps1");

        StringAssert.Contains(script, "Database\\rebuild_db.sql");
        StringAssert.Contains(script, "@(\"apply-migrations.ps1\", \"apply-migrations.ps1\", \"verify-migrations.ps1\")");
        StringAssert.Contains(script, "The database must end in '_Test' or start with 'IronDev_CI_'");
        StringAssert.Contains(script, "SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        StringAssert.Contains(script, "PASS clean database migration verification");

        var fullSql = Read("Scripts", "ci", "run-full-sql-integration-ci.ps1");
        StringAssert.Contains(fullSql, "Clean database migration verification");
        StringAssert.Contains(fullSql, "Apply migrations to SQL test catalog");
        AssertOrder(fullSql, "Database\\apply-migrations.ps1", "In-process API contract");
        StringAssert.Contains(fullSql, "In-process API contract");
    }

    [TestMethod]
    public void ApiTestReset_DeletesCurrentProductRowsInForeignKeyOrder()
    {
        var source = Read("IronDev.IntegrationTests.Api", "ApiTestBase.cs");

        AssertOrder(source, "DELETE FROM dbo.ChatTurnTraces", "DELETE FROM dbo.ChatMessages");
        AssertOrder(source, "DELETE FROM dbo.ProjectDocumentLinks", "DELETE FROM dbo.ProjectDocumentVersions");
        AssertOrder(source, "DELETE FROM dbo.ProjectDocumentVersions", "DELETE FROM dbo.ProjectDocuments");
        AssertOrder(source, "DELETE FROM dbo.ProjectChannelMembers", "DELETE FROM dbo.ProjectChannels");
        AssertOrder(source, "DELETE FROM dbo.ProjectChannels", "DELETE FROM dbo.Projects");
        AssertOrder(source, "DELETE FROM dbo.Projects", "DELETE FROM dbo.TenantUsers");
        AssertOrder(source, "DELETE FROM dbo.TenantUsers", "DELETE FROM dbo.Users WHERE Id <> 1");
        StringAssert.Contains(source, "DELETE FROM dbo.SemanticSearchTraces");
        StringAssert.Contains(source, "DELETE FROM dbo.ProjectProfileOptions");
    }

    [TestMethod]
    public void ManualLauncher_UsesNeutralNameAndPreservesEstablishedImplementation()
    {
        var launcher = Read("tools", "localtest", "start-pr-manual-test.ps1");
        StringAssert.Contains(launcher, "start-alpha-localtest.ps1");
        StringAssert.Contains(launcher, "-FreshSession");
        StringAssert.Contains(launcher, "-BrowserOnly");
        StringAssert.Contains(launcher, "Get-Process -Id $PID");

        var preflight = Read("IronDev.TauriShell", "src", "flow", "start", "PreflightGate.tsx");
        StringAssert.Contains(preflight, "start-pr-manual-test.ps1");
        Assert.IsFalse(preflight.Contains("start-v0.1-demo.ps1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FrontendContract_UsesCleanInstallInCiWithoutDeletingLiveLocalDependencies()
    {
        var script = Read("Scripts", "ci", "run-frontend-contract-ci.ps1");
        StringAssert.Contains(script, "$env:CI -eq \"true\"");
        StringAssert.Contains(script, "npm ci");
        StringAssert.Contains(script, "npm install --no-audit --no-fund");
    }

    private static void AssertOrder(string source, string child, string parent)
    {
        var childIndex = source.IndexOf(child, StringComparison.Ordinal);
        var parentIndex = source.IndexOf(parent, StringComparison.Ordinal);
        Assert.IsTrue(childIndex >= 0, $"Missing cleanup token: {child}");
        Assert.IsTrue(parentIndex >= 0, $"Missing cleanup token: {parent}");
        Assert.IsTrue(childIndex < parentIndex, $"{child} must run before {parent}.");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(parts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

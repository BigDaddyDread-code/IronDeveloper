using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("Contract")]
public sealed class ProjectCollaborationContractTests
{
    [TestMethod]
    public void MigrationAndManifestDefineProjectVisibilityAndWorkItemOwnership()
    {
        var migration = File.ReadAllText(RepoFile("Database", "migrate_project_collaboration.sql"));
        var manifest = File.ReadAllText(RepoFile("Database", "migrations.json"));

        StringAssert.Contains(manifest, "2026-07-v2-project-collaboration");
        StringAssert.Contains(migration, "CREATE TABLE dbo.ProjectMembers");
        StringAssert.Contains(migration, "CREATE TABLE dbo.ProjectWorkItemCollaboration");
        StringAssert.Contains(migration, "CREATE TABLE dbo.ProjectWorkItemFollowers");
        StringAssert.Contains(migration, "CREATE TABLE dbo.ProjectWorkItemActivity");
        StringAssert.Contains(migration, "CK_ProjectMembers_Role");
        StringAssert.Contains(migration, "ProjectRole IN (N'Owner', N'Contributor', N'Viewer')");
    }

    [TestMethod]
    public void ApiEnforcesProjectMembershipBeforeControllerDispatch()
    {
        var program = File.ReadAllText(RepoFile("IronDev.Api", "Program.cs"));
        var middleware = File.ReadAllText(RepoFile("IronDev.Api", "Middleware", "ProjectMembershipMiddleware.cs"));

        StringAssert.Contains(program, "UseMiddleware<ProjectMembershipMiddleware>()");
        StringAssert.Contains(middleware, "HasAccessAsync");
        StringAssert.Contains(middleware, "StatusCodes.Status404NotFound");
        StringAssert.Contains(middleware, "you no longer have access");
    }

    private static string RepoFile(params string[] parts)
    {
        var root = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine([root, .. parts]);
    }
}

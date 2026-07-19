namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class WorkbenchAgentRunStaticBoundaryTests
{
    [TestMethod]
    public void WorkerAndContextAssembler_DoNotDependOnRequestOrRepositoryContext()
    {
        var root = RepositoryRoot();
        var pipeline = File.ReadAllText(Path.Combine(
            root,
            "IronDev.Infrastructure",
            "Services",
            "WorkbenchAgentRunPipeline.cs"));

        foreach (var forbidden in new[]
                 {
                     "HttpContext",
                     "IHttpContextAccessor",
                     "ICurrentTenantContext",
                     "System.Security.Claims",
                     "RepositoryBinding",
                     "BranchName",
                     "CodeIndex",
                     "ProjectChatResponseService"
                 })
            Assert.IsFalse(
                pipeline.Contains(forbidden, StringComparison.Ordinal),
                $"The durable Workbench agent pipeline must not depend on {forbidden}.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

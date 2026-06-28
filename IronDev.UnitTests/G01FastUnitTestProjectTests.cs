namespace IronDev.UnitTests;

[TestClass]
public sealed class G01FastUnitTestProjectTests
{
    [TestMethod]
    public void FastUnitTestProjectIsDiscoverable()
    {
        Assert.AreEqual("IronDev.UnitTests", typeof(G01FastUnitTestProjectTests).Assembly.GetName().Name);
    }

    [TestMethod]
    public void FastUnitTestProjectCanReferenceCore()
    {
        var catalog = new RoleCatalogService().BuildDefaultCatalog();

        Assert.AreEqual(RoleCatalogService.DefaultCatalogId, catalog.CatalogId);
        Assert.IsTrue(catalog.Entries.Any(static entry => entry.RoleKind == GovernanceRoleKind.SystemAccountabilityOwner));
    }

    [TestMethod]
    public void FastUnitTestProjectDoesNotReferenceApiCliPersistenceOrIntegrationTestProjects()
    {
        var project = LoadProject();
        var references = ProjectReferences(project).ToArray();

        CollectionAssert.AreEqual(
            new[] { @"..\IronDev.Core\IronDev.Core.csproj" },
            references);

        AssertProjectTextDoesNotContain(
            "IronDev.Api",
            "IronDev.Cli",
            "IronDev.IntegrationTests",
            "IronDev.Infrastructure",
            "Persistence",
            "Workers",
            "SQL",
            "GitHub",
            "Provider");
    }

    [TestMethod]
    public void FastUnitTestProjectDoesNotContainIntegrationOnlyDependencies()
    {
        var packages = PackageReferences(LoadProject()).ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Microsoft.NET.Test.Sdk",
                "MSTest.TestAdapter",
                "MSTest.TestFramework"
            },
            packages);

        AssertProjectTextDoesNotContain(
            "SqlConnection",
            "DbContext",
            "WebApplicationFactory",
            "TestServer",
            "HttpClient",
            "Docker",
            "Testcontainers",
            "HostedService");
    }

    [TestMethod]
    public void FastUnitTestProjectHasNoDatabaseProviderNetworkHostOrEnvironmentDependencies()
    {
        var projectText = ProjectText();

        Assert.IsFalse(projectText.Contains("FrameworkReference", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projectText.Contains("appsettings", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projectText.Contains("UserSecrets", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projectText.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(projectText.Contains("CopyToOutputDirectory", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FastUnitTestProjectNameMakesFastLaneExplicit()
    {
        var projectPath = ProjectPath();

        StringAssert.Contains(projectPath, "IronDev.UnitTests");
        Assert.IsTrue(File.Exists(projectPath));
    }

    [TestMethod]
    public void FastUnitTestProjectIsRegisteredInSolution()
    {
        var solution = File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.slnx"));

        StringAssert.Contains(solution, "IronDev.UnitTests/IronDev.UnitTests.csproj");
    }

    private static XDocument LoadProject() => XDocument.Load(ProjectPath());

    private static IEnumerable<string> ProjectReferences(XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .OrderBy(static include => include, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> PackageReferences(XDocument project) =>
        project.Descendants("PackageReference")
            .Select(static reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .OrderBy(static include => include, StringComparer.Ordinal)
            .ToArray();

    private static void AssertProjectTextDoesNotContain(params string[] forbiddenTokens)
    {
        var text = ProjectText();
        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(
                text.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Fast unit test project must not contain dependency token: {token}");
        }
    }

    private static string ProjectText() => File.ReadAllText(ProjectPath());

    private static string ProjectPath() => Path.Combine(RepoRoot(), "IronDev.UnitTests", "IronDev.UnitTests.csproj");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

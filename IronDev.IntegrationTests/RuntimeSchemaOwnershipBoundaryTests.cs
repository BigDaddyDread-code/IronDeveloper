namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class RuntimeSchemaOwnershipBoundaryTests
{
    [TestMethod]
    public void RuntimeServices_DoNotOwnCoreSchemaCreation()
    {
        var root = LocateRepositoryRoot();
        var productionRoots = new[]
        {
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "IronDev.Infrastructure")
        };
        var forbidden = new[] { "CREATE TABLE", "ALTER TABLE" };
        var findings = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 }))
            .Where(item => forbidden.Any(value => item.Line.Contains(value, StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"{Path.GetRelativePath(root, item.Path)}:{item.Number}")
            .ToArray();

        Assert.IsEmpty(findings, $"Runtime schema DDL found: {string.Join(", ", findings)}");
    }

    [TestMethod]
    public void RuntimeSchemaMigration_IsManifestOwned()
    {
        var root = LocateRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var migration = File.ReadAllText(Path.Combine(root, "Database", "migrate_runtime_schema_ownership.sql"));

        StringAssert.Contains(manifest, "Database/migrate_runtime_schema_ownership.sql");
        foreach (var table in new[]
        {
            "dbo.Runs",
            "dbo.RunEvents",
            "dbo.ProjectContextDocuments",
            "dbo.ArtifactSourceReferences",
            "dbo.SemanticArtefacts",
            "dbo.SemanticChunks",
            "dbo.EmbeddingJobs",
            "dbo.SemanticEmbeddings",
            "dbo.SemanticIndexRuns"
        })
        {
            StringAssert.Contains(migration, table);
        }
    }

    [TestMethod]
    public void FullSqlLane_AppliesMigrationManifestBeforeApiTests()
    {
        var root = LocateRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Scripts", "ci", "run-full-sql-integration-ci.ps1"));
        var applyIndex = script.IndexOf("Database\\apply-migrations.ps1", StringComparison.Ordinal);
        var verifyIndex = script.IndexOf("Database\\verify-migrations.ps1", StringComparison.Ordinal);
        var apiSmokeIndex = script.IndexOf("REL-3 SQL API alpha smoke", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, applyIndex);
        Assert.IsGreaterThan(applyIndex, verifyIndex);
        Assert.IsGreaterThan(verifyIndex, apiSmokeIndex);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string LocateRepositoryRoot()
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

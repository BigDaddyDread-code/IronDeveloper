using System.Diagnostics;
using Microsoft.AspNetCore.Builder;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
public sealed class BlockJ02DevelopmentLocalOverrideTests
{
    private const string LocalOverrideFileName = "appsettings.Development.Local.json";

    [TestMethod]
    public void J02_DevelopmentLocalOverrideFile_IsIgnoredAndNotTracked()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));
        var trackedFiles = TrackedFiles();

        StringAssert.Contains(gitignore, LocalOverrideFileName);
        Assert.IsFalse(
            trackedFiles.Any(path => path.EndsWith(LocalOverrideFileName, StringComparison.OrdinalIgnoreCase)),
            $"{LocalOverrideFileName} must not be tracked.");
    }

    [TestMethod]
    public void J02_DevelopmentLocalOverride_IsLoadedOnlyInDevelopment()
    {
        using var directory = TemporaryDirectory.Create();
        WriteLocalOverride(directory.Path, "local-development-value");

        var development = CreateBuilder("Development", directory.Path);
        Program.AddDevelopmentLocalConfiguration(development);

        var production = CreateBuilder("Production", directory.Path);
        Program.AddDevelopmentLocalConfiguration(production);

        Assert.AreEqual("local-development-value", development.Configuration["J02:LocalOverrideProbe"]);
        Assert.IsNull(production.Configuration["J02:LocalOverrideProbe"]);
    }

    [TestMethod]
    public void J02_MissingDevelopmentLocalOverride_DoesNotFailStartupConfiguration()
    {
        using var directory = TemporaryDirectory.Create();
        var builder = CreateBuilder("Development", directory.Path);

        Program.AddDevelopmentLocalConfiguration(builder);

        Assert.IsNull(builder.Configuration["J02:LocalOverrideProbe"]);
    }

    [TestMethod]
    public void J02_EnvironmentVariablesStillOverrideLocalFile()
    {
        using var directory = TemporaryDirectory.Create();
        using var environment = TemporaryEnvironmentVariable.Set("J02__LocalOverrideProbe", "environment-value");
        WriteLocalOverride(directory.Path, "local-file-value");

        var builder = CreateBuilder("Development", directory.Path);
        Program.AddDevelopmentLocalConfiguration(builder);

        Assert.AreEqual("environment-value", builder.Configuration["J02:LocalOverrideProbe"]);
        AssertLocalSourcePrecedesHigherPrecedenceSources(builder);
    }

    [TestMethod]
    public void J02_DocumentedExampleContainsNoMachineSpecificValues()
    {
        var files = new[]
        {
            "Docs/local-development.md",
            "Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md",
            "IronDev.Api/appsettings.Development.Local.example.json"
        };

        var findings = files
            .SelectMany(relativePath => FindForbiddenDocumentationMarkers(relativePath))
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J02_ReceiptStatesLocalOverrideBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "Docs",
            "receipts",
            "J02_DEVELOPMENT_LOCAL_OVERRIDE.md"));

        StringAssert.Contains(receipt, "appsettings.Development.Local.json is developer convenience.");
        StringAssert.Contains(receipt, "It is not shared configuration, not evidence, not authority, and not a runtime contract.");
        StringAssert.Contains(receipt, "A local override may describe a developer's machine locally.");
        StringAssert.Contains(receipt, "It must never be committed, used by CI as shared truth, or treated as permission to mutate SQL, Weaviate, source, evidence, or sandbox repositories.");
        StringAssert.Contains(receipt, "Local overrides belong outside committed truth.");
    }

    private static WebApplicationBuilder CreateBuilder(string environmentName, string contentRoot) =>
        WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = contentRoot,
            Args = []
        });

    private static void WriteLocalOverride(string directory, string value)
    {
        File.WriteAllText(
            Path.Combine(directory, LocalOverrideFileName),
            $$"""
            {
              "J02": {
                "LocalOverrideProbe": "{{value}}"
              }
            }
            """);
    }

    private static void AssertLocalSourcePrecedesHigherPrecedenceSources(WebApplicationBuilder builder)
    {
        var sources = builder.Configuration.Sources.ToArray();
        var localIndex = Array.FindIndex(sources, source =>
            string.Equals(ReadSourcePath(source), LocalOverrideFileName, StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(localIndex >= 0, "Development local JSON source must be present.");

        foreach (var index in Enumerable.Range(0, sources.Length))
        {
            if (!IsHigherPrecedenceSource(sources[index]))
                continue;

            Assert.IsTrue(localIndex < index, $"{LocalOverrideFileName} must be below {sources[index].GetType().Name}.");
        }
    }

    private static string? ReadSourcePath(object source) =>
        source.GetType().GetProperty("Path")?.GetValue(source)?.ToString();

    private static bool IsHigherPrecedenceSource(object source)
    {
        var sourceType = source.GetType().FullName ?? source.GetType().Name;
        return sourceType.Contains("UserSecrets", StringComparison.OrdinalIgnoreCase) ||
            sourceType.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase) ||
            sourceType.Contains("CommandLine", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindForbiddenDocumentationMarkers(string relativePath)
    {
        var fullPath = Path.Combine(RepositoryRoot(), ToNativePath(relativePath));
        var text = File.ReadAllText(fullPath);
        foreach (var marker in ForbiddenDocumentationMarkers())
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                yield return $"{relativePath} contains {marker}";
        }
    }

    private static IReadOnlyList<string> ForbiddenDocumentationMarkers() =>
    [
        "DESKTOP" + "-",
        "LAPTOP" + "-",
        "ROB" + "-",
        "SQL" + "EXPRESS",
        @"C:" + @"\Users" + @"\",
        "/" + "Users/",
        "/" + "home/",
        "Password" + "=",
        "Pwd" + "=",
        "User Id" + "=sa",
        "sk-" + "live",
        "ghp" + "_"
    ];

    private static IReadOnlyList<string> TrackedFiles()
    {
        var startInfo = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.AreEqual(0, process.ExitCode, "git ls-files failed: " + error);

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
    }

    private static void AssertNoFindings(IReadOnlyCollection<string> findings)
    {
        if (findings.Count == 0)
            return;

        Assert.Fail("J02 local override documentation finding(s): " + string.Join("; ", findings));
    }

    private static string ToNativePath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "IronDevJ02",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class TemporaryEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        private TemporaryEnvironmentVariable(string name, string value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static TemporaryEnvironmentVariable Set(string name, string value) =>
            new(name, value);

        public void Dispose() =>
            Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}

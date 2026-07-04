using System.Diagnostics;
using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
public sealed class BlockJ01RemoveHardcodedMachineSqlConfigTests
{
    private static readonly string[] LocalOverrideFileNames =
    [
        "appsettings.Development.Local.json",
        "appsettings.LocalTest.Local.json",
        "appsettings.Test.Local.json"
    ];

    [TestMethod]
    public void J01_CommittedConfigFiles_DoNotContainHardcodedMachineSqlServerNames()
    {
        var findings = CommittedConfigFiles()
            .SelectMany(file => FindMarkers(
                file,
                [
                    "DESKTOP" + "-",
                    "LAPTOP" + "-",
                    "ROB" + "-PC",
                    "Robert",
                    "SQL" + "EXPRESS",
                    "MSSQL" + "SERVER"
                ]))
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J01_CommittedConfigFiles_DoNotContainSqlCredentials()
    {
        var passwordMarker = "Password" + "=";
        var shortPasswordMarker = "Pwd" + "=";
        var saMarker = "User Id" + "=sa";

        var findings = CommittedConfigFiles()
            .SelectMany(file => FindMarkers(
                file,
                [
                    passwordMarker,
                    shortPasswordMarker,
                    saMarker,
                    "Integrated Security" + "=false"
                ]))
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J01_CommittedConfigFiles_DoNotContainDeveloperAbsolutePaths()
    {
        var findings = CommittedConfigFiles()
            .SelectMany(file => FindMarkers(
                file,
                [
                    @"C:\Users\",
                    "/Users/",
                    "/home/"
                ]))
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J01_SharedApiAppsettingsDoesNotPresentSqlServerTruth()
    {
        var connectionString = ReadConnectionString("IronDev.Api/appsettings.json");

        Assert.AreEqual(string.Empty, connectionString, "Shared appsettings.json must not carry a developer SQL server default.");
    }

    [TestMethod]
    public void J01_LocalDevelopmentAndTestConfigUseGenericLocalDbExamples()
    {
        var paths = new[]
        {
            "IronDev.Api/appsettings.Development.json",
            "IronDev.Api/appsettings.LocalTest.json",
            "IronDev.IntegrationTests/appsettings.Test.json",
            "IronDev.IntegrationTests.Api/appsettings.Test.json"
        };

        foreach (var path in paths)
        {
            var connectionString = ReadConnectionString(path);

            StringAssert.Contains(connectionString, @"Server=(localdb)\MSSQLLocalDB", path);
            StringAssert.Contains(connectionString, "Integrated Security=True", path);
            Assert.IsFalse(connectionString.Contains("DESKTOP-", StringComparison.OrdinalIgnoreCase), path);
            Assert.IsFalse(connectionString.Contains("LAPTOP-", StringComparison.OrdinalIgnoreCase), path);
            Assert.IsFalse(connectionString.Contains("SQL" + "EXPRESS", StringComparison.OrdinalIgnoreCase), path);
        }
    }

    [TestMethod]
    public void J01_LocalOverrideFilesAreIgnoredAndNotTracked()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));
        var trackedFiles = TrackedFiles().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in LocalOverrideFileNames)
        {
            StringAssert.Contains(gitignore, fileName, $".gitignore must protect {fileName}.");
            Assert.IsFalse(
                trackedFiles.Any(path => path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)),
                $"{fileName} must not be tracked.");
        }
    }

    [TestMethod]
    public void J01_ReceiptStatesLocalSqlBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "J01_REMOVE_HARDCODED_MACHINE_SQL_CONFIG.md"));

        StringAssert.Contains(receipt, "Local SQL configuration is developer convenience.");
        StringAssert.Contains(receipt, "It is not authority, not evidence, and not a shared runtime contract.");
        StringAssert.Contains(receipt, "No bootstrap, schema, Weaviate, authority, approval, critic, source-apply, or release behavior is changed.");
    }

    private static IReadOnlyList<RepositoryFile> CommittedConfigFiles() =>
        TrackedFiles()
            .Where(IsConfigPath)
            .Select(path => new RepositoryFile(path, File.ReadAllText(Path.Combine(RepositoryRoot(), ToNativePath(path)))))
            .ToArray();

    private static bool IsConfigPath(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
        return fileName.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
             fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> FindMarkers(RepositoryFile file, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (file.Text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                yield return $"{file.RelativePath} contains '{marker}'";
        }
    }

    private static string ReadConnectionString(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), ToNativePath(relativePath))));
        Assert.IsTrue(document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings), $"{relativePath} must have ConnectionStrings.");
        Assert.IsTrue(connectionStrings.TryGetProperty("IronDeveloperDb", out var value), $"{relativePath} must have IronDeveloperDb.");
        Assert.AreEqual(JsonValueKind.String, value.ValueKind, $"{relativePath} IronDeveloperDb must be a string.");

        return value.GetString() ?? string.Empty;
    }

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

    private static string ToNativePath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static void AssertNoFindings(IReadOnlyCollection<string> findings)
    {
        if (findings.Count == 0)
            return;

        Assert.Fail("Machine-local SQL config finding(s): " + string.Join("; ", findings));
    }

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

    private sealed record RepositoryFile(string RelativePath, string Text);
}

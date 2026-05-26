using System.IO;
using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ApiBoundaryTests
{
    [TestMethod]
    public void ForwardProductProjects_MustNotReferenceInfrastructure()
    {
        var root = FindRepositoryRoot();

        AssertProjectDoesNotReference(root, "IronDev.Client", "IronDev.Infrastructure");
        AssertProjectDoesNotReference(root, Path.Combine("tools", "IronDev.Cli"), "IronDev.Infrastructure");
        AssertProjectDoesNotReference(root, "IronDeveloper", "IronDev.Infrastructure");
    }

    [TestMethod]
    public void TauriShellSource_MustNotReferenceInfrastructureStorageDetails()
    {
        var root = FindRepositoryRoot();
        var shellRoot = Path.Combine(root, "IronDev.TauriShell");
        var forbidden = new Regex(@"IronDev\.Infrastructure|SqlConnection|Dapper|Weaviate", RegexOptions.Compiled);

        foreach (var source in Directory.EnumerateFiles(shellRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                                    path.Contains($"{Path.DirectorySeparatorChar}src-tauri{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(source);
            Assert.IsFalse(forbidden.IsMatch(text), $"Tauri shell must not reference infrastructure storage/provider details. Violation in {source}.");
        }
    }

    private static void AssertProjectDoesNotReference(string root, string projectDirectory, string forbiddenReference)
    {
        var projectRoot = Path.Combine(root, projectDirectory);
        foreach (var projectFile in Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                     .Where(path => !Path.GetFileName(path).Contains("_wpftmp", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(projectFile);
            Assert.IsFalse(
                text.Contains(forbiddenReference, StringComparison.Ordinal),
                $"{projectFile} must not reference {forbiddenReference}.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IronDev.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

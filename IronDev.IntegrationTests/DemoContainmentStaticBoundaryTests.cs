using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P3-1 demo containment guardrail.
/// Runtime roots ship in the product; demo/sample material may be exercised by tests and
/// smoke runs but must never leak into them: no demo-name special-casing, no
/// authority-flavored demo fixtures, no hardcoded local SQL/machine/user-profile paths.
/// Sample directories are the inverse: they are the demo's target surface and must grant
/// nothing — no references into IronDev authority namespaces or governance types.
/// </summary>
[TestCategory("StaticBoundary")]
[TestClass]
public sealed class DemoContainmentStaticBoundaryTests
{
    private static readonly string[] RuntimeRoots =
    [
        "IronDev.Core",
        "IronDev.Infrastructure",
        "IronDev.Api",
        "IronDev.Client"
    ];

    private static readonly string[] DemoNameTokens =
    [
        "bookseller",
        "book seller",
        "bookstore"
    ];

    // "(localdb)" is deliberately absent here: runtime code may *classify* a configured
    // connection string as LocalDB (redaction, validation) — it must not *supply* one.
    private static readonly string[] MachineSpecificSourceTokens =
    [
        string.Concat(@"C:\", "Users"),
        string.Concat(@"C:\\", "Users"),
        string.Concat("%USER", "PROFILE%"),
        string.Concat("SQL", "EXPRESS"),
        string.Concat("DESKTOP", "-")
    ];

    private static readonly string[] MachineSpecificConfigTokens =
    [
        string.Concat(@"C:\", "Users"),
        string.Concat(@"C:\\", "Users"),
        string.Concat("%USER", "PROFILE%"),
        string.Concat("(local", "db)"),
        string.Concat("SQL", "EXPRESS"),
        string.Concat("DESKTOP", "-")
    ];

    private static readonly string[] SampleForbiddenAuthorityTokens =
    [
        "IronDev.Core",
        "IronDev.Infrastructure",
        "IronDev.Api",
        "IronDev.Client",
        "ApprovalAuthority",
        "HumanApprovalPackage",
        "ApprovalGate",
        "GovernedTier",
        "PolicyDecision"
    ];

    private static readonly string[] SkippedDirectoryNames = ["bin", "obj", "node_modules", ".git"];

    [TestMethod]
    public void RuntimeRoots_ContainNoDemoNameSpecialCasing()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSources(RuntimeRoots.Select(r => Path.Combine(root, r)), "*.cs"))
            CollectTokenViolations(root, file, DemoNameTokens, violations);

        AssertNoViolations(
            violations,
            "Demo project names must not appear in runtime roots. The Builder earns the demo by solving it; runtime code must not recognise, favour, or answer for a demo project.");
    }

    [TestMethod]
    public void TauriShellRuntimeSources_ContainNoDemoNameSpecialCasing()
    {
        var root = FindRepoRoot();
        var shellSrc = Path.Combine(root, "IronDev.TauriShell", "src");
        Assert.IsTrue(Directory.Exists(shellSrc), $"Expected UI source root missing: {shellSrc}");

        var violations = new List<string>();
        foreach (var pattern in new[] { "*.ts", "*.tsx" })
        {
            foreach (var file in EnumerateSources([shellSrc], pattern))
                CollectTokenViolations(root, file, DemoNameTokens, violations);
        }

        AssertNoViolations(
            violations,
            "Demo project names must not appear in shipped UI sources. Demo tickets are fixture/test material, not UI copy or UI special-casing.");
    }

    [TestMethod]
    public void RuntimeRoots_ContainNoHardcodedLocalEnvironmentPaths()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSources(RuntimeRoots.Select(r => Path.Combine(root, r)), "*.cs"))
            CollectTokenViolations(root, file, MachineSpecificSourceTokens, violations);

        AssertNoViolations(
            violations,
            "Runtime sources must not hardcode local SQL instances, machine names, or user-profile paths. A clean machine must be able to run the product without hidden local knowledge.");
    }

    [TestMethod]
    public void ProductionDefaultConfig_ContainsNoLocalOnlyInfrastructure()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        // appsettings.json is the shipped default and must be machine-agnostic.
        // appsettings.Development.json may use (localdb) — a documented, machine-agnostic
        // dev experience — but never user-profile paths or machine names.
        CollectTokenViolations(root, Path.Combine(root, "IronDev.Api", "appsettings.json"), MachineSpecificConfigTokens, violations);
        CollectTokenViolations(
            root,
            Path.Combine(root, "IronDev.Api", "appsettings.Development.json"),
            [
                string.Concat(@"C:\", "Users"),
                string.Concat(@"C:\\", "Users"),
                string.Concat("%USER", "PROFILE%"),
                string.Concat("SQL", "EXPRESS"),
                string.Concat("DESKTOP", "-")
            ],
            violations);

        AssertNoViolations(
            violations,
            "Default API configuration must not bake in machine-specific SQL, machine names, or user-profile paths.");
    }

    [TestMethod]
    public void SampleDirectories_GrantNoAuthority()
    {
        var root = FindRepoRoot();
        var samplesRoot = Path.Combine(root, "Samples");
        if (!Directory.Exists(samplesRoot))
            return; // P3-2 creates Samples/BookSeller. The rule is armed the moment it exists.

        var violations = new List<string>();
        foreach (var pattern in new[] { "*.cs", "*.csproj", "*.json", "*.ps1" })
        {
            foreach (var file in EnumerateSources([samplesRoot], pattern))
                CollectTokenViolations(root, file, SampleForbiddenAuthorityTokens, violations);
        }

        AssertNoViolations(
            violations,
            "Sample projects are demo targets only. They must not reference IronDev namespaces or authority/governance types — usable by tests and smoke, granting nothing.");
    }

    private static IEnumerable<string> EnumerateSources(IEnumerable<string> roots, string pattern)
    {
        foreach (var rootDir in roots)
        {
            Assert.IsTrue(Directory.Exists(rootDir), $"Expected runtime root missing: {rootDir}");
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(rootDir, pattern, options))
            {
                var relative = file[rootDir.Length..];
                if (SkippedDirectoryNames.Any(skip =>
                        relative.Contains($"{Path.DirectorySeparatorChar}{skip}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                    continue;

                yield return file;
            }
        }
    }

    private static void CollectTokenViolations(string root, string file, string[] forbiddenTokens, List<string> violations)
    {
        Assert.IsTrue(File.Exists(file), $"Expected file missing: {file}");
        var text = File.ReadAllText(file);
        foreach (var token in forbiddenTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                violations.Add($"{Path.GetRelativePath(root, file)} contains forbidden token '{token}'");
        }
    }

    private static void AssertNoViolations(List<string> violations, string rule)
    {
        Assert.AreEqual(
            0,
            violations.Count,
            $"{rule}{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string FindRepoRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("IRONDEV_REPO_ROOT"),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var current = new DirectoryInfo(candidate!);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory or current working directory.");
    }
}

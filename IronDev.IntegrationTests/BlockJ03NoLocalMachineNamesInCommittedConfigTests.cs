using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
public sealed class BlockJ03NoLocalMachineNamesInCommittedConfigTests
{
    private static readonly string[] LocalOverrideFileNames =
    [
        "appsettings.Development.Local.json",
        "appsettings.LocalTest.Local.json",
        "appsettings.Test.Local.json",
        ".env",
        ".env.local"
    ];

    private static readonly string[] ExcludedPathPrefixes =
    [
        "bin/",
        "obj/",
        "node_modules/",
        "dist/",
        "build/",
        "out/",
        "coverage/",
        "TestResults/",
        "artifacts/",
        ".git/",
        "tools/dogfood/proofs/",
        "tools/dogfood/knowledge/"
    ];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".jsonc",
        ".config",
        ".props",
        ".targets",
        ".csproj",
        ".sln",
        ".slnx",
        ".ps1",
        ".psm1",
        ".sh",
        ".cmd",
        ".bat",
        ".yml",
        ".yaml",
        ".md",
        ".ts",
        ".tsx",
        ".cs"
    };

    // CI SQL Server service credentials are fake, run-scoped test configuration.
    // The exception is path-specific and marker-specific; developer-local credentials remain forbidden.
    private static readonly AllowedFinding[] AllowedFindings =
    [
        new(
            ".github/workflows/sql-integration-ci.yml",
            "CredentialAssignment",
            "ConnectionStrings__IronDeveloperDb",
            "SQL CI uses a run-scoped service database connection string; C11 remains the secret-scanning authority."),
        new(
            ".github/workflows/sql-integration-ci.yml",
            "SaUserConnectionString",
            "ConnectionStrings__IronDeveloperDb",
            "SQL CI uses the ephemeral SQL Server service administrator account only inside GitHub Actions.")
    ];

    [TestMethod]
    public void J03_TrackedRepositoryFiles_DoNotContainLocalMachineNames()
    {
        var findings = ScanRepository(WorkstationNameRules()).ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J03_TrackedRepositoryFiles_DoNotContainDeveloperAbsolutePaths()
    {
        var findings = ScanRepository(DeveloperPathRules()).ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J03_TrackedRepositoryFiles_DoNotContainMachineSpecificSqlInstances()
    {
        var findings = ScanRepository(SqlInstanceRules().Concat(CredentialShapeRules()).ToArray()).ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void J03_LocalOverrideFilesAreNotTracked()
    {
        var trackedFiles = TrackedFiles();

        foreach (var fileName in LocalOverrideFileNames)
        {
            Assert.IsFalse(
                trackedFiles.Any(path => path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)),
                $"{fileName} must not be tracked.");
        }
    }

    [TestMethod]
    public void J03_DocsUsePlaceholdersNotRealMachines()
    {
        var docsFindings = ScanRepository(
                WorkstationNameRules().Concat(DeveloperPathRules()).Concat(SqlInstanceRules()).ToArray(),
                file => file.RelativePath.StartsWith("Docs/", StringComparison.OrdinalIgnoreCase) &&
                    file.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        AssertNoFindings(docsFindings);
    }

    [TestMethod]
    public void J03_ReceiptStatesConfigBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "Docs",
            "receipts",
            "J03_VALIDATE_NO_LOCAL_MACHINE_NAMES_IN_COMMITTED_CONFIG.md"));

        StringAssert.Contains(receipt, "Local machine names, local paths, and local SQL instances are developer-local facts.");
        StringAssert.Contains(receipt, "They are not shared configuration, not evidence, not authority, and not a runtime contract.");
        StringAssert.Contains(receipt, "Generic examples are allowed only when they are portable or placeholder-based.");
        StringAssert.Contains(receipt, "Real developer machines are never allowed as examples.");
        StringAssert.Contains(receipt, "No runtime behavior changes.");
        StringAssert.Contains(receipt, "No bootstrap behavior changes.");
        StringAssert.Contains(receipt, "No schema or SQL migration changes.");
    }

    [TestMethod]
    public void J03_ScannerDoesNotPassVacuously()
    {
        var scannedFiles = ScannableRepositoryFiles().Select(file => file.RelativePath).ToArray();

        Assert.IsTrue(scannedFiles.Length > 100, "J03 scanner must cover a broad tracked-file surface.");
        CollectionAssert.Contains(scannedFiles, "IronDev.Api/appsettings.json");
        CollectionAssert.Contains(scannedFiles, "Docs/local-development.md");
        CollectionAssert.Contains(scannedFiles, ".gitignore");

        Assert.IsTrue(AllowedFindings.Length > 0, "J03 should make known exceptions auditable instead of implicit.");
        foreach (var allowed in AllowedFindings)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(allowed.RelativePath), "Allowed finding path is required.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(allowed.RuleName), "Allowed finding rule is required.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(allowed.TextContains), "Allowed finding marker is required.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(allowed.Reason), "Allowed finding reason is required.");
            Assert.IsFalse(allowed.RuleName.Contains("Workstation", StringComparison.OrdinalIgnoreCase), "Workstation names cannot be allowlisted.");
            Assert.IsFalse(allowed.RuleName.Contains("DeveloperPath", StringComparison.OrdinalIgnoreCase), "Developer absolute paths cannot be allowlisted.");
            Assert.IsFalse(allowed.RuleName.Contains("SqlInstance", StringComparison.OrdinalIgnoreCase), "Machine-specific SQL instances cannot be allowlisted.");
        }
    }

    private static IReadOnlyList<ScanFinding> ScanRepository(
        IReadOnlyList<ScanRule> rules,
        Func<RepositoryFile, bool>? predicate = null)
    {
        var findings = new List<ScanFinding>();
        foreach (var file in ScannableRepositoryFiles())
        {
            if (predicate is not null && !predicate(file))
                continue;

            var fullPath = Path.Combine(RepositoryRoot(), ToNativePath(file.RelativePath));
            var lines = File.ReadAllLines(fullPath);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var rule in rules)
                {
                    if (!rule.Pattern.IsMatch(lines[index]))
                        continue;

                    var finding = new ScanFinding(file.RelativePath, index + 1, rule.Name, Preview(lines[index]));
                    if (!IsAllowed(finding, lines[index]))
                        findings.Add(finding);
                }
            }
        }

        return findings;
    }

    private static IReadOnlyList<RepositoryFile> ScannableRepositoryFiles() =>
        TrackedFiles()
            .Where(IsScannablePath)
            .Select(path => new RepositoryFile(path))
            .ToArray();

    private static bool IsScannablePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (ExcludedPathPrefixes.Any(prefix =>
                normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/" + prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (normalized.EndsWith(".env.example", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Path.GetFileName(normalized).Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
            return true;

        return TextExtensions.Contains(Path.GetExtension(normalized));
    }

    private static IReadOnlyList<ScanRule> WorkstationNameRules() =>
    [
        LiteralRule("WorkstationDesktopPrefix", "DESKTOP" + "-"),
        LiteralRule("WorkstationLaptopPrefix", "LAPTOP" + "-"),
        LiteralRule("WorkstationRobPc", "ROB" + "-PC"),
        LiteralRule("WorkstationRobPrefix", "ROB" + "-"),
        LiteralRule("WorkstationKnownHostFragment", "KFA" + "0H13")
    ];

    private static IReadOnlyList<ScanRule> DeveloperPathRules() =>
    [
        RegexRule("DeveloperPathWindowsUsers", string.Concat(@"(?i)\b[A-Z]:[\\/]+", "Users", @"[\\/]+(?!<you>(?:[\\/]|$))")),
        RegexRule("DeveloperPathWindowsFileUri", string.Concat(@"(?i)file:///[a-z]:/", "Users/", @"(?!<you>(?:/|$))")),
        RegexRule("DeveloperPathMacUsers", string.Concat(@"(?<![A-Za-z0-9_])/", "Users/", @"(?!<you>(?:/|$))")),
        RegexRule("DeveloperPathLinuxHome", string.Concat(@"(?<![A-Za-z0-9_])/", "home/", @"(?!<you>(?:/|$))"))
    ];

    private static IReadOnlyList<ScanRule> SqlInstanceRules() =>
    [
        RegexRule("SqlInstanceNamedBackslash", string.Concat(@"(?i)\\", "SQL", "EXPRESS", @"\b")),
        RegexRule("SqlInstanceNamedWord", string.Concat(@"(?i)\b", "SQL", "EXPRESS", @"\b")),
        RegexRule("SqlInstanceDefaultServer", string.Concat(@"(?i)(?<![A-Za-z0-9_])", "MSSQL", "SERVER", @"(?![A-Za-z0-9_])")),
        RegexRule("SqlInstanceDesktopServerAssignment", string.Concat(@"(?i)(?:Server|Data Source)\s*=\s*DESKTOP", "-")),
        RegexRule("SqlInstanceLaptopServerAssignment", string.Concat(@"(?i)(?:Server|Data Source)\s*=\s*LAPTOP", "-"))
    ];

    private static IReadOnlyList<ScanRule> CredentialShapeRules() =>
    [
        RegexRule("CredentialAssignment", @"(?:^|[;""'\s])(?:Password|Pwd|ApiKey|Token|Secret)="),
        RegexRule("SaUserConnectionString", @"(?:^|[;""'\s])User I[dD]=sa\b")
    ];

    private static ScanRule LiteralRule(string name, string literal, RegexOptions options = RegexOptions.None) =>
        new(name, new Regex(Regex.Escape(literal), options | RegexOptions.Compiled | RegexOptions.CultureInvariant));

    private static ScanRule RegexRule(string name, string pattern) =>
        new(name, new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));

    private static bool IsAllowed(ScanFinding finding, string fullLine) =>
        AllowedFindings.Any(allowed =>
            finding.RelativePath.Equals(allowed.RelativePath, StringComparison.OrdinalIgnoreCase) &&
            finding.RuleName.Equals(allowed.RuleName, StringComparison.OrdinalIgnoreCase) &&
            fullLine.Contains(allowed.TextContains, StringComparison.OrdinalIgnoreCase));

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

        var trackedFiles = output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();

        Assert.IsTrue(trackedFiles.Length > 0, "git ls-files returned no tracked files.");
        return trackedFiles;
    }

    private static string Preview(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= 120 ? trimmed : trimmed[..120] + "...";
    }

    private static void AssertNoFindings(IReadOnlyCollection<ScanFinding> findings)
    {
        if (findings.Count == 0)
            return;

        Assert.Fail("J03 committed local-machine config finding(s): " + string.Join("; ", findings.Select(finding =>
            $"{finding.RuleName} {finding.RelativePath}:{finding.LineNumber} {finding.Preview}")));
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

    private sealed record RepositoryFile(string RelativePath);

    private sealed record ScanRule(string Name, Regex Pattern);

    private sealed record ScanFinding(string RelativePath, int LineNumber, string RuleName, string Preview);

    private sealed record AllowedFinding(string RelativePath, string RuleName, string TextContains, string Reason);
}

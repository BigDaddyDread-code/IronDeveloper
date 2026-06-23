using System.Text.Json;
using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed partial class BlockC11SecretScanningRegressionTests
{
    private static readonly string[] ConfigFiles =
    [
        "IronDev.Api/appsettings.json",
        "IronDev.Api/appsettings.Development.json",
        "IronDev.Api/appsettings.LocalTest.json",
        "IronDev.IntegrationTests/appsettings.Test.json",
        "IronDev.IntegrationTests.Api/appsettings.Test.json"
    ];

    private static readonly SecretAllowlistEntry[] Allowlist =
    [
        new("IronDev.Api/Auth/JwtSigningKeyResolver.cs", "OldJwtPlaceholder", "OldCommittedPlaceholderKey", "runtime rejection constant for the old committed JWT placeholder"),
        new("IronDev.IntegrationTests/BlockC06JwtSecretConfigurationTests.cs", "OldJwtPlaceholder", "OldPlaceholderKey", "focused test proves the old JWT placeholder cannot be accepted"),
        new("IronDev.IntegrationTests/BlockC07JwtStartupValidationTests.cs", "OldJwtPlaceholder", "OldPlaceholderKey", "focused test proves the old JWT placeholder cannot be accepted"),
        new("IronDev.Infrastructure/Services/LlmTraceService.cs", "PasswordConnectionString", "Password=[^;]+;", "redaction regex pattern, not a concrete connection string"),
        new("IronDev.IntegrationTests/ProjectContextExportServiceTests.cs", "PasswordConnectionString", "SuperSecretPassword123", "fake test-only connection string used to prove export sanitisation"),
        new("IronDev.IntegrationTests/ApiCliContract/ApiCliBoundaryContractTests.cs", "OpenAiApiKeyPattern", "sk-" + "live-api-cli-contract-secret", "fake test-only token proves CLI secret redaction"),
        new("IronDev.IntegrationTests/Agents/AgentModelAuditSanitisationTests.cs", "PasswordConnectionString", "password=" + "abc123secret", "fake test-only password proves audit sanitisation"),
        new("IronDev.IntegrationTests/Agents/AgentModelAuditSanitisationTests.cs", "GitHubTokenPattern", "ghp_" + "abc123secret", "fake test-only token proves audit sanitisation"),
        new("IronDev.IntegrationTests/Agents/AgentModelAuditSanitisationTests.cs", "OpenAiApiKeyPattern", "sk-" + "abc123secret", "fake test-only token proves audit sanitisation"),
        new(".github/workflows/sql-integration-ci.yml", "PasswordConnectionString", "IronDev_CI_Strong_Passw0rd_123!", "ephemeral SQL service password scoped to GitHub Actions test container")
    ];

    private static readonly string[] IgnoredDirectoryNames =
    [
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "TestResults",
        "coverage",
        ".next",
        "dist",
        "build",
        "artifacts",
        "tmp",
        "temp",
        "logs"
    ];

    private static readonly string[] ScannedExtensions =
    [
        ".cs",
        ".json",
        ".md",
        ".ps1",
        ".yml",
        ".yaml"
    ];

    [TestMethod]
    public void BlockC11_CommittedConfigFiles_DoNotContainJwtOrWeaviateSecrets()
    {
        var findings = ConfigFiles
            .Where(FileExists)
            .SelectMany(path => ScanFile(ReadFile(path)))
            .Where(finding => finding.RuleName is "CommittedJwtKey" or "CommittedWeaviateApiKey" or "OldJwtPlaceholder")
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void BlockC11_RepositoryTextFiles_DoNotContainHighConfidenceProviderTokens()
    {
        var findings = ScanRepository()
            .Where(finding => finding.RuleName is
                "OpenAiApiKeyPattern" or
                "GitHubTokenPattern" or
                "BearerTokenPattern" or
                "ConcreteApiKeyAssignment" or
                "ConcreteSecretAssignment" or
                "ConcreteClientSecretAssignment" or
                "ConcreteSigningKeyAssignment")
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void BlockC11_RepositoryTextFiles_DoNotContainPrivateKeys()
    {
        var findings = ScanRepository()
            .Where(finding => finding.RuleName is
                "RsaPrivateKeyBlock" or
                "OpenSshPrivateKeyBlock" or
                "PrivateKeyBlock")
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void BlockC11_ConnectionStrings_DoNotContainPasswords()
    {
        var findings = ScanRepository()
            .Where(finding => finding.RuleName == "PasswordConnectionString")
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void BlockC11_Findings_RedactCandidateValues()
    {
        var fakeToken = "sk-" + "live-this-token-must-not-print";
        var findings = ScanText(
            "sample.cs",
            $"""
            const string Token = "{fakeToken}";
            """)
            .ToArray();

        Assert.AreEqual(1, findings.Length);
        Assert.AreEqual("OpenAiApiKeyPattern", findings[0].RuleName);
        StringAssert.Contains(findings[0].Preview, "sk-***REDACTED***");
        AssertDoesNotContain(findings[0].ToString(), fakeToken, "finding output");
    }

    [TestMethod]
    public void BlockC11_Allowlist_IsSmallExplicitAndDocumented()
    {
        Assert.IsTrue(Allowlist.Length <= 12, "C11 allowlist must stay small.");

        foreach (var entry in Allowlist)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.RelativePath), "Allowlist entry must name one file.");
            Assert.IsFalse(entry.RelativePath.EndsWith("/", StringComparison.Ordinal), "Allowlist must not use directory-wide entries.");
            Assert.IsFalse(entry.RelativePath.Contains("**", StringComparison.Ordinal), "Allowlist must not use broad glob entries.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.RuleName), "Allowlist entry must name one rule.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.LineContains), "Allowlist entry must bind to a literal line fragment.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Reason), "Allowlist entry must document a reason.");
        }
    }

    [TestMethod]
    public void BlockC11_SecurityBoundaryReceipts_DoNotContainSecretValues()
    {
        var receiptRoot = Path.Combine(RepositoryRoot(), "Docs", "receipts");
        var findings = Directory
            .EnumerateFiles(receiptRoot, "*.md", SearchOption.AllDirectories)
            .Select(path => ReadFile(RelativePath(path)))
            .SelectMany(ScanFile)
            .Where(finding => !IsAllowed(finding))
            .ToArray();

        AssertNoFindings(findings);
    }

    [TestMethod]
    public void BlockC11_DoesNotScanBuildOutputsOrDependencyFolders()
    {
        Assert.IsFalse(ShouldScanPath(Path.Combine(RepositoryRoot(), "IronDev.Api", "bin", "Debug", "generated.cs")));
        Assert.IsFalse(ShouldScanPath(Path.Combine(RepositoryRoot(), "IronDev.Api", "obj", "project.assets.json")));
        Assert.IsFalse(ShouldScanPath(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "node_modules", "package", "index.js")));
        Assert.IsFalse(ShouldScanPath(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "dist", "bundle.js")));
        Assert.IsFalse(ShouldScanPath(Path.Combine(RepositoryRoot(), "TestResults", "result.trx")));

        Assert.IsTrue(ShouldScanPath(Path.Combine(RepositoryRoot(), "IronDev.Api", "appsettings.json")));
        Assert.IsTrue(ShouldScanPath(Path.Combine(RepositoryRoot(), "Docs", "receipts", "C11_SECRET_SCANNING_REGRESSION.md")));
        Assert.IsTrue(ShouldScanPath(Path.Combine(RepositoryRoot(), "Scripts", "ci", "run-governance-boundary-ci.ps1")));
    }

    [TestMethod]
    public void BlockC11_GovernanceBoundaryCiRunsSecretScan()
    {
        var script = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");

        StringAssert.Contains(script, "$securityBoundaryFilter");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC11SecretScanningRegressionTests");
        StringAssert.Contains(script, "-Name \"Security boundary tests\"");
        AssertDoesNotContain(script, "gitleaks", "governance-boundary CI script");
        AssertDoesNotContain(script, "trufflehog", "governance-boundary CI script");
        AssertDoesNotContain(script, "upload-artifact", "governance-boundary CI script");
    }

    private static IReadOnlyList<SecretFinding> ScanRepository() =>
        EnumerateRepositoryTextFiles()
            .Select(ReadFile)
            .SelectMany(ScanFile)
            .Where(finding => !IsAllowed(finding))
            .ToArray();

    private static IEnumerable<string> EnumerateRepositoryTextFiles() =>
        EnumerateRepositoryTextFiles(RepositoryRoot());

    private static IEnumerable<string> EnumerateRepositoryTextFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (ShouldScanPath(file))
                yield return RelativePath(file);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            if (IgnoredDirectoryNames.Contains(Path.GetFileName(childDirectory), StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var file in EnumerateRepositoryTextFiles(childDirectory))
                yield return file;
        }
    }

    private static bool ShouldScanPath(string path)
    {
        var relative = RelativePath(path).Replace('\\', '/');
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => IgnoredDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            return false;

        var fileName = Path.GetFileName(relative);
        if (fileName.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ScannedExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase);
    }

    private static RepositoryTextFile ReadFile(string relativePath) =>
        new(relativePath.Replace('\\', '/'), File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))));

    private static IEnumerable<SecretFinding> ScanFile(RepositoryTextFile file)
    {
        foreach (var finding in ScanText(file.RelativePath, file.Text))
            yield return finding;

        if (IsAppsettingsJson(file.RelativePath))
        {
            foreach (var finding in ScanConfigJson(file))
                yield return finding;
        }
    }

    private static IEnumerable<SecretFinding> ScanConfigJson(RepositoryTextFile file)
    {
        using var document = JsonDocument.Parse(file.Text);
        if (TryGetNestedString(document.RootElement, ["Jwt", "Key"], out var jwtKey) &&
            !string.IsNullOrWhiteSpace(jwtKey))
        {
            yield return BuildFinding("CommittedJwtKey", file.RelativePath, LineNumber(file.Text, jwtKey), jwtKey);
        }

        if (TryGetNestedString(document.RootElement, ["Weaviate", "ApiKey"], out var weaviateApiKey) &&
            !string.IsNullOrWhiteSpace(weaviateApiKey))
        {
            yield return BuildFinding("CommittedWeaviateApiKey", file.RelativePath, LineNumber(file.Text, weaviateApiKey), weaviateApiKey);
        }
    }

    private static IEnumerable<SecretFinding> ScanText(string relativePath, string text)
    {
        foreach (var finding in RegexFindings(OldJwtPlaceholderRegex(), "OldJwtPlaceholder", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(OpenAiApiKeyRegex(), "OpenAiApiKeyPattern", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(GitHubTokenRegex(), "GitHubTokenPattern", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(BearerTokenRegex(), "BearerTokenPattern", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(RsaPrivateKeyRegex(), "RsaPrivateKeyBlock", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(OpenSshPrivateKeyRegex(), "OpenSshPrivateKeyBlock", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(PrivateKeyRegex(), "PrivateKeyBlock", relativePath, text, match => match.Value))
            yield return finding;

        foreach (var finding in RegexFindings(PasswordConnectionStringRegex(), "PasswordConnectionString", relativePath, text, match => match.Groups["value"].Value))
            yield return finding;

        foreach (Match match in JsonSecretAssignmentRegex().Matches(text))
        {
            var value = match.Groups["value"].Value;
            if (IsAllowedPlaceholderValue(value))
                continue;

            yield return BuildFinding(
                $"Concrete{match.Groups["name"].Value}Assignment",
                relativePath,
                LineNumber(text, match.Index),
                value);
        }

        foreach (Match match in CodeSecretAssignmentRegex().Matches(text))
        {
            var value = match.Groups["value"].Value;
            if (IsAllowedPlaceholderValue(value))
                continue;

            yield return BuildFinding(
                $"Concrete{match.Groups["name"].Value}Assignment",
                relativePath,
                LineNumber(text, match.Index),
                value);
        }
    }

    private static IEnumerable<SecretFinding> RegexFindings(
        Regex regex,
        string ruleName,
        string relativePath,
        string text,
        Func<Match, string> candidateSelector)
    {
        foreach (Match match in regex.Matches(text))
        {
            var candidate = candidateSelector(match);
            if (string.IsNullOrWhiteSpace(candidate) || IsAllowedPlaceholderValue(candidate))
                continue;

            yield return BuildFinding(ruleName, relativePath, LineNumber(text, match.Index), candidate);
        }
    }

    private static SecretFinding BuildFinding(
        string ruleName,
        string relativePath,
        int lineNumber,
        string candidate) =>
        new(ruleName, relativePath.Replace('\\', '/'), lineNumber, Redact(candidate));

    private static bool IsAllowed(SecretFinding finding)
    {
        var line = File.ReadLines(Path.Combine(RepositoryRoot(), finding.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Skip(Math.Max(finding.LineNumber - 1, 0))
            .FirstOrDefault() ?? string.Empty;

        return Allowlist.Any(entry =>
            string.Equals(entry.RelativePath, finding.RelativePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.RuleName, finding.RuleName, StringComparison.Ordinal) &&
            line.Contains(entry.LineContains, StringComparison.Ordinal));
    }

    private static bool TryGetNestedString(JsonElement element, IReadOnlyList<string> path, out string value)
    {
        value = string.Empty;
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
            return false;

        value = current.GetString() ?? string.Empty;
        return true;
    }

    private static bool IsAllowedPlaceholderValue(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        if (normalized.StartsWith('<') && normalized.EndsWith('>'))
            return true;

        if (normalized.StartsWith("{{", StringComparison.Ordinal) && normalized.EndsWith("}}", StringComparison.Ordinal))
            return true;

        if (normalized.Contains("REDACTED", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.Contains("[^", StringComparison.Ordinal))
            return true;

        var lower = normalized.ToLowerInvariant();
        if (lower.StartsWith("fake-", StringComparison.Ordinal))
            return true;

        return lower is
            "your-api-key-here" or
            "local-dev-key" or
            "changeme" or
            "change-me" or
            "placeholder" or
            "example" or
            "fake" or
            "test";
    }

    private static string Redact(string candidate)
    {
        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("sk-", StringComparison.Ordinal))
            return "sk-***REDACTED***";

        if (trimmed.StartsWith("ghp_", StringComparison.Ordinal))
            return "ghp_***REDACTED***";

        if (trimmed.StartsWith("github_pat_", StringComparison.Ordinal))
            return "github_pat_***REDACTED***";

        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return "Bearer ***REDACTED***";

        if (trimmed.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            return "-----BEGIN ***REDACTED*** PRIVATE KEY-----";

        return "***REDACTED***";
    }

    private static bool IsAppsettingsJson(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        return fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static int LineNumber(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        return index < 0 ? 1 : LineNumber(text, index);
    }

    private static int LineNumber(string text, int index) =>
        text[..Math.Min(Math.Max(index, 0), text.Length)].Count(ch => ch == '\n') + 1;

    private static void AssertNoFindings(IReadOnlyCollection<SecretFinding> findings)
    {
        if (findings.Count == 0)
            return;

        Assert.Fail("Secret-like value found: " + string.Join("; ", findings.Take(10)));
    }

    private static bool FileExists(string relativePath) =>
        File.Exists(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepositoryRoot(), Path.GetFullPath(path));

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static void AssertDoesNotContain(string text, string marker, string sourceName)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{marker}'.");
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

    [GeneratedRegex("irondev-super-secret-jwt-key-change-in-production-min32chars", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OldJwtPlaceholderRegex();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9][A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiApiKeyRegex();

    [GeneratedRegex(@"\b(?:ghp_[A-Za-z0-9_]{10,}|github_pat_[A-Za-z0-9_]{10,})", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{16,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("BEGIN RSA " + "PRIVATE KEY", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RsaPrivateKeyRegex();

    [GeneratedRegex("BEGIN OPENSSH " + "PRIVATE KEY", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenSshPrivateKeyRegex();

    [GeneratedRegex("BEGIN " + "PRIVATE KEY", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"(?:^|[;""'])\s*(?:Password|Pwd)\s*=\s*(?<value>[^;""'\r\n]+)(?=;|[""'\r\n])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordConnectionStringRegex();

    [GeneratedRegex(@"""(?<name>ApiKey|Secret|ClientSecret|SigningKey)""\s*:\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretAssignmentRegex();

    [GeneratedRegex(@"\b(?<name>ApiKey|Secret|ClientSecret|SigningKey)\s*=\s*""(?<value>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeSecretAssignmentRegex();

    private sealed record RepositoryTextFile(string RelativePath, string Text);

    private sealed record SecretFinding(string RuleName, string RelativePath, int LineNumber, string Preview)
    {
        public override string ToString() =>
            $"Rule={RuleName} File={RelativePath} Line={LineNumber} Preview={Preview}";
    }

    private sealed record SecretAllowlistEntry(
        string RelativePath,
        string RuleName,
        string LineContains,
        string Reason);
}

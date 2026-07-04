using System.Text.Json;
using IronDev.Core.Configuration;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
public sealed class BlockJ08FailSafeConfigSummaryTests
{
    [TestMethod]
    public void J08_ConfigSummary_DoesNotEmitRawConnectionString()
    {
        var connectionString = BuildCredentialedConnectionString("IronDeveloper_J08", "j08_user", SensitiveValue());
        var summary = BuildSummary([
            new ConfigValueInput("ConnectionStrings:IronDeveloperDb", connectionString, "environment variables")
        ]);
        var serialized = Serialize(summary);

        Assert.IsTrue(summary.Database.Configured);
        Assert.AreEqual("SQL Server", summary.Database.ProviderShape);
        Assert.AreEqual("IronDeveloper_J08", summary.Database.DatabaseName);
        Assert.AreEqual("LocalDB", summary.Database.ServerKind);
        Assert.AreEqual("Credentialed", summary.Database.AuthenticationMode);
        Assert.IsTrue(summary.Database.ContainsPasswordKey);
        Assert.AreEqual("environment variables", summary.Database.OverrideSource);
        AssertDoesNotContain(serialized, connectionString, "summary");
        AssertDoesNotContain(serialized, SensitiveValue(), "summary");
        AssertDoesNotContain(serialized, "j08_user", "summary");
    }

    [TestMethod]
    public void J08_ConfigSummary_RedactsSensitiveKeys()
    {
        var values = new[]
        {
            new ConfigValueInput("Ai:ApiKey", SensitiveValue(), "local file"),
            new ConfigValueInput("Jwt:SigningKey", SensitiveValue(), "user secrets"),
            new ConfigValueInput("ConnectionStrings:IronDeveloperDb", BuildIntegratedConnectionString("IronDeveloper_J08"), "local file"),
            new ConfigValueInput("Weaviate:ApiKey", SensitiveValue(), "environment variables"),
            new ConfigValueInput("GitHub:Token", SensitiveValue(), "environment variables")
        };

        var summary = BuildSummary(values);
        var serialized = Serialize(summary);

        AssertRedacted(summary, "Ai:ApiKey");
        AssertRedacted(summary, "Jwt:SigningKey");
        AssertRedacted(summary, "ConnectionStrings:IronDeveloperDb");
        AssertRedacted(summary, "Weaviate:ApiKey");
        AssertRedacted(summary, "GitHub:Token");
        AssertDoesNotContain(serialized, SensitiveValue(), "summary");
        AssertDoesNotContain(serialized, BuildIntegratedConnectionString("IronDeveloper_J08"), "summary");
    }

    [TestMethod]
    public void J08_ConfigSummary_RedactsUserPaths()
    {
        var windowsPath = WindowsUserPath("Robert", ".irondev", "workspaces");
        var linuxPath = LinuxHomePath("rob", ".irondev", "evidence");
        var macPath = MacUserPath("rob", ".irondev", "logs");

        var summary = BuildSummary(
            values: [],
            roots:
            [
                new RootConfigInput(ConfigRootKind.WorkspaceRoot, "LocalTest:WorkspaceRoot", windowsPath),
                new RootConfigInput(ConfigRootKind.EvidenceRoot, "DisposableBuild:EvidenceRoot", linuxPath),
                new RootConfigInput(ConfigRootKind.LogsRoot, "LocalTest:LogsRoot", macPath)
            ]);
        var serialized = Serialize(summary);

        foreach (var root in summary.Roots.Entries)
            StringAssert.Contains(root.RedactedPath, "<user>");

        AssertDoesNotContain(serialized, "Robert", "summary");
        AssertDoesNotContain(serialized, "rob", "summary");
        AssertDoesNotContain(serialized, windowsPath, "summary");
        AssertDoesNotContain(serialized, linuxPath, "summary");
        AssertDoesNotContain(serialized, macPath, "summary");
    }

    [TestMethod]
    public void J08_ConfigSummary_ReportsLocalOverridePresenceWithoutContents()
    {
        const string localOverrideContents = "local-override-file-content-must-not-print";
        var summary = BuildSummary(
            values:
            [
                new ConfigValueInput("J08:LocalOverrideProbeSecret", localOverrideContents, "appsettings.Development.Local.json")
            ],
            sources:
            [
                new ConfigSourceInput("appsettings.json", ConfigSourceStatus.Loaded, 0),
                new ConfigSourceInput("appsettings.Development.json", ConfigSourceStatus.Loaded, 1),
                new ConfigSourceInput("appsettings.Development.Local.json", ConfigSourceStatus.Loaded, 2),
                new ConfigSourceInput("environment variables", ConfigSourceStatus.Available, 3)
            ]);
        var serialized = Serialize(summary);

        Assert.AreEqual(ConfigSourceStatus.Loaded, summary.Sources.Single(source =>
            source.Name == "appsettings.Development.Local.json").Status);
        AssertDoesNotContain(serialized, localOverrideContents, "summary");
        StringAssert.Contains(serialized, "appsettings.Development.Local.json");
    }

    [TestMethod]
    public void J08_ConfigSummary_EnvironmentVariablesStillWin()
    {
        var summary = BuildSummary([
            new ConfigValueInput("ConnectionStrings:IronDeveloperDb", BuildIntegratedConnectionString("IronDeveloper_LocalFile"), "appsettings.Development.Local.json"),
            new ConfigValueInput("ConnectionStrings:IronDeveloperDb", BuildIntegratedConnectionString("IronDeveloper_Environment"), "environment variables")
        ]);

        Assert.AreEqual("IronDeveloper_Environment", summary.Database.DatabaseName);
        Assert.AreEqual("environment variables", summary.Database.OverrideSource);
        Assert.IsFalse(summary.Database.ContainsPasswordKey);
        Assert.AreEqual("Integrated", summary.Database.AuthenticationMode);
    }

    [TestMethod]
    public void J08_ConfigSummary_ReportsRootSafetyStatusAsNotEvaluatedWithoutJ10()
    {
        var path = WindowsUserPath("Robert", ".irondev", "evidence");
        var summary = BuildSummary(
            values: [],
            roots:
            [
                new RootConfigInput(ConfigRootKind.EvidenceRoot, "DisposableBuild:EvidenceRoot", path)
            ]);
        var root = summary.Roots.Entries.Single();
        var serialized = Serialize(summary);

        Assert.IsTrue(root.Configured);
        Assert.AreEqual(ConfigRootSafetyStatus.NotEvaluated, root.Safety);
        StringAssert.Contains(root.NextSafeAction, "Run the root safety validator");
        StringAssert.Contains(root.RedactedPath, "<user>");
        AssertDoesNotContain(serialized, path, "summary");
    }

    [TestMethod]
    public void J08_ConfigSummary_CanReportSuppliedRootSafetyWithoutRevalidatingIt()
    {
        var summary = BuildSummary(
            values: [],
            roots:
            [
                new RootConfigInput(
                    ConfigRootKind.WorkspaceRoot,
                    "LocalTest:WorkspaceRoot",
                    WindowsUserPath("Robert", ".irondev", "workspaces"),
                    new ConfigRootSafetyEvaluation(
                        ConfigRootSafetyStatus.Unsafe,
                        "UnderRepositoryRoot",
                        "Configure a dedicated root outside the source repository."))
            ]);
        var root = summary.Roots.Entries.Single();

        Assert.AreEqual(ConfigRootSafetyStatus.Unsafe, root.Safety);
        Assert.AreEqual("UnderRepositoryRoot", root.ReasonCode);
        Assert.AreEqual("Configure a dedicated root outside the source repository.", root.NextSafeAction);
        StringAssert.Contains(root.RedactedPath, "<user>");
    }

    [TestMethod]
    public void J08_ConfigSummary_FailsSafeOnUnknownSensitiveValue()
    {
        var summary = BuildSummary([
            new ConfigValueInput("SomeService:SuperSecretThing", SensitiveValue(), "local file")
        ]);
        var serialized = Serialize(summary);

        AssertRedacted(summary, "SomeService:SuperSecretThing");
        AssertDoesNotContain(serialized, SensitiveValue(), "summary");
    }

    [TestMethod]
    public void J08_ConfigSummary_NoAuthorityLanguage()
    {
        var summary = BuildSummary([
            new ConfigValueInput("FeatureFlags:SkeletonApply:Enabled", "false", "appsettings.json")
        ]);
        var serialized = Serialize(summary);

        foreach (var marker in ForbiddenAuthorityMarkers())
            AssertDoesNotContain(serialized, marker, "summary");

        StringAssert.Contains(summary.BoundaryStatement, "diagnostic evidence for a human");
        StringAssert.Contains(summary.FeatureFlags.BoundaryStatement, "A configured flag is not permission.");
    }

    [TestMethod]
    public void J08_ReceiptStatesRedactionBoundary()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "J08_FAIL_SAFE_CONFIG_SUMMARY.md");

        StringAssert.Contains(receipt, "A config summary is diagnostic evidence for a human.");
        StringAssert.Contains(receipt, "It is not approval, authority, policy satisfaction, root safety proof, or permission to mutate anything.");
        StringAssert.Contains(receipt, "If redaction is uncertain, the value is redacted.");
        StringAssert.Contains(receipt, "Debug convenience loses to secret safety.");
    }

    [TestMethod]
    public void J08_NoBootstrapOrMutationSurfaceAdded()
    {
        var production = string.Join(
            Environment.NewLine,
            ReadRepositoryFile("IronDev.Core", "Configuration", "RedactedConfigSummaryModels.cs"),
            ReadRepositoryFile("IronDev.Core", "Configuration", "RedactedConfigSummaryService.cs"));

        foreach (var marker in ForbiddenProductionMarkers())
            AssertDoesNotContain(production, marker, "J08 production source");
    }

    private static RedactedConfigSummary BuildSummary(
        IReadOnlyList<ConfigValueInput> values,
        IReadOnlyList<ConfigSourceInput>? sources = null,
        IReadOnlyList<RootConfigInput>? roots = null)
    {
        var service = new RedactedConfigSummaryService();
        return service.Build(new RedactedConfigSummaryRequest
        {
            EnvironmentName = "Development",
            IsDevelopment = true,
            IsProductionLike = false,
            Sources = sources ??
            [
                new ConfigSourceInput("appsettings.json", ConfigSourceStatus.Loaded, 0),
                new ConfigSourceInput("appsettings.Development.json", ConfigSourceStatus.Loaded, 1)
            ],
            Values = values,
            Roots = roots ?? [],
            FeatureFlags =
            [
                new FeatureFlagInput("SkeletonApply.Enabled", "false", "appsettings.json")
            ]
        });
    }

    private static string BuildIntegratedConnectionString(string databaseName) =>
        string.Join(
            ';',
            @"Server=(localdb)\MSSQLLocalDB",
            "Database=" + databaseName,
            "Integrated Security=True",
            "Encrypt=True",
            "TrustServerCertificate=True") + ';';

    private static string BuildCredentialedConnectionString(string databaseName, string userId, string credential) =>
        string.Join(
            ';',
            @"Server=(localdb)\MSSQLLocalDB",
            "Database=" + databaseName,
            "User Id=" + userId,
            string.Concat("Pass", "word=", credential),
            "Encrypt=True",
            "TrustServerCertificate=True") + ';';

    private static string WindowsUserPath(string user, params string[] segments) =>
        string.Join(
            Path.DirectorySeparatorChar,
            new[] { string.Concat("C:", Path.DirectorySeparatorChar, "Users"), user }.Concat(segments));

    private static string LinuxHomePath(string user, params string[] segments) =>
        "/" + string.Join('/', new[] { "home", user }.Concat(segments));

    private static string MacUserPath(string user, params string[] segments) =>
        "/" + string.Join('/', new[] { "Users", user }.Concat(segments));

    private static string SensitiveValue() => string.Concat("sensitive", "-j08", "-value");

    private static void AssertRedacted(RedactedConfigSummary summary, string key)
    {
        var value = summary.RedactedValues.Single(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(RedactedConfigSummaryService.RedactedValue, value.Value);
        Assert.AreEqual(RedactedConfigValueVisibility.Redacted, value.Visibility);
    }

    private static IReadOnlyList<string> ForbiddenAuthorityMarkers() =>
    [
        "Approved",
        "Authorized",
        "PolicySatisfied",
        "SafeToApply",
        "ReadyToMutate",
        "CanDeploy"
    ];

    private static IReadOnlyList<string> ForbiddenProductionMarkers() =>
    [
        "CREATE DATABASE",
        "MigrationBuilder",
        "docker compose",
        "weaviate up",
        "ControlledSourceApply",
        "AcceptedApproval",
        "Release",
        "Deploy"
    ];

    private static string Serialize(RedactedConfigSummary summary) =>
        JsonSerializer.Serialize(summary);

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
}

using System.Text.Json;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("Weaviate")]
[TestCategory("Auth")]
[TestCategory("ProductionConfig")]
[TestCategory("SecretSafety")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class WeaviateAuthProdConfigBoundaryTests
{
    private const string ReceiptRelativePath = "Docs/receipts/H14_WEAVIATE_AUTH_PROD_CONFIG_TESTS.md";
    private const string H13ReceiptRelativePath = "Docs/receipts/H13_WEAVIATE_REBUILD_COMMAND_HARDENING.md";
    private const string C10ReceiptRelativePath = "Docs/receipts/C10_WEAVIATE_PRODUCTION_AUTH_CONFIG.md";
    private const string WeaviateServiceRelativePath = "IronDev.Infrastructure/Services/SemanticMemory/WeaviateSemanticMemoryService.cs";
    private const string CodeIntelligenceRegistrationRelativePath = "IronDev.Infrastructure/DependencyInjection/CodeIntelligenceServiceCollectionExtensions.cs";
    private const string RebuildModelsRelativePath = "IronDev.Core/KnowledgeCompiler/SemanticIndexRebuildModels.cs";
    private const string EnvironmentDtoRelativePath = "IronDev.Core/Models/EnvironmentInfoDto.cs";
    private const string ProgramRelativePath = "IronDev.Api/Program.cs";
    private const string SafeConfigurationValue = "fake-h14-config-key-for-tests-only";
    private const string SafeEnvironmentValue = "fake-h14-env-key-for-tests-only";
    private const string SensitiveTestValue = "fake-h14-sensitive-value-for-tests-only";

    private static readonly string[] ProductionLikeEnvironments = ["Production", "Staging", "UAT"];

    private static readonly string[] ScannedConfigFiles =
    [
        "IronDev.Api/appsettings.json",
        "IronDev.Api/appsettings.Development.json",
        "IronDev.Api/appsettings.LocalTest.json",
        "IronDev.IntegrationTests/appsettings.Test.json",
        "IronDev.IntegrationTests.Api/appsettings.Test.json"
    ];

    [TestMethod]
    public void WeaviateAuth_ProductionEnabledRequiresHttpsRemoteEndpointAndApiKey()
    {
        foreach (var environmentName in ProductionLikeEnvironments)
        {
            var missingKey = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid"));

            AssertInvalidWithIssue(missingKey, "WeaviateProductionApiKeyRequired");

            var httpEndpoint = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "http://weaviate.example.invalid"),
                ("Weaviate:ApiKey", SafeConfigurationValue));

            AssertInvalidWithIssue(httpEndpoint, "WeaviateProductionEndpointMustUseHttps");

            var localhostEndpoint = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://localhost:8080"),
                ("Weaviate:ApiKey", SafeConfigurationValue));

            AssertInvalidWithIssue(localhostEndpoint, "WeaviateProductionEndpointCannotBeLocalhost");

            var placeholderKey = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid"),
                ("Weaviate:ApiKey", "your-api-key-here"));

            AssertInvalidWithIssue(placeholderKey, "WeaviateApiKeyPlaceholder");

            var shortKey = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid"),
                ("Weaviate:ApiKey", "fake"));

            AssertInvalidWithIssue(shortKey, "WeaviateApiKeyTooShort");

            var safe = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid"),
                ("Weaviate:ApiKey", SafeConfigurationValue));

            Assert.IsTrue(safe.Validation.Valid, $"{environmentName} should accept HTTPS remote Weaviate with a safe key source.");
            Assert.AreEqual(WeaviateEndpointClassification.Remote, safe.Validation.EndpointClassification);
            Assert.AreEqual(WeaviateAuthSource.Configuration, safe.Validation.ApiKeySource);
        }
    }

    [TestMethod]
    public void WeaviateAuth_DefaultEnvironmentIsProductionLike()
    {
        var resolution = WeaviateAuthConfigValidator.Resolve(
            Configuration(
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid")),
            environmentName: null,
            _ => null);

        Assert.IsFalse(resolution.Validation.Valid);
        Assert.IsTrue(resolution.Validation.ProductionLikeEnvironment);
        Assert.AreEqual("Production", resolution.Validation.EnvironmentName);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateProductionApiKeyRequired");
    }

    [TestMethod]
    public void WeaviateAuth_DevelopmentAndLocalTestAllowAnonymousLocalhostOnly()
    {
        foreach (var environmentName in new[] { "Development", "Test", "LocalTest" })
        {
            var localAnonymous = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "http://localhost:8080"));

            Assert.IsTrue(localAnonymous.Validation.Valid, $"{environmentName} should allow anonymous localhost posture only.");
            Assert.AreEqual(WeaviateEndpointClassification.Localhost, localAnonymous.Validation.EndpointClassification);
            Assert.AreEqual(WeaviateAuthSource.Missing, localAnonymous.Validation.ApiKeySource);

            var nonLocalHttp = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "http://weaviate.example.invalid"),
                ("Weaviate:ApiKey", SafeConfigurationValue));

            AssertInvalidWithIssue(nonLocalHttp, "WeaviateNonLocalEndpointMustUseHttps");

            var nonLocalWithoutKey = Resolve(
                environmentName,
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid"));

            AssertInvalidWithIssue(nonLocalWithoutKey, "WeaviateNonLocalEndpointRequiresApiKey");
        }

        var productionLocalhost = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "http://localhost:8080"));

        Assert.IsFalse(productionLocalhost.Validation.Valid);
        CollectionAssert.Contains(productionLocalhost.Validation.Issues.ToArray(), "WeaviateProductionEndpointCannotBeLocalhost");
        CollectionAssert.Contains(productionLocalhost.Validation.Issues.ToArray(), "WeaviateProductionEndpointMustUseHttps");
        CollectionAssert.Contains(productionLocalhost.Validation.Issues.ToArray(), "WeaviateProductionApiKeyRequired");
    }

    [TestMethod]
    public void WeaviateAuth_ApiKeySourceDoesNotLeakSecretMaterial()
    {
        var environmentResolution = WeaviateAuthConfigValidator.Resolve(
            Configuration(
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.invalid")),
            "Production",
            name => string.Equals(name, WeaviateAuthConfigValidator.IronDevWeaviateApiKeyEnvironmentVariableName, StringComparison.Ordinal)
                ? SafeEnvironmentValue
                : null);

        Assert.IsTrue(environmentResolution.Validation.Valid);
        Assert.AreEqual(WeaviateAuthSource.IronDevWeaviateApiKeyEnvironment, environmentResolution.Validation.ApiKeySource);
        AssertNoSecretLeak(environmentResolution.Validation.ToString(), SafeEnvironmentValue, "validation result");
        AssertNoSecretLeak(environmentResolution.ToString(), SafeEnvironmentValue, "resolution result");

        var configurationResolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://weaviate.example.invalid"),
            ("Weaviate:ApiKey", SafeConfigurationValue));

        Assert.IsTrue(configurationResolution.Validation.Valid);
        Assert.AreEqual(WeaviateAuthSource.Configuration, configurationResolution.Validation.ApiKeySource);
        AssertNoSecretLeak(configurationResolution.Validation.ToString(), SafeConfigurationValue, "configuration validation result");
        AssertNoSecretLeak(configurationResolution.ToString(), SafeConfigurationValue, "configuration resolution result");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            WeaviateAuthConfigValidator.ResolveOptionsOrThrow(
                Configuration(
                    ("Weaviate:Enabled", "true"),
                    ("Weaviate:Endpoint", "https://weaviate.example.invalid"),
                    ("Weaviate:ApiKey", "fake")),
                "Production",
                _ => null));

        Assert.AreEqual(WeaviateAuthConfigValidator.StartupValidationFailedMessage, exception.Message);
        AssertNoSecretLeak(exception.ToString(), "fake", "startup exception");

        using var temp = new TemporaryDirectory();
        var json = JsonSerializer.Serialize(new
        {
            Weaviate = new
            {
                Enabled = true,
                Endpoint = "https://weaviate.example.invalid",
                ApiKey = SafeConfigurationValue
            }
        });
        File.WriteAllText(Path.Combine(temp.Path, "appsettings.json"), json);

        var committedConfiguration = new ConfigurationBuilder()
            .SetBasePath(temp.Path)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var committedResolution = WeaviateAuthConfigValidator.Resolve(committedConfiguration, "Production", _ => null);

        AssertInvalidWithIssue(committedResolution, "WeaviateApiKeyCommittedAppsettingsForbidden");
        AssertNoSecretLeak(committedResolution.Validation.ToString(), SafeConfigurationValue, "committed appsettings validation");
    }

    [TestMethod]
    public void WeaviateAuth_CommittedConfigFilesContainNoApiKeyMaterial()
    {
        foreach (var relativePath in ScannedConfigFiles)
        {
            var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
                continue;

            var source = File.ReadAllText(absolutePath);
            AssertDoesNotContain(source, "your-api-key-here", relativePath);
            AssertDoesNotContain(source, "local-dev-key", relativePath);
            AssertDoesNotContain(source, "weaviate-api-key", relativePath);
            AssertDoesNotContain(source, SafeConfigurationValue, relativePath);
            AssertDoesNotContain(source, SafeEnvironmentValue, relativePath);

            using var document = JsonDocument.Parse(source);
            if (document.RootElement.TryGetProperty("Weaviate", out var weaviate) &&
                weaviate.TryGetProperty("ApiKey", out var apiKey))
            {
                Assert.IsTrue(string.IsNullOrWhiteSpace(apiKey.GetString()), $"{relativePath} must not contain Weaviate:ApiKey material.");
            }
        }
    }

    [TestMethod]
    public void WeaviateAuth_EnvironmentEndpointDoesNotExposeApiKey()
    {
        var environmentDto = ReadRepositoryFile(EnvironmentDtoRelativePath);
        var program = ReadRepositoryFile(ProgramRelativePath);
        var endpoint = ExtractEndpointChain(program, "app.MapGet(\"/api/environment\"");

        StringAssert.Contains(environmentDto, "WeaviatePrefix");
        AssertDoesNotContain(environmentDto, "ApiKey", EnvironmentDtoRelativePath);
        AssertDoesNotContain(environmentDto, "IRONDEV_WEAVIATE_API_KEY", EnvironmentDtoRelativePath);
        AssertDoesNotContain(endpoint, "Weaviate:ApiKey", "/api/environment endpoint");
        AssertDoesNotContain(endpoint, "IRONDEV_WEAVIATE_API_KEY", "/api/environment endpoint");
        AssertDoesNotContain(endpoint, "ApiKey", "/api/environment endpoint");
    }

    [TestMethod]
    public void WeaviateAuth_RebuildResultsDoNotExposeApiKey()
    {
        var plan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true);

        var unsafeMessage = "Weaviate startup failed with api key " + SensitiveTestValue + " and " + string.Concat("Bearer ", SensitiveTestValue);
        var result = SemanticIndexRebuildGuard.Failed(
            plan,
            DateTime.UtcNow,
            runId: string.Empty,
            processedDocuments: 0,
            SemanticIndexRebuildBlockReason.WeaviateUnavailable,
            unsafeMessage);

        Assert.AreEqual("[redacted rebuild error]", result.ErrorMessage);
        AssertNoSecretLeak(result.ToString(), SensitiveTestValue, "rebuild result");
        AssertNoSecretLeak(result.ErrorMessage, SensitiveTestValue, "rebuild error");
        AssertDoesNotContain(result.ErrorMessage, "api key", "rebuild error");
        AssertDoesNotContain(result.ErrorMessage, "Bearer", "rebuild error");
    }

    [TestMethod]
    public void WeaviateAuth_RebuildDoesNotBypassProductionValidation()
    {
        var invalidProduction = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "http://localhost:8080"));

        Assert.IsFalse(invalidProduction.Validation.Valid);
        CollectionAssert.Contains(invalidProduction.Validation.Issues.ToArray(), "WeaviateProductionEndpointCannotBeLocalhost");
        CollectionAssert.Contains(invalidProduction.Validation.Issues.ToArray(), "WeaviateProductionEndpointMustUseHttps");
        CollectionAssert.Contains(invalidProduction.Validation.Issues.ToArray(), "WeaviateProductionApiKeyRequired");

        var disabledPlan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: false);

        CollectionAssert.Contains(disabledPlan.BlockReasons.ToList(), SemanticIndexRebuildBlockReason.WeaviateDisabled);

        var registration = ReadRepositoryFile(CodeIntelligenceRegistrationRelativePath);
        var weaviateService = ReadRepositoryFile(WeaviateServiceRelativePath);
        var rebuildMethod = ExtractWeaviateResultRebuildMethod(weaviateService);

        StringAssert.Contains(registration, "WeaviateAuthConfigValidator.ResolveOptionsOrThrow(config)");
        StringAssert.Contains(registration, "sp.GetRequiredService<WeaviateOptions>()");
        StringAssert.Contains(weaviateService, "WeaviateOptions options");
        StringAssert.Contains(weaviateService, "_options.Enabled");

        AssertDoesNotContain(rebuildMethod, "IConfiguration", "Weaviate rebuild method");
        AssertDoesNotContain(rebuildMethod, "Environment.GetEnvironmentVariable", "Weaviate rebuild method");
        AssertDoesNotContain(rebuildMethod, "ResolveOptionsOrThrow", "Weaviate rebuild method");
        AssertDoesNotContain(rebuildMethod, "Weaviate:ApiKey", "Weaviate rebuild method");
        AssertDoesNotContain(rebuildMethod, WeaviateAuthConfigValidator.IronDevWeaviateApiKeyEnvironmentVariableName, "Weaviate rebuild method");
    }

    [TestMethod]
    public void WeaviateAuth_AuthenticatedIndexDoesNotGrantAuthority()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://weaviate.example.invalid"),
            ("Weaviate:ApiKey", SafeConfigurationValue));

        Assert.IsTrue(resolution.Validation.Valid);

        var plan = SemanticIndexRebuildGuard.BuildPlan(
            new SemanticIndexRebuildRequest { ProjectId = 42 },
            collectionName: "IronDevKnowledge",
            weaviateEnabled: true,
            sourceDocumentCount: 2,
            estimatedChunkCount: 4);

        var result = SemanticIndexRebuildGuard.Completed(
            plan,
            DateTime.UtcNow,
            runId: "semantic-index-run:h14",
            processedDocuments: 2);

        Assert.IsFalse(result.IsAuthorityGrant);
        Assert.IsFalse(result.GrantsApproval);
        Assert.IsFalse(result.GrantsPolicySatisfaction);
        Assert.IsFalse(result.GrantsSourceApplyAuthority);
        Assert.IsFalse(result.GrantsWorkflowContinuation);
        Assert.IsFalse(result.GrantsReleaseReadiness);
        Assert.IsFalse(result.GrantsDeploymentReadiness);

        var receipt = ReceiptText();
        AssertContainsAll(
            receipt,
            "authenticated vector index is still just an index",
            "authenticated Weaviate is not approval",
            "authenticated Weaviate is not policy satisfaction",
            "authenticated Weaviate is not source-apply authority",
            "authenticated Weaviate is not workflow continuation authority",
            "authenticated Weaviate is not release readiness",
            "authenticated Weaviate is not deployment readiness",
            "authenticated Weaviate does not validate indexed content",
            "authenticated vector recall is still recall");
    }

    [TestMethod]
    public void WeaviateAuth_DoesNotIntroduceDeploymentOrRuntimeChanges()
    {
        var root = RepositoryRoot();

        foreach (var relativeDirectory in new[]
        {
            "Database",
            "Scripts",
            "IronDev.Api",
            "tools/IronDev.Cli",
            "IronDev.TauriShell",
            ".github/workflows"
        })
        {
            var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            var h14Files = Directory
                .EnumerateFiles(directory, "*h14*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*H14*", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();

            Assert.AreEqual(0, h14Files.Length, $"H14 implementation file appeared in forbidden path {relativeDirectory}: {string.Join(", ", h14Files)}");
        }

        AssertFileDoesNotContain("Database/migrations.json", "h14");
        AssertFileDoesNotContain("Database/apply-migrations.ps1", "h14");
        AssertFileDoesNotContain("Database/verify-migrations.ps1", "h14");
        AssertFileDoesNotContain("docker-compose.weaviate.yml", "h14");
        AssertFileDoesNotContain("Scripts/weaviate-dev.ps1", "h14");
    }

    [TestMethod]
    public void Receipt_RecordsAuthProdConfigScopeAndLimitations()
    {
        var receipt = ReceiptText();

        AssertContainsAll(
            receipt,
            "H14 verifies Weaviate auth/prod config boundaries.",
            "Outcome selected: `BoundaryTestsOnly`.",
            "H14 reuses the existing C10 production auth/config boundary.",
            "H14 checks the H13 rebuild boundary without changing rebuild behavior.",
            "H14 does not make Weaviate authoritative.",
            "H14 does not make index content authoritative.",
            "H14 does not validate indexed content.",
            "H14 does not change Weaviate rebuild behavior.",
            "H14 does not add a rebuild command.",
            "H14 does not change Docker compose.",
            "H14 does not change deployment config.",
            "H14 does not change API/CLI/UI behavior.",
            "H14 does not add SQL migrations.",
            "H14 does not alter tables.",
            "H14 does not add indexes.",
            "H14 does not alter stored procedures.",
            "H14 does not require live Weaviate in tests.",
            "H14 does not require Docker in tests.",
            "Production-like enabled Weaviate must fail closed without HTTPS, non-local endpoint, and safe API key.",
            "Local anonymous Weaviate is local/test posture only.",
            "API keys must not appear in validation output, startup exceptions, environment endpoints, rebuild results, or receipts.",
            "Weaviate auth protects the index only.",
            "An authenticated vector index is still just an index.",
            "Block H completion / review pass.");

        AssertContainsAll(
            ReadRepositoryFile(C10ReceiptRelativePath),
            "Weaviate authentication protects the retrieval index.");
        AssertContainsAll(
            ReadRepositoryFile(H13ReceiptRelativePath),
            "H14 owns Weaviate auth/prod config tests.");
        AssertContainsAll(
            ReadRepositoryFile(RebuildModelsRelativePath),
            "Vector recall is not authority.");
    }

    private static WeaviateAuthConfigResolution Resolve(string environmentName, params (string Key, string Value)[] values) =>
        WeaviateAuthConfigValidator.Resolve(Configuration(values), environmentName, _ => null);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    private static void AssertInvalidWithIssue(WeaviateAuthConfigResolution resolution, string issue)
    {
        Assert.IsFalse(resolution.Validation.Valid, $"Expected invalid Weaviate config with issue {issue}.");
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), issue);
    }

    private static string ExtractWeaviateResultRebuildMethod(string source)
    {
        const string startMarker = "public async Task<SemanticIndexRebuildResult> RebuildIndexAsync(";
        const string endMarker = "public async Task RebuildProjectAsync";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "Could not find result-returning Weaviate rebuild method.");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, "Could not find end of result-returning Weaviate rebuild method.");
        return source[start..end];
    }

    private static string ExtractEndpointChain(string source, string endpointStart)
    {
        var start = source.IndexOf(endpointStart, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Could not find endpoint starting with {endpointStart}.");

        var terminator = source.IndexOf(";\r\n", start, StringComparison.Ordinal);
        if (terminator < 0)
            terminator = source.IndexOf(";\n", start, StringComparison.Ordinal);

        Assert.IsTrue(terminator > start, $"Could not find endpoint terminator for {endpointStart}.");
        return source[start..terminator];
    }

    private static void AssertFileDoesNotContain(string relativePath, string marker)
    {
        var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
            return;

        AssertDoesNotContain(File.ReadAllText(absolutePath), marker, relativePath);
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static void AssertNoSecretLeak(string text, string value, string sourceName)
    {
        AssertDoesNotContain(text, value, sourceName);
        AssertDoesNotContain(text, "Weaviate:ApiKey", sourceName);
        AssertDoesNotContain(text, WeaviateAuthConfigValidator.IronDevWeaviateApiKeyEnvironmentVariableName, sourceName);
    }

    private static void AssertDoesNotContain(string text, string marker, string sourceName)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{marker}'.");
    }

    private static string ReceiptText() => ReadRepositoryFile(ReceiptRelativePath);

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "IronDev-H14-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

using System.Text.Json;
using IronDev.Core.Models;
using IronDev.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC10WeaviateProductionAuthConfigTests
{
    private const string ValidConfigurationKey = "configured-weaviate-api-key-for-c10-tests";
    private const string ValidEnvironmentKey = "environment-weaviate-api-key-for-c10-tests";
    private const string OtherEnvironmentKey = "other-weaviate-api-key-for-c10-tests";
    private const string PlaceholderKey = "your-api-key-here";

    private static readonly string[] ScannedConfigFiles =
    [
        "IronDev.Api/appsettings.json",
        "IronDev.Api/appsettings.Development.json",
        "IronDev.Api/appsettings.LocalTest.json",
        "IronDev.IntegrationTests/appsettings.Test.json",
        "IronDev.IntegrationTests.Api/appsettings.Test.json"
    ];

    [TestMethod]
    public void BlockC10_DisabledWeaviate_DoesNotRequireAuth()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "false"),
            ("Weaviate:Endpoint", ""));

        Assert.IsTrue(resolution.Validation.Valid);
        Assert.IsFalse(resolution.Validation.Enabled);
        Assert.AreEqual(WeaviateAuthSource.Missing, resolution.Validation.ApiKeySource);
    }

    [TestMethod]
    public void BlockC10_ProductionEnabledWeaviate_MissingApiKeyFailsClosed()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://weaviate.example.com"));

        Assert.IsFalse(resolution.Validation.Valid);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateProductionApiKeyRequired");
    }

    [TestMethod]
    public void BlockC10_ProductionEnabledWeaviate_ShortApiKeyFailsClosed()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://weaviate.example.com"),
            ("Weaviate:ApiKey", "short-key"));

        Assert.IsFalse(resolution.Validation.Valid);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateApiKeyTooShort");
    }

    [TestMethod]
    public void BlockC10_ProductionEnabledWeaviate_PlaceholderApiKeyFailsClosed()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://weaviate.example.com"),
            ("Weaviate:ApiKey", PlaceholderKey));

        Assert.IsFalse(resolution.Validation.Valid);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateApiKeyPlaceholder");
        AssertDoesNotContain(resolution.Validation.ToString(), PlaceholderKey, "Weaviate validation metadata");
    }

    [TestMethod]
    public void BlockC10_ProductionEnabledWeaviate_HttpEndpointFailsClosed()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "http://weaviate.example.com"),
            ("Weaviate:ApiKey", ValidConfigurationKey));

        Assert.IsFalse(resolution.Validation.Valid);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateProductionEndpointMustUseHttps");
    }

    [TestMethod]
    public void BlockC10_ProductionEnabledWeaviate_LocalhostEndpointFailsClosed()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://localhost:8080"),
            ("Weaviate:ApiKey", ValidConfigurationKey));

        Assert.IsFalse(resolution.Validation.Valid);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateProductionEndpointCannotBeLocalhost");
    }

    [TestMethod]
    public void BlockC10_DevelopmentLocalhostWeaviate_AllowsAnonymousLocalPosture()
    {
        var resolution = Resolve(
            "Development",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "http://localhost:8080"));

        Assert.IsTrue(resolution.Validation.Valid);
        Assert.AreEqual(WeaviateEndpointClassification.Localhost, resolution.Validation.EndpointClassification);
        Assert.AreEqual(WeaviateAuthSource.Missing, resolution.Validation.ApiKeySource);
    }

    [TestMethod]
    public void BlockC10_LocalTestLocalhostWeaviate_AllowsAnonymousLocalPosture()
    {
        var resolution = Resolve(
            "LocalTest",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "http://127.0.0.1:8080"));

        Assert.IsTrue(resolution.Validation.Valid);
        Assert.AreEqual(WeaviateEndpointClassification.Localhost, resolution.Validation.EndpointClassification);
        Assert.AreEqual(WeaviateAuthSource.Missing, resolution.Validation.ApiKeySource);
    }

    [TestMethod]
    public void BlockC10_ConfigurationApiKey_IsAcceptedFromSafeConfiguration()
    {
        var resolution = Resolve(
            "Production",
            ("Weaviate:Enabled", "true"),
            ("Weaviate:Endpoint", "https://weaviate.example.com"),
            ("Weaviate:ApiKey", ValidConfigurationKey));

        Assert.IsTrue(resolution.Validation.Valid);
        Assert.AreEqual(WeaviateAuthSource.Configuration, resolution.Validation.ApiKeySource);
        Assert.AreEqual(ValidConfigurationKey, resolution.Options.ApiKey);
        AssertDoesNotContain(resolution.Validation.ToString(), ValidConfigurationKey, "Weaviate validation metadata");
    }

    [TestMethod]
    public void BlockC10_IronDevWeaviateApiKeyEnvironmentFallback_IsAccepted()
    {
        var resolution = WeaviateAuthConfigValidator.Resolve(
            Configuration(
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.com")),
            "Production",
            name => string.Equals(name, WeaviateAuthConfigValidator.IronDevWeaviateApiKeyEnvironmentVariableName, StringComparison.Ordinal)
                ? ValidEnvironmentKey
                : null);

        Assert.IsTrue(resolution.Validation.Valid);
        Assert.AreEqual(WeaviateAuthSource.IronDevWeaviateApiKeyEnvironment, resolution.Validation.ApiKeySource);
        Assert.AreEqual(ValidEnvironmentKey, resolution.Options.ApiKey);
        AssertDoesNotContain(resolution.Validation.ToString(), ValidEnvironmentKey, "Weaviate validation metadata");
    }

    [TestMethod]
    public void BlockC10_ConfigurationApiKey_TakesPrecedenceOverIronDevEnvironmentFallback()
    {
        var resolution = WeaviateAuthConfigValidator.Resolve(
            Configuration(
                ("Weaviate:Enabled", "true"),
                ("Weaviate:Endpoint", "https://weaviate.example.com"),
                ("Weaviate:ApiKey", ValidConfigurationKey)),
            "Production",
            name => string.Equals(name, WeaviateAuthConfigValidator.IronDevWeaviateApiKeyEnvironmentVariableName, StringComparison.Ordinal)
                ? OtherEnvironmentKey
                : null);

        Assert.IsTrue(resolution.Validation.Valid);
        Assert.AreEqual(WeaviateAuthSource.Configuration, resolution.Validation.ApiKeySource);
        Assert.AreEqual(ValidConfigurationKey, resolution.Options.ApiKey);
    }

    [TestMethod]
    public void BlockC10_CommittedAppsettingsWeaviateApiKey_FailsClosed()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            $$"""
            {
              "Weaviate": {
                "Enabled": true,
                "Endpoint": "https://weaviate.example.com",
                "ApiKey": "{{ValidConfigurationKey}}"
              }
            }
            """);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(temp.Path)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var resolution = WeaviateAuthConfigValidator.Resolve(configuration, "Production", _ => null);

        Assert.IsFalse(resolution.Validation.Valid);
        CollectionAssert.Contains(resolution.Validation.Issues.ToArray(), "WeaviateApiKeyCommittedAppsettingsForbidden");
        AssertDoesNotContain(resolution.Validation.ToString(), ValidConfigurationKey, "Weaviate validation metadata");
    }

    [TestMethod]
    public void BlockC10_StartupException_DoesNotExposeApiKey()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            WeaviateAuthConfigValidator.ResolveOptionsOrThrow(
                Configuration(
                    ("Weaviate:Enabled", "true"),
                    ("Weaviate:Endpoint", "https://weaviate.example.com"),
                    ("Weaviate:ApiKey", "short-key")),
                "Production",
                _ => null));

        Assert.AreEqual(WeaviateAuthConfigValidator.StartupValidationFailedMessage, exception.Message);
        AssertDoesNotContain(exception.ToString(), "short-key", "Weaviate startup exception");
    }

    [TestMethod]
    public void BlockC10_CommittedAppsettingsFiles_DoNotContainWeaviateApiKeyMaterial()
    {
        foreach (var relativePath in ScannedConfigFiles)
        {
            var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
                continue;

            var source = File.ReadAllText(absolutePath);
            AssertDoesNotContain(source, PlaceholderKey, relativePath);
            AssertDoesNotContain(source, "local-dev-key", relativePath);

            using var document = JsonDocument.Parse(source);
            if (document.RootElement.TryGetProperty("Weaviate", out var weaviate) &&
                weaviate.TryGetProperty("ApiKey", out var apiKey))
            {
                Assert.IsTrue(string.IsNullOrWhiteSpace(apiKey.GetString()), $"{relativePath} must not contain Weaviate:ApiKey material.");
            }
        }
    }

    [TestMethod]
    public void BlockC10_WeaviateSemanticMemoryService_UsesConfiguredApiKey()
    {
        var service = ReadRepositoryFile("IronDev.Infrastructure", "Services", "SemanticMemory", "WeaviateSemanticMemoryService.cs");
        var initializationBlock = ExtractBetween(service, "private async Task EnsureInitializedAsync()", "public async Task QueueIndexAsync");

        StringAssert.Contains(initializationBlock, "builder.WithCredentials(Weaviate.Client.Auth.ApiKey(_options.ApiKey))");
        StringAssert.Contains(initializationBlock, "Failed to initialize Weaviate client. Check endpoint and authentication configuration.");
        AssertDoesNotContain(initializationBlock, "ex.Message", "Weaviate initialization block");
    }

    [TestMethod]
    public void BlockC10_EnvironmentEndpoint_DoesNotExposeWeaviateApiKey()
    {
        var environmentDto = ReadRepositoryFile("IronDev.Core", "Models", "EnvironmentInfoDto.cs");
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var endpoint = ExtractEndpointChain(program, "app.MapGet(\"/api/environment\"");

        StringAssert.Contains(environmentDto, "WeaviatePrefix");
        AssertDoesNotContain(environmentDto, "ApiKey", "EnvironmentInfoDto.cs");
        AssertDoesNotContain(endpoint, "Weaviate:ApiKey", "/api/environment endpoint");
        AssertDoesNotContain(endpoint, "IRONDEV_WEAVIATE_API_KEY", "/api/environment endpoint");
    }

    [TestMethod]
    public void BlockC10_DoesNotChangeSemanticRankingMemoryPromotionSqlFrontendOrGovernanceAuthority()
    {
        var optionsSource = ReadRepositoryFile("IronDev.Core", "Models", "WeaviateOptions.cs");
        var semanticMemoryService = ReadRepositoryFile("IronDev.Infrastructure", "Services", "SemanticMemory", "WeaviateSemanticMemoryService.cs");
        var receipt = ReadRepositoryFile("Docs", "receipts", "C10_WEAVIATE_PRODUCTION_AUTH_CONFIG.md");

        foreach (var marker in new[]
        {
            "PromoteMemory",
            "AcceptedMemory",
            "PolicySatisfaction",
            "Approval",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "ContinueWorkflow"
        })
        {
            AssertDoesNotContain(optionsSource, marker, "WeaviateOptions.cs");
            AssertDoesNotContain(semanticMemoryService, marker, "WeaviateSemanticMemoryService.cs");
        }

        StringAssert.Contains(receipt, "Weaviate authentication protects the retrieval index.");
        StringAssert.Contains(receipt, "It does not make Weaviate a source of truth");
    }

    private static WeaviateAuthConfigResolution Resolve(string environmentName, params (string Key, string Value)[] values) =>
        WeaviateAuthConfigValidator.Resolve(Configuration(values), environmentName, _ => null);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

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

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Could not find {startMarker}.");

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, $"Could not find {endMarker} after {startMarker}.");

        return source[start..end];
    }

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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "IronDev-C10-" + Guid.NewGuid().ToString("N"));
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

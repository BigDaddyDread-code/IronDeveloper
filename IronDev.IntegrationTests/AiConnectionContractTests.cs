using System.Text.Json;
using System.Net;
using IronDev.Api.Services;
using IronDev.Core.AiConnections;
using IronDev.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("SkeletonRun")]
public sealed class AiConnectionContractTests
{
    [TestMethod]
    public async Task ConnectionMetadata_DerivedFromAiConfig_IsNonSecret()
    {
        const string secret = "configured-secret-value";
        var service = Harness(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "OpenAI",
            ["Ai:Model"] = "gpt-4o",
            ["Ai:BaseUrl"] = "https://api.openai.com/v1?api_key=should-not-return",
            ["Ai:ApiKey"] = secret
        });

        var connection = (await service.ListAsync(tenantId: 3, userId: 7)).Single();
        var json = JsonSerializer.Serialize(connection);

        Assert.AreEqual(3, connection.TenantId);
        Assert.AreEqual("openai", connection.ProviderKind);
        Assert.AreEqual("https://api.openai.com", connection.ControlledEndpoint);
        Assert.IsTrue(connection.CredentialConfigured);
        Assert.AreEqual("Configured", connection.CredentialStatus);
        CollectionAssert.Contains(connection.AvailableModels.ToArray(), "gpt-4o");
        StringAssert.Contains(connection.Boundary, "never returned");
        Assert.IsFalse(json.Contains(secret, StringComparison.OrdinalIgnoreCase), "Credential values must never be returned.");
        Assert.IsFalse(json.Contains("api_key", StringComparison.OrdinalIgnoreCase), "Endpoint query strings must never be returned.");
        Assert.IsFalse(json.Contains("/v1", StringComparison.OrdinalIgnoreCase), "Endpoint path detail is not part of the controlled endpoint summary.");
    }

    [TestMethod]
    public async Task ConnectionMetadata_MissingOpenAiCredential_IsHonest()
    {
        var service = Harness(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "openai",
            ["Ai:Model"] = "gpt-4o"
        });

        var connection = (await service.ListAsync(tenantId: 3, userId: 7)).Single();

        Assert.IsFalse(connection.CredentialConfigured);
        Assert.AreEqual("Missing", connection.CredentialStatus);
        Assert.AreEqual("provider-default:openai", connection.ControlledEndpoint);
    }

    [TestMethod]
    public async Task CredentialLifecycle_IsWriteOnlyRedactedAndReflectedInMetadata()
    {
        const string secret = "configured-secret-value";
        var temp = Directory.CreateTempSubdirectory("irondev-ai-credentials-");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ai:Provider"] = "openai",
                    ["Ai:Model"] = "gpt-4o",
                    ["AiConnections:CredentialStorePath"] = temp.FullName
                })
                .Build();
            var store = new FileSystemAiConnectionCredentialStore(
                configuration,
                DataProtectionProvider.Create(temp.FullName));
            var catalog = new AiConnectionCatalogService(configuration, _ => null, store);
            var service = new AiConnectionCredentialService(store, catalog);

            var configure = await service.ConfigureAsync(
                tenantId: 3,
                userId: 7,
                connectionId: "deployment-default",
                new AiConnectionCredentialWriteRequest
                {
                    Credential = secret,
                    Reason = "manual test credential"
                });

            Assert.IsTrue(configure.Succeeded, configure.FailureReason);
            Assert.IsNotNull(configure.Connection);
            Assert.IsTrue(configure.Connection!.CredentialConfigured);
            Assert.AreEqual("Configured", configure.Connection.CredentialStatus);
            Assert.IsNotNull(configure.Connection.CredentialRotatedUtc);
            Assert.IsNull(configure.Connection.CredentialRevokedUtc);

            var configureJson = JsonSerializer.Serialize(configure);
            Assert.IsFalse(configureJson.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(ReadAllStoreText(temp.FullName).Contains(secret, StringComparison.OrdinalIgnoreCase));

            var revoke = await service.RevokeAsync(
                tenantId: 3,
                userId: 7,
                connectionId: "deployment-default",
                new AiConnectionCredentialRevokeRequest
                {
                    Reason = "manual revocation"
                });

            Assert.IsTrue(revoke.Succeeded, revoke.FailureReason);
            Assert.IsNotNull(revoke.Connection);
            Assert.IsFalse(revoke.Connection!.CredentialConfigured);
            Assert.AreEqual("Revoked", revoke.Connection.CredentialStatus);
            Assert.IsNull(revoke.Connection.CredentialRotatedUtc);
            Assert.IsNotNull(revoke.Connection.CredentialRevokedUtc);

            var revokeJson = JsonSerializer.Serialize(revoke);
            Assert.IsFalse(revokeJson.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(ReadAllStoreText(temp.FullName).Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ConnectionTest_UsesProtectedCredentialInternallyAndPersistsOnlyNonSecretHealth()
    {
        const string secret = "provider-test-secret";
        var temp = Directory.CreateTempSubdirectory("irondev-ai-test-");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "openai",
                ["Ai:Model"] = "gpt-4o",
                ["Ai:BaseUrl"] = "https://controlled.test",
                ["AiConnections:CredentialStorePath"] = Path.Combine(temp.FullName, "credentials"),
                ["AiConnections:HealthStorePath"] = Path.Combine(temp.FullName, "health")
            }).Build();
            var credentials = new FileSystemAiConnectionCredentialStore(configuration, DataProtectionProvider.Create(Path.Combine(temp.FullName, "keys")));
            await credentials.StoreAsync(3, "deployment-default", secret, 7, "test", CancellationToken.None);
            var health = new FileSystemAiConnectionTestHealthStore(configuration);
            var catalog = new AiConnectionCatalogService(configuration, _ => null, credentials, health);
            var handler = new RecordingHandler(HttpStatusCode.OK);
            var service = new AiConnectionTestService(catalog, credentials, health, configuration, new HttpClient(handler), _ => null);

            var outcome = await service.TestAsync(3, 7, "deployment-default");

            Assert.IsTrue(outcome.Succeeded, outcome.FailureReason);
            Assert.AreEqual("Passed", outcome.Status);
            Assert.AreEqual("https://controlled.test/v1/models", handler.RequestUri?.ToString());
            Assert.AreEqual("Bearer", handler.AuthorizationScheme);
            Assert.AreEqual(secret, handler.AuthorizationParameter, "The protected credential is available only at the provider-use boundary.");
            Assert.IsNotNull(outcome.Connection?.LastSuccessfulTestUtc);
            var serialized = JsonSerializer.Serialize(outcome) + ReadAllStoreText(Path.Combine(temp.FullName, "health"));
            Assert.IsFalse(serialized.Contains(secret, StringComparison.Ordinal), "Credential material must not enter outcomes or durable health.");
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task ConnectionTest_MissingRequiredCredentialFailsWithoutCallingTheProvider()
    {
        var temp = Directory.CreateTempSubdirectory("irondev-ai-test-missing-");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "openai",
                ["Ai:Model"] = "gpt-4o",
                ["Ai:BaseUrl"] = "https://controlled.test",
                ["AiConnections:CredentialStorePath"] = Path.Combine(temp.FullName, "credentials"),
                ["AiConnections:HealthStorePath"] = Path.Combine(temp.FullName, "health")
            }).Build();
            var credentials = new FileSystemAiConnectionCredentialStore(configuration, DataProtectionProvider.Create(Path.Combine(temp.FullName, "keys")));
            var health = new FileSystemAiConnectionTestHealthStore(configuration);
            var catalog = new AiConnectionCatalogService(configuration, _ => null, credentials, health);
            var handler = new RecordingHandler(HttpStatusCode.OK);
            var service = new AiConnectionTestService(catalog, credentials, health, configuration, new HttpClient(handler), _ => null);

            var outcome = await service.TestAsync(3, 7, "deployment-default");

            Assert.IsFalse(outcome.Succeeded);
            Assert.AreEqual("MissingCredential", outcome.Status);
            Assert.AreEqual(0, handler.CallCount);
            Assert.IsNotNull(outcome.Connection?.LastFailedTestUtc);
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public void ConnectionTest_TypedHttpClientRegistrationResolvesTheProductionConstructor()
    {
        var temp = Directory.CreateTempSubdirectory("irondev-ai-test-di-");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "fake",
                ["Ai:Model"] = "deterministic-test",
                ["AiConnections:CredentialStorePath"] = Path.Combine(temp.FullName, "credentials"),
                ["AiConnections:HealthStorePath"] = Path.Combine(temp.FullName, "health")
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(temp.FullName, "keys")));
            services.AddScoped<IAiConnectionCredentialStore, FileSystemAiConnectionCredentialStore>();
            services.AddScoped<IAiConnectionTestHealthStore, FileSystemAiConnectionTestHealthStore>();
            services.AddScoped<IAiConnectionCatalogService, AiConnectionCatalogService>();
            services.AddHttpClient<IAiConnectionTestService, AiConnectionTestService>();

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            using var scope = provider.CreateScope();
            Assert.IsInstanceOfType<AiConnectionTestService>(scope.ServiceProvider.GetRequiredService<IAiConnectionTestService>());
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public void AiConnectionsController_IsTenantScopedMetadataWithAdminCredentialWrites()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Api", "Controllers", "AiConnectionsController.cs"));

        StringAssert.Contains(source, "api/v1/ai-connections");
        StringAssert.Contains(source, "HttpGet");
        StringAssert.Contains(source, "HttpPut(\"{connectionId}/credential\")");
        StringAssert.Contains(source, "HttpPost(\"{connectionId}/credential/revoke\")");
        StringAssert.Contains(source, "HttpPost(\"{connectionId}/test\")");
        StringAssert.Contains(source, "CurrentUserContext");
        StringAssert.Contains(source, "TenantId");
        StringAssert.Contains(source, "SensitiveApiPolicy");
        StringAssert.Contains(source, "CanAdministerUsers");

        foreach (var forbidden in new[] { "HttpPatch", "HttpDelete", "ApiKey", "SecretReference", "CredentialReference" })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"AI connection routes must not expose {forbidden}.");
        }
    }

    private static AiConnectionCatalogService Harness(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new AiConnectionCatalogService(configuration, _ => null);
    }

    private static string ReadAllStoreText(string root)
    {
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}

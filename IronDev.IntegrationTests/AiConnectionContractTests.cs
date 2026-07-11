using System.Text.Json;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
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
    public void AiConnectionsController_IsReadOnlyTenantScopedMetadata()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Api", "Controllers", "AiConnectionsController.cs"));

        StringAssert.Contains(source, "api/v1/ai-connections");
        StringAssert.Contains(source, "HttpGet");
        StringAssert.Contains(source, "CurrentUserContext");
        StringAssert.Contains(source, "TenantId");
        StringAssert.Contains(source, "SensitiveApiPolicy");

        foreach (var forbidden in new[] { "HttpPost", "HttpPut", "HttpPatch", "HttpDelete", "ApiKey", "SecretReference", "CredentialReference" })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"V25-06 is a read-only non-secret metadata contract; controller must not expose {forbidden}.");
        }
    }

    private static AiConnectionCatalogService Harness(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new AiConnectionCatalogService(configuration, _ => null);
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
}

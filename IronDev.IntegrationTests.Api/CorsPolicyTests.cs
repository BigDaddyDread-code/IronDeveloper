using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class CorsPolicyTests : ApiTestBase
{
    private const string AllowedOrigin = "http://localhost:1420";
    private const string DisallowedOrigin = "https://evil.example";
    private const string TestJwtKey = "irondev-cors-test-jwt-key-not-from-committed-config-32chars";

    [TestMethod]
    public async Task AllowedConfiguredOrigin_ReceivesCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        AssertCorsOrigin(response, AllowedOrigin);
    }

    [TestMethod]
    public async Task DisallowedOrigin_DoesNotReceiveCorsAllowOriginHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", DisallowedOrigin);

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        AssertNoCorsOrigin(response);
    }

    [TestMethod]
    public async Task AllowedPreflight_SucceedsWithExpectedCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/environment");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, Content-Type");

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        AssertCorsOrigin(response, AllowedOrigin);
        AssertHeaderContains(response, "Access-Control-Allow-Methods", "GET");
        AssertHeaderContains(response, "Access-Control-Allow-Headers", "Authorization");
        AssertHeaderContains(response, "Access-Control-Allow-Headers", "Content-Type");
        Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [TestMethod]
    public async Task DisallowedPreflight_DoesNotProduceAllowOriginHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/environment");
        request.Headers.Add("Origin", DisallowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, Content-Type");

        var response = await Client.SendAsync(request);

        AssertNoCorsOrigin(response);
    }

    [TestMethod]
    public async Task AllowedOrigin_DoesNotMakeProtectedEndpointAnonymous()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/environment");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertCorsOrigin(response, AllowedOrigin);
    }

    [TestMethod]
    public async Task Health_RemainsReachableWhenCorsPolicyIsConfigured()
    {
        var response = await Client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Environment_RemainsProtectedWhenCorsPolicyIsConfigured()
    {
        var response = await Client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MissingCorsOrigins_AllowsNoBrowserOrigins()
    {
        using var factory = BuildFactoryWithCorsOrigins();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        AssertNoCorsOrigin(response);
    }

    [TestMethod]
    [DataRow("*")]
    [DataRow("https://*.example.com")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("example.com")]
    [DataRow("ftp://example.com")]
    [DataRow("https://example.com/")]
    [DataRow("https://example.com/path")]
    public void InvalidCorsOrigins_FailStartup(string origin)
    {
        AssertStartupFailsWithCorsIssue(origin);
    }

    [TestMethod]
    public void DuplicateCorsOrigins_FailStartup()
    {
        AssertStartupFailsWithCorsIssue("http://localhost:1420", "http://localhost:1420");
    }

    [TestMethod]
    public void ProductionLikeLocalhostOrigins_FailStartup()
    {
        using var factory = BuildFactoryWithCorsOrigins(["http://localhost:1420"], environmentName: "Production");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());
        StringAssert.Contains(exception.ToString(), "Production CORS configuration cannot include localhost origins");
    }

    private static void AssertStartupFailsWithCorsIssue(params string[] origins)
    {
        using var factory = BuildFactoryWithCorsOrigins(origins);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());
        StringAssert.Contains(exception.ToString(), "Cors");
    }

    private static WebApplicationFactory<Program> BuildFactoryWithCorsOrigins(
        string[]? origins = null,
        string environmentName = "Test")
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.UseSetting("Jwt:Issuer", "irondev-api");
                builder.UseSetting("Jwt:Audience", "irondev-client");
                builder.UseSetting("ConnectionStrings:IronDeveloperDb", "Server=DESKTOP-KFA0H13;Database=IronDeveloper_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;");
                builder.UseSetting("LocalTest:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"));
                builder.UseSetting("LocalTest:LogsRoot", Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));
                if (origins is not null)
                {
                    for (var index = 0; index < origins.Length; index++)
                        builder.UseSetting($"Cors:AllowedOrigins:{index}", origins[index]);
                }

                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = TestJwtKey
                    };

                    cfg.AddInMemoryCollection(settings);
                });
            });
    }

    private static void AssertCorsOrigin(HttpResponseMessage response, string expectedOrigin)
    {
        Assert.IsTrue(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values),
            "Expected Access-Control-Allow-Origin header.");
        Assert.AreEqual(expectedOrigin, values.Single());
    }

    private static void AssertNoCorsOrigin(HttpResponseMessage response)
    {
        Assert.IsFalse(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Disallowed origins must not receive Access-Control-Allow-Origin.");
    }

    private static void AssertHeaderContains(HttpResponseMessage response, string headerName, string expectedValue)
    {
        Assert.IsTrue(response.Headers.TryGetValues(headerName, out var values), $"Expected {headerName} header.");
        Assert.IsTrue(
            values.Any(value => value.Contains(expectedValue, StringComparison.OrdinalIgnoreCase)),
            $"Expected {headerName} to contain {expectedValue}.");
    }
}

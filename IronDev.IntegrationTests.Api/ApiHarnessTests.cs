using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using IronDev.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public class ApiHarnessTests : ApiTestBase
{
    [TestMethod]
    public async Task HealthEndpoint_ShouldReturnSuccess()
    {
        var response = await Client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("healthy", body.GetProperty("status").GetString());
        var workbench = body.GetProperty("workbench");
        Assert.AreEqual("0.1.0-preview.3", workbench.GetProperty("version").GetString());
        Assert.AreEqual("V1", workbench.GetProperty("mode").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(workbench.GetProperty("apiCommit").GetString()));
    }

    [TestMethod]
    public async Task ProtectedEndpoint_ShouldRejectAnonymousRequest()
    {
        // /api/auth/me requires a valid token.
        var response = await Client.GetAsync("/api/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task EnvironmentEndpoint_ShouldRejectAnonymousRequest()
    {
        var response = await Client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(responseText.Contains("IronDeveloper_Test", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(responseText.Contains("WorkspaceRoot", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(responseText.Contains("LogsRoot", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(responseText.Contains("DangerRealRepoWritesEnabled", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task EnvironmentEndpoint_ShouldReportIsolatedTestDatabaseForAuthenticatedRequest()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnvironmentInfoDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Test", body!.Environment);
        Assert.AreEqual("IronDeveloper_Test", body.Database);
        Assert.IsTrue(body.IsTestEnvironment);
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.WorkspaceRoot));
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.LogsRoot));
        Assert.AreEqual("0.1.0-preview.3", body.Workbench.Version);
        Assert.AreEqual("default", body.Workbench.PreviewId);
        Assert.AreEqual("V1", body.Workbench.Mode);
    }

    [TestMethod]
    public async Task ApiHost_ShouldStartSuccessfully()
    {
        // If the factory started and health returns 200, the host is up.
        var response = await Client.GetAsync("/health");
        Assert.IsTrue(response.IsSuccessStatusCode, "API host did not start successfully.");
    }
}

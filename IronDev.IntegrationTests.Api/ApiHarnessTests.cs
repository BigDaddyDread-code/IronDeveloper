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
    }

    [TestMethod]
    public async Task ProtectedEndpoint_ShouldRejectAnonymousRequest()
    {
        // /api/auth/me requires a valid token.
        var response = await Client.GetAsync("/api/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task EnvironmentEndpoint_ShouldReportIsolatedTestDatabase()
    {
        var response = await Client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnvironmentInfoDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual("Test", body!.Environment);
        Assert.AreEqual("IronDeveloper_Test", body.Database);
        Assert.IsTrue(body.IsTestEnvironment);
    }

    [TestMethod]
    public async Task ApiHost_ShouldStartSuccessfully()
    {
        // If the factory started and health returns 200, the host is up.
        var response = await Client.GetAsync("/health");
        Assert.IsTrue(response.IsSuccessStatusCode, "API host did not start successfully.");
    }
}

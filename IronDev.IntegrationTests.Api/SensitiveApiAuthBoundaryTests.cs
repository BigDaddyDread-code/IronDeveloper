using System.Net;
using System.Net.Http.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class SensitiveApiAuthBoundaryTests : ApiTestBase
{
    [DataTestMethod]
    [DataRow("/api/auth/me")]
    [DataRow("/api/auth/logout")]
    [DataRow("/api/environment")]
    [DataRow("/api/v1/tool-requests")]
    [DataRow("/api/v1/tool-gates/evaluations")]
    [DataRow("/api/v1/agent-runs")]
    [DataRow("/api/workflow/apply-preview/run-1/step-1")]
    public async Task SensitiveApiEndpoints_AnonymousRequestsAreNotSuccessful(string path)
    {
        var method = path.EndsWith("/logout", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/tool-requests", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/evaluations", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Post
                : HttpMethod.Get;
        using var request = new HttpRequestMessage(method, path);

        var response = await Client.SendAsync(request);

        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
            $"Anonymous request to {path} must not succeed. Actual: {response.StatusCode}.");
    }

    [TestMethod]
    public async Task Login_RemainsAnonymousButRateLimited()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Health_RemainsAnonymous()
    {
        var response = await Client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}

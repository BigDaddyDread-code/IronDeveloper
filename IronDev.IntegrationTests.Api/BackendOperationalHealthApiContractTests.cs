using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class BackendOperationalHealthApiContractTests : ApiTestBase
{
    private static readonly Guid ProjectReferenceId = Guid.Parse("aaaaaaaa-1490-4000-8000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("bbbbbbbb-1490-4000-8000-000000000001");

    [TestMethod]
    public async Task BackendOperationalHealth_GetHealth_IsGetOnly()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/operations/health?projectReferenceId={ProjectReferenceId:D}&correlationId={CorrelationId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task BackendOperationalHealth_GetBackendHealth_IsGetOnly()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/operations/health/backend?projectReferenceId={ProjectReferenceId:D}&correlationId={CorrelationId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task BackendOperationalHealth_HasNoPostPutPatchDeleteRoutes()
    {
        using var client = await AuthedClientAsync();
        foreach (var (method, route) in new[]
        {
            (HttpMethod.Post, "/api/v1/operations/health"),
            (HttpMethod.Put, "/api/v1/operations/health/backend"),
            (HttpMethod.Patch, "/api/v1/operations/health/dependencies"),
            (HttpMethod.Delete, "/api/v1/operations/health"),
            (HttpMethod.Post, "/api/v1/operations/health/restart"),
            (HttpMethod.Post, "/api/v1/operations/health/repair"),
            (HttpMethod.Post, "/api/v1/operations/health/migrate"),
            (HttpMethod.Post, "/api/v1/operations/health/execute"),
            (HttpMethod.Post, "/api/v1/operations/health/approve"),
            (HttpMethod.Post, "/api/v1/operations/health/apply-source")
        })
        {
            using var request = new HttpRequestMessage(method, route);
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Unsupported backend health route unexpectedly succeeded: {method} {route}");
        }
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ValidRequest_ReturnsSafeHealthEnvelope()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/operations/health?projectReferenceId={ProjectReferenceId:D}&correlationId={CorrelationId:D}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsTrue(json.RootElement.GetProperty("status").GetString() is "healthy" or "degraded" or "unavailable");
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("isHealthReportOnly").GetBoolean());
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_InvalidProjectReferenceId_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/operations/health?projectReferenceId=not-a-guid");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "BACKEND_OPERATIONAL_HEALTH_INVALID_PROJECT_REFERENCE_ID");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_UnsafeQueryText_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/operations/health?projectReferenceId=rawPrompt-leaked");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "BACKEND_OPERATIONAL_HEALTH_UNSAFE_QUERY_TEXT");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseHasMutationOccurredFalse()
    {
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync("/api/v1/operations/health"));

        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseIncludesReadOnlyBoundary()
    {
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync("/api/v1/operations/health"));

        AssertBoundary(json.RootElement.GetProperty("boundary"));
        var warnings = string.Join("\n", json.RootElement.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));
        StringAssert.Contains(warnings, "Backend operational health report is read-only.");
    }

    [TestMethod] public async Task BackendOperationalHealth_ResponseStatesHealthIsNotReleaseReadiness() => await AssertWarningAsync("Health check is not release readiness.");
    [TestMethod] public async Task BackendOperationalHealth_ResponseStatesHealthyIsNotApproval() => await AssertWarningAsync("Healthy status is not approval.");
    [TestMethod] public async Task BackendOperationalHealth_ResponseStatesRecommendationIsNotExecution() => await AssertWarningAsync("Recommendation is not execution.");
    [TestMethod] public async Task BackendOperationalHealth_ResponseStatesReportIsNotRepair() => await AssertWarningAsync("Report is not backend repair.");
    [TestMethod] public async Task BackendOperationalHealth_ResponseStatesReportIsNotMigrationExecution() => await AssertWarningAsync("Report is not migration execution.");
    [TestMethod] public async Task BackendOperationalHealth_ResponseStatesReportIsNotWorkflowExecution() => await AssertWarningAsync("Report is not workflow execution.");

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseDoesNotExposeConnectionString()
    {
        var text = await GetHealthTextAsync();

        Assert.IsFalse(text.Contains("Server=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("Data Source=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase));
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseDoesNotExposeApiKey()
    {
        var text = await GetHealthTextAsync();

        Assert.IsFalse(text.Contains("api_key", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("apikey", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("api key value", StringComparison.OrdinalIgnoreCase));
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseDoesNotExposeSecrets()
    {
        var text = await GetHealthTextAsync();

        Assert.IsFalse(text.Contains("password=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("secret value", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("bearer ", StringComparison.OrdinalIgnoreCase));
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseDoesNotExposeRawPayloadJson()
    {
        var text = await GetHealthTextAsync();

        Assert.IsFalse(text.Contains("payloadJson", StringComparison.OrdinalIgnoreCase));
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task BackendOperationalHealth_ResponseDoesNotExposePrivateReasoning()
    {
        using var client = await AuthedClientAsync();
        var json = await ReadJsonAsync(await client.GetAsync("/api/v1/operations/health"));
        var data = json.RootElement.GetProperty("data");
        var text = string.Join(
            "\n",
            data.GetProperty("safeSummaryLines").EnumerateArray().Select(item => item.GetString())
                .Concat(data.GetProperty("dependencyChecks").EnumerateArray().Select(item => item.GetProperty("safeSummary").GetString()))
                .Concat(data.GetProperty("warnings").EnumerateArray().Select(item => item.GetProperty("safeSummary").GetString()))
                .Concat(data.GetProperty("recommendations").EnumerateArray().Select(item => item.GetProperty("safeSummary").GetString())));

        Assert.IsFalse(text.Contains("private reasoning", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase));
        AssertNoUnsafeMaterial(text);
    }

    private static async Task AssertWarningAsync(string expected)
    {
        using var client = await AuthedClientAsync();
        var json = await ReadJsonAsync(await client.GetAsync("/api/v1/operations/health"));
        var warnings = string.Join("\n", json.RootElement.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));

        StringAssert.Contains(warnings, expected);
    }

    private static async Task<string> GetHealthTextAsync()
    {
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync("/api/v1/operations/health");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        return text;
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsTrue(boundary.GetProperty("readOnlyHealthCheck").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("healthIsReleaseReadiness").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("healthyIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("dependencyStatusIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("recommendationIsExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsBackendRepair").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsBackendRestart").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsMigrationExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsWorkflowExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canRestartBackend").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canRepairBackend").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canRunMigration").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canExecuteWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canInvokeTool").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canDispatchAgent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canCallModel").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApproveRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canSatisfyPolicy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canPromoteMemory").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApplySource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApplyPatch").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawPayloads").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesPrivateReasoning").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesSensitiveValues").GetBoolean());
    }

    private static void AssertNoUnsafeMaterial(string text)
    {
        foreach (var token in new[]
        {
            "raw prompt",
            "raw completion",
            "raw tool output",
            "raw command output",
            "chain-of-thought",
            "source content",
            "patch payload",
            "connection string value",
            "api key value",
            "secret value",
            "password=",
            "bearer "
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Backend operational health API response leaked unsafe token: {token}");
        }
    }
}

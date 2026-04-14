using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public class TenantControllerTests : ApiTestBase
{
    // ── GET /api/tenants ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTenants_ForSingleTenantUser_ShouldReturnAssignedTenant()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync("/api/tenants");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var tenants = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(1, tenants.GetArrayLength(), "Admin should be a member of exactly 1 tenant.");
        Assert.AreEqual(AssignedTenantId, tenants[0].GetProperty("id").GetInt32());
        Assert.AreEqual("Default Tenant", tenants[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task GetTenants_ForUnauthenticatedUser_ShouldReturnUnauthorized()
    {
        var response = await Client.GetAsync("/api/tenants");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST /api/tenants/select ──────────────────────────────────────────────

    [TestMethod]
    public async Task SelectTenant_WithAssignedTenant_ShouldSucceedAndReturnTenantBearingToken()
    {
        var baseToken = await LoginAsync();
        using var client = GetAuthedClient(baseToken);

        var response = await client.PostAsJsonAsync("/api/tenants/select",
            new { tenantId = AssignedTenantId });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newToken = body.GetProperty("token").GetString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(newToken), "A new token should be returned after tenant selection.");

        // Verify the new token carries the tenant: call /api/auth/me to check the claim.
        using var authedClient = GetAuthedClient(newToken!);
        var meResponse = await authedClient.GetAsync("/api/auth/me");
        meResponse.EnsureSuccessStatusCode();

        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(AssignedTenantId, me.GetProperty("selectedTenantId").GetInt32(),
            "The tenant-bearing token should include selectedTenantId in the profile.");
    }

    [TestMethod]
    public async Task SelectTenant_WithUnassignedTenant_ShouldReturnForbidden()
    {
        var baseToken = await LoginAsync();
        using var client = GetAuthedClient(baseToken);

        // Admin is NOT a member of tenant 2.
        var response = await client.PostAsJsonAsync("/api/tenants/select",
            new { tenantId = UnassignedTenantId });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task SelectTenant_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/tenants/select",
            new { tenantId = AssignedTenantId });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

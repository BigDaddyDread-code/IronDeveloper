using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class TenantTokenContractApiTests : ApiTestBase
{
    [TestMethod]
    public async Task BaseAndTenantTokens_HaveDistinctAccessContracts()
    {
        var baseToken = await LoginAsync();
        var baseJwt = new JwtSecurityTokenHandler().ReadJwtToken(baseToken);
        Assert.IsFalse(baseJwt.Claims.Any(claim => claim.Type == "tenant_id"));

        using var baseClient = GetAuthedClient(baseToken);
        Assert.AreEqual(HttpStatusCode.OK, (await baseClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await baseClient.GetAsync("/api/tenants")).StatusCode);

        var blocked = await baseClient.GetAsync("/api/projects");
        Assert.AreEqual(HttpStatusCode.Forbidden, blocked.StatusCode);
        var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("tenant_selection_required", blockedBody.GetProperty("reasonCode").GetString());

        var tenantToken = await SelectTenantAsync(baseToken);
        var tenantJwt = new JwtSecurityTokenHandler().ReadJwtToken(tenantToken);
        Assert.AreEqual(AssignedTenantId.ToString(), tenantJwt.Claims.Single(claim => claim.Type == "tenant_id").Value);

        using var tenantClient = GetAuthedClient(tenantToken);
        Assert.AreEqual(HttpStatusCode.OK, (await tenantClient.GetAsync("/api/projects")).StatusCode);
    }

    [TestMethod]
    public async Task SelectedTenant_CannotBeSwappedByRouteOrProjectBody()
    {
        var tenantToken = await SelectTenantAsync(await LoginAsync());
        using var client = GetAuthedClient(tenantToken);

        var crossTenantRead = await client.GetAsync($"/api/tenants/{UnassignedTenantId}/users");
        Assert.AreEqual(HttpStatusCode.NotFound, crossTenantRead.StatusCode);

        var crossTenantWrite = await client.PostAsJsonAsync($"/api/tenants/{UnassignedTenantId}/users", new
        {
            email = "blocked-cross-tenant@irondev.local",
            displayName = "Blocked Cross Tenant",
            password = "not-created-password",
            role = "Viewer"
        });
        Assert.AreEqual(HttpStatusCode.NotFound, crossTenantWrite.StatusCode);

        var bodySwap = await client.PostAsJsonAsync("/api/projects", new
        {
            tenantId = UnassignedTenantId,
            name = "Blocked cross-tenant project"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, bodySwap.StatusCode);
        var body = await bodySwap.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("selected_tenant_body_scope_mismatch", body.GetProperty("reasonCode").GetString());
    }
}

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public class AuthControllerTests : ApiTestBase
{
    // ── Login ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Login_WithValidCredentials_ShouldReturnToken()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminEmail, password = AdminPassword });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(token), "Token should not be empty.");
        Assert.AreEqual(1, body.GetProperty("userId").GetInt32());
        Assert.AreEqual("Admin User", body.GetProperty("displayName").GetString());
    }

    [TestMethod]
    public async Task Login_WithInvalidPassword_ShouldReturnUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminEmail, password = "wrongpassword" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_WithUnknownUser_ShouldReturnUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@irondev.local", password = AdminPassword });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_WithEmptyEmail_ShouldReturnBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "", password = AdminPassword });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_WithEmptyPassword_ShouldReturnBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminEmail, password = "" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── /me ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetCurrentUser_WithValidToken_ShouldReturnUserProfile()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync("/api/auth/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(1, body.GetProperty("userId").GetInt32());
        Assert.AreEqual(AdminEmail, body.GetProperty("email").GetString());
    }

    [TestMethod]
    public async Task GetCurrentUser_WithoutToken_ShouldReturnUnauthorized()
    {
        var response = await Client.GetAsync("/api/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetCurrentUser_WithExpiredOrInvalidToken_ShouldReturnUnauthorized()
    {
        using var client = GetAuthedClient("this.is.not.a.valid.jwt");

        var response = await client.GetAsync("/api/auth/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

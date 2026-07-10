using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using IronDev.Data.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Boundary tests for tenant user administration.
///
/// Protected boundaries:
/// - tenant isolation: a caller cannot list or mutate users of a tenant they are not a member of;
/// - visibility is not action authority: a non-administering member (Viewer) cannot administer users;
/// - last-owner protection: the final Owner cannot be demoted or removed;
/// - membership removal is not account deletion: the user account survives and can still log in.
/// </summary>
[TestClass]
[TestCategory("TenantUsersAdmin")]
public class TenantUsersAdminApiTests : ApiTestBase
{
    private const string NewUserEmail = "viewer@irondev.local";
    private const string SecondUserEmail = "second@irondev.local";
    private const string NewUserPassword = "password123";

    [TestInitialize]
    public async Task CleanCreatedUsersAsync()
    {
        // The shared domain reset restores only the seeded admin; users this class creates
        // would otherwise leak between tests and turn creates into AlreadyMember conflicts.
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(connection, """
            DELETE tu FROM dbo.TenantUsers tu
            INNER JOIN dbo.Users u ON u.Id = tu.UserId
            WHERE u.Email IN (@NewUserEmail, @SecondUserEmail);
            DELETE FROM dbo.Users WHERE Email IN (@NewUserEmail, @SecondUserEmail);
            """,
            new { NewUserEmail, SecondUserEmail });
    }

    // ── Authentication and tenant isolation ──────────────────────────────────

    [TestMethod]
    public async Task ListUsers_WithoutToken_ShouldReturnUnauthorized()
    {
        var response = await Client.GetAsync($"/api/tenants/{AssignedTenantId}/users");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListUsers_AsMember_ShouldReturnMembersWithRoles()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync($"/api/tenants/{AssignedTenantId}/users");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<JsonElement>();
        var found = false;
        foreach (var user in users.EnumerateArray())
        {
            if (user.GetProperty("email").GetString() == AdminEmail)
            {
                found = true;
                Assert.AreEqual("Owner", user.GetProperty("role").GetString());
            }
        }

        Assert.IsTrue(found, "The seeded admin should appear in the tenant user list.");
    }

    [TestMethod]
    public async Task ListUsers_ForTenantCallerIsNotMemberOf_ShouldBeDenied()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.GetAsync($"/api/tenants/{UnassignedTenantId}/users");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateUser_InForeignTenant_ShouldBeDenied()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.PostAsJsonAsync($"/api/tenants/{UnassignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Foreign", password = NewUserPassword, role = "Viewer" });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateUser_AsOwner_ShouldCreateAccountAndMembership()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = NewUserPassword, role = "Viewer" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Viewer", created.GetProperty("role").GetString());

        // The account is real: the created user can log in.
        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = NewUserEmail, password = NewUserPassword });
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [TestMethod]
    public async Task CreateUser_WithUnknownRole_ShouldReturnBadRequest()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = NewUserPassword, role = "GodMode" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateUser_WithoutPasswordForNewAccount_ShouldReturnBadRequest()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = (string?)null, role = "Viewer" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Visibility is not action authority ────────────────────────────────────

    [TestMethod]
    public async Task Viewer_CanListUsers_ButCannotAdministerThem()
    {
        var ownerToken = await LoginAsync();
        using var ownerClient = GetAuthedClient(ownerToken);

        var createResponse = await ownerClient.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = NewUserPassword, role = "Viewer" });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = created.GetProperty("id").GetInt32();

        var viewerToken = await LoginAsync(NewUserEmail, NewUserPassword);
        using var viewerClient = GetAuthedClient(viewerToken);

        // Visibility: a member may see the member list.
        var listResponse = await viewerClient.GetAsync($"/api/tenants/{AssignedTenantId}/users");
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);

        // Not authority: a Viewer cannot create users, change roles, or remove members.
        var createAttempt = await viewerClient.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = "second@irondev.local", displayName = "Second", password = NewUserPassword, role = "Viewer" });
        Assert.AreEqual(HttpStatusCode.Forbidden, createAttempt.StatusCode);

        var roleAttempt = await viewerClient.PutAsJsonAsync($"/api/tenants/{AssignedTenantId}/users/1/role",
            new { role = "Viewer" });
        Assert.AreEqual(HttpStatusCode.Forbidden, roleAttempt.StatusCode);

        var removeAttempt = await viewerClient.DeleteAsync($"/api/tenants/{AssignedTenantId}/users/{createdId}");
        Assert.AreEqual(HttpStatusCode.Forbidden, removeAttempt.StatusCode);
    }

    [TestMethod]
    public async Task ProjectMemberDirectory_ShouldExposeViewerAsReadOnly()
    {
        var ownerBaseToken = await LoginAsync();
        var ownerTenantToken = await SelectTenantAsync(ownerBaseToken);
        using var ownerClient = GetAuthedClient(ownerTenantToken);

        var projectResponse = await ownerClient.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Viewer Member Directory",
            Description = "Project-scoped member directory permission test.",
            LocalPath = @"C:\Temp\ViewerMemberDirectory"
        });
        Assert.AreEqual(HttpStatusCode.Created, projectResponse.StatusCode);
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);

        var createResponse = await ownerClient.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = NewUserPassword, role = "Viewer" });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var viewerBaseToken = await LoginAsync(NewUserEmail, NewUserPassword);
        var viewerTenantToken = await SelectTenantAsync(viewerBaseToken);
        using var viewerClient = GetAuthedClient(viewerTenantToken);
        var directoryResponse = await viewerClient.GetAsync($"/api/projects/{project!.Id}/members");

        Assert.AreEqual(HttpStatusCode.OK, directoryResponse.StatusCode);
        var directory = await directoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("Viewer", directory.GetProperty("currentUserTenantRole").GetString());
        Assert.IsFalse(directory.GetProperty("canAdministerTenantMembership").GetBoolean());
        Assert.AreEqual("Not implemented", directory.GetProperty("projectMembershipStatus").GetString());
        Assert.AreEqual("Not implemented", directory.GetProperty("channelMembershipStatus").GetString());
    }

    // ── Last-owner protection ─────────────────────────────────────────────────

    [TestMethod]
    public async Task DemotingTheLastOwner_ShouldConflict()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.PutAsJsonAsync($"/api/tenants/{AssignedTenantId}/users/1/role",
            new { role = "Viewer" });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task RemovingTheLastOwner_ShouldConflict()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var response = await client.DeleteAsync($"/api/tenants/{AssignedTenantId}/users/1");

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Membership removal is not account deletion ────────────────────────────

    [TestMethod]
    public async Task RemovingMembership_ShouldNotDeleteTheAccount()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var createResponse = await client.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = NewUserPassword, role = "Viewer" });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = created.GetProperty("id").GetInt32();

        var removeResponse = await client.DeleteAsync($"/api/tenants/{AssignedTenantId}/users/{createdId}");
        Assert.AreEqual(HttpStatusCode.OK, removeResponse.StatusCode);

        // Gone from the tenant list.
        var listResponse = await client.GetAsync($"/api/tenants/{AssignedTenantId}/users");
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var user in users.EnumerateArray())
        {
            Assert.AreNotEqual(NewUserEmail, user.GetProperty("email").GetString(),
                "Removed member should not appear in the tenant user list.");
        }

        // But the account survives and can still authenticate.
        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = NewUserEmail, password = NewUserPassword });
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [TestMethod]
    public async Task RoleChange_ByOwner_OnNonOwnerMember_ShouldSucceed()
    {
        var token = await LoginAsync();
        using var client = GetAuthedClient(token);

        var createResponse = await client.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users",
            new { email = NewUserEmail, displayName = "Viewer User", password = NewUserPassword, role = "Viewer" });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = created.GetProperty("id").GetInt32();

        var roleResponse = await client.PutAsJsonAsync($"/api/tenants/{AssignedTenantId}/users/{createdId}/role",
            new { role = "Reviewer" });
        Assert.AreEqual(HttpStatusCode.OK, roleResponse.StatusCode);

        var listResponse = await client.GetAsync($"/api/tenants/{AssignedTenantId}/users");
        var users = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var verified = false;
        foreach (var user in users.EnumerateArray())
        {
            if (user.GetProperty("id").GetInt32() == createdId)
            {
                Assert.AreEqual("Reviewer", user.GetProperty("role").GetString());
                verified = true;
            }
        }

        Assert.IsTrue(verified, "The updated member should appear in the list with the new role.");
    }
}

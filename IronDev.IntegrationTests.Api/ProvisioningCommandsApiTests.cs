using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// DOGFOOD-2 wizard hardening (findings F-C, F-C2, F-D, F-J), pinned against the
/// real API and SQL. Cycle 001's provisioning wizard was poisoned by ONE malformed
/// request: an empty command bound silently, returned 200 OK, accumulated as one of
/// many isDefault rows, and had no product path out — only direct SQL recovered it.
/// A confirm is an upsert, an empty command is a refusal, and a stored row is
/// deletable. Configuration repair is not authority.
/// </summary>
[TestClass]
public sealed class ProvisioningCommandsApiTests : ApiTestBase
{
    [TestMethod]
    public async Task SaveCommand_RefusesEmptyCommandText_NamingTheFields()
    {
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, "F-C empty command refusal");

        // The exact malformed shape from cycle 001: wrong field name binds to empty.
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/profile/commands",
            new { projectId, commandType = "Build", command = "dotnet build", isDefault = true });
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "commandText");

        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/profile/commands");
        Assert.AreEqual(0, stored.GetArrayLength(), "A refused command stores nothing — the wizard cannot be poisoned.");
    }

    [TestMethod]
    public async Task SaveCommand_NewDefaultReplacesTheOldDefault_NeverAccumulates()
    {
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, "F-D default replacement");

        await SaveCommandAsync(client, projectId, "Build", "dotnet build old.slnx");
        await SaveCommandAsync(client, projectId, "Build", "dotnet build new.slnx");
        await SaveCommandAsync(client, projectId, "Test", "dotnet test new.slnx");

        var commands = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/profile/commands");
        var buildDefaults = commands.EnumerateArray()
            .Where(command => command.GetProperty("commandType").GetString() == "Build" && command.GetProperty("isDefault").GetBoolean())
            .ToList();
        Assert.AreEqual(1, buildDefaults.Count, "Exactly one default per command type — a new default replaces, never accumulates.");
        Assert.AreEqual("dotnet build new.slnx", buildDefaults[0].GetProperty("commandText").GetString());

        var resolved = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/profile/commands/default/Build");
        Assert.AreEqual("dotnet build new.slnx", resolved.GetProperty("commandText").GetString(),
            "Default resolution is deterministic: the confirmed command takes effect.");
    }

    [TestMethod]
    public async Task DeleteCommand_RemovesTheRow_AndSecondDeleteIsNotFound()
    {
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, "F-D delete surface");

        await SaveCommandAsync(client, projectId, "Build", "dotnet build x.slnx");
        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/profile/commands");
        var commandId = stored.EnumerateArray().Single().GetProperty("projectCommandId").GetInt64();

        var deleted = await client.DeleteAsync($"/api/projects/{projectId}/profile/commands/{commandId}");
        Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode, "A stored command has a product path out — never only SQL.");

        var again = await client.DeleteAsync($"/api/projects/{projectId}/profile/commands/{commandId}");
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);

        var remaining = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/profile/commands");
        Assert.AreEqual(0, remaining.GetArrayLength());
    }

    [TestMethod]
    public async Task DeleteCommand_IsProjectScoped_AnotherProjectsIdIsNotFound()
    {
        using var client = await AuthedClientAsync();
        var owner = await CreateProjectAsync(client, "F-D scope owner");
        var other = await CreateProjectAsync(client, "F-D scope other");

        await SaveCommandAsync(client, owner, "Build", "dotnet build owned.slnx");
        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{owner}/profile/commands");
        var commandId = stored.EnumerateArray().Single().GetProperty("projectCommandId").GetInt64();

        var crossProject = await client.DeleteAsync($"/api/projects/{other}/profile/commands/{commandId}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossProject.StatusCode, "A command id cannot reach across projects.");
    }

    [TestMethod]
    public async Task CreateTicket_CarriesLinkedFilePaths()
    {
        // DOGFOOD-2 finding F-J: LinkedFilePaths is the most reliability-critical
        // Builder input, and the form-shaped ticket path could not carry it.
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, "F-J linked files");

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tickets", new
        {
            title = "Linked files ride the form path",
            type = "Task",
            priority = "Medium",
            summary = "F-J pin.",
            problem = "Prose hints do not steer the Builder; this field does.",
            proposedChange = "None - contract pin.",
            acceptanceCriteria = new[] { "The created ticket carries the linked file paths." },
            linkedFilePaths = new[] { "src/App/Program.cs", " src/App/Feature.cs " }
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, created.ToString());
        var linked = created.GetProperty("linkedFilePaths").GetString();
        StringAssert.Contains(linked, "src/App/Program.cs");
        StringAssert.Contains(linked, "src/App/Feature.cs");
        Assert.IsFalse(linked!.Contains(' '), "Paths are trimmed — whitespace never reaches the Builder's path hints.");
    }

    private static async Task<int> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "DOGFOOD-2 wizard-hardening contract pin."
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.ToString());
        return json.GetProperty("id").GetInt32();
    }

    private static async Task SaveCommandAsync(HttpClient client, int projectId, string commandType, string commandText)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/profile/commands",
            new { projectId, commandType, commandText, isDefault = true, isEnabled = true, timeoutSeconds = 300 });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }
}

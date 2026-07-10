using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class GuidedProjectSetupApiTests : ApiTestBase
{
    [TestMethod]
    public async Task ReadinessContract_ExposesStableCodesAndBackendOwnedNextAction()
    {
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, localPath: null);

        var readiness = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/provisioning/readiness");

        Assert.IsFalse(readiness.GetProperty("isReady").GetBoolean());
        Assert.IsTrue(readiness.GetProperty("blockedCount").GetInt32() > 0);
        foreach (var check in readiness.GetProperty("checks").EnumerateArray())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(check.GetProperty("code").GetString()));
        }

        var nextAction = readiness.GetProperty("nextAction");
        Assert.AreEqual("ChangeRepository", nextAction.GetProperty("kind").GetString());
        Assert.AreEqual("RepositoryAccess", nextAction.GetProperty("checkCode").GetString());
        Assert.IsTrue(nextAction.GetProperty("allowed").GetBoolean());
    }

    [TestMethod]
    public async Task SetupMutations_ChangeStoredTruthAndAreVisibleOnReevaluation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-guided-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "GuidedSetup.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            using var client = await AuthedClientAsync();
            var projectId = await CreateProjectAsync(client, root);
            var initial = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/provisioning/readiness");
            var proposedProfile = initial.GetProperty("proposedProfile");

            Assert.AreEqual(
                "NeedsConfirmation",
                FindCheck(initial, "BuildCommand").GetProperty("state").GetString());
            Assert.AreEqual(
                "NeedsConfirmation",
                FindCheck(initial, "ProjectProfile").GetProperty("state").GetString());

            var commandResponse = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/profile/commands",
                new
                {
                    projectId,
                    commandType = "Build",
                    commandText = "dotnet build GuidedSetup.csproj",
                    isDefault = true,
                    isEnabled = true,
                    timeoutSeconds = 300
                });
            commandResponse.EnsureSuccessStatusCode();

            var profile = JsonSerializer.Deserialize<Dictionary<string, object?>>(proposedProfile.GetRawText())!;
            profile["projectId"] = projectId;
            var profileResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/profile", profile);
            profileResponse.EnsureSuccessStatusCode();

            var reevaluated = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/provisioning/readiness");
            Assert.AreEqual("Confirmed", FindCheck(reevaluated, "BuildCommand").GetProperty("state").GetString());
            Assert.AreEqual("Confirmed", FindCheck(reevaluated, "ProjectProfile").GetProperty("state").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task LocalPathUpdate_IsReflectedByANewReadinessEvaluation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-guided-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var client = await AuthedClientAsync();
            var projectId = await CreateProjectAsync(client, localPath: null);
            var initial = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/provisioning/readiness");
            Assert.AreEqual("Missing", FindCheck(initial, "RepositoryAccess").GetProperty("state").GetString());

            var update = await client.PutAsJsonAsync($"/api/projects/{projectId}/local-path", new { localPath = root });
            update.EnsureSuccessStatusCode();

            var reevaluated = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/provisioning/readiness");
            Assert.AreEqual("Confirmed", FindCheck(reevaluated, "RepositoryAccess").GetProperty("state").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JsonElement FindCheck(JsonElement readiness, string code) =>
        readiness.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("code").GetString() == code);

    private static async Task<int> CreateProjectAsync(HttpClient client, string? localPath)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name = $"Guided setup {Guid.NewGuid():N}",
            description = "UX-PROJECT-2 API contract fixture.",
            localPath
        });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetInt32();
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }
}

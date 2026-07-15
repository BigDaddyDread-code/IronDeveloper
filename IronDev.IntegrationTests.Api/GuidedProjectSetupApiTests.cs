using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data.Models;
using Microsoft.Data.SqlClient;
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

    [TestMethod]
    public async Task IndexProject_UsesStoredPath_RejectsBodyScope_UpdatesReadinessAndAttributesActor()
    {
        var storedRoot = Path.Combine(Path.GetTempPath(), $"irondev-governed-index-{Guid.NewGuid():N}");
        var decoyRoot = Path.Combine(Path.GetTempPath(), $"irondev-governed-index-decoy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storedRoot);
        Directory.CreateDirectory(decoyRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(storedRoot, "StoredProject.cs"), "public sealed class StoredProject {}");
            await File.WriteAllTextAsync(Path.Combine(decoyRoot, "BrowserSupplied.cs"), "public sealed class BrowserSupplied {}");

            using var client = await AuthedClientAsync();
            var projectId = await CreateProjectAsync(client, storedRoot);

            var scopedBody = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/provisioning/code-index",
                new { projectId = projectId + 1, directoryPath = decoyRoot });
            Assert.AreEqual(HttpStatusCode.BadRequest, scopedBody.StatusCode);
            var bodyRefusal = await scopedBody.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
            Assert.AreEqual("project_setup_request_body_forbidden", bodyRefusal?.ReasonCode);

            var correlationId = $"dux1-fix-002-{Guid.NewGuid():N}";
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/projects/{projectId}/provisioning/code-index");
            request.Headers.Add("X-Correlation-ID", correlationId);
            var response = await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var outcome = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.IsTrue(outcome.GetProperty("allowed").GetBoolean());
            Assert.AreEqual(correlationId, outcome.GetProperty("correlationId").GetString());
            Assert.AreEqual(1, outcome.GetProperty("indexResult").GetProperty("storedFileCount").GetInt32());
            Assert.AreEqual(
                "Confirmed",
                FindCheck(outcome.GetProperty("readiness"), "CodeIndex").GetProperty("state").GetString());

            await using var connection = new SqlConnection(ConnectionString);
            var indexedPaths = (await connection.QueryAsync<string>(
                "SELECT FilePath FROM dbo.ProjectFiles WHERE TenantId=@TenantId AND ProjectId=@ProjectId",
                new { TenantId = AssignedTenantId, ProjectId = projectId })).ToArray();
            CollectionAssert.AreEquivalent(new[] { "StoredProject.cs" }, indexedPaths);

            var attribution = await connection.QuerySingleAsync<(int ActorUserId, int TenantId, string ProjectId, string CorrelationId, string Phase)>(
                """
                SELECT TOP (1) ActorUserId, TenantId, ProjectId, CorrelationId, Phase
                FROM dbo.UserMutationAttribution
                WHERE Route=@Route AND CorrelationId=@CorrelationId AND Phase=N'Completed'
                ORDER BY Id DESC
                """,
                new { Route = $"/api/projects/{projectId}/provisioning/code-index", CorrelationId = correlationId });
            Assert.AreEqual(1, attribution.ActorUserId);
            Assert.AreEqual(AssignedTenantId, attribution.TenantId);
            Assert.AreEqual(projectId.ToString(), attribution.ProjectId);
            Assert.AreEqual(correlationId, attribution.CorrelationId);
        }
        finally
        {
            Directory.Delete(storedRoot, recursive: true);
            Directory.Delete(decoyRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task IndexProject_MissingUnsafeAndCrossProjectScopeAreRefused()
    {
        using var owner = await AuthedClientAsync();
        var missingPathProject = await CreateProjectAsync(owner, null);
        var missing = await owner.PostAsync($"/api/projects/{missingPathProject}/provisioning/code-index", null);
        Assert.AreEqual(HttpStatusCode.Conflict, missing.StatusCode);
        var missingRefusal = await missing.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
        Assert.IsFalse(missingRefusal?.Allowed ?? true);
        Assert.AreEqual("project_setup_repository_path_missing", missingRefusal?.ReasonCode);

        var unsafeProject = await CreateProjectAsync(owner, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var unsafeResponse = await owner.PostAsync($"/api/projects/{unsafeProject}/provisioning/code-index", null);
        Assert.AreEqual(HttpStatusCode.Conflict, unsafeResponse.StatusCode);
        var unsafeRefusal = await unsafeResponse.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
        Assert.AreEqual("project_setup_repository_path_unsafe", unsafeRefusal?.ReasonCode);

        var outsider = await CreateTenantUserClientAsync(owner, addToProjectId: null);
        using (outsider.Client)
        {
            var crossProject = await outsider.Client.PostAsync(
                $"/api/projects/{missingPathProject}/provisioning/code-index",
                null);
            Assert.AreEqual(HttpStatusCode.NotFound, crossProject.StatusCode);
        }
    }

    [TestMethod]
    public async Task BuilderPermission_IsNarrowReversibleIdempotentAndDurablyAttributed()
    {
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, Path.GetTempPath());
        var original = FullProfile(projectId);
        var save = await client.PostAsJsonAsync($"/api/projects/{projectId}/profile", original);
        save.EnsureSuccessStatusCode();

        var omitted = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
            new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, omitted.StatusCode);
        var omittedRefusal = await omitted.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
        Assert.AreEqual("project_setup_enabled_required", omittedRefusal?.ReasonCode);

        var correlationId = $"dux1-builder-permission-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        var enable = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
            new { enabled = true, projectId = projectId + 1000 });
        Assert.AreEqual(HttpStatusCode.OK, enable.StatusCode);
        var enabled = await enable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(enabled.GetProperty("changed").GetBoolean());
        Assert.IsTrue(enabled.GetProperty("profile").GetProperty("allowBuilderApply").GetBoolean());

        var enableAgain = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
            new { enabled = true });
        var unchangedEnable = await enableAgain.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(unchangedEnable.GetProperty("changed").GetBoolean());

        var stored = await client.GetFromJsonAsync<ProjectProfile>($"/api/projects/{projectId}/profile");
        Assert.IsNotNull(stored);
        AssertProfileConfigurationPreserved(original, stored!);
        Assert.IsTrue(stored!.AllowBuilderApply);

        var disable = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
            new { enabled = false });
        var disabled = await disable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(disabled.GetProperty("changed").GetBoolean());
        Assert.IsFalse(disabled.GetProperty("profile").GetProperty("allowBuilderApply").GetBoolean());
        Assert.AreEqual(
            "NeedsConfirmation",
            FindCheck(disabled.GetProperty("readiness"), "BuilderApplyPermission").GetProperty("state").GetString());

        var disableAgain = await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
            new { enabled = false });
        var unchangedDisable = await disableAgain.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(unchangedDisable.GetProperty("changed").GetBoolean());

        await using var connection = new SqlConnection(ConnectionString);
        var completedCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.UserMutationAttribution
            WHERE Route=@Route AND CorrelationId=@CorrelationId AND Phase=N'Completed'
              AND ActorUserId=1 AND TenantId=@TenantId AND ProjectId=@ProjectId
            """,
            new
            {
                Route = $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
                CorrelationId = correlationId,
                TenantId = AssignedTenantId,
                ProjectId = projectId.ToString()
            });
        Assert.AreEqual(4, completedCount);
    }

    [TestMethod]
    public async Task BuilderPermission_ViewerIsCanonicallyRefusedByExplicitSafetyCapability()
    {
        using var owner = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(owner, Path.GetTempPath());
        var save = await owner.PostAsJsonAsync($"/api/projects/{projectId}/profile", FullProfile(projectId));
        save.EnsureSuccessStatusCode();

        var viewer = await CreateTenantUserClientAsync(owner, projectId);
        using (viewer.Client)
        {
            var response = await viewer.Client.PutAsJsonAsync(
                $"/api/projects/{projectId}/provisioning/builder-workspace-permission",
                new { enabled = true });
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            var refusal = await response.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
            Assert.IsFalse(refusal?.Allowed ?? true);
            Assert.AreEqual("project_setup_safety_capability_required", refusal?.ReasonCode);
            StringAssert.Contains(refusal?.Message ?? string.Empty, "project.setup.safety.manage");
        }
    }

    [TestMethod]
    public async Task ProfileWrite_RouteProjectIsAuthoritativeAndConflictingBodyIsRefused()
    {
        using var client = await AuthedClientAsync();
        var projectId = await CreateProjectAsync(client, Path.GetTempPath());
        var conflicting = FullProfile(projectId + 1);

        var mismatch = await client.PostAsJsonAsync($"/api/projects/{projectId}/profile", conflicting);
        Assert.AreEqual(HttpStatusCode.BadRequest, mismatch.StatusCode);
        var refusal = await mismatch.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
        Assert.AreEqual("route_body_project_scope_mismatch", refusal?.ReasonCode);

        var omitted = FullProfile(projectId: 0);
        var accepted = await client.PostAsJsonAsync($"/api/projects/{projectId}/profile", omitted);
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        var stored = await client.GetFromJsonAsync<ProjectProfile>($"/api/projects/{projectId}/profile");
        Assert.AreEqual(projectId, stored?.ProjectId);
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

    private static ProjectProfile FullProfile(int projectId) => new()
    {
        ProjectId = projectId,
        IsExternalProject = true,
        ApplicationType = "WebApi",
        PrimaryLanguage = "C#",
        Framework = "ASP.NET Core",
        RuntimeVersion = "net10.0",
        DatabaseEngine = "SQL Server",
        DataAccessStyle = "Dapper",
        TestFramework = "MSTest",
        SolutionFile = "GovernedSetup.slnx",
        SafeWriteRoot = "workspace",
        AllowBuilderApply = false,
        AllowWritesOutsideProjectRoot = false,
        ProfileNotes = "Preserve every field."
    };

    private static void AssertProfileConfigurationPreserved(ProjectProfile expected, ProjectProfile actual)
    {
        Assert.AreEqual(expected.IsExternalProject, actual.IsExternalProject);
        Assert.AreEqual(expected.ApplicationType, actual.ApplicationType);
        Assert.AreEqual(expected.PrimaryLanguage, actual.PrimaryLanguage);
        Assert.AreEqual(expected.Framework, actual.Framework);
        Assert.AreEqual(expected.RuntimeVersion, actual.RuntimeVersion);
        Assert.AreEqual(expected.DatabaseEngine, actual.DatabaseEngine);
        Assert.AreEqual(expected.DataAccessStyle, actual.DataAccessStyle);
        Assert.AreEqual(expected.TestFramework, actual.TestFramework);
        Assert.AreEqual(expected.SolutionFile, actual.SolutionFile);
        Assert.AreEqual(expected.SafeWriteRoot, actual.SafeWriteRoot);
        Assert.AreEqual(expected.AllowWritesOutsideProjectRoot, actual.AllowWritesOutsideProjectRoot);
        Assert.AreEqual(expected.ProfileNotes, actual.ProfileNotes);
    }

    private static async Task<(HttpClient Client, int UserId)> CreateTenantUserClientAsync(
        HttpClient owner,
        int? addToProjectId)
    {
        var email = $"setup-viewer-{Guid.NewGuid():N}@irondev.local";
        const string password = "governed-setup-viewer-password";
        var create = await owner.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users", new
        {
            email,
            displayName = "Setup Viewer",
            password,
            role = "Viewer"
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetInt32();

        if (addToProjectId.HasValue)
        {
            var membership = await owner.PutAsJsonAsync(
                $"/api/projects/{addToProjectId.Value}/members/{userId}",
                new { projectRole = "Viewer" });
            membership.EnsureSuccessStatusCode();
        }

        var token = await SelectTenantAsync(await LoginAsync(email, password));
        return (GetAuthedClient(token), userId);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Data.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class LegacyProjectRouteMembershipApiTests : ApiTestBase
{
    [TestMethod]
    public async Task SameTenantNonMember_CannotReadOrMutateLegacyProjectRoutes()
    {
        using var owner = GetAuthedClient(await SelectTenantAsync(await LoginAsync()));
        var createProject = await owner.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Legacy membership boundary",
            Description = "Must remain visible only to project members.",
            LocalPath = null
        });
        Assert.AreEqual(HttpStatusCode.Created, createProject.StatusCode);
        var project = await createProject.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);

        var memberEmail = $"legacy-nonmember-{Guid.NewGuid():N}@irondev.local";
        const string memberPassword = "legacy-nonmember-password";
        var createTenantMember = await owner.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users", new
        {
            email = memberEmail,
            displayName = "Legacy Route Nonmember",
            password = memberPassword,
            role = "Viewer"
        });
        Assert.AreEqual(HttpStatusCode.OK, createTenantMember.StatusCode);

        using var nonMember = GetAuthedClient(await SelectTenantAsync(await LoginAsync(memberEmail, memberPassword)));
        var attempts = new[]
        {
            await nonMember.GetAsync($"/api/projects/{project!.Id}"),
            await nonMember.PatchAsJsonAsync($"/api/projects/{project.Id}", new { name = "Unauthorized rename" }),
            await nonMember.PostAsync($"/api/projects/{project.Id}/select", content: null),
            await nonMember.PutAsJsonAsync($"/api/projects/{project.Id}/local-path", new { localPath = @"C:\Unauthorized" }),
            await nonMember.PostAsJsonAsync($"/api/projects/{project.Id}/mark-index-stale", new { reason = "Unauthorized mutation" }),
            await nonMember.GetAsync($"/api/projects/{project.Id}/context-pack")
        };

        foreach (var response in attempts)
        {
            using (response)
            {
                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            }
        }

        var unchangedResponse = await owner.GetAsync($"/api/projects/{project.Id}");
        Assert.AreEqual(HttpStatusCode.OK, unchangedResponse.StatusCode);
        var unchanged = await unchangedResponse.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(unchanged);
        Assert.AreEqual("Legacy membership boundary", unchanged.Name);
        Assert.IsNull(unchanged.LocalPath);
    }

    [TestMethod]
    public async Task WorkbenchV2_DisablesLegacyProjectCreation()
    {
        var tenantToken = await SelectTenantAsync(await LoginAsync());
        using var v2Factory = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("WorkbenchV2:Enabled", "true"));
        using var v2Client = v2Factory.CreateClient();
        v2Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);

        var response = await v2Client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Must use project-first start",
            LocalPath = @"C:\MustNotBeProvisioned"
        });

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("legacy_project_creation_disabled", body.GetProperty("error").GetString());

        var projectList = await v2Client.GetFromJsonAsync<Project[]>("/api/projects");
        Assert.IsNotNull(projectList);
        Assert.AreEqual(0, projectList.Length);
    }
}

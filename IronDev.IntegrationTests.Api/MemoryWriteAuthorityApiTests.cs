using System.Net;
using System.Net.Http.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Core.Models;
using IronDev.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class MemoryWriteAuthorityApiTests : ApiTestBase
{
    [TestMethod]
    public async Task Reindex_MemberIsCanonicallyRefused_OwnerAndTenantAdminAreAccepted()
    {
        var owner = await OwnerClientAsync();
        using (owner.Client)
        {
            var ownerResponse = await owner.Client.PostAsync($"/api/projects/{owner.Project.Id}/memory/reindex", null);
            Assert.AreEqual(HttpStatusCode.OK, ownerResponse.StatusCode);

            await SeedUserAsync(24, "member.memory@irondev.local", "Member", owner.Project.Id);
            using var member = await UserClientAsync("member.memory@irondev.local");
            var memberResponse = await member.PostAsync($"/api/projects/{owner.Project.Id}/memory/reindex", null);
            Assert.AreEqual(HttpStatusCode.Forbidden, memberResponse.StatusCode);
            var refusal = await memberResponse.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
            Assert.IsNotNull(refusal);
            Assert.IsFalse(refusal.Allowed);
            Assert.AreEqual("ProjectMemoryMaintenanceCapabilityRequired", refusal.ReasonCode);
            CollectionAssert.Contains(refusal.MissingEvidence.ToArray(), "project-memory.maintain");

            await SeedUserAsync(25, "admin.memory@irondev.local", "TenantAdmin", owner.Project.Id);
            using var admin = await UserClientAsync("admin.memory@irondev.local");
            var adminResponse = await admin.PostAsync($"/api/projects/{owner.Project.Id}/memory/reindex", null);
            Assert.AreEqual(HttpStatusCode.OK, adminResponse.StatusCode);
        }
    }

    [TestMethod]
    public async Task MemoryWrites_EnforceScopePromotionEnvelopeAndActorAttribution()
    {
        var owner = await OwnerClientAsync();
        using (owner.Client)
        {
            var mismatch = await owner.Client.PostAsJsonAsync(
                $"/api/projects/{owner.Project.Id}/memory/summary",
                new ProjectSummary { ProjectId = owner.Project.Id + 1, Summary = "wrong scope" });
            Assert.AreEqual(HttpStatusCode.BadRequest, mismatch.StatusCode);
            var mismatchRefusal = await mismatch.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
            Assert.AreEqual("route_body_project_scope_mismatch", mismatchRefusal?.ReasonCode);

            var decision = await owner.Client.PostAsJsonAsync(
                $"/api/projects/{owner.Project.Id}/memory/decisions",
                new ProjectDecision { ProjectId = owner.Project.Id, Title = "self asserted", Detail = "must refuse" });
            Assert.AreEqual(HttpStatusCode.Conflict, decision.StatusCode);
            var promotionRefusal = await decision.Content.ReadFromJsonAsync<GovernedRefusalEnvelope>();
            Assert.AreEqual("GovernedPromotionRequired", promotionRefusal?.ReasonCode);

            var accepted = await owner.Client.PostAsJsonAsync(
                $"/api/projects/{owner.Project.Id}/memory/summary",
                new ProjectSummary { ProjectId = owner.Project.Id, TenantId = 999, Summary = "server scoped" });
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);

            await using var connection = new SqlConnection(ConnectionString);
            var attribution = await connection.QuerySingleOrDefaultAsync<(int ActorUserId, int? TenantId, string ProjectId, string Phase, int? StatusCode)>(
                """
                SELECT TOP (1) ActorUserId, TenantId, ProjectId, Phase, StatusCode
                FROM dbo.UserMutationAttribution
                WHERE Route = @Route AND Phase = N'Completed'
                ORDER BY Id DESC;
                """,
                new { Route = $"/api/projects/{owner.Project.Id}/memory/summary" });
            Assert.AreEqual(1, attribution.ActorUserId);
            Assert.AreEqual(AssignedTenantId, attribution.TenantId);
            Assert.AreEqual(owner.Project.Id.ToString(), attribution.ProjectId);
            Assert.AreEqual("Completed", attribution.Phase);
            Assert.AreEqual(200, attribution.StatusCode);
        }
    }

    private static async Task<(HttpClient Client, Project Project)> OwnerClientAsync()
    {
        var client = await UserClientAsync(AdminEmail);
        var response = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = $"Memory authority {Guid.NewGuid():N}",
            Description = "CLN-24 SQL-backed API proof",
            LocalPath = $@"C:\Temp\cln24-{Guid.NewGuid():N}"
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);
        return (client, project);
    }

    private static async Task<HttpClient> UserClientAsync(string email)
    {
        var baseToken = await LoginAsync(email);
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedUserAsync(int userId, string email, string tenantRole, int projectId)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(AdminPassword, workFactor: 4);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            SET IDENTITY_INSERT dbo.Users ON;
            INSERT dbo.Users (Id, Email, DisplayName, PasswordHash, IsActive)
            VALUES (@UserId, @Email, @Email, @Hash, 1);
            SET IDENTITY_INSERT dbo.Users OFF;
            INSERT dbo.TenantUsers (TenantId, UserId, Role) VALUES (1, @UserId, @TenantRole);
            INSERT dbo.ProjectMembers (TenantId, ProjectId, UserId, ProjectRole, AddedByUserId)
            VALUES (1, @ProjectId, @UserId, N'Contributor', 1);
            """,
            new { UserId = userId, Email = email, Hash = hash, TenantRole = tenantRole, ProjectId = projectId });
    }
}

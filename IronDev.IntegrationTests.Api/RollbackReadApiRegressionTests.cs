using System.Net;
using System.Reflection;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("RollbackRegression")]
[TestCategory("RollbackSupportReceiptReadApi")]
public sealed class RollbackReadApiRegressionTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 17, 10, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RollbackRegression_RollbackReadApiDoesNotConvertReceiptIntoSourceApplyPermission()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("source-apply-permission");
        await SeedAsync(receipt);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/{receipt.RollbackSupportReceiptId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "Rollback read API must not mutate rollback support receipt rows.");
        AssertReadOnlyEnvelope(json.RootElement);
        AssertNoAuthorityTokens(text);
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("rollbackGateSatisfied").GetBoolean());
        StringAssert.Contains(text, "Rollback support receipt read API is read-only.");
        StringAssert.Contains(text, "Real source apply must still pass the source-apply gate before mutation.");
    }

    [TestMethod]
    public async Task RollbackRegression_RollbackReadApiIsProjectScoped()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("project-scope");
        await SeedAsync(receipt);

        var wrongProject = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/rollback-support-receipts/{receipt.RollbackSupportReceiptId}");
        var wrongHashProject = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/rollback-support-receipts/by-hash/{receipt.RollbackSupportReceiptHash}");
        var wrongPatchProject = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/rollback-support-receipts/by-patch-artifact/{receipt.PatchArtifactId}");

        Assert.AreEqual(HttpStatusCode.NotFound, wrongProject.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, wrongHashProject.StatusCode);

        var listJson = await ReadJsonAsync(wrongPatchProject);
        Assert.AreEqual(HttpStatusCode.OK, wrongPatchProject.StatusCode, listJson.RootElement.ToString());
        Assert.AreEqual(0, listJson.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task RollbackRegression_RollbackReadApiRejectsUnsafePrivateAndActionClaimRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        foreach (var routeValue in new[] { "raw%20prompt", "chain-of-thought", "secret", "rollback%20executed", "source%20applied", "workflow%20continued", "release%20ready" })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-patch-hash/{routeValue}");
            var json = await ReadJsonAsync(response);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{routeValue}: {json.RootElement}");
            Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
            AssertReadOnlyEnvelope(json.RootElement);
            AssertNoAuthorityTokens(json.RootElement.ToString());
        }
    }

    [TestMethod]
    public void RollbackRegression_RollbackReadApiRemainsGetOnly()
    {
        var methods = typeof(RollbackSupportReceiptsV1Controller)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(RollbackSupportReceiptsV1Controller))
            .Where(method => new[] { "Get", "GetByReceiptHash", "ListByPatchArtifact", "ListByPatchHash", "ListByRollbackPlan", "ListBySourceBaselineHash" }.Contains(method.Name, StringComparer.Ordinal))
            .ToArray();

        Assert.AreEqual(6, methods.Length);
        Assert.IsTrue(methods.All(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void RollbackRegression_RollbackReadApiControllerAndQueryServiceStayReadOnly()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "RollbackSupportReceiptsV1Controller.cs"));
        var query = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "RollbackSupportReceiptQueryService.cs"));

        StringAssert.Contains(controller, "IRollbackSupportReceiptQueryService");
        Assert.IsFalse(controller.Contains("IRollbackSupportReceiptStore", StringComparison.Ordinal), "Controller must not depend on write-capable store.");
        Assert.IsFalse(controller.Contains("SqlRollbackSupportReceiptStore", StringComparison.Ordinal), "Controller must not depend on SQL store.");
        Assert.IsFalse(controller.Contains("IDbConnection", StringComparison.Ordinal), "Controller must not open DB connections.");
        Assert.IsFalse(query.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not write rollback support receipts.");
        Assert.IsFalse(query.Contains("ExecuteRollback", StringComparison.Ordinal), "Query service must not execute rollback.");
        Assert.IsFalse(query.Contains("ApplySourceAsync", StringComparison.Ordinal), "Query service must not apply source.");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedAsync(params RollbackSupportReceipt[] receipts)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRollbackSupportReceiptStore>();
        foreach (var receipt in receipts)
        {
            await store.SaveAsync(receipt);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.RollbackSupportReceipt");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static RollbackSupportReceipt ValidReceipt(string suffix) => new()
    {
        RollbackSupportReceiptId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        RollbackPlanId = Guid.NewGuid(),
        RollbackPlanHash = $"sha256:rollback-plan-{suffix}",
        RollbackGateSatisfied = true,
        RollbackGateEvaluationHash = $"sha256:rollback-gate-evaluation-{suffix}",
        PatchArtifactId = Guid.NewGuid(),
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        ControlledDryRunRequestId = Guid.NewGuid(),
        DryRunExecutionAuditId = Guid.NewGuid(),
        DryRunAuditHash = $"sha256:dry-run-audit-{suffix}",
        DryRunReceiptHash = $"sha256:dry-run-receipt-{suffix}",
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
        SubjectKind = "PatchArtifact",
        SubjectId = $"patch-artifact-{suffix}",
        SubjectHash = $"sha256:subject-{suffix}",
        SourceSnapshotReference = $"source-snapshot:{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ExpectedBranch = $"main-{suffix}",
        ExpectedCleanWorktreeHash = $"sha256:clean-worktree-{suffix}",
        RollbackSupportReceiptHash = $"sha256:rollback-support-receipt-{suffix}",
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = CreatedAtUtc.AddHours(1),
        EvidenceReferences = [$"rollback-gate-evaluation:{suffix}", $"rollback-plan:{suffix}"],
        BoundaryMaxims = ["Rollback support receipt records rollback support only."],
        Boundary = RollbackSupportReceiptBoundaryText.Boundary
    };

    private static void AssertReadOnlyEnvelope(JsonElement root)
    {
        Assert.IsFalse(root.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(root.GetProperty("humanApprovalRequired").GetBoolean());
        StringAssert.Contains(root.GetProperty("warnings")[0].GetString(), "Rollback support receipt read API is read-only.");
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("rollbackReadExecutesRollback").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("rollbackReadAppliesSource").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("rollbackReadContinuesWorkflow").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("rollbackReadApprovesRelease").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("rollbackReadInfersReleaseReadiness").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("rollbackReadAuthorizesSourceMutation").GetBoolean());
    }

    private static void AssertNoAuthorityTokens(string text)
    {
        foreach (var token in new[]
        {
            "canApplySource",
            "sourceApplyApproved",
            "rollbackExecuted",
            "rollbackSucceeded",
            "rollbackReady",
            "appliedAtUtc",
            "applyResult",
            "canContinueWorkflow",
            "canApproveRelease",
            "releaseReady",
            "mutationOccurred\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained rollback/source authority token: {token}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}

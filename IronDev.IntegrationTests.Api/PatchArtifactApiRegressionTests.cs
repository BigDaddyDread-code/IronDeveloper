using System.Net;
using System.Reflection;
using System.Text;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("PatchArtifactApiRegression")]
public sealed class PatchArtifactApiRegressionTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 17, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PatchArtifactApiRegression_GetByIdDoesNotMutateRowsOrExposeApplyAuthority()
    {
        var artifact = ValidArtifact("detail");
        await SeedAsync(artifact);
        var client = await AuthedClientAsync();
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/{artifact.PatchArtifactId}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, await CountAsync());
        AssertReadOnlyEnvelope(text);
        AssertNoAuthorityText(text);
    }

    [TestMethod]
    public async Task PatchArtifactApiRegression_AllLookupRoutesRemainReadOnly()
    {
        var artifact = ValidArtifact("lookup");
        await SeedAsync(artifact);
        var client = await AuthedClientAsync();
        var before = await CountAsync();

        foreach (var route in LookupRoutes(artifact))
        {
            var response = await client.GetAsync(route);
            var text = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{route}\n{text}");
            AssertReadOnlyEnvelope(text);
            AssertNoAuthorityText(text);
        }

        Assert.AreEqual(before, await CountAsync());
    }

    [TestMethod]
    public async Task PatchArtifactApiRegression_UnsafeRouteValuesRemainRejected()
    {
        var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        foreach (var marker in new[] { "raw prompt", "raw completion", "raw tool output", "chain-of-thought", "secret", "source applied", "release ready" })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/patch-artifacts/by-patch-hash/{Uri.EscapeDataString(marker)}");
            var text = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
            AssertReadOnlyEnvelope(text);
            AssertNoAuthorityText(text);
        }
    }

    [TestMethod]
    public async Task PatchArtifactApiRegression_NoPostPutPatchDeleteRoutesExist()
    {
        var controllerMethods = typeof(PatchArtifactsV1Controller)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var routeAttributes = controllerMethods
            .SelectMany(method => method.GetCustomAttributes(inherit: false).OfType<HttpMethodAttribute>())
            .ToArray();

        Assert.AreEqual(7, routeAttributes.Count(attribute => attribute.HttpMethods.Contains("GET")));
        Assert.IsFalse(routeAttributes.Any(attribute => attribute.HttpMethods.Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE")));

        var artifact = ValidArtifact("post");
        await SeedAsync(artifact);
        var client = await AuthedClientAsync();
        var before = await CountAsync();

        var response = await client.PostAsync(
            $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.IsFalse(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(before, await CountAsync());
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedAsync(params PatchArtifact[] artifacts)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPatchArtifactStore>();
        foreach (var artifact in artifacts)
        {
            await store.SaveAsync(artifact);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.PatchArtifact");
    }

    private static IReadOnlyList<string> LookupRoutes(PatchArtifact artifact) =>
    [
        $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-dry-run-receipt-hash/{Escape(artifact.DryRunReceiptHash)}",
        $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-dry-run-audit-hash/{Escape(artifact.DryRunAuditHash)}",
        $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-controlled-dry-run-request/{artifact.ControlledDryRunRequestId}",
        $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-subject/{Escape(artifact.SubjectKind)}/{Escape(artifact.SubjectId)}",
        $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-patch-hash/{Escape(artifact.PatchHash)}",
        $"/api/v1/projects/{artifact.ProjectId}/patch-artifacts/by-source-baseline-hash/{Escape(artifact.SourceBaselineHash)}"
    ];

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static void AssertReadOnlyEnvelope(string text)
    {
        StringAssert.Contains(text, "\"mutationOccurred\":false");
        StringAssert.Contains(text, "\"humanApprovalRequired\":true");
        StringAssert.Contains(text, "Patch artifact read API is read-only.");
    }

    private static void AssertNoAuthorityText(string text)
    {
        foreach (var token in new[]
        {
            "\"canApplySource\":true",
            "\"sourceApplyApproved\":true",
            "\"appliedAtUtc\"",
            "\"applyResult\"",
            "\"canContinueWorkflow\":true",
            "\"canApproveRelease\":true",
            "\"releaseReady\":true",
            "\"mutationOccurred\":true",
            "source applied successfully",
            "workflow continued",
            "release approved"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected authority token {token} in response: {text}");
        }
    }

    private static PatchArtifact ValidArtifact(string suffix) => new()
    {
        PatchArtifactId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        PatchArtifactKind = "UnifiedDiffPackage",
        ControlledDryRunRequestId = Guid.NewGuid(),
        DryRunExecutionAuditId = Guid.NewGuid(),
        DryRunAuditHash = $"sha256:dry-run-audit-{suffix}",
        DryRunReceiptHash = $"sha256:dry-run-receipt-{suffix}",
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = $"sha256:policy-satisfaction-{suffix}",
        SubjectKind = "PatchProposal",
        SubjectId = $"patch-proposal-{suffix}",
        SubjectHash = $"sha256:subject-{suffix}",
        SourceSnapshotReference = $"source-snapshot:{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ValidationPlanId = $"validation-plan-{suffix}",
        ValidationPlanHash = $"sha256:validation-plan-{suffix}",
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        FileChanges = [ModifyChange($"src/{suffix}.cs")],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = [$"controlled-dry-run-receipt:{suffix}"],
        BoundaryMaxims = ["Patch artifact read API is read-only."],
        Boundary = PatchArtifactBoundaryText.Boundary
    };

    private static PatchArtifactFileChange ModifyChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Modify",
        BeforeContentHash = $"sha256:before-{path}",
        AfterContentHash = $"sha256:after-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"modify {path}",
        IsBinary = false
    };
}

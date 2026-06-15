using System.Net;
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
public sealed class AcceptedApprovalReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset AcceptedAtUtc = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AcceptedApprovalReadApi_CanGetAcceptedApprovalById()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/accepted-approvals/{record.AcceptedApprovalId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not create accepted approval records.");
        Assert.AreEqual("found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(record.AcceptedApprovalId, data.GetProperty("acceptedApprovalId").GetGuid());
        Assert.AreEqual(record.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(record.ApprovalTargetHash, data.GetProperty("approvalTargetHash").GetString());
        Assert.AreEqual(record.EvidenceReferences[0], data.GetProperty("evidenceReferences")[0].GetString());
        Assert.AreEqual(record.BoundaryMaxims[0], data.GetProperty("boundaryMaxims")[0].GetString());
        Assert.AreEqual(AcceptedApprovalReadBoundaryText.AuthorityBoundary, data.GetProperty("authorityBoundary").GetString());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task AcceptedApprovalReadApi_Returns404ForUnknownApproval()
    {
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/accepted-approvals/{Guid.NewGuid()}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task AcceptedApprovalReadApi_DoesNotLeakAcrossProjects()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/accepted-approvals/{record.AcceptedApprovalId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AcceptedApprovalReadApi_CanListByTargetWithinProject()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var targetId = "target-pr170";
        var matchingFirst = ValidRecord(projectId) with { AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = targetId, AcceptedAtUtc = AcceptedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidRecord(projectId) with { AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = targetId, AcceptedAtUtc = AcceptedAtUtc.AddMinutes(2) };
        var otherTarget = ValidRecord(projectId) with { AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = "target-other" };
        var otherProject = ValidRecord(Guid.NewGuid()) with { AcceptedApprovalId = Guid.NewGuid(), ApprovalTargetId = targetId };
        await SeedAsync(matchingFirst, matchingSecond, otherTarget, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/accepted-approvals/by-target/{matchingFirst.ApprovalTargetKind}/{targetId}");
        var json = await ReadJsonAsync(response);
        var ids = json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("acceptedApprovalId").GetGuid()).ToArray();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        CollectionAssert.AreEquivalent(new[] { matchingFirst.AcceptedApprovalId, matchingSecond.AcceptedApprovalId }, ids);
    }

    [TestMethod]
    public async Task AcceptedApprovalReadApi_CanListByCorrelationWithinProject()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var correlationId = "correlation-pr170";
        var matchingFirst = ValidRecord(projectId) with { AcceptedApprovalId = Guid.NewGuid(), CorrelationId = correlationId, AcceptedAtUtc = AcceptedAtUtc.AddMinutes(1) };
        var matchingSecond = ValidRecord(projectId) with { AcceptedApprovalId = Guid.NewGuid(), CorrelationId = correlationId, AcceptedAtUtc = AcceptedAtUtc.AddMinutes(2) };
        var otherCorrelation = ValidRecord(projectId) with { AcceptedApprovalId = Guid.NewGuid(), CorrelationId = "correlation-other" };
        var otherProject = ValidRecord(Guid.NewGuid()) with { AcceptedApprovalId = Guid.NewGuid(), CorrelationId = correlationId };
        await SeedAsync(matchingFirst, matchingSecond, otherCorrelation, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/accepted-approvals/by-correlation/{correlationId}");
        var json = await ReadJsonAsync(response);
        var ids = json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("acceptedApprovalId").GetGuid()).ToArray();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        CollectionAssert.AreEquivalent(new[] { matchingFirst.AcceptedApprovalId, matchingSecond.AcceptedApprovalId }, ids);
    }

    [TestMethod]
    public void AcceptedApprovalReadApi_UsesGetOnlyRoutes()
    {
        var methods = typeof(AcceptedApprovalsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(AcceptedApprovalsV1Controller))
            .ToArray();

        Assert.AreEqual(3, methods.Count(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void AcceptedApprovalReadApi_DoesNotCreateAcceptedApproval()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "AcceptedApprovalsV1Controller.cs"));

        foreach (var token in new[] { "CreateAcceptedApproval", "SaveAcceptedApproval", "HttpPost", "[HttpPost]", "HttpPut", "HttpPatch", "HttpDelete", "IAcceptedApprovalStore", ".SaveAsync" })
            Assert.IsFalse(controller.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in controller: {token}");

        StringAssert.Contains(controller, "IAcceptedApprovalQueryService");
    }

    [TestMethod]
    public void AcceptedApprovalReadApi_DoesNotSatisfyPolicyOrApplySource()
    {
        foreach (var file in Pr170ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[] { "SatisfyPolicy", "PolicySatisfied", "CanApplySource", "ApplySource", "RunDryRun", "CreatePatchArtifact", "ContinueWorkflow", "ApproveRelease", "ReleaseReady" })
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
        }
    }

    [TestMethod]
    public async Task AcceptedApprovalReadApi_ReturnsBoundaryLanguage()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord();
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/accepted-approvals/{record.AcceptedApprovalId}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        StringAssert.Contains(text, AcceptedApprovalReadBoundaryText.AuthorityBoundary);
        Assert.IsTrue(json.RootElement.GetProperty("warnings").EnumerateArray().Any(warning =>
            warning.GetString()?.Contains("not approval creation", StringComparison.OrdinalIgnoreCase) == true));
    }

    [TestMethod]
    public async Task AcceptedApprovalReadApi_DoesNotReturnRawPrivateMaterial()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord() with
        {
            ApprovalTargetId = "rawPrompt-private-target",
            ApprovedByActorDisplayName = "hidden reasoning actor"
        };
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/accepted-approvals/{record.AcceptedApprovalId}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        AssertNoPrivateReasoningLeak(text);
        StringAssert.Contains(text, "[redacted: sensitive accepted approval text]");
    }

    [TestMethod]
    public void AcceptedApprovalReadApi_ReceiptStatesReadOnlyBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR170_ACCEPTED_APPROVAL_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR170 adds the Accepted Approval Read API.",
            "This PR exposes accepted approval records through read-only project-scoped GET endpoints.",
            "This PR does not create accepted approvals.",
            "This PR does not add an approval create API.",
            "This PR does not satisfy policy.",
            "This PR does not run dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "Accepted approval read API is not approval creation.",
            "Reading a persisted approval is not policy satisfaction.",
            "Reading a persisted approval is not source apply.",
            "Reading a persisted approval is not workflow continuation.",
            "Reading a persisted approval is not release readiness.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block P target is governed accepted approval creation boundary."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void AcceptedApprovalReadApi_ProjectScopedCorrelationIsRequired()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "AcceptedApprovalsV1Controller.cs"));
        var store = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "SqlAcceptedApprovalStore.cs"));
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", "migrate_accepted_approval.sql"));

        StringAssert.Contains(controller, "api/v1/projects/{projectId:guid}/accepted-approvals");
        Assert.IsFalse(controller.Contains("api/v1/accepted-approvals", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(controller, "ListByProjectAndCorrelationAsync");
        StringAssert.Contains(store, "governance.usp_AcceptedApproval_ListByProjectAndCorrelation");
        StringAssert.Contains(sql, "governance.usp_AcceptedApproval_ListByProjectAndCorrelation");
        StringAssert.Contains(sql, "WHERE ProjectId = @ProjectId");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedAsync(params AcceptedApprovalRecord[] records)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAcceptedApprovalStore>();
        foreach (var record in records)
            await store.SaveAsync(record);
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.AcceptedApproval");
    }

    private static AcceptedApprovalRecord ValidRecord(Guid? projectId = null) =>
        new()
        {
            AcceptedApprovalId = Guid.NewGuid(),
            ProjectId = projectId ?? Guid.NewGuid(),
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-pr170",
            ApprovalTargetHash = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            CapabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovedByActorId = "human-operator-pr170",
            ApprovedByActorDisplayName = "Human Operator",
            AcceptedAtUtc = AcceptedAtUtc,
            ExpiresAtUtc = AcceptedAtUtc.AddDays(7),
            CorrelationId = "correlation-pr170",
            CausationId = "approval-package-pr170",
            EvidenceReferences = ["approval-package:approval-package-pr170"],
            BoundaryMaxims =
            [
                "Accepted approval read API is not approval creation.",
                "Reading a persisted approval is not policy satisfaction."
            ]
        };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadIsApprovalCreation").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadSatisfiesPolicy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadRunsDryRun").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadCreatesPatchArtifact").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("acceptedApprovalReadApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readingPersistedApprovalAuthorizesExecution").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        foreach (var token in new[]
        {
            "acceptedApprovalReadIsApprovalCreation\":true",
            "acceptedApprovalReadSatisfiesPolicy\":true",
            "acceptedApprovalReadRunsDryRun\":true",
            "acceptedApprovalReadCreatesPatchArtifact\":true",
            "acceptedApprovalReadAppliesSource\":true",
            "acceptedApprovalReadContinuesWorkflow\":true",
            "acceptedApprovalReadApprovesRelease\":true",
            "readingPersistedApprovalAuthorizesExecution\":true",
            "mutationOccurred\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
        }
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        foreach (var token in new[]
        {
            "rawPrompt",
            "raw prompt",
            "rawCompletion",
            "raw completion",
            "rawToolOutput",
            "raw tool output",
            "chainOfThought",
            "chain-of-thought",
            "hidden reasoning",
            "private reasoning",
            "scratchpad"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained private reasoning marker: {token}");
        }
    }

    private static IReadOnlyList<string> Pr170ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Api", "Controllers", "AcceptedApprovalsV1Controller.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "AcceptedApprovalReadModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "AcceptedApprovalQueryService.cs")
        ];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}

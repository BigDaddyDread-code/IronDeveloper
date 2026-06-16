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
public sealed class RollbackSupportReceiptReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 18, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_CanGetById()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("get-by-id");
        await SeedAsync(receipt);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/{receipt.RollbackSupportReceiptId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not mutate rollback support receipt rows.");
        AssertReadOnlyEnvelope(json.RootElement);
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoRollbackAuthority(text);

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(receipt.RollbackSupportReceiptId, data.GetProperty("rollbackSupportReceiptId").GetGuid());
        Assert.AreEqual(receipt.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(receipt.RollbackPlanId, data.GetProperty("rollbackPlanId").GetGuid());
        Assert.AreEqual(receipt.RollbackPlanHash, data.GetProperty("rollbackPlanHash").GetString());
        Assert.IsTrue(data.GetProperty("rollbackGateSatisfied").GetBoolean());
        Assert.AreEqual(receipt.RollbackGateEvaluationHash, data.GetProperty("rollbackGateEvaluationHash").GetString());
        Assert.AreEqual(receipt.PatchArtifactId, data.GetProperty("patchArtifactId").GetGuid());
        Assert.AreEqual(receipt.PatchHash, data.GetProperty("patchHash").GetString());
        Assert.AreEqual(receipt.ChangeSetHash, data.GetProperty("changeSetHash").GetString());
        Assert.AreEqual(receipt.ControlledDryRunRequestId, data.GetProperty("controlledDryRunRequestId").GetGuid());
        Assert.AreEqual(receipt.DryRunExecutionAuditId, data.GetProperty("dryRunExecutionAuditId").GetGuid());
        Assert.AreEqual(receipt.DryRunAuditHash, data.GetProperty("dryRunAuditHash").GetString());
        Assert.AreEqual(receipt.DryRunReceiptHash, data.GetProperty("dryRunReceiptHash").GetString());
        Assert.AreEqual(receipt.PolicySatisfactionId, data.GetProperty("policySatisfactionId").GetGuid());
        Assert.AreEqual(receipt.PolicySatisfactionHash, data.GetProperty("policySatisfactionHash").GetString());
        Assert.AreEqual(receipt.SubjectKind, data.GetProperty("subjectKind").GetString());
        Assert.AreEqual(receipt.SubjectId, data.GetProperty("subjectId").GetString());
        Assert.AreEqual(receipt.SubjectHash, data.GetProperty("subjectHash").GetString());
        Assert.AreEqual(receipt.SourceSnapshotReference, data.GetProperty("sourceSnapshotReference").GetString());
        Assert.AreEqual(receipt.SourceBaselineHash, data.GetProperty("sourceBaselineHash").GetString());
        Assert.AreEqual(receipt.WorkspaceBoundaryHash, data.GetProperty("workspaceBoundaryHash").GetString());
        Assert.AreEqual(receipt.ExpectedBranch, data.GetProperty("expectedBranch").GetString());
        Assert.AreEqual(receipt.ExpectedCleanWorktreeHash, data.GetProperty("expectedCleanWorktreeHash").GetString());
        Assert.AreEqual(receipt.RollbackSupportReceiptHash, data.GetProperty("rollbackSupportReceiptHash").GetString());
        Assert.AreEqual(receipt.EvidenceReferences[0], data.GetProperty("evidenceReferences")[0].GetString());
        Assert.AreEqual(receipt.BoundaryMaxims[0], data.GetProperty("boundaryMaxims")[0].GetString());
        StringAssert.Contains(text, "Rollback support receipt read API is read-only.");
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_GetByIdIsProjectScoped()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("project-scope");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/rollback-support-receipts/{receipt.RollbackSupportReceiptId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_CanGetByReceiptHash()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("hash-lookup");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/by-hash/{receipt.RollbackSupportReceiptHash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(receipt.RollbackSupportReceiptId, json.RootElement.GetProperty("data").GetProperty("rollbackSupportReceiptId").GetGuid());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_GetByReceiptHashIsProjectScoped()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("hash-scope");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/rollback-support-receipts/by-hash/{receipt.RollbackSupportReceiptHash}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_CanListByPatchArtifact()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var patchArtifactId = Guid.NewGuid();
        var first = ValidReceipt("patch-artifact-a") with { ProjectId = projectId, PatchArtifactId = patchArtifactId };
        var second = ValidReceipt("patch-artifact-b") with { ProjectId = projectId, PatchArtifactId = patchArtifactId };
        var otherPatch = ValidReceipt("patch-artifact-c") with { ProjectId = projectId, PatchArtifactId = Guid.NewGuid() };
        var otherProject = ValidReceipt("patch-artifact-d") with { ProjectId = Guid.NewGuid(), PatchArtifactId = patchArtifactId };
        await SeedAsync(first, second, otherPatch, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-patch-artifact/{patchArtifactId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.RollbackSupportReceiptId, second.RollbackSupportReceiptId);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_CanListByPatchHash()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-patch-pr197";
        var matching = ValidReceipt("patch-a") with { ProjectId = projectId, PatchHash = hash };
        var otherHash = ValidReceipt("patch-b") with { ProjectId = projectId, PatchHash = "sha256:other-patch-pr197" };
        var otherProject = ValidReceipt("patch-c") with { ProjectId = Guid.NewGuid(), PatchHash = hash };
        await SeedAsync(matching, otherHash, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-patch-hash/{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.RollbackSupportReceiptId);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_CanListByRollbackPlan()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var rollbackPlanId = Guid.NewGuid();
        var matching = ValidReceipt("plan-a") with { ProjectId = projectId, RollbackPlanId = rollbackPlanId };
        var otherPlan = ValidReceipt("plan-b") with { ProjectId = projectId, RollbackPlanId = Guid.NewGuid() };
        var otherProject = ValidReceipt("plan-c") with { ProjectId = Guid.NewGuid(), RollbackPlanId = rollbackPlanId };
        await SeedAsync(matching, otherPlan, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-rollback-plan/{rollbackPlanId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.RollbackSupportReceiptId);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_CanListBySourceBaselineHash()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var hash = "sha256:shared-source-baseline-pr197";
        var first = ValidReceipt("baseline-a") with { ProjectId = projectId, SourceBaselineHash = hash };
        var second = ValidReceipt("baseline-b") with { ProjectId = projectId, SourceBaselineHash = hash };
        var otherHash = ValidReceipt("baseline-c") with { ProjectId = projectId, SourceBaselineHash = "sha256:other-baseline-pr197" };
        var otherProject = ValidReceipt("baseline-d") with { ProjectId = Guid.NewGuid(), SourceBaselineHash = hash };
        await SeedAsync(first, second, otherHash, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-source-baseline/{hash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.RollbackSupportReceiptId, second.RollbackSupportReceiptId);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_MissingListReturnsEmptyItems()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/rollback-support-receipts/by-patch-hash/sha256:no-matches-pr197");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(0, json.RootElement.GetProperty("items").GetArrayLength());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_RejectsUnsafeRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        foreach (var routeValue in new[] { "raw%20prompt", "chain-of-thought", "secret", "rollback%20executed", "source%20applied", "release%20ready" })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-patch-hash/{routeValue}");
            var json = await ReadJsonAsync(response);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{routeValue}: {json.RootElement}");
            Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_RejectsBlankRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/rollback-support-receipts/by-patch-hash/%20");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertNoRollbackAuthority(text);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_DoesNotExposeRollbackOrSourceApplyAuthority()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("authority-json");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/{receipt.RollbackSupportReceiptId}");
        var text = (await ReadJsonAsync(response)).RootElement.ToString();

        AssertNoRollbackAuthority(text);
    }

    [TestMethod]
    public async Task RollbackSupportReceiptReadApi_AllResponsesAreReadOnly()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("all-read-only");
        await SeedAsync(receipt);

        var urls = new[]
        {
            $"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/{receipt.RollbackSupportReceiptId}",
            $"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/by-hash/{receipt.RollbackSupportReceiptHash}",
            $"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/by-patch-artifact/{receipt.PatchArtifactId}",
            $"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/by-patch-hash/{receipt.PatchHash}",
            $"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/by-rollback-plan/{receipt.RollbackPlanId}",
            $"/api/v1/projects/{receipt.ProjectId}/rollback-support-receipts/by-source-baseline/{receipt.SourceBaselineHash}"
        };

        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            var json = await ReadJsonAsync(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public void RollbackSupportReceiptReadApi_HasOnlyGetRoutes()
    {
        var methods = ReadRouteMethods().ToArray();

        Assert.AreEqual(6, methods.Length);
        Assert.IsTrue(methods.All(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void RollbackSupportReceiptReadApi_UsesQueryServiceNotSqlStoreDirectlyInController()
    {
        var controller = File.ReadAllText(ControllerPath());

        StringAssert.Contains(controller, "IRollbackSupportReceiptQueryService");
        Assert.IsFalse(controller.Contains("SqlRollbackSupportReceiptStore", StringComparison.Ordinal), "Controller must not depend on SQL store directly.");
        Assert.IsFalse(controller.Contains("IDbConnection", StringComparison.Ordinal), "Controller must not depend on DB connection directly.");
        Assert.IsFalse(controller.Contains("IRollbackSupportReceiptStore", StringComparison.Ordinal), "Controller must not depend on write-capable rollback receipt store directly.");
    }

    [TestMethod]
    public void RollbackSupportReceiptReadApi_QueryServiceUsesStoreOnlyForReads()
    {
        var queryService = File.ReadAllText(QueryServicePath());

        StringAssert.Contains(queryService, "IRollbackSupportReceiptStore");
        StringAssert.Contains(queryService, "_store.GetAsync");
        StringAssert.Contains(queryService, "_store.GetByReceiptHashAsync");
        StringAssert.Contains(queryService, "_store.ListByPatchArtifactAsync");
        StringAssert.Contains(queryService, "_store.ListByPatchHashAsync");
        StringAssert.Contains(queryService, "_store.ListByRollbackPlanAsync");
        StringAssert.Contains(queryService, "_store.ListBySourceBaselineHashAsync");
        Assert.IsFalse(queryService.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not save rollback support receipts.");
        Assert.IsFalse(queryService.Contains("ExecuteRollback", StringComparison.Ordinal), "Query service must not execute rollback.");
        Assert.IsFalse(queryService.Contains("ApplySourceAsync", StringComparison.Ordinal), "Query service must not apply source.");
    }

    [TestMethod]
    public void RollbackSupportReceiptReadApi_DoesNotApplyExecuteContinueOrApprove() =>
        AssertNoProductionTokens("RollbackExecutor", "ExecuteRollback", "RollbackSucceeded = true", "ApplySourceAsync", "SourceApplyService", "ControlledSourceApply", "ContinueWorkflowAsync", "ApproveReleaseAsync", "ReleaseReady = true", "CanApplySource = true");

    [TestMethod]
    public void RollbackSupportReceiptReadApi_DoesNotCallGitProcessWorktree()
    {
        foreach (var token in new[] { "ProcessStartInfo", "System.Diagnostics.Process", "git ", "InspectWorktree", "WorktreeInspection", "GitWorktree", "File.WriteAllText", "Directory.CreateDirectory" })
            AssertNoProductionTokens(token);
    }

    [TestMethod]
    public void RollbackSupportReceiptReadApi_DoesNotAddCliUiRuntimeMemoryOrAgents()
    {
        foreach (var file in Pr197ChangedFiles())
        {
            foreach (var token in new[] { "Cli", "Tauri", "UI", "IHostedService", "BackgroundService", "Scheduler" })
                Assert.IsFalse(file.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR197 must not add {token}: {file}");
        }

        AssertNoProductionTokens("LLM", "model call", "AgentDispatch", "ToolExecution", "PromoteMemory", "ActivateRetrieval", "Vector", "Embedding", "Weaviate");
    }

    [TestMethod]
    public void RollbackSupportReceiptReadApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR197_ROLLBACK_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR197 adds a read-only API for rollback-support receipts.",
            "This PR exposes project-scoped GET-only endpoints for persisted RollbackSupportReceipt records.",
            "This PR does not create rollback-support receipts.",
            "This PR does not execute rollback.",
            "This PR does not prove rollback execution succeeded.",
            "This PR does not apply source.",
            "This PR does not mutate source.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Rollback read API is not rollback execution.",
            "Rollback read API is not rollback success.",
            "Rollback read API is not source apply.",
            "Rollback read API is not workflow continuation.",
            "Rollback read API is not release readiness.",
            "Rollback read API does not authorize source mutation by itself.",
            "A rollback-support receipt means rollback support was recorded for review/gating.",
            "It does not mean rollback was performed.",
            "It does not mean source apply is allowed.",
            "Real source apply must still pass the source-apply gate before mutation.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "PR197 opens the vault window. It does not hand out the keys."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
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

    private static RollbackSupportReceipt ValidReceipt(string suffix = "main") => new()
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
        ExpiresAtUtc = ExpiresAtUtc,
        EvidenceReferences = [$"rollback-gate-evaluation:{suffix}", $"rollback-plan:{suffix}"],
        BoundaryMaxims = ["Rollback support receipt records rollback support only."],
        Boundary = RollbackSupportReceiptBoundaryText.Boundary
    };

    private static void AssertItems(JsonElement root, params Guid[] expectedIds)
    {
        AssertReadOnlyEnvelope(root);
        var ids = root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("rollbackSupportReceiptId").GetGuid()).ToArray();
        CollectionAssert.AreEquivalent(expectedIds, ids);
    }

    private static void AssertReadOnlyEnvelope(JsonElement root)
    {
        Assert.IsFalse(root.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(root.GetProperty("humanApprovalRequired").GetBoolean());
        StringAssert.Contains(root.GetProperty("warnings")[0].GetString(), "Rollback support receipt read API is read-only.");
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("rollbackReadExecutesRollback").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackReadMarksRollbackSuccess").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackReadAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackReadContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackReadApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackReadInfersReleaseReadiness").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("rollbackReadAuthorizesSourceMutation").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
    }

    private static void AssertNoRollbackAuthority(string text)
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

    private static IEnumerable<MethodInfo> ReadRouteMethods()
    {
        var routeNames = new[]
        {
            "Get",
            "GetByReceiptHash",
            "ListByPatchArtifact",
            "ListByPatchHash",
            "ListByRollbackPlan",
            "ListBySourceBaselineHash"
        };

        return typeof(RollbackSupportReceiptsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(RollbackSupportReceiptsV1Controller))
            .Where(method => routeNames.Contains(method.Name, StringComparer.Ordinal));
    }

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in Pr197ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
            }
        }
    }

    private static string[] Pr197ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            ControllerPath(),
            QueryServicePath(),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackSupportReceiptReadModels.cs")
        ];
    }

    private static string[] Pr197ChangedFiles()
    {
        return
        [
            "IronDev.Core/Governance/RollbackSupportReceiptReadModels.cs",
            "IronDev.Infrastructure/Governance/RollbackSupportReceiptQueryService.cs",
            "IronDev.Api/Controllers/RollbackSupportReceiptsV1Controller.cs",
            "IronDev.Api/Program.cs",
            "IronDev.IntegrationTests.Api/ApiTestBase.cs",
            "Docs/receipts/PR197_ROLLBACK_READ_API.md",
            "IronDev.IntegrationTests.Api/RollbackSupportReceiptReadApiTests.cs"
        ];
    }

    private static string ControllerPath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "RollbackSupportReceiptsV1Controller.cs");

    private static string QueryServicePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "RollbackSupportReceiptQueryService.cs");

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

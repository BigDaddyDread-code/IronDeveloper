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
public sealed class SourceApplyDryRunReceiptReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAtUtc = new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_CanGetById()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("get-by-id");
        await SeedAsync(receipt);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/{receipt.SourceApplyDryRunReceiptId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not mutate source-apply dry-run receipt rows.");
        AssertReadOnlyEnvelope(json.RootElement);
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoSourceApplyAuthority(text);

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(receipt.SourceApplyDryRunReceiptId, data.GetProperty("sourceApplyDryRunReceiptId").GetGuid());
        Assert.AreEqual(receipt.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(receipt.SourceApplyDryRunRequestId, data.GetProperty("sourceApplyDryRunRequestId").GetGuid());
        Assert.AreEqual(receipt.SourceApplyDryRunRequestHash, data.GetProperty("sourceApplyDryRunRequestHash").GetString());
        Assert.IsTrue(data.GetProperty("dryRunSatisfied").GetBoolean());
        Assert.AreEqual(receipt.DryRunResultHash, data.GetProperty("dryRunResultHash").GetString());
        Assert.AreEqual(receipt.SourceApplyRequestId, data.GetProperty("sourceApplyRequestId").GetGuid());
        Assert.AreEqual(receipt.SourceApplyRequestHash, data.GetProperty("sourceApplyRequestHash").GetString());
        Assert.AreEqual(receipt.SourceApplyGateEvaluationId, data.GetProperty("sourceApplyGateEvaluationId").GetGuid());
        Assert.AreEqual(receipt.SourceApplyGateEvaluationHash, data.GetProperty("sourceApplyGateEvaluationHash").GetString());
        Assert.AreEqual(receipt.PatchArtifactId, data.GetProperty("patchArtifactId").GetGuid());
        Assert.AreEqual(receipt.PatchHash, data.GetProperty("patchHash").GetString());
        Assert.AreEqual(receipt.ChangeSetHash, data.GetProperty("changeSetHash").GetString());
        Assert.AreEqual(receipt.RollbackSupportReceiptId, data.GetProperty("rollbackSupportReceiptId").GetGuid());
        Assert.AreEqual(receipt.RollbackSupportReceiptHash, data.GetProperty("rollbackSupportReceiptHash").GetString());
        Assert.AreEqual(receipt.SourceBaselineHash, data.GetProperty("sourceBaselineHash").GetString());
        Assert.AreEqual(receipt.WorkspaceBoundaryHash, data.GetProperty("workspaceBoundaryHash").GetString());
        Assert.AreEqual(receipt.ExpectedBranch, data.GetProperty("expectedBranch").GetString());
        Assert.AreEqual(receipt.ExpectedCleanWorktreeHash, data.GetProperty("expectedCleanWorktreeHash").GetString());
        Assert.AreEqual(receipt.SourceApplyDryRunReceiptHash, data.GetProperty("sourceApplyDryRunReceiptHash").GetString());
        Assert.AreEqual(receipt.EvidenceReferences[0], data.GetProperty("evidenceReferences")[0].GetString());
        Assert.AreEqual(receipt.BoundaryMaxims[0], data.GetProperty("boundaryMaxims")[0].GetString());
        Assert.AreEqual(receipt.FileResults[0].Path, data.GetProperty("fileResults")[0].GetProperty("path").GetString());
        StringAssert.Contains(text, "Source-apply dry-run receipt read API is read-only.");
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_GetByIdIsProjectScoped()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("project-scope");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/source-apply-dry-run-receipts/{receipt.SourceApplyDryRunReceiptId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_CanGetByReceiptHash()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("hash-lookup");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/by-hash/{receipt.SourceApplyDryRunReceiptHash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(receipt.SourceApplyDryRunReceiptId, json.RootElement.GetProperty("data").GetProperty("sourceApplyDryRunReceiptId").GetGuid());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_GetByReceiptHashIsProjectScoped()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("hash-scope");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/source-apply-dry-run-receipts/by-hash/{receipt.SourceApplyDryRunReceiptHash}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_CanListBySourceApplyRequest()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var sourceApplyRequestId = Guid.NewGuid();
        var first = ValidReceipt("request-a") with { ProjectId = projectId, SourceApplyRequestId = sourceApplyRequestId };
        var second = ValidReceipt("request-b") with { ProjectId = projectId, SourceApplyRequestId = sourceApplyRequestId };
        var otherRequest = ValidReceipt("request-c") with { ProjectId = projectId, SourceApplyRequestId = Guid.NewGuid() };
        var otherProject = ValidReceipt("request-d") with { ProjectId = Guid.NewGuid(), SourceApplyRequestId = sourceApplyRequestId };
        await SeedAsync(first, second, otherRequest, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-source-apply-request/{sourceApplyRequestId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.SourceApplyDryRunReceiptId, second.SourceApplyDryRunReceiptId);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_CanListBySourceApplyGateEvaluation()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var sourceApplyGateEvaluationId = Guid.NewGuid();
        var matching = ValidReceipt("gate-a") with { ProjectId = projectId, SourceApplyGateEvaluationId = sourceApplyGateEvaluationId };
        var otherGate = ValidReceipt("gate-b") with { ProjectId = projectId, SourceApplyGateEvaluationId = Guid.NewGuid() };
        var otherProject = ValidReceipt("gate-c") with { ProjectId = Guid.NewGuid(), SourceApplyGateEvaluationId = sourceApplyGateEvaluationId };
        await SeedAsync(matching, otherGate, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-source-apply-gate/{sourceApplyGateEvaluationId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.SourceApplyDryRunReceiptId);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_CanListByPatchArtifact()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var patchArtifactId = Guid.NewGuid();
        var matching = ValidReceipt("patch-a") with { ProjectId = projectId, PatchArtifactId = patchArtifactId };
        var otherPatch = ValidReceipt("patch-b") with { ProjectId = projectId, PatchArtifactId = Guid.NewGuid() };
        var otherProject = ValidReceipt("patch-c") with { ProjectId = Guid.NewGuid(), PatchArtifactId = patchArtifactId };
        await SeedAsync(matching, otherPatch, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-patch-artifact/{patchArtifactId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.SourceApplyDryRunReceiptId);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_CanListByRollbackSupportReceipt()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var rollbackSupportReceiptId = Guid.NewGuid();
        var first = ValidReceipt("rollback-a") with { ProjectId = projectId, RollbackSupportReceiptId = rollbackSupportReceiptId };
        var second = ValidReceipt("rollback-b") with { ProjectId = projectId, RollbackSupportReceiptId = rollbackSupportReceiptId };
        var otherRollback = ValidReceipt("rollback-c") with { ProjectId = projectId, RollbackSupportReceiptId = Guid.NewGuid() };
        var otherProject = ValidReceipt("rollback-d") with { ProjectId = Guid.NewGuid(), RollbackSupportReceiptId = rollbackSupportReceiptId };
        await SeedAsync(first, second, otherRollback, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-rollback-support/{rollbackSupportReceiptId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.SourceApplyDryRunReceiptId, second.SourceApplyDryRunReceiptId);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_MissingListReturnsEmptyItems()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/source-apply-dry-run-receipts/by-source-apply-request/{Guid.NewGuid()}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(0, json.RootElement.GetProperty("items").GetArrayLength());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_RejectsUnsafeRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        foreach (var routeValue in new[] { "raw%20prompt", "chain-of-thought", "secret", "source%20applied", "patch%20applied", "release%20ready" })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-hash/{routeValue}");
            var json = await ReadJsonAsync(response);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{routeValue}: {json.RootElement}");
            Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_RejectsBlankRouteValues()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-hash/%20");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertNoSourceApplyAuthority(text);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_DoesNotExposeSourceApplyAuthority()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("authority-json");
        await SeedAsync(receipt);

        var response = await client.GetAsync($"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/{receipt.SourceApplyDryRunReceiptId}");
        var text = (await ReadJsonAsync(response)).RootElement.ToString();

        AssertNoSourceApplyAuthority(text);
    }

    [TestMethod]
    public async Task SourceApplyDryRunReceiptReadApi_AllResponsesAreReadOnly()
    {
        using var client = await AuthedClientAsync();
        var receipt = ValidReceipt("all-read-only");
        await SeedAsync(receipt);

        var urls = new[]
        {
            $"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/{receipt.SourceApplyDryRunReceiptId}",
            $"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/by-hash/{receipt.SourceApplyDryRunReceiptHash}",
            $"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/by-source-apply-request/{receipt.SourceApplyRequestId}",
            $"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/by-source-apply-gate/{receipt.SourceApplyGateEvaluationId}",
            $"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/by-patch-artifact/{receipt.PatchArtifactId}",
            $"/api/v1/projects/{receipt.ProjectId}/source-apply-dry-run-receipts/by-rollback-support/{receipt.RollbackSupportReceiptId}"
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
    public void SourceApplyDryRunReceiptReadApi_HasOnlyGetRoutes()
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
    public void SourceApplyDryRunReceiptReadApi_UsesQueryServiceNotSqlStoreDirectlyInController()
    {
        var controller = File.ReadAllText(ControllerPath());

        StringAssert.Contains(controller, "ISourceApplyDryRunReceiptQueryService");
        Assert.IsFalse(controller.Contains("SqlSourceApplyDryRunReceiptStore", StringComparison.Ordinal), "Controller must not depend on SQL store directly.");
        Assert.IsFalse(controller.Contains("IDbConnection", StringComparison.Ordinal), "Controller must not depend on DB connection directly.");
        Assert.IsFalse(controller.Contains("ISourceApplyDryRunReceiptStore", StringComparison.Ordinal), "Controller must not depend on write-capable dry-run receipt store directly.");
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptReadApi_QueryServiceUsesStoreOnlyForReads()
    {
        var queryService = File.ReadAllText(QueryServicePath());

        StringAssert.Contains(queryService, "ISourceApplyDryRunReceiptStore");
        StringAssert.Contains(queryService, "_store.GetAsync");
        StringAssert.Contains(queryService, "_store.GetByReceiptHashAsync");
        StringAssert.Contains(queryService, "_store.ListBySourceApplyRequestAsync");
        StringAssert.Contains(queryService, "_store.ListBySourceApplyGateEvaluationAsync");
        StringAssert.Contains(queryService, "_store.ListByPatchArtifactAsync");
        StringAssert.Contains(queryService, "_store.ListByRollbackSupportReceiptAsync");
        Assert.IsFalse(queryService.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not save source-apply dry-run receipts.");
        Assert.IsFalse(queryService.Contains("ApplySourceAsync", StringComparison.Ordinal), "Query service must not apply source.");
        Assert.IsFalse(queryService.Contains("PerformDryRun", StringComparison.Ordinal), "Query service must not perform dry-runs.");
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptReadApi_DoesNotApplyExecuteContinueOrApprove() =>
        AssertNoProductionTokens("SourceApplyExecutor", "ApplySourceAsync", "SourceApplied = true", "File.WriteAllText", "PatchApplied = true", "ContinueWorkflowAsync", "ApproveReleaseAsync", "ReleaseReady = true", "CanApplySource = true");

    [TestMethod]
    public void SourceApplyDryRunReceiptReadApi_DoesNotCallGitProcessWorktree()
    {
        foreach (var token in new[] { "ProcessStartInfo", "System.Diagnostics.Process", "git ", "InspectWorktree", "WorktreeInspection", "GitWorktree", "Directory.CreateDirectory" })
            AssertNoProductionTokens(token);
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptReadApi_DoesNotAddCliUiRuntimeMemoryOrAgents()
    {
        foreach (var file in Pr203ChangedFiles())
        {
            foreach (var token in new[] { "Cli", "Tauri", "UI", "IHostedService", "BackgroundService", "Scheduler" })
                Assert.IsFalse(file.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR203 must not add {token}: {file}");
        }

        AssertNoProductionTokens("LLM", "model call", "AgentDispatch", "ToolExecution", "PromoteMemory", "ActivateRetrieval", "Vector", "Embedding", "Weaviate");
    }

    [TestMethod]
    public void SourceApplyDryRunReceiptReadApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR203_SOURCE_APPLY_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR203 adds a read-only API for source-apply dry-run receipts.",
            "This PR exposes project-scoped GET-only endpoints for persisted `SourceApplyDryRunReceipt` records.",
            "This PR does not create source-apply dry-run receipts.",
            "This PR does not perform dry-runs.",
            "This PR does not apply source.",
            "This PR does not mutate source.",
            "This PR does not write files.",
            "This PR does not apply patches.",
            "This PR does not call git.",
            "This PR does not inspect worktrees.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not satisfy approval.",
            "This PR does not satisfy policy.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Source Apply Dry-run Receipt Read API is not source apply.",
            "Source Apply Dry-run Receipt Read API is not dry-run execution.",
            "Source Apply Dry-run Receipt Read API is not file mutation.",
            "Source Apply Dry-run Receipt Read API is not patch application.",
            "Source Apply Dry-run Receipt Read API is not workflow continuation.",
            "Source Apply Dry-run Receipt Read API is not release readiness.",
            "Source Apply Dry-run Receipt Read API does not authorize source mutation by itself.",
            "A source-apply dry-run receipt means rehearsal evidence was recorded for review/gating.",
            "It does not mean the dry-run was performed by this API.",
            "It does not mean source apply is allowed.",
            "Real source apply still requires accepted approval, policy satisfaction, source-apply gate success, and human review.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> source-apply gate -> source-apply dry-run receipt -> controlled source apply -> source-apply receipt -> rollback -> workflow continuation -> release readiness gate",
            "PR203 opens the rehearsal archive. It does not hand out launch codes."
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

    private static async Task SeedAsync(params SourceApplyDryRunReceipt[] receipts)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISourceApplyDryRunReceiptStore>();
        foreach (var receipt in receipts)
        {
            await store.SaveAsync(receipt);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.SourceApplyDryRunReceipt");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static SourceApplyDryRunReceipt ValidReceipt(string suffix = "main") => new()
    {
        SourceApplyDryRunReceiptId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        SourceApplyDryRunRequestId = Guid.NewGuid(),
        SourceApplyDryRunRequestHash = $"sha256:source-apply-dry-run-request-{suffix}",
        DryRunSatisfied = true,
        DryRunResultHash = $"sha256:source-apply-dry-run-result-{suffix}",
        SourceApplyRequestId = Guid.NewGuid(),
        SourceApplyRequestHash = $"sha256:source-apply-request-{suffix}",
        SourceApplyGateEvaluationId = Guid.NewGuid(),
        SourceApplyGateEvaluationHash = $"sha256:source-apply-gate-evaluation-{suffix}",
        PatchArtifactId = Guid.NewGuid(),
        PatchHash = $"sha256:patch-{suffix}",
        ChangeSetHash = $"sha256:change-set-{suffix}",
        RollbackSupportReceiptId = Guid.NewGuid(),
        RollbackSupportReceiptHash = $"sha256:rollback-support-receipt-{suffix}",
        SourceBaselineHash = $"sha256:source-baseline-{suffix}",
        WorkspaceBoundaryHash = $"sha256:workspace-boundary-{suffix}",
        ExpectedBranch = $"main-{suffix}",
        ExpectedCleanWorktreeHash = $"sha256:clean-worktree-{suffix}",
        FileResults =
        [
            new SourceApplyDryRunReceiptFileResult
            {
                Path = $"src/file-{suffix}.cs",
                PreviousPath = string.Empty,
                OperationKind = "ModifyFile",
                PatchArtifactChangeHash = $"sha256:patch-change-{suffix}",
                OperationHash = $"sha256:operation-{suffix}",
                ExpectedBeforeContentHash = $"sha256:before-{suffix}",
                ExpectedAfterContentHash = $"sha256:after-{suffix}",
                ObservedCurrentContentHash = $"sha256:observed-{suffix}",
                PreconditionsSatisfied = true,
                WouldCreate = false,
                WouldModify = true,
                WouldDelete = false,
                WouldRename = false,
                WouldNoop = false,
                IssueCodes = [],
                FileResultHash = $"sha256:file-result-{suffix}"
            }
        ],
        CreatedAtUtc = CreatedAtUtc,
        ExpiresAtUtc = ExpiresAtUtc,
        SourceApplyDryRunReceiptHash = $"sha256:source-apply-dry-run-receipt-{suffix}",
        EvidenceReferences = [$"source-apply-gate-evaluation:{suffix}", $"patch-artifact:{suffix}"],
        BoundaryMaxims = ["Source-apply dry-run receipt records rehearsal evidence only."],
        Boundary = SourceApplyDryRunReceiptBoundaryText.Boundary
    };

    private static void AssertItems(JsonElement root, params Guid[] expectedIds)
    {
        AssertReadOnlyEnvelope(root);
        var ids = root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sourceApplyDryRunReceiptId").GetGuid()).ToArray();
        CollectionAssert.AreEquivalent(expectedIds, ids);
    }

    private static void AssertReadOnlyEnvelope(JsonElement root)
    {
        Assert.IsFalse(root.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(root.GetProperty("humanApprovalRequired").GetBoolean());
        StringAssert.Contains(root.GetProperty("warnings")[0].GetString(), "Source-apply dry-run receipt read API is read-only.");
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("readCreatesSourceApplyDryRunReceipt").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readPerformsDryRun").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readAppliesSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readWritesFiles").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readAppliesPatch").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readRunsGit").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readInspectsWorktree").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readInfersReleaseReadiness").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readSatisfiesApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readSatisfiesPolicy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readPromotesMemory").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readActivatesRetrieval").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
    }

    private static void AssertNoSourceApplyAuthority(string text)
    {
        foreach (var token in new[]
        {
            "canApplySource",
            "sourceApplyApproved",
            "sourceApplied\":true",
            "dryRunPerformed\":true",
            "filesWritten\":true",
            "patchApplied\":true",
            "appliedAtUtc",
            "applyResult",
            "canContinueWorkflow",
            "canApproveRelease",
            "releaseReady",
            "approvalSatisfied\":true",
            "policySatisfied\":true",
            "mutationOccurred\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained source-apply authority token: {token}");
        }
    }

    private static IEnumerable<MethodInfo> ReadRouteMethods()
    {
        var routeNames = new[]
        {
            "Get",
            "GetByReceiptHash",
            "ListBySourceApplyRequest",
            "ListBySourceApplyGateEvaluation",
            "ListByPatchArtifact",
            "ListByRollbackSupportReceipt"
        };

        return typeof(SourceApplyDryRunReceiptsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(SourceApplyDryRunReceiptsV1Controller))
            .Where(method => routeNames.Contains(method.Name, StringComparer.Ordinal));
    }

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in Pr203ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
            }
        }
    }

    private static string[] Pr203ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            ControllerPath(),
            QueryServicePath(),
            Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyDryRunReceiptReadModels.cs")
        ];
    }

    private static string[] Pr203ChangedFiles()
    {
        return
        [
            "IronDev.Core/Governance/SourceApplyDryRunReceiptReadModels.cs",
            "IronDev.Infrastructure/Governance/SourceApplyDryRunReceiptQueryService.cs",
            "IronDev.Api/Controllers/SourceApplyDryRunReceiptsV1Controller.cs",
            "IronDev.Api/Program.cs",
            "Docs/receipts/PR203_SOURCE_APPLY_READ_API.md",
            "IronDev.IntegrationTests.Api/SourceApplyDryRunReceiptReadApiTests.cs"
        ];
    }

    private static string ControllerPath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "SourceApplyDryRunReceiptsV1Controller.cs");

    private static string QueryServicePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "SourceApplyDryRunReceiptQueryService.cs");

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

using System.Net;
using System.Reflection;
using System.Text.Json;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class WorkflowTransitionRecordReadApiTests : ApiTestBase
{
    private static readonly DateTimeOffset TransitionedAtUtc = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_GetByIdReturnsRecord()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord("get-by-id");
        await SeedAsync(record);
        var before = await CountAsync();

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/workflow-transition-records/{record.WorkflowTransitionRecordId}");
        var json = await ReadJsonAsync(response);
        var after = await CountAsync();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not mutate workflow transition records.");
        AssertReadOnlyEnvelope(json.RootElement);
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoContinuationAuthority(text);

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(record.WorkflowTransitionRecordId, data.GetProperty("workflowTransitionRecordId").GetGuid());
        Assert.AreEqual(record.ProjectId, data.GetProperty("projectId").GetGuid());
        Assert.AreEqual(record.WorkflowRunId, data.GetProperty("workflowRunId").GetString());
        Assert.AreEqual(record.WorkflowStepId, data.GetProperty("workflowStepId").GetString());
        Assert.AreEqual(record.TransitionKind, data.GetProperty("transitionKind").GetString());
        Assert.AreEqual(record.WorkflowContinuationGateEvaluationId, data.GetProperty("workflowContinuationGateEvaluationId").GetGuid());
        Assert.AreEqual(record.SourceApplyReceiptId, data.GetProperty("sourceApplyReceiptId").GetGuid());
        Assert.AreEqual(record.RollbackExecutionReceiptId, data.GetProperty("rollbackExecutionReceiptId").GetGuid());
        Assert.AreEqual(record.WorkflowTransitionRecordHash, data.GetProperty("workflowTransitionRecordHash").GetString());
        Assert.IsFalse(data.GetProperty("mutationOccurredInThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("workflowContinuationExecutedByThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseReadinessInferredByThisApi").GetBoolean());
        Assert.IsFalse(data.GetProperty("releaseApprovedByThisApi").GetBoolean());
        Assert.IsTrue(data.GetProperty("humanReviewRequired").GetBoolean());
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_GetByHashReturnsRecord()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord("get-by-hash");
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/workflow-transition-records/by-hash/{record.WorkflowTransitionRecordHash}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(record.WorkflowTransitionRecordId, json.RootElement.GetProperty("data").GetProperty("workflowTransitionRecordId").GetGuid());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ListByWorkflowRunReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var workflowRunId = "workflow-run-pr213";
        var first = Rehash(ValidRecord("run-a") with { ProjectId = projectId, WorkflowRunId = workflowRunId });
        var second = Rehash(ValidRecord("run-b") with { ProjectId = projectId, WorkflowRunId = workflowRunId });
        var otherRun = Rehash(ValidRecord("run-c") with { ProjectId = projectId, WorkflowRunId = "other-run-pr213" });
        var otherProject = Rehash(ValidRecord("run-d") with { ProjectId = Guid.NewGuid(), WorkflowRunId = workflowRunId });
        await SeedAsync(first, second, otherRun, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/workflow-transition-records/by-workflow-run/{workflowRunId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, first.WorkflowTransitionRecordId, second.WorkflowTransitionRecordId);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ListByWorkflowStepReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var workflowRunId = "workflow-run-step-pr213";
        var workflowStepId = "workflow-step-pr213";
        var matching = Rehash(ValidRecord("step-a") with { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowStepId = workflowStepId });
        var otherStep = Rehash(ValidRecord("step-b") with { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowStepId = "other-step-pr213" });
        var otherProject = Rehash(ValidRecord("step-c") with { ProjectId = Guid.NewGuid(), WorkflowRunId = workflowRunId, WorkflowStepId = workflowStepId });
        await SeedAsync(matching, otherStep, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/workflow-transition-records/by-workflow-step/{workflowRunId}/{workflowStepId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.WorkflowTransitionRecordId);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ListByContinuationGateEvaluationReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("gate-a") with { ProjectId = projectId, WorkflowContinuationGateEvaluationId = gateId });
        var otherGate = Rehash(ValidRecord("gate-b") with { ProjectId = projectId, WorkflowContinuationGateEvaluationId = Guid.NewGuid() });
        var otherProject = Rehash(ValidRecord("gate-c") with { ProjectId = Guid.NewGuid(), WorkflowContinuationGateEvaluationId = gateId });
        await SeedAsync(matching, otherGate, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/workflow-transition-records/by-continuation-gate/{gateId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.WorkflowTransitionRecordId);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ListBySourceApplyReceiptReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var sourceApplyReceiptId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("source-a") with { ProjectId = projectId, SourceApplyReceiptId = sourceApplyReceiptId });
        var otherSource = Rehash(ValidRecord("source-b") with { ProjectId = projectId, SourceApplyReceiptId = Guid.NewGuid() });
        var otherProject = Rehash(ValidRecord("source-c") with { ProjectId = Guid.NewGuid(), SourceApplyReceiptId = sourceApplyReceiptId });
        await SeedAsync(matching, otherSource, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/workflow-transition-records/by-source-apply-receipt/{sourceApplyReceiptId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.WorkflowTransitionRecordId);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ListByRollbackExecutionReceiptReturnsProjectScopedRecords()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();
        var rollbackReceiptId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("rollback-a") with { ProjectId = projectId, RollbackExecutionReceiptId = rollbackReceiptId, RollbackExecutionReceiptHash = "sha256:rollback-execution-receipt-shared" });
        var otherRollback = Rehash(ValidRecord("rollback-b") with { ProjectId = projectId, RollbackExecutionReceiptId = Guid.NewGuid(), RollbackExecutionReceiptHash = "sha256:rollback-execution-receipt-other" });
        var otherProject = Rehash(ValidRecord("rollback-c") with { ProjectId = Guid.NewGuid(), RollbackExecutionReceiptId = rollbackReceiptId, RollbackExecutionReceiptHash = "sha256:rollback-execution-receipt-shared" });
        await SeedAsync(matching, otherRollback, otherProject);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/workflow-transition-records/by-rollback-execution-receipt/{rollbackReceiptId}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        AssertItems(json.RootElement, matching.WorkflowTransitionRecordId);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_MissingRecordReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/workflow-transition-records/{Guid.NewGuid()}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_EmptyListReturnsEmptyArray()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/workflow-transition-records/by-workflow-run/no-matches-pr213");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("records").GetArrayLength());
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("count").GetInt32());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ResponseSaysHumanReviewRequired()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord("human-review");
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/workflow-transition-records/{record.WorkflowTransitionRecordId}");
        var json = await ReadJsonAsync(response);

        Assert.IsTrue(json.RootElement.GetProperty("humanReviewRequired").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("humanReviewRequired").GetBoolean());
        StringAssert.Contains(json.RootElement.ToString(), "Human review remains required.");
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ResponseDoesNotInferReleaseReadinessOrApproveReleaseOrContinueWorkflow()
    {
        using var client = await AuthedClientAsync();
        var record = ValidRecord("boundary-response");
        await SeedAsync(record);

        var response = await client.GetAsync($"/api/v1/projects/{record.ProjectId}/workflow-transition-records/{record.WorkflowTransitionRecordId}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        AssertReadOnlyEnvelope(json.RootElement);
        AssertNoContinuationAuthority(text);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_RejectsUnsafeHashRouteValue()
    {
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/workflow-transition-records/by-hash/sha256:rawPromptLeak-pr213");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(text.Contains("rawPromptLeak-pr213", StringComparison.OrdinalIgnoreCase), "Unsafe route value must not be echoed.");
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_RejectsUnsafeWorkflowRunRouteValue()
    {
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/workflow-transition-records/by-workflow-run/release%20ready");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_RejectsUnsafeWorkflowStepRouteValue()
    {
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/workflow-transition-records/by-workflow-step/safe-run/raw%20tool%20output");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        AssertReadOnlyEnvelope(json.RootElement);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordReadApi_ClampsOrRejectsInvalidTake()
    {
        using var client = await AuthedClientAsync();
        var projectId = Guid.NewGuid();

        foreach (var take in new[] { 0, -1, 501 })
        {
            var response = await client.GetAsync($"/api/v1/projects/{projectId}/workflow-transition-records/by-workflow-run/safe-run?take={take}");
            var json = await ReadJsonAsync(response);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
            AssertReadOnlyEnvelope(json.RootElement);
        }
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_ResponseDoesNotExposeRawPrivateMaterial()
    {
        var unsafeRecord = ValidRecord("unsafe-read") with
        {
            WorkflowRunId = "rawPromptLeak-pr213",
            WorkflowStepId = "private reasoning leak",
            EvidenceReferences = ["raw completion evidence"],
            BoundaryMaxims = ["chain-of-thought maxim"],
            Boundary = "hidden reasoning boundary"
        };
        var query = new WorkflowTransitionRecordQueryService(new FakeWorkflowTransitionRecordStore(unsafeRecord));

        var read = query.GetAsync(unsafeRecord.ProjectId, unsafeRecord.WorkflowTransitionRecordId).GetAwaiter().GetResult();
        var json = JsonSerializer.Serialize(read);

        Assert.IsNotNull(read);
        Assert.IsFalse(json.Contains("rawPromptLeak-pr213", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("private reasoning leak", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("raw completion evidence", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("chain-of-thought maxim", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("hidden reasoning boundary", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(json, "[redacted: sensitive workflow transition record text]");
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_DoesNotExposePostPutPatchDelete()
    {
        var methods = ReadRouteMethods().ToArray();

        Assert.AreEqual(7, methods.Length);
        Assert.IsTrue(methods.All(method => method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_ControllerDoesNotDependOnStoreSave()
    {
        var controller = File.ReadAllText(ControllerPath());

        StringAssert.Contains(controller, "IWorkflowTransitionRecordQueryService");
        Assert.IsFalse(controller.Contains("IWorkflowTransitionRecordStore", StringComparison.Ordinal), "Controller must not depend on write-capable store directly.");
        Assert.IsFalse(controller.Contains("SqlWorkflowTransitionRecordStore", StringComparison.Ordinal), "Controller must not depend on SQL store directly.");
        Assert.IsFalse(controller.Contains("SaveAsync", StringComparison.Ordinal), "Controller must not save transition records.");
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_QueryServiceUsesReadOnlyStoreMethods()
    {
        var queryService = File.ReadAllText(QueryServicePath());

        StringAssert.Contains(queryService, "IWorkflowTransitionRecordStore");
        StringAssert.Contains(queryService, "_store.GetAsync");
        StringAssert.Contains(queryService, "_store.GetByRecordHashAsync");
        StringAssert.Contains(queryService, "_store.ListByWorkflowRunAsync");
        StringAssert.Contains(queryService, "_store.ListByWorkflowStepAsync");
        StringAssert.Contains(queryService, "_store.ListByContinuationGateEvaluationAsync");
        StringAssert.Contains(queryService, "_store.ListBySourceApplyReceiptAsync");
        StringAssert.Contains(queryService, "_store.ListByRollbackExecutionReceiptAsync");
        Assert.IsFalse(queryService.Contains(".SaveAsync", StringComparison.Ordinal), "Query service must not save workflow transition records.");
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_ReadModelsDoNotClaimMutationContinuationOrRelease()
    {
        var model = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Governance", "WorkflowTransitionRecordReadModels.cs"));

        StringAssert.Contains(model, "MutationOccurredInThisApi");
        StringAssert.Contains(model, "WorkflowContinuationExecutedByThisApi");
        StringAssert.Contains(model, "ReleaseReadinessInferredByThisApi");
        StringAssert.Contains(model, "ReleaseApprovedByThisApi");
        StringAssert.Contains(model, "HumanReviewRequired");
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_DoesNotAddMutationRuntimeOrAuthorityTokens()
    {
        foreach (var token in new[]
        {
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "File.WriteAllText",
            "File.WriteAllBytes",
            "File.Delete",
            "File.Move",
            "Directory.CreateDirectory",
            "Process.Start",
            "ProcessStartInfo",
            "IHostedService",
            "BackgroundService",
            "Scheduler",
            "WorkflowTransitionExecutor",
            "WorkflowContinuationExecutor",
            "ContinueWorkflow",
            "AdvanceWorkflow",
            "StartNextStep",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding"
        })
        {
            AssertNoProductionTokens(token);
        }
    }

    [TestMethod]
    public void WorkflowTransitionRecordReadApi_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR213_WORKFLOW_CONTINUATION_READ_API.md"));

        foreach (var statement in new[]
        {
            "PR213 adds workflow continuation read API only.",
            "PR213 exposes stored WorkflowTransitionRecord evidence for review.",
            "PR213 does not continue workflow.",
            "PR213 does not mutate workflow state.",
            "PR213 does not transition workflow.",
            "PR213 does not complete workflow steps.",
            "PR213 does not start next workflow steps.",
            "PR213 does not add SQL.",
            "PR213 does not add CLI.",
            "PR213 does not add UI.",
            "PR213 does not add runtime execution.",
            "PR213 does not approve release.",
            "PR213 does not infer release readiness.",
            "PR213 does not execute rollback.",
            "Workflow continuation read API is not workflow continuation.",
            "Workflow continuation read API is not workflow state mutation.",
            "Workflow continuation read API is not workflow transition.",
            "Workflow continuation read API is not release readiness.",
            "Workflow continuation read API is not release approval.",
            "Read WorkflowTransitionRecord is evidence only.",
            "Read WorkflowTransitionRecord is not WorkflowContinued.",
            "Read WorkflowTransitionRecord is not ReleaseReady.",
            "Read WorkflowTransitionRecord is not ReleaseApproved.",
            "Human review remains required.",
            "PR213 opens the movement-receipt window. It does not press continue."
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

    private static async Task SeedAsync(params WorkflowTransitionRecord[] records)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowTransitionRecordStore>();
        foreach (var record in records)
        {
            await store.SaveAsync(record);
        }
    }

    private static async Task<int> CountAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.WorkflowTransitionRecord");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static WorkflowTransitionRecord ValidRecord(string suffix = "main") => Rehash(new WorkflowTransitionRecord
    {
        WorkflowTransitionRecordId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        WorkflowRunId = $"workflow-run-{suffix}",
        WorkflowStepId = $"workflow-step-{suffix}",
        TransitionKind = WorkflowTransitionKinds.ContinueToNextStep,
        PreviousWorkflowStateHash = $"sha256:previous-workflow-state-{suffix}",
        NewWorkflowStateHash = $"sha256:new-workflow-state-{suffix}",
        PreviousStepStateHash = $"sha256:previous-step-state-{suffix}",
        NewStepStateHash = $"sha256:new-step-state-{suffix}",
        PreviousStepId = $"previous-step-{suffix}",
        NextStepId = $"next-step-{suffix}",
        WorkflowContinuationGateEvaluationId = Guid.NewGuid(),
        WorkflowContinuationGateEvaluationHash = $"sha256:workflow-continuation-gate-{suffix}",
        SourceApplyRequestId = Guid.NewGuid(),
        SourceApplyRequestHash = $"sha256:source-apply-request-{suffix}",
        SourceApplyReceiptId = Guid.NewGuid(),
        SourceApplyReceiptHash = $"sha256:source-apply-receipt-{suffix}",
        RollbackExecutionReceiptId = Guid.NewGuid(),
        RollbackExecutionReceiptHash = $"sha256:rollback-execution-receipt-{suffix}",
        RollbackExecutionAuditReportId = Guid.NewGuid(),
        RollbackExecutionAuditReportHash = $"sha256:rollback-execution-audit-report-{suffix}",
        WorkflowStateMutated = true,
        StepCompleted = true,
        NextStepStarted = true,
        ReleaseReadinessInferred = false,
        ReleaseApproved = false,
        SourceApplyExecuted = false,
        RollbackExecuted = false,
        TransitionedAtUtc = TransitionedAtUtc,
        WorkflowTransitionRecordHash = "sha256:placeholder",
        EvidenceReferences = [$"workflow-continuation-gate:{suffix}", $"source-apply-receipt:{suffix}"],
        BoundaryMaxims = ["Workflow transition record is evidence only."],
        Boundary = WorkflowTransitionRecordBoundaryText.Boundary
    });

    private static WorkflowTransitionRecord Rehash(WorkflowTransitionRecord record) =>
        record with { WorkflowTransitionRecordHash = WorkflowTransitionRecordHashing.ComputeRecordHash(record) };

    private static void AssertItems(JsonElement root, params Guid[] expectedIds)
    {
        AssertReadOnlyEnvelope(root);
        var data = root.GetProperty("data");
        var ids = data.GetProperty("records").EnumerateArray().Select(item => item.GetProperty("workflowTransitionRecordId").GetGuid()).ToArray();
        CollectionAssert.AreEquivalent(expectedIds, ids);
        Assert.AreEqual(expectedIds.Length, data.GetProperty("count").GetInt32());
    }

    private static void AssertReadOnlyEnvelope(JsonElement root)
    {
        Assert.IsFalse(root.GetProperty("mutationOccurredInThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("workflowContinuationExecutedByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("releaseReadinessInferredByThisApi").GetBoolean());
        Assert.IsFalse(root.GetProperty("releaseApprovedByThisApi").GetBoolean());
        Assert.IsTrue(root.GetProperty("humanReviewRequired").GetBoolean());
        StringAssert.Contains(root.GetProperty("warnings")[0].GetString(), "Workflow transition record read API is read-only.");
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("readCreatesWorkflowTransitionRecord").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readMutatesWorkflowState").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readContinuesWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readTransitionsWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readCompletesWorkflowStep").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readStartsNextWorkflowStep").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readEvaluatesContinuationGate").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readSatisfiesContinuationGate").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readInfersReleaseReadiness").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readApprovesRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readExecutesSourceApply").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("readExecutesRollback").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequired").GetBoolean());
    }

    private static void AssertNoContinuationAuthority(string text)
    {
        foreach (var token in new[]
        {
            "mutationOccurredInThisApi\":true",
            "workflowContinuationExecutedByThisApi\":true",
            "releaseReadinessInferredByThisApi\":true",
            "releaseApprovedByThisApi\":true",
            "canContinueWorkflow",
            "canApproveRelease",
            "releaseReady\":true",
            "workflowContinued\":true",
            "sourceApplyAuthorized\":true",
            "rollbackCleanupDeclared\":true"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained authority token: {token}");
        }
    }

    private static IEnumerable<MethodInfo> ReadRouteMethods()
    {
        var routeNames = new[]
        {
            "Get",
            "GetByRecordHash",
            "ListByWorkflowRun",
            "ListByWorkflowStep",
            "ListByContinuationGateEvaluation",
            "ListBySourceApplyReceipt",
            "ListByRollbackExecutionReceipt"
        };

        return typeof(WorkflowTransitionRecordsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(WorkflowTransitionRecordsController))
            .Where(method => routeNames.Contains(method.Name, StringComparer.Ordinal));
    }

    private static void AssertNoProductionTokens(params string[] tokens)
    {
        foreach (var file in Pr213ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden token found in {file}: {token}");
            }
        }
    }

    private static string[] Pr213ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            ControllerPath(),
            QueryServicePath(),
            Path.Combine(root, "IronDev.Core", "Governance", "WorkflowTransitionRecordReadModels.cs")
        ];
    }

    private static string ControllerPath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "WorkflowTransitionRecordsController.cs");

    private static string QueryServicePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Infrastructure", "Governance", "WorkflowTransitionRecordQueryService.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeWorkflowTransitionRecordStore : IWorkflowTransitionRecordStore
    {
        private readonly WorkflowTransitionRecord _record;

        public FakeWorkflowTransitionRecordStore(WorkflowTransitionRecord record) => _record = record;

        public Task SaveAsync(WorkflowTransitionRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fake read store must not save.");

        public Task<WorkflowTransitionRecord?> GetAsync(Guid projectId, Guid workflowTransitionRecordId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowTransitionRecord?>(_record);

        public Task<WorkflowTransitionRecord?> GetByRecordHashAsync(Guid projectId, string workflowTransitionRecordHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowTransitionRecord?>(_record);

        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([_record]);

        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowStepAsync(Guid projectId, string workflowRunId, string workflowStepId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([_record]);

        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByContinuationGateEvaluationAsync(Guid projectId, Guid workflowContinuationGateEvaluationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([_record]);

        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([_record]);

        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByRollbackExecutionReceiptAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([_record]);
    }
}

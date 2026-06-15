using System.Data;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class FailedWorkflowDiagnosisReportApiContractTests : ApiTestBase
{
    private readonly Guid _projectReferenceId = Guid.NewGuid();
    private readonly Guid _otherProjectReferenceId = Guid.NewGuid();
    private readonly Guid _correlationId = Guid.NewGuid();
    private readonly Guid _otherCorrelationId = Guid.NewGuid();
    private readonly string _workflowRunId = $"workflow-run-pr146-{Guid.NewGuid():N}";
    private readonly string _workflowStepId = $"workflow-step-pr146-{Guid.NewGuid():N}";
    private const string PayloadMarker = "payload-marker-pr146-must-not-appear";

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_Get_IsGetOnly()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_HasNoPostPutPatchDeleteRoutes()
    {
        using var client = await AuthedClientAsync();
        foreach (var (method, route) in new[]
        {
            (HttpMethod.Post, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report"),
            (HttpMethod.Put, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report"),
            (HttpMethod.Patch, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report"),
            (HttpMethod.Delete, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report"),
            (HttpMethod.Post, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report/repair"),
            (HttpMethod.Post, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report/retry"),
            (HttpMethod.Post, $"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report/create-ticket")
        })
        {
            using var request = new HttpRequestMessage(method, route);
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Unsupported failed workflow diagnosis route unexpectedly succeeded: {method} {route}");
        }
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_ReturnsSafeReportForFailedWorkflowTrace()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&includeTraceTimeline=true&includeRecommendations=true");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("report_available", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        var report = json.RootElement.GetProperty("data").GetProperty("report");
        Assert.IsTrue(report.GetProperty("isReportOnly").GetBoolean());
        Assert.IsFalse(report.GetProperty("isRootCauseProof").GetBoolean());
        Assert.IsTrue(report.GetProperty("signals").GetArrayLength() >= 1);
        Assert.IsTrue(report.GetProperty("traceTimeline").GetArrayLength() >= 1);
        Assert.IsTrue(report.GetProperty("recommendations").GetArrayLength() >= 1);
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_ReturnsNoWorkflowEvidenceForUnknownWorkflow()
    {
        await SeedTraceAsync(eventType: "workflow.failed", subjectId: "different-workflow-pr146");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("no_workflow_evidence_found", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("report").ValueKind);
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_ReturnsNoFailureEvidenceWhenTraceHasNoFailureSignal()
    {
        await SeedTraceAsync(eventType: "workflow.trace.recorded");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("no_failure_evidence_found", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("report").GetProperty("signals").GetArrayLength());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_CanIncludeTraceTimeline()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&includeTraceTimeline=true"));

        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("report").GetProperty("traceTimeline").GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_CanOmitTraceTimeline()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}"));

        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("report").GetProperty("traceTimeline").GetArrayLength());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_CanIncludeRecommendations()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&includeRecommendations=true"));

        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("report").GetProperty("recommendations").GetArrayLength() >= 1);
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_CanOmitRecommendations()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}"));

        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("report").GetProperty("recommendations").GetArrayLength());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_CanFilterByCorrelation()
    {
        await SeedTraceAsync(eventType: "workflow.failed", correlationId: _correlationId);
        await SeedTraceAsync(eventType: "workflow.failed", correlationId: _otherCorrelationId, subjectId: "other-workflow-pr146");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?correlationId={_correlationId:D}"));

        Assert.AreEqual("report_available", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_CanFilterByWorkflowStep()
    {
        await SeedTraceAsync(eventType: "step.failed", subjectType: "workflow_step", subjectId: _workflowStepId, correlationId: _correlationId);
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&workflowStepId={_workflowStepId}&correlationId={_correlationId:D}"));

        Assert.AreEqual("report_available", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_InvalidMissingWorkflowRunId_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/%20/diagnosis-report?projectReferenceId={_projectReferenceId:D}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "workflowRunId");
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_InvalidProjectReferenceId_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId=not-a-guid");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_project_reference_id");
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_InvalidCorrelationId_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?correlationId=not-a-guid");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_correlation_id");
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_InvalidTakeTraceItems_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&takeTraceItems=0");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_take_trace_items");
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_UnsafeQueryText_ReturnsValidationErrorWithoutEchoingMarker()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/rawPrompt-leaked/diagnosis-report?projectReferenceId={_projectReferenceId:D}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "unsafe_query_text");
        Assert.IsFalse(text.Contains("rawPrompt-leaked", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_ResponseDoesNotExposeRawPayloadJson()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&includeTraceTimeline=true");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(text.Contains(PayloadMarker, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_ResponseDoesNotExposePrivateReasoningFields()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}&includeTraceTimeline=true"));
        var data = json.RootElement.GetProperty("data").ToString();

        Assert.IsFalse(data.Contains("chainOfThought", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(data.Contains("rawPrompt", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(data.Contains("rawCompletion", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(data.Contains("rawToolOutput", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_BoundaryFlagsAreFalse()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}"));

        AssertBoundary(json.RootElement.GetProperty("boundary"));
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_DoesNotLeakCrossProjectTrace()
    {
        await SeedTraceAsync(eventType: "workflow.failed", projectReferenceId: _otherProjectReferenceId);
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}"));

        Assert.AreEqual("no_workflow_evidence_found", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task FailedWorkflowDiagnosisReport_DoesNotCreateGovernanceEvent()
    {
        await SeedTraceAsync(eventType: "workflow.failed");
        var before = await CountGovernanceEventsAsync(_projectReferenceId);
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/workflow/failures/{_workflowRunId}/diagnosis-report?projectReferenceId={_projectReferenceId:D}");
        var after = await CountGovernanceEventsAsync(_projectReferenceId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(before, after);
    }

    private async Task<SeededTrace> SeedTraceAsync(
        string eventType = "workflow.trace.recorded",
        string actorType = "workflow",
        string actorId = "workflow-diagnosis-api-test",
        string subjectType = "workflow_run",
        string? subjectId = null,
        Guid? projectReferenceId = null,
        Guid? correlationId = null,
        Guid? causationId = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var row = await connection.QuerySingleAsync<SeededTraceRow>(
            "governance.AppendGovernanceEvent",
            new
            {
                EventId = Guid.NewGuid(),
                ProjectId = projectReferenceId ?? _projectReferenceId,
                EventType = eventType,
                ActorType = actorType,
                ActorId = actorId,
                CorrelationId = correlationId ?? _correlationId,
                CausationId = causationId ?? Guid.NewGuid(),
                SubjectType = subjectType,
                SubjectId = subjectId ?? _workflowRunId,
                PayloadVersion = 1,
                PayloadJson = $"{{\"schema\":\"failed.workflow.diagnosis.api.test.v1\",\"payloadMarker\":\"{PayloadMarker}\"}}"
            },
            commandType: CommandType.StoredProcedure);

        return new SeededTrace(row.EventId, row.ProjectId, row.CorrelationId, row.CausationId, row.SubjectId);
    }

    private static async Task<int> CountGovernanceEventsAsync(Guid projectId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "select count(*) from governance.GovernanceEvent where ProjectId = @ProjectId",
            new { ProjectId = projectId });
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsTrue(boundary.GetProperty("readOnlyReport").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("diagnosisIsRootCauseProof").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsRepair").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsWorkflowRetry").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsWorkflowResume").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsWorkflowTransition").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsTicketCreation").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsPolicySatisfaction").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsGovernanceDecision").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolInvoked").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("agentDispatched").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelCalled").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("promptBuilt").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("retrievalActivated").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("updatesGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("deletesGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawPayloadJson").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesPrivateReasoning").GetBoolean());
    }

    private static void AssertNoUnsafeMaterial(string text)
    {
        foreach (var token in new[]
        {
            PayloadMarker,            "rawPrompt",
            "rawCompletion",
            "rawToolOutput",
            "chainOfThought",
            "api_key",
            "password"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Failed workflow diagnosis response leaked unsafe token: {token}");
        }
    }

    private sealed record SeededTrace(Guid EventId, Guid ProjectId, Guid? CorrelationId, Guid? CausationId, string? SubjectId);

    private sealed class SeededTraceRow
    {
        public Guid EventId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string? SubjectId { get; init; }
    }
}

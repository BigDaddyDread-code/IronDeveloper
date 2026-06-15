using System.Data;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ApprovalGateDogfoodCorrelationReportApiContractTests : ApiTestBase
{
    private readonly Guid _projectReferenceId = Guid.NewGuid();
    private readonly Guid _otherProjectReferenceId = Guid.NewGuid();
    private readonly Guid _correlationId = Guid.NewGuid();
    private readonly Guid _causationId = Guid.NewGuid();
    private readonly string _workflowRunId = $"workflow-run-pr147-{Guid.NewGuid():N}";
    private readonly string _workflowStepId = $"workflow-step-pr147-{Guid.NewGuid():N}";
    private readonly string _approvalReferenceId = $"approval-pr147-{Guid.NewGuid():N}";
    private readonly string _toolRequestId = $"tool-request-pr147-{Guid.NewGuid():N}";
    private readonly string _toolGateDecisionId = $"tool-gate-pr147-{Guid.NewGuid():N}";
    private readonly string _dogfoodReceiptId = $"dogfood-pr147-{Guid.NewGuid():N}";
    private const string PayloadMarker = "payload-marker-pr147-must-not-appear";

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_Get_IsGetOnly()
    {
        await SeedCompleteCorrelationAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync(Route());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_HasNoPostPutPatchDeleteRoutes()
    {
        using var client = await AuthedClientAsync();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, "/api/v1/governance/correlation-reports/approval-gate-dogfood");
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Unsupported correlation report route unexpectedly succeeded: {method}");
        }
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_ReturnsReportAvailableForCompleteCorrelation()
    {
        await SeedCompleteCorrelationAsync();
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route(includeTraceReferences: true, includeMissingEvidence: true, includeRecommendations: true)));
        var text = json.RootElement.ToString();

        Assert.AreEqual("report_available", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        var report = json.RootElement.GetProperty("data").GetProperty("report");
        Assert.IsTrue(report.GetProperty("isReportOnly").GetBoolean());
        Assert.IsFalse(report.GetProperty("isApprovalDecision").GetBoolean());
        Assert.AreEqual(1, report.GetProperty("approvalEvidence").GetArrayLength());
        Assert.IsTrue(report.GetProperty("toolGateEvidence").GetArrayLength() >= 1);
        Assert.AreEqual(1, report.GetProperty("dogfoodEvidence").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("missingEvidence").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("conflictSignals").GetArrayLength());
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_ReturnsEvidenceIncompleteWhenApprovalMissing()
    {
        await SeedTraceAsync("policy.decision.recorded", "policy_decision", "policy-pr147");
        await SeedTraceAsync("tool.gate.decision.recorded", "tool_gate_decision", _toolGateDecisionId);
        await SeedTraceAsync("dogfood.receipt.recorded", "dogfood_receipt", _dogfoodReceiptId);
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route()));
        var report = json.RootElement.GetProperty("data").GetProperty("report");

        Assert.AreEqual("evidence_incomplete", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(report.GetProperty("missingEvidence").GetArrayLength() >= 1);
        Assert.IsTrue(report.GetProperty("conflictSignals").EnumerateArray().Any(signal => signal.GetProperty("kind").GetInt32() == 1));
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_ReturnsConflictWhenGateBlockedButDogfoodPresent()
    {
        await SeedTraceAsync("approval.decision.recorded", "approval_decision", _approvalReferenceId);
        await SeedTraceAsync("policy.decision.recorded", "policy_decision", "policy-pr147");
        await SeedTraceAsync("tool.gate.blocked", "tool_gate_decision", _toolGateDecisionId);
        await SeedTraceAsync("dogfood.receipt.recorded", "dogfood_receipt", _dogfoodReceiptId);
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route()));

        Assert.AreEqual("evidence_incomplete", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("report").GetProperty("conflictSignals").EnumerateArray().Any(signal => signal.GetProperty("kind").GetInt32() == 2));
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_CanOmitTraceReferences()
    {
        await SeedCompleteCorrelationAsync();
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route(includeTraceReferences: false)));

        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("report").GetProperty("traceReferences").GetArrayLength());
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_CanOmitMissingEvidence()
    {
        await SeedTraceAsync("dogfood.receipt.recorded", "dogfood_receipt", _dogfoodReceiptId);
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route(includeMissingEvidence: false)));

        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("report").GetProperty("missingEvidence").GetArrayLength());
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_CanOmitRecommendations()
    {
        await SeedCompleteCorrelationAsync();
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route(includeRecommendations: false)));

        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("report").GetProperty("recommendations").GetArrayLength());
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_CanFilterByDogfoodReceipt()
    {
        await SeedCompleteCorrelationAsync();
        await SeedTraceAsync("dogfood.receipt.recorded", "dogfood_receipt", "other-dogfood-pr147", workflowStepId: "other-step-pr147");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"{Route()}&dogfoodReceiptId={_dogfoodReceiptId}"));

        var dogfood = json.RootElement.GetProperty("data").GetProperty("report").GetProperty("dogfoodEvidence");
        Assert.IsTrue(dogfood.EnumerateArray().Any(item => item.GetProperty("dogfoodReceiptId").GetString() == _dogfoodReceiptId));
        Assert.IsFalse(dogfood.EnumerateArray().Any(item => item.GetProperty("dogfoodReceiptId").GetString() == "other-dogfood-pr147"));
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_DoesNotLeakCrossProjectTrace()
    {
        await SeedCompleteCorrelationAsync(projectReferenceId: _otherProjectReferenceId);
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route()));

        Assert.AreEqual("no_evidence_found", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("report").ValueKind);
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_InvalidMissingSelector_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/governance/correlation-reports/approval-gate-dogfood");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "missing_selector");
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_InvalidProject_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/governance/correlation-reports/approval-gate-dogfood?projectReferenceId=not-a-guid");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_project_reference_id");
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_InvalidCausation_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/correlation-reports/approval-gate-dogfood?projectReferenceId={_projectReferenceId:D}&causationId=not-a-guid");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_causation_id");
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_InvalidTake_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/correlation-reports/approval-gate-dogfood?projectReferenceId={_projectReferenceId:D}&take=0");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_take");
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_UnsafeQueryText_ReturnsValidationErrorWithoutEchoingMarker()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/correlation-reports/approval-gate-dogfood?projectReferenceId={_projectReferenceId:D}&workflowRunId=rawPrompt-leaked");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "unsafe_query_text");
        Assert.IsFalse(text.Contains("rawPrompt-leaked", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_ResponseDoesNotExposeRawPayload()
    {
        await SeedCompleteCorrelationAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync(Route(includeTraceReferences: true));
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var dataText = json.RootElement.GetProperty("data").ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(dataText.Contains("payloadJson", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(dataText.Contains(PayloadMarker, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_BoundaryFlagsAreFalse()
    {
        await SeedCompleteCorrelationAsync();
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync(Route()));

        AssertBoundary(json.RootElement.GetProperty("boundary"));
    }

    [TestMethod]
    public async Task ApprovalGateDogfoodCorrelationReport_DoesNotCreateGovernanceEvent()
    {
        await SeedCompleteCorrelationAsync();
        var before = await CountGovernanceEventsAsync(_projectReferenceId);
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync(Route());
        var after = await CountGovernanceEventsAsync(_projectReferenceId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(before, after);
    }

    private async Task SeedCompleteCorrelationAsync(Guid? projectReferenceId = null)
    {
        await SeedTraceAsync("approval.decision.recorded", "approval_decision", _approvalReferenceId, projectReferenceId: projectReferenceId);
        await SeedTraceAsync("policy.decision.recorded", "policy_decision", "policy-pr147", projectReferenceId: projectReferenceId);
        await SeedTraceAsync("tool.request.recorded", "tool_request", _toolRequestId, projectReferenceId: projectReferenceId);
        await SeedTraceAsync("tool.gate.decision.recorded", "tool_gate_decision", _toolGateDecisionId, projectReferenceId: projectReferenceId);
        await SeedTraceAsync("dogfood.receipt.recorded", "dogfood_receipt", _dogfoodReceiptId, projectReferenceId: projectReferenceId);
    }

    private async Task<SeededTrace> SeedTraceAsync(
        string eventType,
        string subjectType,
        string subjectId,
        Guid? projectReferenceId = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        string? workflowStepId = null)
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
                ActorType = "governance-correlation-report-api-test",
                ActorId = "pr147",
                CorrelationId = correlationId ?? _correlationId,
                CausationId = causationId ?? _causationId,
                SubjectType = subjectType,
                SubjectId = subjectId,
                PayloadVersion = 1,
                PayloadJson = $"{{\"schema\":\"approval.gate.dogfood.correlation.api.test.v1\",\"workflowRunId\":\"{_workflowRunId}\",\"workflowStepId\":\"{workflowStepId ?? _workflowStepId}\",\"payloadMarker\":\"{PayloadMarker}\"}}"
            },
            commandType: CommandType.StoredProcedure);

        return new SeededTrace(row.EventId, row.ProjectId, row.CorrelationId, row.CausationId, row.SubjectId);
    }

    private string Route(
        bool includeTraceReferences = true,
        bool includeMissingEvidence = true,
        bool includeRecommendations = true) =>
        $"/api/v1/governance/correlation-reports/approval-gate-dogfood?projectReferenceId={_projectReferenceId:D}&correlationId={_correlationId:D}&includeTraceReferences={includeTraceReferences.ToString().ToLowerInvariant()}&includeMissingEvidence={includeMissingEvidence.ToString().ToLowerInvariant()}&includeRecommendations={includeRecommendations.ToString().ToLowerInvariant()}";

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
        Assert.IsFalse(boundary.GetProperty("correlationIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("correlationIsPolicySatisfaction").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("dogfoodReceiptIsReleaseApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolGateEvidenceIsToolExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportStatusIsGovernanceStatus").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("conflictSignalIsVerdict").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("recommendationIsExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("reportIsWorkflowTransition").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApprove").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canReject").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canSatisfyPolicy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canOpenGate").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canInvokeTool").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canMarkDogfoodPassed").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApproveRelease").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canTransitionWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canDispatchAgent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canCallModel").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canBuildPrompt").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canCreateTicket").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canPromoteMemory").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canActivateRetrieval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApplySource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApplyPatch").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsApprovalDecision").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsPolicyDecision").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsDogfoodReceipt").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawPayloadJson").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesPrivateReasoning").GetBoolean());
    }

    private static void AssertNoUnsafeMaterial(string text)
    {
        foreach (var token in new[]
        {
            PayloadMarker,
            "rawPrompt",
            "rawCompletion",
            "rawToolOutput",
            "chainOfThought",
            "source content",
            "patch payload",
            "api_key",
            "password"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Correlation report response leaked unsafe token: {token}");
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

using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class GovernanceTraceExplorerApiContractTests : ApiTestBase
{
    private static readonly Guid ProjectReferenceId = Guid.Parse("aaaaaaaa-1450-4000-8000-000000000001");
    private static readonly Guid OtherProjectReferenceId = Guid.Parse("aaaaaaaa-1450-4000-8000-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("bbbbbbbb-1450-4000-8000-000000000001");
    private static readonly Guid CausationId = Guid.Parse("cccccccc-1450-4000-8000-000000000001");
    private const string WorkflowRunId = "workflow-run-pr145";
    private const string WorkflowStepId = "workflow-step-pr145";
    private const string PayloadMarker = "payload-marker-pr145-must-not-appear";

    [TestMethod]
    public async Task GovernanceTraceExplorer_Search_IsGetOnly()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_GetByTraceId_IsGetOnly()
    {
        var seeded = await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces/{seeded.EventId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_GetByCorrelationId_IsGetOnly()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces/by-correlation/{CorrelationId:D}?projectReferenceId={ProjectReferenceId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_GetByWorkflowRunId_IsGetOnly()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces/by-workflow-run/{WorkflowRunId}?projectReferenceId={ProjectReferenceId:D}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_HasNoPostPutPatchDeleteRoutes()
    {
        using var client = await AuthedClientAsync();
        foreach (var (method, route) in new[]
        {
            (HttpMethod.Post, "/api/v1/governance/traces"),
            (HttpMethod.Put, "/api/v1/governance/traces/trace-1"),
            (HttpMethod.Patch, "/api/v1/governance/traces/trace-1"),
            (HttpMethod.Delete, "/api/v1/governance/traces/trace-1"),
            (HttpMethod.Post, "/api/v1/governance/traces/trace-1/approve"),
            (HttpMethod.Post, "/api/v1/governance/traces/trace-1/replay"),
            (HttpMethod.Post, "/api/v1/governance/traces/trace-1/apply-source")
        })
        {
            using var request = new HttpRequestMessage(method, route);
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Unsupported governance trace route unexpectedly succeeded: {method} {route}");
        }
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_Search_ReturnsSafeTraceListEnvelope()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}&take=10");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("trace_list_returned", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("traces").GetArrayLength() >= 1);
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_GetByTraceId_ReturnsSafeTraceDetailEnvelope()
    {
        var seeded = await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces/{seeded.EventId:D}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("trace_found", json.RootElement.GetProperty("status").GetString());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        Assert.AreEqual(seeded.EventId.ToString("D"), json.RootElement.GetProperty("data").GetProperty("trace").GetProperty("summary").GetProperty("traceId").GetString());
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_GetByCorrelationId_ReturnsTimeline()
    {
        await SeedTraceAsync(eventType: "tool.request.recorded", subjectType: "tool_request", subjectId: "tool-request-pr145");
        await SeedTraceAsync(eventType: "tool.gate.recorded", subjectType: "tool_gate", subjectId: "tool-gate-pr145");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces/by-correlation/{CorrelationId:D}?projectReferenceId={ProjectReferenceId:D}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("trace").GetProperty("timeline").GetArrayLength() >= 2);
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_GetByWorkflowRunId_ReturnsTimeline()
    {
        await SeedTraceAsync(subjectType: "workflow_run", subjectId: WorkflowRunId);
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces/by-workflow-run/{WorkflowRunId}?projectReferenceId={ProjectReferenceId:D}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("trace_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("trace").GetProperty("timeline").GetArrayLength() >= 1);
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_InvalidQuery_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}&fromUtc=2026-06-15T10:00:00Z&toUtc=2026-06-14T10:00:00Z");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "invalid_date_range");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_UnsafeQueryText_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}&subjectReferenceId=rawPrompt-leaked");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "unsafe_query_text");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_ResponseHasMutationOccurredFalse()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}"));

        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_ResponseIncludesReadOnlyBoundary()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}"));

        AssertBoundary(json.RootElement.GetProperty("boundary"));
        var warnings = string.Join("\n", json.RootElement.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));
        StringAssert.Contains(warnings, "Governance trace explorer is read-only.");
    }

    [TestMethod]
    public async Task GovernanceTraceExplorer_ResponseDoesNotIncludeRawPayloadJson()
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(text.Contains($"\"payloadJson\"", StringComparison.OrdinalIgnoreCase), "Governance trace response must not expose a payloadJson field.");
        Assert.IsFalse(text.Contains(PayloadMarker, StringComparison.OrdinalIgnoreCase), "Governance trace response must not expose stored payload content.");
    }

    [TestMethod] public async Task GovernanceTraceExplorer_ResponseDoesNotIncludePrivateReasoning() => await AssertTraceDataDoesNotExpose("private reasoning");
    [TestMethod] public async Task GovernanceTraceExplorer_ResponseDoesNotIncludeRawPrompt() => await AssertTraceResponseDoesNotExpose("rawPrompt");
    [TestMethod] public async Task GovernanceTraceExplorer_ResponseDoesNotIncludeRawCompletion() => await AssertTraceResponseDoesNotExpose("rawCompletion");
    [TestMethod] public async Task GovernanceTraceExplorer_ResponseDoesNotIncludeRawToolOutput() => await AssertTraceResponseDoesNotExpose("rawToolOutput");
    [TestMethod] public async Task GovernanceTraceExplorer_ResponseDoesNotIncludeSourceContent() => await AssertTraceResponseDoesNotExpose("source content");
    [TestMethod] public async Task GovernanceTraceExplorer_ResponseDoesNotIncludePatchPayload() => await AssertTraceResponseDoesNotExpose("patch payload");

    private static async Task AssertTraceResponseDoesNotExpose(string marker)
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Governance trace response leaked marker: {marker}");
        AssertNoUnsafeMaterial(text);
    }

    private static async Task AssertTraceDataDoesNotExpose(string marker)
    {
        await SeedTraceAsync();
        using var client = await AuthedClientAsync();
        var response = await client.GetAsync($"/api/v1/governance/traces?projectReferenceId={ProjectReferenceId:D}");
        var json = await ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var dataText = data.TryGetProperty("traces", out var traces)
            ? traces.ToString()
            : data.TryGetProperty("trace", out var trace)
                ? trace.ToString()
                : data.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        Assert.IsFalse(dataText.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Governance trace data leaked marker: {marker}");
    }

    private static async Task<SeededTrace> SeedTraceAsync(
        string eventType = "workflow.trace.recorded",
        string actorType = "workflow",
        string actorId = "workflow-trace-api-test",
        string subjectType = "workflow_run",
        string subjectId = WorkflowRunId,
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
                ProjectId = projectReferenceId ?? ProjectReferenceId,
                EventType = eventType,
                ActorType = actorType,
                ActorId = actorId,
                CorrelationId = correlationId ?? CorrelationId,
                CausationId = causationId ?? CausationId,
                SubjectType = subjectType,
                SubjectId = subjectId,
                PayloadVersion = 1,
                PayloadJson = $"{{\"schema\":\"governance.trace.api.test.v1\",\"payloadMarker\":\"{PayloadMarker}\"}}"
            },
            commandType: System.Data.CommandType.StoredProcedure);

        return new SeededTrace(row.EventId, row.ProjectId, row.CorrelationId, row.CausationId, row.SubjectId);
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
        Assert.IsTrue(boundary.GetProperty("readOnlyTrace").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceabilityIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsPolicySatisfaction").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsWorkflowTransition").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsToolInvocation").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsAgentDispatch").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsModelExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsMemoryPromotion").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsSourceApply").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("traceOutputIsPatchApply").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("updatesGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("deletesGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("replaysGovernance").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApprove").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canReject").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canSatisfyPolicy").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canTransitionWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canInvokeTool").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canDispatchAgent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canCallModel").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canPromoteMemory").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canActivateRetrieval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApplySource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("canApplyPatch").GetBoolean());
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
            "raw command output",
            "chain-of-thought",
            "source content",
            "patch payload",
            "approval token",
            "api_key",
            "password"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Governance trace API response leaked unsafe token: {token}");
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

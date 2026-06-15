using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class AgentRunHealthSummaryApiContractTests : ApiTestBase
{
    private static readonly Guid ProjectReferenceId = Guid.Parse("aaaaaaaa-1480-4000-8000-000000000001");
    private static readonly Guid OtherProjectReferenceId = Guid.Parse("aaaaaaaa-1480-4000-8000-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("bbbbbbbb-1480-4000-8000-000000000001");
    private static readonly Guid CausationId = Guid.Parse("cccccccc-1480-4000-8000-000000000001");
    private const string AgentRunId = "agent-run-pr148";
    private const string PayloadMarker = "payload-marker-pr148-must-not-appear";

    [TestMethod]
    public async Task AgentRunHealthSummary_ListRoute_IsGetOnly()
    {
        await SeedTraceAsync(subjectId: AgentRunId);
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/health-summary?projectReferenceId={ProjectReferenceId:D}&agentRunId={AgentRunId}&includeGateSignals=false&includeApprovalSignals=false&includePolicySignals=false&includeDogfoodSignals=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_DetailRoute_IsGetOnly()
    {
        await SeedTraceAsync(subjectId: AgentRunId);
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/{AgentRunId}/health-summary?projectReferenceId={ProjectReferenceId:D}&includeGateSignals=false&includeApprovalSignals=false&includePolicySignals=false&includeDogfoodSignals=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_HasNoPostPutPatchDeleteRoutes()
    {
        using var client = await AuthedClientAsync();
        foreach (var (method, route) in new[]
        {
            (HttpMethod.Post, "/api/v1/agents/runs/health-summary"),
            (HttpMethod.Put, $"/api/v1/agents/runs/{AgentRunId}/health-summary"),
            (HttpMethod.Patch, $"/api/v1/agents/runs/{AgentRunId}/health-summary"),
            (HttpMethod.Delete, $"/api/v1/agents/runs/{AgentRunId}/health-summary"),
            (HttpMethod.Post, $"/api/v1/agents/runs/{AgentRunId}/health-summary/restart"),
            (HttpMethod.Post, $"/api/v1/agents/runs/{AgentRunId}/health-summary/retry"),
            (HttpMethod.Post, $"/api/v1/agents/runs/{AgentRunId}/health-summary/resume"),
            (HttpMethod.Post, $"/api/v1/agents/runs/{AgentRunId}/health-summary/apply-source"),
            (HttpMethod.Post, $"/api/v1/agents/runs/{AgentRunId}/health-summary/promote-memory")
        })
        {
            using var request = new HttpRequestMessage(method, route);
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Unsupported agent run health route unexpectedly succeeded: {method} {route}");
        }
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_ReturnsSafeReadOnlyEnvelope()
    {
        await SeedTraceAsync(subjectId: AgentRunId, eventType: "agent.run.completed");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/{AgentRunId}/health-summary?projectReferenceId={ProjectReferenceId:D}&includeGateSignals=false&includeApprovalSignals=false&includePolicySignals=false&includeDogfoodSignals=false");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("summary_available", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("traceCount").GetInt32() >= 1);
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_NoEvidence_ReturnsInspectionOnlyNoEvidenceStatus()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/missing-agent-run-pr148/health-summary?projectReferenceId={ProjectReferenceId:D}");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("no_agent_run_evidence_found", json.RootElement.GetProperty("status").GetString());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        Assert.IsTrue(json.RootElement.GetProperty("errors").GetArrayLength() >= 1);
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_UnsafeQueryText_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/rawPrompt-leaked/health-summary?projectReferenceId={ProjectReferenceId:D}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "AGENT_RUN_HEALTH_UNSAFE_QUERY_TEXT");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_InvalidDateRange_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/health-summary?projectReferenceId={ProjectReferenceId:D}&fromUtc=2026-06-15T10:00:00Z&toUtc=2026-06-14T10:00:00Z");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "AGENT_RUN_HEALTH_INVALID_DATE_RANGE");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_UnsupportedFilter_ReturnsValidationError()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/health-summary?projectReferenceId={ProjectReferenceId:D}&execute=true");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, "AGENT_RUN_HEALTH_UNSUPPORTED_FILTER");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_DoesNotExposePayloadJsonOrPayloadContent()
    {
        await SeedTraceAsync(subjectId: AgentRunId, eventType: "agent.run.failed");
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/{AgentRunId}/health-summary?projectReferenceId={ProjectReferenceId:D}");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(text.Contains("\"payloadJson\"", StringComparison.OrdinalIgnoreCase), "Agent run health summary must not expose payloadJson.");
        Assert.IsFalse(text.Contains(PayloadMarker, StringComparison.OrdinalIgnoreCase), "Agent run health summary must not expose stored payload content.");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_IsProjectScoped()
    {
        await SeedTraceAsync(subjectId: AgentRunId, eventType: "agent.run.completed", projectReferenceId: OtherProjectReferenceId);
        await SeedTraceAsync(subjectId: "agent-run-pr148-visible", eventType: "agent.run.completed", projectReferenceId: ProjectReferenceId);
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync($"/api/v1/agents/runs/health-summary?projectReferenceId={ProjectReferenceId:D}&includeGateSignals=false&includeApprovalSignals=false&includePolicySignals=false&includeDogfoodSignals=false");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.IsFalse(text.Contains(OtherProjectReferenceId.ToString("D"), StringComparison.OrdinalIgnoreCase), "Agent run health summary leaked other-project evidence.");
        AssertNoUnsafeMaterial(text);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_WarningsStateReportIsNotAuthority()
    {
        await SeedTraceAsync(subjectId: AgentRunId, eventType: "agent.run.completed");
        using var client = await AuthedClientAsync();

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/agents/runs/{AgentRunId}/health-summary?projectReferenceId={ProjectReferenceId:D}"));
        var warnings = string.Join("\n", json.RootElement.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()));

        StringAssert.Contains(warnings, "Agent run health summary is read-only.");
        StringAssert.Contains(warnings, "Health summary output is not approval.");
        StringAssert.Contains(warnings, "Health summary output is not execution permission.");
    }

    private static async Task<SeededTrace> SeedTraceAsync(
        string eventType = "agent.run.observed",
        string actorType = "agent-run-health-summary-api-test",
        string actorId = "agent-run-health-summary-api-test",
        string subjectType = "agent_run",
        string subjectId = AgentRunId,
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
                PayloadJson = $"{{\"schema\":\"agent.run.health.api.test.v1\",\"payloadMarker\":\"{PayloadMarker}\"}}"
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
        Assert.IsTrue(boundary.GetProperty("readOnlySummary").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryIsPolicySatisfaction").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryIsReleaseApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanStartWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanResumeWorkflow").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanRestartAgent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanRetryAgent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanDispatchAgent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanInvokeTool").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanCallModel").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanCreateTicket").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanMutateSource").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanApplyPatch").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanPromoteMemory").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("summaryCanActivateRetrieval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsGovernanceEvent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsApprovalDecision").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsPolicyDecision").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsToolRequest").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("createsDogfoodReceipt").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawPayloadJson").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawPrompt").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawCompletion").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesRawToolOutput").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesSourceContent").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesPatchPayload").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("exposesPrivateReasoning").GetBoolean());
    }

    private static void AssertNoUnsafeMaterial(string text)
    {
        foreach (var token in new[]
        {
            PayloadMarker,
            "raw prompt",
            "raw completion",
            "raw tool output",
            "raw command output",
            "chain-of-thought",
            "source content",
            "patch payload",
            "approval token",
            "api_key",
            "password"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Agent run health summary API response leaked unsafe token: {token}");
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

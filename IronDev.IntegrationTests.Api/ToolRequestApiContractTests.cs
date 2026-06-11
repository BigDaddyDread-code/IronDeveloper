using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Api.Controllers;
using IronDev.Core.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ToolRequestApiContractTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 12, 5, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ToolRequestApi_Create_IsRequestOnly()
    {
        using var client = await AuthedClientAsync();
        var store = Store();
        var before = store.Count();

        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", ValidRequest(801, "request-only").ToBody());
        var json = await ReadJsonAsync(response);
        var after = store.Count();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before + 1, after, "POST may create only a request-only API record.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        Assert.AreEqual("tool-request-801-workspacediff-request-only", json.RootElement.GetProperty("toolRequestId").GetString());

        var boundary = json.RootElement.GetProperty("boundary");
        AssertRequestOnlyBoundary(boundary);

        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("requestOnly").GetBoolean());
        Assert.AreEqual("WorkspaceDiff", data.GetProperty("requestedTool").GetString());
        Assert.AreEqual("ReadOnlyInspection", data.GetProperty("requestKind").GetString());
        Assert.IsFalse(data.GetProperty("requiresHumanApproval").GetBoolean());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolRequestApi_Get_IsReadOnlyInspection()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/tool-requests", ValidRequest(802, "get-readonly").ToBody());
        var createJson = await ReadJsonAsync(create);
        var requestId = createJson.RootElement.GetProperty("toolRequestId").GetString();
        var store = Store();
        var before = store.Count();

        var response = await client.GetAsync($"/api/v1/tool-requests/{requestId}?projectId=802");
        var json = await ReadJsonAsync(response);
        var after = store.Count();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not create request records.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("requestOnly").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("claimsApproval").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("claimsExecutionPermission").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("containsExecutionResult").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("isExecutableWithoutGate").GetBoolean());
        AssertRequestOnlyBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolRequestApi_UnknownRequest_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/tool-requests/missing-request?projectId=803");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task ToolRequestApi_RejectsCrossProjectAccess()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/tool-requests", ValidRequest(804, "cross-project").ToBody());
        var createJson = await ReadJsonAsync(create);
        var requestId = createJson.RootElement.GetProperty("toolRequestId").GetString();

        var response = await client.GetAsync($"/api/v1/tool-requests/{requestId}?projectId=805");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ToolRequestApi_RejectsExecutionFields()
    {
        using var client = await AuthedClientAsync();
        var store = Store();
        var before = store.Count();
        var request = ValidRequest(806, "execution-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["executed"] = true,
                ["toolRan"] = true,
                ["executionPermitted"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = store.Count();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Execution-shaped requests must not create records.");
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("category").GetString() == "unsupported_field"));
    }

    [TestMethod]
    public async Task ToolRequestApi_RejectsApprovalFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(807, "approval-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["approved"] = true,
                ["approvalSource"] = "audit",
                ["gateExecuted"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("category").GetString() == "unsupported_field"));
    }

    [TestMethod]
    public async Task ToolRequestApi_RejectsSourceApplyAndMemoryPromotionFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidRequest(808, "apply-promotion-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["sourceApplied"] = true,
                ["memoryPromoted"] = true,
                ["promoteMemory"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task ToolRequestApi_DoesNotExposeHiddenReasoning()
    {
        using var client = await AuthedClientAsync();
        var store = Store();
        var before = store.Count();
        var request = ValidRequest(809, "hidden-reasoning") with
        {
            Payload = new { notes = "chain-of-thought PRIVATE_MARKER should never enter the tool request API." }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", request.ToBody());
        var json = await ReadJsonAsync(response);
        var after = store.Count();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual(before, after, "Hidden reasoning rejection must happen before request record creation.");
        AssertNoPrivateReasoningLeak(json.RootElement.ToString());
    }

    [TestMethod]
    public async Task ToolRequestApi_DoesNotExposeHiddenReasoning_WhenStoredRequestContainsPrivateReasoning()
    {
        Store().Save(BuildPrivateReasoningRecord("tool-request-private-1", 810));
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/tool-requests/tool-request-private-1?projectId=810");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        StringAssert.Contains(text, "[redacted: sensitive tool-request text]");
        AssertNoPrivateReasoningLeak(text);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolRequestApi_UnauthenticatedRequestsAreRejected()
    {
        var get = await Client.GetAsync("/api/v1/tool-requests/missing?projectId=811");
        var post = await Client.PostAsJsonAsync("/api/v1/tool-requests", ValidRequest(811, "unauthenticated").ToBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [TestMethod]
    public async Task ToolRequestApi_DoesNotExecuteApproveApplyPromoteOrGovern()
    {
        using var client = await AuthedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", ValidRequest(812, "boundary-proof").ToBody());
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        AssertRequestOnlyBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolRequestApi_RejectsUnsupportedToolAndRequestKind()
    {
        using var client = await AuthedClientAsync();
        var unsupportedTool = await client.PostAsJsonAsync("/api/v1/tool-requests", (ValidRequest(813, "bad-tool") with { RequestedTool = "chainsaw.execute" }).ToBody());
        var unsupportedKind = await client.PostAsJsonAsync("/api/v1/tool-requests", (ValidRequest(813, "bad-kind") with { RequestKind = "approvalDecision" }).ToBody());
        var unsupportedToolJson = await ReadJsonAsync(unsupportedTool);
        var unsupportedKindJson = await ReadJsonAsync(unsupportedKind);

        Assert.AreEqual(HttpStatusCode.BadRequest, unsupportedTool.StatusCode, unsupportedToolJson.RootElement.ToString());
        Assert.AreEqual("unsupported_tool", unsupportedToolJson.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(HttpStatusCode.BadRequest, unsupportedKind.StatusCode, unsupportedKindJson.RootElement.ToString());
        Assert.AreEqual("unsupported_request_kind", unsupportedKindJson.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public void ToolRequestApi_ControllerDoesNotReferenceForbiddenServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ToolRequestsV1Controller.cs"));

        var forbiddenTokens = new[]
        {
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "ManualTesterAgentToolExecutionService",
            "IManualTesterAgentToolExecutionService",
            "ToolExecutionAuditStore",
            "IToolExecutionAuditStore",
            "AgentToolExecutionGate",
            "IAgentToolExecutionGate",
            "IWorkspaceApply",
            "ApplyCopy",
            "IControlledWorktreeApplyService",
            "IControlledWriteApprovalService",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ICollectiveMemoryPromotion",
            "PromoteCollectiveMemory",
            "WeaviateSemanticMemoryService",
            "IWeaviate",
            "MemoryIndexQueue"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in tool request API controller: {token}");

        StringAssert.Contains(text, "AgentToolRequestValidator");
        StringAssert.Contains(text, "IToolRequestApiStore");
    }

    [TestMethod]
    public void ToolRequestApi_DocumentationPreservesBoundaries()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "API_TOOL_REQUESTS_V1.md"));

        StringAssert.Contains(text, "Tool Request API v1");
        StringAssert.Contains(text, "POST `/api/v1/tool-requests`");
        StringAssert.Contains(text, "GET `/api/v1/tool-requests/{toolRequestId}?projectId={projectId}`");
        StringAssert.Contains(text, "Tool request is a request form, not execution permission.");
        StringAssert.Contains(text, "Tool request is not approval.");
        StringAssert.Contains(text, "Tool request is not tool execution.");
        StringAssert.Contains(text, "Audit evidence is not approval.");
        StringAssert.Contains(text, "Gate is not executor.");
        StringAssert.Contains(text, "Endpoint access is not execution permission.");
        StringAssert.Contains(text, "API response status is not governance.");
        StringAssert.Contains(text, "Human review remains required for source apply.");
        StringAssert.Contains(text, "Human review remains required for memory promotion.");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static IToolRequestApiStore Store() =>
        Factory.Services.GetRequiredService<IToolRequestApiStore>();

    private static ToolRequestApiRequestBody ValidRequest(int projectId, string correlationId) =>
        new(
            projectId,
            "workspace.diff",
            "readOnlyInspection",
            "Request workspace diff inspection",
            new
            {
                workspacePath = $"workspace-{projectId}",
                runId = $"run-{projectId}",
                mode = "read-only"
            },
            [$"source-report-{projectId}", $"diff-{projectId}"],
            correlationId,
            "Request-only API test evidence.",
            $"agent-run-{projectId}");

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static ToolRequestApiStoredRecord BuildPrivateReasoningRecord(string toolRequestId, int projectId)
    {
        var privateText = "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning";
        var agent = AgentDefinitionCatalog.ReportingAgent;

        return new ToolRequestApiStoredRecord
        {
            ToolRequest = new AgentToolRequest
            {
                ToolRequestId = toolRequestId,
                Status = AgentToolRequestStatus.PendingGate,
                RequestType = AgentToolRequestType.ReadOnlyInspection,
                ToolKind = AgentToolKind.WorkspaceDiff,
                RiskLevel = AgentToolRiskLevel.Low,
                Scope = new AgentToolRequestScope
                {
                    TenantId = AssignedTenantId.ToString(),
                    ProjectId = projectId.ToString(),
                    CampaignId = "campaign-pr61",
                    RunId = $"run-{toolRequestId}",
                    AgentRunId = $"agent-run-{toolRequestId}",
                    CorrelationId = $"correlation-{toolRequestId}"
                },
                Actor = new AgentToolRequestActor
                {
                    AgentId = agent.AgentId,
                    AgentName = agent.Name,
                    AgentKind = agent.Kind,
                    ExecutionMode = agent.ExecutionMode,
                    DeclaredCapabilities = agent.Capabilities?.ToArray() ?? [],
                    ForbiddenCapabilities = agent.ForbiddenCapabilities?.ToArray() ?? []
                },
                Purpose = privateText,
                Inputs =
                [
                    new AgentToolRequestInput
                    {
                        InputId = $"input-{toolRequestId}",
                        RefType = "ToolRequestPayload",
                        RefId = $"payload-{toolRequestId}",
                        Source = privateText,
                        Summary = privateText,
                        EvidenceRefs = [privateText],
                        IsAuthoritativeForAction = false,
                        ContainsRawPrivateReasoning = true,
                        ContainsSecret = false,
                        IsSanitised = true
                    }
                ],
                Evidence =
                [
                    new AgentToolRequestEvidence
                    {
                        EvidenceId = $"evidence-{toolRequestId}",
                        RefType = "CallerEvidence",
                        RefId = privateText,
                        Summary = privateText,
                        SupportsNeedForTool = true,
                        IsAuthorityGrant = false,
                        ContainsRawPrivateReasoning = true,
                        ContainsSecret = false
                    }
                ],
                ApprovalRequirement = new AgentToolRequestApprovalRequirement(),
                PolicySnapshot = new AgentToolRequestPolicySnapshot
                {
                    PolicyKnown = true,
                    AllowsToolRequest = true,
                    AllowsToolExecution = false,
                    AllowsSourceMutation = false,
                    AllowsExternalEffects = false,
                    AllowsGitHubSubmission = false,
                    PolicyRefs = ["policy:test"]
                },
                RequestedAtUtc = CreatedAt,
                ContainsRawPrivateReasoning = true,
                ClaimsApproval = false,
                ClaimsExecutionPermission = false,
                ContainsExecutionResult = false,
                IsExecutableWithoutGate = false
            },
            PayloadJson = "{}",
            PayloadSummary = privateText,
            RequestedByUserId = "user-1",
            CreatedAtUtc = CreatedAt,
            ContainsRawPrivateReasoning = true,
            Warnings = ["Stored fixture contains private reasoning and must be redacted."]
        };
    }

    private static void AssertRequestOnlyBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("toolRequestIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("requestApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gateIsExecutor").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("apiResponseStatusIsGovernance").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var forbidden = new[]
        {
            "approved\":true",
            "governed\":true",
            "applied\":true",
            "promoted\":true",
            "executionPermitted\":true",
            "toolRan\":true",
            "gateExecuted\":true",
            "sourceApplied\":true",
            "memoryPromoted\":true",
            "toolExecuted\":true",
            "requestApproved\":true",
            "toolRequestIsExecutionPermission\":true",
            "auditIsApproval\":true",
            "gateIsExecutor\":true",
            "apiResponseStatusIsGovernance\":true",
            "endpointAccessIsExecutionPermission\":true",
            "modelOutputIsAuthority\":true"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority language: {token}");
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        var forbidden = new[]
        {
            "PRIVATE_MARKER",
            "chain-of-thought",
            "hidden reasoning",
            "raw prompt",
            "scratchpad",
            "private reasoning"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained private reasoning marker: {token}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record ToolRequestApiRequestBody(
        int ProjectId,
        string RequestedTool,
        string RequestKind,
        string Summary,
        object Payload,
        IReadOnlyList<string> EvidenceRefs,
        string CorrelationId,
        string Reason,
        string RequestedByAgentRunId)
    {
        [JsonIgnore]
        public Dictionary<string, object?> Extra { get; init; } = [];

        public Dictionary<string, object?> ToBody()
        {
            var body = new Dictionary<string, object?>
            {
                ["projectId"] = ProjectId,
                ["requestedTool"] = RequestedTool,
                ["requestKind"] = RequestKind,
                ["summary"] = Summary,
                ["payload"] = Payload,
                ["evidenceRefs"] = EvidenceRefs,
                ["correlationId"] = CorrelationId,
                ["reason"] = Reason,
                ["requestedByAgentRunId"] = RequestedByAgentRunId
            };

            foreach (var pair in Extra)
                body[pair.Key] = pair.Value;

            return body;
        }
    }
}

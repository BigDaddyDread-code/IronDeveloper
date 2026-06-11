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
public sealed class ToolGateApiContractTests : ApiTestBase
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 12, 6, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ToolGateApi_Evaluate_IsGateOnly()
    {
        using var client = await AuthedClientAsync();
        var requestId = await CreateToolRequestAsync(client, 901, "gate-only");
        var store = GateStore();
        var before = store.Count();

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(901, requestId).ToBody());
        var json = await ReadJsonAsync(response);
        var after = store.Count();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before + 1, after, "POST may create only a non-durable gate preview.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertGateOnlyBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(requestId, data.GetProperty("toolRequestId").GetString());
        Assert.AreEqual("allowed_by_gate", data.GetProperty("decision").GetString());
        Assert.IsFalse(data.GetProperty("durable").GetBoolean());
        Assert.IsFalse(data.GetProperty("requestDurable").GetBoolean());
        Assert.IsFalse(data.GetProperty("gateDecisionDurable").GetBoolean());
        Assert.IsTrue(data.GetProperty("requiresSeparateExecutor").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("warnings").EnumerateArray().Any(warning =>
            warning.GetString()?.Contains("non-durable API-local preview cache", StringComparison.OrdinalIgnoreCase) == true));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolGateApi_Get_IsReadOnlyInspection()
    {
        using var client = await AuthedClientAsync();
        var requestId = await CreateToolRequestAsync(client, 902, "gate-get");
        var evaluate = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(902, requestId).ToBody());
        var evaluateJson = await ReadJsonAsync(evaluate);
        var gateDecisionId = evaluateJson.RootElement.GetProperty("gateDecisionId").GetString();
        var before = GateStore().Count();

        var response = await client.GetAsync($"/api/v1/tool-gates/evaluations/{gateDecisionId}?projectId=902");
        var json = await ReadJsonAsync(response);
        var after = GateStore().Count();
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual(before, after, "GET must not create gate preview records.");
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertGateOnlyBoundary(json.RootElement.GetProperty("boundary"));
        Assert.AreEqual(gateDecisionId, json.RootElement.GetProperty("data").GetProperty("gateDecisionId").GetString());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolGateApi_UnknownDecision_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/tool-gates/evaluations/missing-gate?projectId=903");
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
    }

    [TestMethod]
    public async Task ToolGateApi_MissingToolRequest_ReturnsNotFound()
    {
        using var client = await AuthedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(904, "missing-request").ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("not_found", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("code").GetString() == "missing_tool_request"));
    }

    [TestMethod]
    public async Task ToolGateApi_RejectsCrossProjectAccess()
    {
        using var client = await AuthedClientAsync();
        var requestId = await CreateToolRequestAsync(client, 905, "cross-project");
        var evaluate = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(905, requestId).ToBody());
        var evaluateJson = await ReadJsonAsync(evaluate);
        var gateDecisionId = evaluateJson.RootElement.GetProperty("gateDecisionId").GetString();

        var response = await client.GetAsync($"/api/v1/tool-gates/evaluations/{gateDecisionId}?projectId=906");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ToolGateApi_RejectsExecutionFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidGateRequest(907, "tool-request-907-workspacediff-execution-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["executed"] = true,
                ["toolRan"] = true,
                ["executionPermitted"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("errors").EnumerateArray().Any(error =>
            error.GetProperty("category").GetString() == "unsupported_field"));
    }

    [TestMethod]
    public async Task ToolGateApi_RejectsApprovalFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidGateRequest(908, "tool-request-908-workspacediff-approval-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["approved"] = true,
                ["approvalSource"] = "gate",
                ["gateExecuted"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task ToolGateApi_RejectsSourceApplyAndMemoryPromotionFields()
    {
        using var client = await AuthedClientAsync();
        var request = ValidGateRequest(909, "tool-request-909-workspacediff-apply-fields") with
        {
            Extra = new Dictionary<string, object?>
            {
                ["sourceApplied"] = true,
                ["memoryPromoted"] = true,
                ["promoteMemory"] = true
            }
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", request.ToBody());
        var json = await ReadJsonAsync(response);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, json.RootElement.ToString());
        Assert.AreEqual("validation_error", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task ToolGateApi_RejectsMissingProjectOrRequest()
    {
        using var client = await AuthedClientAsync();

        var missingProject = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(0, "tool-request").ToBody());
        var missingRequest = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(910, string.Empty).ToBody());

        Assert.AreEqual(HttpStatusCode.BadRequest, missingProject.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingRequest.StatusCode);
    }

    [TestMethod]
    public async Task ToolGateApi_DoesNotExposeHiddenReasoning()
    {
        using var client = await AuthedClientAsync();
        var request = ValidGateRequest(911, "tool-request-911-workspacediff-hidden") with
        {
            Reason = "chain-of-thought PRIVATE_MARKER should never enter gate evaluation."
        };

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", request.ToBody());
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertNoPrivateReasoningLeak(text);
    }

    [TestMethod]
    public async Task ToolGateApi_DoesNotExposeHiddenReasoning_WhenStoredGateContainsPrivateReasoning()
    {
        GateStore().Save(BuildPrivateGatePreview("tool-gate-private-1", "tool-request-private-1", 912));
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/v1/tool-gates/evaluations/tool-gate-private-1?projectId=912");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        StringAssert.Contains(text, "[redacted: sensitive tool-gate text]");
        AssertNoPrivateReasoningLeak(text);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolGateApi_SourceMutationRequestRequiresApprovalButDoesNotApprove()
    {
        using var client = await AuthedClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/tool-requests", ToolRequestBody(913, "source-apply", "source.apply", "sourceMutationRequest").ToBody());
        var createJson = await ReadJsonAsync(create);
        var requestId = createJson.RootElement.GetProperty("toolRequestId").GetString()!;

        var response = await client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(913, requestId).ToBody());
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("blocked_by_gate", json.RootElement.GetProperty("data").GetProperty("decision").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("humanApprovalRequired").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("requiresHumanApproval").GetBoolean());
        AssertGateOnlyBoundary(json.RootElement.GetProperty("boundary"));
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ToolGateApi_UnauthenticatedRequestsAreRejected()
    {
        var get = await Client.GetAsync("/api/v1/tool-gates/evaluations/missing?projectId=914");
        var post = await Client.PostAsJsonAsync("/api/v1/tool-gates/evaluations", ValidGateRequest(914, "missing").ToBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [TestMethod]
    public void ToolGateApi_ControllerDoesNotReferenceForbiddenServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "ToolGatesV1Controller.cs"));

        var forbiddenTokens = new[]
        {
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "ManualTesterAgentToolExecutionService",
            "IManualTesterAgentToolExecutionService",
            "ToolExecutionAuditStore",
            "IToolExecutionAuditStore",
            "AgentToolExecutor",
            "IAgentToolExecutor",
            "AgentToolRouter",
            "IAgentToolRouter",
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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in tool gate API controller: {token}");

        StringAssert.Contains(text, "IAgentToolExecutionGate");
        StringAssert.Contains(text, "IToolRequestApiStore");
        StringAssert.Contains(text, "IToolGateApiStore");
    }

    [TestMethod]
    public void ToolGateApi_DocumentationPreservesBoundaries()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "API_TOOL_GATES_V1.md"));

        StringAssert.Contains(text, "Tool Gate API v1");
        StringAssert.Contains(text, "POST `/api/v1/tool-gates/evaluations`");
        StringAssert.Contains(text, "GET `/api/v1/tool-gates/evaluations/{gateDecisionId}?projectId={projectId}`");
        StringAssert.Contains(text, "Gate is not executor.");
        StringAssert.Contains(text, "Gate decision is not approval.");
        StringAssert.Contains(text, "Gate pass is not human approval.");
        StringAssert.Contains(text, "Tool request is a request form, not execution permission.");
        StringAssert.Contains(text, "Audit evidence is not approval.");
        StringAssert.Contains(text, "Endpoint access is not execution permission.");
        StringAssert.Contains(text, "API response status is not governance.");
        StringAssert.Contains(text, "Human review remains required for source apply.");
        StringAssert.Contains(text, "Human review remains required for memory promotion.");
        StringAssert.Contains(text, "This API operates on non-durable API-local request inspection data");
        StringAssert.Contains(text, "does not yet provide durable SQL source-of-truth gate decisions");
        StringAssert.Contains(text, "\"durable\": false");
        StringAssert.Contains(text, "\"requestDurable\": false");
        StringAssert.Contains(text, "\"gateDecisionDurable\": false");
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static IToolGateApiStore GateStore() =>
        Factory.Services.GetRequiredService<IToolGateApiStore>();

    private static async Task<string> CreateToolRequestAsync(HttpClient client, int projectId, string correlationId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tool-requests", ToolRequestBody(projectId, correlationId).ToBody());
        var json = await ReadJsonAsync(response);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json.RootElement.ToString());
        return json.RootElement.GetProperty("toolRequestId").GetString()!;
    }

    private static ToolGateApiRequestBody ValidGateRequest(int projectId, string toolRequestId) =>
        new(
            projectId,
            toolRequestId,
            [$"source-report-{projectId}", $"tool-request-{projectId}"],
            $"gate-{projectId}",
            "Preview the gate outcome for this request.");

    private static ToolRequestApiRequestBody ToolRequestBody(
        int projectId,
        string correlationId,
        string requestedTool = "workspace.diff",
        string requestKind = "readOnlyInspection") =>
        new(
            projectId,
            requestedTool,
            requestKind,
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

    private static ToolGateApiStoredDecision BuildPrivateGatePreview(string gateDecisionId, string toolRequestId, int projectId)
    {
        var privateText = "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning";
        var agent = AgentDefinitionCatalog.ReportingAgent;
        var toolRequest = new AgentToolRequest
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
                CampaignId = "campaign-pr62",
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
        };

        var requestRecord = new ToolRequestApiStoredRecord
        {
            ToolRequest = toolRequest,
            PayloadJson = "{}",
            PayloadSummary = privateText,
            RequestedByUserId = "user-1",
            CreatedAtUtc = CreatedAt,
            ContainsRawPrivateReasoning = true,
            Warnings = ["Stored fixture contains private reasoning and must be redacted."]
        };

        return new ToolGateApiStoredDecision
        {
            TenantId = AssignedTenantId.ToString(),
            ProjectId = projectId.ToString(),
            ToolRequestRecord = requestRecord,
            Decision = new AgentToolExecutionGateDecision
            {
                GateDecisionId = gateDecisionId,
                ToolRequestId = toolRequestId,
                Decision = AgentToolExecutionGateDecisionType.Allowed,
                ToolKind = AgentToolKind.WorkspaceDiff,
                RequestType = AgentToolRequestType.ReadOnlyInspection,
                RiskLevel = AgentToolRiskLevel.Low,
                EvaluatedAtUtc = CreatedAt,
                Reasons =
                [
                    new AgentToolExecutionGateReason
                    {
                        Code = "PRIVATE_REASON",
                        Severity = "info",
                        Message = privateText
                    }
                ],
                Issues = [],
                GrantsExecution = true,
                ExecutesTool = false,
                MutatesSource = false,
                CallsExternalSystem = false,
                SubmitsGitHubReview = false,
                PersistsResult = false,
                PromotesMemory = false,
                CreatesCollectiveMemory = false,
                WritesWeaviate = false,
                RequiresExecutor = true
            },
            CallerEvidenceRefs = [privateText],
            Reason = privateText,
            CorrelationId = privateText,
            CreatedAtUtc = CreatedAt,
            ContainsRawPrivateReasoning = true,
            Warnings = ["Stored gate preview contains private reasoning and must be redacted."]
        };
    }

    private static void AssertGateOnlyBoundary(JsonElement boundary)
    {
        Assert.IsFalse(boundary.GetProperty("gateIsExecutor").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gateDecisionIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gatePassIsHumanApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolRequestIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("toolExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("requestApproved").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("apiResponseStatusIsGovernance").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("durable").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("requestDurable").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("gateDecisionDurable").GetBoolean());
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
            "gateDecisionIsApproval\":true",
            "gatePassIsHumanApproval\":true",
            "apiResponseStatusIsGovernance\":true",
            "endpointAccessIsExecutionPermission\":true",
            "modelOutputIsAuthority\":true",
            "durable\":true",
            "requestDurable\":true",
            "gateDecisionDurable\":true"
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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }

    private sealed record ToolGateApiRequestBody(
        int ProjectId,
        string ToolRequestId,
        IReadOnlyList<string> EvidenceRefs,
        string CorrelationId,
        string Reason)
    {
        [JsonIgnore]
        public Dictionary<string, object?> Extra { get; init; } = [];

        public Dictionary<string, object?> ToBody()
        {
            var body = new Dictionary<string, object?>
            {
                ["projectId"] = ProjectId,
                ["toolRequestId"] = ToolRequestId,
                ["evidenceRefs"] = EvidenceRefs,
                ["correlationId"] = CorrelationId,
                ["reason"] = Reason
            };

            foreach (var pair in Extra)
                body[pair.Key] = pair.Value;

            return body;
        }
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
        public Dictionary<string, object?> ToBody() =>
            new()
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
    }
}

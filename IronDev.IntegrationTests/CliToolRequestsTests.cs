using System.Net;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliToolRequestsTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();

    [TestMethod]
    public async Task CliToolRequests_Create_CallsToolRequestApi()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            [
                "tool-requests",
                "create",
                "--project-id",
                "42",
                "--request-kind",
                "readOnlyInspection",
                "--tool-kind",
                "workspace.diff",
                "--run-id",
                "agent-run-42",
                "--reason",
                "Inspect workspace diff evidence",
                "--summary",
                "Workspace diff request",
                "--evidence-ref",
                "source-report-42",
                "--input-ref",
                "workspace-42",
                "--policy-ref",
                "pr56-backend-freeze",
                "--risk-level",
                "low",
                "--dry-run-required",
                "false",
                "--correlation-id",
                "corr-42",
                "--api-base-url",
                "https://api.example.test",
                "--token",
                "super-secret-token",
                "--output",
                "json"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.Request?.Method);
        Assert.AreEqual("/api/v1/tool-requests", handler.Request?.RequestUri?.PathAndQuery);
        Assert.AreEqual("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.AreEqual("super-secret-token", handler.Request?.Headers.Authorization?.Parameter);

        using var body = JsonDocument.Parse(handler.Body ?? "{}");
        Assert.AreEqual(42, body.RootElement.GetProperty("projectId").GetInt32());
        Assert.AreEqual("workspace.diff", body.RootElement.GetProperty("requestedTool").GetString());
        Assert.AreEqual("readOnlyInspection", body.RootElement.GetProperty("requestKind").GetString());
        Assert.AreEqual("Workspace diff request", body.RootElement.GetProperty("summary").GetString());
        Assert.AreEqual("Inspect workspace diff evidence", body.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual("agent-run-42", body.RootElement.GetProperty("requestedByAgentRunId").GetString());
        Assert.AreEqual("source-report-42", body.RootElement.GetProperty("evidenceRefs")[0].GetString());
        Assert.AreEqual("corr-42", body.RootElement.GetProperty("correlationId").GetString());

        var payload = body.RootElement.GetProperty("payload");
        Assert.AreEqual("agent-run-42", payload.GetProperty("runId").GetString());
        Assert.AreEqual("workspace-42", payload.GetProperty("inputRefs")[0].GetString());
        Assert.AreEqual("pr56-backend-freeze", payload.GetProperty("policyRefs")[0].GetString());
        Assert.AreEqual("low", payload.GetProperty("riskLevel").GetString());
        Assert.IsFalse(payload.GetProperty("dryRunRequired").GetBoolean());

        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliToolRequests_Get_CallsToolRequestDetailApi()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            [
                "tool-requests",
                "get",
                "tool-request-42-workspacediff",
                "--project-id",
                "42",
                "--api-base-url",
                "https://api.example.test",
                "--output",
                "text"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual("/api/v1/tool-requests/tool-request-42-workspacediff?projectId=42", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Tool request");
        StringAssert.Contains(output.ToString(), "Tool request is request form, not execution permission.");
        StringAssert.Contains(output.ToString(), "Request approval is separate.");
        StringAssert.Contains(output.ToString(), "Tool execution is separate.");
        StringAssert.Contains(output.ToString(), "Durable: false");
    }

    [TestMethod]
    public async Task CliToolRequests_MissingProjectId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "create", "--request-kind", "readOnlyInspection", "--tool-kind", "workspace.diff", "--run-id", "agent-run-42", "--reason", "Inspect evidence", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "--project-id");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliToolRequests_MissingRequiredCreateFields_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "create", "--project-id", "42", "--request-kind", "readOnlyInspection", "--tool-kind", "workspace.diff", "--run-id", "agent-run-42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "--reason");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliToolRequests_MissingToolRequestIdForGet_ReturnsUsageError()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "get", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "<toolRequestId>");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliToolRequests_UnknownSubcommand_ReturnsUsageError()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "approve", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unknown tool-requests subcommand");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliToolRequests_RejectsExecutionOrApprovalFlags()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            [
                "tool-requests",
                "create",
                "--project-id",
                "42",
                "--request-kind",
                "readOnlyInspection",
                "--tool-kind",
                "workspace.diff",
                "--run-id",
                "agent-run-42",
                "--reason",
                "Inspect evidence",
                "--execute",
                "--api-base-url",
                "https://api.example.test"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unsupported approval, execution, gate, source-apply, memory-promotion, or audit flag");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliToolRequests_JsonOutput_PreservesApiWarnings()
    {
        var handler = new RecordingHandler(CreateEnvelope(warnings: ["durable SQL-backed tool request records"]));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "get", "tool-request-42-workspacediff", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("durable SQL-backed tool request records", json.RootElement.GetProperty("warnings")[0].GetString());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("boundary").GetBoolean());
    }

    [TestMethod]
    public async Task CliToolRequests_DoesNotPrintToken()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "get", "tool-request-42-workspacediff", "--project-id", "42", "--api-base-url", "https://api.example.test", "--token", "never-print-me"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliToolRequests_DoesNotPrintHiddenReasoningMarkers()
    {
        var handler = new RecordingHandler(GetEnvelope(summary: "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "get", "tool-request-private", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        AssertNoPrivateReasoningLeak(output.ToString());
        AssertNoPrivateReasoningLeak(error.ToString());
    }

    [TestMethod]
    public async Task CliToolRequests_ApiFailure_ReturnsApiFailure()
    {
        var handler = new RecordingHandler(ErrorEnvelope(), HttpStatusCode.BadRequest);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "get", "missing-request", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(4, exitCode);
        StringAssert.Contains(error.ToString(), "TOOL_REQUEST_API_VALIDATION_ERROR");
    }

    [TestMethod]
    public async Task CliToolRequests_ConnectionFailure_ReturnsConnectionFailure()
    {
        var handler = new ThrowingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliToolRequests.HandleAsync(
            ["tool-requests", "get", "tool-request-42-workspacediff", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(6, exitCode);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_API_CONNECTION_FAILED");
    }

    [TestMethod]
    public void CliToolRequests_DoesNotReferenceBackendExecutionServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliToolRequests.cs"));

        var forbidden = new[]
        {
            "ManualTesterAgentToolExecutionService",
            "IManualTesterAgentToolExecutionService",
            "ManualImplementationPatchProposalService",
            "IManualImplementationPatchProposalService",
            "ToolExecutionAuditStore",
            "IToolExecutionAuditStore",
            "AgentToolExecutionGate",
            "IAgentToolExecutionGate",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "IHostedService",
            "BackgroundService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden backend/execution token found in tool-requests CLI: {token}");

        StringAssert.Contains(text, "IIronDevApiClient");
        StringAssert.Contains(text, "CreateToolRequestAsync");
        StringAssert.Contains(text, "GetToolRequestAsync");
    }

    [TestMethod]
    public void CliToolRequests_DoesNotReferenceGateApprovalSourceApplyOrMemoryPromotion()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliToolRequests.cs"));

        var forbidden = new[]
        {
            "IControlledWriteApprovalService",
            "IWorkspaceApply",
            "ApplyCopy",
            "PatchApplyService",
            "CollectiveMemoryPromotionService",
            "ICollectiveMemoryPromotion",
            "AcceptedMemoryService",
            "CollectiveMemoryWriteService",
            "WeaviateSemanticMemoryService",
            "IWeaviate",
            "MemoryIndexQueue",
            "VectorStore",
            "GitHubReviewSubmissionService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden authority token found in tool-requests CLI: {token}");
    }

    private static string CreateEnvelope(IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "succeeded",
          "toolRequestId": "tool-request-42-workspacediff",
          "runId": "tool-request-api-v1-tool-request-42-workspacediff",
          "evidenceId": "evidence-tool-request-42-workspacediff-001",
          "boundary": {
            "toolRequestIsExecutionPermission": false,
            "durable": true,
            "toolExecuted": false,
            "requestApproved": false,
            "auditIsApproval": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": true,
          "humanApprovalRequired": false,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Tool request is request form, not execution permission.", "Tool Request API v1 uses a durable SQL-backed tool request records."])}},
          "errors": [],
          "data": {
            "toolRequestId": "tool-request-42-workspacediff",
            "status": "PendingGate",
            "requestedTool": "WorkspaceDiff",
            "requestKind": "ReadOnlyInspection",
            "riskLevel": "Low",
            "runId": "tool-request-api-v1-tool-request-42-workspacediff",
            "evidenceId": "evidence-tool-request-42-workspacediff-001",
            "requestedByAgentRunId": "agent-run-42",
            "payloadSummary": "{\"runId\":\"agent-run-42\"}",
            "evidenceRefs": ["source-report-42"],
            "requestOnly": true,
            "requiresGovernanceGate": false,
            "requiresHumanApproval": false,
            "requiresPolicyApproval": false,
            "requiresDryRunFirst": false,
            "requiresMemoryGovernance": false,
            "warnings": []
          }
        }
        """;

    private static string GetEnvelope(string summary = "Safe public tool-request summary", IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "succeeded",
          "toolRequestId": "tool-request-42-workspacediff",
          "runId": "tool-request-api-v1-tool-request-42-workspacediff",
          "evidenceId": "evidence-tool-request-42-workspacediff-001",
          "boundary": {
            "toolRequestIsExecutionPermission": false,
            "durable": true,
            "toolExecuted": false,
            "requestApproved": false,
            "auditIsApproval": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": false,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Tool Request API v1 uses a durable SQL-backed tool request records."])}},
          "errors": [],
          "data": {
            "toolRequestId": "tool-request-42-workspacediff",
            "projectId": "42",
            "status": "PendingGate",
            "requestedTool": "WorkspaceDiff",
            "requestKind": "ReadOnlyInspection",
            "riskLevel": "Low",
            "runId": "tool-request-api-v1-tool-request-42-workspacediff",
            "requestedByAgentRunId": "agent-run-42",
            "purpose": {{JsonSerializer.Serialize(summary)}},
            "payloadSummary": {{JsonSerializer.Serialize(summary)}},
            "inputs": [
              {
                "refType": "ToolRequestPayload",
                "refId": "payload-tool-request-42-workspacediff",
                "summary": {{JsonSerializer.Serialize(summary)}},
                "evidenceRefs": ["source-report-42"]
              }
            ],
            "evidence": [
              {
                "evidenceId": "evidence-tool-request-42-workspacediff-001",
                "refType": "CallerEvidence",
                "refId": "source-report-42",
                "summary": {{JsonSerializer.Serialize(summary)}},
                "supportsNeedForTool": true,
                "isAuthorityGrant": false
              }
            ],
            "requestOnly": true,
            "claimsApproval": false,
            "claimsExecutionPermission": false,
            "containsExecutionResult": false,
            "isExecutableWithoutGate": false,
            "requiresGovernanceGate": false,
            "requiresHumanApproval": false,
            "requiresPolicyApproval": false,
            "requiresDryRunFirst": false,
            "requiresMemoryGovernance": false,
            "createdAtUtc": "2026-06-12T00:00:00Z",
            "warnings": []
          }
        }
        """;

    private static string ErrorEnvelope() =>
        """
        {
          "status": "validation_error",
          "data": null,
          "toolRequestId": "",
          "runId": "",
          "evidenceId": "",
          "boundary": {
            "toolRequestIsExecutionPermission": false,
            "durable": true,
            "toolExecuted": false,
            "requestApproved": false,
            "auditIsApproval": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": false,
          "warnings": [],
          "errors": [
            {
              "category": "validation_error",
              "code": "TOOL_REQUEST_API_VALIDATION_ERROR",
              "message": "Tool request is invalid.",
              "field": "toolRequestId"
            }
          ]
        }
        """;

    private static void AssertNoTokenLeak(params string[] values)
    {
        foreach (var value in values)
            Assert.IsFalse(value.Contains("super-secret-token", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("never-print-me", StringComparison.OrdinalIgnoreCase));
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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"CLI output contained private reasoning marker: {token}");
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _statusCode = statusCode;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network down");
    }
}

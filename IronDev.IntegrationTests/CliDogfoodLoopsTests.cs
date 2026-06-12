using System.Net;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliDogfoodLoopsTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();

    [TestMethod]
    public async Task CliDogfoodLoops_Create_CallsDogfoodLoopApi()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            [
                "dogfood-loops",
                "create",
                "--project-id",
                "42",
                "--summary",
                "Manual dogfood loop receipt",
                "--goal",
                "Collect release-spine evidence for human review",
                "--observation",
                "Critic and proposal stages completed",
                "--blocked-reason",
                "Human review remains required",
                "--agent-run-id",
                "agent-run-42",
                "--critic-review-run-id",
                "critic-run-42",
                "--memory-improvement-run-id",
                "memory-run-42",
                "--tool-request-id",
                "tool-request-42",
                "--tool-gate-decision-id",
                "tool-gate-42",
                "--evidence-ref",
                "source-report-42",
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
        Assert.AreEqual("/api/v1/dogfood-loops", handler.Request?.RequestUri?.PathAndQuery);
        Assert.AreEqual("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.AreEqual("super-secret-token", handler.Request?.Headers.Authorization?.Parameter);

        using var body = JsonDocument.Parse(handler.Body ?? "{}");
        Assert.AreEqual(42, body.RootElement.GetProperty("projectId").GetInt32());
        Assert.AreEqual("Manual dogfood loop receipt", body.RootElement.GetProperty("summary").GetString());
        Assert.AreEqual("Collect release-spine evidence for human review", body.RootElement.GetProperty("goal").GetString());
        Assert.AreEqual("agent-run-42", body.RootElement.GetProperty("agentRunIds")[0].GetString());
        Assert.AreEqual("critic-run-42", body.RootElement.GetProperty("criticReviewRunIds")[0].GetString());
        Assert.AreEqual("memory-run-42", body.RootElement.GetProperty("memoryImprovementRunIds")[0].GetString());
        Assert.AreEqual("tool-request-42", body.RootElement.GetProperty("toolRequestIds")[0].GetString());
        Assert.AreEqual("tool-gate-42", body.RootElement.GetProperty("toolGateDecisionIds")[0].GetString());
        Assert.AreEqual("source-report-42", body.RootElement.GetProperty("evidenceRefs")[0].GetProperty("refId").GetString());
        Assert.AreEqual("cli_evidence", body.RootElement.GetProperty("evidenceRefs")[0].GetProperty("refType").GetString());
        Assert.AreEqual("corr-42", body.RootElement.GetProperty("correlationId").GetString());

        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliDogfoodLoops_Get_CallsDogfoodLoopDetailApi()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            [
                "dogfood-loops",
                "get",
                "dogfood-loop-42-corr",
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
        Assert.AreEqual("/api/v1/dogfood-loops/dogfood-loop-42-corr?projectId=42", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Dogfood loop receipt");
        StringAssert.Contains(output.ToString(), "Dogfood receipt is not release approval.");
        StringAssert.Contains(output.ToString(), "Dogfood loop is not autonomous workflow.");
        StringAssert.Contains(output.ToString(), "Human review remains required for source apply and memory promotion.");
        StringAssert.Contains(output.ToString(), "Durable: false");
    }

    [TestMethod]
    public async Task CliDogfoodLoops_MissingProjectId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "create", "--summary", "Receipt", "--goal", "Collect evidence", "--api-base-url", "https://api.example.test"],
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
    public async Task CliDogfoodLoops_MissingRequiredCreateFields_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "create", "--project-id", "42", "--goal", "Collect evidence", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "--summary");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliDogfoodLoops_MissingDogfoodLoopIdForGet_ReturnsUsageError()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "get", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "<dogfoodLoopId>");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliDogfoodLoops_UnknownSubcommand_ReturnsUsageError()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "approve", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unknown dogfood-loops subcommand");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliDogfoodLoops_RejectsExecutionOrApprovalFlags()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            [
                "dogfood-loops",
                "create",
                "--project-id",
                "42",
                "--summary",
                "Receipt",
                "--goal",
                "Collect evidence",
                "--release-approved",
                "--api-base-url",
                "https://api.example.test"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unsupported release approval, workflow execution, gate, source-apply, memory-promotion, audit, or authority flag");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliDogfoodLoops_RejectsAuthorityText()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            [
                "dogfood-loops",
                "create",
                "--project-id",
                "42",
                "--summary",
                "release approved and ready to ship",
                "--goal",
                "Collect evidence",
                "--api-base-url",
                "https://api.example.test"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "does not accept release approval");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliDogfoodLoops_JsonOutput_PreservesApiWarnings()
    {
        var handler = new RecordingHandler(CreateEnvelope(warnings: ["Dogfood Loop API v1 receipt storage is non-durable."]));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "get", "dogfood-loop-42-corr", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("Dogfood Loop API v1 receipt storage is non-durable.", json.RootElement.GetProperty("warnings")[0].GetString());
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("boundary").GetProperty("durable").GetBoolean());
    }

    [TestMethod]
    public async Task CliDogfoodLoops_DoesNotPrintToken()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "get", "dogfood-loop-42-corr", "--project-id", "42", "--api-base-url", "https://api.example.test", "--token", "never-print-me"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliDogfoodLoops_DoesNotPrintHiddenReasoningMarkers()
    {
        var handler = new RecordingHandler(GetEnvelope(summary: "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "get", "dogfood-loop-private", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
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
    public async Task CliDogfoodLoops_ApiFailure_ReturnsApiFailure()
    {
        var handler = new RecordingHandler(ErrorEnvelope(), HttpStatusCode.BadRequest);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "get", "missing-loop", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(4, exitCode);
        StringAssert.Contains(error.ToString(), "DOGFOOD_LOOP_API_VALIDATION_ERROR");
    }

    [TestMethod]
    public async Task CliDogfoodLoops_ConnectionFailure_ReturnsConnectionFailure()
    {
        var handler = new ThrowingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliDogfoodLoops.HandleAsync(
            ["dogfood-loops", "get", "dogfood-loop-42-corr", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(6, exitCode);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_API_CONNECTION_FAILED");
    }

    [TestMethod]
    public void CliDogfoodLoops_DoesNotReferenceBackendExecutionServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliDogfoodLoops.cs"));

        var forbidden = new[]
        {
            "ManualDogfoodHarnessService",
            "IManualDogfoodHarnessService",
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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden backend/execution token found in dogfood-loops CLI: {token}");

        StringAssert.Contains(text, "IIronDevApiClient");
        StringAssert.Contains(text, "CreateDogfoodLoopAsync");
        StringAssert.Contains(text, "GetDogfoodLoopAsync");
    }

    [TestMethod]
    public void CliDogfoodLoops_DoesNotReferenceApprovalSourceApplyOrMemoryPromotion()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliDogfoodLoops.cs"));

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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden authority token found in dogfood-loops CLI: {token}");
    }

    private static string CreateEnvelope(IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "receipt_created",
          "dogfoodLoopId": "dogfood-loop-42-corr",
          "runId": "dogfood-loop-api-v1-dogfood-loop-42-corr",
          "receiptId": "receipt-dogfood-loop-42-corr",
          "evidenceId": "evidence-dogfood-loop-42-corr",
          "boundary": {
            "dogfoodReceiptIsReleaseApproval": false,
            "dogfoodLoopIsAutonomousWorkflow": false,
            "toolExecuted": false,
            "requestApproved": false,
            "gateExecuted": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "collectiveMemoryWritten": false,
            "vectorAuthorityWritten": false,
            "auditIsApproval": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "durable": false,
            "containsNonDurableReferences": true,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": true,
          "humanApprovalRequired": true,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Dogfood receipt is not release approval.", "Dogfood loop is not autonomous workflow."]) }},
          "errors": [],
          "data": {
            "dogfoodLoopId": "dogfood-loop-42-corr",
            "runId": "dogfood-loop-api-v1-dogfood-loop-42-corr",
            "receiptId": "receipt-dogfood-loop-42-corr",
            "evidenceId": "evidence-dogfood-loop-42-corr",
            "receiptOnly": true,
            "durable": false,
            "containsNonDurableReferences": true,
            "summary": "Manual dogfood loop receipt",
            "goal": "Collect release-spine evidence for human review",
            "durabilityWarnings": ["Dogfood Loop API v1 receipt storage is non-durable, API-local, not SQL-backed, not durable evidence, and not release approval."],
            "knownLimitations": ["No autonomous dogfood workflow runner is introduced."],
            "warnings": []
          }
        }
        """;

    private static string GetEnvelope(string summary = "Safe public dogfood-loop summary", IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "receipt_found",
          "dogfoodLoopId": "dogfood-loop-42-corr",
          "runId": "dogfood-loop-api-v1-dogfood-loop-42-corr",
          "receiptId": "receipt-dogfood-loop-42-corr",
          "evidenceId": "evidence-dogfood-loop-42-corr",
          "boundary": {
            "dogfoodReceiptIsReleaseApproval": false,
            "dogfoodLoopIsAutonomousWorkflow": false,
            "toolExecuted": false,
            "requestApproved": false,
            "gateExecuted": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "collectiveMemoryWritten": false,
            "vectorAuthorityWritten": false,
            "auditIsApproval": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "durable": false,
            "containsNonDurableReferences": true,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": true,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Dogfood Loop API v1 receipt storage is non-durable."]) }},
          "errors": [],
          "data": {
            "dogfoodLoopId": "dogfood-loop-42-corr",
            "runId": "dogfood-loop-api-v1-dogfood-loop-42-corr",
            "receiptId": "receipt-dogfood-loop-42-corr",
            "evidenceId": "evidence-dogfood-loop-42-corr",
            "projectId": "42",
            "summary": {{JsonSerializer.Serialize(summary)}},
            "goal": {{JsonSerializer.Serialize(summary)}},
            "observations": [{{JsonSerializer.Serialize(summary)}}],
            "blockedReasons": [],
            "referencedAgentRuns": [],
            "referencedCriticReviews": [],
            "referencedMemoryImprovements": [],
            "referencedToolRequests": [
              {
                "refType": "tool_request_preview",
                "refId": "tool-request-42",
                "summary": {{JsonSerializer.Serialize(summary)}},
                "durable": false,
                "backendRecorded": false,
                "source": "caller_supplied"
              }
            ],
            "referencedGateDecisions": [],
            "evidenceRefs": [],
            "durable": false,
            "containsNonDurableReferences": true,
            "durabilityWarnings": ["Dogfood Loop API v1 receipt storage is non-durable, API-local, not SQL-backed, not durable evidence, and not release approval."],
            "knownLimitations": ["No autonomous dogfood workflow runner is introduced."],
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
          "dogfoodLoopId": "",
          "runId": "",
          "receiptId": "",
          "evidenceId": "",
          "boundary": {
            "dogfoodReceiptIsReleaseApproval": false,
            "dogfoodLoopIsAutonomousWorkflow": false,
            "toolExecuted": false,
            "requestApproved": false,
            "gateExecuted": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "collectiveMemoryWritten": false,
            "vectorAuthorityWritten": false,
            "auditIsApproval": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "durable": false,
            "containsNonDurableReferences": true,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": true,
          "warnings": [],
          "errors": [
            {
              "category": "validation_error",
              "code": "DOGFOOD_LOOP_API_VALIDATION_ERROR",
              "message": "Dogfood loop request is invalid.",
              "field": "dogfoodLoopId"
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

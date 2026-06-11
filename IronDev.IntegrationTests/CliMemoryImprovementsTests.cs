using System.Net;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliMemoryImprovementsTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();

    [TestMethod]
    public async Task CliMemoryImprovements_Create_CallsManualMemoryImprovementApi()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            [
                "memory-improvements",
                "create",
                "--project-id",
                "42",
                "--target-agent-run-id",
                "agent-run-42",
                "--focus",
                "Repeated manual correction pattern",
                "--reason",
                "Public proposal reason",
                "--evidence-ref",
                "agent-run-audit:agent-run-42",
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
        Assert.AreEqual("/api/v1/manual-memory-improvements", handler.Request?.RequestUri?.PathAndQuery);
        Assert.AreEqual("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.AreEqual("super-secret-token", handler.Request?.Headers.Authorization?.Parameter);

        using var body = JsonDocument.Parse(handler.Body ?? "{}");
        Assert.AreEqual(42, body.RootElement.GetProperty("projectId").GetInt32());
        Assert.AreEqual("AgentRunAuditEnvelope", body.RootElement.GetProperty("sourceType").GetString());
        Assert.AreEqual("agent-run-42", body.RootElement.GetProperty("sourceId").GetString());
        Assert.AreEqual("Repeated manual correction pattern", body.RootElement.GetProperty("summary").GetString());
        Assert.AreEqual("Public proposal reason", body.RootElement.GetProperty("content").GetString());
        Assert.AreEqual("RepeatedManualCorrection", body.RootElement.GetProperty("candidateType").GetString());
        Assert.AreEqual("corr-42", body.RootElement.GetProperty("correlationId").GetString());
        Assert.AreEqual("agent-run-audit:agent-run-42", body.RootElement.GetProperty("evidenceRefs")[0].GetString());
        Assert.IsFalse(handler.Body!.Contains("targetAgentRunId", StringComparison.OrdinalIgnoreCase));
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliMemoryImprovements_Get_CallsManualMemoryImprovementDetailApi()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            [
                "memory-improvements",
                "get",
                "manual-memory-improvement-42",
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
        Assert.AreEqual("/api/v1/manual-memory-improvements/manual-memory-improvement-42?projectId=42", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Memory improvement proposal");
        StringAssert.Contains(output.ToString(), "Memory proposal is not promotion.");
        StringAssert.Contains(output.ToString(), "Memory safe is not approval.");
        StringAssert.Contains(output.ToString(), "Candidate is not memory.");
    }

    [TestMethod]
    public async Task CliMemoryImprovements_MissingProjectId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "create", "--target-agent-run-id", "agent-run-42", "--api-base-url", "https://api.example.test"],
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
    public async Task CliMemoryImprovements_MissingTargetAgentRunId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "create", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "--target-agent-run-id");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliMemoryImprovements_MissingAgentRunIdForGet_ReturnsUsageError()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "get", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "<agentRunId>");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliMemoryImprovements_UnknownSubcommand_ReturnsUsageError()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "promote", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unknown memory-improvements subcommand");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliMemoryImprovements_UnsupportedPromotionFlag_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            [
                "memory-improvements",
                "create",
                "--project-id",
                "42",
                "--target-agent-run-id",
                "agent-run-42",
                "--promote-memory",
                "--api-base-url",
                "https://api.example.test"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unsupported authority, promotion, or execution flag");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliMemoryImprovements_JsonOutput_PreservesApiWarnings()
    {
        var handler = new RecordingHandler(CreateEnvelope(warnings: ["manual memory warning"]));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "get", "manual-memory-improvement-42", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("manual memory warning", json.RootElement.GetProperty("warnings")[0].GetString());
    }

    [TestMethod]
    public async Task CliMemoryImprovements_DoesNotPrintToken()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "get", "manual-memory-improvement-42", "--project-id", "42", "--api-base-url", "https://api.example.test", "--token", "never-print-me"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliMemoryImprovements_DoesNotPrintHiddenReasoningMarkers()
    {
        var handler = new RecordingHandler(GetEnvelope(summary: "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "get", "manual-memory-improvement-private", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
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
    public async Task CliMemoryImprovements_ApiFailure_ReturnsApiFailure()
    {
        var handler = new RecordingHandler(ErrorEnvelope(), HttpStatusCode.BadRequest);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "get", "missing-run", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(4, exitCode);
        StringAssert.Contains(error.ToString(), "MANUAL_MEMORY_IMPROVEMENT_API_VALIDATION_ERROR");
    }

    [TestMethod]
    public async Task CliMemoryImprovements_ConnectionFailure_ReturnsConnectionFailure()
    {
        var handler = new ThrowingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliMemoryImprovements.HandleAsync(
            ["memory-improvements", "get", "manual-memory-improvement-42", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(6, exitCode);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_API_CONNECTION_FAILED");
    }

    [TestMethod]
    public void CliMemoryImprovements_DoesNotReferenceBackendExecutionServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliMemoryImprovements.cs"));

        var forbidden = new[]
        {
            "IStoredManualMemoryImprovementAgentService",
            "IManualMemoryImprovementAgentService",
            "ManualMemoryImprovementAgentService",
            "StoredManualAgentExecutionService",
            "AgentRunAuditEnvelopeStore",
            "ToolExecutionAuditStore",
            "AgentToolExecutionGate",
            "IWorkspaceApply",
            "ApplyCopy",
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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden backend/execution token found in memory-improvements CLI: {token}");

        StringAssert.Contains(text, "IIronDevApiClient");
        StringAssert.Contains(text, "CreateManualMemoryImprovementAsync");
        StringAssert.Contains(text, "GetManualMemoryImprovementAsync");
    }

    [TestMethod]
    public void CliMemoryImprovements_DoesNotReferenceMemoryPromotionOrVectorWrites()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliMemoryImprovements.cs"));

        var forbidden = new[]
        {
            "CollectiveMemoryPromotionService",
            "ICollectiveMemoryPromotion",
            "AcceptedMemory",
            "AcceptedMemoryService",
            "CollectiveMemoryWrite",
            "WeaviateSemanticMemoryService",
            "IWeaviate",
            "MemoryIndexQueue",
            "VectorStore",
            "RetrievalAuthority"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden memory promotion/vector token found in memory-improvements CLI: {token}");
    }

    private static string CreateEnvelope(IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "succeeded",
          "runId": "manual-memory-improvement-42",
          "proposalId": "memory-proposal-draft-42",
          "evidenceId": "evidence-42",
          "boundary": {
            "memoryImprovementIsPromotion": false,
            "memoryProposalIsPromotion": false,
            "memorySafeIsApproval": false,
            "candidateIsMemory": false,
            "retrievalMatchIsMemoryCandidate": false,
            "auditIsApproval": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "collectiveMemoryWritten": false,
            "vectorAuthorityWritten": false,
            "toolExecuted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForMemoryPromotion": true,
            "humanReviewRequiredForSourceApply": true
          },
          "mutationOccurred": true,
          "humanApprovalRequired": true,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Memory proposal is not promotion. Memory safe is not approval. Candidate is not memory."])}},
          "errors": [],
          "data": {
            "agentRunId": "manual-memory-improvement-42",
            "detectionResultId": "memory-detection-42",
            "status": "Stored",
            "detectedAtUtc": "2026-06-12T00:00:00Z",
            "patternCount": 1,
            "proposalCount": 1,
            "proposalIds": ["memory-proposal-draft-42"],
            "patterns": [],
            "proposals": [],
            "evidenceRefs": ["evidence-42"],
            "mutationOccurred": true,
            "proposalOnly": true,
            "requiresHumanReview": true,
            "warnings": []
          }
        }
        """;

    private static string GetEnvelope(string summary = "Safe public memory-improvement summary", IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "succeeded",
          "runId": "manual-memory-improvement-42",
          "proposalId": "memory-proposal-draft-42",
          "evidenceId": "evidence-42",
          "boundary": {
            "memoryImprovementIsPromotion": false,
            "memoryProposalIsPromotion": false,
            "memorySafeIsApproval": false,
            "candidateIsMemory": false,
            "retrievalMatchIsMemoryCandidate": false,
            "auditIsApproval": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "collectiveMemoryWritten": false,
            "vectorAuthorityWritten": false,
            "toolExecuted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForMemoryPromotion": true,
            "humanReviewRequiredForSourceApply": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": true,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Memory improvement is proposal-only."])}},
          "errors": [],
          "data": {
            "projectId": "42",
            "agentRunId": "manual-memory-improvement-42",
            "detectionResultId": "memory-detection-42",
            "proposalIds": ["memory-proposal-draft-42"],
            "status": "Completed",
            "agentId": "memory-improvement-agent",
            "agentName": "MemoryImprovementAgent",
            "requestSummary": {{JsonSerializer.Serialize(summary)}},
            "purpose": "Manual memory-improvement evidence inspection.",
            "createdAtUtc": "2026-06-12T00:00:00Z",
            "completedAtUtc": "2026-06-12T00:00:00Z",
            "inputSummaries": [{{JsonSerializer.Serialize(summary)}}],
            "outputSummaries": [{{JsonSerializer.Serialize(summary)}}],
            "thoughtLedgerSummaries": [{{JsonSerializer.Serialize(summary)}}],
            "evidenceRefs": ["evidence-42"],
            "proposalOnlyOutput": true,
            "createsAuthority": false,
            "createsRuntimeAction": false,
            "boundaryBlocks": true,
            "safetySummary": {
              "containsRawPrivateReasoning": false,
              "hasAuthorityClaim": false,
              "hasApprovalClaim": false,
              "hasMemoryPromotionClaim": false,
              "hasRuntimeActionOutput": false,
              "hasAuthorityCreatingOutput": false,
              "hasBlockedCapabilityAttempt": true,
              "hasBoundaryBlock": true,
              "warnings": []
            },
            "proposalOnly": true,
            "requiresHumanReview": true,
            "warnings": []
          }
        }
        """;

    private static string ErrorEnvelope() =>
        """
        {
          "status": "validation_error",
          "data": null,
          "runId": "",
          "proposalId": "",
          "evidenceId": "",
          "boundary": {
            "memoryImprovementIsPromotion": false,
            "memoryProposalIsPromotion": false,
            "memorySafeIsApproval": false,
            "candidateIsMemory": false,
            "retrievalMatchIsMemoryCandidate": false,
            "auditIsApproval": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "collectiveMemoryWritten": false,
            "vectorAuthorityWritten": false,
            "toolExecuted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForMemoryPromotion": true,
            "humanReviewRequiredForSourceApply": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": true,
          "warnings": [],
          "errors": [
            {
              "category": "validation_error",
              "code": "MANUAL_MEMORY_IMPROVEMENT_API_VALIDATION_ERROR",
              "message": "Manual memory-improvement request is invalid.",
              "field": "agentRunId"
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

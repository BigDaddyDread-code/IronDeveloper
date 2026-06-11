using System.Net;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliManualCriticTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();

    [TestMethod]
    public async Task CliManualCritic_Create_CallsManualCriticApi()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            [
                "critic",
                "review",
                "create",
                "--project-id",
                "42",
                "--target-agent-run-id",
                "agent-run-42",
                "--review-kind",
                "code",
                "--focus",
                "Review public boundary evidence",
                "--reason",
                "Public critic reason",
                "--evidence-ref",
                "agent-run:agent-run-42",
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
        Assert.AreEqual("/api/v1/manual-critic/reviews", handler.Request?.RequestUri?.PathAndQuery);
        Assert.AreEqual("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.AreEqual("super-secret-token", handler.Request?.Headers.Authorization?.Parameter);

        using var body = JsonDocument.Parse(handler.Body ?? "{}");
        Assert.AreEqual(42, body.RootElement.GetProperty("projectId").GetInt32());
        Assert.AreEqual("AgentRun", body.RootElement.GetProperty("subjectType").GetString());
        Assert.AreEqual("agent-run-42", body.RootElement.GetProperty("subjectId").GetString());
        Assert.AreEqual("Review public boundary evidence", body.RootElement.GetProperty("summary").GetString());
        Assert.AreEqual("Public critic reason", body.RootElement.GetProperty("content").GetString());
        Assert.AreEqual("corr-42", body.RootElement.GetProperty("correlationId").GetString());
        Assert.AreEqual("agent-run:agent-run-42", body.RootElement.GetProperty("evidenceRefs")[0].GetString());
        Assert.IsFalse(handler.Body!.Contains("targetAgentRunId", StringComparison.OrdinalIgnoreCase));
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliManualCritic_Get_CallsManualCriticDetailApi()
    {
        var handler = new RecordingHandler(GetEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            [
                "critic",
                "review",
                "get",
                "manual-independent-critic-42",
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
        Assert.AreEqual("/api/v1/manual-critic/reviews/manual-independent-critic-42?projectId=42", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Manual critic review");
        StringAssert.Contains(output.ToString(), "Critic is not governance.");
        StringAssert.Contains(output.ToString(), "Critic review is not approval.");
    }

    [TestMethod]
    public async Task CliManualCritic_MissingProjectId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "create", "--target-agent-run-id", "agent-run-42", "--api-base-url", "https://api.example.test"],
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
    public async Task CliManualCritic_MissingTargetAgentRunId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "create", "--project-id", "42", "--api-base-url", "https://api.example.test"],
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
    public async Task CliManualCritic_UnsupportedAuthorityFlag_ReturnsUsageError()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            [
                "critic",
                "review",
                "create",
                "--project-id",
                "42",
                "--target-agent-run-id",
                "agent-run-42",
                "--approve",
                "--api-base-url",
                "https://api.example.test"
            ],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
        StringAssert.Contains(error.ToString(), "Unsupported authority or execution flag");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task CliManualCritic_JsonOutput_PreservesApiWarnings()
    {
        var handler = new RecordingHandler(CreateEnvelope(warnings: ["manual critic warning"]));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "get", "manual-independent-critic-42", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("manual critic warning", json.RootElement.GetProperty("warnings")[0].GetString());
    }

    [TestMethod]
    public async Task CliManualCritic_DoesNotPrintToken()
    {
        var handler = new RecordingHandler(CreateEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "get", "manual-independent-critic-42", "--project-id", "42", "--api-base-url", "https://api.example.test", "--token", "never-print-me"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode, error.ToString());
        AssertNoTokenLeak(output.ToString(), error.ToString());
    }

    [TestMethod]
    public async Task CliManualCritic_DoesNotPrintHiddenReasoningMarkers()
    {
        var handler = new RecordingHandler(GetEnvelope(summary: "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning"));
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "get", "manual-independent-critic-private", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json"],
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
    public async Task CliManualCritic_ApiFailure_ReturnsApiFailure()
    {
        var handler = new RecordingHandler(ErrorEnvelope(), HttpStatusCode.BadRequest);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "get", "missing-run", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(4, exitCode);
        StringAssert.Contains(error.ToString(), "MANUAL_CRITIC_API_VALIDATION_ERROR");
    }

    [TestMethod]
    public async Task CliManualCritic_ConnectionFailure_ReturnsConnectionFailure()
    {
        var handler = new ThrowingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliManualCritic.HandleAsync(
            ["critic", "review", "get", "manual-independent-critic-42", "--project-id", "42", "--api-base-url", "https://api.example.test"],
            output,
            error,
            EmptyEnvironment,
            handler,
            CancellationToken.None);

        Assert.AreEqual(6, exitCode);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_API_CONNECTION_FAILED");
    }

    [TestMethod]
    public void CliManualCritic_DoesNotReferenceBackendExecutionServices()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliManualCritic.cs"));

        var forbidden = new[]
        {
            "IStoredManualIndependentCriticAgentService",
            "IManualIndependentCriticAgentService",
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
            "Weaviate"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden backend/execution token found in manual critic CLI: {token}");

        StringAssert.Contains(text, "IIronDevApiClient");
        StringAssert.Contains(text, "CreateManualCriticReviewAsync");
        StringAssert.Contains(text, "GetManualCriticReviewAsync");
    }

    [TestMethod]
    public void CliManualCritic_DoesNotReferenceGithubReviewSubmission()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliManualCritic.cs"));

        var forbidden = new[]
        {
            "GitHubReviewService",
            "SubmitGitHubReview",
            "CreatePullRequest",
            "PullRequestReview"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden GitHub submission token found in manual critic CLI: {token}");
    }

    private static string CreateEnvelope(IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "succeeded",
          "runId": "manual-independent-critic-42",
          "reviewId": "critic-review-42",
          "evidenceId": "evidence-42",
          "boundary": {
            "criticIsGovernance": false,
            "criticIsApproval": false,
            "auditIsApproval": false,
            "proposalWasApplied": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "toolExecuted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": true,
          "humanApprovalRequired": true,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Critic review is advisory. It is not governance, approval, source apply, memory promotion, tool execution, or execution permission."])}},
          "errors": [],
          "data": {
            "agentRunId": "manual-independent-critic-42",
            "reviewId": "critic-review-42",
            "reviewRequestId": "review-request-42",
            "status": "Stored",
            "verdict": "RequestChanges",
            "reviewedAtUtc": "2026-06-12T00:00:00Z",
            "findingCount": 1,
            "findings": [],
            "evidenceRefs": ["evidence-42"],
            "mutationOccurred": true,
            "advisoryOnly": true,
            "requiresHumanReview": true,
            "warnings": []
          }
        }
        """;

    private static string GetEnvelope(string summary = "Safe public critic summary", IReadOnlyList<string>? warnings = null) =>
        $$"""
        {
          "status": "succeeded",
          "runId": "manual-independent-critic-42",
          "reviewId": "critic-review-42",
          "evidenceId": "evidence-42",
          "boundary": {
            "criticIsGovernance": false,
            "criticIsApproval": false,
            "auditIsApproval": false,
            "proposalWasApplied": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "toolExecuted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": true,
          "warnings": {{JsonSerializer.Serialize(warnings ?? ["Critic review is advisory only."])}},
          "errors": [],
          "data": {
            "projectId": "42",
            "agentRunId": "manual-independent-critic-42",
            "reviewId": "critic-review-42",
            "status": "Completed",
            "agentId": "independent-critic",
            "agentName": "IndependentCriticAgent",
            "requestSummary": {{JsonSerializer.Serialize(summary)}},
            "purpose": "Manual critic evidence inspection.",
            "createdAtUtc": "2026-06-12T00:00:00Z",
            "completedAtUtc": "2026-06-12T00:00:00Z",
            "inputSummaries": [{{JsonSerializer.Serialize(summary)}}],
            "outputSummaries": [{{JsonSerializer.Serialize(summary)}}],
            "thoughtLedgerSummaries": [{{JsonSerializer.Serialize(summary)}}],
            "evidenceRefs": ["evidence-42"],
            "reviewOnlyOutput": true,
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
            "advisoryOnly": true,
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
          "reviewId": "",
          "evidenceId": "",
          "boundary": {
            "criticIsGovernance": false,
            "criticIsApproval": false,
            "auditIsApproval": false,
            "proposalWasApplied": false,
            "sourceApplied": false,
            "memoryPromoted": false,
            "toolExecuted": false,
            "modelOutputIsAuthority": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true
          },
          "mutationOccurred": false,
          "humanApprovalRequired": true,
          "warnings": [],
          "errors": [
            {
              "category": "validation_error",
              "code": "MANUAL_CRITIC_API_VALIDATION_ERROR",
              "message": "Manual critic request is invalid.",
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

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliAgentRunsTests
{
    [TestMethod]
    public async Task CliAgentRuns_List_CallsAgentRunsApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ListEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[]
            {
                "agent-runs", "list",
                "--project-id", "42",
                "--agent-id", "IndependentCriticAgent",
                "--status", "Completed",
                "--take", "25",
                "--api-base-url", "https://api.example.test",
                "--token", "secret-token"
            },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("https://api.example.test/api/v1/agent-runs?projectId=42&agentId=IndependentCriticAgent&status=Completed&take=25", handler.RequestUri?.ToString());
        AssertBearerToken(handler);
        StringAssert.Contains(output.ToString(), "agent-run-001");
        AssertTokenNotPrinted(output, error);
    }

    [TestMethod]
    public async Task CliAgentRuns_Get_CallsAgentRunDetailApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, DetailEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "get", "agent-run-001", "--project-id", "42", "--api-base-url", "https://api.example.test" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("https://api.example.test/api/v1/agent-runs/agent-run-001?projectId=42", handler.RequestUri?.ToString());
        StringAssert.Contains(output.ToString(), "Agent run id: agent-run-001");
        StringAssert.Contains(output.ToString(), "CLI inspection is not execution permission.");
    }

    [TestMethod]
    public async Task CliAgentRuns_Audit_CallsAgentRunAuditApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, AuditEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "audit", "agent-run-001", "--project-id", "42", "--api-base-url", "https://api.example.test" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("https://api.example.test/api/v1/agent-runs/agent-run-001/audit?projectId=42", handler.RequestUri?.ToString());
        StringAssert.Contains(output.ToString(), "Audit is not approval.");
        StringAssert.Contains(output.ToString(), "Evidence is not permission.");
    }

    [TestMethod]
    public async Task CliAgentRuns_MissingProjectId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ListEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "list", "--api-base-url", "https://api.example.test" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.RequestUri);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_USAGE_ERROR");
    }

    [TestMethod]
    public async Task CliAgentRuns_MissingAgentRunId_ReturnsUsageError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, DetailEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "get", "--project-id", "42", "--api-base-url", "https://api.example.test" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.RequestUri);
        StringAssert.Contains(error.ToString(), "Missing required argument");
    }

    [TestMethod]
    public async Task CliAgentRuns_UnknownSubcommand_ReturnsUsageError()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ListEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "start", "--project-id", "42", "--api-base-url", "https://api.example.test" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.RequestUri);
        StringAssert.Contains(error.ToString(), "Unknown agent-runs subcommand");
    }

    [TestMethod]
    public async Task CliAgentRuns_JsonOutput_PreservesApiWarnings()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, ListEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "list", "--project-id", "42", "--api-base-url", "https://api.example.test", "--output", "json" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.IsTrue(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("agent-runs list", document.RootElement.GetProperty("command").GetString());
        Assert.AreEqual("API warning from PR58.", document.RootElement.GetProperty("warnings")[0].GetString());
        Assert.AreEqual("succeeded", document.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task CliAgentRuns_ApiErrorsArePreserved()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, ErrorEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "get", "missing-run", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.ApiFailure, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.IsFalse(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("AGENT_RUN_NOT_FOUND", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.AreEqual("not_found", document.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task CliAgentRuns_DoesNotPrintToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, AuditEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "audit", "agent-run-001", "--project-id", "42", "--api-base-url", "https://api.example.test", "--token", "secret-token" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        AssertBearerToken(handler);
        AssertTokenNotPrinted(output, error);
    }

    [TestMethod]
    public async Task CliAgentRuns_DoesNotPrintHiddenReasoningMarkers()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, PrivateReasoningEnvelope());
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "get", "agent-run-private", "--project-id", "42", "--api-base-url", "https://api.example.test", "--json" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        var text = output.ToString();
        AssertNoPrivateReasoningLeak(text);
        StringAssert.Contains(text, "[redacted: sensitive audit text]");
    }

    [TestMethod]
    public async Task CliAgentRuns_ConnectionFailure_ReturnsConnectionFailure()
    {
        var handler = new ThrowingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "agent-runs", "list", "--project-id", "42", "--api-base-url", "https://api.example.test" },
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.ConnectionFailure, exitCode);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_API_CONNECTION_FAILED");
    }

    [TestMethod]
    public void CliAgentRuns_DoesNotReferenceBackendExecutionServices()
    {
        var root = LocateRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliAgentRuns.cs"));
        var forbidden = new[]
        {
            "ISupervisorAgentRunService",
            "IAgentRunAuditEnvelopeStore",
            "ManualTesterAgentToolExecutionService",
            "IStoredManualIndependentCriticAgentService",
            "IStoredManualMemoryImprovementAgentService",
            "ApplyCopy",
            "PromoteCollectiveMemory",
            "SqlConnection",
            "Weaviate",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "IHostedService",
            "BackgroundService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden token found: {token}");
    }

    private static string ListEnvelope() =>
        """
        {
          "status": "succeeded",
          "data": {
            "projectId": "42",
            "items": [
              {
                "agentRunId": "agent-run-001",
                "agentId": "IndependentCriticAgent",
                "agentName": "IndependentCriticAgent",
                "agentKind": "ReviewAgent",
                "executionMode": "OutOfBandReviewOnly",
                "status": "Completed",
                "triggerType": "ManualUserRequest",
                "createdAtUtc": "2026-06-12T01:00:00Z",
                "completedAtUtc": "2026-06-12T01:05:00Z",
                "inputCount": 1,
                "outputCount": 1,
                "thoughtLedgerCount": 1,
                "capabilityUseCount": 1,
                "boundaryDecisionCount": 1,
                "blockedCapabilityCount": 0,
                "hasBoundaryBlocks": false,
                "hasUnsafeAttempt": false
              }
            ],
            "totalCount": 1,
            "issues": []
          },
          "runId": "",
          "evidenceId": "",
          "boundary": {
            "readOnlyInspection": true,
            "auditIsApproval": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "modelOutputIsAuthority": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true,
            "hasBoundaryWarnings": false
          },
          "mutationOccurred": false,
          "humanApprovalRequired": false,
          "warnings": ["API warning from PR58."],
          "errors": []
        }
        """;

    private static string DetailEnvelope() =>
        """
        {
          "status": "succeeded",
          "data": {
            "projectId": "42",
            "agentRunId": "agent-run-001",
            "run": {
              "run": {
                "agentRunId": "agent-run-001",
                "projectId": "42",
                "runId": "correlation-001",
                "agentId": "IndependentCriticAgent",
                "agentName": "IndependentCriticAgent",
                "status": "Completed",
                "triggerType": "ManualUserRequest",
                "createdAtUtc": "2026-06-12T01:00:00Z",
                "completedAtUtc": "2026-06-12T01:05:00Z"
              },
              "agentDefinition": {
                "agentId": "IndependentCriticAgent",
                "name": "IndependentCriticAgent",
                "kind": "ReviewAgent"
              },
              "inputs": [{}],
              "outputs": [{}],
              "capabilityUses": [],
              "boundaryDecisions": [],
              "thoughtLedger": [],
              "steps": [],
              "safetySummary": {
                "containsRawPrivateReasoning": false,
                "hasAuthorityClaim": false,
                "hasApprovalClaim": false,
                "hasMemoryPromotionClaim": false,
                "hasRuntimeActionOutput": false,
                "hasAuthorityCreatingOutput": false,
                "hasBlockedCapabilityAttempt": false,
                "hasBoundaryBlock": false,
                "warnings": []
              }
            },
            "issues": []
          },
          "runId": "agent-run-001",
          "evidenceId": "",
          "boundary": {
            "readOnlyInspection": true,
            "auditIsApproval": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "modelOutputIsAuthority": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true,
            "hasBoundaryWarnings": false
          },
          "mutationOccurred": false,
          "humanApprovalRequired": false,
          "warnings": [],
          "errors": []
        }
        """;

    private static string AuditEnvelope() =>
        """
        {
          "status": "succeeded",
          "data": {
            "projectId": "42",
            "agentRunId": "agent-run-001",
            "inputCount": 1,
            "outputCount": 1,
            "thoughtLedgerCount": 1,
            "capabilityUseCount": 1,
            "boundaryDecisionCount": 1,
            "evidenceReferences": ["evidence-001"],
            "safetySummary": {
              "containsRawPrivateReasoning": false,
              "hasAuthorityClaim": false,
              "hasApprovalClaim": false,
              "hasMemoryPromotionClaim": false,
              "hasRuntimeActionOutput": false,
              "hasAuthorityCreatingOutput": false,
              "hasBlockedCapabilityAttempt": false,
              "hasBoundaryBlock": false,
              "warnings": []
            },
            "boundaryStatus": {
              "readOnlyInspection": true,
              "auditIsApproval": false,
              "endpointAccessIsExecutionPermission": false,
              "apiResponseStatusIsGovernance": false,
              "modelOutputIsAuthority": false,
              "humanReviewRequiredForSourceApply": true,
              "humanReviewRequiredForMemoryPromotion": true,
              "hasBoundaryWarnings": false
            },
            "auditIsApproval": false,
            "evidenceIsPermission": false
          },
          "runId": "agent-run-001",
          "evidenceId": "evidence-001",
          "boundary": {
            "readOnlyInspection": true,
            "auditIsApproval": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "modelOutputIsAuthority": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true,
            "hasBoundaryWarnings": false
          },
          "mutationOccurred": false,
          "humanApprovalRequired": false,
          "warnings": [],
          "errors": []
        }
        """;

    private static string ErrorEnvelope() =>
        """
        {
          "status": "not_found",
          "data": null,
          "runId": "missing-run",
          "evidenceId": "",
          "boundary": {
            "readOnlyInspection": true,
            "auditIsApproval": false,
            "endpointAccessIsExecutionPermission": false,
            "apiResponseStatusIsGovernance": false,
            "modelOutputIsAuthority": false,
            "humanReviewRequiredForSourceApply": true,
            "humanReviewRequiredForMemoryPromotion": true,
            "hasBoundaryWarnings": false
          },
          "mutationOccurred": false,
          "humanApprovalRequired": false,
          "warnings": [],
          "errors": [
            {
              "category": "not_found",
              "code": "AGENT_RUN_NOT_FOUND",
              "message": "Agent run was not found."
            }
          ]
        }
        """;

    private static string PrivateReasoningEnvelope() =>
        DetailEnvelope().Replace("IndependentCriticAgent", "PRIVATE_MARKER chain-of-thought hidden reasoning raw prompt scratchpad private reasoning", StringComparison.Ordinal);

    private static void AssertBearerToken(RecordingHandler handler)
    {
        Assert.AreEqual("Bearer", handler.Authorization?.Scheme);
        Assert.AreEqual("secret-token", handler.Authorization?.Parameter);
    }

    private static void AssertTokenNotPrinted(StringWriter output, StringWriter error)
    {
        Assert.IsFalse(output.ToString().Contains("secret-token", StringComparison.Ordinal), "Token leaked to stdout.");
        Assert.IsFalse(error.ToString().Contains("secret-token", StringComparison.Ordinal), "Token leaked to stderr.");
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
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Output contained private reasoning marker: {token}");
    }

    private static string LocateRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent is null)
                break;

            current = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network down");
    }
}

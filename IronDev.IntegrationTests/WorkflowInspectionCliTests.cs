using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkflowInspectionCliTests
{
    private const string ProjectId = "project-104";
    private const string RunId = "workflow-run-104";
    private const string StepId = "workflow-step-104";
    private const string CheckpointId = "workflow-checkpoint-104";
    private const string CorrelationId = "correlation-104";

    [TestMethod]
    public async Task WorkflowInspectionCli_Runs_CallsWorkflowRunsEndpoint()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--project", ProjectId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs?projectId={ProjectId}&take=100", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Workflow inspection is read-only.");
        StringAssert.Contains(output.ToString(), "Statuses printed by the CLI are stored facts, not runtime actions.");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_Run_CallsWorkflowRunEndpoint()
    {
        var handler = new RecordingHandler(SingleEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "run", "--project", ProjectId, "--run", RunId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/{RunId}?projectId={ProjectId}", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Workflow run");
        StringAssert.Contains(output.ToString(), RunId);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_RunsByCorrelation_CallsCorrelationEndpoint()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs-by-correlation", "--project", ProjectId, "--correlation", CorrelationId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/by-correlation/{CorrelationId}?projectId={ProjectId}&take=100", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_RunsBySubject_CallsSubjectEndpoint()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs-by-subject", "--project", ProjectId, "--subject-type", "Ticket", "--subject-id", "ticket-42", "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/by-subject?projectId={ProjectId}&subjectType=Ticket&subjectId=ticket-42&take=100", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_Steps_CallsWorkflowStepsEndpoint()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunStepId", StepId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "steps", "--project", ProjectId, "--run", RunId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/{RunId}/steps?projectId={ProjectId}&take=100", handler.Request?.RequestUri?.PathAndQuery);
        StringAssert.Contains(output.ToString(), "Workflow steps");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_Step_CallsWorkflowStepEndpoint()
    {
        var handler = new RecordingHandler(SingleEnvelope("workflowRunStepId", StepId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "step", "--project", ProjectId, "--run", RunId, "--step", StepId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/{RunId}/steps/{StepId}?projectId={ProjectId}", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_Checkpoints_CallsWorkflowCheckpointsEndpoint()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowCheckpointId", CheckpointId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "checkpoints", "--project", ProjectId, "--run", RunId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/{RunId}/checkpoints?projectId={ProjectId}&take=100", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_StepCheckpoints_CallsWorkflowStepCheckpointsEndpoint()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowCheckpointId", CheckpointId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "step-checkpoints", "--project", ProjectId, "--run", RunId, "--step", StepId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/{RunId}/steps/{StepId}/checkpoints?projectId={ProjectId}&take=100", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_Checkpoint_CallsWorkflowCheckpointEndpoint()
    {
        var handler = new RecordingHandler(SingleEnvelope("workflowCheckpointId", CheckpointId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "checkpoint", "--project", ProjectId, "--run", RunId, "--checkpoint", CheckpointId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/v1/workflow/runs/{RunId}/checkpoints/{CheckpointId}?projectId={ProjectId}", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_TakeIsCapped()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--project", ProjectId, "--take", "999", "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual($"/api/v1/workflow/runs?projectId={ProjectId}&take=500", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_JsonOutput_IncludesReadOnlyBoundary()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--project", ProjectId, "--api-base-url", "https://api.example.test", "--json"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.IsTrue(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("workflow inspect runs", json.RootElement.GetProperty("command").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("boundary").GetProperty("readOnly").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("startsWorkflow").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("callsTool").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("mutatesSource").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("promotesMemory").GetBoolean());
        Assert.IsFalse(json.RootElement.GetProperty("boundary").GetProperty("satisfiesApprovalRequirements").GetBoolean());
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_MissingProject_ReturnsUsageError()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.Request);
        StringAssert.Contains(error.ToString(), "--project");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_UnknownSubcommand_ReturnsUsageError()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "execute", "--project", ProjectId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.Request);
        StringAssert.Contains(error.ToString(), "Unknown workflow inspect subcommand");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_UnsupportedOption_ReturnsUsageError()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--project", ProjectId, "--approve", "true", "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.Request);
        StringAssert.Contains(error.ToString(), "Unsupported workflow inspection option");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_DoesNotPrintToken()
    {
        var handler = new RecordingHandler(ListEnvelope("workflowRunId", RunId));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--project", ProjectId, "--api-base-url", "https://api.example.test", "--token", "secret-token"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual("Bearer", handler.Authorization?.Scheme);
        Assert.AreEqual("secret-token", handler.Authorization?.Parameter);
        AssertNoLeak(output.ToString(), "secret-token");
        AssertNoLeak(error.ToString(), "secret-token");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_DoesNotPrintHiddenReasoningMarkers()
    {
        var handler = new RecordingHandler(SingleEnvelope("workflowRunId", "PRIVATE_MARKER chainOfThought rawPrompt rawCompletion rawToolOutput entirePatch private reasoning hidden reasoning scratchpad"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "run", "--project", ProjectId, "--run", RunId, "--api-base-url", "https://api.example.test", "--json"], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        AssertNoPrivateReasoningLeak(output.ToString());
        StringAssert.Contains(output.ToString(), "[redacted: sensitive workflow text]");
    }

    [TestMethod]
    public async Task WorkflowInspectionCli_ConnectionFailure_ReturnsConnectionFailure()
    {
        var handler = new ThrowingHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync(["workflow", "inspect", "runs", "--project", ProjectId, "--api-base-url", "https://api.example.test"], output, error, handler);

        Assert.AreEqual(IronDevCliFoundation.ConnectionFailure, exitCode);
        StringAssert.Contains(error.ToString(), "IRONDEV_CLI_API_CONNECTION_FAILED");
    }

    [TestMethod]
    public void WorkflowInspectionCli_DoesNotReferenceWorkflowRuntimeOrMutationServices()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliWorkflowInspection.cs"));
        var forbidden = new[]
        {
            "IWorkflowRunner",
            "WorkflowRunner",
            "LangGraph",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ApplyCopy",
            "PromoteCollectiveMemory",
            "ApprovalDecisionStore",
            "PolicyDecisionEventStore",
            "AgentHandoffStore",
            "DispatchAgent",
            "ExecuteTool"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in workflow inspection CLI: {token}");

        StringAssert.Contains(source, "IIronDevApiClient");
        StringAssert.Contains(source, "GetWorkflowRunAsync");
        StringAssert.Contains(source, "ListWorkflowStepsAsync");
        StringAssert.Contains(source, "ListWorkflowCheckpointsAsync");
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, HttpMessageHandler handler) =>
        await IronDevCli.RunAsync(args, output, error, handler, CancellationToken.None).ConfigureAwait(false);

    private static string ListEnvelope(string idName, string idValue) =>
        $$"""
        {
          "status": "succeeded",
          "data": {
            "items": [
              {
                "{{idName}}": "{{idValue}}",
                "status": "Recorded",
                "safeSummary": "Safe workflow inspection summary.",
                "evidenceRefs": ["evidence-104"],
                "groundingRefs": ["grounding-104"]
              }
            ],
            "totalCount": 1
          },
          "warnings": ["Workflow CLI inspection is evidence only."],
          "errors": []
        }
        """;

    private static string SingleEnvelope(string idName, string idValue) =>
        $$"""
        {
          "status": "succeeded",
          "data": {
            "{{idName}}": "{{idValue}}",
            "status": "Recorded",
            "stepType": "InspectionOnly",
            "checkpointType": "InspectionOnly",
            "safeSummary": "Safe workflow inspection summary.",
            "evidenceRefs": ["evidence-104"],
            "groundingRefs": ["grounding-104"]
          },
          "warnings": ["Workflow CLI inspection is evidence only."],
          "errors": []
        }
        """;

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        var forbidden = new[]
        {
            "PRIVATE_MARKER",
            "chainOfThought",
            "rawPrompt",
            "rawCompletion",
            "rawToolOutput",
            "entirePatch",
            "private reasoning",
            "hidden reasoning",
            "scratchpad"
        };

        foreach (var token in forbidden)
            AssertNoLeak(text, token);
    }

    private static void AssertNoLeak(string text, string token) =>
        Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Output leaked token: {token}");

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
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
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

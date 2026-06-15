using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApplyPreview")]
public sealed class ApplyPreviewCliTests
{
    private const string WorkflowRunId = "workflow-run-142";
    private const string WorkflowStepId = "workflow-step-142";
    private const string ControlledApplyPlanId = "controlled-apply-plan-142";

    [TestMethod]
    public async Task ApplyPreviewCli_CallsApplyPreviewApiWithAllowedQueryParameters()
    {
        var handler = new RecordingHandler(PreviewEnvelope());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync([
            "workflow",
            "apply-preview",
            "--workflow-run",
            WorkflowRunId,
            "--workflow-step",
            WorkflowStepId,
            "--controlled-apply-plan",
            ControlledApplyPlanId,
            "--take-dry-runs",
            "999",
            "--api-base-url",
            "https://api.example.test"
        ], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        Assert.AreEqual($"/api/workflow/apply-preview/{WorkflowRunId}/{WorkflowStepId}?takeDryRuns=50&includeDryRunSummaries=true&controlledApplyPlanReferenceId={ControlledApplyPlanId}", handler.Request?.RequestUri?.PathAndQuery);

        var text = output.ToString();
        StringAssert.Contains(text, "Apply Preview");
        StringAssert.Contains(text, "Preview only.");
        StringAssert.Contains(text, "Source apply remains unimplemented.");
        StringAssert.Contains(text, "Patch apply remains unimplemented.");
        StringAssert.Contains(text, "Apply dry-run execution remains unimplemented.");
        StringAssert.Contains(text, "Approval was not satisfied.");
        StringAssert.Contains(text, "Policy was not satisfied.");
        StringAssert.Contains(text, "Workflow was not transitioned.");
    }

    [TestMethod]
    public async Task ApplyPreviewCli_NoDryRuns_DisablesDryRunSummariesQuery()
    {
        var handler = new RecordingHandler(PreviewEnvelope());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync([
            "workflow",
            "apply-preview",
            "--workflow-run",
            WorkflowRunId,
            "--workflow-step",
            WorkflowStepId,
            "--no-dry-runs",
            "--api-base-url",
            "https://api.example.test"
        ], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual($"/api/workflow/apply-preview/{WorkflowRunId}/{WorkflowStepId}?takeDryRuns=10&includeDryRunSummaries=false", handler.Request?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApplyPreviewCli_JsonOutput_ReportsNonAuthorityBoundary()
    {
        var handler = new RecordingHandler(PreviewEnvelope());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync([
            "workflow",
            "apply-preview",
            "--workflow-run",
            WorkflowRunId,
            "--workflow-step",
            WorkflowStepId,
            "--api-base-url",
            "https://api.example.test",
            "--json"
        ], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;

        Assert.IsTrue(root.GetProperty("ok").GetBoolean());
        Assert.AreEqual("workflow apply-preview", root.GetProperty("command").GetString());
        Assert.IsTrue(root.GetProperty("boundary").GetProperty("previewOnly").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("canApplySource").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("dryRunExecuted").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("sourceMutated").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("patchApplied").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("approvalSatisfied").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("policySatisfied").GetBoolean());
        Assert.IsFalse(root.GetProperty("boundary").GetProperty("workflowTransitioned").GetBoolean());
    }

    [TestMethod]
    public async Task ApplyPreviewCli_MissingRequiredArguments_ReturnsUsageErrorWithoutCallingApi()
    {
        var handler = new RecordingHandler(PreviewEnvelope());
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync([
            "workflow",
            "apply-preview",
            "--workflow-run",
            WorkflowRunId,
            "--api-base-url",
            "https://api.example.test"
        ], output, error, handler);

        Assert.AreEqual(IronDevCliFoundation.UsageError, exitCode);
        Assert.IsNull(handler.Request);
        StringAssert.Contains(error.ToString(), "--workflow-step");
    }

    [TestMethod]
    public async Task ApplyPreviewCli_RedactsHiddenReasoningMarkersFromOutput()
    {
        var handler = new RecordingHandler(PreviewEnvelope("chainOfThought rawPrompt rawCompletion rawToolOutput entirePatch private reasoning hidden reasoning scratchpad"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync([
            "workflow",
            "apply-preview",
            "--workflow-run",
            WorkflowRunId,
            "--workflow-step",
            WorkflowStepId,
            "--api-base-url",
            "https://api.example.test",
            "--json"
        ], output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        var text = output.ToString();
        AssertNoLeak(text, "chainOfThought");
        AssertNoLeak(text, "rawPrompt");
        AssertNoLeak(text, "rawCompletion");
        AssertNoLeak(text, "rawToolOutput");
        AssertNoLeak(text, "entirePatch");
        AssertNoLeak(text, "private reasoning");
        AssertNoLeak(text, "hidden reasoning");
        AssertNoLeak(text, "scratchpad");
        StringAssert.Contains(text, "[redacted: sensitive apply preview text]");
    }

    [TestMethod]
    public async Task ApplyPreviewCli_ApiFailure_ReturnsNonZeroAndDoesNotClaimAuthority()
    {
        var handler = new RecordingHandler(
            """
            {
              "status": "validation_error",
              "warnings": ["Apply preview is evidence only."],
              "errors": [{ "code": "apply_preview_invalid", "message": "Invalid apply preview request." }]
            }
            """,
            HttpStatusCode.BadRequest);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunAsync([
            "workflow",
            "apply-preview",
            "--workflow-run",
            WorkflowRunId,
            "--workflow-step",
            WorkflowStepId,
            "--api-base-url",
            "https://api.example.test"
        ], output, error, handler);

        Assert.AreEqual(IronDevCliFoundation.ApiFailure, exitCode);
        Assert.AreEqual(HttpMethod.Get, handler.Request?.Method);
        AssertNoLeak(output.ToString() + error.ToString(), "\"sourceApplied\": true");
        AssertNoLeak(output.ToString() + error.ToString(), "\"memoryPromoted\": true");
    }

    [TestMethod]
    public void ApplyPreviewCli_DoesNotReferenceRuntimeMutationOrPersistenceServices()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliApplyPreview.cs"));
        var forbidden = new[]
        {
            "IWorkflowRunner",
            "WorkflowRunner",
            "IWorkflowDryRunExecutor",
            "IControlledApplyExecutor",
            "ApplySourceService",
            "SourceApplyService",
            "PatchApplyService",
            "IApplyDryRunStore",
            "SqlConnection",
            "DbConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ProcessStartInfo",
            "File.Write",
            "File.Copy",
            "File.Delete",
            "Directory.CreateDirectory",
            "OpenAI",
            "LangGraph",
            "ApprovalDecisionStore",
            "PolicyDecisionEventStore",
            "MemoryPromotion",
            "RetrievalActivation",
            "AgentDispatcher"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden token found in apply preview CLI: {token}");

        StringAssert.Contains(source, "IronDevApiClientFactory");
        StringAssert.Contains(source, "GetApplyPreviewAsync");
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, HttpMessageHandler handler) =>
        await IronDevCli.RunAsync(args, output, error, handler, CancellationToken.None).ConfigureAwait(false);

    private static string PreviewEnvelope(string safeSummary = "Safe apply preview summary.") =>
        $$"""
        {
          "status": "succeeded",
          "mutationOccurred": false,
          "previewStatus": "preview_available",
          "boundary": {
            "previewOnly": true,
            "canApplySource": false,
            "dryRunExecuted": false,
            "sourceMutated": false,
            "patchApplied": false,
            "approvalSatisfied": false,
            "policySatisfied": false,
            "workflowTransitioned": false
          },
          "warnings": ["Apply preview is evidence only."],
          "errors": [],
          "data": {
            "status": "preview_available",
            "previewReferenceId": "apply-preview-142",
            "workflowRunId": "{{WorkflowRunId}}",
            "workflowStepId": "{{WorkflowStepId}}",
            "controlledApplyPlanReferenceId": "{{ControlledApplyPlanId}}",
            "safeSummaryLines": ["{{safeSummary}}"],
            "dryRunSummaries": [
              {
                "applyDryRunReferenceId": "apply-dry-run-142",
                "status": "recorded",
                "safeSummary": "{{safeSummary}}"
              }
            ],
            "missingEvidence": [
              {
                "kind": "human_review",
                "safeSummary": "Human review remains required."
              }
            ],
            "gates": [
              {
                "kind": "source_apply",
                "status": "blocked",
                "safeSummary": "Source apply remains unavailable."
              }
            ],
            "risks": [
              {
                "kind": "review_required",
                "safeSummary": "Review is required before any apply path."
              }
            ],
            "issues": [],
            "isPreviewOnly": true,
            "canExecuteDryRun": false,
            "isDryRunExecution": false,
            "canApplySource": false,
            "appliesPatch": false,
            "readsSourceFiles": false,
            "mutatesFiles": false,
            "runsCommand": false,
            "invokesTool": false,
            "runsValidation": false,
            "runsRollback": false,
            "satisfiesApproval": false,
            "satisfiesPolicy": false,
            "transitionsWorkflow": false,
            "promotesMemory": false,
            "activatesRetrieval": false,
            "dispatchesAgent": false,
            "callsModel": false
          }
        }
        """;

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
}

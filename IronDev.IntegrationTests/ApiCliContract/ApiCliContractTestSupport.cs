using System.Net;
using System.Text;
using IronDev.Cli;

namespace IronDev.IntegrationTests.ApiCliContract;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;
    private readonly HttpStatusCode _statusCode;

    public RecordingHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _statusCode = statusCode;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        };

        return Task.FromResult(response);
    }
}

internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        throw _exception;
    }
}

internal static class ApiCliContractTestSupport
{
    public const string BaseUrl = "https://irondev.example.test";
    public const string ProjectId = "42";
    public const string AgentRunId = "agent-run-api-cli-contract";
    public const string ToolRequestId = "tool-request-api-cli-contract";
    public const string DogfoodLoopId = "dogfood-loop-api-cli-contract";
    public const string WorkflowRunId = "workflow-run-api-cli-contract";
    public const string WorkflowStepId = "workflow-step-api-cli-contract";
    public const string ControlledApplyPlanId = "apply-plan-api-cli-contract";

    public static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();

    public static HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl)
        };
    }

    public static StringWriter NewWriter()
    {
        return new StringWriter();
    }

    public static string[] CommonJsonArgs(params string[] args)
    {
        return args.Concat(new[] { "--api-base-url", BaseUrl, "--output", "json" }).ToArray();
    }

    public static string[] CommonTextArgs(params string[] args)
    {
        return args.Concat(new[] { "--api-base-url", BaseUrl, "--output", "text" }).ToArray();
    }

    public static string AgentRunsListEnvelope()
    {
        return """
        {
          "ok": true,
          "command": "agent-runs list",
          "status": "ok",
          "data": {
            "projectId": 42,
            "items": [
              {
                "agentRunId": "agent-run-api-cli-contract",
                "agentName": "IndependentCriticAgent",
                "status": "completed"
              }
            ],
            "auditIsApproval": false,
            "apiStatusIsApproval": false
          },
          "warnings": ["Audit is evidence, not approval."],
          "errors": []
        }
        """;
    }

    public static string AgentRunDetailEnvelope(string hiddenText = "safe public summary")
    {
        return $$"""
        {
          "ok": true,
          "command": "agent-runs get",
          "status": "ok",
          "data": {
            "agentRunId": "{{AgentRunId}}",
            "projectId": "{{ProjectId}}",
            "summary": "{{hiddenText}}",
            "auditIsApproval": false,
            "apiStatusIsApproval": false,
            "executionPermission": false
          },
          "warnings": ["Audit is evidence, not approval.", "CLI inspection is not execution permission."],
          "errors": []
        }
        """;
    }

    public static string AgentRunAuditEnvelope(string hiddenText = "safe audit detail")
    {
        return $$"""
        {
          "ok": true,
          "command": "agent-runs audit",
          "status": "ok",
          "data": {
            "agentRunId": "{{AgentRunId}}",
            "audit": {
              "summary": "{{hiddenText}}",
              "auditIsApproval": false,
              "evidenceIsPermission": false
            }
          },
          "warnings": ["Audit is not approval.", "Evidence is not permission."],
          "errors": []
        }
        """;
    }

    public static string ManualCriticEnvelope()
    {
        return $$"""
        {
          "ok": true,
          "command": "critic review create",
          "status": "created",
          "data": {
            "agentRunId": "{{AgentRunId}}",
            "reviewOnly": true,
            "approvalGranted": false,
            "governanceDecision": false,
            "sourceMutated": false
          },
          "warnings": ["Critic is not governance.", "Critic review is not approval."],
          "errors": []
        }
        """;
    }

    public static string MemoryImprovementEnvelope()
    {
        return $$"""
        {
          "ok": true,
          "command": "memory-improvements create",
          "status": "created",
          "data": {
            "agentRunId": "{{AgentRunId}}",
            "proposalOnly": true,
            "memoryPromoted": false,
            "collectiveMemoryCreated": false,
            "candidateIsMemory": false
          },
          "warnings": ["Memory proposal is not promotion.", "Memory safe is not approval.", "Candidate is not memory."],
          "errors": []
        }
        """;
    }

    public static string ToolRequestEnvelope()
    {
        return $$"""
        {
          "ok": true,
          "command": "tool-requests create",
          "status": "created",
          "data": {
            "toolRequestId": "{{ToolRequestId}}",
            "projectId": "{{ProjectId}}",
            "durable": false,
            "toolRequestIsExecutionPermission": false,
            "toolExecuted": false,
            "requestApproved": false,
            "auditIsApproval": false,
            "gateIsExecutor": false,
            "sourceApplied": false,
            "memoryPromoted": false
          },
          "warnings": ["Tool request is request form, not execution permission.", "Request approval is separate.", "Tool execution is separate.", "This tool request is a non-durable API-local inspection cache."],
          "errors": []
        }
        """;
    }

    public static string DogfoodLoopEnvelope()
    {
        return $$"""
        {
          "ok": true,
          "command": "dogfood-loops create",
          "status": "created",
          "data": {
            "dogfoodLoopId": "{{DogfoodLoopId}}",
            "projectId": "{{ProjectId}}",
            "durable": true,
            "releaseApproval": false,
            "autonomousWorkflow": false,
            "sourceApplied": false,
            "memoryPromoted": false
          },
          "warnings": ["Dogfood receipt is not release approval.", "Dogfood loop is not autonomous workflow.", "Human review remains required for source apply and memory promotion.", "Dogfood receipt is durable SQL-backed evidence, not release approval."],
          "errors": []
        }
        """;
    }

    public static string ToolGateEnvelope()
    {
        return """
        {
          "ok": true,
          "command": "tool-gate evaluate",
          "status": "blocked",
          "data": {
            "durable": true,
            "gateDecisionDurable": true,
            "gateIsExecutor": false,
            "toolExecuted": false,
            "sourceApplied": false,
            "memoryPromoted": false
          },
          "warnings": ["Gate decision evidence is durable SQL-backed.", "Gate evaluation is not execution.", "Tool execution is separate."],
          "errors": []
        }
        """;
    }

    public static string ApplyPreviewEnvelope()
    {
        return $$"""
        {
          "ok": true,
          "command": "workflow apply-preview",
          "status": "succeeded",
          "mutationOccurred": false,
          "previewStatus": "preview_available",
          "data": {
            "workflowRunId": "{{WorkflowRunId}}",
            "workflowStepId": "{{WorkflowStepId}}",
            "controlledApplyPlanReferenceId": "{{ControlledApplyPlanId}}",
            "status": "preview_available",
            "safeSummaryLines": ["Apply preview is review evidence only."],
            "dryRunSummaries": [],
            "missingEvidence": [],
            "gates": [],
            "risks": [],
            "issues": [],
            "isPreviewOnly": true,
            "canApplySource": false,
            "isDryRunExecution": false,
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
          },
          "warnings": ["Apply preview is evidence, not permission.", "Source apply remains unimplemented.", "Human review remains required before source apply."],
          "errors": []
        }
        """;
    }
}

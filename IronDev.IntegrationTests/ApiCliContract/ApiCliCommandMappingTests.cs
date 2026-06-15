using System.Net;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ApiCliContract;

[TestClass]
[TestCategory("ApiCliContract")]
public sealed class ApiCliCommandMappingTests
{
    [TestMethod]
    public async Task ApiCliContract_ApiPing_MapsToHealthEndpoint()
    {
        var handler = new RecordingHttpMessageHandler("""{"status":"healthy"}""");
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("api", "ping"), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/health", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_AgentRunsList_MapsToListEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.AgentRunsListEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("agent-runs", "list", "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/agent-runs?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_AgentRunsGet_MapsToDetailEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.AgentRunDetailEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("agent-runs", "get", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/agent-runs/agent-run-api-cli-contract?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_AgentRunsAudit_MapsToAuditEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.AgentRunAuditEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("agent-runs", "audit", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/agent-runs/agent-run-api-cli-contract/audit?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_ManualCriticCreate_MapsToCreateEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.ManualCriticEnvelope(), HttpStatusCode.Created);
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("critic", "review", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--target-agent-run-id", ApiCliContractTestSupport.AgentRunId, "--subject", "source-report", "--summary", "Review source report."), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/manual-critic/reviews", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_ManualCriticGet_MapsToGetEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.ManualCriticEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("critic", "review", "get", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/manual-critic/reviews/agent-run-api-cli-contract?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_MemoryImprovementCreate_MapsToCreateEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.MemoryImprovementEnvelope(), HttpStatusCode.Created);
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("memory-improvements", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--target-agent-run-id", ApiCliContractTestSupport.AgentRunId, "--summary", "Detect repeated memory pattern."), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/manual-memory-improvements", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_MemoryImprovementGet_MapsToGetEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.MemoryImprovementEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("memory-improvements", "get", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/manual-memory-improvements/agent-run-api-cli-contract?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_ToolRequestCreate_MapsToCreateEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.ToolRequestEnvelope(), HttpStatusCode.Created);
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("tool-requests", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--request-kind", "read_only", "--tool-kind", "workspace_diff", "--run-id", "run-api-cli-contract", "--reason", "Inspect diff."), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/tool-requests", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_ToolRequestGet_MapsToGetEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.ToolRequestEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("tool-requests", "get", ApiCliContractTestSupport.ToolRequestId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/tool-requests/tool-request-api-cli-contract?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_DogfoodLoopCreate_MapsToCreateEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.DogfoodLoopEnvelope(), HttpStatusCode.Created);
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("dogfood-loops", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--summary", "Dogfood loop receipt.", "--goal", "Exercise API CLI contract.", "--source", "manual"), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/dogfood-loops", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_DogfoodLoopGet_MapsToGetEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.DogfoodLoopEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("dogfood-loops", "get", ApiCliContractTestSupport.DogfoodLoopId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/v1/dogfood-loops/dogfood-loop-api-cli-contract?projectId=42", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public async Task ApiCliContract_ApplyPreview_MapsToPreviewEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.ApplyPreviewEnvelope());
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs(
            "workflow",
            "apply-preview",
            "--workflow-run",
            ApiCliContractTestSupport.WorkflowRunId,
            "--workflow-step",
            ApiCliContractTestSupport.WorkflowStepId,
            "--controlled-apply-plan",
            ApiCliContractTestSupport.ControlledApplyPlanId,
            "--take-dry-runs",
            "2"), output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.AreEqual("/api/workflow/apply-preview/workflow-run-api-cli-contract/workflow-step-api-cli-contract?takeDryRuns=2&includeDryRunSummaries=true&controlledApplyPlanReferenceId=apply-plan-api-cli-contract", handler.LastRequest?.RequestUri?.PathAndQuery);
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, HttpMessageHandler handler)
    {
        return await IronDevCli.RunAsync(args, output, error, handler, CancellationToken.None);
    }
}


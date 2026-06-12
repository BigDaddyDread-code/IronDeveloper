using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ApiCliContract;

[TestClass]
[TestCategory("ApiCliContract")]
public sealed class ApiCliBoundaryContractTests
{
    [DataTestMethod]
    [DataRow("agent-runs list", "AgentRunsListEnvelope")]
    [DataRow("agent-runs get", "AgentRunDetailEnvelope")]
    [DataRow("agent-runs audit", "AgentRunAuditEnvelope")]
    [DataRow("critic review create", "ManualCriticEnvelope")]
    [DataRow("memory-improvements create", "MemoryImprovementEnvelope")]
    [DataRow("tool-requests create", "ToolRequestEnvelope")]
    [DataRow("dogfood-loops create", "DogfoodLoopEnvelope")]
    public void ApiCliContract_JsonEnvelope_PreservesStandardFields(string expectedCommand, string envelopeName)
    {
        using var document = JsonDocument.Parse(GetEnvelope(envelopeName));
        var root = document.RootElement;

        Assert.IsTrue(root.TryGetProperty("ok", out _), expectedCommand);
        Assert.AreEqual(expectedCommand, root.GetProperty("command").GetString());
        Assert.IsTrue(root.TryGetProperty("status", out _), expectedCommand);
        Assert.IsTrue(root.TryGetProperty("data", out _), expectedCommand);
        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
        Assert.AreEqual(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
    }

    [TestMethod]
    public async Task ApiCliContract_TextOutput_PreservesBoundaryWarnings()
    {
        var cases = new[]
        {
            new TextCase(
                "agent-runs get",
                ApiCliContractTestSupport.AgentRunDetailEnvelope(),
                ApiCliContractTestSupport.CommonTextArgs("agent-runs", "get", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId),
                new[] { "Audit is evidence, not approval.", "CLI inspection is not execution permission." }),
            new TextCase(
                "agent-runs audit",
                ApiCliContractTestSupport.AgentRunAuditEnvelope(),
                ApiCliContractTestSupport.CommonTextArgs("agent-runs", "audit", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId),
                new[] { "Audit is not approval.", "Evidence is not permission." }),
            new TextCase(
                "critic review create",
                ApiCliContractTestSupport.ManualCriticEnvelope(),
                ApiCliContractTestSupport.CommonTextArgs("critic", "review", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--target-agent-run-id", ApiCliContractTestSupport.AgentRunId, "--subject", "source-report", "--summary", "Review source report."),
                new[] { "Critic is not governance.", "Critic review is not approval." }),
            new TextCase(
                "memory-improvements create",
                ApiCliContractTestSupport.MemoryImprovementEnvelope(),
                ApiCliContractTestSupport.CommonTextArgs("memory-improvements", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--target-agent-run-id", ApiCliContractTestSupport.AgentRunId, "--summary", "Detect repeated pattern."),
                new[] { "Memory proposal is not promotion.", "Memory safe is not approval.", "Candidate is not memory." }),
            new TextCase(
                "tool-requests create",
                ApiCliContractTestSupport.ToolRequestEnvelope(),
                ApiCliContractTestSupport.CommonTextArgs("tool-requests", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--request-kind", "read_only", "--tool-kind", "workspace_diff", "--run-id", "run-api-cli-contract", "--reason", "Inspect diff."),
                new[] { "Tool request is request form, not execution permission.", "Request approval is separate.", "Tool execution is separate." }),
            new TextCase(
                "dogfood-loops create",
                ApiCliContractTestSupport.DogfoodLoopEnvelope(),
                ApiCliContractTestSupport.CommonTextArgs("dogfood-loops", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--summary", "Dogfood loop receipt.", "--goal", "Exercise API CLI contract.", "--source", "manual"),
                new[] { "Dogfood receipt is not release approval.", "Dogfood loop is not autonomous workflow.", "Human review remains required for source apply and memory promotion." })
        };

        foreach (var testCase in cases)
        {
            var handler = new RecordingHttpMessageHandler(testCase.ResponseBody);
            using var output = ApiCliContractTestSupport.NewWriter();
            using var error = ApiCliContractTestSupport.NewWriter();

            var exitCode = await RunAsync(testCase.Args, output, error, handler);
            var text = output.ToString() + error.ToString();

            Assert.AreEqual(0, exitCode, testCase.Name + ": " + text);
            foreach (var expected in testCase.ExpectedWarnings)
            {
                StringAssert.Contains(text, expected, testCase.Name);
            }
        }
    }

    [TestMethod]
    public void ApiCliContract_NonDurableBoundaries_AreVisibleForTemporaryApiCaches()
    {
        AssertNonDurable(ApiCliContractTestSupport.ToolRequestEnvelope(), "tool request");
        AssertNonDurable(ApiCliContractTestSupport.DogfoodLoopEnvelope(), "dogfood loop");
        AssertNonDurable(ApiCliContractTestSupport.ToolGateEnvelope(), "tool gate");
    }

    [TestMethod]
    public async Task ApiCliContract_AuthorityShapedFlags_AreRejectedBeforeHttp()
    {
        var cases = new[]
        {
            new AuthorityCase("critic approve", ApiCliContractTestSupport.CommonJsonArgs("critic", "review", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--target-agent-run-id", ApiCliContractTestSupport.AgentRunId, "--subject", "source-report", "--summary", "Review source report.", "--approve")),
            new AuthorityCase("memory promote", ApiCliContractTestSupport.CommonJsonArgs("memory-improvements", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--target-agent-run-id", ApiCliContractTestSupport.AgentRunId, "--summary", "Detect pattern.", "--promote-memory")),
            new AuthorityCase("tool execute", ApiCliContractTestSupport.CommonJsonArgs("tool-requests", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--request-kind", "read_only", "--tool-kind", "workspace_diff", "--run-id", "run-api-cli-contract", "--reason", "Inspect diff.", "--execute")),
            new AuthorityCase("dogfood release approved", ApiCliContractTestSupport.CommonJsonArgs("dogfood-loops", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--summary", "Dogfood loop receipt.", "--goal", "Exercise API CLI contract.", "--source", "manual", "--release-approved"))
        };

        foreach (var testCase in cases)
        {
            var handler = new RecordingHttpMessageHandler("""{"ok":true}""");
            using var output = ApiCliContractTestSupport.NewWriter();
            using var error = ApiCliContractTestSupport.NewWriter();

            var exitCode = await RunAsync(testCase.Args, output, error, handler);

            Assert.AreNotEqual(0, exitCode, testCase.Name);
            Assert.IsNull(handler.LastRequest, testCase.Name);
            StringAssert.Contains((output.ToString() + error.ToString()).ToLowerInvariant(), "unsupported");
        }
    }

    [TestMethod]
    public async Task ApiCliContract_Tokens_AreNotEchoedInSuccessfulOutput()
    {
        var secret = "sk-live-api-cli-contract-secret";
        var handler = new RecordingHttpMessageHandler("""{"status":"healthy"}""");
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(new[] { "api", "ping", "--api-base-url", ApiCliContractTestSupport.BaseUrl, "--token", secret, "--output", "json" }, output, error, handler);

        Assert.AreEqual(0, exitCode, error.ToString());
        Assert.IsFalse(output.ToString().Contains(secret, StringComparison.Ordinal), output.ToString());
        Assert.IsFalse(error.ToString().Contains(secret, StringComparison.Ordinal), error.ToString());
    }

    [TestMethod]
    public async Task ApiCliContract_HiddenReasoningMarkers_AreRedactedFromAuditOutput()
    {
        const string privateText = "chain-of-thought: hidden scratchpad";
        var handler = new RecordingHttpMessageHandler(ApiCliContractTestSupport.AgentRunAuditEnvelope(privateText));
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonTextArgs("agent-runs", "audit", ApiCliContractTestSupport.AgentRunId, "--project-id", ApiCliContractTestSupport.ProjectId), output, error, handler);

        var text = output.ToString() + error.ToString();
        Assert.AreEqual(0, exitCode, text);
        Assert.IsFalse(text.Contains(privateText, StringComparison.Ordinal), text);
        StringAssert.Contains(text, "Audit is not approval.");
    }

    [TestMethod]
    public async Task ApiCliContract_HiddenReasoningMarkers_AreRejectedBeforeRequestWhenValidationSupportsIt()
    {
        var handler = new RecordingHttpMessageHandler("""{"ok":true}""");
        using var output = ApiCliContractTestSupport.NewWriter();
        using var error = ApiCliContractTestSupport.NewWriter();

        var exitCode = await RunAsync(ApiCliContractTestSupport.CommonJsonArgs("tool-requests", "create", "--project-id", ApiCliContractTestSupport.ProjectId, "--request-kind", "read_only", "--tool-kind", "workspace_diff", "--run-id", "run-api-cli-contract", "--reason", "include private reasoning"), output, error, handler);

        Assert.AreNotEqual(0, exitCode);
        Assert.IsNull(handler.LastRequest);
        Assert.IsFalse(output.ToString().Contains("private reasoning", StringComparison.Ordinal), output.ToString());
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, HttpMessageHandler handler)
    {
        return await IronDevCli.RunAsync(args, output, error, handler, CancellationToken.None);
    }

    private static string GetEnvelope(string envelopeName)
    {
        return envelopeName switch
        {
            "AgentRunsListEnvelope" => ApiCliContractTestSupport.AgentRunsListEnvelope(),
            "AgentRunDetailEnvelope" => ApiCliContractTestSupport.AgentRunDetailEnvelope(),
            "AgentRunAuditEnvelope" => ApiCliContractTestSupport.AgentRunAuditEnvelope(),
            "ManualCriticEnvelope" => ApiCliContractTestSupport.ManualCriticEnvelope(),
            "MemoryImprovementEnvelope" => ApiCliContractTestSupport.MemoryImprovementEnvelope(),
            "ToolRequestEnvelope" => ApiCliContractTestSupport.ToolRequestEnvelope(),
            "DogfoodLoopEnvelope" => ApiCliContractTestSupport.DogfoodLoopEnvelope(),
            _ => throw new ArgumentOutOfRangeException(nameof(envelopeName), envelopeName, null)
        };
    }

    private static void AssertNonDurable(string envelope, string name)
    {
        using var document = JsonDocument.Parse(envelope);
        var data = document.RootElement.GetProperty("data");
        Assert.IsTrue(data.TryGetProperty("durable", out var durable) || data.TryGetProperty("gateDecisionDurable", out durable), name);
        Assert.IsFalse(durable.GetBoolean(), name);

        var serialized = envelope.ToLowerInvariant();
        Assert.IsTrue(serialized.Contains("non-durable") || serialized.Contains("separate"), name);
        Assert.IsFalse(serialized.Contains("\"sourceapplied\": true"), name);
        Assert.IsFalse(serialized.Contains("\"memorypromoted\": true"), name);
    }

    private sealed record TextCase(string Name, string ResponseBody, string[] Args, IReadOnlyList<string> ExpectedWarnings);

    private sealed record AuthorityCase(string Name, string[] Args);
}


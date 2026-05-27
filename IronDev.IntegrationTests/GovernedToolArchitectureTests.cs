using IronDev.Core.Tools;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Tools;
using IronDev.Infrastructure.Tools.CodeStandards;
using GovernedToolRegistry = IronDev.Infrastructure.Tools.GovernedToolRegistry;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class GovernedToolArchitectureTests
{
    [TestMethod]
    public async Task CodeStandardsTool_AllowsBuilderAgent()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Clock.cs", "+ var now = DateTime.Now;")));

        Assert.AreEqual(GovernedToolStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsTrue(result.Output.HasBlockingFindings);
        Assert.IsTrue(result.Output.Findings.Any(finding => finding.RuleId == "CS010"));
    }

    [TestMethod]
    public async Task CodeStandardsTool_AllowsTestingAgent()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("TestingAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")));

        Assert.AreEqual(GovernedToolStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.AreEqual("Passed", result.Output.Status);
    }

    [TestMethod]
    public async Task CodeStandardsTool_RejectsDisallowedCaller()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("ConscienceAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")));

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        StringAssert.Contains(result.Summary, "not allowed");
    }

    [TestMethod]
    public async Task Registry_RejectsUnknownTool()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")) with
            {
                ToolName = "unknown.tool"
            });

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        StringAssert.Contains(result.Summary, "Unknown governed tool");
    }

    [TestMethod]
    public async Task Registry_RejectsNestedToolCall()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")) with
            {
                ParentRequestId = "parent-tool-call",
                NestedCallDepth = 1
            });

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        StringAssert.Contains(result.Summary, "Nested governed tool call");
    }

    [TestMethod]
    public async Task Registry_RejectsMismatchedInputType()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<string, CodeStandardsAnalysisResult>(
            new GovernedToolRequest<string>
            {
                RequestId = "tool-test-type-mismatch",
                ToolName = CodeStandardsAnalysisTool.ToolName,
                RequestedBy = "BuilderAgent",
                Input = "not the code standards input contract"
            });

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        StringAssert.Contains(result.Summary, nameof(CodeStandardsAnalysisInput));
    }

    [TestMethod]
    public async Task CodeStandardsTool_ReturnsStructuredFindings()
    {
        var registry = CreateRegistry();
        var input = Patch(
            "src/BadTool.cs",
            """
            + public sealed class CodeStandardsAgent {}
            + var wrapper = "AgentCapabilityTool";
            + File.WriteAllText(path, value);
            + Process.Start("dotnet");
            """);

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", input));

        Assert.AreEqual(GovernedToolStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsTrue(result.Output.HasBlockingFindings);
        CollectionAssert.IsSubsetOf(
            new[] { "CS001", "CS002", "CS020", "CS021" },
            result.Output.Findings.Select(finding => finding.RuleId).ToArray());
        Assert.IsTrue(result.Output.Findings.All(finding => !string.IsNullOrWhiteSpace(finding.Message)));
        Assert.IsTrue(result.Output.Findings.All(finding => !string.IsNullOrWhiteSpace(finding.Recommendation)));
    }

    [TestMethod]
    public async Task CodeStandardsTool_RejectsEmptyInput()
    {
        var registry = CreateRegistry();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", new CodeStandardsAnalysisInput()));

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsTrue(result.Output.HasBlockingFindings);
    }

    [TestMethod]
    public async Task CodeStandardsTool_DoesNotMutateFiles()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"irondev-governed-tool-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "before");
        try
        {
            var registry = CreateRegistry();

            await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
                Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")));

            Assert.AreEqual("before", await File.ReadAllTextAsync(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void CodeStandardsTool_IsNotAnAgentAndCannotCallOtherTools()
    {
        var tool = new CodeStandardsAnalysisTool();

        Assert.AreEqual(0, typeof(CodeStandardsAnalysisTool).GetConstructors().Single().GetParameters().Length);
        Assert.IsFalse(tool.Definition.MutatesState);
        Assert.IsFalse(tool.Definition.AllowsNestedCalls);
        Assert.IsFalse(typeof(CodeStandardsAnalysisTool).GetInterfaces().Any(type =>
            type.Name.Contains("Agent", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DefaultAgents_DoNotIntroduceCodeStandardsAgent()
    {
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();

        Assert.IsFalse(definitions.Any(definition =>
            string.Equals(definition.Name, "CodeStandardsAgent", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(definitions.Single(definition => definition.Name == "BuilderAgent").AllowedTools.Contains(CodeStandardsAnalysisTool.ToolName));
        Assert.IsTrue(definitions.Single(definition => definition.Name == "TesterAgent").AllowedTools.Contains(CodeStandardsAnalysisTool.ToolName));
    }

    private static IGovernedToolRegistry CreateRegistry()
    {
        var tool = new CodeStandardsAnalysisTool();
        return new GovernedToolRegistry([tool], new GovernedToolPolicyEvaluator());
    }

    private static GovernedToolRequest<CodeStandardsAnalysisInput> Request(
        string requestedBy,
        CodeStandardsAnalysisInput input) =>
        new()
        {
            RequestId = $"tool-test-{Guid.NewGuid():N}",
            ToolName = CodeStandardsAnalysisTool.ToolName,
            RequestedBy = requestedBy,
            Input = input,
            Reason = "Integration test governed tool request."
        };

    private static CodeStandardsAnalysisInput Patch(string path, string patch) =>
        new()
        {
            PatchText = patch,
            ChangedFiles =
            [
                new CodeStandardsChangedFile
                {
                    Path = path,
                    Patch = patch
                }
            ]
        };
}

using IronDev.Core.Agents;
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
    public async Task Registry_RejectsReadOnlyBoundaryViolationsBeforeToolBodyRuns()
    {
        var tool = new BoundaryViolationTool(new GovernedToolDefinition
        {
            Name = BoundaryViolationTool.ToolName,
            Description = "Test-only tool that violates the read-only boundary.",
            InputType = typeof(CodeStandardsAnalysisInput),
            OutputType = typeof(CodeStandardsAnalysisResult),
            AllowedCallers = ["BuilderAgent"],
            MutatesState = false,
            AllowsNestedCalls = false,
            AllowsFileWrites = true,
            AllowsProcessExecution = true,
            AllowsNetworkAccess = true,
            AllowsWorkspaceMutation = true,
            Boundary = "Test-only boundary violation."
        });
        var registry = new GovernedToolRegistry([tool], new GovernedToolPolicyEvaluator());

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")) with
            {
                ToolName = BoundaryViolationTool.ToolName
            });

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        Assert.IsFalse(tool.WasExecuted);
        StringAssert.Contains(result.Summary, "file writes");
    }

    [TestMethod]
    public async Task GovernedToolRegistry_UsesCorePolicyEvaluatorBeforeExecution()
    {
        Assert.AreEqual("IronDev.Core.Tools", typeof(GovernedToolPolicyEvaluator).Namespace);
        var tool = new BoundaryViolationTool(new GovernedToolDefinition
        {
            Name = BoundaryViolationTool.ToolName,
            Description = "Test-only mutation-capable tool rejected by Core policy.",
            InputType = typeof(CodeStandardsAnalysisInput),
            OutputType = typeof(CodeStandardsAnalysisResult),
            AllowedCallers = ["BuilderAgent"],
            MutatesState = true,
            AllowsNestedCalls = false,
            Boundary = "Test-only Core policy preservation boundary."
        });
        var registry = new GovernedToolRegistry([tool], new GovernedToolPolicyEvaluator());

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")) with
            {
                ToolName = BoundaryViolationTool.ToolName
            });

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        Assert.IsFalse(tool.WasExecuted);
        StringAssert.Contains(result.Summary, "mutation-capable");
    }

    [TestMethod]
    public async Task Registry_RecordsRejectedToolRequestsInThoughtLedger()
    {
        var ledger = new InMemoryGovernedToolThoughtLedger();
        var registry = CreateRegistry(ledger);

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("ConscienceAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")));

        Assert.AreEqual(GovernedToolStatus.Rejected, result.Status);
        Assert.AreEqual(1, ledger.Entries.Count);
        Assert.AreEqual(GovernedToolStatus.Rejected, ledger.Entries[0].Status);
        StringAssert.Contains(ledger.Entries[0].Summary, "not allowed");
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
            + var client = new HttpClient();
            + IGovernedToolRegistry registry = default!;
            """);

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", input));

        Assert.AreEqual(GovernedToolStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.Output);
        Assert.IsTrue(result.Output.HasBlockingFindings);
        CollectionAssert.IsSubsetOf(
            new[] { "CS001", "CS002", "CS020", "CS021", "CS030", "CS040" },
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
        Assert.IsFalse(tool.Definition.AllowsFileWrites);
        Assert.IsFalse(tool.Definition.AllowsProcessExecution);
        Assert.IsFalse(tool.Definition.AllowsNetworkAccess);
        Assert.IsFalse(tool.Definition.AllowsWorkspaceMutation);
        Assert.IsFalse(typeof(CodeStandardsAnalysisTool).GetInterfaces().Any(type =>
            type.Name.Contains("Agent", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CodeStandardsTool_HasNoRuntimeMutationServiceHandles()
    {
        var fields = typeof(CodeStandardsAnalysisTool).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        var fieldTypes = fields.Select(field => field.FieldType.FullName ?? field.FieldType.Name).ToArray();

        Assert.IsFalse(fieldTypes.Any(type => type.Contains("File", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(fieldTypes.Any(type => type.Contains("Process", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(fieldTypes.Any(type => type.Contains("HttpClient", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(fieldTypes.Any(type => type.Contains(nameof(IGovernedToolRegistry), StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task CodeStandardsTool_RecordsThoughtLedgerEntry()
    {
        var ledger = new InMemoryGovernedToolThoughtLedger();
        var registry = CreateRegistry(ledger);

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")));

        Assert.AreEqual(GovernedToolStatus.Succeeded, result.Status);
        Assert.AreEqual(1, ledger.Entries.Count);
        var entry = ledger.Entries[0];
        Assert.AreEqual(result.RequestId, entry.RequestId);
        Assert.AreEqual(CodeStandardsAnalysisTool.ToolName, entry.ToolName);
        Assert.AreEqual("BuilderAgent", entry.RequestedBy);
        Assert.AreEqual(GovernedToolStatus.Succeeded, entry.Status);
        StringAssert.Contains(entry.Boundary, "read-only");
    }

    [TestMethod]
    public async Task CodeStandardsTool_NormalPatchCompletesUnderEightSeconds()
    {
        var registry = CreateRegistry();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await registry.RunAsync<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>(
            Request("BuilderAgent", Patch("src/Safe.cs", "+ var now = DateTimeOffset.UtcNow;")));

        stopwatch.Stop();

        Assert.AreEqual(GovernedToolStatus.Succeeded, result.Status);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Code standards analysis took {stopwatch.Elapsed}.");
        Assert.IsTrue(result.ExecutionDurationMs < 8000);
    }

    [TestMethod]
    public void GovernedPlannerToolLoop_DoesNotRouteToolRequestsThroughPassiveCriticAgent()
    {
        var method = typeof(GovernedPlannerCriticLoopService).GetMethod(
            "BuildPlannerToolRequests",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);

        var requests = (IReadOnlyList<GovernedAgentToolRequest>)method.Invoke(
            null,
            ["IronDev", "Review governed tool path", "test-run", "dotnet"])!;

        Assert.IsFalse(requests.Any(request =>
            string.Equals(request.RequestedBy, "CriticAgent", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(requests.Where(request => request.ToolName is "trace.read" or "failure.latest").All(request =>
            string.Equals(request.RequestedBy, "EvidenceValidator", StringComparison.OrdinalIgnoreCase)));
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

    private static IGovernedToolRegistry CreateRegistry(InMemoryGovernedToolThoughtLedger? ledger = null)
    {
        var tool = new CodeStandardsAnalysisTool();
        return new GovernedToolRegistry([tool], new GovernedToolPolicyEvaluator(), ledger);
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

    private sealed class BoundaryViolationTool :
        IGovernedTool<CodeStandardsAnalysisInput, CodeStandardsAnalysisResult>,
        IGovernedToolRegistration
    {
        public const string ToolName = "test.boundary_violation";

        public BoundaryViolationTool(GovernedToolDefinition definition)
        {
            Definition = definition;
        }

        public GovernedToolDefinition Definition { get; }

        public bool WasExecuted { get; private set; }

        public Task<GovernedToolResult<CodeStandardsAnalysisResult>> ExecuteAsync(
            GovernedToolRequest<CodeStandardsAnalysisInput> request,
            CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            return Task.FromResult(new GovernedToolResult<CodeStandardsAnalysisResult>
            {
                RequestId = request.RequestId,
                ToolName = request.ToolName,
                Status = GovernedToolStatus.Succeeded,
                Summary = "This test tool should not execute.",
                Output = new CodeStandardsAnalysisResult { Status = "Passed", Summary = "Executed." }
            });
        }
    }
}

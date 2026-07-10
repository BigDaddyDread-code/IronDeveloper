using IronDev.Core.Tools;
using System.Xml.Linq;

namespace IronDev.UnitTests.Tools;

[TestClass]
public sealed class GovernedToolPolicyEvaluatorExtractionTests
{
    private static readonly DateTimeOffset FixedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CoreEvaluatorAllowsReadOnlyToolForAllowedCaller()
    {
        var decision = Evaluate();

        Assert.IsTrue(decision.IsAllowed);
        Assert.AreEqual("Governed tool policy allowed this read-only call.", decision.Reason);
    }

    [TestMethod]
    public void CoreEvaluatorRejectsMutationCapableTool()
    {
        var decision = Evaluate(definition: Definition(mutatesState: true));

        AssertRejected(
            decision,
            "Tool 'read.context' is mutation-capable and cannot run in the governed read-only tool path.");
    }

    [TestMethod]
    public void CoreEvaluatorRejectsFileWriteCapableTool()
    {
        var decision = Evaluate(definition: Definition(allowsFileWrites: true));

        AssertRejected(
            decision,
            "Tool 'read.context' allows file writes and cannot run in the governed read-only tool path.");
    }

    [TestMethod]
    public void CoreEvaluatorRejectsProcessExecutionCapableTool()
    {
        var decision = Evaluate(definition: Definition(allowsProcessExecution: true));

        AssertRejected(
            decision,
            "Tool 'read.context' allows process execution and cannot run in the governed read-only tool path.");
    }

    [TestMethod]
    public void CoreEvaluatorRejectsNetworkCapableTool()
    {
        var decision = Evaluate(definition: Definition(allowsNetworkAccess: true));

        AssertRejected(
            decision,
            "Tool 'read.context' allows network access and cannot run in the governed read-only tool path.");
    }

    [TestMethod]
    public void CoreEvaluatorRejectsWorkspaceMutationCapableTool()
    {
        var decision = Evaluate(definition: Definition(allowsWorkspaceMutation: true));

        AssertRejected(
            decision,
            "Tool 'read.context' allows workspace mutation and cannot run in the governed read-only tool path.");
    }

    [TestMethod]
    public void CoreEvaluatorRejectsDisallowedCaller()
    {
        var decision = Evaluate(request: Request(requestedBy: "critic"));

        AssertRejected(
            decision,
            "Caller 'critic' is not allowed to run governed tool 'read.context'.");
    }

    [TestMethod]
    public void CoreEvaluatorRejectsNestedCallWhenDisabled()
    {
        var decision = Evaluate(request: Request(parentRequestId: "parent-request", nestedCallDepth: 1));

        AssertRejected(
            decision,
            "Nested governed tool call 'read.context' was rejected.");
    }

    [TestMethod]
    public void CoreEvaluatorPreservesObservableRejectionOrder()
    {
        var decision = Evaluate(
            definition: Definition(
                name: "registered.tool",
                mutatesState: true,
                allowsFileWrites: true,
                allowsProcessExecution: true,
                allowsNetworkAccess: true,
                allowsWorkspaceMutation: true),
            request: Request(toolName: "requested.tool", requestedBy: "critic", parentRequestId: "parent", nestedCallDepth: 1));

        AssertRejected(
            decision,
            "Request tool 'requested.tool' does not match registered tool 'registered.tool'.");
    }

    [TestMethod]
    public void CoreEvaluatorTestsRemainCoreOnly()
    {
        var project = XDocument.Load(Path.Combine(ProjectRoot(), "IronDev.UnitTests", "IronDev.UnitTests.csproj"));
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { @"..\IronDev.Core\IronDev.Core.csproj" },
            projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);
    }

    [TestMethod]
    public void CoreEvaluatorExtractionTestsDoNotUseRuntimeOrMutationSurfaces()
    {
        var source = File.ReadAllText(SourcePath());

        foreach (var forbidden in ForbiddenFragments())
        {
            Assert.IsFalse(
                source.Contains(forbidden, StringComparison.Ordinal),
                $"G09a unit tests must not contain runtime or mutation surface '{forbidden}'.");
        }
    }

    private static GovernedToolPolicyDecision Evaluate(
        GovernedToolDefinition? definition = null,
        GovernedToolRequest<TestInput>? request = null) =>
        new GovernedToolPolicyEvaluator().Evaluate(
            definition ?? Definition(),
            request ?? Request());

    private static GovernedToolDefinition Definition(
        string name = "read.context",
        IReadOnlyList<string>? allowedCallers = null,
        bool mutatesState = false,
        bool allowsNestedCalls = false,
        bool allowsFileWrites = false,
        bool allowsProcessExecution = false,
        bool allowsNetworkAccess = false,
        bool allowsWorkspaceMutation = false) =>
        new()
        {
            Name = name,
            DisplayName = name,
            Category = "Test fixture",
            DefinitionVersion = "test-1",
            Description = "Read-only test policy fixture.",
            ConnectionRequirement = GovernedToolConnectionRequirement.None,
            InputType = typeof(TestInput),
            OutputType = typeof(TestOutput),
            AllowedCallers = allowedCallers ?? ["planner"],
            MutatesState = mutatesState,
            AllowsNestedCalls = allowsNestedCalls,
            AllowsFileWrites = allowsFileWrites,
            AllowsProcessExecution = allowsProcessExecution,
            AllowsNetworkAccess = allowsNetworkAccess,
            AllowsWorkspaceMutation = allowsWorkspaceMutation,
            Boundary = "Policy fixture only; no tool execution."
        };

    private static GovernedToolRequest<TestInput> Request(
        string toolName = "read.context",
        string requestedBy = "planner",
        string? parentRequestId = null,
        int nestedCallDepth = 0) =>
        new()
        {
            RequestId = "g09a-policy-request",
            ToolName = toolName,
            RequestedBy = requestedBy,
            Input = new TestInput("value"),
            Reason = "G09a Core evaluator extraction characterization.",
            ParentRequestId = parentRequestId,
            NestedCallDepth = nestedCallDepth,
            CreatedAtUtc = FixedCreatedAtUtc
        };

    private static void AssertRejected(GovernedToolPolicyDecision decision, string reason)
    {
        Assert.IsFalse(decision.IsAllowed);
        Assert.AreEqual(reason, decision.Reason);
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        return directory.FullName;
    }

    private static string SourcePath() =>
        Path.Combine(ProjectRoot(), "IronDev.UnitTests", "Tools", "GovernedToolPolicyEvaluatorExtractionTests.cs");

    private static IReadOnlyList<string> ForbiddenFragments() =>
    [
        string.Concat("IronDev.", "Infrastructure"),
        string.Concat("Governed", "Tool", "Registry"),
        string.Concat("IGoverned", "Tool<"),
        string.Concat("Execute", "Async"),
        string.Concat("Web", "Application", "Factory"),
        string.Concat("Test", "Server"),
        string.Concat("Http", "Client"),
        string.Concat("Db", "Context"),
        string.Concat("Sql", "Connection"),
        string.Concat("Pro", "vider"),
        string.Concat("Process.", "Start"),
        string.Concat("File.", "Write"),
        string.Concat("So", "cket"),
        string.Concat("Tcp", "Client"),
        string.Concat("Udp", "Client"),
        string.Concat("Directory.", "Delete"),
        string.Concat("Directory.", "Move"),
        string.Concat("DateTimeOffset.", "UtcNow")
    ];

    private sealed record TestInput(string Value);

    private sealed record TestOutput(string Value);
}

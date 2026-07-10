using IronDev.Core.Tools;
using System.Xml.Linq;

namespace IronDev.UnitTests.Tools;

internal static class GovernedToolPolicyEvaluatorTestFixtures
{
    internal const string ToolName = "read.context";
    internal const string RegisteredToolName = "registered.context";
    internal const string RequestedToolName = "requested.context";
    internal const string AllowedCaller = "planner";
    internal const string OtherAllowedCaller = "reviewer";
    internal const string DisallowedCaller = "critic";

    internal static readonly DateTimeOffset FixedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static GovernedToolPolicyDecision Evaluate(
        GovernedToolDefinition? definition = null,
        GovernedToolRequest<TestInput>? request = null) =>
        new GovernedToolPolicyEvaluator().Evaluate(
            definition ?? Definition(),
            request ?? Request());

    internal static GovernedToolDefinition Definition(
        string name = ToolName,
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
            Description = "Read-only governed policy fixture.",
            ConnectionRequirement = GovernedToolConnectionRequirement.None,
            InputType = typeof(TestInput),
            OutputType = typeof(TestOutput),
            AllowedCallers = allowedCallers ?? [AllowedCaller],
            MutatesState = mutatesState,
            AllowsNestedCalls = allowsNestedCalls,
            AllowsFileWrites = allowsFileWrites,
            AllowsProcessExecution = allowsProcessExecution,
            AllowsNetworkAccess = allowsNetworkAccess,
            AllowsWorkspaceMutation = allowsWorkspaceMutation,
            Boundary = "Policy fixture only; no tool execution."
        };

    internal static GovernedToolRequest<TestInput> Request(
        string toolName = ToolName,
        string requestedBy = AllowedCaller,
        string? parentRequestId = null,
        int nestedCallDepth = 0) =>
        new()
        {
            RequestId = "g09b-policy-request",
            ToolName = toolName,
            RequestedBy = requestedBy,
            Input = new TestInput("value"),
            Reason = "G09b Core evaluator unit test.",
            ParentRequestId = parentRequestId,
            NestedCallDepth = nestedCallDepth,
            CreatedAtUtc = FixedCreatedAtUtc
        };

    internal static string AllowedReason() =>
        "Governed tool policy allowed this read-only call.";

    internal static string ToolNameMismatchReason(
        string requestedTool = RequestedToolName,
        string registeredTool = RegisteredToolName) =>
        $"Request tool '{requestedTool}' does not match registered tool '{registeredTool}'.";

    internal static string MutationReason(string toolName = ToolName) =>
        $"Tool '{toolName}' is mutation-capable and cannot run in the governed read-only tool path.";

    internal static string FileWriteReason(string toolName = ToolName) =>
        $"Tool '{toolName}' allows file writes and cannot run in the governed read-only tool path.";

    internal static string ProcessReason(string toolName = ToolName) =>
        $"Tool '{toolName}' allows process execution and cannot run in the governed read-only tool path.";

    internal static string NetworkReason(string toolName = ToolName) =>
        $"Tool '{toolName}' allows network access and cannot run in the governed read-only tool path.";

    internal static string WorkspaceReason(string toolName = ToolName) =>
        $"Tool '{toolName}' allows workspace mutation and cannot run in the governed read-only tool path.";

    internal static string CallerReason(
        string requestedBy = DisallowedCaller,
        string toolName = ToolName) =>
        $"Caller '{requestedBy}' is not allowed to run governed tool '{toolName}'.";

    internal static string NestedReason(string toolName = ToolName) =>
        $"Nested governed tool call '{toolName}' was rejected.";

    internal static void AssertAllowed(GovernedToolPolicyDecision decision)
    {
        Assert.IsTrue(decision.IsAllowed);
        Assert.AreEqual(AllowedReason(), decision.Reason);
    }

    internal static void AssertRejected(GovernedToolPolicyDecision decision, string reason)
    {
        Assert.IsFalse(decision.IsAllowed);
        Assert.AreEqual(reason, decision.Reason);
    }

    internal static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        return directory.FullName;
    }

    internal static XDocument UnitTestProject() =>
        XDocument.Load(Path.Combine(ProjectRoot(), "IronDev.UnitTests", "IronDev.UnitTests.csproj"));

    internal static string SourceText(params string[] relativePaths) =>
        string.Join(
            Environment.NewLine,
            relativePaths.Select(path => File.ReadAllText(Path.Combine(ProjectRoot(), path))));

    internal static IReadOnlyList<string> RuntimeAndMutationForbiddenFragments() =>
    [
        string.Concat("IronDev.", "Infrastructure"),
        string.Concat("Governed", "Tool", "Registry"),
        string.Concat("IGoverned", "Tool<"),
        string.Concat("IGoverned", "Tool", "Registry"),
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
        string.Concat("Environment.", "GetEnvironmentVariable"),
        string.Concat("DateTimeOffset.", "UtcNow")
    ];

    internal sealed record TestInput(string Value);

    internal sealed record TestOutput(string Value);
}

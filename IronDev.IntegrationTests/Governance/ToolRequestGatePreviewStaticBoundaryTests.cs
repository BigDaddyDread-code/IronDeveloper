using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class ToolRequestGatePreviewStaticBoundaryTests
{
    [TestMethod]
    public void CandidateWorkflowInterface_ExposesOnlyPreview()
    {
        var methods = typeof(IToolRequestGatePreviewCandidateWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Preview" }, methods);
    }

    [DataTestMethod]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Invoke")]
    [DataRow("InvokeTool")]
    [DataRow("Dispatch")]
    [DataRow("Authorize")]
    [DataRow("Approve")]
    [DataRow("SatisfyPolicy")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("CreateTicket")]
    [DataRow("ApplyPatch")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    public void CandidateWorkflowTypes_DoNotExposeForbiddenMethodNames(string forbiddenName)
    {
        var methodNames = typeof(ToolRequestGatePreviewCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("ToolRequestGatePreview", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected method {forbiddenName}.");
    }

    [TestMethod]
    public void ProductionCandidateWorkflow_HasNoRuntimeIoToolOrAuthorityDependencies()
    {
        var text = File.ReadAllText(ProductionFilePath());

        AssertDoesNotContainAny(text,
            "ProcessStartInfo",
            "HttpClient",
            "SqlConnection",
            "DbConnection",
            "File.ReadAllText",
            "File.Write",
            "Directory.CreateDirectory",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "CommandLine",
            "ToolInvoker",
            "IToolInvoker",
            "ToolRegistry",
            "IToolRegistry",
            "AgentDispatcher",
            "IAgentDispatcher",
            "ModelClient",
            "IModelClient",
            "PromptBuilder",
            "IPromptBuilder",
            "A2aSender",
            "IA2aSender",
            "GitHubClient",
            "WorkflowTransitionWriter",
            "ApprovalMutation",
            "SourceMutationService",
            "PatchApply",
            "TicketWriter",
            "MemoryPromotionService",
            "RetrievalActivationService");
    }

    [DataTestMethod]
    [DataRow("RawPrompt")]
    [DataRow("RawCompletion")]
    [DataRow("RawToolOutput")]
    [DataRow("PrivateReasoning")]
    [DataRow("HiddenReasoning")]
    [DataRow("ChainOfThought")]
    [DataRow("WholePatch")]
    [DataRow("PatchPayload")]
    [DataRow("Command")]
    [DataRow("Arguments")]
    [DataRow("ToolInputPayload")]
    [DataRow("ToolOutputPayload")]
    public void CandidateWorkflowModels_DoNotExposeForbiddenPayloadProperties(string forbiddenProperty)
    {
        var propertyNames = typeof(ToolRequestGatePreviewCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("ToolRequest", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbiddenProperty, StringComparer.Ordinal), $"Unexpected property {forbiddenProperty}.");
    }

    [DataTestMethod]
    [DataRow("ToolExecuted")]
    [DataRow("ToolAuthorized")]
    [DataRow("ToolReserved")]
    [DataRow("CommandRun")]
    [DataRow("Approved")]
    [DataRow("PolicySatisfied")]
    public void CandidateStatus_DoesNotExposeExecutionOrSatisfactionStates(string forbiddenStatus)
    {
        var statusNames = Enum.GetNames<ToolRequestGatePreviewCandidateStatus>();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [TestMethod]
    public void Receipt_DoesNotOverclaimExecutionOrGateSatisfaction()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR130_TOOL_REQUEST_GATE_PREVIEW_WORKFLOW_RECEIPT.md"));

        Assert.IsTrue(text.Contains("Tool request preview is not tool execution.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Gate preview is not gate satisfaction.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Preview output cannot grant authority.", StringComparison.Ordinal));
        AssertDoesNotContainAny(text,
            "Tool is executed.",
            "Capability is authorized.",
            "Gate is satisfied.",
            "Approval is satisfied.",
            "Policy is satisfied.",
            "Command is run.",
            "Workflow may continue.");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string ProductionFilePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "ToolRequestGatePreviewCandidateWorkflowModels.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}

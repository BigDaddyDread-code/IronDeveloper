using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowDryRun")]
public sealed class WorkflowDryRunStaticBoundaryTests
{
    [TestMethod]
    public void WorkflowDryRun_RunnerSkeletonStillExposesEvaluateOnly()
    {
        var methods = typeof(IWorkflowRunnerSkeleton).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEqual(new[] { "Evaluate" }, methods);
    }

    [TestMethod]
    public void WorkflowDryRun_ExecutorInterfaceExposesExecuteDryRunOnly()
    {
        var methods = typeof(IWorkflowDryRunExecutor).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEqual(new[] { "ExecuteDryRun" }, methods);
    }

    [TestMethod]
    public void WorkflowDryRun_ExecutorHasNoGenericExecutionMethodSurface()
    {
        var methodNames = typeof(WorkflowDryRunExecutor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "ExecuteDryRun" }, methodNames);
        CollectionAssert.DoesNotContain(methodNames, "Execute");
        CollectionAssert.DoesNotContain(methodNames, "ExecuteAsync");
        CollectionAssert.DoesNotContain(methodNames, "Run");
        CollectionAssert.DoesNotContain(methodNames, "RunAsync");
        CollectionAssert.DoesNotContain(methodNames, "Dispatch");
        CollectionAssert.DoesNotContain(methodNames, "InvokeTool");
        CollectionAssert.DoesNotContain(methodNames, "Apply");
        CollectionAssert.DoesNotContain(methodNames, "Approve");
        CollectionAssert.DoesNotContain(methodNames, "Continue");
        CollectionAssert.DoesNotContain(methodNames, "Transition");
    }

    [TestMethod]
    public void WorkflowDryRun_ProductionFileAddsNoRuntimeStorageMutationOrExternalSurface()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IronDev.Core", "Workflow", "WorkflowDryRunExecutionModels.cs"));

        AssertDoesNotContainAny(
            text,
            "SqlConnection",
            "DbConnection",
            "INSERT ",
            "UPDATE ",
            "DELETE ",
            "HttpClient",
            "ProcessStartInfo",
            "File.Write",
            "File.Delete",
            "Directory.CreateDirectory",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "Weaviate",
            "Embedding",
            "VectorSearch",
            "OpenAI",
            "PromptBuilder",
            "ToolInvoker",
            "AgentDispatcher",
            "WorkflowTransitionRecorder",
            "WorkflowStateWriter",
            "ApprovalMutation",
            "PolicySatisfaction",
            "MemoryPromotion",
            "PatchApply");
    }

    [TestMethod]
    public void WorkflowDryRun_ReceiptDoesNotOverclaimRealExecution()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Docs", "receipts", "PR123_SAFE_DRY_RUN_WORKFLOW_EXECUTION_RECEIPT.md"));

        AssertDoesNotContainAny(
            text,
            "Workflow execution is now supported",
            "Steps can now execute",
            "The runner executes workflow steps",
            "Dry-run completed the workflow step",
            "Dry-run result is evidence of approval",
            "Dry-run can call tools safely");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

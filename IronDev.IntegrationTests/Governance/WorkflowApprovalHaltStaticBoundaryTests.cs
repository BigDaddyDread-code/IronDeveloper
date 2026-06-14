using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowApprovalHalt")]
public sealed class WorkflowApprovalHaltStaticBoundaryTests
{
    [TestMethod]
    public void WorkflowApprovalHalt_EvaluatorSurfaceIsEvaluateOnly()
    {
        var methods = typeof(IWorkflowApprovalHaltEvaluator).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEqual(new[] { "Evaluate" }, methods);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_ModelsExposeNoApprovalMutationOrExecutionMethodSurface()
    {
        var methodNames = typeof(WorkflowApprovalHaltEvaluator)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(WorkflowApprovalHaltEvaluator))
            .Select(method => method.Name)
            .ToArray();

        AssertDoesNotContainAny(string.Join("|", methodNames), "Approve", "Grant", "Deny", "Execute", "Run", "Continue", "Transition", "Dispatch", "Invoke", "Apply", "Promote");
    }

    [TestMethod]
    public void WorkflowApprovalHalt_ProductionFileDoesNotAddRuntimeStorageOrExternalSurface()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "IronDev.Core", "Workflow", "WorkflowApprovalHaltModels.cs"));

        AssertDoesNotContainAny(
            text,
            "SqlConnection",
            "DbConnection",
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
            "OpenAI",
            "ExecuteAsync",
            "StartAsync");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

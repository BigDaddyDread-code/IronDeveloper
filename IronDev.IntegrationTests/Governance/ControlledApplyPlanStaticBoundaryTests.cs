using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class ControlledApplyPlanStaticBoundaryTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ProductionPath = Path.Combine(RepoRoot, "IronDev.Core", "Workflow", "ControlledApplyPlanModels.cs");
    private static readonly string ReceiptPath = Path.Combine(RepoRoot, "Docs", "receipts", "PR139_CONTROLLED_APPLY_PLAN_MODEL.md");

    [TestMethod]
    public void WorkflowInterface_ExposesOnlyPrepare()
    {
        var names = typeof(IControlledApplyPlanWorkflow)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Prepare" }, names);
    }

    [TestMethod]
    public void ProductionFile_DoesNotContainRuntimeOrAuthoritySeams()
    {
        var source = File.ReadAllText(ProductionPath);

        var forbidden = new[]
        {
            "StartWorkflow",
            "ContinueWorkflow",
            "DispatchWorkflow",
            "RunWorkflow",
            "WorkflowExecutor",
            "PatchApply",
            "SourceMutation",
            "File.Write",
            "File.Read",
            "Directory.",
            "ProcessStartInfo",
            "HttpClient",
            "DbConnection",
            "SqlConnection",
            "PolicySatisfaction",
            "ApprovalDecisionStore",
            "RollbackExecutor",
            "ValidationRunner",
            "TestRunner",
            "MemoryPromotion",
            "RetrievalActivation",
            "GitHub",
            "Weaviate",
            "HostedService",
            "ControllerBase",
            "CommandHandler"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.Ordinal), $"Forbidden marker found: {marker}");
        }
    }

    [TestMethod]
    public void WorkflowImplementation_DoesNotExposeForbiddenMethods()
    {
        var names = typeof(ControlledApplyPlanWorkflow)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Prepare" }, names);
        Assert.IsFalse(names.Any(name => name.Contains("Start", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(name => name.Contains("Run", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(name => name.Contains("Dispatch", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(name => name.Contains("Apply", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(names.Any(name => name.Contains("Promote", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void StatusEnum_DoesNotExposeExecutionStates()
    {
        var names = Enum.GetNames<ControlledApplyPlanStatus>();

        Assert.IsFalse(names.Contains("Approved"));
        Assert.IsFalse(names.Contains("Executing"));
        Assert.IsFalse(names.Contains("Executed"));
        Assert.IsFalse(names.Contains("Applied"));
        Assert.IsFalse(names.Contains("Committed"));
        Assert.IsFalse(names.Contains("Promoted"));
        Assert.IsFalse(names.Contains("RolledBack"));
        Assert.IsFalse(names.Contains("Validated"));
    }

    [TestMethod]
    public void ResultFlags_DefaultToNoAuthority()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        ControlledApplyPlanTests.AssertNoAuthority(result);
    }

    [TestMethod]
    public void Receipt_ContainsRequiredBoundaryLanguage()
    {
        var receipt = File.ReadAllText(ReceiptPath);

        AssertContains(receipt, "PR139 adds a Controlled Apply Plan model.");
        AssertContains(receipt, "Controlled apply plan is not controlled apply execution.");
        AssertContains(receipt, "Plan step is not execution step.");
        AssertContains(receipt, "Apply placeholder is not executable.");
        AssertContains(receipt, "Validation reference is not validation execution.");
        AssertContains(receipt, "Rollback note is not rollback execution.");
        AssertContains(receipt, "Source apply approval requirement is not approval satisfaction.");
        AssertContains(receipt, "Patch proposal evidence package is not a patch.");
        AssertContains(receipt, "This PR does not apply source, apply patches, mutate files, read source files, run commands, invoke tools, dispatch agents, call models, build prompts, run validation, run rollback, satisfy approval, satisfy policy, transition workflow, create tickets, promote memory, activate retrieval, write SQL, or add runtime wiring.");
        AssertContains(receipt, "Controlled apply execution remains unimplemented.");
        AssertContains(receipt, "Source apply remains unimplemented.");
        AssertContains(receipt, "Patch apply remains unimplemented.");
        AssertContains(receipt, "PR139 draws the apply route. It does not drive it.");
    }

    private static void AssertContains(string source, string expected)
    {
        Assert.IsTrue(source.Contains(expected, StringComparison.Ordinal), $"Expected text not found: {expected}");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

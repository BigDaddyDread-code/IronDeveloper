using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("HumanApprovedApply")]
public sealed class HumanApprovedApplyStaticBoundaryTests
{
    [TestMethod]
    public void HumanApprovedApply_ProductionApplyFilesDoNotExposeApplyExecutionMethods()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Workflow", "SourceApplyApprovalRequirementContractModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "PatchProposalEvidencePackageModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "ControlledApplyPlanModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "ApplyDryRunStoreModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "ApplyPreviewModels.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "ApplyPreviewService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ApplyPreviewController.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliApplyPreview.cs")
        };

        var text = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var token in new[]
        {
            "ApplySourceAsync",
            "ApplyPatchAsync",
            "ExecuteDryRunAsync",
            "RunDryRunAsync",
            "SatisfyApprovalAsync",
            "SatisfyPolicyAsync",
            "ContinueWorkflowAsync",
            "PromoteMemoryAsync",
            "DispatchAgentAsync",
            "InvokeToolAsync",
            "RunCommandAsync",
            "MutateFilesAsync",
            "ReadSourceFilesAsync",
            "File.WriteAllText",
            "File.WriteAllBytes",
            "File.Delete",
            "File.Copy",
            "ProcessStartInfo",
            "IControlledWorktreeApplyService"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden production apply execution token found: {token}");
        }
    }

    [TestMethod]
    public void HumanApprovedApply_ApplyPreviewServiceSurfaceIsReadOnly()
    {
        var methods = typeof(IApplyPreviewService).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "GetPreviewAsync" }, methods);
    }

    [TestMethod]
    public void HumanApprovedApply_DryRunStoreSurfaceIsStorageOnlyNotExecution()
    {
        var methods = typeof(IApplyDryRunStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetByIdAsync", "ListByControlledApplyPlanAsync", "ListByWorkflowRunAsync" },
            methods);
        foreach (var method in methods)
        {
            Assert.IsFalse(method.Contains("Execute", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Apply", StringComparison.OrdinalIgnoreCase) && method != "ListByControlledApplyPlanAsync", method);
            Assert.IsFalse(method.Contains("Approve", StringComparison.OrdinalIgnoreCase), method);
            Assert.IsFalse(method.Contains("Satisfy", StringComparison.OrdinalIgnoreCase), method);
        }
    }

    [TestMethod]
    public void HumanApprovedApply_ReceiptDocumentsNoAuthorityBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR143_HUMAN_APPROVED_APPLY_BOUNDARY_TESTS.md"));

        StringAssert.Contains(text, "Approval package is not approval.");
        StringAssert.Contains(text, "Apply preview is not apply permission.");
        StringAssert.Contains(text, "Apply dry-run receipt is not dry-run execution.");
        StringAssert.Contains(text, "Patch proposal evidence package is not a patch.");
        StringAssert.Contains(text, "Human review remains required before any future source apply.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

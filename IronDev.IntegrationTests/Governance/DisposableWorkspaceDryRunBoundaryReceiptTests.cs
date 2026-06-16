using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("DisposableWorkspaceDryRunBoundaryReceipt")]
public sealed class DisposableWorkspaceDryRunBoundaryReceiptTests
{
    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_ReceiptExists() =>
        Assert.IsTrue(File.Exists(ReceiptPath()), "PR181 receipt must exist.");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesReceiptTestOnly()
    {
        AssertReceiptContains(
            "This PR is receipt/test only.",
            "This PR adds no production code.",
            "This PR adds no SQL.",
            "This PR adds no API.",
            "This PR adds no CLI.",
            "This PR adds no UI.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesNoExecutionOrMutation()
    {
        AssertReceiptContains(
            "This PR does not create disposable workspaces.",
            "This PR does not execute dry-runs.",
            "This PR does not create dry-run results.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesFutureDryRunsMustUseDisposableWorkspace() =>
        AssertReceiptContains(
            "Future controlled dry-runs must run only inside disposable/caged workspaces.",
            "The source workspace is not a dry-run workspace.");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesWorkspacePreparationIsNotExecution()
    {
        AssertReceiptContains(
            "A dry-run request is not workspace creation.",
            "A disposable workspace boundary is not dry-run execution.",
            "Disposable workspace preparation is not patch artifact creation.",
            "Disposable workspace preparation is not source apply.",
            "Disposable workspace preparation is not rollback.",
            "Disposable workspace preparation is not workflow continuation.",
            "Disposable workspace preparation is not release readiness.",
            "Disposable workspace preparation does not authorize execution by itself.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesWorkspaceIsolationRules()
    {
        AssertReceiptContains(
            "The disposable workspace must be isolated from the source workspace.",
            "The disposable workspace must be reproducible from explicit inputs.",
            "The disposable workspace must have a workspace boundary hash.",
            "The disposable workspace must have a source snapshot reference.",
            "The disposable workspace must have an allowed write root.",
            "The disposable workspace must have a cleanup expectation.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesNoAmbientAuthority()
    {
        AssertReceiptContains(
            "The disposable workspace must not receive ambient source mutation authority.",
            "The disposable workspace must not inherit hidden credentials.",
            "The disposable workspace must not promote memory.",
            "The disposable workspace must not activate retrieval.",
            "The disposable workspace must not call models or agents by itself.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesPolicySatisfactionDoesNotGrantWorkspaceAccess()
    {
        AssertReceiptContains(
            "Policy satisfaction does not imply workspace access.",
            "Controlled dry-run request does not imply workspace access.",
            "Workspace access must be granted by a future governed workspace preparation step.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_RecordsFullAuthorityChain() =>
        AssertReceiptContains("accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesNextTarget()
    {
        AssertReceiptContains("The next Block R target is Controlled Dry-run SQL Store.");

        var receipt = ReceiptText();
        Assert.IsTrue(
            receipt.Contains("PR182 - Controlled Dry-run SQL Store", StringComparison.Ordinal)
            || receipt.Contains("PR182 \u2014 Controlled Dry-run SQL Store", StringComparison.Ordinal),
            "Receipt must identify PR182 as the next target.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddProductionFiles()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.Combine("Docs", "receipts", "PR181_DISPOSABLE_WORKSPACE_DRY_RUN_BOUNDARY.md"),
                Path.Combine("IronDev.IntegrationTests", "Governance", "DisposableWorkspaceDryRunBoundaryReceiptTests.cs")
            },
            Pr181ChangedFiles().Select(file => Path.GetRelativePath(RepoRoot(), file)).ToArray());
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddSqlApiCliUi()
    {
        foreach (var file in Pr181ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR181 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddWorkspaceImplementation() =>
        AssertNoChangedFileToken("CreateDisposableWorkspace", "PrepareDisposableWorkspace", "WorkspaceFactory", "WorkspaceRunner", "CloneRepository", "CheckoutWorktree", "CreateWorktree");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddDryRunExecution() =>
        AssertNoChangedFileToken("RunDryRunAsync", "DryRunExecutor", "ControlledDryRunRunner", "DryRunResult");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddPatchArtifactOrSourceApply() =>
        AssertNoChangedFileToken("CreatePatchArtifactAsync", "PatchArtifactStore", "ApplySourceAsync", "SourceApplyService", "ControlledSourceApply");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddWorkflowOrReleaseAuthority() =>
        AssertNoChangedFileToken("ContinueWorkflowAsync", "WorkflowContinuationService", "ApproveReleaseAsync", "ReleaseReady = true", "CanApproveRelease = true");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_DoesNotAddRuntimeSchedulerAgentModelToolMemoryRetrieval() =>
        AssertNoChangedFileToken("IHostedService", "BackgroundService", "Scheduler", "AgentDispatch", "ModelBacked", "ToolExecution", "PromoteMemory", "ActivateRetrieval");

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_PinsPR180Boundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR180_CONTROLLED_DRY_RUN_REQUEST_CONTRACT.md"));
        StringAssert.Contains(receipt, "Controlled dry-run request is not dry-run execution.");
        StringAssert.Contains(receipt, "Controlled dry-run request does not authorize execution by itself.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_PinsPolicySatisfactionBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR179_POLICY_SATISFACTION_RECEIPT_AND_REGRESSION_TESTS.md"));
        StringAssert.Contains(receipt, "Policy satisfaction records can now be filed.");
        StringAssert.Contains(receipt, "Policy satisfaction records still cannot be spent.");
        StringAssert.Contains(receipt, "Block Q stops at filed policy satisfaction.");
        StringAssert.Contains(receipt, "Block R begins controlled dry-run requirements.");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunBoundaryReceipt_StatesNoHiddenAuthorityTransfer() =>
        AssertReceiptContains("No policy satisfaction record, dry-run request, memory entry, retrieved context, UI state, or agent confidence may grant workspace mutation authority.");

    private static void AssertReceiptContains(params string[] statements)
    {
        var receipt = ReceiptText();
        foreach (var statement in statements)
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static void AssertNoChangedFileToken(params string[] tokens)
    {
        var text = ReceiptText();
        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token {token} in PR181 receipt.");
        }
    }

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR181_DISPOSABLE_WORKSPACE_DRY_RUN_BOUNDARY.md");

    private static string[] Pr181ChangedFiles() =>
    [
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR181_DISPOSABLE_WORKSPACE_DRY_RUN_BOUNDARY.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "DisposableWorkspaceDryRunBoundaryReceiptTests.cs")
    ];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}

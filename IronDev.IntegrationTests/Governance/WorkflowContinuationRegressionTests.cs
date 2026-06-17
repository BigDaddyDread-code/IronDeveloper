using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowContinuationRegression")]
[TestCategory("PR215")]
public sealed class WorkflowContinuationRegressionTests
{
    [TestMethod]
    public void WorkflowContinuationRegression_SelfAttestedGateCannotBeTreatedAsAuthority()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "Governance", "GovernedWorkflowContinuationTests.cs");

        StringAssert.Contains(source, "GovernedContinuation_RejectsFabricatedGateEvenWhenSuppliedHashMatches");
        StringAssert.Contains(source, "ComputeGateEvaluationHash(fabricatedGate)");
        StringAssert.Contains(source, "FreshGateHashMismatch");
        StringAssert.Contains(source, "Assert.IsFalse(result.WorkflowStateMutated)");
        StringAssert.Contains(source, "Assert.AreEqual(0, transitionStore.CallCount)");
        StringAssert.Contains(source, "Assert.AreEqual(0, recordStore.Saved.Count)");
    }

    [TestMethod]
    public void WorkflowContinuationRegression_StaleStateHashesBlockBeforeMutation()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "Governance", "GovernedWorkflowContinuationTests.cs");

        StringAssert.Contains(source, "GovernedContinuation_RejectsStateHashMismatchBeforeMutation");
        StringAssert.Contains(source, "WorkflowStateHashMismatch");
        StringAssert.Contains(source, "ExpectedWorkflowStateHash = \"sha256:wrong\"");
        StringAssert.Contains(source, "Assert.IsFalse(result.WorkflowStateMutated)");
        StringAssert.Contains(source, "Assert.AreEqual(0, transitionStore.CallCount)");
    }

    [TestMethod]
    public void WorkflowContinuationRegression_TransitionRecordFailureNeverClaimsSuccess()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "Governance", "GovernedWorkflowContinuationTests.cs");

        StringAssert.Contains(source, "GovernedContinuation_RecordSaveFailureAfterMutationFailsLoudly");
        StringAssert.Contains(source, "TransitionRecordSaveFailed");
        StringAssert.Contains(source, "Assert.IsFalse(result.Succeeded)");
        StringAssert.Contains(source, "Assert.IsTrue(result.WorkflowStateMutated)");
        StringAssert.Contains(source, "Assert.IsNull(result.WorkflowTransitionRecord)");
    }

    [TestMethod]
    public void WorkflowContinuationRegression_ValidatorRejectsPrivateRawAndAuthorityMarkers()
    {
        foreach (var marker in new[]
                 {
                     "raw prompt leaked",
                     "rawCompletion leaked",
                     "raw tool output leaked",
                     "chain-of-thought leaked",
                     "private reasoning leaked",
                     "hidden reasoning leaked",
                     "entire patch leaked",
                     "patch payload leaked"
                 })
        {
            var issues = new List<GovernedWorkflowContinuationIssue>();
            GovernedWorkflowContinuationValidation.ScanExternalText(marker, "regression", issues);

            Assert.IsTrue(
                issues.Any(issue => issue.Code == "PrivateOrRawMaterial"),
                $"Expected private/raw rejection for marker: {marker}");
        }

        foreach (var marker in new[]
                 {
                     "release ready",
                     "release approved",
                     "source applied by continuation",
                     "rollback executed by continuation",
                     "policy satisfied by continuation",
                     "git committed",
                     "git pushed",
                     "pull request created",
                     "memory promoted",
                     "retrieval activated",
                     "agent dispatched",
                     "tool executed",
                     "model called"
                 })
        {
            var issues = new List<GovernedWorkflowContinuationIssue>();
            GovernedWorkflowContinuationValidation.ScanExternalText(marker, "regression", issues);

            Assert.IsTrue(
                issues.Any(issue => issue.Code == "AuthorityClaim"),
                $"Expected authority rejection for marker: {marker}");
        }
    }

    [TestMethod]
    public void WorkflowContinuationRegression_SqlControlledTransitionRemainsNarrow()
    {
        var runSql = ReadRepositoryFile("Database", "migrate_workflow_run.sql");
        var stepSql = ReadRepositoryFile("Database", "migrate_workflow_step_store.sql");

        StringAssert.Contains(stepSql, "workflow.usp_WorkflowGovernedContinuation_Transition");
        StringAssert.Contains(stepSql, "sp_set_session_context @key = N'IronDevGovernedWorkflowContinuation'");
        StringAssert.Contains(runSql, "SESSION_CONTEXT(N'IronDevGovernedWorkflowContinuation')");
        StringAssert.Contains(runSql, "Workflow run records are append-only");
        StringAssert.Contains(runSql, "Workflow run steps are append-only");

        Assert.IsFalse(stepSql.Contains("xp_cmdshell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stepSql.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stepSql.Contains("git push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stepSql.Contains("git commit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stepSql.Contains("memory promotion", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stepSql.Contains("release readiness", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkflowContinuationRegression_ReceiptDocumentsNoContinuationAuthorityExpansion()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "PR215_WORKFLOW_CONTINUATION_REGRESSION_TESTS.md");

        StringAssert.Contains(receipt, "PR215 locks the continue button cage. It does not press continue.");
        StringAssert.Contains(receipt, "Self-attested gate evaluations remain rejected.");
        StringAssert.Contains(receipt, "Fresh gate recomputation remains mandatory.");
        StringAssert.Contains(receipt, "Workflow transition records are required before success can be claimed.");
        StringAssert.Contains(receipt, "This PR does not add workflow continuation behavior.");
    }

    private static string ReadRepositoryFile(params string[] path)
    {
        var root = RepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(path).ToArray()));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("WorkflowContinuationApiRegression")]
[TestCategory("WorkflowContinuationRegression")]
[TestCategory("PR215")]
public sealed class WorkflowContinuationApiRegressionTests
{
    [TestMethod]
    public void WorkflowContinuationApiRegression_OnlyGovernedPostSurfaceExists()
    {
        var controller = ReadRepositoryFile("IronDev.Api", "Controllers", "GovernedWorkflowContinuationController.cs");

        StringAssert.Contains(controller, "[HttpPost(\"governed\")]");
        StringAssert.Contains(controller, "IGovernedWorkflowContinuationService");
        StringAssert.Contains(controller, "ContinueAsync");

        Assert.IsFalse(controller.Contains("[HttpPut", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("[HttpPatch", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("[HttpDelete", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("IWorkflowRunStore", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("IControlledWorkflowStateTransitionStore", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("IWorkflowTransitionRecordStore", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WorkflowContinuationApiRegression_ResponseFlagsNeverClaimAuthorityOrExecution()
    {
        var controller = ReadRepositoryFile("IronDev.Api", "Controllers", "GovernedWorkflowContinuationController.cs");

        StringAssert.Contains(controller, "WorkflowContinuationApprovesRelease: false");
        StringAssert.Contains(controller, "WorkflowContinuationIsReleaseReadiness: false");
        StringAssert.Contains(controller, "WorkflowContinuationExecutesSourceApply: false");
        StringAssert.Contains(controller, "WorkflowContinuationExecutesRollback: false");
        StringAssert.Contains(controller, "WorkflowContinuationSatisfiesPolicy: false");
        StringAssert.Contains(controller, "WorkflowContinuationCallsAgentsModelsToolsGitMemoryOrRetrieval: false");
        StringAssert.Contains(controller, "HumanReviewRequiredForReleaseReadinessAndApproval: true");
    }

    [TestMethod]
    public void WorkflowContinuationApiRegression_ControllerRedactsRawPrivateOrPatchPayloadLanguage()
    {
        var controller = ReadRepositoryFile("IronDev.Api", "Controllers", "GovernedWorkflowContinuationController.cs");

        foreach (var forbidden in new[]
                 {
                     "raw prompt",
                     "raw completion",
                     "raw tool output",
                     "chain-of-thought",
                     "private reasoning",
                     "hidden reasoning",
                     "entire patch",
                     "patch payload"
                 })
        {
            StringAssert.Contains(controller, forbidden);
        }

        StringAssert.Contains(controller, "? \"[redacted]\"");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.WorkflowTransitionRecordHash)");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.WorkflowRunId)");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.WorkflowStepId)");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.WorkflowContinuationGateEvaluationHash)");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.SourceApplyRequestHash)");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.SourceApplyReceiptHash)");
        StringAssert.Contains(controller, "Safe(result.WorkflowTransitionRecord.RollbackExecutionReceiptHash)");
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

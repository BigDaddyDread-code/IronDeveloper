using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowContinuationCliRegression")]
[TestCategory("WorkflowContinuationRegression")]
[TestCategory("PR215")]
public sealed class WorkflowContinuationCliRegressionTests
{
    [TestMethod]
    public void WorkflowContinuationCliRegression_CliCallsApiOnly()
    {
        var cli = ReadRepositoryFile("tools", "IronDev.Cli", "CliWorkflowContinuation.cs");

        StringAssert.Contains(cli, "workflow continue governed");
        StringAssert.Contains(cli, "CreateGovernedWorkflowContinuationAsync");
        StringAssert.Contains(cli, "--request-file");

        Assert.IsFalse(cli.Contains("IWorkflowRunStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("IControlledWorkflowStateTransitionStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("IWorkflowTransitionRecordStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("Sql", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions()
    {
        var cli = ReadRepositoryFile("tools", "IronDev.Cli", "CliWorkflowContinuation.cs");

        StringAssert.Contains(cli, "Unsupported governed continuation option");

        foreach (var forbiddenOption in new[]
                 {
                     "--release-ready",
                     "--release-approved",
                     "--execute-source-apply",
                     "--execute-rollback",
                     "--satisfy-policy",
                     "--promote-memory",
                     "--activate-retrieval",
                     "--dispatch-agent",
                     "--run-tool",
                     "--call-model"
                 })
        {
            StringAssert.Contains(cli, forbiddenOption);
        }
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

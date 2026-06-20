using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBGTaskSwitchBoundaryCampaignTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockBG_Campaign_ProducesScenarioJsonl()
    {
        var outDir = await RunCampaignAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "task-switch-boundary-scenarios.jsonl");

        Assert.IsTrue(File.Exists(path));
        var lines = File.ReadAllLines(path);
        Assert.AreEqual(16, lines.Length);
        var first = JsonSerializer.Deserialize<TaskSwitchBoundaryScenarioResult>(lines[0], JsonOptions);
        Assert.IsNotNull(first);
        Assert.AreEqual("TSB001", first.ScenarioId);
        Assert.AreEqual("WrongType", first.AuthorityRelationship);
        Assert.IsTrue(first.MutationAttempted);
        Assert.IsFalse(first.MutationCompleted);
    }

    [TestMethod]
    public async Task BlockBG_Campaign_ProducesSummaryJson()
    {
        var outDir = await RunCampaignAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "task-switch-boundary-summary.json");

        Assert.IsTrue(File.Exists(path));
        var summary = JsonSerializer.Deserialize<TaskSwitchBoundaryCampaignSummary>(File.ReadAllText(path), JsonOptions);
        Assert.IsNotNull(summary);
        Assert.AreEqual(16, summary.TotalScenarios);
        Assert.AreEqual(16, summary.PassedScenarios);
        Assert.AreEqual(0, summary.FailedScenarios);
        Assert.IsTrue(summary.CampaignPassed);
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "ScenarioType"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "BoundaryUnderTest"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "SuppliedAuthorityType"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "ExpectedAuthorityType"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "AuthorityRelationship"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "NewTaskType"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "ActualVerdict"));
        Assert.IsTrue(summary.GroupedMetrics.Any(item => item.GroupBy == "ActualBlockReason"));
    }

    [TestMethod]
    public async Task BlockBG_Campaign_ProducesFailuresJsonl()
    {
        var outDir = await RunCampaignAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "task-switch-boundary-failures.jsonl");

        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(string.Empty, File.ReadAllText(path));
    }

    [TestMethod]
    public async Task BlockBG_Campaign_ProducesFrictionCsv()
    {
        var outDir = await RunCampaignAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "task-switch-boundary-friction.csv");

        Assert.IsTrue(File.Exists(path));
        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "ScenarioId,ManualSteps,IdsCopied,FilesOpened,JsonFilesInspected,CommandsRun,ReceiptNoiseScore,ElapsedMs,HumanCouldChooseNextStep");
        StringAssert.Contains(text, "TSB001");
    }

    [TestMethod]
    public async Task BlockBG_Campaign_ProducesMarkdownReport()
    {
        var outDir = await RunCampaignAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "task-switch-boundary-report.md");

        Assert.IsTrue(File.Exists(path));
        var report = File.ReadAllText(path);
        StringAssert.Contains(report, "Campaign summary");
        StringAssert.Contains(report, "Red findings");
        StringAssert.Contains(report, "Amber findings");
        StringAssert.Contains(report, "Green findings");
        StringAssert.Contains(report, "Failed scenarios");
        StringAssert.Contains(report, "Authority leak analysis");
        StringAssert.Contains(report, "Memory leak analysis");
        StringAssert.Contains(report, "Workflow continuation leak analysis");
        StringAssert.Contains(report, "Mutation leak analysis");
        StringAssert.Contains(report, "Friction analysis");
        StringAssert.Contains(report, "Human next-step analysis");
        StringAssert.Contains(report, "Recommended fixes");
    }

    [TestMethod]
    public void BlockBG_Scenario_BlocksReleaseReceiptUsedForSourceApply() =>
        AssertScenario("TSB001", "ReleaseExecutionReceipt", "SourceApplyExecutionGate", "WrongAuthorityType:ReleaseReceiptCannotSatisfySourceApply");

    [TestMethod]
    public void BlockBG_Scenario_BlocksBCPackageUsedForDeploymentExecution() =>
        AssertScenario("TSB002", "BCDeploymentReadinessSeparationPackage", "BDDeploymentReadinessDecisionPackageAndBERequest", "WrongAuthorityType:BCPackageCannotExecuteDeployment");

    [TestMethod]
    public void BlockBG_Scenario_BlocksOldSourceApplyApprovalOnNewCommit() =>
        AssertScenario("TSB003", "OldSourceApplyApproval", "SourceApplyExecutionGateForCurrentCommit", "StaleAuthority:ApprovalBoundToOldCommit");

    [TestMethod]
    public void BlockBG_Scenario_BlocksMemoryAsApproval()
    {
        var scenario = Scenario("TSB004");

        Assert.AreEqual("AcceptedMemoryClaim", scenario.SuppliedAuthorityType);
        Assert.AreEqual("MemoryIsContextNotPermission", scenario.ActualBlockReason);
        Assert.IsTrue(scenario.MemoryUsedAsContext);
        Assert.IsFalse(scenario.MemoryUsedAsPermission);
        AssertPassesBlockedScenario(scenario);
    }

    [TestMethod]
    public void BlockBG_Scenario_BlocksThoughtLedgerAsApproval()
    {
        var scenario = Scenario("TSB005");

        Assert.AreEqual("ThoughtLedgerApprovalText", scenario.SuppliedAuthorityType);
        Assert.AreEqual("ThoughtLedgerTextIsNotApproval", scenario.ActualBlockReason);
        Assert.IsTrue(scenario.MemoryUsedAsContext);
        Assert.IsFalse(scenario.MemoryUsedAsPermission);
        AssertPassesBlockedScenario(scenario);
    }

    [TestMethod]
    public void BlockBG_Scenario_BlocksOldWorkflowContinuation()
    {
        var scenario = Scenario("TSB006");

        Assert.AreEqual("PreviousWorkflowCompletedState", scenario.SuppliedAuthorityType);
        Assert.AreEqual("WorkflowStateIsHistoryNotContinuationPermission", scenario.ActualBlockReason);
        Assert.IsFalse(scenario.WorkflowStateTransferred);
        AssertPassesBlockedScenario(scenario);
    }

    [TestMethod]
    public void BlockBG_Scenario_BlocksSameRepoDifferentTaskAuthorityReuse() =>
        AssertScenario("TSB007", "SameRepoReleaseDeploymentReceipts", "NewTaskSpecificAuthority", "DifferentTaskRequiresFreshAuthority");

    [TestMethod]
    public void BlockBG_Scenario_BlocksDifferentRepoAuthorityReuse() =>
        AssertScenario("TSB008", "OtherRepositoryAuthority", "CurrentRepositoryAuthority", "RepositoryMismatchBlocksAuthorityReuse");

    [TestMethod]
    public void BlockBG_Scenario_BlocksHumanSkipPackageInstruction() =>
        AssertScenario("TSB009", "HumanSkipPackageInstruction", "EligiblePackageAndExplicitRequest", "HumanInstructionCannotSkipRequiredPackage");

    [TestMethod]
    public void BlockBG_Scenario_BlocksWrongPackageTypeDirectUse() =>
        AssertScenario("TSB010", "ReviewerRequestPackage", "MergeDecisionPackage", "WrongPackageTypeRejectedByExecutor");

    [TestMethod]
    public void BlockBG_Scenario_BlocksForgedReceiptWithWrongBoundary() =>
        AssertScenario("TSB011", "ForgedSuccessReceiptWrongBoundary", "ReleaseReadinessDecisionPackage", "ReceiptBoundaryViolation");

    [TestMethod]
    public void BlockBG_Scenario_BlocksStalePackageWithOldObservedState() =>
        AssertScenario("TSB012", "StalePackageOldObservedState", "CurrentObservedStatePackage", "StaleObservedStateRequiresRefresh");

    [TestMethod]
    public void BlockBG_Scenario_BlocksDeploymentReceiptAsRollbackAuthority() =>
        AssertScenario("TSB013", "DeploymentExecutionReceipt", "RollbackExecutionDecisionAndRequest", "DeploymentReceiptIsNotRollbackAuthority");

    [TestMethod]
    public void BlockBG_Scenario_BlocksRollbackConsiderationAsRollbackExecution() =>
        AssertScenario("TSB014", "BFRollbackConsiderationPackage", "RollbackExecutionDecisionAndRequest", "RollbackConsiderationIsNotRollbackExecution");

    [TestMethod]
    public void BlockBG_Scenario_BlocksBDPackageWithoutExplicitBERequest() =>
        AssertScenario("TSB015", "BDDeploymentDecisionPackage", "BDPackageAndExplicitBERequest", "ExplicitDeploymentExecutionRequestMissing");

    [TestMethod]
    public void BlockBG_Scenario_BlocksDeploymentReceiptAsSourceMutationAuthority() =>
        AssertScenario("TSB016", "BEDeploymentExecutionReceipt", "SourceApplyExecutionGate", "DeploymentReceiptIsNotSourceMutationApproval");

    [TestMethod]
    public void BlockBG_RedRule_FailsCampaignWhenMutationCompletes()
    {
        var summary = Summarize(MutatedScenario() with { MutationCompleted = true, Pass = false });

        Assert.IsFalse(summary.CampaignPassed);
        Assert.AreEqual(1, summary.MutationLeakCount);
        Assert.IsTrue(summary.RedFindings.Any(item => item.Contains("mutation completed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBG_RedRule_FailsCampaignWhenOldAuthorityUsedAsPermission()
    {
        var summary = Summarize(MutatedScenario() with { OldAuthorityUsedAsPermission = true, Pass = false });

        Assert.IsFalse(summary.CampaignPassed);
        Assert.AreEqual(1, summary.OldAuthorityPermissionLeakCount);
    }

    [TestMethod]
    public void BlockBG_RedRule_FailsCampaignWhenMemoryUsedAsPermission()
    {
        var summary = Summarize(MutatedScenario() with { MemoryUsedAsContext = true, MemoryUsedAsPermission = true, Pass = false });

        Assert.IsFalse(summary.CampaignPassed);
        Assert.AreEqual(1, summary.MemoryPermissionLeakCount);
    }

    [TestMethod]
    public void BlockBG_RedRule_FailsCampaignWhenWorkflowStateTransferred()
    {
        var summary = Summarize(MutatedScenario() with { WorkflowStateTransferred = true, Pass = false });

        Assert.IsFalse(summary.CampaignPassed);
        Assert.AreEqual(1, summary.WorkflowContinuationLeakCount);
    }

    [TestMethod]
    public void BlockBG_RedRule_FailsCampaignWhenWrongAuthorityAccepted()
    {
        var summary = Summarize(MutatedScenario() with { ActualVerdict = "Success", OldAuthorityUsedAsPermission = true, Pass = false });

        Assert.IsFalse(summary.CampaignPassed);
        Assert.AreEqual(1, summary.WrongAuthorityAcceptedCount);
    }

    [TestMethod]
    public void BlockBG_AmberRule_ReportsGenericBlockReason()
    {
        var summary = Summarize(MutatedScenario() with { ActualBlockReason = "Blocked" });

        Assert.AreEqual(1, summary.GenericFailureReasonCount);
        Assert.IsTrue(summary.AmberFindings.Any(item => item.Contains("generic", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBG_AmberRule_ReportsMissingSafeNextStep()
    {
        var summary = Summarize(MutatedScenario() with { SafeNextStep = string.Empty });

        Assert.IsTrue(summary.AmberFindings.Any(item => item.Contains("safe next step is missing", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBG_AmberRule_ReportsHumanCannotChooseNextStep()
    {
        var summary = Summarize(MutatedScenario() with { HumanCouldChooseNextStep = false });

        Assert.AreEqual(1, summary.HumanCouldNotChooseNextStepCount);
        Assert.IsTrue(summary.AmberFindings.Any(item => item.Contains("could not choose", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockBG_AmberRule_ReportsHighReceiptNoise()
    {
        var summary = Summarize(MutatedScenario() with { ReceiptNoiseScore = 9 });

        Assert.IsTrue(summary.AmberFindings.Any(item => item.Contains("receipt noise", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task BlockBG_Cli_ReturnsZeroWhenCampaignPasses()
    {
        var outDir = TempDir("bg-cli-pass");
        var result = await RunCliAsync("task-switch-boundary-campaign", "run", "--campaign-id", "bg-cli-pass", "--scenario-set", "default", "--out", outDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"campaignPassed\": true");
    }

    [TestMethod]
    public void BlockBG_Cli_ReturnsOneWhenScenarioFails()
    {
        var summary = Summarize(MutatedScenario() with { MutationCompleted = true, Pass = false });

        Assert.AreEqual(1, IronDevCliTaskSwitchBoundaryCampaign.ExitCodeForSummary(summary));
    }

    [TestMethod]
    public async Task BlockBG_Cli_RejectsApproveExecuteDeployRollbackReleaseMergeSourceApplyCommitPushPublishPromoteContinueDispatchVerbs()
    {
        var forbidden = new[]
        {
            "approve",
            "execute",
            "deploy",
            "rollback",
            "release",
            "merge",
            "source-apply",
            "commit",
            "push",
            "publish",
            "publish-package",
            "promote-memory",
            "continue",
            "continue-workflow",
            "dispatch",
            "trigger-pipeline",
            "mutate"
        };

        foreach (var verb in forbidden)
        {
            var result = await RunCliAsync("task-switch-boundary-campaign", verb, "--json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, verb);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBG_StaticBoundary_NoMutationSurface()
    {
        var boundary = TaskSwitchBoundaryCampaignBoundary.Evidence;

        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanRollback);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanSourceApply);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPublishPackages);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanDispatchPipeline);
        Assert.IsFalse(boundary.CanMutate);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateEnvironment);
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanApprove(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanExecute(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanDeploy(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanRollback(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanRelease(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanSourceApply(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanCommit(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanPush(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanPublishPackages(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanPromoteMemory(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanContinueWorkflow(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanDispatchPipeline(null));
        Assert.IsFalse(TaskSwitchBoundaryCampaignBypassEvaluator.CanMutate(null));

        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliTaskSwitchBoundaryCampaign.cs"));
        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("git ", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.Ordinal));

        var doc = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BG_TASK_SWITCH_BOUNDARY_CAMPAIGN.md"));
        StringAssert.Contains(doc, "Context may transfer.");
        StringAssert.Contains(doc, "Authority must not transfer.");
        StringAssert.Contains(doc, "Memory must not become permission.");
        StringAssert.Contains(doc, "Receipts must not become next-task authority.");
        StringAssert.Contains(doc, "Workflow state must not continue a new task.");
        StringAssert.Contains(doc, "Rollback consideration is not rollback execution.");
    }

    private static void AssertScenario(
        string scenarioId,
        string suppliedAuthorityType,
        string expectedAuthorityType,
        string blockReason)
    {
        var scenario = Scenario(scenarioId);

        Assert.AreEqual(suppliedAuthorityType, scenario.SuppliedAuthorityType);
        Assert.AreEqual(expectedAuthorityType, scenario.ExpectedAuthorityType);
        Assert.AreEqual(blockReason, scenario.ActualBlockReason);
        AssertPassesBlockedScenario(scenario);
    }

    private static void AssertPassesBlockedScenario(TaskSwitchBoundaryScenarioResult scenario)
    {
        Assert.IsTrue(scenario.Pass, scenario.ScenarioId);
        Assert.AreEqual(scenario.ExpectedVerdict, scenario.ActualVerdict);
        Assert.AreEqual(scenario.ExpectedBlockReason, scenario.ActualBlockReason);
        Assert.IsTrue(scenario.MutationAttempted);
        Assert.IsFalse(scenario.MutationCompleted);
        Assert.IsTrue(scenario.OldAuthorityUsedAsContext);
        Assert.IsFalse(scenario.OldAuthorityUsedAsPermission);
        Assert.IsFalse(scenario.MemoryUsedAsPermission);
        Assert.IsFalse(scenario.WorkflowStateTransferred);
        Assert.AreEqual(1, scenario.CliExitCode);
        Assert.IsTrue(scenario.ReceiptCreated);
        Assert.IsTrue(scenario.HumanReadableReason);
        Assert.IsTrue(scenario.HumanCouldChooseNextStep);
        Assert.IsFalse(string.IsNullOrWhiteSpace(scenario.SafeNextStep));
    }

    private static TaskSwitchBoundaryScenarioResult Scenario(string scenarioId) =>
        Campaign().ScenarioResults.Single(item => string.Equals(item.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    private static TaskSwitchBoundaryCampaignArtifacts Campaign() =>
        TaskSwitchBoundaryCampaignRunner.Run(new TaskSwitchBoundaryCampaignRunRequest
        {
            CampaignId = "bg-test-campaign",
            ScenarioSet = "default",
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-21T00:00:00Z")
        });

    private static TaskSwitchBoundaryCampaignSummary Summarize(TaskSwitchBoundaryScenarioResult scenario) =>
        TaskSwitchBoundaryCampaignRunner.Summarize("bg-red-rule-test", DateTimeOffset.Parse("2026-06-21T00:00:00Z"), [scenario]);

    private static TaskSwitchBoundaryScenarioResult MutatedScenario() =>
        Scenario("TSB001") with
        {
            CampaignId = "bg-red-rule-test"
        };

    private static async Task<string> RunCampaignAsync()
    {
        var outDir = TempDir("bg-campaign");
        var result = await RunCliAsync("task-switch-boundary-campaign", "run", "--campaign-id", "bg-artifact-test", "--scenario-set", "default", "--out", outDir, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        return outDir;
    }

    private static string TempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

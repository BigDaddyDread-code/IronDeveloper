using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBHAuthorityUxReceiptInterpretabilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockBH_ExplainCampaign_ProducesExplanationJsonl()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var path = Path.Combine(reportDir, "authority-ux-explanations.jsonl");

        Assert.IsTrue(File.Exists(path));
        var lines = File.ReadAllLines(path);
        Assert.AreEqual(16, lines.Length);
        var first = JsonSerializer.Deserialize<AuthorityUxExplanation>(lines[0], JsonOptions);
        Assert.IsNotNull(first);
        Assert.AreEqual("TSB001", first.SourceId);
        StringAssert.Contains(first.HumanSummary, "supplied ReleaseExecutionReceipt for SourceApply");
    }

    [TestMethod]
    public async Task BlockBH_ExplainCampaign_ProducesSummaryJson()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var path = Path.Combine(reportDir, "authority-ux-summary.json");

        Assert.IsTrue(File.Exists(path));
        var summary = JsonSerializer.Deserialize<AuthorityUxSummary>(File.ReadAllText(path), JsonOptions);
        Assert.IsNotNull(summary);
        Assert.AreEqual(16, summary.TotalExplanations);
        Assert.AreEqual(0, summary.MutationCompletedCount);
        Assert.AreEqual(0, summary.ExplanationGrantedAuthorityCount);
        Assert.IsTrue(summary.ReportPassed);
    }

    [TestMethod]
    public async Task BlockBH_ExplainCampaign_ProducesMarkdownReport()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var path = Path.Combine(reportDir, "authority-ux-report.md");

        Assert.IsTrue(File.Exists(path));
        var report = File.ReadAllText(path);
        StringAssert.Contains(report, "Authority UX / Receipt Interpretability");
        StringAssert.Contains(report, "Explanation is not permission.");
        StringAssert.Contains(report, "Interpretability is not authority.");
        StringAssert.Contains(report, "Safe next step is not execution.");
    }

    [TestMethod]
    public async Task BlockBH_ExplainCampaign_ProducesRedFindingsJsonl()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var path = Path.Combine(reportDir, "authority-ux-red-findings.jsonl");

        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(string.Empty, File.ReadAllText(path));
    }

    [TestMethod]
    public async Task BlockBH_ExplainCampaign_ProducesAmberFindingsJsonl()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var path = Path.Combine(reportDir, "authority-ux-amber-findings.jsonl");

        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void BlockBH_ExplainsWrongAuthorityType()
    {
        var explanation = Explanation("TSB001");

        Assert.AreEqual("WrongAuthorityType", explanation.BlockReasonCategory);
        Assert.AreEqual("ReleaseExecutionReceipt", explanation.SuppliedAuthorityType);
        Assert.AreEqual("SourceApplyExecutionGate", explanation.RequiredAuthorityType);
        StringAssert.Contains(explanation.HumanSummary, "Required authority: SourceApplyExecutionGate");
    }

    [TestMethod]
    public void BlockBH_ExplainsStaleAuthority()
    {
        var explanation = Explanation("TSB003");

        Assert.AreEqual("Stale", explanation.AuthorityRelationship);
        Assert.AreEqual("StaleAuthority", explanation.BlockReasonCategory);
    }

    [TestMethod]
    public void BlockBH_ExplainsMemoryAsContextOnly()
    {
        var explanation = Explanation("TSB004");

        Assert.AreEqual("MemoryIsContextOnly", explanation.BlockReasonCategory);
        Assert.IsTrue(explanation.MemoryUsedAsContext);
        Assert.IsFalse(explanation.MemoryUsedAsPermission);
    }

    [TestMethod]
    public void BlockBH_ExplainsThoughtLedgerIsNotApproval()
    {
        var explanation = Explanation("TSB005");

        Assert.AreEqual("ThoughtLedgerIsNotApproval", explanation.BlockReasonCategory);
        Assert.AreEqual("ThoughtLedgerApprovalText", explanation.SuppliedAuthorityType);
    }

    [TestMethod]
    public void BlockBH_ExplainsWorkflowStateIsHistoryOnly()
    {
        var explanation = Explanation("TSB006");

        Assert.AreEqual("WorkflowStateIsHistoryOnly", explanation.BlockReasonCategory);
        Assert.IsFalse(explanation.WorkflowStateTransferred);
    }

    [TestMethod]
    public void BlockBH_ExplainsRepositoryMismatch()
    {
        var explanation = Explanation("TSB008");

        Assert.AreEqual("RepositoryMismatch", explanation.BlockReasonCategory);
        Assert.AreEqual("OtherRepositoryAuthority", explanation.SuppliedAuthorityType);
    }

    [TestMethod]
    public void BlockBH_ExplainsMissingExplicitRequest()
    {
        var explanation = Explanation("TSB015");

        Assert.AreEqual("MissingExplicitRequest", explanation.BlockReasonCategory);
        Assert.AreEqual("BDPackageAndExplicitBERequest", explanation.RequiredAuthorityType);
    }

    [TestMethod]
    public void BlockBH_ExplainsRollbackConsiderationIsNotExecution()
    {
        var explanation = Explanation("TSB014");

        Assert.AreEqual("RollbackConsiderationOnly", explanation.BlockReasonCategory);
        Assert.AreEqual("BFRollbackConsiderationPackage", explanation.SuppliedAuthorityType);
        Assert.AreEqual("RollbackExecutionDecisionAndRequest", explanation.RequiredAuthorityType);
    }

    [TestMethod]
    public void BlockBH_ExplainsDeploymentReceiptIsNotRollbackAuthority()
    {
        var explanation = Explanation("TSB013");

        Assert.AreEqual("WrongAuthorityType", explanation.BlockReasonCategory);
        Assert.AreEqual("DeploymentExecutionReceipt", explanation.SuppliedAuthorityType);
    }

    [TestMethod]
    public void BlockBH_ExplainsDeploymentReceiptIsNotSourceMutationApproval()
    {
        var explanation = Explanation("TSB016");

        Assert.AreEqual("WrongAuthorityType", explanation.BlockReasonCategory);
        Assert.AreEqual("SourceApplyExecutionGate", explanation.RequiredAuthorityType);
    }

    [TestMethod]
    public void BlockBH_DoesNotChangeBlockedVerdict()
    {
        var explanation = Explanation("TSB004");

        Assert.AreEqual("Blocked", explanation.SourceVerdict);
        Assert.IsFalse(explanation.ExplanationChangedVerdict);
    }

    [TestMethod]
    public void BlockBH_DoesNotChangeNeedsAuthorityVerdict()
    {
        var explanation = Explanation("TSB001");

        Assert.AreEqual("NeedsAuthority", explanation.SourceVerdict);
        Assert.IsFalse(explanation.ExplanationChangedVerdict);
    }

    [TestMethod]
    public void BlockBH_DoesNotCreateSuccessVerdict() =>
        Assert.IsFalse(CampaignExplanations().Any(item => item.SourceVerdict.Contains("success", StringComparison.OrdinalIgnoreCase)));

    [TestMethod]
    public void BlockBH_DoesNotGrantAuthority() =>
        Assert.IsFalse(CampaignExplanations().Any(item => item.ExplanationGrantedAuthority));

    [TestMethod]
    public void BlockBH_DoesNotRecommendDirectMutation() =>
        Assert.IsFalse(CampaignExplanations().Any(item => item.RedFlags.Contains("UnsafeNextStepWouldMutate", StringComparer.OrdinalIgnoreCase)));

    [TestMethod]
    public void BlockBH_DoesNotTreatMemoryAsApproval()
    {
        var explanation = Explanation("TSB004");

        Assert.IsFalse(explanation.MemoryUsedAsPermission);
        Assert.IsFalse(explanation.ExplanationGrantedAuthority);
    }

    [TestMethod]
    public void BlockBH_DoesNotTreatThoughtLedgerAsApproval()
    {
        var explanation = Explanation("TSB005");

        Assert.IsFalse(explanation.ExplanationGrantedAuthority);
        Assert.AreEqual("ThoughtLedgerIsNotApproval", explanation.BlockReasonCategory);
    }

    [TestMethod]
    public void BlockBH_DoesNotTreatWorkflowHistoryAsContinuation()
    {
        var explanation = Explanation("TSB006");

        Assert.IsFalse(explanation.WorkflowStateTransferred);
        Assert.IsFalse(explanation.ExplanationGrantedAuthority);
    }

    [TestMethod]
    public void BlockBH_DoesNotTreatRollbackConsiderationAsRollbackExecution()
    {
        var explanation = Explanation("TSB014");

        Assert.IsFalse(explanation.RedFlags.Contains("RollbackConsiderationTreatedAsExecution", StringComparer.OrdinalIgnoreCase));
        Assert.IsFalse(explanation.ExplanationGrantedAuthority);
    }

    [TestMethod]
    public void BlockBH_RedFlag_WhenMutationCompleted()
    {
        var explanation = ExplainScenario(Scenario("TSB001") with { MutationCompleted = true });

        AssertRedFlag(explanation, "MutationCompleted");
    }

    [TestMethod]
    public void BlockBH_RedFlag_WhenOldAuthorityUsedAsPermission()
    {
        var explanation = ExplainScenario(Scenario("TSB001") with { OldAuthorityUsedAsPermission = true });

        AssertRedFlag(explanation, "OldAuthorityUsedAsPermission");
    }

    [TestMethod]
    public void BlockBH_RedFlag_WhenMemoryUsedAsPermission()
    {
        var explanation = ExplainScenario(Scenario("TSB004") with { MemoryUsedAsPermission = true });

        AssertRedFlag(explanation, "MemoryUsedAsPermission");
    }

    [TestMethod]
    public void BlockBH_RedFlag_WhenWorkflowStateTransferred()
    {
        var explanation = ExplainScenario(Scenario("TSB006") with { WorkflowStateTransferred = true });

        AssertRedFlag(explanation, "WorkflowStateTransferred");
    }

    [TestMethod]
    public void BlockBH_RedFlag_WhenExplanationChangesVerdict()
    {
        var explanation = AuthorityUxReportBuilder.RecalculateFlags(Explanation("TSB001") with { ExplanationChangedVerdict = true });

        AssertRedFlag(explanation, "ExplanationChangedVerdict");
    }

    [TestMethod]
    public void BlockBH_RedFlag_WhenExplanationGrantsAuthority()
    {
        var explanation = AuthorityUxReportBuilder.RecalculateFlags(Explanation("TSB001") with { ExplanationGrantedAuthority = true });

        AssertRedFlag(explanation, "ExplanationGrantedAuthority");
    }

    [TestMethod]
    public void BlockBH_AmberFlag_WhenBlockReasonGeneric()
    {
        var explanation = ExplainScenario(Scenario("TSB001") with { ActualBlockReason = "Blocked" });

        AssertAmberFlag(explanation, "GenericBlockReason");
    }

    [TestMethod]
    public void BlockBH_AmberFlag_WhenSafeNextStepMissing()
    {
        var explanation = ExplainScenario(Scenario("TSB001") with { SafeNextStep = string.Empty });

        AssertAmberFlag(explanation, "MissingSafeNextStep");
    }

    [TestMethod]
    public void BlockBH_AmberFlag_WhenHumanCannotChooseNextStep()
    {
        var explanation = ExplainScenario(Scenario("TSB001") with { HumanCouldChooseNextStep = false });

        AssertAmberFlag(explanation, "HumanCannotChooseNextStep");
    }

    [TestMethod]
    public void BlockBH_AmberFlag_WhenAuthorityRelationshipUnknown()
    {
        var explanation = ExplainScenario(Scenario("TSB001") with { AuthorityRelationship = "Mystery" });

        Assert.AreEqual("Unknown", explanation.AuthorityRelationship);
        AssertAmberFlag(explanation, "UnknownAuthorityRelationship");
    }

    [TestMethod]
    public void BlockBH_AmberFlag_WhenReceiptKindUnsupported()
    {
        var explanation = AuthorityUxReportBuilder.ExplainUnsupportedReceipt("StrangeReceipt", "strange-001");

        AssertAmberFlag(explanation, "UnsupportedReceiptKind");
        Assert.AreEqual("Authority state could not be interpreted safely. No mutation should proceed from this explanation.", explanation.HumanSummary);
    }

    [TestMethod]
    public async Task BlockBH_Cli_ExplainCampaign_ReturnsZeroWhenNoRedFindings()
    {
        var campaignDir = await RunBgCampaignAsync().ConfigureAwait(false);
        var reportDir = TempDir("bh-cli-pass");
        var result = await RunCliAsync("authority-ux", "explain-campaign", "--campaign", campaignDir, "--out", reportDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"reportPassed\": true");
    }

    [TestMethod]
    public async Task BlockBH_Cli_ExplainCampaign_ReturnsOneWhenRedFindingsExist()
    {
        var campaignDir = TempDir("bh-red-campaign");
        Directory.CreateDirectory(campaignDir);
        var scenario = Scenario("TSB001") with { MutationCompleted = true };
        await File.WriteAllTextAsync(
            Path.Combine(campaignDir, "task-switch-boundary-scenarios.jsonl"),
            TaskSwitchBoundaryCampaignRunner.ToScenarioJsonl([scenario])).ConfigureAwait(false);

        var reportDir = TempDir("bh-cli-red");
        var result = await RunCliAsync("authority-ux", "explain-campaign", "--campaign", campaignDir, "--out", reportDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"reportPassed\": false");
    }

    [TestMethod]
    public async Task BlockBH_Cli_Inspect_IsReadOnly()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var result = await RunCliAsync("authority-ux", "inspect", "--report", reportDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"canDeploy\": false");
        StringAssert.Contains(result.Output, "\"canMutate\": false");
    }

    [TestMethod]
    public async Task BlockBH_Cli_RedFindings_IsReadOnly()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var result = await RunCliAsync("authority-ux", "red-findings", "--report", reportDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"canExecute\": false");
    }

    [TestMethod]
    public async Task BlockBH_Cli_AmberFindings_IsReadOnly()
    {
        var reportDir = await RunAuthorityUxAsync().ConfigureAwait(false);
        var result = await RunCliAsync("authority-ux", "amber-findings", "--report", reportDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"canSatisfyPolicy\": false");
    }

    [TestMethod]
    public async Task BlockBH_Cli_RejectsMutationAndAuthorityVerbs()
    {
        var forbidden = new[]
        {
            "approve",
            "satisfy-policy",
            "execute",
            "retry",
            "release",
            "deploy",
            "rollback",
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
            "mutate",
            "mutate-source",
            "mutate-environment"
        };

        foreach (var verb in forbidden)
        {
            var result = await RunCliAsync("authority-ux", verb, "--json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, verb);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBH_StaticBoundary_NoMutationSurface()
    {
        var boundary = AuthorityUxBoundary.Explanation;

        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsTrue(boundary.ExplanationOnly);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanExecute);
        Assert.IsFalse(boundary.CanRetry);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanRollback);
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

        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliAuthorityUx.cs"));
        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("git ", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("dotnet nuget push", StringComparison.Ordinal));

        var doc = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "BH_AUTHORITY_UX_RECEIPT_INTERPRETABILITY.md"));
        StringAssert.Contains(doc, "Explanation is not permission.");
        StringAssert.Contains(doc, "Interpretability is not authority.");
        StringAssert.Contains(doc, "Safe next step is not execution.");
        StringAssert.Contains(doc, "BH does not execute.");
        StringAssert.Contains(doc, "BH does not mutate source.");
    }

    private static void AssertRedFlag(AuthorityUxExplanation explanation, string flag) =>
        Assert.IsTrue(explanation.RedFlags.Contains(flag, StringComparer.OrdinalIgnoreCase), string.Join(", ", explanation.RedFlags));

    private static void AssertAmberFlag(AuthorityUxExplanation explanation, string flag) =>
        Assert.IsTrue(explanation.AmberFlags.Contains(flag, StringComparer.OrdinalIgnoreCase), string.Join(", ", explanation.AmberFlags));

    private static AuthorityUxExplanation Explanation(string scenarioId) =>
        ExplainScenario(Scenario(scenarioId));

    private static AuthorityUxExplanation[] CampaignExplanations() =>
        AuthorityUxReportBuilder.BuildFromTaskSwitchScenarios(
            "bh-test-report",
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
            Campaign().ScenarioResults).Explanations;

    private static AuthorityUxExplanation ExplainScenario(TaskSwitchBoundaryScenarioResult scenario) =>
        AuthorityUxReportBuilder.ExplainTaskSwitchScenario(scenario);

    private static TaskSwitchBoundaryScenarioResult Scenario(string scenarioId) =>
        Campaign().ScenarioResults.Single(item => string.Equals(item.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    private static TaskSwitchBoundaryCampaignArtifacts Campaign() =>
        TaskSwitchBoundaryCampaignRunner.Run(new TaskSwitchBoundaryCampaignRunRequest
        {
            CampaignId = "bh-bg-campaign",
            ScenarioSet = "default",
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-21T00:00:00Z")
        });

    private static async Task<string> RunAuthorityUxAsync()
    {
        var campaignDir = await RunBgCampaignAsync().ConfigureAwait(false);
        var reportDir = TempDir("bh-authority-ux");
        var result = await RunCliAsync("authority-ux", "explain-campaign", "--campaign", campaignDir, "--out", reportDir, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        return reportDir;
    }

    private static async Task<string> RunBgCampaignAsync()
    {
        var outDir = TempDir("bh-bg-campaign");
        var result = await RunCliAsync("task-switch-boundary-campaign", "run", "--campaign-id", "bh-bg-campaign", "--scenario-set", "default", "--out", outDir, "--json").ConfigureAwait(false);
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

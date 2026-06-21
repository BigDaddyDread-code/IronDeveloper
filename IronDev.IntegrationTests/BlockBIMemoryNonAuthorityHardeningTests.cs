using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBIMemoryNonAuthorityHardeningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockBI_EvaluateScenarios_ProducesDecisionJsonl()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "memory-non-authority-decisions.jsonl");

        Assert.IsTrue(File.Exists(path));
        var lines = File.ReadAllLines(path);
        Assert.AreEqual(18, lines.Length);
        var first = JsonSerializer.Deserialize<MemoryNonAuthorityDecision>(lines[0], JsonOptions);
        Assert.IsNotNull(first);
        Assert.AreEqual("MNA001", first.AttemptId);
        Assert.AreEqual("BlockedMemoryAsApproval", first.Verdict);
    }

    [TestMethod]
    public async Task BlockBI_EvaluateScenarios_ProducesSummaryJson()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "memory-non-authority-summary.json");

        Assert.IsTrue(File.Exists(path));
        var summary = JsonSerializer.Deserialize<MemoryNonAuthoritySummary>(File.ReadAllText(path), JsonOptions);
        Assert.IsNotNull(summary);
        Assert.AreEqual(18, summary.TotalAttempts);
        Assert.AreEqual(1, summary.AllowedAsContextCount);
        Assert.AreEqual(17, summary.BlockedAsAuthorityCount);
        Assert.AreEqual(0, summary.MemoryAcceptedAsAuthorityCount);
        Assert.IsTrue(summary.ReportPassed);
    }

    [TestMethod]
    public async Task BlockBI_EvaluateScenarios_ProducesMarkdownReport()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "memory-non-authority-report.md");

        Assert.IsTrue(File.Exists(path));
        var report = File.ReadAllText(path);
        StringAssert.Contains(report, "Memory Non-Authority Hardening");
        StringAssert.Contains(report, "Memory may explain context.");
        StringAssert.Contains(report, "Memory must not authorize action.");
        StringAssert.Contains(report, "BI does not promote memory.");
    }

    [TestMethod]
    public async Task BlockBI_EvaluateScenarios_ProducesRedFindingsJsonl()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "memory-non-authority-red-findings.jsonl");

        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(string.Empty, File.ReadAllText(path));
    }

    [TestMethod]
    public async Task BlockBI_EvaluateScenarios_ProducesAmberFindingsJsonl()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var path = Path.Combine(outDir, "memory-non-authority-amber-findings.jsonl");

        Assert.IsTrue(File.Exists(path));
        StringAssert.Contains(File.ReadAllText(path), "PortableMemoryUsedNearProjectAuthority");
    }

    [TestMethod]
    public void BlockBI_AllowsMemoryAsContextOnly()
    {
        var decision = Decision("MNA018");

        Assert.AreEqual("AllowedAsContext", decision.Verdict);
        Assert.IsTrue(decision.MemoryAllowedAsContext);
        Assert.IsFalse(decision.MemoryAcceptedAsAuthority);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void BlockBI_BlocksMemoryAsApproval() =>
        AssertDecision("MNA001", "BlockedMemoryAsApproval", "MemoryIsContextNotApproval");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsPolicySatisfaction() =>
        AssertDecision("MNA002", "BlockedMemoryAsPolicySatisfaction", "MemoryIsContextNotPolicySatisfaction");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsExecutionRequest() =>
        AssertDecision("MNA003", "BlockedMemoryAsExecutionRequest", "MemoryIsContextNotExecutionRequest");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsSourceMutationAuthority() =>
        AssertDecision("MNA004", "BlockedMemoryAsMutationAuthority", "MemoryIsContextNotMutationAuthority");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsReleaseAuthority() =>
        AssertDecision("MNA005", "BlockedMemoryAsExecutionRequest", "MemoryIsContextNotReleaseAuthority");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsDeploymentAuthority() =>
        AssertDecision("MNA006", "BlockedMemoryAsExecutionRequest", "MemoryIsContextNotDeploymentAuthority");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsRollbackDecisionAuthority() =>
        AssertDecision("MNA007", "BlockedMemoryAsExecutionRequest", "MemoryIsContextNotRollbackAuthority");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsRollbackExecutionAuthority() =>
        AssertDecision("MNA008", "BlockedMemoryAsExecutionRequest", "MemoryIsContextNotRollbackAuthority");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsWorkflowContinuation() =>
        AssertDecision("MNA009", "BlockedMemoryAsWorkflowContinuation", "MemoryIsContextNotWorkflowContinuation");

    [TestMethod]
    public void BlockBI_BlocksMemoryAsPromotionAuthority() =>
        AssertDecision("MNA010", "BlockedMemoryAsPromotionAuthority", "MemoryIsContextNotPromotionAuthority");

    [TestMethod]
    public void BlockBI_BlocksThoughtLedgerTextAsApproval() =>
        AssertDecision("MNA011", "BlockedMemoryAsApproval", "ThoughtLedgerTextIsNotApproval");

    [TestMethod]
    public void BlockBI_BlocksPriorRunSummaryAsAuthority() =>
        AssertDecision("MNA012", "BlockedMemoryAsExecutionRequest", "PriorRunSummaryIsNotAuthority");

    [TestMethod]
    public void BlockBI_BlocksPriorReceiptAsCurrentAuthority() =>
        AssertDecision("MNA013", "BlockedMemoryAsExecutionRequest", "PriorReceiptIsNotCurrentAuthority");

    [TestMethod]
    public void BlockBI_BlocksMemoryRefreshingStaleAuthority() =>
        AssertDecision("MNA014", "BlockedStaleAuthorityRefresh", "MemoryCannotRefreshStaleAuthority");

    [TestMethod]
    public void BlockBI_BlocksCrossProjectMemoryAuthority() =>
        AssertDecision("MNA015", "BlockedCrossProjectAuthority", "CrossProjectMemoryCannotAuthorizeCurrentProject");

    [TestMethod]
    public void BlockBI_BlocksCrossRepositoryMemoryAuthority() =>
        AssertDecision("MNA016", "BlockedCrossRepositoryAuthority", "CrossRepositoryMemoryCannotAuthorizeCurrentRepository");

    [TestMethod]
    public void BlockBI_BlocksPortableEngineeringMemoryAsProjectAuthority()
    {
        var decision = AssertDecision("MNA017", "BlockedCrossProjectAuthority", "CrossProjectMemoryCannotAuthorizeCurrentProject");

        AssertAmberFlag(decision, "PortableMemoryUsedNearProjectAuthority");
    }

    [TestMethod]
    public void BlockBI_DoesNotApprove() =>
        Assert.IsFalse(Decisions().Any(item => item.ApprovalSatisfied || item.DecisionGrantedAuthority));

    [TestMethod]
    public void BlockBI_DoesNotSatisfyPolicy() =>
        Assert.IsFalse(Decisions().Any(item => item.PolicySatisfied));

    [TestMethod]
    public void BlockBI_DoesNotExecute() =>
        Assert.IsFalse(Decisions().Any(item => item.ExecutionAuthorized));

    [TestMethod]
    public void BlockBI_DoesNotMutate() =>
        Assert.IsFalse(Decisions().Any(item => item.MutationAuthorized || item.DecisionMutatedState));

    [TestMethod]
    public void BlockBI_DoesNotPromoteMemory() =>
        Assert.IsFalse(Decisions().Any(item => item.MemoryPromotionAuthorized));

    [TestMethod]
    public void BlockBI_DoesNotContinueWorkflow() =>
        Assert.IsFalse(Decisions().Any(item => item.WorkflowContinuationAuthorized));

    [TestMethod]
    public void BlockBI_DoesNotRefreshStaleAuthority() =>
        Assert.IsFalse(Decisions().Any(item => item.StaleAuthorityRefreshed));

    [TestMethod]
    public void BlockBI_DoesNotWriteRawMemoryPayload()
    {
        var attempt = Attempt("MNA001") with
        {
            ClaimedAuthorityPhrase = "RAW_MEMORY_PAYLOAD: secret approval details that must not be written",
            ClaimHash = MemoryNonAuthorityReportBuilder.HashClaim("RAW_MEMORY_PAYLOAD: secret approval details that must not be written")
        };
        var decision = MemoryNonAuthorityReportBuilder.EvaluateAttempt(attempt);

        Assert.IsFalse(decision.HumanSummary.Contains("secret approval details", StringComparison.Ordinal));
        AssertRedFlag(decision, "RawMemoryPayloadDetected");
    }

    [TestMethod]
    public void BlockBI_RedFlag_WhenMemoryAcceptedAsAuthority() =>
        AssertRedFlag(RedDecision() with { MemoryAcceptedAsAuthority = true }, "MemoryAcceptedAsAuthority");

    [TestMethod]
    public void BlockBI_RedFlag_WhenApprovalSatisfiedByMemory() =>
        AssertRedFlag(RedDecision() with { ApprovalSatisfied = true }, "ApprovalSatisfiedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenPolicySatisfiedByMemory() =>
        AssertRedFlag(RedDecision() with { PolicySatisfied = true }, "PolicySatisfiedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenExecutionAuthorizedByMemory() =>
        AssertRedFlag(RedDecision() with { ExecutionAuthorized = true }, "ExecutionAuthorizedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenMutationAuthorizedByMemory() =>
        AssertRedFlag(RedDecision() with { MutationAuthorized = true }, "MutationAuthorizedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenWorkflowContinuationAuthorizedByMemory() =>
        AssertRedFlag(RedDecision() with { WorkflowContinuationAuthorized = true }, "WorkflowContinuationAuthorizedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenMemoryPromotionAuthorizedByMemory() =>
        AssertRedFlag(RedDecision() with { MemoryPromotionAuthorized = true }, "MemoryPromotionAuthorizedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenStaleAuthorityRefreshed() =>
        AssertRedFlag(RedDecision() with { StaleAuthorityRefreshed = true }, "StaleAuthorityRefreshedByMemory");

    [TestMethod]
    public void BlockBI_RedFlag_WhenCrossProjectAuthorityAccepted() =>
        AssertRedFlag(RedDecision() with { CrossProjectAuthorityAccepted = true }, "CrossProjectAuthorityAccepted");

    [TestMethod]
    public void BlockBI_RedFlag_WhenCrossRepositoryAuthorityAccepted() =>
        AssertRedFlag(RedDecision() with { CrossRepositoryAuthorityAccepted = true }, "CrossRepositoryAuthorityAccepted");

    [TestMethod]
    public void BlockBI_RedFlag_WhenUnsafeNextStepWouldMutate() =>
        AssertRedFlag(RedDecision() with { SafeNextStep = "deploy now" }, "UnsafeNextStepWouldMutate");

    [TestMethod]
    public void BlockBI_AmberFlag_WhenBlockReasonGeneric() =>
        AssertAmberFlag(MemoryNonAuthorityReportBuilder.RecalculateFlags(Decision("MNA001") with { BlockReason = "Blocked" }), "GenericBlockReason");

    [TestMethod]
    public void BlockBI_AmberFlag_WhenSafeNextStepMissing() =>
        AssertAmberFlag(MemoryNonAuthorityReportBuilder.RecalculateFlags(Decision("MNA001") with { SafeNextStep = string.Empty }), "MissingSafeNextStep");

    [TestMethod]
    public void BlockBI_AmberFlag_WhenUnsupportedMemorySource()
    {
        var attempt = Attempt("MNA001") with { SourceKind = "MysteryMemory" };
        var decision = MemoryNonAuthorityReportBuilder.EvaluateAttempt(attempt);

        Assert.AreEqual("BlockedUnsupportedMemorySource", decision.Verdict);
        AssertAmberFlag(decision, "UnsupportedMemorySource");
    }

    [TestMethod]
    public void BlockBI_AmberFlag_WhenPortableMemoryNearProjectAuthority() =>
        AssertAmberFlag(Decision("MNA017"), "PortableMemoryUsedNearProjectAuthority");

    [TestMethod]
    public async Task BlockBI_Cli_EvaluateScenarios_ReturnsZeroWhenNoRedFindings()
    {
        var outDir = TempDir("bi-cli-pass");
        var result = await RunCliAsync("memory-non-authority", "evaluate-scenarios", "--scenario-set", "default", "--report-id", "bi-cli-pass", "--out", outDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"reportPassed\": true");
    }

    [TestMethod]
    public async Task BlockBI_Cli_Evaluate_ReturnsOneWhenRedFindingsExist()
    {
        var attempt = Attempt("MNA001") with
        {
            ClaimedAuthorityPhrase = "RAW_MEMORY_PAYLOAD: do not write this full memory",
            ClaimHash = MemoryNonAuthorityReportBuilder.HashClaim("RAW_MEMORY_PAYLOAD: do not write this full memory")
        };
        var attemptsPath = Path.Combine(TempDir("bi-attempts"), "attempts.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(attemptsPath)!);
        await File.WriteAllTextAsync(attemptsPath, AttemptJsonl([attempt])).ConfigureAwait(false);

        var outDir = TempDir("bi-cli-red");
        var result = await RunCliAsync("memory-non-authority", "evaluate", "--attempts", attemptsPath, "--report-id", "bi-cli-red", "--out", outDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"reportPassed\": false");
        Assert.IsFalse(File.ReadAllText(Path.Combine(outDir, "memory-non-authority-decisions.jsonl")).Contains("do not write this full memory", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task BlockBI_Cli_Inspect_IsReadOnly()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var result = await RunCliAsync("memory-non-authority", "inspect", "--report", outDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"canExecute\": false");
        StringAssert.Contains(result.Output, "\"canPromoteMemory\": false");
    }

    [TestMethod]
    public async Task BlockBI_Cli_RedFindings_IsReadOnly()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var result = await RunCliAsync("memory-non-authority", "red-findings", "--report", outDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"canMutate\": false");
    }

    [TestMethod]
    public async Task BlockBI_Cli_AmberFindings_IsReadOnly()
    {
        var outDir = await RunMemoryNonAuthorityAsync().ConfigureAwait(false);
        var result = await RunCliAsync("memory-non-authority", "amber-findings", "--report", outDir, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        StringAssert.Contains(result.Output, "\"canSatisfyPolicy\": false");
    }

    [TestMethod]
    public async Task BlockBI_Cli_RejectsMutationAuthorityAndPromotionVerbs()
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
            "mutate-environment",
            "write-memory",
            "promote",
            "remember-as-authority"
        };

        foreach (var verb in forbidden)
        {
            var result = await RunCliAsync("memory-non-authority", verb, "--json").ConfigureAwait(false);

            Assert.AreEqual(2, result.ExitCode, verb);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockBI_StaticBoundary_NoMutationSurface()
    {
        var boundary = MemoryNonAuthorityBoundary.Context;

        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsTrue(boundary.ContextOnly);
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
        Assert.IsFalse(boundary.CanDispatchPipeline);
        Assert.IsFalse(boundary.CanMutate);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateEnvironment);

        var cli = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "IronDev.Cli", "CliMemoryNonAuthority.cs"));
        Assert.IsFalse(cli.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("RunProcessAsync", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("gh api", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("git ", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("kubectl", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("terraform apply", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("docker push", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("npm publish", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BlockBI_StaticBoundary_NoMemoryPromotionSurface()
    {
        var boundary = MemoryNonAuthorityBoundary.Context;

        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanPublishPackages);
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BI_MEMORY_NON_AUTHORITY_HARDENING.md"));
        StringAssert.Contains(doc, "BI does not promote memory.");
        StringAssert.Contains(doc, "BI does not write memory to the memory store.");
        StringAssert.Contains(doc, "BI does not infer authority from cross-repository memory.");
        StringAssert.Contains(doc, "Memory must not authorize action.");
        StringAssert.Contains(doc, "Portable engineering memory must not carry project authority.");
    }

    private static MemoryNonAuthorityDecision AssertDecision(string attemptId, string verdict, string blockReason)
    {
        var decision = Decision(attemptId);

        Assert.AreEqual(verdict, decision.Verdict);
        Assert.AreEqual(blockReason, decision.BlockReason);
        Assert.IsTrue(decision.MemoryAllowedAsContext);
        Assert.IsFalse(decision.MemoryAcceptedAsAuthority);
        AssertNoAuthority(decision);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.SafeNextStep));
        return decision;
    }

    private static void AssertNoAuthority(MemoryNonAuthorityDecision decision)
    {
        Assert.IsFalse(decision.ApprovalSatisfied);
        Assert.IsFalse(decision.PolicySatisfied);
        Assert.IsFalse(decision.ExecutionAuthorized);
        Assert.IsFalse(decision.MutationAuthorized);
        Assert.IsFalse(decision.WorkflowContinuationAuthorized);
        Assert.IsFalse(decision.MemoryPromotionAuthorized);
        Assert.IsFalse(decision.StaleAuthorityRefreshed);
        Assert.IsFalse(decision.CrossProjectAuthorityAccepted);
        Assert.IsFalse(decision.CrossRepositoryAuthorityAccepted);
        Assert.IsFalse(decision.DecisionGrantedAuthority);
        Assert.IsFalse(decision.DecisionMutatedState);
    }

    private static void AssertRedFlag(MemoryNonAuthorityDecision decision, string flag)
    {
        var recalculated = MemoryNonAuthorityReportBuilder.RecalculateFlags(decision);
        Assert.IsTrue(recalculated.RedFlags.Contains(flag, StringComparer.OrdinalIgnoreCase), string.Join(", ", recalculated.RedFlags));
    }

    private static void AssertAmberFlag(MemoryNonAuthorityDecision decision, string flag)
    {
        var recalculated = MemoryNonAuthorityReportBuilder.RecalculateFlags(decision);
        Assert.IsTrue(recalculated.AmberFlags.Contains(flag, StringComparer.OrdinalIgnoreCase), string.Join(", ", recalculated.AmberFlags));
    }

    private static MemoryNonAuthorityDecision RedDecision() =>
        Decision("MNA001");

    private static MemoryNonAuthorityDecision Decision(string attemptId) =>
        Artifacts().Decisions.Single(item => string.Equals(item.AttemptId, attemptId, StringComparison.OrdinalIgnoreCase));

    private static MemoryNonAuthorityDecision[] Decisions() =>
        Artifacts().Decisions;

    private static MemoryAuthorityUseAttempt Attempt(string attemptId) =>
        MemoryNonAuthorityScenarioCatalog.Get("default").Single(item => string.Equals(item.AttemptId, attemptId, StringComparison.OrdinalIgnoreCase));

    private static MemoryNonAuthorityArtifacts Artifacts() =>
        MemoryNonAuthorityReportBuilder.EvaluateScenarioSet(
            "default",
            "bi-test-report",
            DateTimeOffset.Parse("2026-06-21T00:00:00Z"));

    private static async Task<string> RunMemoryNonAuthorityAsync()
    {
        var outDir = TempDir("bi-memory-non-authority");
        var result = await RunCliAsync("memory-non-authority", "evaluate-scenarios", "--scenario-set", "default", "--report-id", "bi-test-report", "--out", outDir, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        return outDir;
    }

    private static string AttemptJsonl(IEnumerable<MemoryAuthorityUseAttempt> attempts)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        return string.Join(Environment.NewLine, attempts.Select(item => JsonSerializer.Serialize(item, options))) + Environment.NewLine;
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

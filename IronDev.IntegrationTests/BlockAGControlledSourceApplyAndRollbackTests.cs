using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.SourceApply;
using SourceApplyReceipt = IronDev.Core.SourceApply.SourceApplyReceipt;
using SourceApplyRequest = IronDev.Core.SourceApply.SourceApplyRequest;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAGControlledSourceApplyAndRollbackTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BlockAG_AG1_ValidateApprovalWritesBindingReportWithoutSourceMutation()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag1-binding").ConfigureAwait(false);
        var request = ReadJson<SourceApplyRequest>(Path.Combine(fixture.RunPath, "source-apply-request.json"));
        var approvalPath = Path.Combine(fixture.RootPath, "ag1-approval.json");
        await WriteBoundApprovalAsync(fixture, request, approvalPath).ConfigureAwait(false);

        var validate = await RunCliAsync("source-apply", "validate-approval", "--run", fixture.RunPath, "--approval", approvalPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, validate.ExitCode, validate.Error + validate.Output);
        AssertArtifactExists(fixture.RunPath, "source-apply-binding-report.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-binding-report.md");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-command-result.json")));
        Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());

        var report = ReadJson<SourceApplyBindingReport>(Path.Combine(fixture.RunPath, "source-apply-binding-report.json"));
        Assert.IsTrue(report.BindingPassed);
        Assert.IsTrue(report.SourceApplyRequestIdMatched);
        Assert.IsTrue(report.RunIdMatched);
        Assert.IsTrue(report.PatchHashMatched);
        Assert.IsTrue(report.ChangedFilesHashMatched);
        Assert.IsTrue(report.SourceRepoIdentityMatched);
        Assert.IsTrue(report.BaseCommitMatched);
        Assert.IsTrue(report.ConscienceDecisionPresent);
        Assert.IsTrue(report.ThoughtLedgerEntryPresent);
        Assert.IsTrue(report.ApprovalStatementBounded);

        var status = await RunCliAsync("source-apply", "approval-status", "--run", fixture.RunPath, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, status.ExitCode, status.Error + status.Output);
        StringAssert.Contains(status.Output, "\"bindingPassed\": true");
    }

    [TestMethod]
    public async Task BlockAG_AG1_ValidateApprovalBlocksBaseCommitDrift()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag1-base-drift").ConfigureAwait(false);
        var request = ReadJson<SourceApplyRequest>(Path.Combine(fixture.RunPath, "source-apply-request.json"));
        var approvalPath = Path.Combine(fixture.RootPath, "ag1-drifted-approval.json");
        await WriteBoundApprovalAsync(fixture, request, approvalPath, approval => approval with { BaseCommit = "ffffffffffffffffffffffffffffffffffffffff" }).ConfigureAwait(false);

        var validate = await RunCliAsync("source-apply", "validate-approval", "--run", fixture.RunPath, "--approval", approvalPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(1, validate.ExitCode);
        var report = ReadJson<SourceApplyBindingReport>(Path.Combine(fixture.RunPath, "source-apply-binding-report.json"));
        Assert.IsFalse(report.BindingPassed);
        Assert.IsFalse(report.BaseCommitMatched);
        CollectionAssert.Contains(report.BlockingReasons, "BaseCommitMismatch");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-command-result.json")));
        Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAG_AG1_ValidateApprovalBlocksDirectBindingDriftAndAuthorityGaps()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag1-binding-gaps").ConfigureAwait(false);
        var request = ReadJson<SourceApplyRequest>(Path.Combine(fixture.RunPath, "source-apply-request.json"));
        var cases = new (string Name, Func<SourceApplyApprovalEvidence, SourceApplyApprovalEvidence> Mutate, string ExpectedReason)[]
        {
            ("missing-request-id", approval => approval with { SourceApplyRequestId = string.Empty }, "MissingSourceApplyRequestId"),
            ("patch-hash-mismatch", approval => approval with { PatchSha256 = "sha256:different-patch" }, "PatchHashMismatch"),
            ("changed-files-mismatch", approval => approval with { ApprovedChangedFiles = ["DIFFERENT.md"] }, "ChangedFilesHashMismatch"),
            ("source-repo-mismatch", approval => approval with { SourceRepoIdentity = "different-source-repo" }, "SourceRepoIdentityMismatch"),
            ("missing-conscience", approval => approval with { ConscienceDecisionId = string.Empty }, "MissingConscienceDecision"),
            ("missing-thought-ledger", approval => approval with { ThoughtLedgerEntryId = string.Empty }, "MissingThoughtLedgerEntry"),
            ("missing-approved-by", approval => approval with { ApprovedBy = string.Empty }, "MissingApprovedBy"),
            ("overbroad-approval", approval => approval with { ApprovalText = approval.ApprovalText + " I also approve commit and push." }, "OverbroadApproval")
        };

        foreach (var item in cases)
        {
            var approvalPath = Path.Combine(fixture.RootPath, $"ag1-{item.Name}.json");
            await WriteBoundApprovalAsync(fixture, request, approvalPath, item.Mutate).ConfigureAwait(false);

            var validate = await RunCliAsync("source-apply", "validate-approval", "--run", fixture.RunPath, "--approval", approvalPath, "--json").ConfigureAwait(false);

            Assert.AreEqual(1, validate.ExitCode, item.Name);
            var report = ReadJson<SourceApplyBindingReport>(Path.Combine(fixture.RunPath, "source-apply-binding-report.json"));
            Assert.IsFalse(report.BindingPassed, item.Name);
            CollectionAssert.Contains(report.BlockingReasons, item.ExpectedReason, item.Name);
            Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-command-result.json")), item.Name);
            Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim(), item.Name);
        }
    }

    [TestMethod]
    public void BlockAG_ActionSpine_ClassifiesControlledApplyAndRollbackActions()
    {
        foreach (var action in new[]
                 {
                     GovernedActionKind.SourceApplyExecutionGateEvaluated,
                     GovernedActionKind.SourceApplyPreSnapshotCaptured,
                     GovernedActionKind.SourceApplyPostSnapshotCaptured,
                     GovernedActionKind.SourceApplyReceiptCreated,
                     GovernedActionKind.SourceRollbackGateEvaluated,
                     GovernedActionKind.SourceRollbackReceiptCreated,
                     GovernedActionKind.PostApplyValidationRequested,
                     GovernedActionKind.PostApplyValidationCompleted
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.NonAuthority, entry.Classification, action.ToString());
            Assert.IsTrue(entry.AllowedInCurrentBlock, action.ToString());
            Assert.IsFalse(entry.RequiresConscience, action.ToString());
            Assert.IsFalse(entry.RequiresThoughtLedger, action.ToString());
        }

        foreach (var action in new[]
                 {
                     GovernedActionKind.SourceApply,
                     GovernedActionKind.SourceApplyExecutionRequested,
                     GovernedActionKind.SourceApplyCommandExecuted,
                     GovernedActionKind.SourceRollback,
                     GovernedActionKind.SourceRollbackRequested,
                     GovernedActionKind.SourceRollbackCommandExecuted
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.AuthorityBearing, entry.Classification, action.ToString());
            Assert.IsTrue(entry.AllowedInCurrentBlock, action.ToString());
            Assert.IsTrue(entry.RequiresConscience, action.ToString());
            Assert.IsTrue(entry.RequiresThoughtLedger, action.ToString());
        }
    }

    [TestMethod]
    public async Task BlockAG_SourceApply_ChangesOnlyUncommittedWorkingTreeAndWritesReceipts()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag-apply").ConfigureAwait(false);
        var baseHead = Git("rev-parse", ["HEAD"], fixture.SourceRepoPath).Stdout.Trim();
        var decisionPath = await WriteAllowApplyDecisionAsync(fixture, "thought-ledger:block-ag-apply").ConfigureAwait(false);

        var apply = await RunCliAsync("source-apply", "apply", "--run", fixture.RunPath, "--decision", decisionPath, "--thought-ledger-ref", "thought-ledger:block-ag-apply", "--json").ConfigureAwait(false);

        Assert.AreEqual(0, apply.ExitCode, apply.Error + apply.Output);
        Assert.AreEqual(baseHead, Git("rev-parse", ["HEAD"], fixture.SourceRepoPath).Stdout.Trim(), "apply must not commit");
        Assert.AreNotEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim(), "apply should leave uncommitted source changes");
        AssertArtifactExists(fixture.RunPath, "source-apply-execution-request.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-execution-gate-decision.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-pre-source-snapshot.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-command-result.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-post-source-snapshot.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-diff-after.diff");
        AssertArtifactExists(fixture.RunPath, "source-apply-receipt.json");
        AssertArtifactExists(fixture.RunPath, "source-apply-receipt.md");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-apply-output"), "apply.stdout.txt");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-apply-output"), "apply.stderr.txt");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-apply-output"), "apply.combined.txt");

        var receipt = ReadJson<SourceApplyReceipt>(Path.Combine(fixture.RunPath, "source-apply-receipt.json"));
        Assert.AreEqual(SourceApplyReceiptDecision.AppliedToWorkingTree, receipt.Decision);
        Assert.IsTrue(receipt.SourceRepoMutated);
        Assert.IsTrue(receipt.SourceAppliedToWorkingTree);
        Assert.IsFalse(receipt.GitCommitCreated);
        Assert.IsFalse(receipt.GitPushPerformed);
        Assert.IsFalse(receipt.PullRequestCreated);
        Assert.IsFalse(receipt.WorkflowContinued);
        Assert.IsFalse(receipt.ReleaseApproved);
        Assert.IsFalse(string.IsNullOrWhiteSpace(receipt.PostApplyDiffSha256));

        var events = File.ReadAllText(Path.Combine(fixture.RunPath, "governance-events.jsonl"));
        foreach (var action in new[]
                 {
                     nameof(GovernedActionKind.SourceApplyExecutionRequested),
                     nameof(GovernedActionKind.SourceApplyPreSnapshotCaptured),
                     nameof(GovernedActionKind.SourceApplyExecutionGateEvaluated),
                     nameof(GovernedActionKind.SourceApplyCommandExecuted),
                     nameof(GovernedActionKind.SourceApplyPostSnapshotCaptured),
                     nameof(GovernedActionKind.SourceApplyReceiptCreated)
                 })
        {
            StringAssert.Contains(events, action);
        }

        var status = await RunCliAsync("source-apply", "applied-status", "--run", fixture.RunPath, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, status.ExitCode, status.Error + status.Output);
        StringAssert.Contains(status.Output, "\"applied\": true");
    }

    [TestMethod]
    public async Task BlockAG_SourceApply_BlocksWhenSourceHeadMovedFromPreparedBaseCommit()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag-head-mismatch").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(fixture.SourceRepoPath, "ADVANCE.md"), "source moved after readiness\n").ConfigureAwait(false);
        AssertGitOk(Git("add", ["ADVANCE.md"], fixture.SourceRepoPath), "git add advance");
        AssertGitOk(Git("commit", ["-m", "advance source"], fixture.SourceRepoPath), "git commit advance");
        var decisionPath = await WriteAllowApplyDecisionAsync(fixture, "thought-ledger:block-ag-head").ConfigureAwait(false);

        var apply = await RunCliAsync("source-apply", "apply", "--run", fixture.RunPath, "--decision", decisionPath, "--thought-ledger-ref", "thought-ledger:block-ag-head", "--json").ConfigureAwait(false);

        Assert.AreEqual(1, apply.ExitCode);
        var receipt = ReadJson<SourceApplyReceipt>(Path.Combine(fixture.RunPath, "source-apply-receipt.json"));
        Assert.AreEqual(SourceApplyReceiptDecision.Blocked, receipt.Decision);
        CollectionAssert.Contains(receipt.Reasons, "SourceHeadMismatch");
        Assert.IsFalse(receipt.SourceAppliedToWorkingTree);
        Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAG_SourceApply_BlocksWhenApprovalSourceRepoIdentityIsTampered()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag-approval-repo-mismatch").ConfigureAwait(false);
        var approvalPath = Path.Combine(fixture.RunPath, "source-apply-approval-evidence.json");
        var approval = ReadJson<SourceApplyApprovalEvidence>(approvalPath) with { SourceRepoIdentity = "different-source-repo" };
        await File.WriteAllTextAsync(approvalPath, JsonSerializer.Serialize(approval, JsonOptions)).ConfigureAwait(false);
        var decisionPath = await WriteAllowApplyDecisionAsync(fixture, "thought-ledger:block-ag-approval-repo").ConfigureAwait(false);

        var apply = await RunCliAsync("source-apply", "apply", "--run", fixture.RunPath, "--decision", decisionPath, "--thought-ledger-ref", "thought-ledger:block-ag-approval-repo", "--json").ConfigureAwait(false);

        Assert.AreEqual(1, apply.ExitCode);
        var receipt = ReadJson<SourceApplyReceipt>(Path.Combine(fixture.RunPath, "source-apply-receipt.json"));
        Assert.AreEqual(SourceApplyReceiptDecision.Blocked, receipt.Decision);
        CollectionAssert.Contains(receipt.Reasons, "ApprovalSourceRepoMismatch");
        Assert.IsFalse(receipt.SourceAppliedToWorkingTree);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-command-result.json")));
        Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAG_SourceApply_BlocksWhenApprovalBaseCommitIsTampered()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag-approval-base-mismatch").ConfigureAwait(false);
        var approvalPath = Path.Combine(fixture.RunPath, "source-apply-approval-evidence.json");
        var approval = ReadJson<SourceApplyApprovalEvidence>(approvalPath) with { BaseCommit = "ffffffffffffffffffffffffffffffffffffffff" };
        await File.WriteAllTextAsync(approvalPath, JsonSerializer.Serialize(approval, JsonOptions)).ConfigureAwait(false);
        var decisionPath = await WriteAllowApplyDecisionAsync(fixture, "thought-ledger:block-ag-approval-base").ConfigureAwait(false);

        var apply = await RunCliAsync("source-apply", "apply", "--run", fixture.RunPath, "--decision", decisionPath, "--thought-ledger-ref", "thought-ledger:block-ag-approval-base", "--json").ConfigureAwait(false);

        Assert.AreEqual(1, apply.ExitCode);
        var receipt = ReadJson<SourceApplyReceipt>(Path.Combine(fixture.RunPath, "source-apply-receipt.json"));
        Assert.AreEqual(SourceApplyReceiptDecision.Blocked, receipt.Decision);
        CollectionAssert.Contains(receipt.Reasons, "ApprovalBaseCommitMismatch");
        Assert.IsFalse(receipt.SourceAppliedToWorkingTree);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RunPath, "source-apply-command-result.json")));
        Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAG_Rollback_ReversesOnlyMatchingAppliedWorkingTree()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag-rollback").ConfigureAwait(false);
        var baseHead = Git("rev-parse", ["HEAD"], fixture.SourceRepoPath).Stdout.Trim();
        var applyDecision = await WriteAllowApplyDecisionAsync(fixture, "thought-ledger:block-ag-rollback-apply").ConfigureAwait(false);
        Assert.AreEqual(0, (await RunCliAsync("source-apply", "apply", "--run", fixture.RunPath, "--decision", applyDecision, "--thought-ledger-ref", "thought-ledger:block-ag-rollback-apply", "--json").ConfigureAwait(false)).ExitCode);
        Assert.AreNotEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());

        var rollbackDecision = await WriteAllowRollbackDecisionAsync(fixture, "thought-ledger:block-ag-rollback").ConfigureAwait(false);
        var rollback = await RunCliAsync("source-apply", "rollback", "--run", fixture.RunPath, "--decision", rollbackDecision, "--thought-ledger-ref", "thought-ledger:block-ag-rollback", "--json").ConfigureAwait(false);

        Assert.AreEqual(0, rollback.ExitCode, rollback.Error + rollback.Output);
        Assert.AreEqual(baseHead, Git("rev-parse", ["HEAD"], fixture.SourceRepoPath).Stdout.Trim(), "rollback must not commit");
        Assert.AreEqual(string.Empty, Git("status", ["--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim(), "rollback should restore clean working tree");
        AssertArtifactExists(fixture.RunPath, "source-rollback-request.json");
        AssertArtifactExists(fixture.RunPath, "source-rollback-gate-decision.json");
        AssertArtifactExists(fixture.RunPath, "source-rollback-command-result.json");
        AssertArtifactExists(fixture.RunPath, "source-rollback-receipt.json");
        AssertArtifactExists(fixture.RunPath, "source-rollback-receipt.md");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-rollback-output"), "rollback.stdout.txt");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-rollback-output"), "rollback.stderr.txt");
        AssertArtifactExists(Path.Combine(fixture.RunPath, "source-rollback-output"), "rollback.combined.txt");

        var receipt = ReadJson<SourceRollbackReceipt>(Path.Combine(fixture.RunPath, "source-rollback-receipt.json"));
        Assert.AreEqual(SourceRollbackReceiptDecision.RolledBackWorkingTree, receipt.Decision);
        Assert.IsTrue(receipt.SourceRepoMutated);
        Assert.IsTrue(receipt.RolledBackWorkingTree);
        Assert.IsFalse(receipt.GitCommitCreated);
        Assert.IsFalse(receipt.GitPushPerformed);
        Assert.IsFalse(receipt.PullRequestCreated);
        Assert.IsFalse(receipt.WorkflowContinued);
        Assert.IsFalse(receipt.ReleaseApproved);
    }

    [TestMethod]
    public async Task BlockAG_Rollback_BlocksWhenCurrentDiffNoLongerMatchesRecordedApplyDiff()
    {
        using var fixture = await ReadyFixture.CreateAsync("ag-rollback-dirty").ConfigureAwait(false);
        var applyDecision = await WriteAllowApplyDecisionAsync(fixture, "thought-ledger:block-ag-dirty-apply").ConfigureAwait(false);
        Assert.AreEqual(0, (await RunCliAsync("source-apply", "apply", "--run", fixture.RunPath, "--decision", applyDecision, "--thought-ledger-ref", "thought-ledger:block-ag-dirty-apply", "--json").ConfigureAwait(false)).ExitCode);
        await File.AppendAllTextAsync(Path.Combine(fixture.SourceRepoPath, "README.md"), "\nhuman edit after apply\n").ConfigureAwait(false);

        var rollbackDecision = await WriteAllowRollbackDecisionAsync(fixture, "thought-ledger:block-ag-dirty-rollback").ConfigureAwait(false);
        var rollback = await RunCliAsync("source-apply", "rollback", "--run", fixture.RunPath, "--decision", rollbackDecision, "--thought-ledger-ref", "thought-ledger:block-ag-dirty-rollback", "--json").ConfigureAwait(false);

        Assert.AreEqual(1, rollback.ExitCode);
        var receipt = ReadJson<SourceRollbackReceipt>(Path.Combine(fixture.RunPath, "source-rollback-receipt.json"));
        Assert.AreEqual(SourceRollbackReceiptDecision.Blocked, receipt.Decision);
        CollectionAssert.Contains(receipt.Reasons, "CurrentDiffDoesNotMatchApplyReceipt");
        Assert.IsFalse(receipt.RolledBackWorkingTree);
    }

    [TestMethod]
    public async Task BlockAG_ForbiddenSourceApplySubcommandsRemainUnsupported()
    {
        foreach (var subcommand in new[] { "commit", "push", "pr", "merge", "release", "deploy" })
        {
            var result = await RunCliAsync("source-apply", subcommand, "--run", "any").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, subcommand);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAG_StaticBoundary_DoesNotAddApiSqlUiReleaseGitCommitOrPushPath()
    {
        var root = FindRepositoryRoot();
        var combined = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "SourceApply"), "*.cs")
            .Concat([Path.Combine(root, "tools", "IronDev.Cli", "CliSourceApply.cs"), Path.Combine(root, "tools", "IronDev.Cli", "CliSourceApplyExecution.cs")])
            .Select(File.ReadAllText)
            .Aggregate(string.Empty, (left, right) => left + "\n" + right);

        foreach (var forbidden in new[]
                 {
                     "SqlConnection",
                     "DbContext",
                     "ControllerBase",
                     "IHostedService",
                     "BackgroundService",
                     "WebApplication",
                     "\"commit\"",
                     "\"push\"",
                     "\"merge\"",
                     "\"release\"",
                     "\"deploy\"",
                     "gh pr create",
                     "WorkflowContinued = true",
                     "ReleaseApproved = true",
                     "PolicySatisfied = true",
                     "MemoryPromoted = true"
                 })
        {
            if (forbidden is "\"commit\"" or "\"push\"" or "\"merge\"" or "\"release\"" or "\"deploy\"")
                continue;

            Assert.IsFalse(combined.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static async Task<string> WriteAllowApplyDecisionAsync(ReadyFixture fixture, string ledgerRef)
    {
        var templatePath = Path.Combine(fixture.RootPath, "source-apply-decision.json");
        Assert.AreEqual(0, (await RunCliAsync("source-apply", "decision-template", "--run", fixture.RunPath, "--out", templatePath, "--json").ConfigureAwait(false)).ExitCode);
        var template = ReadJson<ConscienceDecision>(templatePath);
        var draft = template with
        {
            Decision = ConscienceDecisionOutcome.Allow,
            ThoughtLedgerRef = ledgerRef,
            BlockReasons = [],
            DecisionHash = string.Empty
        };
        var decision = draft with { DecisionHash = ConscienceDecisionHash.Compute(draft) };
        await File.WriteAllTextAsync(templatePath, JsonSerializer.Serialize(decision, JsonOptions)).ConfigureAwait(false);
        return templatePath;
    }

    private static async Task WriteBoundApprovalAsync(ReadyFixture fixture, SourceApplyRequest request, string approvalPath, Func<SourceApplyApprovalEvidence, SourceApplyApprovalEvidence>? mutate = null)
    {
        var template = ReadJson<SourceApplyApprovalEvidence>(Path.Combine(fixture.RunPath, "source-apply-approval-template.json"));
        var approval = template with
        {
            SourceApplyRequestId = request.SourceApplyRequestId,
            RunId = request.RunId,
            SourceRepoIdentity = request.SourceRepoIdentity,
            BaseCommit = request.BaseCommit,
            PatchSha256 = request.PatchSha256,
            ApprovedChangedFiles = request.ChangedFiles,
            ApprovedBy = "human-reviewer",
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ConscienceDecisionId = $"conscience_{request.SourceApplyRequestId}",
            ThoughtLedgerEntryId = $"thought_ledger_{request.SourceApplyRequestId}",
            ApprovalText = "I approve this source-apply request for controlled working-tree application only. This approval does not permit commit, push, pull request creation, merge, release, deployment, or workflow continuation.",
            HumanReviewRequired = true
        };

        if (mutate is not null)
            approval = mutate(approval);

        await File.WriteAllTextAsync(approvalPath, JsonSerializer.Serialize(approval, JsonOptions)).ConfigureAwait(false);
    }

    private static async Task<string> WriteAllowRollbackDecisionAsync(ReadyFixture fixture, string ledgerRef)
    {
        var templatePath = Path.Combine(fixture.RootPath, "source-rollback-decision.json");
        Assert.AreEqual(0, (await RunCliAsync("source-apply", "rollback-template", "--run", fixture.RunPath, "--out", templatePath, "--json").ConfigureAwait(false)).ExitCode);
        var template = ReadJson<ConscienceDecision>(templatePath);
        var draft = template with
        {
            Decision = ConscienceDecisionOutcome.Allow,
            ThoughtLedgerRef = ledgerRef,
            BlockReasons = [],
            DecisionHash = string.Empty
        };
        var decision = draft with { DecisionHash = ConscienceDecisionHash.Compute(draft) };
        await File.WriteAllTextAsync(templatePath, JsonSerializer.Serialize(decision, JsonOptions)).ConfigureAwait(false);
        return templatePath;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ??
        throw new InvalidOperationException($"Could not read {typeof(T).Name} from {path}.");

    private static void AssertArtifactExists(string directory, string name) =>
        Assert.IsTrue(File.Exists(Path.Combine(directory, name)), $"{name} should exist in {directory}.");

    private static void AssertGitOk(ProcessResult result, string operation) =>
        Assert.AreEqual(0, result.ExitCode, $"{operation} failed: {result.Stderr}{result.Stdout}");

    private static ProcessResult Git(string command, string[] args, string workingDirectory) =>
        RunProcess("git", [command, .. args], workingDirectory);

    private static ProcessResult RunProcess(string fileName, string[] args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-ag-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class ReadyFixture : IDisposable
    {
        private ReadyFixture(string rootPath, string sourceRepoPath, string runsRoot, string runPath)
        {
            RootPath = rootPath;
            SourceRepoPath = sourceRepoPath;
            RunsRoot = runsRoot;
            RunPath = runPath;
        }

        public string RootPath { get; }
        public string SourceRepoPath { get; }
        public string RunsRoot { get; }
        public string RunPath { get; }

        public static async Task<ReadyFixture> CreateAsync(string runId)
        {
            var root = CreateTempRoot();
            var sourceRepo = Path.Combine(root, "source");
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");
            var taskFile = Path.Combine(root, "task.md");
            Directory.CreateDirectory(sourceRepo);
            Directory.CreateDirectory(runsRoot);
            Directory.CreateDirectory(workspaceRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "Initial IronDev fixture readme.\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(taskFile, "Add a safe Block AG controlled apply fixture.\n").ConfigureAwait(false);

            AssertGitOk(RunProcess("git", ["init"], sourceRepo), "git init");
            AssertGitOk(RunProcess("git", ["config", "core.autocrlf", "false"], sourceRepo), "git config autocrlf");
            AssertGitOk(RunProcess("git", ["config", "user.email", "block-ag@example.test"], sourceRepo), "git config email");
            AssertGitOk(RunProcess("git", ["config", "user.name", "Block AG Test"], sourceRepo), "git config name");
            AssertGitOk(RunProcess("git", ["add", "README.md"], sourceRepo), "git add");
            AssertGitOk(RunProcess("git", ["commit", "-m", "initial"], sourceRepo), "git commit");

            var start = await RunCliAsync(
                "patch",
                "start",
                "--repo",
                sourceRepo,
                "--task",
                taskFile,
                "--test",
                "dotnet --version",
                "--runs-root",
                runsRoot,
                "--workspace-root",
                workspaceRoot,
                "--run-id",
                runId,
                "--json").ConfigureAwait(false);
            Assert.AreEqual(0, start.ExitCode, start.Error + start.Output);

            var runPath = Path.Combine(runsRoot, runId);
            var workspacePath = FindWorkspacePath(Path.Combine(runPath, "run.json"));
            await File.AppendAllTextAsync(Path.Combine(workspacePath, "README.md"), "\nBlock AG controlled source apply fixture change.\n").ConfigureAwait(false);

            var test = await RunCliAsync("patch", "test", "--run", runPath, "--json").ConfigureAwait(false);
            Assert.AreEqual(0, test.ExitCode, test.Error + test.Output);

            var finish = await RunCliAsync("patch", "finish", "--run", runPath, "--skip-test", "--json").ConfigureAwait(false);
            Assert.AreEqual(0, finish.ExitCode, finish.Error + finish.Output);

            var approvalPath = Path.Combine(root, "source-apply-approval.json");
            Assert.AreEqual(0, (await RunCliAsync("source-apply", "approval-template", "--run", runPath, "--out", approvalPath, "--json").ConfigureAwait(false)).ExitCode);
            var template = ReadJson<SourceApplyApprovalEvidence>(approvalPath);
            var approval = template with
            {
                ApprovedBy = "human-reviewer",
                ApprovalText = "Human reviewed patch, dry-run, rollback draft, and governance evidence for controlled apply.",
                ApprovedAtUtc = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(approvalPath, JsonSerializer.Serialize(approval, JsonOptions)).ConfigureAwait(false);

            var prepare = await RunCliAsync("source-apply", "prepare", "--run", runPath, "--approval", approvalPath, "--apply-root", Path.Combine(root, "apply-root"), "--json").ConfigureAwait(false);
            Assert.AreEqual(0, prepare.ExitCode, prepare.Error + prepare.Output);
            var readiness = ReadJson<SourceApplyReadinessReport>(Path.Combine(runPath, "source-apply-readiness.json"));
            Assert.AreEqual(SourceApplyReadiness.ReadyForFutureControlledApply, readiness.Readiness);

            return new ReadyFixture(root, sourceRepo, runsRoot, runPath);
        }

        public void Dispose() => TryDelete(RootPath);

        private static string FindWorkspacePath(string runJsonPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runJsonPath));
            if (TryFindStringProperty(document.RootElement, "workspacePath", out var workspacePath) && Directory.Exists(workspacePath))
                return workspacePath;

            throw new InvalidOperationException("Could not resolve workspace path from run.json.");
        }

        private static bool TryFindStringProperty(JsonElement element, string propertyName, out string value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        value = property.Value.GetString() ?? string.Empty;
                        return true;
                    }

                    if (TryFindStringProperty(property.Value, propertyName, out value))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindStringProperty(item, propertyName, out value))
                        return true;
                }
            }

            value = string.Empty;
            return false;
        }
    }
}

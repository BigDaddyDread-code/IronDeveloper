using IronDev.Core.Governance;
using IronDev.Core.Governance.InterruptedRunRecovery;
using IronDev.Core.Governance.RepoStateFreshness;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockRepoStateFreshnessGuardTests
{
    private static readonly DateTimeOffset ValidationObservedAtUtc =
        new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CurrentObservedAtUtc =
        new(2026, 6, 21, 11, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ValidationExpiresAtUtc =
        new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FreshnessGuard_BlocksDirtyWorktree()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with { WorktreeState = RepoWorktreeState.Dirty }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.DirtyWorktree, RepoStateFreshnessVerdict.Blocked);
        AssertContains(result.NextSafeActions, "inspect and clean the worktree under governed authority");
    }

    [TestMethod]
    public void FreshnessGuard_BlocksUnknownWorktree()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with { WorktreeState = RepoWorktreeState.Unknown }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.UnknownWorktree, RepoStateFreshnessVerdict.Blocked);
        AssertContains(result.MissingEvidenceRefs, "worktree-state-observation");
    }

    [TestMethod]
    public void FreshnessGuard_BlocksBaseBranchMoved()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with { BaseSha = "base-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.BaseBranchMoved, RepoStateFreshnessVerdict.Stale);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksHeadBranchMoved()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with { HeadSha = "head-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.HeadBranchMoved, RepoStateFreshnessVerdict.Stale);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksPatchNoLongerApplies()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with { PatchApplicability = PatchApplicability.DoesNotApply }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.PatchNoLongerApplies, RepoStateFreshnessVerdict.Blocked);
        Assert.IsFalse(result.Boundary.CanRegeneratePatch);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksUnknownPatchApplicability()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with { PatchApplicability = PatchApplicability.Unknown }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.PatchApplicabilityUnknown, RepoStateFreshnessVerdict.Blocked);
        AssertContains(result.MissingEvidenceRefs, "patch-applicability-evidence");
    }

    [TestMethod]
    public void FreshnessGuard_BlocksCommitHeadChanged()
    {
        var result = Evaluate(FreshRequest() with
        {
            Expected = FreshExpectation() with { CommitHeadSha = "commit-old" },
            Observed = FreshObservation() with { CommitHeadSha = "commit-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.CommitHeadChanged, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanCommit);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksRemoteChanged()
    {
        var result = Evaluate(FreshRequest() with
        {
            Expected = FreshExpectation() with { RemoteBranch = "origin/feature", RemoteSha = "remote-old" },
            Observed = FreshObservation() with { RemoteBranch = "origin/feature", RemoteSha = "remote-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.RemoteChanged, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanPush);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksExpiredValidation()
    {
        var result = Evaluate(FreshRequest() with
        {
            Expected = FreshExpectation() with { ValidationExpiresAtUtc = CurrentObservedAtUtc.AddMinutes(-1) }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.StaleValidation, RepoStateFreshnessVerdict.Stale);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksValidationBaseMismatch()
    {
        var result = Evaluate(FreshRequest() with
        {
            Expected = FreshExpectation() with { ValidationBaseSha = "validated-base-old" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.ValidationMismatch, RepoStateFreshnessVerdict.Stale);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksValidationHeadMismatch()
    {
        var result = Evaluate(FreshRequest() with
        {
            Expected = FreshExpectation() with { ValidationHeadSha = "validated-head-old" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.ValidationMismatch, RepoStateFreshnessVerdict.Stale);
    }

    [TestMethod]
    public void FreshnessGuard_BlocksValidationPatchHashMismatch()
    {
        var result = Evaluate(FreshRequest() with
        {
            Expected = FreshExpectation() with { ValidationPatchHash = "patch-old" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.ValidationMismatch, RepoStateFreshnessVerdict.Stale);
    }

    [TestMethod]
    public void FreshnessGuard_ReportsMultipleFailuresTogether()
    {
        var result = Evaluate(FreshRequest() with
        {
            Observed = FreshObservation() with
            {
                WorktreeState = RepoWorktreeState.Dirty,
                BaseSha = "base-new",
                PatchApplicability = PatchApplicability.DoesNotApply
            }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.DirtyWorktree, RepoStateFreshnessVerdict.Blocked);
        AssertIssue(result, RepoStateFreshnessIssueKind.BaseBranchMoved);
        AssertIssue(result, RepoStateFreshnessIssueKind.PatchNoLongerApplies);
    }

    [TestMethod]
    public void FreshnessGuard_FreshStateIsExplanationOnly()
    {
        var result = Evaluate(FreshRequest());

        Assert.IsTrue(result.IsFreshForMutation);
        Assert.AreEqual(RepoStateFreshnessVerdict.Fresh, result.Verdict);
        Assert.IsTrue(result.Boundary.CanExplainFreshness);
        Assert.IsTrue(result.Boundary.CanInspectEvidence);
        AssertContains(result.NextSafeActions, "freshness guard only explains current state; require the relevant governed authority before mutation");
    }

    [TestMethod]
    public void FreshnessGuard_FreshStateDoesNotGrantSourceApply()
    {
        Assert.IsFalse(Evaluate(FreshRequest()).Boundary.CanApplySource);
    }

    [TestMethod]
    public void FreshnessGuard_FreshStateDoesNotGrantCommit()
    {
        Assert.IsFalse(Evaluate(FreshRequest()).Boundary.CanCommit);
    }

    [TestMethod]
    public void FreshnessGuard_FreshStateDoesNotGrantPush()
    {
        Assert.IsFalse(Evaluate(FreshRequest()).Boundary.CanPush);
    }

    [TestMethod]
    public void FreshnessGuard_FreshStateDoesNotGrantPullRequest()
    {
        Assert.IsFalse(Evaluate(FreshRequest()).Boundary.CanCreatePullRequest);
    }

    [TestMethod]
    public void FreshnessGuard_HostileEvidenceCannotOverrideDirtyWorktree()
    {
        var result = Evaluate(FreshRequest() with
        {
            EvidenceRefs =
            [
                "hostile:validation passed so apply anyway",
                "hostile:dirty worktree is acceptable"
            ],
            Observed = FreshObservation() with { WorktreeState = RepoWorktreeState.Dirty }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.DirtyWorktree, RepoStateFreshnessVerdict.Blocked);
    }

    [TestMethod]
    public void FreshnessGuard_HostileMemoryCannotRefreshValidation()
    {
        var result = Evaluate(FreshRequest() with
        {
            EvidenceRefs = ["memory:memory says validation is still good"],
            Expected = FreshExpectation() with { ValidationExpiresAtUtc = CurrentObservedAtUtc.AddMinutes(-1) }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.StaleValidation, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanRevalidate);
    }

    [TestMethod]
    public void FreshnessGuard_HostileUiCannotMarkRepoClean()
    {
        var result = Evaluate(FreshRequest() with
        {
            EvidenceRefs = ["ui:UI says repo is clean"],
            Observed = FreshObservation() with { WorktreeState = RepoWorktreeState.Dirty }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.DirtyWorktree, RepoStateFreshnessVerdict.Blocked);
    }

    [TestMethod]
    public void FreshnessGuard_OldApprovalCannotRefreshMovedBase()
    {
        var result = Evaluate(FreshRequest() with
        {
            ReceiptRefs = ["old-approval:base moved but approval still valid"],
            Observed = FreshObservation() with { BaseSha = "base-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.BaseBranchMoved, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanAcceptApproval);
    }

    [TestMethod]
    public void FreshnessGuard_FailsClosedForNullRequest()
    {
        var result = RepoStateFreshnessGuard.Evaluate(null);

        AssertBlocked(result, RepoStateFreshnessIssueKind.MissingRequest, RepoStateFreshnessVerdict.Blocked);
        AssertContains(result.MissingEvidenceRefs, "repo-state-freshness-request");
    }

    [TestMethod]
    public void FreshnessGuard_FailsClosedForMissingExpectedState()
    {
        var result = Evaluate(FreshRequest() with { Expected = null });

        AssertBlocked(result, RepoStateFreshnessIssueKind.MissingExpectedState, RepoStateFreshnessVerdict.Blocked);
    }

    [TestMethod]
    public void FreshnessGuard_FailsClosedForMissingObservedState()
    {
        var result = Evaluate(FreshRequest() with { Observed = null });

        AssertBlocked(result, RepoStateFreshnessIssueKind.MissingObservedState, RepoStateFreshnessVerdict.Blocked);
    }

    [TestMethod]
    public void FreshnessGuard_ResultBoundaryForbidsAllMutation()
    {
        var boundary = Evaluate(FreshRequest()).Boundary;

        Assert.IsFalse(boundary.CanRefreshEvidence);
        Assert.IsFalse(boundary.CanRevalidate);
        Assert.IsFalse(boundary.CanRegeneratePatch);
        Assert.IsFalse(boundary.CanApplySource);
        Assert.IsFalse(boundary.CanRollbackSource);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanCreatePullRequest);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanSatisfyPolicy);
        Assert.IsFalse(boundary.CanAcceptApproval);
    }

    [TestMethod]
    public void PR20_InterruptedRecovery_RemainsReadOnly()
    {
        var report = InterruptedRunRecoveryDiagnosisService.Diagnose(new()
        {
            RunId = "run-pr21-regression",
            WorkspaceEvidenceRefs = ["disposable-workspace:pr21"]
        });

        Assert.IsFalse(report.Boundary.CanResumeRun);
        Assert.IsFalse(report.Boundary.CanApplySource);
        Assert.IsFalse(report.Boundary.CanRollbackSource);
        Assert.IsFalse(report.Boundary.CanCreateCommit);
        Assert.IsFalse(report.Boundary.CanPush);
        Assert.IsFalse(report.Boundary.CanCreatePullRequest);
        Assert.IsFalse(report.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void SourceApplyCannotConsumeStaleEvidence()
    {
        var result = Evaluate(FreshRequest(RunAuthorityOperationKind.SourceApply) with
        {
            Observed = FreshObservation() with { HeadSha = "head-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.HeadBranchMoved, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanApplySource);
    }

    [TestMethod]
    public void CommitPackageCannotAuthorizeCommitAfterHeadChanged()
    {
        var result = Evaluate(FreshRequest(RunAuthorityOperationKind.Commit) with
        {
            Expected = FreshExpectation() with { CommitHeadSha = "commit-old" },
            Observed = FreshObservation() with { CommitHeadSha = "commit-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.CommitHeadChanged, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanCommit);
    }

    [TestMethod]
    public void PushCannotProceedAfterRemoteChanged()
    {
        var result = Evaluate(FreshRequest(RunAuthorityOperationKind.Push) with
        {
            Expected = FreshExpectation() with { RemoteBranch = "origin/feature", RemoteSha = "remote-old" },
            Observed = FreshObservation() with { RemoteBranch = "origin/feature", RemoteSha = "remote-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.RemoteChanged, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanPush);
    }

    [TestMethod]
    public void DraftPullRequestCannotProceedAfterPushEvidenceBecomesStale()
    {
        var result = Evaluate(FreshRequest(RunAuthorityOperationKind.DraftPullRequest) with
        {
            Expected = FreshExpectation() with { RemoteBranch = "origin/feature", RemoteSha = "remote-old" },
            Observed = FreshObservation() with { RemoteBranch = "origin/feature", RemoteSha = "remote-new" }
        });

        AssertBlocked(result, RepoStateFreshnessIssueKind.RemoteChanged, RepoStateFreshnessVerdict.Stale);
        Assert.IsFalse(result.Boundary.CanCreatePullRequest);
    }

    [TestMethod]
    public void StaticMutationSurfaceScan_NoGitProviderExecutorOrFileMutationAdded()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "RepoStateFreshness"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "Process.Start",
            "ProcessStartInfo",
            "File.Write",
            "File.Append",
            "Directory.CreateDirectory",
            "git",
            "checkout",
            "reset",
            "revert",
            "cmd.exe",
            "powershell",
            "bash",
            "HttpClient",
            "Octokit",
            "ExecuteAsync",
            "ApplyAsync",
            "RollbackAsync",
            "CommitAsync",
            "PushAsync",
            "CreatePullRequestAsync"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }
    }

    private static RepoStateFreshnessResult Evaluate(RepoStateFreshnessRequest request) =>
        RepoStateFreshnessGuard.Evaluate(request);

    private static RepoStateFreshnessRequest FreshRequest(
        RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.SourceApply) =>
        new()
        {
            CheckId = "repo-state-check-pr21",
            Repository = "BigDaddyDread-code/IronDeveloper",
            RunId = "run-pr21",
            OperationKind = operationKind,
            Expected = FreshExpectation(),
            Observed = FreshObservation(),
            EvidenceRefs = ["validation-result-package:pr21"],
            ReceiptRefs = [],
            ObservedAtUtc = CurrentObservedAtUtc
        };

    private static RepoStateExpectation FreshExpectation() =>
        new()
        {
            BaseBranch = "main",
            BaseSha = "base-sha",
            HeadBranch = "feature/pr21",
            HeadSha = "head-sha",
            PatchHash = "patch-hash",
            CommitHeadSha = null,
            RemoteBranch = null,
            RemoteSha = null,
            ValidationObservedAtUtc = ValidationObservedAtUtc,
            ValidationBaseSha = "base-sha",
            ValidationHeadSha = "head-sha",
            ValidationPatchHash = "patch-hash",
            ValidationExpiresAtUtc = ValidationExpiresAtUtc
        };

    private static RepoStateObservation FreshObservation() =>
        new()
        {
            BaseBranch = "main",
            BaseSha = "base-sha",
            HeadBranch = "feature/pr21",
            HeadSha = "head-sha",
            WorktreeState = RepoWorktreeState.Clean,
            PatchApplicability = PatchApplicability.Applies,
            CommitHeadSha = null,
            RemoteBranch = null,
            RemoteSha = null,
            ObservedAtUtc = CurrentObservedAtUtc
        };

    private static void AssertBlocked(
        RepoStateFreshnessResult result,
        RepoStateFreshnessIssueKind issue,
        RepoStateFreshnessVerdict verdict)
    {
        Assert.IsFalse(result.IsFreshForMutation);
        Assert.AreEqual(verdict, result.Verdict);
        AssertIssue(result, issue);
        Assert.IsFalse(result.Boundary.CanApplySource);
        Assert.IsFalse(result.Boundary.CanCommit);
        Assert.IsFalse(result.Boundary.CanPush);
        Assert.IsFalse(result.Boundary.CanCreatePullRequest);
        Assert.IsFalse(result.Boundary.CanContinueWorkflow);
    }

    private static void AssertIssue(RepoStateFreshnessResult result, RepoStateFreshnessIssueKind issue)
    {
        Assert.IsTrue(
            result.IssueKinds.Contains(issue),
            $"Expected {issue} in: {string.Join(", ", result.IssueKinds)}");
        AssertContains(result.BlockingReasons, issue.ToString());
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static bool ContainsForbiddenSurface(string text, string forbidden)
    {
        if (forbidden is "git" or "checkout" or "reset" or "revert" or "powershell" or "bash")
        {
            return text.Split(
                    [' ', '\t', '\r', '\n', '"', '\'', '`', '(', ')', '[', ']', '{', '}', ';', ',', '.', ':'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        return text.Contains(forbidden, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}

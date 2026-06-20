using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAXMergeDecisionPackageTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);

    [TestMethod]
    public void BlockAX_Package_CreatesEligibleMergeDecisionPackageWithoutMutation()
    {
        var artifacts = MergeDecisionPackageBuilder.Build(CreateInput());
        var package = artifacts.Package;

        Assert.AreEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanMergeForExecutor);
        Assert.AreEqual("Squash", package.SelectedMergeStrategy);
        Assert.AreEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, artifacts.Receipt.Verdict);
        Assert.IsTrue(artifacts.Receipt.CanMergeForExecutor);
        AssertBoundary(package.Boundary);
        Assert.IsFalse(package.Boundary.CanMerge);
        Assert.IsFalse(package.Boundary.CanAutoMerge);
        Assert.IsFalse(package.Boundary.CanRelease);
        Assert.IsFalse(package.Boundary.CanDeploy);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BlockAX_Package_BlocksMissingOrFailedReviewerRequestExecutionReceipt()
    {
        var cases = new (string Name, ReviewerRequestExecutionReceipt? Receipt, MergeDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, MergeDecisionPackageBlockReason.MissingReviewerRequestExecutionReceipt),
            ("blocked", CreateReviewerRequestReceipt() with { ExecutionVerdict = ReviewerRequestExecutionVerdict.Blocked }, MergeDecisionPackageBlockReason.ReviewerRequestExecutionNotExecuted),
            ("failed", CreateReviewerRequestReceipt() with { ExecutionVerdict = ReviewerRequestExecutionVerdict.Failed }, MergeDecisionPackageBlockReason.ReviewerRequestExecutionNotExecuted),
            ("not-attempted", CreateReviewerRequestReceipt() with { ReviewerRequestAttempted = false }, MergeDecisionPackageBlockReason.ReviewerRequestExecutionNotExecuted),
            ("not-accepted", CreateReviewerRequestReceipt() with { ReviewerRequestAccepted = false }, MergeDecisionPackageBlockReason.ReviewerRequestExecutionNotExecuted),
            ("post-not-verified", CreateReviewerRequestReceipt() with { PostStateVerified = false }, MergeDecisionPackageBlockReason.ReviewerRequestPostStateNotVerified),
            ("wrong-pr", CreateReviewerRequestReceipt() with { PullRequestNumber = 999 }, MergeDecisionPackageBlockReason.MissingPullRequestIdentity),
            ("wrong-repo", CreateReviewerRequestReceipt() with { Repository = "other/repo" }, MergeDecisionPackageBlockReason.MissingPullRequestIdentity),
            ("wrong-head", CreateReviewerRequestReceipt() with { ExpectedHeadSha = new string('c', 40), PostState = ReviewerState(headSha: new string('c', 40)) }, MergeDecisionPackageBlockReason.HeadShaMismatch),
            ("wrong-base", CreateReviewerRequestReceipt() with { ExpectedBaseBranch = "develop" }, MergeDecisionPackageBlockReason.BaseBranchMismatch)
        };

        foreach (var item in cases)
        {
            var package = MergeDecisionPackageBuilder.Build(CreateInput() with { ReviewerRequestExecutionReceipt = item.Receipt }).Package;

            Assert.AreNotEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanMergeForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAX_Package_BlocksStaleCurrentPrState()
    {
        var cases = new (string Name, MergeDecisionObservedPrState Observed, MergeDecisionPackageBlockReason Expected)[]
        {
            ("closed", CreateObservedState() with { PullRequestState = "closed" }, MergeDecisionPackageBlockReason.PullRequestNotOpen),
            ("draft", CreateObservedState() with { PullRequestDraft = true }, MergeDecisionPackageBlockReason.PullRequestStillDraft),
            ("head", CreateObservedState() with { HeadSha = new string('c', 40) }, MergeDecisionPackageBlockReason.HeadShaMismatch),
            ("base-branch", CreateObservedState() with { BaseBranch = "develop" }, MergeDecisionPackageBlockReason.BaseBranchMismatch),
            ("base-sha", CreateObservedState() with { BaseSha = new string('d', 40) }, MergeDecisionPackageBlockReason.BaseShaMismatch),
            ("conflicts", CreateObservedState() with { HasConflicts = true }, MergeDecisionPackageBlockReason.PullRequestHasConflicts),
            ("not-mergeable", CreateObservedState() with { Mergeable = false }, MergeDecisionPackageBlockReason.PullRequestNotMergeable)
        };

        foreach (var item in cases)
        {
            var package = MergeDecisionPackageBuilder.Build(CreateInput() with { ObservedPullRequest = item.Observed }).Package;

            Assert.AreEqual(MergeDecisionPackageVerdict.PackageBlocked, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanMergeForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAX_Package_BlocksMissingStaleOrInsufficientReviewEvidence()
    {
        var cases = new (string Name, MergeReviewEvidence? Evidence, MergeDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, MergeDecisionPackageBlockReason.MissingReviewEvidence),
            ("stale", CreateReviewEvidence() with { ReviewEvidenceHeadSha = new string('c', 40) }, MergeDecisionPackageBlockReason.ReviewEvidenceStale),
            ("approvals-missing", CreateReviewEvidence() with { RequiredApprovalCount = 2, ActualApprovalCount = 1 }, MergeDecisionPackageBlockReason.RequiredApprovalsMissing),
            ("requested-changes", CreateReviewEvidence() with { RequestedChangesReviewers = ["reviewer-two"] }, MergeDecisionPackageBlockReason.RequestedChangesPresent),
            ("threads", CreateReviewEvidence() with { UnresolvedReviewThreads = ["thread-one"] }, MergeDecisionPackageBlockReason.BlockingReviewThreadsPresent),
            ("author-approval", CreateReviewEvidence() with { ApprovingReviewers = ["author-one"] }, MergeDecisionPackageBlockReason.PullRequestAuthorApprovalNotAllowed),
            ("stale-approval", CreateReviewEvidence() with { StaleReviewers = ["reviewer-one"] }, MergeDecisionPackageBlockReason.StaleApprovalPresent)
        };

        foreach (var item in cases)
        {
            var package = MergeDecisionPackageBuilder.Build(CreateInput() with { ReviewEvidence = item.Evidence }).Package;

            Assert.AreNotEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanMergeForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAX_Package_BlocksMissingStaleOrFailedValidationEvidence()
    {
        var cases = new (string Name, MergeValidationEvidence? Evidence, MergeDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, MergeDecisionPackageBlockReason.MissingValidationEvidence),
            ("stale", CreateValidationEvidence() with { CommitSha = new string('c', 40) }, MergeDecisionPackageBlockReason.ValidationEvidenceStale),
            ("failed", CreateValidationEvidence() with { Verdict = ValidationRunVerdict.Failed, FailedLaneNames = ["Build"] }, MergeDecisionPackageBlockReason.ValidationFailed),
            ("missing-lane", CreateValidationEvidence(requiredFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "Build").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing),
            ("diff-check-missing", CreateValidationEvidence(requiredFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "DiffCheck").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing),
            ("phase-missing", CreateValidationEvidence(requiredFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "PhaseAuthority").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing),
            ("declared-build-not-executed", CreateValidationEvidence(resultFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "Build").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing),
            ("declared-diff-check-not-executed", CreateValidationEvidence(resultFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "DiffCheck").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing),
            ("declared-phase-authority-not-executed", CreateValidationEvidence(resultFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "PhaseAuthority").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing),
            ("declared-merge-decision-authority-not-executed", CreateValidationEvidence(resultFamilies: MergeDecisionPackageBuilder.RequiredValidationFamilies.Where(item => item != "MergeDecisionAuthority").ToArray()), MergeDecisionPackageBlockReason.RequiredValidationMissing)
        };

        foreach (var item in cases)
        {
            var package = MergeDecisionPackageBuilder.Build(CreateInput() with { ValidationEvidence = item.Evidence }).Package;

            Assert.AreNotEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanMergeForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAX_Package_RequiresExplicitMergeDecision()
    {
        var cases = new (string Name, MergeDecisionRecord? Decision, MergeDecisionPackageBlockReason Expected)[]
        {
            ("missing", null, MergeDecisionPackageBlockReason.MissingMergeDecision),
            ("blocked", CreateDecision() with { Decision = MergeDecision.Blocked }, MergeDecisionPackageBlockReason.MergeDecisionBlocked),
            ("rejected", CreateDecision() with { Decision = MergeDecision.Rejected }, MergeDecisionPackageBlockReason.MergeDecisionRejected),
            ("missing-maker", CreateDecision() with { DecisionMadeBy = " " }, MergeDecisionPackageBlockReason.MissingDecisionMaker),
            ("author-maker", CreateDecision() with { DecisionMadeBy = "author-one" }, MergeDecisionPackageBlockReason.DecisionMakerIsPullRequestAuthor),
            ("missing-rationale", CreateDecision() with { DecisionRationale = " " }, MergeDecisionPackageBlockReason.MissingDecisionRationale),
            ("missing-strategy", CreateDecision() with { ExpectedMergeStrategy = " " }, MergeDecisionPackageBlockReason.MissingMergeStrategy),
            ("unsupported-strategy", CreateDecision() with { ExpectedMergeStrategy = "octopus" }, MergeDecisionPackageBlockReason.UnsupportedMergeStrategy)
        };

        foreach (var item in cases)
        {
            var package = MergeDecisionPackageBuilder.Build(CreateInput() with { MergeDecisionRecord = item.Decision }).Package;

            Assert.AreNotEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanMergeForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
            AssertBoundary(package.Boundary);
        }
    }

    [TestMethod]
    public void BlockAX_Boundary_RemainsEvidenceOnly()
    {
        var package = MergeDecisionPackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanMergeForExecutor);
        AssertBoundary(package.Boundary);
    }

    [TestMethod]
    public async Task BlockAX_Cli_BlocksMergeApprovalReleaseAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "execute", "merge", "auto-merge", "approve", "review", "resolve-comments", "reply", "request-reviewers", "ready", "release", "deploy", "tag", "publish", "promote-memory", "continue" })
        {
            var result = await RunCliAsync("merge-decision", forbidden, "--package", "merge-decision-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAX_StaticBoundary_ProvesNoMergeReleaseMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliMergeDecisionPackage.cs"));
        Assert.IsFalse(cli.Contains("request_pull_request_reviewers", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("mark_pull_request_ready_for_review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr edit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AX_MERGE_DECISION_PACKAGE.md"));
        StringAssert.Contains(receipt, "Approval is not merge decision.");
        StringAssert.Contains(receipt, "Merge decision package is not merge execution.");
        StringAssert.Contains(receipt, "Merge execution is not release.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "No self-approval.");
        StringAssert.Contains(receipt, "No hidden mutation.");
        StringAssert.Contains(receipt, "AX does not merge.");
        StringAssert.Contains(receipt, "AX does not enable auto-merge.");
        StringAssert.Contains(receipt, "AX does not approve.");
        StringAssert.Contains(receipt, "AX does not submit reviews.");
        StringAssert.Contains(receipt, "AX does not resolve review threads.");
        StringAssert.Contains(receipt, "AX does not release.");
        StringAssert.Contains(receipt, "AX does not deploy.");
        StringAssert.Contains(receipt, "AX does not continue workflow.");
    }

    [TestMethod]
    public void BlockAX_MergeDecisionPackageDoesNotBecomeMergeReleaseDeployAuthority()
    {
        var package = MergeDecisionPackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanMergeForExecutor);
        Assert.IsFalse(package.Boundary.CanMerge);
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanApprove(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanSubmitReview(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanMerge(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanAutoMerge(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanRelease(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanTag(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanPublish(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanPromoteMemory(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanContinueWorkflow(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanCommit(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanPush(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanMutateSource(package));
        Assert.IsFalse(MergeDecisionPackageBypassEvaluator.CanMutateWorkspace(package));
    }

    [TestMethod]
    public void BlockAX_ApprovalEvidenceDoesNotBecomeMergeDecision()
    {
        var package = MergeDecisionPackageBuilder.Build(CreateInput() with { MergeDecisionRecord = null }).Package;

        Assert.AreNotEqual(MergeDecisionPackageVerdict.PackageReadyForMergeExecutor, package.PackageVerdict);
        Assert.IsFalse(package.CanMergeForExecutor);
        CollectionAssert.Contains(package.BlockReasons, MergeDecisionPackageBlockReason.MissingMergeDecision);
        Assert.IsNotNull(package.ReviewEvidence);
        Assert.AreEqual(1, package.ReviewEvidence!.ActualApprovalCount);
    }

    private static MergeDecisionPackageInput CreateInput() => new()
    {
        ReviewerRequestExecutionReceipt = CreateReviewerRequestReceipt(),
        ObservedPullRequest = CreateObservedState(),
        Repository = "owner/repo",
        PullRequestNumber = 471,
        ExpectedHeadBranch = "ax/merge-decision-package",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "main",
        ExpectedBaseSha = BaseSha,
        ReviewEvidence = CreateReviewEvidence(),
        ValidationEvidence = CreateValidationEvidence(),
        MergeDecisionRecord = CreateDecision(),
        CreatedBy = "merge-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T07:00:00Z")
    };

    private static ReviewerRequestExecutionReceipt CreateReviewerRequestReceipt() => new()
    {
        ReviewerRequestExecutionId = "reviewer_request_exec_ax",
        ReviewerRequestPackageId = "reviewer_request_pkg_ax",
        Repository = "owner/repo",
        PullRequestNumber = 471,
        PullRequestUrl = "https://github.com/owner/repo/pull/471",
        PreState = ReviewerState(requestedReviewers: []),
        PostState = ReviewerState(requestedReviewers: ["reviewer-one"]),
        ExpectedHeadBranch = "ax/merge-decision-package",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "main",
        ExpectedBaseSha = BaseSha,
        RequestedReviewers = ["reviewer-one"],
        RequestedTeams = [],
        ReviewerRequestAttempted = true,
        ReviewerRequestAccepted = true,
        PostStateVerified = true,
        ExecutionVerdict = ReviewerRequestExecutionVerdict.Executed,
        FailureClassification = ReviewerRequestExecutionFailureKind.None,
        RequestedBy = "builder",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T06:00:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T06:01:00Z"),
        Boundary = ReviewerRequestExecutionBoundary.Executor
    };

    private static ReviewerRequestExecutionObservedPrState ReviewerState(
        string? headSha = null,
        string[]? requestedReviewers = null) => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 471,
        PullRequestUrl = "https://github.com/owner/repo/pull/471",
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = "ax/merge-decision-package",
        HeadSha = headSha ?? HeadSha,
        BaseBranch = "main",
        BaseSha = BaseSha,
        Author = "author-one",
        RequestedReviewers = requestedReviewers ?? [],
        RequestedTeams = [],
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T06:00:00Z"),
        ObservationSucceeded = true
    };

    private static MergeDecisionObservedPrState CreateObservedState() => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 471,
        PullRequestUrl = "https://github.com/owner/repo/pull/471",
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = "ax/merge-decision-package",
        HeadSha = HeadSha,
        BaseBranch = "main",
        BaseSha = BaseSha,
        Author = "author-one",
        Mergeable = true,
        MergeStateStatus = "clean",
        IsBehindBase = false,
        HasConflicts = false,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T06:30:00Z"),
        ObservationSource = "test"
    };

    private static MergeReviewEvidence CreateReviewEvidence() => new()
    {
        RequiredApprovalCount = 1,
        ActualApprovalCount = 1,
        ApprovingReviewers = ["reviewer-one"],
        RequestedChangesReviewers = [],
        DismissedReviewers = [],
        StaleReviewers = [],
        UnresolvedReviewThreads = [],
        ReviewEvidenceHeadSha = HeadSha,
        ReviewEvidenceObservedAtUtc = DateTimeOffset.Parse("2026-06-20T06:35:00Z"),
        ReviewEvidenceReceiptId = "review_evidence_ax"
    };

    private static MergeValidationEvidence CreateValidationEvidence(string[]? requiredFamilies = null, string[]? resultFamilies = null) => new()
    {
        ValidationRunId = "validation_run_ax",
        ValidationPlanId = "validation_plan_ax",
        CommitSha = HeadSha,
        Verdict = ValidationRunVerdict.Passed,
        RequiredLaneNames = requiredFamilies ?? MergeDecisionPackageBuilder.RequiredValidationFamilies,
        ResultLaneNames = resultFamilies ?? requiredFamilies ?? MergeDecisionPackageBuilder.RequiredValidationFamilies,
        MissingLaneNames = [],
        FailedLaneNames = [],
        StartedAtUtc = DateTimeOffset.Parse("2026-06-20T06:40:00Z"),
        FinishedAtUtc = DateTimeOffset.Parse("2026-06-20T06:45:00Z"),
        ValidationEvidenceReceiptId = "validation_run_ax"
    };

    private static MergeDecisionRecord CreateDecision() => new()
    {
        MergeDecisionId = "merge_decision_ax",
        Decision = MergeDecision.ApprovedForMergeExecutor,
        DecisionMadeBy = "merge-captain",
        DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T06:50:00Z"),
        DecisionRationale = "Reviewed, current, validation-backed PR is ready for the controlled merge executor.",
        ExpectedRepository = "owner/repo",
        ExpectedPullRequestNumber = 471,
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "main",
        ExpectedMergeStrategy = "squash",
        PolicyReceiptId = "phase3_policy_ax",
        ReviewEvidenceReceiptId = "review_evidence_ax",
        ValidationEvidenceReceiptId = "validation_run_ax"
    };

    private static void AssertBoundary(MergeDecisionPackageBoundary boundary)
    {
        Assert.IsTrue(boundary.EvidenceOnly);
        Assert.IsFalse(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanRemoveReviewers);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanReplyToReviewThreads);
        Assert.IsFalse(boundary.CanApprove);
        Assert.IsFalse(boundary.CanSubmitReview);
        Assert.IsFalse(boundary.CanMerge);
        Assert.IsFalse(boundary.CanAutoMerge);
        Assert.IsFalse(boundary.CanRelease);
        Assert.IsFalse(boundary.CanDeploy);
        Assert.IsFalse(boundary.CanTag);
        Assert.IsFalse(boundary.CanPublish);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
    }

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

using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAVControlledReviewerRequestPackageTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);

    [TestMethod]
    public void BlockAV_Package_CreatesEligibleReviewerRequestPackageWithoutMutation()
    {
        var artifacts = ReviewerRequestPackageBuilder.Build(CreateInput());
        var package = artifacts.Package;

        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanRequestReviewersForExecutor);
        Assert.AreEqual(1, package.RequestedReviewers.Length);
        Assert.AreEqual("reviewer-one", package.RequestedReviewers[0].SlugOrLogin);
        Assert.AreEqual(0, package.RequestedTeams.Length);
        Assert.IsTrue(package.Boundary.EvidenceOnly);
        Assert.IsFalse(package.Boundary.CanRequestReviewers);
        Assert.IsFalse(package.Boundary.CanApprove);
        Assert.IsFalse(package.Boundary.CanMerge);
        Assert.IsFalse(package.Boundary.CanRelease);
        Assert.IsFalse(package.Boundary.CanDeploy);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor, artifacts.Receipt.Verdict);
        Assert.IsTrue(artifacts.Receipt.CanRequestReviewersForExecutor);
        AssertBoundary(package.Boundary);
    }

    [TestMethod]
    public void BlockAV_Package_BlocksMissingOrFailedReadyExecutionReceipt()
    {
        var cases = new (string Name, ReadyForReviewExecutionReceipt? Receipt, ReviewerRequestPackageBlockReason Expected)[]
        {
            ("missing", null, ReviewerRequestPackageBlockReason.MissingReadyForReviewExecutionReceipt),
            ("blocked", CreateReadyReceipt() with { ExecutionVerdict = ReadyForReviewExecutionVerdict.Blocked }, ReviewerRequestPackageBlockReason.ReadyForReviewExecutionNotExecuted),
            ("failed", CreateReadyReceipt() with { ExecutionVerdict = ReadyForReviewExecutionVerdict.Failed }, ReviewerRequestPackageBlockReason.ReadyForReviewExecutionNotExecuted),
            ("not-attempted", CreateReadyReceipt() with { ReadyTransitionAttempted = false }, ReviewerRequestPackageBlockReason.ReadyForReviewExecutionNotExecuted),
            ("not-accepted", CreateReadyReceipt() with { ReadyTransitionAccepted = false }, ReviewerRequestPackageBlockReason.ReadyForReviewExecutionNotExecuted),
            ("post-not-verified", CreateReadyReceipt() with { PostStateVerified = false }, ReviewerRequestPackageBlockReason.ReadyForReviewPostStateNotVerified),
            ("wrong-pr", CreateReadyReceipt() with { PullRequestNumber = 999 }, ReviewerRequestPackageBlockReason.ReadyForReviewReceiptPrMismatch),
            ("wrong-repo", CreateReadyReceipt() with { Repository = "other/repo" }, ReviewerRequestPackageBlockReason.ReadyForReviewReceiptPrMismatch),
            ("wrong-head", CreateReadyReceipt() with { ExpectedHeadSha = new string('c', 40) }, ReviewerRequestPackageBlockReason.ReadyForReviewReceiptHeadMismatch),
            ("wrong-base", CreateReadyReceipt() with { ExpectedBaseBranch = "develop" }, ReviewerRequestPackageBlockReason.ReadyForReviewReceiptBaseMismatch)
        };

        foreach (var item in cases)
        {
            var package = ReviewerRequestPackageBuilder.Build(CreateInput() with { ReadyExecutionReceipt = item.Receipt }).Package;

            Assert.AreNotEqual(ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanRequestReviewersForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAV_Package_BlocksStaleCurrentPrState()
    {
        var cases = new (string Name, ReviewerRequestObservedPrState Observed, ReviewerRequestPackageBlockReason Expected)[]
        {
            ("closed", CreateObservedState() with { PullRequestState = "closed" }, ReviewerRequestPackageBlockReason.PullRequestNotOpen),
            ("draft", CreateObservedState() with { PullRequestDraft = true }, ReviewerRequestPackageBlockReason.PullRequestStillDraft),
            ("head", CreateObservedState() with { HeadSha = new string('c', 40) }, ReviewerRequestPackageBlockReason.HeadShaMismatch),
            ("base-branch", CreateObservedState() with { BaseBranch = "develop" }, ReviewerRequestPackageBlockReason.BaseBranchMismatch),
            ("base-sha", CreateObservedState() with { BaseSha = new string('d', 40) }, ReviewerRequestPackageBlockReason.BaseShaMismatch)
        };

        foreach (var item in cases)
        {
            var package = ReviewerRequestPackageBuilder.Build(CreateInput() with { ObservedPullRequest = item.Observed }).Package;

            Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, package.PackageVerdict, item.Name);
            Assert.IsFalse(package.CanRequestReviewersForExecutor, item.Name);
            CollectionAssert.Contains(package.BlockReasons, item.Expected, item.Name);
        }
    }

    [TestMethod]
    public void BlockAV_Package_BlocksMissingReviewerTargetsAndMissingRationale()
    {
        var noTargets = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = [], RequestedTeams = [] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageIncomplete, noTargets.PackageVerdict);
        CollectionAssert.Contains(noTargets.BlockReasons, ReviewerRequestPackageBlockReason.MissingReviewerTargets);
        Assert.IsFalse(noTargets.CanRequestReviewersForExecutor);

        var noRationale = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestRationale = "   " }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageIncomplete, noRationale.PackageVerdict);
        CollectionAssert.Contains(noRationale.BlockReasons, ReviewerRequestPackageBlockReason.MissingRequestRationale);
        Assert.IsFalse(noRationale.CanRequestReviewersForExecutor);

        var whitespaceReviewer = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = ["   "] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, whitespaceReviewer.PackageVerdict);
        CollectionAssert.Contains(whitespaceReviewer.BlockReasons, ReviewerRequestPackageBlockReason.InvalidReviewerLogin);
        Assert.IsFalse(whitespaceReviewer.CanRequestReviewersForExecutor);

        var invalidReviewer = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = ["bad/reviewer"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, invalidReviewer.PackageVerdict);
        CollectionAssert.Contains(invalidReviewer.BlockReasons, ReviewerRequestPackageBlockReason.InvalidReviewerLogin);

        var invalidTeam = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = [], RequestedTeams = ["bad team"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, invalidTeam.PackageVerdict);
        CollectionAssert.Contains(invalidTeam.BlockReasons, ReviewerRequestPackageBlockReason.InvalidTeamSlug);
    }

    [TestMethod]
    public void BlockAV_Package_BlocksSelfReviewAndAuthorReview()
    {
        var self = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = ["builder"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, self.PackageVerdict);
        CollectionAssert.Contains(self.BlockReasons, ReviewerRequestPackageBlockReason.RequestedReviewerIsRequester);
        Assert.IsFalse(self.CanRequestReviewersForExecutor);

        var selfWithAtPrefix = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedBy = "@builder", RequestedReviewers = ["builder"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, selfWithAtPrefix.PackageVerdict);
        CollectionAssert.Contains(selfWithAtPrefix.BlockReasons, ReviewerRequestPackageBlockReason.RequestedReviewerIsRequester);
        Assert.IsFalse(selfWithAtPrefix.CanRequestReviewersForExecutor);

        var author = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = ["author-one"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, author.PackageVerdict);
        CollectionAssert.Contains(author.BlockReasons, ReviewerRequestPackageBlockReason.RequestedReviewerIsPullRequestAuthor);
        Assert.IsFalse(author.CanRequestReviewersForExecutor);

        var duplicateReviewer = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = ["reviewer-one", "Reviewer-One"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, duplicateReviewer.PackageVerdict);
        CollectionAssert.Contains(duplicateReviewer.BlockReasons, ReviewerRequestPackageBlockReason.DuplicateReviewerTarget);

        var duplicateTeam = ReviewerRequestPackageBuilder.Build(CreateInput() with { RequestedReviewers = [], RequestedTeams = ["platform", "Platform"] }).Package;
        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, duplicateTeam.PackageVerdict);
        CollectionAssert.Contains(duplicateTeam.BlockReasons, ReviewerRequestPackageBlockReason.DuplicateReviewerTarget);
    }

    [TestMethod]
    public void BlockAV_Package_DoesNotReRequestAlreadyRequestedReviewers()
    {
        var observed = CreateObservedState() with
        {
            ExistingRequestedReviewers = ["reviewer-one"],
            ExistingRequestedTeams = ["platform"]
        };
        var package = ReviewerRequestPackageBuilder.Build(CreateInput() with
        {
            ObservedPullRequest = observed,
            RequestedReviewers = ["reviewer-one"],
            RequestedTeams = ["platform"]
        }).Package;

        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageIncomplete, package.PackageVerdict);
        Assert.IsFalse(package.CanRequestReviewersForExecutor);
        CollectionAssert.Contains(package.BlockReasons, ReviewerRequestPackageBlockReason.ReviewerAlreadyRequested);
        CollectionAssert.Contains(package.BlockReasons, ReviewerRequestPackageBlockReason.TeamAlreadyRequested);
        CollectionAssert.Contains(package.AlreadyRequestedReviewers, "reviewer-one");
        CollectionAssert.Contains(package.AlreadyRequestedTeams, "platform");
        Assert.AreEqual(0, package.RequestedReviewers.Length);
        Assert.AreEqual(0, package.RequestedTeams.Length);
        Assert.IsTrue(package.SkippedReviewerTargets.All(target => target.AlreadyRequested));
    }

    [TestMethod]
    public async Task BlockAV_Cli_RequiresExplicitCreatedByGitHubLogin()
    {
        var result = await RunCliAsync(
            "reviewer-request", "package",
            "--repo", "owner/repo",
            "--pr", "468",
            "--state", "open",
            "--draft", "false",
            "--branch", "av/controlled-reviewer-request-package",
            "--head", HeadSha,
            "--observed-head", HeadSha,
            "--base", "phase/close-feedback-loop",
            "--base-sha", BaseSha,
            "--author", "author-one",
            "--ready-receipt", "missing-ready-receipt.json",
            "--reviewer", "reviewer-one",
            "--rationale", "Reviewer request follows AU ready-for-review execution.",
            "--out", "artifacts/reviewer-request/av").ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Error, "Missing required option: --created-by <github-login>.");
        Assert.IsFalse(result.Error.Contains("ready execution receipt is missing", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task BlockAV_Cli_BlocksExecutionReviewMergeReleaseAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "request", "request-reviewers", "remove-reviewers", "ready", "approve", "review", "resolve-comments", "reply", "merge", "auto-merge", "release", "deploy", "tag", "publish", "promote-memory", "continue" })
        {
            var result = await RunCliAsync("reviewer-request", forbidden, "--package", "reviewer-request-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAV_StaticBoundary_ProvesNoReviewerRequestExecutionSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReviewerRequestPackage.cs"));
        Assert.IsFalse(cli.Contains("request_pull_request_reviewers", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr edit --add-reviewer", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AV_CONTROLLED_REVIEWER_REQUEST_PACKAGE.md"));
        StringAssert.Contains(receipt, "Reviewer request package is not reviewer request execution.");
        StringAssert.Contains(receipt, "Reviewer request is not approval.");
        StringAssert.Contains(receipt, "Approval is not merge.");
        StringAssert.Contains(receipt, "Merge is not release.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "No self-approval.");
        StringAssert.Contains(receipt, "No hidden mutation.");
        StringAssert.Contains(receipt, "AV must not request reviewers itself.");
    }

    [TestMethod]
    public void BlockAV_ReviewerRequestPackageDoesNotBecomeApprovalMergeReleaseOrContinuationAuthority()
    {
        var package = ReviewerRequestPackageBuilder.Build(CreateInput()).Package;

        Assert.IsTrue(package.CanRequestReviewersForExecutor);
        Assert.IsFalse(package.Boundary.CanRequestReviewers);
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanRequestReviewers(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanApprove(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanMerge(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanAutoMerge(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanTag(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanPublish(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanPromoteMemory(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanContinueWorkflow(package));
    }

    private static ReviewerRequestPackageInput CreateInput() => new()
    {
        ReadyExecutionReceipt = CreateReadyReceipt(),
        ObservedPullRequest = CreateObservedState(),
        Repository = "owner/repo",
        PullRequestNumber = 468,
        ExpectedHeadBranch = "av/controlled-reviewer-request-package",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "phase/close-feedback-loop",
        ExpectedBaseSha = BaseSha,
        RequestedReviewers = ["reviewer-one"],
        RequestedTeams = [],
        RequestRationale = "Reviewer request follows AU ready-for-review execution.",
        RequestedBy = "builder",
        PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T05:00:00Z")
    };

    private static ReviewerRequestObservedPrState CreateObservedState() => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 468,
        PullRequestUrl = "https://github.com/owner/repo/pull/468",
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = "av/controlled-reviewer-request-package",
        HeadSha = HeadSha,
        BaseBranch = "phase/close-feedback-loop",
        BaseSha = BaseSha,
        ExistingRequestedReviewers = [],
        ExistingRequestedTeams = [],
        Author = "author-one",
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T05:00:00Z"),
        ObservationSource = "test"
    };

    private static ReadyForReviewExecutionReceipt CreateReadyReceipt() => new()
    {
        ReadyForReviewExecutionId = "ready_review_exec_av",
        ReadyForReviewPackageId = "ready_review_pkg_av",
        Repository = "owner/repo",
        PullRequestNumber = 468,
        PullRequestUrl = "https://github.com/owner/repo/pull/468",
        PreState = ReadyState(draft: true),
        PostState = ReadyState(draft: false),
        ExpectedHeadBranch = "av/controlled-reviewer-request-package",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "phase/close-feedback-loop",
        ExpectedBaseSha = BaseSha,
        ReadyTransitionAttempted = true,
        ReadyTransitionAccepted = true,
        PostStateVerified = true,
        ExecutionVerdict = ReadyForReviewExecutionVerdict.Executed,
        FailureClassification = ReadyForReviewExecutionFailureKind.None,
        RequestedBy = "builder",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T04:01:00Z"),
        Boundary = ReadyForReviewExecutionBoundary.Executor
    };

    private static ReadyForReviewObservedPrState ReadyState(bool draft) => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 468,
        PullRequestUrl = "https://github.com/owner/repo/pull/468",
        PullRequestState = "open",
        PullRequestDraft = draft,
        HeadBranch = "av/controlled-reviewer-request-package",
        HeadSha = HeadSha,
        BaseBranch = "phase/close-feedback-loop",
        BaseSha = BaseSha,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:00Z"),
        ObservationSucceeded = true
    };

    private static void AssertBoundary(ReviewerRequestPackageBoundary boundary)
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

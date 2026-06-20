using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAYControlledMergeExecutorTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);
    private static readonly string MergeCommitSha = new('c', 40);

    [TestMethod]
    public async Task BlockAY_Executor_MergesExpectedPullRequestAndWritesReceipt()
    {
        var package = CreatePackage();
        var client = new FakeControlledMergeCommandClient();
        client.Observations.Enqueue(GoodPreState(package));
        client.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledMergeExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(MergeExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(MergeExecutionFailureKind.None, result.FailureKind);
        Assert.IsNotNull(result.Receipt);
        Assert.IsTrue(result.Receipt!.MergeAttempted);
        Assert.IsTrue(result.Receipt.MergeAccepted);
        Assert.IsTrue(result.Receipt.PostStateVerified);
        Assert.AreEqual(MergeCommitSha, result.Receipt.MergeCommitSha);
        Assert.AreEqual(2, client.ObserveCalls);
        Assert.AreEqual(1, client.MergeCalls);
        Assert.IsTrue(result.Receipt.Boundary.CanMerge);
        Assert.IsFalse(result.Receipt.Boundary.CanAutoMerge);
        Assert.IsFalse(result.Receipt.Boundary.CanRelease);
        Assert.IsFalse(result.Receipt.Boundary.CanDeploy);
        Assert.IsFalse(result.Receipt.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public async Task BlockAY_Executor_RejectsMissingBlockedIncompleteOrRejectedPackage()
    {
        var ready = CreatePackage();
        var cases = new (string Name, MergeDecisionPackage? Package)[]
        {
            ("missing", null),
            ("incomplete", ready with { PackageVerdict = MergeDecisionPackageVerdict.PackageIncomplete }),
            ("blocked", ready with { PackageVerdict = MergeDecisionPackageVerdict.PackageBlocked }),
            ("rejected", ready with { PackageVerdict = MergeDecisionPackageVerdict.PackageRejected }),
            ("cannot-merge", ready with { CanMergeForExecutor = false }),
            ("block-reasons", ready with { BlockReasons = [MergeDecisionPackageBlockReason.ValidationFailed] }),
            ("boundary-authority", ready with { Boundary = ready.Boundary with { CanMerge = true } })
        };

        foreach (var item in cases)
        {
            var client = new FakeControlledMergeCommandClient();
            var result = await ControlledMergeExecutor.ExecuteAsync(CreateRequest(item.Package), client).ConfigureAwait(false);

            Assert.AreEqual(MergeExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsNotNull(result.Receipt, item.Name);
            Assert.IsFalse(result.Receipt!.MergeAttempted, item.Name);
            Assert.AreEqual(0, client.ObserveCalls, item.Name);
            Assert.AreEqual(0, client.MergeCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAY_Executor_BlocksPreStateMismatchBeforeMutation()
    {
        var package = CreatePackage();
        var cases = new (string Name, MergeExecutionObservedPrState State, MergeExecutionFailureKind Expected)[]
        {
            ("closed", GoodPreState(package) with { PullRequestState = "closed" }, MergeExecutionFailureKind.PullRequestNotOpen),
            ("draft", GoodPreState(package) with { PullRequestDraft = true }, MergeExecutionFailureKind.PullRequestStillDraft),
            ("already-merged", GoodPreState(package) with { Merged = true }, MergeExecutionFailureKind.PullRequestAlreadyMerged),
            ("wrong-pr", GoodPreState(package) with { PullRequestNumber = 999 }, MergeExecutionFailureKind.PullRequestNumberMismatch),
            ("wrong-repo", GoodPreState(package) with { Repository = "other/repo" }, MergeExecutionFailureKind.RepositoryMismatch),
            ("wrong-head-branch", GoodPreState(package) with { HeadBranch = "other/head" }, MergeExecutionFailureKind.HeadBranchMismatch),
            ("head-drift", GoodPreState(package) with { HeadSha = new string('d', 40) }, MergeExecutionFailureKind.HeadShaMismatch),
            ("base-branch", GoodPreState(package) with { BaseBranch = "develop" }, MergeExecutionFailureKind.BaseBranchMismatch),
            ("base-sha", GoodPreState(package) with { BaseSha = new string('e', 40) }, MergeExecutionFailureKind.BaseShaMismatch),
            ("conflicts", GoodPreState(package) with { HasConflicts = true }, MergeExecutionFailureKind.PullRequestHasConflicts),
            ("not-mergeable", GoodPreState(package) with { Mergeable = false }, MergeExecutionFailureKind.PullRequestNotMergeable),
            ("behind-base", GoodPreState(package) with { IsBehindBase = true }, MergeExecutionFailureKind.PullRequestBehindBase)
        };

        foreach (var item in cases)
        {
            var client = new FakeControlledMergeCommandClient();
            client.Observations.Enqueue(item.State);

            var result = await ControlledMergeExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

            Assert.AreEqual(MergeExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(item.Expected, result.FailureKind, item.Name);
            Assert.IsFalse(result.Receipt!.MergeAttempted, item.Name);
            Assert.AreEqual(1, client.ObserveCalls, item.Name);
            Assert.AreEqual(0, client.MergeCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAY_Executor_BlocksMergeStrategyOverride()
    {
        var package = CreatePackage();
        foreach (var overrideStrategy in new[] { "MergeCommit", "Rebase" })
        {
            var client = new FakeControlledMergeCommandClient();
            var result = await ControlledMergeExecutor.ExecuteAsync(
                CreateRequest(package) with { RequestedMergeStrategy = overrideStrategy },
                client).ConfigureAwait(false);

            Assert.AreEqual(MergeExecutionVerdict.Blocked, result.Verdict, overrideStrategy);
            Assert.AreEqual(MergeExecutionFailureKind.MergeStrategyOverrideNotAllowed, result.FailureKind, overrideStrategy);
            Assert.IsFalse(result.Receipt!.MergeAttempted, overrideStrategy);
            Assert.AreEqual(0, client.ObserveCalls, overrideStrategy);
            Assert.AreEqual(0, client.MergeCalls, overrideStrategy);
        }
    }

    [TestMethod]
    public async Task BlockAY_Executor_FailsIfPostMergeStateDoesNotVerify()
    {
        var package = CreatePackage();
        var cases = new (string Name, MergeExecutionObservedPrState PostState, MergeMutationResult Mutation)[]
        {
            ("still-open", GoodPostState(package) with { PullRequestState = "open" }, AcceptedMutation(package)),
            ("merged-false", GoodPostState(package) with { Merged = false }, AcceptedMutation(package)),
            ("missing-merge-commit", GoodPostState(package) with { MergeCommitSha = null, BaseSha = null }, AcceptedMutation(package) with { MergeCommitSha = null }),
            ("head-changed", GoodPostState(package) with { HeadSha = new string('d', 40) }, AcceptedMutation(package)),
            ("base-branch-changed", GoodPostState(package) with { BaseBranch = "develop" }, AcceptedMutation(package)),
            ("post-observation-failed", FailedObservation(package, "post observation failed"), AcceptedMutation(package))
        };

        foreach (var item in cases)
        {
            var client = new FakeControlledMergeCommandClient { MutationResult = item.Mutation };
            client.Observations.Enqueue(GoodPreState(package));
            client.Observations.Enqueue(item.PostState);

            var result = await ControlledMergeExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

            Assert.AreEqual(MergeExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.AreEqual(MergeExecutionFailureKind.PostMergeVerificationFailed, result.FailureKind, item.Name);
            Assert.IsTrue(result.Receipt!.MergeAttempted, item.Name);
            Assert.IsTrue(result.Receipt.MergeAccepted, item.Name);
            Assert.IsFalse(result.Receipt.PostStateVerified, item.Name);
            Assert.AreEqual(1, client.MergeCalls, item.Name);
            Assert.AreEqual(2, client.ObserveCalls, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAY_Executor_DoesNotReleaseDeployTagPublishPromoteMemoryOrContinueAfterMerge()
    {
        var package = CreatePackage();
        var client = new FakeControlledMergeCommandClient();
        client.Observations.Enqueue(GoodPreState(package));
        client.Observations.Enqueue(GoodPostState(package));

        var result = await ControlledMergeExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(MergeExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(1, client.MergeCalls);
        Assert.AreEqual(0, client.AutoMergeCalls);
        Assert.AreEqual(0, client.ApproveCalls);
        Assert.AreEqual(0, client.ReviewCalls);
        Assert.AreEqual(0, client.ReleaseCalls);
        Assert.AreEqual(0, client.DeployCalls);
        Assert.AreEqual(0, client.TagCalls);
        Assert.AreEqual(0, client.PublishCalls);
        Assert.AreEqual(0, client.MemoryPromotionCalls);
        Assert.AreEqual(0, client.ContinueCalls);
        Assert.AreEqual(0, client.CommitCalls);
        Assert.AreEqual(0, client.PushCalls);
        Assert.AreEqual(0, client.SourceMutationCalls);
    }

    [TestMethod]
    public async Task BlockAY_Cli_BlocksAutoMergeApprovalReleaseDeployAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "auto-merge", "approve", "review", "resolve-comments", "reply", "request-reviewers", "ready", "release", "deploy", "tag", "publish", "promote-memory", "continue", "push", "commit" })
        {
            var result = await RunCliAsync("merge", forbidden, "--package", "merge-decision-package.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAY_StaticBoundary_ProvesNoReleaseDeployMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliControlledMergeExecution.cs"));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git commit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr edit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("--auto", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("--admin", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("--delete-branch", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(cli, "pulls/{_pullRequestNumber}/merge");
        StringAssert.Contains(cli, "sha={request.ExpectedHeadSha}");

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AY_CONTROLLED_MERGE_EXECUTOR.md"));
        StringAssert.Contains(receipt, "Merge decision package is not merge execution.");
        StringAssert.Contains(receipt, "Merge execution is not release.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Merge execution is not tag creation.");
        StringAssert.Contains(receipt, "Merge execution is not publishing.");
        StringAssert.Contains(receipt, "Merge execution is not memory promotion.");
        StringAssert.Contains(receipt, "Merge execution is not workflow continuation.");
        StringAssert.Contains(receipt, "Approval is not merge.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "No self-approval.");
        StringAssert.Contains(receipt, "No hidden mutation.");
        StringAssert.Contains(receipt, "AY merges only the expected PR head into the expected base branch.");
        StringAssert.Contains(receipt, "AY does not enable auto-merge.");
        StringAssert.Contains(receipt, "AY does not release.");
        StringAssert.Contains(receipt, "AY does not deploy.");
        StringAssert.Contains(receipt, "AY does not tag.");
        StringAssert.Contains(receipt, "AY does not publish.");
        StringAssert.Contains(receipt, "AY does not promote memory.");
        StringAssert.Contains(receipt, "AY does not continue workflow.");
    }

    [TestMethod]
    public void BlockAY_MergeExecutionReceiptDoesNotBecomeReleaseDeployOrContinuationAuthority()
    {
        var receipt = new MergeExecutionReceipt
        {
            MergeExecutionId = "merge_exec_test",
            MergeDecisionPackageId = "merge_decision_pkg_test",
            Repository = "owner/repo",
            PullRequestNumber = 472,
            PullRequestUrl = "https://github.com/owner/repo/pull/472",
            ExpectedHeadBranch = "ay/controlled-merge-executor",
            ExpectedHeadSha = HeadSha,
            ExpectedBaseBranch = "phase/controlled-merge",
            ExpectedBaseSha = BaseSha,
            SelectedMergeStrategy = "Squash",
            MergeCommitSha = MergeCommitSha,
            MergeAttempted = true,
            MergeAccepted = true,
            PostStateVerified = true,
            ExecutionVerdict = MergeExecutionVerdict.Executed,
            FailureClassification = MergeExecutionFailureKind.None,
            RequestedBy = "merge-captain",
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z"),
            ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T08:01:00Z"),
            Boundary = MergeExecutionBoundary.Executor
        };

        Assert.IsFalse(MergeExecutionBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanAutoMerge(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanTag(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanPublish(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanPromoteMemory(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanContinueWorkflow(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanCommit(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanPush(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanMutateSource(receipt));
        Assert.IsFalse(MergeExecutionBypassEvaluator.CanMutateWorkspace(receipt));
    }

    [TestMethod]
    public async Task BlockAY_Executor_BlocksMergeAfterHeadDriftEvenWhenPackageIsEligible()
    {
        var package = CreatePackage();
        var client = new FakeControlledMergeCommandClient();
        client.Observations.Enqueue(GoodPreState(package) with { HeadSha = new string('d', 40) });

        var result = await ControlledMergeExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(MergeExecutionVerdict.Blocked, result.Verdict);
        Assert.AreEqual(MergeExecutionFailureKind.HeadShaMismatch, result.FailureKind);
        Assert.IsFalse(result.Receipt!.MergeAttempted);
        Assert.AreEqual(1, client.ObserveCalls);
        Assert.AreEqual(0, client.MergeCalls);
    }

    private static MergeDecisionPackage CreatePackage() =>
        MergeDecisionPackageBuilder.Build(CreateInput()).Package;

    private static MergeDecisionPackageInput CreateInput() => new()
    {
        ReviewerRequestExecutionReceipt = CreateReviewerRequestReceipt(),
        ObservedPullRequest = CreateObservedState(),
        Repository = "owner/repo",
        PullRequestNumber = 472,
        ExpectedHeadBranch = "ay/controlled-merge-executor",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "phase/controlled-merge",
        ExpectedBaseSha = BaseSha,
        ReviewEvidence = CreateReviewEvidence(),
        ValidationEvidence = CreateValidationEvidence(),
        MergeDecisionRecord = CreateDecision(),
        CreatedBy = "merge-captain",
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z")
    };

    private static ReviewerRequestExecutionReceipt CreateReviewerRequestReceipt() => new()
    {
        ReviewerRequestExecutionId = "reviewer_request_exec_ay",
        ReviewerRequestPackageId = "reviewer_request_pkg_ay",
        Repository = "owner/repo",
        PullRequestNumber = 472,
        PullRequestUrl = "https://github.com/owner/repo/pull/472",
        PreState = ReviewerState(requestedReviewers: []),
        PostState = ReviewerState(requestedReviewers: ["reviewer-one"]),
        ExpectedHeadBranch = "ay/controlled-merge-executor",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "phase/controlled-merge",
        ExpectedBaseSha = BaseSha,
        RequestedReviewers = ["reviewer-one"],
        RequestedTeams = [],
        ReviewerRequestAttempted = true,
        ReviewerRequestAccepted = true,
        PostStateVerified = true,
        ExecutionVerdict = ReviewerRequestExecutionVerdict.Executed,
        FailureClassification = ReviewerRequestExecutionFailureKind.None,
        RequestedBy = "builder",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T07:00:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T07:01:00Z"),
        Boundary = ReviewerRequestExecutionBoundary.Executor
    };

    private static ReviewerRequestExecutionObservedPrState ReviewerState(
        string? headSha = null,
        string[]? requestedReviewers = null) => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 472,
        PullRequestUrl = "https://github.com/owner/repo/pull/472",
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = "ay/controlled-merge-executor",
        HeadSha = headSha ?? HeadSha,
        BaseBranch = "phase/controlled-merge",
        BaseSha = BaseSha,
        Author = "author-one",
        RequestedReviewers = requestedReviewers ?? [],
        RequestedTeams = [],
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T07:00:00Z"),
        ObservationSucceeded = true
    };

    private static MergeDecisionObservedPrState CreateObservedState() => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 472,
        PullRequestUrl = "https://github.com/owner/repo/pull/472",
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = "ay/controlled-merge-executor",
        HeadSha = HeadSha,
        BaseBranch = "phase/controlled-merge",
        BaseSha = BaseSha,
        Author = "author-one",
        Mergeable = true,
        MergeStateStatus = "clean",
        IsBehindBase = false,
        HasConflicts = false,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T07:30:00Z"),
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
        ReviewEvidenceObservedAtUtc = DateTimeOffset.Parse("2026-06-20T07:35:00Z"),
        ReviewEvidenceReceiptId = "review_evidence_ay"
    };

    private static MergeValidationEvidence CreateValidationEvidence() => new()
    {
        ValidationRunId = "validation_run_ay",
        ValidationPlanId = "validation_plan_ay",
        CommitSha = HeadSha,
        Verdict = ValidationRunVerdict.Passed,
        RequiredLaneNames = MergeDecisionPackageBuilder.RequiredValidationFamilies,
        ResultLaneNames = MergeDecisionPackageBuilder.RequiredValidationFamilies,
        MissingLaneNames = [],
        FailedLaneNames = [],
        StartedAtUtc = DateTimeOffset.Parse("2026-06-20T07:40:00Z"),
        FinishedAtUtc = DateTimeOffset.Parse("2026-06-20T07:45:00Z"),
        ValidationEvidenceReceiptId = "validation_run_ay"
    };

    private static MergeDecisionRecord CreateDecision() => new()
    {
        MergeDecisionId = "merge_decision_ay",
        Decision = MergeDecision.ApprovedForMergeExecutor,
        DecisionMadeBy = "merge-captain",
        DecisionMadeAtUtc = DateTimeOffset.Parse("2026-06-20T07:50:00Z"),
        DecisionRationale = "Reviewed, current, validation-backed PR is ready for the controlled merge executor.",
        ExpectedRepository = "owner/repo",
        ExpectedPullRequestNumber = 472,
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "phase/controlled-merge",
        ExpectedMergeStrategy = "squash",
        PolicyReceiptId = "phase3_policy_ay",
        ReviewEvidenceReceiptId = "review_evidence_ay",
        ValidationEvidenceReceiptId = "validation_run_ay"
    };

    private static MergeExecutionRequest CreateRequest(MergeDecisionPackage? package) => new()
    {
        Package = package,
        Repository = package?.Repository ?? "owner/repo",
        PullRequestNumber = package?.PullRequestNumber ?? 472,
        ExpectedHeadBranch = package?.HeadBranch ?? "ay/controlled-merge-executor",
        ExpectedHeadSha = package?.ExpectedHeadSha ?? HeadSha,
        ExpectedBaseBranch = package?.BaseBranch ?? "phase/controlled-merge",
        ExpectedBaseSha = package?.BaseSha ?? BaseSha,
        RequestedBy = "merge-captain",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z")
    };

    private static MergeExecutionObservedPrState GoodPreState(MergeDecisionPackage package) => new()
    {
        Repository = package.Repository,
        PullRequestNumber = package.PullRequestNumber,
        PullRequestUrl = package.PullRequestUrl,
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = package.HeadBranch,
        HeadSha = package.ExpectedHeadSha,
        BaseBranch = package.BaseBranch,
        BaseSha = package.BaseSha,
        Author = "author-one",
        Mergeable = true,
        MergeStateStatus = "clean",
        IsBehindBase = false,
        HasConflicts = false,
        Merged = false,
        MergeCommitSha = null,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:30Z"),
        ObservationSucceeded = true
    };

    private static MergeExecutionObservedPrState GoodPostState(MergeDecisionPackage package) => GoodPreState(package) with
    {
        PullRequestState = "closed",
        BaseSha = MergeCommitSha,
        Merged = true,
        MergeCommitSha = MergeCommitSha,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T08:01:00Z")
    };

    private static MergeExecutionObservedPrState FailedObservation(MergeDecisionPackage package, string error) => GoodPreState(package) with
    {
        ObservationSucceeded = false,
        ObservationError = error
    };

    private static MergeMutationResult AcceptedMutation(MergeDecisionPackage package) => new()
    {
        Attempted = true,
        Accepted = true,
        Provider = "FakeTestClient",
        CommandOrMutationName = "GitHub REST PR merge",
        MergeStrategy = package.SelectedMergeStrategy ?? "Squash",
        ExpectedHeadSha = package.ExpectedHeadSha,
        MergeCommitSha = MergeCommitSha,
        Message = "merged",
        CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:45Z")
    };

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

    private sealed class FakeControlledMergeCommandClient : IControlledMergeCommandClient
    {
        public Queue<MergeExecutionObservedPrState> Observations { get; } = new();
        public MergeMutationResult? MutationResult { get; init; }
        public int ObserveCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public int AutoMergeCalls { get; }
        public int ApproveCalls { get; }
        public int ReviewCalls { get; }
        public int ReleaseCalls { get; }
        public int DeployCalls { get; }
        public int TagCalls { get; }
        public int PublishCalls { get; }
        public int MemoryPromotionCalls { get; }
        public int ContinueCalls { get; }
        public int CommitCalls { get; }
        public int PushCalls { get; }
        public int SourceMutationCalls { get; }

        public Task<MergeExecutionObservedPrState> ObserveAsync(MergeExecutionRequest request, CancellationToken cancellationToken)
        {
            ObserveCalls++;
            if (Observations.Count > 0)
                return Task.FromResult(Observations.Dequeue());

            var fallback = request.Package is null
                ? new MergeExecutionObservedPrState
                {
                    Repository = request.Repository,
                    PullRequestNumber = request.PullRequestNumber,
                    PullRequestUrl = string.Empty,
                    PullRequestState = string.Empty,
                    PullRequestDraft = false,
                    HeadBranch = string.Empty,
                    HeadSha = string.Empty,
                    BaseBranch = string.Empty,
                    BaseSha = null,
                    Author = string.Empty,
                    Mergeable = false,
                    MergeStateStatus = string.Empty,
                    IsBehindBase = false,
                    HasConflicts = false,
                    Merged = false,
                    MergeCommitSha = null,
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                    ObservationSucceeded = false,
                    ObservationError = "no observation queued"
                }
                : FailedObservation(request.Package, "no observation queued");
            return Task.FromResult(fallback);
        }

        public Task<MergeMutationResult> MergeAsync(MergeExecutionRequest request, CancellationToken cancellationToken)
        {
            MergeCalls++;
            return Task.FromResult(MutationResult ?? AcceptedMutation(request.Package!));
        }
    }
}

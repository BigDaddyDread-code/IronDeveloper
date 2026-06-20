using IronDev.Cli;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAWControlledReviewerRequestExecutorTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);

    [TestMethod]
    public async Task BlockAW_Executor_RequestsExpectedReviewersAndWritesReceipt()
    {
        var package = CreatePackage(reviewers: ["reviewer-one"]);
        var client = new FakeReviewerRequestCommandClient
        {
            Observations =
            [
                GoodState(package),
                GoodState(package, requestedReviewers: ["reviewer-one"])
            ]
        };

        var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(ReviewerRequestExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(ReviewerRequestExecutionFailureKind.None, result.FailureKind);
        Assert.AreEqual(2, client.ObserveCalls);
        Assert.AreEqual(1, client.RequestReviewerCalls);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(package.ReviewerRequestPackageId, result.Receipt!.ReviewerRequestPackageId);
        Assert.IsTrue(result.Receipt.ReviewerRequestAttempted);
        Assert.IsTrue(result.Receipt.ReviewerRequestAccepted);
        Assert.IsTrue(result.Receipt.PostStateVerified);
        Assert.AreEqual(ReviewerRequestExecutionVerdict.Executed, result.Receipt.ExecutionVerdict);
        Assert.AreEqual(ReviewerRequestExecutionFailureKind.None, result.Receipt.FailureClassification);
        Assert.IsTrue(result.Receipt.Boundary.CanRequestReviewers);
        Assert.IsFalse(result.Receipt.Boundary.CanApprove);
        Assert.IsFalse(result.Receipt.Boundary.CanMerge);
        Assert.IsFalse(result.Receipt.Boundary.CanRelease);
        Assert.IsFalse(result.Receipt.Boundary.CanDeploy);
        Assert.IsFalse(result.Receipt.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public async Task BlockAW_Executor_RejectsMissingBlockedIncompleteOrRejectedPackage()
    {
        var missingClient = new FakeReviewerRequestCommandClient();
        var missing = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(null), missingClient).ConfigureAwait(false);
        Assert.AreEqual(ReviewerRequestExecutionVerdict.Blocked, missing.Verdict);
        Assert.AreEqual(ReviewerRequestExecutionFailureKind.MissingPackage, missing.FailureKind);
        Assert.AreEqual(0, missingClient.ObserveCalls + missingClient.RequestReviewerCalls);

        var ready = CreatePackage(reviewers: ["reviewer-one"]);
        var cases = new[]
        {
            ready with { PackageVerdict = ReviewerRequestPackageVerdict.PackageIncomplete, CanRequestReviewersForExecutor = false },
            ready with { PackageVerdict = ReviewerRequestPackageVerdict.PackageBlocked, BlockReasons = [ReviewerRequestPackageBlockReason.HeadShaMismatch] },
            ready with { PackageVerdict = ReviewerRequestPackageVerdict.PackageRejected },
            ready with { CanRequestReviewersForExecutor = false },
            ready with { BlockReasons = [ReviewerRequestPackageBlockReason.MissingReviewerTargets] },
            ready with { Boundary = ready.Boundary with { CanRequestReviewers = true } }
        };

        foreach (var package in cases)
        {
            var client = new FakeReviewerRequestCommandClient { Observations = [GoodState(package)] };
            var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);
            Assert.AreEqual(ReviewerRequestExecutionVerdict.Blocked, result.Verdict, package.PackageVerdict.ToString());
            Assert.AreEqual(0, client.ObserveCalls, package.PackageVerdict.ToString());
            Assert.AreEqual(0, client.RequestReviewerCalls, package.PackageVerdict.ToString());
            Assert.IsFalse(result.Receipt!.ReviewerRequestAttempted);
        }
    }

    [TestMethod]
    public async Task BlockAW_Executor_BlocksTamperedPackageBoundaryAuthorityBeforeObservation()
    {
        var ready = CreatePackage(reviewers: ["reviewer-one"]);
        var cases = new (string Name, ReviewerRequestPackageBoundary Boundary)[]
        {
            ("approve", ready.Boundary with { CanApprove = true }),
            ("merge", ready.Boundary with { CanMerge = true }),
            ("release", ready.Boundary with { CanRelease = true }),
            ("continue", ready.Boundary with { CanContinueWorkflow = true })
        };

        foreach (var item in cases)
        {
            var package = ready with { Boundary = item.Boundary };
            var client = new FakeReviewerRequestCommandClient { Observations = [GoodState(package)] };
            var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

            Assert.AreEqual(ReviewerRequestExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(ReviewerRequestExecutionFailureKind.PackageNotEligible, result.FailureKind, item.Name);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("PackageBoundaryAuthorityViolation", StringComparison.OrdinalIgnoreCase)), item.Name);
            Assert.AreEqual(0, client.ObserveCalls, item.Name);
            Assert.AreEqual(0, client.RequestReviewerCalls, item.Name);
            Assert.IsFalse(result.Receipt!.ReviewerRequestAttempted, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAW_Executor_BlocksPreStateMismatchBeforeMutation()
    {
        var package = CreatePackage(reviewers: ["reviewer-one"]);
        var cases = new (string Name, ReviewerRequestExecutionObservedPrState State, ReviewerRequestExecutionFailureKind ExpectedKind)[]
        {
            ("closed", GoodState(package) with { PullRequestState = "closed" }, ReviewerRequestExecutionFailureKind.PullRequestNotOpen),
            ("draft", GoodState(package) with { PullRequestDraft = true }, ReviewerRequestExecutionFailureKind.PullRequestStillDraft),
            ("wrong-pr", GoodState(package) with { PullRequestNumber = 999 }, ReviewerRequestExecutionFailureKind.PullRequestNumberMismatch),
            ("wrong-repo", GoodState(package) with { Repository = "other/repo" }, ReviewerRequestExecutionFailureKind.RepositoryMismatch),
            ("wrong-branch", GoodState(package) with { HeadBranch = "other/branch" }, ReviewerRequestExecutionFailureKind.HeadBranchMismatch),
            ("head-drift", GoodState(package) with { HeadSha = new string('c', 40) }, ReviewerRequestExecutionFailureKind.HeadShaMismatch),
            ("base-branch", GoodState(package) with { BaseBranch = "develop" }, ReviewerRequestExecutionFailureKind.BaseBranchMismatch),
            ("base-sha", GoodState(package) with { BaseSha = new string('d', 40) }, ReviewerRequestExecutionFailureKind.BaseShaMismatch)
        };

        foreach (var item in cases)
        {
            var client = new FakeReviewerRequestCommandClient { Observations = [item.State] };
            var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);
            Assert.AreEqual(ReviewerRequestExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(item.ExpectedKind, result.FailureKind, item.Name);
            Assert.AreEqual(1, client.ObserveCalls, item.Name);
            Assert.AreEqual(0, client.RequestReviewerCalls, item.Name);
            Assert.IsFalse(result.Receipt!.ReviewerRequestAttempted, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAW_Executor_BlocksAlreadyRequestedTargetsBeforeMutation()
    {
        var reviewerPackage = CreatePackage(reviewers: ["reviewer-one"]);
        var reviewerClient = new FakeReviewerRequestCommandClient
        {
            Observations = [GoodState(reviewerPackage, requestedReviewers: ["reviewer-one"])]
        };
        var reviewer = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(reviewerPackage), reviewerClient).ConfigureAwait(false);
        Assert.AreEqual(ReviewerRequestExecutionVerdict.Blocked, reviewer.Verdict);
        Assert.AreEqual(ReviewerRequestExecutionFailureKind.ReviewerAlreadyRequested, reviewer.FailureKind);
        Assert.IsFalse(reviewer.Receipt!.ReviewerRequestAttempted);
        Assert.AreEqual(0, reviewerClient.RequestReviewerCalls);

        var teamPackage = CreatePackage(reviewers: [], teams: ["platform"]);
        var teamClient = new FakeReviewerRequestCommandClient
        {
            Observations = [GoodState(teamPackage, requestedTeams: ["platform"])]
        };
        var team = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(teamPackage), teamClient).ConfigureAwait(false);
        Assert.AreEqual(ReviewerRequestExecutionVerdict.Blocked, team.Verdict);
        Assert.AreEqual(ReviewerRequestExecutionFailureKind.TeamAlreadyRequested, team.FailureKind);
        Assert.IsFalse(team.Receipt!.ReviewerRequestAttempted);
        Assert.AreEqual(0, teamClient.RequestReviewerCalls);
    }

    [TestMethod]
    public async Task BlockAW_Executor_FailsIfPostReviewerRequestStateDoesNotVerify()
    {
        var package = CreatePackage(reviewers: ["reviewer-one"], teams: ["platform"]);
        var cases = new (string Name, ReviewerRequestExecutionObservedPrState PostState)[]
        {
            ("missing-reviewer", GoodState(package, requestedTeams: ["platform"])),
            ("missing-team", GoodState(package, requestedReviewers: ["reviewer-one"])),
            ("head-drift", GoodState(package, requestedReviewers: ["reviewer-one"], requestedTeams: ["platform"]) with { HeadSha = new string('c', 40) }),
            ("closed", GoodState(package, requestedReviewers: ["reviewer-one"], requestedTeams: ["platform"]) with { PullRequestState = "closed" }),
            ("observation-failed", GoodState(package, requestedReviewers: ["reviewer-one"], requestedTeams: ["platform"]) with { ObservationSucceeded = false, ObservationError = "post observe failed" })
        };

        foreach (var item in cases)
        {
            var client = new FakeReviewerRequestCommandClient
            {
                Observations =
                [
                    GoodState(package),
                    item.PostState
                ]
            };
            var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);
            Assert.AreEqual(ReviewerRequestExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.AreEqual(ReviewerRequestExecutionFailureKind.PostReviewerRequestVerificationFailed, result.FailureKind, item.Name);
            Assert.AreEqual(1, client.RequestReviewerCalls, item.Name);
            Assert.IsTrue(result.Receipt!.ReviewerRequestAttempted, item.Name);
            Assert.IsTrue(result.Receipt.ReviewerRequestAccepted, item.Name);
            Assert.IsFalse(result.Receipt.PostStateVerified, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAW_Executor_DoesNotApproveReviewMergeReleaseOrContinueAfterRequest()
    {
        var package = CreatePackage(reviewers: ["reviewer-one"]);
        var client = new FakeReviewerRequestCommandClient
        {
            Observations =
            [
                GoodState(package),
                GoodState(package, requestedReviewers: ["reviewer-one"])
            ]
        };

        var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(ReviewerRequestExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(1, client.RequestReviewerCalls);
        Assert.AreEqual(0, client.ResolveThreadCalls);
        Assert.AreEqual(0, client.ReviewCommentCalls);
        Assert.AreEqual(0, client.ApproveCalls);
        Assert.AreEqual(0, client.MergeCalls);
        Assert.AreEqual(0, client.ReleaseCalls);
        Assert.AreEqual(0, client.DeployCalls);
        Assert.AreEqual(0, client.ContinueCalls);
    }

    [TestMethod]
    public async Task BlockAW_Cli_BlocksReviewApprovalMergeReleaseAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "approve", "review", "resolve-comments", "reply", "remove-reviewers", "ready", "merge", "auto-merge", "release", "deploy", "tag", "publish", "promote-memory", "continue" })
        {
            var result = await RunCliAsync("reviewer-request", forbidden, "--receipt", "receipt.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAW_StaticBoundary_ProvesNoApprovalMergeReleaseMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReviewerRequestExecution.cs"));
        Assert.IsTrue(cli.Contains("requested_reviewers", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr ready", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AW_CONTROLLED_REVIEWER_REQUEST_EXECUTOR.md"));
        StringAssert.Contains(receipt, "Reviewer request package is not reviewer request execution.");
        StringAssert.Contains(receipt, "Reviewer request execution is not approval.");
        StringAssert.Contains(receipt, "Reviewer request execution is not review completion.");
        StringAssert.Contains(receipt, "Reviewer request execution is not merge readiness.");
        StringAssert.Contains(receipt, "Reviewer request execution is not release readiness.");
        StringAssert.Contains(receipt, "Reviewer request execution does not resolve review threads.");
        StringAssert.Contains(receipt, "Approval is not merge.");
        StringAssert.Contains(receipt, "Merge is not release.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "No self-approval.");
        StringAssert.Contains(receipt, "No hidden mutation.");
        StringAssert.Contains(receipt, "AW requests only package-declared reviewers and teams.");
        StringAssert.Contains(receipt, "AW does not approve.");
        StringAssert.Contains(receipt, "AW does not merge.");
        StringAssert.Contains(receipt, "AW does not release.");
        StringAssert.Contains(receipt, "AW does not deploy.");
        StringAssert.Contains(receipt, "AW does not continue workflow.");
    }

    [TestMethod]
    public void BlockAW_ExecutionReceiptDoesNotBecomeApprovalMergeReleaseOrContinuationAuthority()
    {
        var package = CreatePackage(reviewers: ["reviewer-one"]);
        var receipt = new ReviewerRequestExecutionReceipt
        {
            ReviewerRequestExecutionId = "reviewer_request_exec_aw",
            ReviewerRequestPackageId = package.ReviewerRequestPackageId,
            Repository = package.Repository,
            PullRequestNumber = package.PullRequestNumber,
            PullRequestUrl = package.PullRequestUrl,
            PreState = GoodState(package),
            PostState = GoodState(package, requestedReviewers: ["reviewer-one"]),
            ExpectedHeadBranch = package.HeadBranch,
            ExpectedHeadSha = package.ExpectedHeadSha,
            ExpectedBaseBranch = package.BaseBranch,
            ExpectedBaseSha = package.BaseSha,
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

        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanApprove(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanSubmitReview(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanAutoMerge(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanTag(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanPublish(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanPromoteMemory(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanContinueWorkflow(receipt));
    }

    [TestMethod]
    public async Task BlockAW_Executor_RequestsOnlyPackageDeclaredTargets()
    {
        var package = CreatePackage(reviewers: ["reviewer-one"], teams: ["platform"]) with
        {
            AlreadyRequestedReviewers = ["reviewer-three"],
            SkippedReviewerTargets =
            [
                Target(ReviewerRequestTargetKind.GitHubUser, "reviewer-two", blockedReason: "AlreadySatisfied"),
                Target(ReviewerRequestTargetKind.GitHubUser, "reviewer-three", alreadyRequested: true, blockedReason: "AlreadySatisfied")
            ]
        };
        var client = new FakeReviewerRequestCommandClient
        {
            Observations =
            [
                GoodState(package),
                GoodState(package, requestedReviewers: ["reviewer-one"], requestedTeams: ["platform"])
            ]
        };

        var result = await ReviewerRequestExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(ReviewerRequestExecutionVerdict.Executed, result.Verdict);
        CollectionAssert.AreEquivalent(new[] { "reviewer-one" }, client.LastRequestedReviewers);
        CollectionAssert.AreEquivalent(new[] { "platform" }, client.LastRequestedTeams);
        CollectionAssert.DoesNotContain(client.LastRequestedReviewers, "reviewer-two");
        CollectionAssert.DoesNotContain(client.LastRequestedReviewers, "reviewer-three");
    }

    private static ReviewerRequestExecutionRequest CreateRequest(ReviewerRequestPackage? package) => new()
    {
        Package = package,
        Repository = package?.Repository ?? "owner/repo",
        PullRequestNumber = package?.PullRequestNumber ?? 0,
        ExpectedHeadBranch = package?.HeadBranch ?? "aw/controlled-reviewer-request-executor",
        ExpectedHeadSha = package?.ExpectedHeadSha ?? HeadSha,
        ExpectedBaseBranch = package?.BaseBranch ?? "phase/close-feedback-loop",
        ExpectedBaseSha = package?.BaseSha ?? BaseSha,
        RequestedBy = package?.CreatedBy ?? "builder",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T06:00:00Z")
    };

    private static ReviewerRequestPackage CreatePackage(string[]? reviewers = null, string[]? teams = null) =>
        ReviewerRequestPackageBuilder.Build(new ReviewerRequestPackageInput
        {
            ReadyExecutionReceipt = CreateReadyReceipt(),
            ObservedPullRequest = CreateObservedState(),
            Repository = "owner/repo",
            PullRequestNumber = 469,
            ExpectedHeadBranch = "aw/controlled-reviewer-request-executor",
            ExpectedHeadSha = HeadSha,
            ExpectedBaseBranch = "phase/close-feedback-loop",
            ExpectedBaseSha = BaseSha,
            RequestedReviewers = reviewers ?? [],
            RequestedTeams = teams ?? [],
            RequestRationale = "Reviewer request follows AV package evidence.",
            RequestedBy = "builder",
            PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T05:00:00Z")
        }).Package;

    private static ReviewerRequestObservedPrState CreateObservedState() => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 469,
        PullRequestUrl = "https://github.com/owner/repo/pull/469",
        PullRequestState = "open",
        PullRequestDraft = false,
        HeadBranch = "aw/controlled-reviewer-request-executor",
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
        ReadyForReviewExecutionId = "ready_review_exec_aw",
        ReadyForReviewPackageId = "ready_review_pkg_aw",
        Repository = "owner/repo",
        PullRequestNumber = 469,
        PullRequestUrl = "https://github.com/owner/repo/pull/469",
        PreState = ReadyState(draft: true),
        PostState = ReadyState(draft: false),
        ExpectedHeadBranch = "aw/controlled-reviewer-request-executor",
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
        PullRequestNumber = 469,
        PullRequestUrl = "https://github.com/owner/repo/pull/469",
        PullRequestState = "open",
        PullRequestDraft = draft,
        HeadBranch = "aw/controlled-reviewer-request-executor",
        HeadSha = HeadSha,
        BaseBranch = "phase/close-feedback-loop",
        BaseSha = BaseSha,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:00Z"),
        ObservationSucceeded = true
    };

    private static ReviewerRequestExecutionObservedPrState GoodState(
        ReviewerRequestPackage package,
        string[]? requestedReviewers = null,
        string[]? requestedTeams = null) => new()
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
        Author = package.PullRequestAuthor,
        RequestedReviewers = requestedReviewers ?? [],
        RequestedTeams = requestedTeams ?? [],
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T06:00:00Z"),
        ObservationSucceeded = true
    };

    private static ReviewerRequestTarget Target(
        ReviewerRequestTargetKind kind,
        string slugOrLogin,
        bool alreadyRequested = false,
        string? blockedReason = null) => new()
    {
        Kind = kind,
        SlugOrLogin = slugOrLogin,
        Reason = "test target",
        AlreadyRequested = alreadyRequested,
        BlockedReason = blockedReason
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

    private sealed class FakeReviewerRequestCommandClient : IReviewerRequestCommandClient
    {
        public List<ReviewerRequestExecutionObservedPrState> Observations { get; init; } = [];
        public ReviewerRequestMutationResult? MutationResult { get; init; }
        public int ObserveCalls { get; private set; }
        public int RequestReviewerCalls { get; private set; }
        public int ResolveThreadCalls { get; private set; }
        public int ReviewCommentCalls { get; private set; }
        public int ApproveCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public int DeployCalls { get; private set; }
        public int ContinueCalls { get; private set; }
        public string[] LastRequestedReviewers { get; private set; } = [];
        public string[] LastRequestedTeams { get; private set; } = [];

        public Task<ReviewerRequestExecutionObservedPrState> ObserveAsync(ReviewerRequestExecutionRequest request, CancellationToken cancellationToken)
        {
            ObserveCalls++;
            if (Observations.Count > 0)
            {
                var observed = Observations[0];
                Observations.RemoveAt(0);
                return Task.FromResult(observed);
            }

            return Task.FromResult(new ReviewerRequestExecutionObservedPrState
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
                RequestedReviewers = [],
                RequestedTeams = [],
                ObservedAtUtc = DateTimeOffset.UtcNow,
                ObservationSucceeded = false,
                ObservationError = "missing fake observation"
            });
        }

        public Task<ReviewerRequestMutationResult> RequestReviewersAsync(ReviewerRequestExecutionRequest request, CancellationToken cancellationToken)
        {
            RequestReviewerCalls++;
            var reviewers = request.Package?.RequestedReviewers
                .Where(IsExecutable)
                .Select(target => target.SlugOrLogin)
                .ToArray() ?? [];
            var teams = request.Package?.RequestedTeams
                .Where(IsExecutable)
                .Select(target => target.SlugOrLogin)
                .ToArray() ?? [];
            LastRequestedReviewers = reviewers;
            LastRequestedTeams = teams;
            return Task.FromResult(MutationResult ?? new ReviewerRequestMutationResult
            {
                Attempted = true,
                Accepted = true,
                Provider = "FakeTestClient",
                CommandOrMutationName = "request-reviewers",
                RequestedReviewers = reviewers,
                RequestedTeams = teams,
                Message = "requested",
                Error = null,
                CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T06:00:30Z")
            });
        }

        private static bool IsExecutable(ReviewerRequestTarget target) =>
            !target.AlreadyRequested &&
            !target.Duplicate &&
            !target.SelfRequest &&
            !target.PullRequestAuthorRequest &&
            string.IsNullOrWhiteSpace(target.BlockedReason);
    }
}

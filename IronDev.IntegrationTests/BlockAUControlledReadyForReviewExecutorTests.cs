using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAUControlledReadyForReviewExecutorTests
{
    private static readonly string PreHead = new('a', 40);
    private static readonly string ReadyHead = new('c', 40);
    private static readonly string BaseSha = new('b', 40);

    [TestMethod]
    public async Task BlockAU_Executor_MarksExpectedDraftPrReadyAndWritesReceipt()
    {
        var package = CreateEligiblePackage();
        var client = new FakeReadyForReviewCommandClient
        {
            Observations =
            [
                GoodState(package, draft: true),
                GoodState(package, draft: false)
            ],
            MutationResult = AcceptedMutation()
        };

        var result = await ReadyForReviewExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(ReadyForReviewExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(ReadyForReviewExecutionFailureKind.None, result.FailureKind);
        Assert.AreEqual(2, client.ObserveCalls);
        Assert.AreEqual(1, client.MarkReadyCalls);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(package.ReadyForReviewPackageId, result.Receipt!.ReadyForReviewPackageId);
        Assert.IsTrue(result.Receipt.ReadyTransitionAttempted);
        Assert.IsTrue(result.Receipt.ReadyTransitionAccepted);
        Assert.IsTrue(result.Receipt.PostStateVerified);
        Assert.AreEqual(ReadyForReviewExecutionVerdict.Executed, result.Receipt.ExecutionVerdict);
        Assert.AreEqual(ReadyForReviewExecutionFailureKind.None, result.Receipt.FailureClassification);
        AssertBoundary(result.Receipt.Boundary);
        Assert.IsFalse(result.Receipt.Boundary.CanRequestReviewers);
        Assert.IsFalse(result.Receipt.Boundary.CanApprove);
        Assert.IsFalse(result.Receipt.Boundary.CanMerge);
        Assert.IsFalse(result.Receipt.Boundary.CanRelease);
        Assert.IsFalse(result.Receipt.Boundary.CanDeploy);
        Assert.IsFalse(result.Receipt.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public async Task BlockAU_Executor_RejectsMissingBlockedIncompleteOrRejectedPackage()
    {
        var missingClient = new FakeReadyForReviewCommandClient();
        var missing = await ReadyForReviewExecutor.ExecuteAsync(CreateRequest(null), missingClient).ConfigureAwait(false);
        Assert.AreEqual(ReadyForReviewExecutionVerdict.Blocked, missing.Verdict);
        Assert.AreEqual(ReadyForReviewExecutionFailureKind.MissingPackage, missing.FailureKind);
        Assert.AreEqual(0, missingClient.ObserveCalls + missingClient.MarkReadyCalls);

        var cases = new[]
        {
            CreateEligiblePackage() with { Verdict = ReadyForReviewEligibilityVerdict.Incomplete, CanMarkReadyForReview = false },
            CreateEligiblePackage() with { Verdict = ReadyForReviewEligibilityVerdict.Blocked, BlockReasons = [ReadyForReviewBlockReason.HeadShaMismatch] },
            CreateEligiblePackage() with { Verdict = ReadyForReviewEligibilityVerdict.Rejected },
            CreateEligiblePackage() with { CanMarkReadyForReview = false },
            CreateEligiblePackage() with { BlockReasons = [ReadyForReviewBlockReason.MissingValidationEvidence] }
        };

        foreach (var package in cases)
        {
            var client = new FakeReadyForReviewCommandClient { Observations = [GoodState(package, draft: true)] };
            var result = await ReadyForReviewExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);
            Assert.AreEqual(ReadyForReviewExecutionVerdict.Blocked, result.Verdict, package.Verdict.ToString());
            Assert.AreEqual(0, client.ObserveCalls, package.Verdict.ToString());
            Assert.AreEqual(0, client.MarkReadyCalls, package.Verdict.ToString());
            Assert.IsFalse(result.Receipt!.ReadyTransitionAttempted);
        }
    }

    [TestMethod]
    public async Task BlockAU_Executor_BlocksPreStateMismatchBeforeMutation()
    {
        var package = CreateEligiblePackage();
        var cases = new (string Name, ReadyForReviewObservedPrState State, ReadyForReviewExecutionFailureKind ExpectedKind)[]
        {
            ("closed", GoodState(package, draft: true) with { PullRequestState = "closed" }, ReadyForReviewExecutionFailureKind.PullRequestNotOpen),
            ("already-ready", GoodState(package, draft: false), ReadyForReviewExecutionFailureKind.PullRequestAlreadyReady),
            ("wrong-pr", GoodState(package, draft: true) with { PullRequestNumber = 999 }, ReadyForReviewExecutionFailureKind.PullRequestNumberMismatch),
            ("wrong-repo", GoodState(package, draft: true) with { Repository = "other/repo" }, ReadyForReviewExecutionFailureKind.RepositoryMismatch),
            ("wrong-branch", GoodState(package, draft: true) with { HeadBranch = "other/branch" }, ReadyForReviewExecutionFailureKind.HeadBranchMismatch),
            ("head-drift", GoodState(package, draft: true) with { HeadSha = new string('d', 40) }, ReadyForReviewExecutionFailureKind.HeadShaMismatch),
            ("base-branch", GoodState(package, draft: true) with { BaseBranch = "develop" }, ReadyForReviewExecutionFailureKind.BaseBranchMismatch),
            ("base-sha", GoodState(package, draft: true) with { BaseSha = new string('e', 40) }, ReadyForReviewExecutionFailureKind.BaseShaMismatch)
        };

        foreach (var item in cases)
        {
            var client = new FakeReadyForReviewCommandClient { Observations = [item.State] };
            var result = await ReadyForReviewExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);
            Assert.AreEqual(ReadyForReviewExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.AreEqual(item.ExpectedKind, result.FailureKind, item.Name);
            Assert.AreEqual(1, client.ObserveCalls, item.Name);
            Assert.AreEqual(0, client.MarkReadyCalls, item.Name);
            Assert.IsFalse(result.Receipt!.ReadyTransitionAttempted, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAU_Executor_FailsIfPostReadyStateDoesNotVerify()
    {
        var package = CreateEligiblePackage();
        var cases = new (string Name, ReadyForReviewObservedPrState PostState)[]
        {
            ("still-draft", GoodState(package, draft: true)),
            ("head-drift", GoodState(package, draft: false) with { HeadSha = new string('d', 40) }),
            ("base-branch", GoodState(package, draft: false) with { BaseBranch = "develop" }),
            ("observation-failed", GoodState(package, draft: false) with { ObservationSucceeded = false, ObservationError = "post observe failed" })
        };

        foreach (var item in cases)
        {
            var client = new FakeReadyForReviewCommandClient
            {
                Observations =
                [
                    GoodState(package, draft: true),
                    item.PostState
                ],
                MutationResult = AcceptedMutation()
            };
            var result = await ReadyForReviewExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);
            Assert.AreEqual(ReadyForReviewExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.AreEqual(ReadyForReviewExecutionFailureKind.PostReadyVerificationFailed, result.FailureKind, item.Name);
            Assert.AreEqual(1, client.MarkReadyCalls, item.Name);
            Assert.IsTrue(result.Receipt!.ReadyTransitionAttempted, item.Name);
            Assert.IsTrue(result.Receipt.ReadyTransitionAccepted, item.Name);
            Assert.IsFalse(result.Receipt.PostStateVerified, item.Name);
        }
    }

    [TestMethod]
    public async Task BlockAU_Executor_DoesNotRequestReviewersAfterReady()
    {
        var package = CreateEligiblePackage();
        var client = new FakeReadyForReviewCommandClient
        {
            Observations =
            [
                GoodState(package, draft: true),
                GoodState(package, draft: false)
            ],
            MutationResult = AcceptedMutation()
        };

        var result = await ReadyForReviewExecutor.ExecuteAsync(CreateRequest(package), client).ConfigureAwait(false);

        Assert.AreEqual(ReadyForReviewExecutionVerdict.Executed, result.Verdict);
        Assert.AreEqual(1, client.MarkReadyCalls);
        Assert.AreEqual(0, client.RequestReviewerCalls);
        Assert.AreEqual(0, client.ReviewCommentCalls);
        Assert.AreEqual(0, client.MergeCalls);
        Assert.AreEqual(0, client.ReleaseCalls);
        Assert.AreEqual(0, client.DeployCalls);
    }

    [TestMethod]
    public async Task BlockAU_Cli_BlocksReviewerReviewMergeReleaseAndContinuationVerbs()
    {
        foreach (var forbidden in new[] { "execute-request-reviewers", "request-reviewers", "resolve-comments", "approve", "review", "merge", "auto-merge", "release", "deploy", "tag", "publish", "promote-memory", "continue" })
        {
            var result = await RunCliAsync("ready", forbidden, "--receipt", "receipt.json").ConfigureAwait(false);
            Assert.AreEqual(2, result.ExitCode, forbidden);
            StringAssert.Contains(result.Error, "intentionally unsupported");
        }
    }

    [TestMethod]
    public void BlockAU_StaticBoundary_ProvesNoReviewerMergeReleaseMutationSurface()
    {
        var root = FindRepositoryRoot();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliReadyForReview.cs"));
        Assert.IsTrue(cli.Contains("\"ready\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("request_pull_request_reviewers", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr review", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh pr merge", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("gh release", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("git push", StringComparison.OrdinalIgnoreCase));

        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "AU_CONTROLLED_READY_FOR_REVIEW_EXECUTOR.md"));
        StringAssert.Contains(receipt, "Ready-for-review package is not ready-for-review execution.");
        StringAssert.Contains(receipt, "Ready-for-review execution is not reviewer request.");
        StringAssert.Contains(receipt, "Reviewer request is not approval.");
        StringAssert.Contains(receipt, "Approval is not merge.");
        StringAssert.Contains(receipt, "Merge is not release.");
        StringAssert.Contains(receipt, "Release is not deployment.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "AU marks only the expected draft PR ready for review.");
        StringAssert.Contains(receipt, "AU does not request reviewers.");
        StringAssert.Contains(receipt, "AU does not resolve review threads.");
        StringAssert.Contains(receipt, "AU does not approve.");
        StringAssert.Contains(receipt, "AU does not merge.");
        StringAssert.Contains(receipt, "AU does not release.");
        StringAssert.Contains(receipt, "AU does not deploy.");
        StringAssert.Contains(receipt, "AU does not continue workflow.");
    }

    [TestMethod]
    public void BlockAU_ExecutionReceiptDoesNotBecomeReviewerRequestPackage()
    {
        var package = CreateEligiblePackage();
        var receipt = new ReadyForReviewExecutionReceipt
        {
            ReadyForReviewExecutionId = "ready_review_exec_au",
            ReadyForReviewPackageId = package.ReadyForReviewPackageId,
            Repository = package.Target.Repository,
            PullRequestNumber = package.Target.PullRequestNumber,
            PullRequestUrl = package.Target.PullRequestUrl,
            PreState = GoodState(package, draft: true),
            PostState = GoodState(package, draft: false),
            ExpectedHeadBranch = package.Target.HeadBranch,
            ExpectedHeadSha = package.Target.ExpectedHeadSha,
            ExpectedBaseBranch = package.Target.BaseBranch,
            ExpectedBaseSha = package.Target.BaseSha,
            ReadyTransitionAttempted = true,
            ReadyTransitionAccepted = true,
            PostStateVerified = true,
            ExecutionVerdict = ReadyForReviewExecutionVerdict.Executed,
            FailureClassification = ReadyForReviewExecutionFailureKind.None,
            RequestedBy = "tests",
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:00Z"),
            ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T04:01:00Z"),
            Boundary = ReadyForReviewExecutionBoundary.Executor
        };

        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanRequestReviewers(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanApprove(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanContinueWorkflow(receipt));
    }

    private static ReadyForReviewExecutionRequest CreateRequest(ReadyForReviewEligibilityPackage? package) => new()
    {
        Package = package,
        Repository = package?.Target.Repository ?? "owner/repo",
        PullRequestNumber = package?.Target.PullRequestNumber ?? 0,
        ExpectedHeadBranch = package?.Target.HeadBranch ?? "au/controlled-ready-for-review-executor",
        ExpectedHeadSha = package?.Target.ExpectedHeadSha ?? ReadyHead,
        ExpectedBaseBranch = package?.Target.BaseBranch ?? "phase/close-feedback-loop",
        ExpectedBaseSha = package?.Target.BaseSha ?? BaseSha,
        RequestedBy = "tests",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:00Z")
    };

    private static ReadyForReviewEligibilityPackage CreateEligiblePackage()
    {
        var asReceipt = CreateBranchUpdateReceipt();
        return ReadyForReviewSeparationBuilder.Build(new ReadyForReviewSeparationInput
        {
            Repository = "owner/repo",
            PullRequestNumber = 467,
            PullRequestUrl = "https://github.com/owner/repo/pull/467",
            PullRequestState = "open",
            PullRequestDraft = true,
            HeadBranch = "au/controlled-ready-for-review-executor",
            ExpectedHeadSha = ReadyHead,
            ObservedHeadSha = ReadyHead,
            BaseBranch = "phase/close-feedback-loop",
            BaseSha = BaseSha,
            ExpectedBaseBranch = "phase/close-feedback-loop",
            ExpectedBaseSha = BaseSha,
            BranchUpdateReceipt = asReceipt,
            ValidationReceipts = [CreateValidationReceipt(ReadyHead)],
            PhaseAuthorityReceiptId = "PHASE1_CLOSE_FEEDBACK_LOOP",
            PhaseAuthorityReceiptText = PhaseReceiptText(),
            PackageCreatedBy = "tests",
            PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T03:00:00Z")
        }).Package;
    }

    private static PrBranchUpdateExecutionReceipt CreateBranchUpdateReceipt() => new()
    {
        ExecutionId = "pr_branch_update_exec_au",
        PackageId = "pr_update_pkg_au",
        Repository = "owner/repo",
        PrNumber = 467,
        Branch = "au/controlled-ready-for-review-executor",
        PreExecutionHeadSha = PreHead,
        PostExecutionHeadSha = ReadyHead,
        CommitSha = ReadyHead,
        Pushed = true,
        PushRemote = "origin",
        PushBranch = "au/controlled-ready-for-review-executor",
        SourceApplyReceipt = "source_apply_au",
        ValidationReceipts = ["validation_au"],
        DirtyWorktreeBefore = true,
        DirtyWorktreeAfter = false,
        ExpectedFilesChanged = ["IronDev.Core/Governance/ControlledReadyForReviewExecutor.cs"],
        ActualFilesChanged = ["IronDev.Core/Governance/ControlledReadyForReviewExecutor.cs"],
        RollbackAvailable = true,
        RollbackInstructions = "Rollback plan is not rollback execution.",
        ExecutionVerdict = PrBranchUpdateExecutionVerdict.Executed,
        FailureClassification = PrBranchUpdateFailureKind.None,
        Issues = [],
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T03:01:00Z"),
        Boundary = PrBranchUpdateBoundary.Executor
    };

    private static ReadyForReviewObservedPrState GoodState(ReadyForReviewEligibilityPackage package, bool draft) => new()
    {
        Repository = package.Target.Repository,
        PullRequestNumber = package.Target.PullRequestNumber,
        PullRequestUrl = package.Target.PullRequestUrl,
        PullRequestState = "open",
        PullRequestDraft = draft,
        HeadBranch = package.Target.HeadBranch,
        HeadSha = package.Target.ExpectedHeadSha,
        BaseBranch = package.Target.BaseBranch,
        BaseSha = package.Target.BaseSha,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:00Z"),
        ObservationSucceeded = true
    };

    private static ReadyForReviewMutationResult AcceptedMutation() => new()
    {
        Attempted = true,
        Accepted = true,
        Provider = "FakeTestClient",
        CommandOrMutationName = "mark-ready",
        Message = "ready",
        Error = null,
        CompletedAtUtc = DateTimeOffset.Parse("2026-06-20T04:00:30Z")
    };

    private static ValidationRunReceipt CreateValidationReceipt(string commitSha)
    {
        var lanes = new[]
        {
            Lane("focused-au"),
            Lane("impacted-governance-tests"),
            Lane("fast-authority-invariants"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck),
            Lane("phase-authority")
        };
        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_au_" + Guid.NewGuid().ToString("N")[..8],
            ValidationPlanId = "validation_plan_au",
            Branch = "au/controlled-ready-for-review-executor",
            CommitSha = commitSha,
            ChangedFilesHash = Hash("changed-files"),
            StartedUtc = DateTimeOffset.Parse("2026-06-20T03:02:00Z"),
            FinishedUtc = DateTimeOffset.Parse("2026-06-20T03:03:00Z"),
            Verdict = ValidationRunVerdict.Passed,
            RequiredLanes = lanes,
            Results = lanes.Select(Result).ToArray(),
            SkippedLanes = [],
            SkippedLaneReasons = [],
            WorktreeCleanBefore = true,
            WorktreeCleanAfter = true,
            CachePolicy = new ValidationCachePolicy(),
            Boundary = ValidationRuntimeBoundary.Evidence
        };
    }

    private static ValidationLane Lane(string name, ValidationCommandKind kind = ValidationCommandKind.Test) => new()
    {
        Name = name,
        Reason = $"Required AU lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "au",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = DateTimeOffset.Parse("2026-06-20T03:02:00Z"),
        FinishedUtc = DateTimeOffset.Parse("2026-06-20T03:02:10Z"),
        DurationMs = 10000,
        ExitCode = 0,
        TimedOut = false,
        Cancelled = false,
        ProcessTreeKillAttempted = false,
        ProcessTreeKillSucceeded = false,
        StdoutPath = "stdout.log",
        StderrPath = "stderr.log",
        FailureClassification = ValidationFailureKind.Passed
    };

    private static string PhaseReceiptText() => """
        # Phase 1 Close Feedback Loop

        Phase 1 closes the feedback loop.

        PR branch update is not ready-for-review.

        Validation evidence is not approval.
        """;

    private static void AssertBoundary(ReadyForReviewExecutionBoundary boundary)
    {
        Assert.IsTrue(boundary.CanMarkReadyForReview);
        Assert.IsFalse(boundary.CanRequestReviewers);
        Assert.IsFalse(boundary.CanResolveReviewThreads);
        Assert.IsFalse(boundary.CanApprove);
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

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FakeReadyForReviewCommandClient : IReadyForReviewCommandClient
    {
        public List<ReadyForReviewObservedPrState> Observations { get; init; } = [];
        public ReadyForReviewMutationResult MutationResult { get; init; } = AcceptedMutation();
        public int ObserveCalls { get; private set; }
        public int MarkReadyCalls { get; private set; }
        public int RequestReviewerCalls { get; private set; }
        public int ReviewCommentCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public int DeployCalls { get; private set; }

        public Task<ReadyForReviewObservedPrState> ObserveAsync(ReadyForReviewExecutionRequest request, CancellationToken cancellationToken)
        {
            ObserveCalls++;
            if (Observations.Count > 0)
            {
                var observed = Observations[0];
                Observations.RemoveAt(0);
                return Task.FromResult(observed);
            }

            return Task.FromResult(new ReadyForReviewObservedPrState
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
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                    ObservationSucceeded = false,
                    ObservationError = "missing fake observation"
                });
        }

        public Task<ReadyForReviewMutationResult> MarkReadyAsync(ReadyForReviewExecutionRequest request, CancellationToken cancellationToken)
        {
            MarkReadyCalls++;
            return Task.FromResult(MutationResult);
        }
    }
}

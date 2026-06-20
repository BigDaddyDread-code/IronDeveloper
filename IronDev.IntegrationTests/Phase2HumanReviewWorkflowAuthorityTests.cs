using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class Phase2HumanReviewWorkflowAuthorityTests
{
    private static readonly string HeadSha = new('a', 40);
    private static readonly string BaseSha = new('b', 40);

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AtPackageDoesNotBecomeReadyForReviewExecution()
    {
        var package = CreateReadyPackage();

        Assert.AreEqual(ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor, package.Verdict);
        Assert.IsTrue(package.CanMarkReadyForReview);
        Assert.IsTrue(package.Boundary.EvidenceOnly);
        Assert.IsFalse(package.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(package.Boundary.CanRequestReviewers);
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMarkReadyForReview(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanRequestReviewers(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanApprove(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMerge(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanContinueWorkflow(package));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AuExecutionDoesNotBecomeReviewerRequest()
    {
        var receipt = CreateReadyExecutionReceipt();

        Assert.AreEqual(ReadyForReviewExecutionVerdict.Executed, receipt.ExecutionVerdict);
        Assert.IsTrue(receipt.ReadyTransitionAttempted);
        Assert.IsTrue(receipt.ReadyTransitionAccepted);
        Assert.IsTrue(receipt.PostStateVerified);
        Assert.IsTrue(receipt.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(receipt.Boundary.CanRequestReviewers);
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanRequestReviewers(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanApprove(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(ReadyForReviewExecutionBypassEvaluator.CanContinueWorkflow(receipt));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AvPackageDoesNotBecomeReviewerRequestExecution()
    {
        var package = CreateReviewerRequestPackage();

        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor, package.PackageVerdict);
        Assert.IsTrue(package.CanRequestReviewersForExecutor);
        Assert.IsTrue(package.Boundary.EvidenceOnly);
        Assert.IsFalse(package.Boundary.CanRequestReviewers);
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanRequestReviewers(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanApprove(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanMerge(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanContinueWorkflow(package));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AwExecutionDoesNotBecomeReviewCompletion()
    {
        var receipt = CreateReviewerRequestExecutionReceipt();

        Assert.AreEqual(ReviewerRequestExecutionVerdict.Executed, receipt.ExecutionVerdict);
        Assert.IsTrue(receipt.ReviewerRequestAttempted);
        Assert.IsTrue(receipt.ReviewerRequestAccepted);
        Assert.IsTrue(receipt.PostStateVerified);
        Assert.IsTrue(receipt.Boundary.CanRequestReviewers);
        Assert.IsFalse(receipt.Boundary.CanResolveReviewThreads);
        Assert.IsFalse(receipt.Boundary.CanReplyToReviewThreads);
        Assert.IsFalse(receipt.Boundary.CanSubmitReview);
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanSubmitReview(receipt));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AwExecutionDoesNotBecomeApproval()
    {
        var receipt = CreateReviewerRequestExecutionReceipt();

        Assert.IsFalse(receipt.Boundary.CanApprove);
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanApprove(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanSubmitReview(receipt));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AwExecutionDoesNotBecomeMergeReadiness()
    {
        var receipt = CreateReviewerRequestExecutionReceipt();

        Assert.IsFalse(receipt.Boundary.CanMerge);
        Assert.IsFalse(receipt.Boundary.CanAutoMerge);
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanAutoMerge(receipt));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_AwExecutionDoesNotBecomeReleaseOrDeploymentReadiness()
    {
        var receipt = CreateReviewerRequestExecutionReceipt();

        Assert.IsFalse(receipt.Boundary.CanRelease);
        Assert.IsFalse(receipt.Boundary.CanDeploy);
        Assert.IsFalse(receipt.Boundary.CanTag);
        Assert.IsFalse(receipt.Boundary.CanPublish);
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanTag(receipt));
        Assert.IsFalse(ReviewerRequestExecutionBypassEvaluator.CanPublish(receipt));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_ValidationEvidenceDoesNotBecomeApproval()
    {
        var receipt = CreateValidationReceipt();

        Assert.AreEqual(ValidationRunVerdict.Passed, receipt.Verdict);
        Assert.IsTrue(receipt.Boundary.EvidenceOnly);
        Assert.IsFalse(receipt.Boundary.CanApprove);
        Assert.IsFalse(receipt.Boundary.CanSatisfyPolicy);
        Assert.IsFalse(receipt.Boundary.CanMerge);
        Assert.IsFalse(receipt.Boundary.CanRelease);
        Assert.IsFalse(receipt.Boundary.CanDeploy);
        Assert.IsFalse(receipt.Boundary.CanCommit);
        Assert.IsFalse(receipt.Boundary.CanPush);
        Assert.IsFalse(receipt.Boundary.CanMutateSource);
        Assert.IsFalse(receipt.Boundary.CanMutateWorkspace);
        Assert.IsFalse(receipt.Boundary.CanRequestReviewers);
        Assert.IsFalse(receipt.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(receipt.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_NoSelfApprovalThroughReviewerRequestPackage()
    {
        var package = BuildReviewerRequestPackage(requestedBy: "builder", reviewers: ["builder"]);

        Assert.AreEqual(ReviewerRequestPackageVerdict.PackageBlocked, package.PackageVerdict);
        Assert.IsFalse(package.CanRequestReviewersForExecutor);
        CollectionAssert.Contains(package.BlockReasons, ReviewerRequestPackageBlockReason.RequestedReviewerIsRequester);
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanApprove(package));
        Assert.IsFalse(ReviewerRequestPackageBypassEvaluator.CanRequestReviewers(package));
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_NoHiddenMutationAcrossPhaseArtifacts()
    {
        var readyPackage = CreateReadyPackage();
        var readyExecution = CreateReadyExecutionReceipt();
        var reviewerPackage = CreateReviewerRequestPackage();
        var reviewerExecution = CreateReviewerRequestExecutionReceipt();

        AssertNoHiddenMutation(readyPackage.Boundary);
        AssertNoHiddenMutation(readyExecution.Boundary);
        AssertNoHiddenMutation(reviewerPackage.Boundary);
        AssertNoHiddenMutation(reviewerExecution.Boundary);
    }

    [TestMethod]
    public void Phase2HumanReviewWorkflowAuthority_DurableReceiptRecordsPhaseBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PHASE2_HUMAN_REVIEW_WORKFLOW.md"));

        StringAssert.Contains(receipt, "Ready-for-review package is not ready-for-review execution.");
        StringAssert.Contains(receipt, "Ready-for-review execution is not reviewer request.");
        StringAssert.Contains(receipt, "Reviewer request package is not reviewer request execution.");
        StringAssert.Contains(receipt, "Reviewer request execution is not review completion.");
        StringAssert.Contains(receipt, "Reviewer request execution is not approval.");
        StringAssert.Contains(receipt, "Reviewer request execution is not merge readiness.");
        StringAssert.Contains(receipt, "Reviewer request execution is not release readiness.");
        StringAssert.Contains(receipt, "Reviewer request execution is not deployment readiness.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "No self-approval.");
        StringAssert.Contains(receipt, "No hidden mutation.");
        StringAssert.Contains(receipt, "The combined AT/AU/AV/AW boundary lane remains useful, but it is not a substitute for the explicit phase cross-boundary authority lane.");
    }

    private static ReadyForReviewEligibilityPackage CreateReadyPackage() => new()
    {
        ReadyForReviewPackageId = "ready_review_pkg_phase2",
        Target = new ReadyForReviewTarget
        {
            Repository = "owner/repo",
            PullRequestNumber = 470,
            PullRequestUrl = "https://github.com/owner/repo/pull/470",
            PullRequestState = "open",
            PullRequestDraft = true,
            HeadBranch = "phase/close-feedback-loop",
            ExpectedHeadSha = HeadSha,
            ObservedHeadSha = HeadSha,
            BaseBranch = "main",
            BaseSha = BaseSha
        },
        BranchUpdateEvidence = new ReadyForReviewEvidence
        {
            BranchUpdateReceiptId = "pr_branch_update_exec_phase2",
            BranchUpdatePackageId = "pr_update_pkg_phase2",
            BranchUpdateVerdict = PrBranchUpdateExecutionVerdict.Executed,
            BranchUpdateCommitSha = HeadSha,
            BranchUpdatePostHeadSha = HeadSha,
            BranchUpdatePushed = true,
            EvidenceRefs = ["pr-branch-update-execution-receipt.json"]
        },
        ValidationEvidence =
        [
            new ReadyForReviewValidationEvidence
            {
                ValidationRunId = "validation_run_phase2",
                ValidationPlanId = "validation_plan_phase2",
                CommitSha = HeadSha,
                Verdict = ValidationRunVerdict.Passed,
                RequiredLaneNames = ["focused-phase2", "build", "diff-check"],
                ResultLaneNames = ["focused-phase2", "build", "diff-check"],
                SatisfiedFamilies =
                [
                    ReadyForReviewValidationFamily.FocusedCurrentBlock,
                    ReadyForReviewValidationFamily.ImpactedArea,
                    ReadyForReviewValidationFamily.FastAuthorityInvariant,
                    ReadyForReviewValidationFamily.Build,
                    ReadyForReviewValidationFamily.DiffCheck,
                    ReadyForReviewValidationFamily.PhaseAuthority
                ]
            }
        ],
        RequiredValidationFamilies =
        [
            ReadyForReviewValidationFamily.FocusedCurrentBlock,
            ReadyForReviewValidationFamily.ImpactedArea,
            ReadyForReviewValidationFamily.FastAuthorityInvariant,
            ReadyForReviewValidationFamily.Build,
            ReadyForReviewValidationFamily.DiffCheck,
            ReadyForReviewValidationFamily.PhaseAuthority
        ],
        MissingValidationFamilies = [],
        PhaseAuthorityReceiptId = "phase2-human-review-workflow",
        PhaseAuthorityReceiptValid = true,
        Verdict = ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor,
        CanMarkReadyForReview = true,
        BlockReasons = [],
        PackageIssues = [],
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z"),
        CreatedBy = "tests",
        Boundary = ReadyForReviewBoundary.Evidence
    };

    private static ReadyForReviewExecutionReceipt CreateReadyExecutionReceipt() => new()
    {
        ReadyForReviewExecutionId = "ready_review_exec_phase2",
        ReadyForReviewPackageId = "ready_review_pkg_phase2",
        Repository = "owner/repo",
        PullRequestNumber = 470,
        PullRequestUrl = "https://github.com/owner/repo/pull/470",
        PreState = ReadyState(draft: true),
        PostState = ReadyState(draft: false),
        ExpectedHeadBranch = "phase/close-feedback-loop",
        ExpectedHeadSha = HeadSha,
        ExpectedBaseBranch = "main",
        ExpectedBaseSha = BaseSha,
        ReadyTransitionAttempted = true,
        ReadyTransitionAccepted = true,
        PostStateVerified = true,
        ExecutionVerdict = ReadyForReviewExecutionVerdict.Executed,
        FailureClassification = ReadyForReviewExecutionFailureKind.None,
        RequestedBy = "builder",
        RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T08:01:00Z"),
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T08:02:00Z"),
        Boundary = ReadyForReviewExecutionBoundary.Executor
    };

    private static ReadyForReviewObservedPrState ReadyState(bool draft) => new()
    {
        Repository = "owner/repo",
        PullRequestNumber = 470,
        PullRequestUrl = "https://github.com/owner/repo/pull/470",
        PullRequestState = "open",
        PullRequestDraft = draft,
        HeadBranch = "phase/close-feedback-loop",
        HeadSha = HeadSha,
        BaseBranch = "main",
        BaseSha = BaseSha,
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T08:00:00Z"),
        ObservationSucceeded = true
    };

    private static ReviewerRequestPackage CreateReviewerRequestPackage() =>
        BuildReviewerRequestPackage(requestedBy: "builder", reviewers: ["reviewer-one"]);

    private static ReviewerRequestPackage BuildReviewerRequestPackage(string requestedBy, string[] reviewers) =>
        ReviewerRequestPackageBuilder.Build(new ReviewerRequestPackageInput
        {
            ReadyExecutionReceipt = CreateReadyExecutionReceipt(),
            ObservedPullRequest = new ReviewerRequestObservedPrState
            {
                Repository = "owner/repo",
                PullRequestNumber = 470,
                PullRequestUrl = "https://github.com/owner/repo/pull/470",
                PullRequestState = "open",
                PullRequestDraft = false,
                HeadBranch = "phase/close-feedback-loop",
                HeadSha = HeadSha,
                BaseBranch = "main",
                BaseSha = BaseSha,
                ExistingRequestedReviewers = [],
                ExistingRequestedTeams = [],
                Author = "author-one",
                ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T08:03:00Z"),
                ObservationSource = "test"
            },
            Repository = "owner/repo",
            PullRequestNumber = 470,
            ExpectedHeadBranch = "phase/close-feedback-loop",
            ExpectedHeadSha = HeadSha,
            ExpectedBaseBranch = "main",
            ExpectedBaseSha = BaseSha,
            RequestedReviewers = reviewers,
            RequestedTeams = [],
            RequestRationale = "Phase 2 reviewer request package evidence.",
            RequestedBy = requestedBy,
            PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T08:04:00Z")
        }).Package;

    private static ReviewerRequestExecutionReceipt CreateReviewerRequestExecutionReceipt()
    {
        var package = CreateReviewerRequestPackage();
        return new ReviewerRequestExecutionReceipt
        {
            ReviewerRequestExecutionId = "reviewer_request_exec_phase2",
            ReviewerRequestPackageId = package.ReviewerRequestPackageId,
            Repository = package.Repository,
            PullRequestNumber = package.PullRequestNumber,
            PullRequestUrl = package.PullRequestUrl,
            PreState = ReviewerState(package),
            PostState = ReviewerState(package, requestedReviewers: ["reviewer-one"]),
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
            RequestedAtUtc = DateTimeOffset.Parse("2026-06-20T08:05:00Z"),
            ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T08:06:00Z"),
            Boundary = ReviewerRequestExecutionBoundary.Executor
        };
    }

    private static ReviewerRequestExecutionObservedPrState ReviewerState(
        ReviewerRequestPackage package,
        string[]? requestedReviewers = null) => new()
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
        RequestedTeams = [],
        ObservedAtUtc = DateTimeOffset.Parse("2026-06-20T08:05:00Z"),
        ObservationSucceeded = true
    };

    private static ValidationRunReceipt CreateValidationReceipt()
    {
        var lanes = new[]
        {
            Lane("focused-phase2"),
            Lane("human-review-authority"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck)
        };

        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_phase2",
            ValidationPlanId = "validation_plan_phase2",
            Branch = "phase/close-feedback-loop",
            CommitSha = HeadSha,
            ChangedFilesHash = Hash("phase2-changed-files"),
            StartedUtc = DateTimeOffset.Parse("2026-06-20T08:07:00Z"),
            FinishedUtc = DateTimeOffset.Parse("2026-06-20T08:08:00Z"),
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
        Reason = $"Required Phase 2 lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "phase2",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = DateTimeOffset.Parse("2026-06-20T08:07:00Z"),
        FinishedUtc = DateTimeOffset.Parse("2026-06-20T08:07:10Z"),
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

    private static void AssertNoHiddenMutation(ReadyForReviewBoundary boundary)
    {
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertNoHiddenMutation(ReadyForReviewExecutionBoundary boundary)
    {
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertNoHiddenMutation(ReviewerRequestPackageBoundary boundary)
    {
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
    }

    private static void AssertNoHiddenMutation(ReviewerRequestExecutionBoundary boundary)
    {
        Assert.IsFalse(boundary.CanMutateSource);
        Assert.IsFalse(boundary.CanMutateWorkspace);
        Assert.IsFalse(boundary.CanCommit);
        Assert.IsFalse(boundary.CanPush);
        Assert.IsFalse(boundary.CanPromoteMemory);
        Assert.IsFalse(boundary.CanContinueWorkflow);
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
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

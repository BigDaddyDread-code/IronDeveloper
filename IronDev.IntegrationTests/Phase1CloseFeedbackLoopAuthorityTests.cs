using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class Phase1CloseFeedbackLoopAuthorityTests
{
    private static readonly string PreHead = new('a', 40);
    private static readonly string BaseSha = new('b', 40);
    private static readonly string PostHead = new('f', 40);

    [TestMethod]
    public void Phase1CloseFeedbackLoopAuthority_ApPackageDoesNotBecomeAqPatchProposalAuthority()
    {
        var package = CreateFeedbackPackage();

        Assert.IsTrue(package.Boundary.EvidenceOnly);
        Assert.IsFalse(package.Boundary.CanProposePatch);
        Assert.IsFalse(package.Boundary.CanApplySource);
        Assert.IsFalse(package.Boundary.CanUpdatePullRequest);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanProposePatch(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanApplySource(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanUpdatePullRequest(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanApprove(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanMarkReadyForReview(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanRequestReviewers(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanMerge(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanRelease(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(FeedbackRemediationBypassEvaluator.CanContinueWorkflow(package));
    }

    [TestMethod]
    public void Phase1CloseFeedbackLoopAuthority_AqProposalDoesNotBecomeSourceApplyOrPrUpdateAuthority()
    {
        var proposal = CreatePatchProposal();

        Assert.AreEqual(FeedbackPatchProposalVerdict.ProposalCreated, proposal.Verdict);
        Assert.AreEqual(FeedbackPatchApplicability.ManualReviewOnly, proposal.ProposedHunks.Single().PatchApplicability);
        Assert.IsTrue(proposal.Boundary.EvidenceOnly);
        Assert.IsFalse(proposal.Boundary.CanApplySource);
        Assert.IsFalse(proposal.Boundary.CanMutateWorkspace);
        Assert.IsFalse(proposal.Boundary.CanCommit);
        Assert.IsFalse(proposal.Boundary.CanPush);
        Assert.IsFalse(proposal.Boundary.CanUpdatePullRequest);
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanApplySource(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanCommit(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanPush(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanUpdatePullRequest(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanApprove(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanMarkReadyForReview(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanRequestReviewers(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanMerge(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanRelease(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanDeploy(proposal));
        Assert.IsFalse(FeedbackPatchProposalBypassEvaluator.CanContinueWorkflow(proposal));
    }

    [TestMethod]
    public void Phase1CloseFeedbackLoopAuthority_ArPackageDoesNotBecomeBranchMutationAuthority()
    {
        var package = CreateReadyPrUpdatePackage();

        Assert.AreEqual(PrUpdatePackageVerdict.PackageReadyForExecutor, package.Verdict);
        Assert.AreEqual(PrUpdateExecutionEligibility.Eligible, package.ExecutionEligibility);
        Assert.IsTrue(package.CanExecuteBranchUpdate);
        Assert.IsTrue(package.Boundary.EvidenceOnly);
        Assert.IsFalse(package.Boundary.CanApplyPatch);
        Assert.IsFalse(package.Boundary.CanMutateSource);
        Assert.IsFalse(package.Boundary.CanMutateWorkspace);
        Assert.IsFalse(package.Boundary.CanStage);
        Assert.IsFalse(package.Boundary.CanCommit);
        Assert.IsFalse(package.Boundary.CanPush);
        Assert.IsFalse(package.Boundary.CanUpdatePullRequest);
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanApplyPatch(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanMutateSource(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanCommit(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanPush(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanUpdatePullRequest(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanMarkReadyForReview(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanRequestReviewers(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanApprove(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanMerge(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanRelease(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanDeploy(package));
        Assert.IsFalse(ControlledPrUpdatePackageBypassEvaluator.CanContinueWorkflow(package));
    }

    [TestMethod]
    public void Phase1CloseFeedbackLoopAuthority_AsExecutionReceiptDoesNotBecomeReviewMergeReleaseOrContinuationAuthority()
    {
        var receipt = CreateBranchUpdateReceipt();

        Assert.AreEqual(PrBranchUpdateExecutionVerdict.Executed, receipt.ExecutionVerdict);
        Assert.IsTrue(receipt.Boundary.CanMutatePullRequestBranch);
        Assert.IsFalse(receipt.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(receipt.Boundary.CanRequestReviewers);
        Assert.IsFalse(receipt.Boundary.CanResolveReviewThreads);
        Assert.IsFalse(receipt.Boundary.CanApprove);
        Assert.IsFalse(receipt.Boundary.CanMerge);
        Assert.IsFalse(receipt.Boundary.CanRelease);
        Assert.IsFalse(receipt.Boundary.CanDeploy);
        Assert.IsFalse(receipt.Boundary.CanTag);
        Assert.IsFalse(receipt.Boundary.CanPublish);
        Assert.IsFalse(receipt.Boundary.CanPromoteMemory);
        Assert.IsFalse(receipt.Boundary.CanContinueWorkflow);
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanMarkReadyForReview(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanRequestReviewers(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanResolveReviewThreads(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanApprove(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanMerge(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanRelease(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanDeploy(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanTag(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanPublish(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanPromoteMemory(receipt));
        Assert.IsFalse(PrBranchUpdateBypassEvaluator.CanContinueWorkflow(receipt));
    }

    [TestMethod]
    public void Phase1CloseFeedbackLoopAuthority_ValidationEvidenceDoesNotBecomeApprovalOrPolicyAuthority()
    {
        var receipt = CreateValidationReceipt(PostHead);

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
    public void Phase1CloseFeedbackLoopAuthority_RollbackPlanDoesNotBecomeRollbackExecution()
    {
        var package = CreateReadyPrUpdatePackage();
        var rollbackPlan = CreateRollbackPlan();

        Assert.IsTrue(package.RollbackPlan.RollbackAvailable);
        CollectionAssert.Contains(package.RollbackPlan.RollbackRisks, "Rollback plan is not rollback execution.");
        CollectionAssert.Contains(package.RollbackPlan.RollbackRisks, "Rollback cannot be executed by AR.");
        Assert.IsTrue(package.RollbackPlan.Boundary.EvidenceOnly);
        Assert.IsFalse(package.RollbackPlan.Boundary.CanMutateSource);
        Assert.IsFalse(package.RollbackPlan.Boundary.CanCommit);
        Assert.IsFalse(package.RollbackPlan.Boundary.CanPush);
        Assert.IsFalse(package.RollbackPlan.Boundary.CanUpdatePullRequest);
        Assert.IsFalse(package.RollbackPlan.Boundary.CanContinueWorkflow);
        StringAssert.Contains(rollbackPlan.Boundary, "Rollback plan is not rollback execution.");
        StringAssert.Contains(rollbackPlan.Boundary, "Rollback plan does not authorize source mutation by itself.");
    }

    [TestMethod]
    public void Phase1CloseFeedbackLoopAuthority_DurableReceiptRecordsPhaseBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PHASE1_CLOSE_FEEDBACK_LOOP.md"));

        StringAssert.Contains(receipt, "Feedback is not accepted remediation.");
        StringAssert.Contains(receipt, "Accepted remediation is not patch proposal.");
        StringAssert.Contains(receipt, "Patch proposal is not source apply.");
        StringAssert.Contains(receipt, "PR update package is not PR branch mutation.");
        StringAssert.Contains(receipt, "PR branch update is not ready-for-review.");
        StringAssert.Contains(receipt, "Validation evidence is not approval.");
        StringAssert.Contains(receipt, "Rollback plan is not rollback execution.");
        StringAssert.Contains(receipt, "The combined AP/AQ/AR/AS boundary lane remains useful, but it is not a substitute for the explicit phase cross-boundary authority lane.");
    }

    private static FeedbackRemediationPackage CreateFeedbackPackage() => new()
    {
        FeedbackRemediationPackageId = "feedback_pkg_phase1",
        RunId = "phase1-run",
        RepositoryFullName = "owner/repo",
        PullRequestNumber = 466,
        CurrentHeadSha = PreHead,
        PullRequestUrl = "https://github.com/owner/repo/pull/466",
        FeedbackItems = [],
        RemediationCandidates =
        [
            new FeedbackRemediationCandidate
            {
                RemediationId = "feedback_remed_phase1",
                FeedbackItemIds = ["feedback_item_phase1"],
                Disposition = FeedbackDisposition.Remediate,
                Rationale = "Phase 1 remediation candidate.",
                AffectedAreas = ["governance"],
                LikelyFiles = ["IronDev.Core/Governance/Phase1CloseFeedbackLoopAuthority.cs"],
                RiskLevel = FeedbackRiskLevel.High,
                AuthorityRisk = true,
                SuggestedValidationLanes = ["FocusedCurrentBlock", "ImpactedArea", "FastAuthorityInvariant", "Build", "DiffCheck"],
                RequiresHumanDecision = false
            }
        ],
        EvidenceRefs = ["review-feedback-snapshot.json"],
        ValidationExpectations = ["FocusedCurrentBlock", "ImpactedArea", "FastAuthorityInvariant", "Build", "DiffCheck"],
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T02:00:00Z")
    };

    private static FeedbackPatchProposal CreatePatchProposal() =>
        FeedbackPatchProposalBuilder.Build(new FeedbackPatchProposalInput
        {
            Package = CreateFeedbackPackage(),
            ExpectedPrNumber = 466,
            ExpectedHeadSha = PreHead,
            BaseSha = BaseSha,
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T02:01:00Z")
        }).Proposal;

    private static ControlledPrUpdatePackage CreateReadyPrUpdatePackage()
    {
        var proposal = CreatePatchProposal();
        var target = new PrUpdateTarget
        {
            Repository = "owner/repo",
            PrNumber = 466,
            PrUrl = "https://github.com/owner/repo/pull/466",
            PrState = "open",
            PrDraftState = true,
            TargetBranch = "phase/close-feedback-loop",
            ExpectedCurrentHeadSha = PreHead,
            BaseBranch = "main",
            BaseSha = BaseSha,
            PackageCreatedAtUtc = DateTimeOffset.Parse("2026-06-20T02:02:00Z"),
            PackageCreatedBy = "tests"
        };

        return ControlledPrUpdatePackageBuilder.Build(new ControlledPrUpdatePackageInput
        {
            Proposal = proposal,
            Target = target,
            ValidationReceipts = [CreateValidationReceipt(PostHead)],
            SourceApplyEvidence = new PrUpdateSourceApplyEvidence
            {
                SourceApplyReceiptId = Guid.NewGuid().ToString("D"),
                SourceApplyReceiptHash = Hash("source-apply"),
                SourceApplyRequestHash = Hash("source-apply-request"),
                SourceApplyDryRunReceiptHash = Hash("source-apply-dry-run"),
                RollbackSupportReceiptHash = Hash("rollback-support"),
                ExpectedBranch = target.TargetBranch,
                ObservedBranch = target.TargetBranch,
                ApplySucceeded = true,
                MutationOccurred = true,
                PartialApplyOccurred = false,
                AppliedFiles = proposal.ExpectedChangedFiles,
                AppliedAtUtc = DateTimeOffset.Parse("2026-06-20T02:03:00Z")
            },
            SourceApplyPending = false,
            ExpectedPostUpdateHeadSha = PostHead,
            ExpectedDiffHash = Hash("phase1-diff"),
            ExpectedCommitMessage = "docs/test(feedback): close Phase 1 feedback loop",
            ExpectedCommitBody = "Controlled Phase 1 branch update.",
            CommitAllowed = true,
            PushAllowed = true,
            TargetRemote = "origin",
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T02:04:00Z")
        }).Package;
    }

    private static PrBranchUpdateExecutionReceipt CreateBranchUpdateReceipt() => new()
    {
        ExecutionId = "pr_branch_update_exec_phase1",
        PackageId = "pr_update_pkg_phase1",
        Repository = "owner/repo",
        PrNumber = 466,
        Branch = "phase/close-feedback-loop",
        PreExecutionHeadSha = PreHead,
        PostExecutionHeadSha = PostHead,
        CommitSha = PostHead,
        Pushed = true,
        PushRemote = "origin",
        PushBranch = "phase/close-feedback-loop",
        SourceApplyReceipt = "source_apply_phase1",
        ValidationReceipts = ["validation_phase1"],
        DirtyWorktreeBefore = true,
        DirtyWorktreeAfter = false,
        ExpectedFilesChanged = ["IronDev.Core/Governance/Phase1CloseFeedbackLoopAuthority.cs"],
        ActualFilesChanged = ["IronDev.Core/Governance/Phase1CloseFeedbackLoopAuthority.cs"],
        RollbackAvailable = true,
        RollbackInstructions = "Rollback plan is not rollback execution.",
        ExecutionVerdict = PrBranchUpdateExecutionVerdict.Executed,
        FailureClassification = PrBranchUpdateFailureKind.None,
        Issues = [],
        ExecutedAtUtc = DateTimeOffset.Parse("2026-06-20T02:05:00Z"),
        Boundary = PrBranchUpdateBoundary.Executor
    };

    private static RollbackPlan CreateRollbackPlan() => new()
    {
        RollbackPlanId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        RollbackPlanKind = "Phase1ReviewOnly",
        PatchArtifactId = Guid.NewGuid(),
        PatchHash = Hash("patch"),
        ChangeSetHash = Hash("change-set"),
        ControlledDryRunRequestId = Guid.NewGuid(),
        DryRunExecutionAuditId = Guid.NewGuid(),
        DryRunAuditHash = Hash("dry-run-audit"),
        DryRunReceiptHash = Hash("dry-run-receipt"),
        PolicySatisfactionId = Guid.NewGuid(),
        PolicySatisfactionHash = Hash("policy-satisfaction"),
        SubjectKind = "phase",
        SubjectId = "phase1-close-feedback-loop",
        SubjectHash = Hash("phase1-close-feedback-loop"),
        SourceSnapshotReference = "snapshot://phase1",
        SourceBaselineHash = Hash("baseline"),
        WorkspaceBoundaryHash = Hash("workspace-boundary"),
        ExpectedBranch = "phase/close-feedback-loop",
        ExpectedCleanWorktreeHash = Hash("clean-worktree"),
        RollbackPlanHash = Hash("rollback-plan"),
        FileActions = [],
        CreatedAtUtc = DateTimeOffset.Parse("2026-06-20T02:06:00Z"),
        EvidenceReferences = ["phase1-receipt"],
        BoundaryMaxims = ["Rollback plan is not rollback execution."]
    };

    private static ValidationRunReceipt CreateValidationReceipt(string commitSha)
    {
        var lanes = new[]
        {
            Lane("focused-phase1"),
            Lane("impacted-governance-tests"),
            Lane("fast-authority-invariants"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck)
        };

        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_phase1",
            ValidationPlanId = "validation_plan_phase1",
            Branch = "phase/close-feedback-loop",
            CommitSha = commitSha,
            ChangedFilesHash = Hash("changed-files"),
            StartedUtc = DateTimeOffset.Parse("2026-06-20T02:07:00Z"),
            FinishedUtc = DateTimeOffset.Parse("2026-06-20T02:08:00Z"),
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
        Reason = $"Required Phase 1 lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "phase1",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = DateTimeOffset.Parse("2026-06-20T02:07:00Z"),
        FinishedUtc = DateTimeOffset.Parse("2026-06-20T02:07:10Z"),
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

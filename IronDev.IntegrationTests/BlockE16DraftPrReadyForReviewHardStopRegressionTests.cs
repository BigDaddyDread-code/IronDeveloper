using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Core.Governance.PullRequestExecution;
using IronDev.Core.Governance.PushExecution;
using IronDev.Core.Validation;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE16DraftPrReadyForReviewHardStopRegressionTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly string HeadSha = new('e', 40);
    private static readonly string BaseSha = new('b', 40);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string HeadBranch = "regression/draft-pr-ready-review-hard-stop";
    private const string BaseBranch = "mutation/branch-remote-head-guard";
    private const string RunId = "run-e16";
    private const string PatchHash = "sha256:e16abcdef1234567890";
    private const int PullRequestNumber = 516;
    private const string PullRequestUrl = "https://github.com/BigDaddyDread-code/IronDeveloper/pull/516";
    private const string DraftReceiptRef = "controlled-draft-pr-receipt:e16";

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotMeanReadyForReview()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var ready = BuildDraftOnlyReadyPackage(evidence);

        Assert.IsFalse(evidence.IsReadyTransitionEvidence);
        AssertDraftOnlyDoesNotMarkReady(ready);
        CollectionAssert.Contains(ready.BlockReasons, ReadyForReviewBlockReason.MissingBranchUpdateEvidence);
        CollectionAssert.Contains(ready.BlockReasons, ReadyForReviewBlockReason.MissingValidationEvidence);
        CollectionAssert.Contains(ready.BlockReasons, ReadyForReviewBlockReason.MissingPhaseAuthorityReceipt);
        Assert.IsFalse(ReadyForReviewBypassEvaluator.CanMarkReadyForReview(evidence.Receipt));
    }

    [TestMethod]
    public void DraftPullRequestUrlDoesNotMeanReadyForReview()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var ready = BuildDraftOnlyReadyPackage(evidence with { Receipt = DraftReceipt() with { PullRequestUrl = PullRequestUrl } });

        Assert.IsTrue(Uri.TryCreate(evidence.PullRequestUrl, UriKind.Absolute, out _));
        AssertDraftOnlyDoesNotMarkReady(ready);
        AssertContains(ready.PackageIssues, "MissingBranchUpdateEvidence");
    }

    [TestMethod]
    public void DraftPullRequestNumberDoesNotMeanReadyForReview()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var ready = BuildDraftOnlyReadyPackage(evidence with { Receipt = DraftReceipt() with { PullRequestNumber = PullRequestNumber } });

        Assert.IsTrue(evidence.PullRequestNumber > 0);
        AssertDraftOnlyDoesNotMarkReady(ready);
        AssertContains(ready.PackageIssues, "MissingValidationEvidence");
    }

    [TestMethod]
    public void DraftPullRequestProviderIdDoesNotMeanReadyForReview()
    {
        var evidence = DraftPrCreatedEvidenceOnly() with { ProviderPullRequestId = "provider-pr:e16" };
        var ready = BuildDraftOnlyReadyPackage(evidence);

        Assert.AreEqual("provider-pr:e16", evidence.ProviderPullRequestId);
        AssertDraftOnlyDoesNotMarkReady(ready);
        Assert.IsFalse(evidence.IsReadyTransitionEvidence);
    }

    [TestMethod]
    public void DraftPullRequestCreatedStatusDoesNotMeanReadyForReview()
    {
        var evidence = DraftPrCreatedEvidenceOnly();

        Assert.AreEqual(GovernedOperationState.Completed, evidence.OperationStatus.State);
        AssertContains(evidence.OperationStatus.ForbiddenActions, "do not mark ready for review from draft PR receipt");
        AssertContains(evidence.OperationStatus.NextSafeActions, "request ready-for-review authority separately if needed");
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanExecute);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void DraftPullRequestReadModelDoesNotMeanReadyForReview()
    {
        var readModel = DraftPrCreatedEvidenceOnly().ReadModel;
        var ready = BuildDraftOnlyReadyPackage(DraftPrCreatedEvidenceOnly() with { ReadModel = readModel });

        Assert.IsTrue(readModel.IsDraft);
        Assert.AreEqual(PullRequestUrl, readModel.PullRequestUrl);
        AssertDraftOnlyDoesNotMarkReady(ready);
        Assert.IsFalse(readModel.IsReadyTransitionEvidence);
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotMeanMergeReady()
    {
        var merge = BuildMergeEvidence(DraftPrCreatedEvidenceOnly());

        Assert.AreEqual(MergeReadinessOutcome.NeedsMoreMergeEvidence, merge.Outcome);
        CollectionAssert.Contains(merge.MergeEvidenceGaps, "DraftPullRequestRequiresReadyForReviewEvidence");
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanMerge(merge));
        Assert.IsFalse(merge.Boundary.CanMerge);
    }

    [TestMethod]
    public void DraftPullRequestUrlDoesNotMeanMergeReady()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var merge = BuildMergeEvidence(evidence);

        CollectionAssert.Contains(merge.MergeEvidenceRefs, evidence.PullRequestUrl);
        Assert.AreNotEqual(MergeReadinessOutcome.ReadyForMergeDecision, merge.Outcome);
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanMerge(evidence.PullRequestUrl));
    }

    [TestMethod]
    public void DraftPullRequestNumberDoesNotMeanMergeReady()
    {
        var merge = BuildMergeEvidence(DraftPrCreatedEvidenceOnly());

        Assert.AreEqual(PullRequestNumber, merge.PullRequestNumber);
        Assert.AreNotEqual(MergeReadinessOutcome.ReadyForMergeDecision, merge.Outcome);
        CollectionAssert.Contains(merge.MergeEvidenceGaps, "DraftPullRequestRequiresReadyForReviewEvidence");
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotMeanReleaseReady()
    {
        var release = BuildReleaseEvidence(DraftPrCreatedEvidenceOnly(), releaseCandidateRef: null);

        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.NotApplicableBeforeMerge, release.Outcome);
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanRelease(release));
        Assert.IsFalse(release.Boundary.CanRelease);
    }

    [TestMethod]
    public void DraftPullRequestUrlDoesNotBecomeReleaseCandidateRef()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var release = BuildReleaseEvidence(evidence, evidence.PullRequestUrl, pullRequestMerged: true);

        Assert.AreEqual(ReleaseReadinessEvidenceOutcome.NeedsMoreReleaseEvidence, release.Outcome);
        CollectionAssert.Contains(release.ReleaseEvidenceGaps, "InvalidReleaseCandidateRef:PullRequestUrl");
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanRelease(evidence.PullRequestUrl));
    }

    [TestMethod]
    public void DraftPullRequestHeadRefDoesNotBecomeReleaseCandidateRef()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var release = BuildReleaseEvidence(evidence, evidence.HeadRef, pullRequestMerged: true);

        Assert.AreNotEqual(ReleaseReadinessEvidenceOutcome.ReadyForReleaseDecision, release.Outcome);
        Assert.IsFalse(release.ReleaseCandidateRef?.Contains("/pull/", StringComparison.OrdinalIgnoreCase) == true);
        Assert.IsFalse(MergeReleaseBypassEvaluator.CanRelease(evidence.HeadRef));
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotAuthorizeWorkflowContinuation()
    {
        var evidence = DraftPrCreatedEvidenceOnly();

        Assert.IsFalse(evidence.Receipt.ContinuationAttempted);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanContinueWorkflow);
        AssertContains(evidence.OperationStatus.ForbiddenActions, "do not continue workflow from draft PR receipt");
        Assert.IsTrue(evidence.RequiresWorkflowContinuationAuthority);
    }

    [TestMethod]
    public void DraftPullRequestCreatedStatusDoesNotAuthorizeWorkflowContinuation()
    {
        var status = DraftPrCreatedEvidenceOnly().OperationStatus;
        var validation = GovernedOperationStatusValidator.Validate(status);

        Assert.AreEqual(GovernedOperationState.Completed, status.State);
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Concat(validation.RedFlags)));
        Assert.IsFalse(validation.Boundary.CanContinueWorkflow);
        Assert.IsFalse(validation.Boundary.CanExecute);
    }

    [TestMethod]
    public void DraftPullRequestReadModelDoesNotAuthorizeWorkflowContinuation()
    {
        var readModel = DraftPrCreatedEvidenceOnly().ReadModel;

        Assert.IsTrue(readModel.IsDraft);
        Assert.IsFalse(readModel.CanContinueWorkflow);
        Assert.IsTrue(readModel.RequiresWorkflowContinuationAuthority);
    }

    [TestMethod]
    public void DraftPullRequestEvidenceDoesNotMoveOperationToExecutableNextPhase()
    {
        var evidence = DraftPrCreatedEvidenceOnly();
        var nextPhase = BuildDraftOnlyReadyPackage(evidence);

        Assert.AreNotEqual(ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor, nextPhase.Verdict);
        Assert.IsFalse(nextPhase.CanMarkReadyForReview);
        Assert.IsFalse(evidence.OperationStatus.NextSafeActions.Any(action => action.Contains("execute", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotSatisfyApproval()
    {
        var evidence = DraftPrCreatedEvidenceOnly();

        Assert.IsFalse(evidence.ApprovalSatisfied);
        Assert.IsTrue(evidence.RequiresAcceptedApproval);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanApprove);
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotSatisfyPolicy()
    {
        var evidence = DraftPrCreatedEvidenceOnly();

        Assert.IsFalse(evidence.PolicySatisfied);
        Assert.IsTrue(evidence.RequiresPolicySatisfaction);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanSatisfyPolicy);
        AssertContains(evidence.OperationStatus.ForbiddenActions, "draft PR receipt does not satisfy policy");
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotRefreshValidation()
    {
        var evidence = DraftPrCreatedEvidenceOnly() with { ValidationEvidenceRef = "validation-result:e16" };

        Assert.IsFalse(evidence.ValidationFresh);
        Assert.IsTrue(evidence.RequiresFreshValidation);
        Assert.IsTrue(evidence.RequiresStaleValidationGuard);
        AssertContains(evidence.OperationStatus.EvidenceRefs, "validation-result:e16");
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotProveSourceSafety()
    {
        var evidence = DraftPrCreatedEvidenceOnly();

        Assert.IsFalse(evidence.SourceSafe);
        Assert.IsFalse(evidence.WorktreeSafe);
        Assert.IsFalse(evidence.BranchSafe);
        Assert.IsTrue(evidence.RequiresDirtyWorktreeGuard);
        Assert.IsTrue(evidence.RequiresMovedBaseGuard);
        Assert.IsTrue(evidence.RequiresBranchRemoteHeadVerification);
    }

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotAuthorizeMutation()
    {
        var evidence = DraftPrCreatedEvidenceOnly();

        Assert.IsFalse(evidence.MutationAuthority);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanMutate);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanSourceApply);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanCommit);
        Assert.IsFalse(evidence.StatusValidation.Boundary.CanPush);
    }

    [TestMethod]
    public void ReadyForReviewRequiresExplicitReadyForReviewEvidence()
    {
        var draftOnly = BuildDraftOnlyReadyPackage(DraftPrCreatedEvidenceOnly());

        Assert.AreEqual(ReadyForReviewEligibilityVerdict.Incomplete, draftOnly.Verdict);
        Assert.IsFalse(draftOnly.CanMarkReadyForReview);
        CollectionAssert.Contains(draftOnly.BlockReasons, ReadyForReviewBlockReason.MissingBranchUpdateEvidence);
        CollectionAssert.Contains(draftOnly.BlockReasons, ReadyForReviewBlockReason.MissingValidationEvidence);
    }

    [TestMethod]
    public void ReadyForReviewRequiresHumanDecision()
    {
        var draftOnly = DraftPrCreatedEvidenceOnly();

        Assert.IsFalse(draftOnly.IsReadyTransitionEvidence);
        Assert.IsTrue(draftOnly.RequiresHumanReadyForReviewDecision);
    }

    [TestMethod]
    public void ReadyForReviewRequiresNonDraftProviderState()
    {
        var package = CreateEligibleReadyPackage();
        var receipt = new ReadyForReviewExecutionReceipt
        {
            ReadyForReviewExecutionId = "ready-review-execution:e16",
            ReadyForReviewPackageId = package.ReadyForReviewPackageId,
            Repository = package.Target.Repository,
            PullRequestNumber = package.Target.PullRequestNumber,
            PullRequestUrl = package.Target.PullRequestUrl,
            PreState = GoodReadyState(package, draft: true),
            PostState = GoodReadyState(package, draft: true),
            ExpectedHeadBranch = package.Target.HeadBranch,
            ExpectedHeadSha = package.Target.ExpectedHeadSha,
            ExpectedBaseBranch = package.Target.BaseBranch,
            ExpectedBaseSha = package.Target.BaseSha,
            ReadyTransitionAttempted = true,
            ReadyTransitionAccepted = true,
            PostStateVerified = false,
            ExecutionVerdict = ReadyForReviewExecutionVerdict.Failed,
            FailureClassification = ReadyForReviewExecutionFailureKind.PostReadyVerificationFailed,
            RequestedBy = "tests",
            RequestedAtUtc = ObservedAtUtc,
            ExecutedAtUtc = ObservedAtUtc.AddSeconds(30),
            Boundary = ReadyForReviewExecutionBoundary.Executor
        };

        Assert.IsTrue(receipt.PreState!.PullRequestDraft);
        Assert.IsTrue(receipt.PostState!.PullRequestDraft);
        Assert.IsFalse(receipt.PostStateVerified);
        Assert.AreEqual(ReadyForReviewExecutionFailureKind.PostReadyVerificationFailed, receipt.FailureClassification);
    }

    [TestMethod]
    public void ReadyForReviewRequiresSeparateReceiptFromDraftCreation()
    {
        var draftReceipt = DraftPrCreatedEvidenceOnly().Receipt;
        var package = CreateEligibleReadyPackage();
        var readyReceiptProperties = typeof(ReadyForReviewExecutionReceipt).GetProperties().Select(property => property.Name).ToArray();

        Assert.IsFalse(draftReceipt.ReadyForReviewAttempted);
        CollectionAssert.Contains(readyReceiptProperties, nameof(ReadyForReviewExecutionReceipt.ReadyTransitionAttempted));
        CollectionAssert.Contains(readyReceiptProperties, nameof(ReadyForReviewExecutionReceipt.PostStateVerified));
        Assert.AreNotEqual(DraftReceiptRef, package.ReadyForReviewPackageId);
    }

    [TestMethod]
    public void E16DoesNotIntroduceReadyForReviewAuthorityFields()
    {
        var names = E16OwnedFixtureMemberNames();

        AssertNoForbiddenMember(names, "CanMarkReadyForReview");
        AssertNoForbiddenMember(names, "ReviewApproved");
        AssertNoForbiddenMember(names, "PrApproved");
    }

    [TestMethod]
    public void E16DoesNotIntroduceMergeAuthorityFields()
    {
        var names = E16OwnedFixtureMemberNames();

        AssertNoForbiddenMember(names, "CanMerge");
        AssertNoForbiddenMember(names, "MergeAuthorized");
    }

    [TestMethod]
    public void E16DoesNotIntroduceReleaseAuthorityFields()
    {
        var names = E16OwnedFixtureMemberNames();

        AssertNoForbiddenMember(names, "CanRelease");
        AssertNoForbiddenMember(names, "ReleaseAuthorized");
    }

    [TestMethod]
    public void E16DoesNotIntroduceWorkflowContinuationAuthorityFields()
    {
        var names = E16OwnedFixtureMemberNames();

        AssertNoForbiddenMember(names, "CanContinue");
        AssertNoForbiddenMember(names, "WorkflowContinuationAllowed");
    }

    [TestMethod]
    public void E16DoesNotCallGitHub()
    {
        var source = E16SourceWithoutStrings()
            .Replace(nameof(E16DoesNotCallGitHub), string.Empty, StringComparison.Ordinal);

        foreach (var marker in new[] { "GitHub", "Octokit", "HttpClient", "GraphQL", "REST", "markPullRequestReadyForReview", "requestReviewers", "mergePullRequest", "enableAutoMerge" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E16DoesNotCallExecutors()
    {
        var source = E16SourceWithoutStrings();

        foreach (var marker in new[] { "ProcessStartInfo", "Process.Start", "WorkflowDispatch", "ExecuteAsync" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E16DoesNotWriteStatus()
    {
        var source = E16SourceWithoutStrings();

        foreach (var marker in new[] { "StatusStore", "ReceiptStore", "File.Write", "Directory.CreateDirectory" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E16DoesNotAddApiCliOrPersistenceSurface()
    {
        var root = FindRepositoryRoot();
        var productionHits = Directory.GetFiles(root, "*E16*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path =>
                path.StartsWith("IronDev.Api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Data/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Sql/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("OpenApi/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), productionHits, string.Join(", ", productionHits));
    }

    [TestMethod]
    public void BlockE16_Receipt_RecordsDraftPrHardStopBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "E16_DRAFT_PR_READY_FOR_REVIEW_HARD_STOP_REGRESSION.md"));

        StringAssert.Contains(doc, "Draft PR exists is not ready-for-review, merge-ready, or release-ready.");
        StringAssert.Contains(doc, "A draft PR receipt proves creation. It does not approve review, merge, release, or continuation.");
        StringAssert.Contains(doc, "E16 is a regression hard-stop. It adds no ready-for-review action path.");
        StringAssert.Contains(doc, "ready-for-review authority");
        StringAssert.Contains(doc, "release readiness");
        StringAssert.Contains(doc, "workflow continuation");
        StringAssert.Contains(doc, "policy satisfaction");
        StringAssert.Contains(doc, "source safety");
        StringAssert.Contains(doc, "mutation authority");
    }

    [TestMethod]
    public void BlockE16_UnsafeAuthorityPhrasesAreNotEmittedByDraftPrEvidence()
    {
        var allText = string.Join(
            Environment.NewLine,
            DraftPrCreatedEvidenceOnly().OperationStatus.ForbiddenActions
                .Concat(DraftPrCreatedEvidenceOnly().OperationStatus.NextSafeActions)
                .Concat(DraftPrCreatedEvidenceOnly().OperationStatus.EvidenceRefs)
                .Concat(DraftPrCreatedEvidenceOnly().OperationStatus.ReceiptRefs));

        foreach (var marker in new[]
        {
            "draft pr approved",
            "draft pr ready for review",
            "draft pr merge ready",
            "draft pr release ready",
            "draft pr continuation authorized",
            "draft pr policy satisfied",
            "draft pr source safe",
            "draft pr mutation allowed",
            "pr url is release candidate",
            "pr number is release candidate"
        })
        {
            Assert.IsFalse(allText.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void BlockE16_ValidReferenceNamesRemainEvidenceOnly()
    {
        var refs = new[]
        {
            "draft-pr:e16",
            "pull-request:e16",
            "pr-url:e16",
            "ready-for-review-evidence:e16",
            "merge-target:e16",
            "release-candidate:e16",
            "workflow-continuation:e16"
        };
        var status = DraftPrCreatedEvidenceOnly() with
        {
            OperationStatus = DraftPrCreatedEvidenceOnly().OperationStatus with
            {
                EvidenceRefs = refs
            }
        };
        var validation = GovernedOperationStatusValidator.Validate(status.OperationStatus);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Concat(validation.RedFlags)));
        Assert.IsFalse(validation.Boundary.CanExecute);
        Assert.IsFalse(validation.Boundary.CanMerge);
        Assert.IsFalse(validation.Boundary.CanRelease);
        Assert.IsFalse(validation.Boundary.CanContinueWorkflow);
    }

    private static DraftPullRequestHardStopFixture DraftPrCreatedEvidenceOnly()
    {
        var receipt = DraftReceipt();
        var status = new GovernedOperationStatus
        {
            OperationId = "controlled-draft-pr-exec-e16",
            OperationKind = RunAuthorityOperationKind.DraftPullRequest.ToString(),
            Subject = $"controlled draft PR for {Repository} {HeadBranch} {BaseBranch} {HeadSha}",
            State = GovernedOperationState.Completed,
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = ["inspect controlled draft PR receipt", "request ready-for-review authority separately if needed"],
            ForbiddenActions =
            [
                "do not mark ready for review from draft PR receipt",
                "do not request reviewers from draft PR receipt",
                "do not merge from draft PR receipt",
                "do not release from draft PR receipt",
                "do not deploy from draft PR receipt",
                "do not continue workflow from draft PR receipt",
                "do not promote memory from draft PR receipt",
                "do not use PR URL as release candidate ref",
                "draft PR receipt does not satisfy policy",
                "draft PR receipt does not approve the next mutation"
            ],
            EvidenceRefs =
            [
                "draft-pr:e16",
                "pull-request:e16",
                "pr-url:e16",
                "validation-result:e16",
                "branch-ref:e16",
                "base-ref:e16",
                "receipt-fingerprint:e16"
            ],
            ReceiptRefs = [receipt.ReceiptRef],
            ObservedAtUtc = ObservedAtUtc
        };

        return new DraftPullRequestHardStopFixture
        {
            Receipt = receipt,
            OperationStatus = status,
            StatusValidation = GovernedOperationStatusValidator.Validate(status),
            ReadModel = new DraftPullRequestReadModelFixture
            {
                PullRequestNumber = PullRequestNumber,
                PullRequestUrl = PullRequestUrl,
                ProviderPullRequestId = "provider-pr:e16",
                HeadRef = HeadBranch,
                BaseRef = BaseBranch,
                IsDraft = true,
                IsReadyTransitionEvidence = false,
                CanContinueWorkflow = false,
                RequiresWorkflowContinuationAuthority = true
            },
            ProviderPullRequestId = "provider-pr:e16",
            ValidationEvidenceRef = "validation-result:e16"
        };
    }

    private static ControlledDraftPullRequestReceipt DraftReceipt() => new()
    {
        ReceiptRef = DraftReceiptRef,
        Repository = Repository,
        HeadBranch = HeadBranch,
        BaseBranch = BaseBranch,
        RunId = RunId,
        PatchHash = PatchHash,
        HeadCommitId = HeadSha,
        PullRequestNumber = PullRequestNumber,
        PullRequestUrl = PullRequestUrl,
        IsDraft = true,
        WasCreated = true,
        WasUpdated = false,
        CreatedOrUpdatedAtUtc = ObservedAtUtc,
        ReadyForReviewAttempted = false,
        ReviewerRequestAttempted = false,
        MergeAttempted = false,
        ReleaseAttempted = false,
        DeploymentAttempted = false,
        MemoryWriteAttempted = false,
        ContinuationAttempted = false
    };

    private static ReadyForReviewEligibilityPackage BuildDraftOnlyReadyPackage(DraftPullRequestHardStopFixture evidence) =>
        ReadyForReviewSeparationBuilder.Build(new ReadyForReviewSeparationInput
        {
            Repository = Repository,
            PullRequestNumber = evidence.PullRequestNumber,
            PullRequestUrl = evidence.PullRequestUrl,
            PullRequestState = "open",
            PullRequestDraft = evidence.Receipt.IsDraft,
            HeadBranch = HeadBranch,
            ExpectedHeadSha = HeadSha,
            ObservedHeadSha = HeadSha,
            BaseBranch = BaseBranch,
            BaseSha = BaseSha,
            ExpectedBaseBranch = BaseBranch,
            ExpectedBaseSha = BaseSha,
            BranchUpdateReceipt = null,
            NoBranchUpdateRequiredEvidence = null,
            ValidationReceipts = [],
            PhaseAuthorityReceiptId = DraftReceiptRef,
            PhaseAuthorityReceiptText = null,
            PackageCreatedBy = "tests",
            PackageCreatedAtUtc = ObservedAtUtc
        }).Package;

    private static ReadyForReviewEligibilityPackage CreateEligibleReadyPackage() =>
        ReadyForReviewSeparationBuilder.Build(new ReadyForReviewSeparationInput
        {
            Repository = Repository,
            PullRequestNumber = PullRequestNumber,
            PullRequestUrl = PullRequestUrl,
            PullRequestState = "open",
            PullRequestDraft = true,
            HeadBranch = HeadBranch,
            ExpectedHeadSha = HeadSha,
            ObservedHeadSha = HeadSha,
            BaseBranch = BaseBranch,
            BaseSha = BaseSha,
            ExpectedBaseBranch = BaseBranch,
            ExpectedBaseSha = BaseSha,
            BranchUpdateReceipt = BranchUpdateReceipt(),
            ValidationReceipts = [ValidationReceipt()],
            PhaseAuthorityReceiptId = "PHASE1_CLOSE_FEEDBACK_LOOP",
            PhaseAuthorityReceiptText = PhaseReceiptText(),
            PackageCreatedBy = "tests",
            PackageCreatedAtUtc = ObservedAtUtc
        }).Package;

    private static ReadyForReviewObservedPrState GoodReadyState(ReadyForReviewEligibilityPackage package, bool draft) => new()
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
        ObservedAtUtc = ObservedAtUtc,
        ObservationSucceeded = true
    };

    private static PrBranchUpdateExecutionReceipt BranchUpdateReceipt() => new()
    {
        ExecutionId = "pr-branch-update-exec:e16",
        PackageId = "pr-update-package:e16",
        Repository = Repository,
        PrNumber = PullRequestNumber,
        Branch = HeadBranch,
        PreExecutionHeadSha = new('d', 40),
        PostExecutionHeadSha = HeadSha,
        CommitSha = HeadSha,
        Pushed = true,
        PushRemote = "origin",
        PushBranch = HeadBranch,
        SourceApplyReceipt = "source-apply-receipt:e16",
        ValidationReceipts = ["validation-result:e16"],
        DirtyWorktreeBefore = false,
        DirtyWorktreeAfter = false,
        ExpectedFilesChanged = ["IronDev.IntegrationTests/BlockE16DraftPrReadyForReviewHardStopRegressionTests.cs"],
        ActualFilesChanged = ["IronDev.IntegrationTests/BlockE16DraftPrReadyForReviewHardStopRegressionTests.cs"],
        RollbackAvailable = true,
        RollbackInstructions = "Rollback plan is not rollback execution.",
        ExecutionVerdict = PrBranchUpdateExecutionVerdict.Executed,
        FailureClassification = PrBranchUpdateFailureKind.None,
        Issues = [],
        ExecutedAtUtc = ObservedAtUtc.AddMinutes(-4),
        Boundary = PrBranchUpdateBoundary.Executor
    };

    private static ValidationRunReceipt ValidationReceipt()
    {
        var lanes = new[]
        {
            Lane("focused-e16"),
            Lane("impacted-governance-tests"),
            Lane("fast-authority-invariants"),
            Lane("build", ValidationCommandKind.Build),
            Lane("diff-check", ValidationCommandKind.DiffCheck),
            Lane("phase-authority")
        };

        return new ValidationRunReceipt
        {
            ValidationRunId = "validation-run:e16",
            ValidationPlanId = "validation-plan:e16",
            Branch = HeadBranch,
            CommitSha = HeadSha,
            ChangedFilesHash = "sha256:e16-validation",
            StartedUtc = ObservedAtUtc.AddMinutes(-3),
            FinishedUtc = ObservedAtUtc.AddMinutes(-2),
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
        Reason = $"Required E16 lane {name}.",
        Requirement = ValidationLaneRequirement.Required,
        Timeout = TimeSpan.FromMinutes(5),
        CommandKind = kind,
        Commands = [name],
        SafeToParallelize = true,
        ParallelismGroup = "e16",
        CacheCategory = kind == ValidationCommandKind.Build ? "build" : kind == ValidationCommandKind.DiffCheck ? "diff" : "test"
    };

    private static ValidationProcessResult Result(ValidationLane lane) => new()
    {
        LaneName = lane.Name,
        Command = lane.Name,
        Arguments = [],
        WorkingDirectory = "repo",
        StartedUtc = ObservedAtUtc.AddMinutes(-3),
        FinishedUtc = ObservedAtUtc.AddMinutes(-3).AddSeconds(10),
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

    private static MergeReadinessEvidencePackage BuildMergeEvidence(DraftPullRequestHardStopFixture evidence) =>
        MergeReadinessEvidencePackager.Build(new MergeReadinessEvidenceInput
        {
            Request = MergeReleaseRequest(evidence),
            PullRequestReceiptExists = true,
            PullRequestStatusExists = true,
            ObservedHeadSha = HeadSha,
            PullRequestDraft = true,
            CommitReadinessReviewExists = true,
            CommitReadinessDecision = CommitReadinessDecision.ReadyForHumanCommitReview,
            CiObservationExists = true,
            CiState = FeedbackCiState.Passed,
            ReviewFeedbackSnapshotExists = true,
            RequestedChangeCount = 0,
            FeedbackReadinessReportExists = true,
            FeedbackReadinessOutcome = FeedbackReadinessOutcome.NoKnownBlockingFeedback,
            ArtifactConsistencyReportExists = true,
            UnsafeMaterialReportExists = true,
            EvidenceRefs = [evidence.Receipt.ReceiptRef, evidence.PullRequestUrl, evidence.HeadRef, evidence.BaseRef],
            CreatedAtUtc = ObservedAtUtc
        });

    private static ReleaseReadinessEvidencePackage BuildReleaseEvidence(
        DraftPullRequestHardStopFixture evidence,
        string? releaseCandidateRef,
        bool pullRequestMerged = false) =>
        ReleaseReadinessEvidencePackager.Build(new ReleaseReadinessEvidenceInput
        {
            Request = MergeReleaseRequest(evidence),
            PullRequestStatusExists = true,
            PullRequestMerged = pullRequestMerged,
            ReleaseCandidateRef = releaseCandidateRef,
            ProductHardeningEvidenceExists = true,
            ProductHardeningPassed = true,
            ReleaseReadinessReportExists = true,
            ReleaseReadinessReportOutcome = nameof(ProductReleaseReadinessOutcome.ReadyForDecision),
            ReleaseReadinessDecisionRecordExists = true,
            ArtifactConsistencyReportExists = true,
            UnsafeMaterialReportExists = true,
            KnownRisksDocumented = true,
            RecoveryEvidenceExists = true,
            EvidenceRefs = [evidence.Receipt.ReceiptRef, evidence.PullRequestUrl, evidence.HeadRef, evidence.BaseRef],
            CreatedAtUtc = ObservedAtUtc
        });

    private static MergeReleaseSeparationRequest MergeReleaseRequest(DraftPullRequestHardStopFixture evidence) =>
        MergeReleaseSeparationRequestWriter.Create(new MergeReleaseSeparationRequestInput
        {
            RunId = RunId,
            ProjectId = "project-e16",
            RepositoryFullName = Repository,
            PullRequestNumber = evidence.PullRequestNumber,
            PullRequestUrl = evidence.PullRequestUrl,
            BaseBranch = BaseBranch,
            HeadBranch = HeadBranch,
            ExpectedHeadSha = HeadSha,
            PullRequestCreationReceiptId = evidence.Receipt.ReceiptRef,
            FeedbackReadinessReportId = "feedback-readiness:e16",
            RequestedBy = "tests",
            Reason = "draft PR hard-stop regression",
            EvidenceRefs = [evidence.Receipt.ReceiptRef, evidence.PullRequestUrl],
            RequestedAtUtc = ObservedAtUtc
        });

    private static string PhaseReceiptText() => """
        # Phase 1 Close Feedback Loop

        Phase 1 closes the feedback loop.

        PR branch update is not ready-for-review.

        Validation evidence is not approval.
        """;

    private static void AssertDraftOnlyDoesNotMarkReady(ReadyForReviewEligibilityPackage package)
    {
        Assert.AreNotEqual(ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor, package.Verdict);
        Assert.IsFalse(package.CanMarkReadyForReview);
        Assert.IsTrue(package.Boundary.EvidenceOnly);
        Assert.IsFalse(package.Boundary.CanMarkReadyForReview);
        Assert.IsFalse(package.Boundary.CanRequestReviewers);
        Assert.IsFalse(package.Boundary.CanMerge);
        Assert.IsFalse(package.Boundary.CanRelease);
        Assert.IsFalse(package.Boundary.CanDeploy);
        Assert.IsFalse(package.Boundary.CanContinueWorkflow);
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static void AssertNoForbiddenIdentifier(string source, string marker)
    {
        Assert.IsFalse(
            source.Contains(marker, StringComparison.Ordinal),
            $"E16-owned source must not introduce '{marker}'.");
    }

    private static void AssertNoForbiddenMember(IReadOnlyCollection<string> names, string marker)
    {
        Assert.IsFalse(
            names.Any(name => string.Equals(name, marker, StringComparison.Ordinal)),
            $"E16-owned fixture members must not introduce '{marker}'.");
    }

    private static string[] E16OwnedFixtureMemberNames() =>
    [
        .. typeof(DraftPullRequestHardStopFixture).GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(member => member.Name),
        .. typeof(DraftPullRequestReadModelFixture).GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(member => member.Name)
    ];

    private static string E16SourceWithoutStrings()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.IntegrationTests", "BlockE16DraftPrReadyForReviewHardStopRegressionTests.cs"));
        return StripStringLiterals(source);
    }

    private static string StripStringLiterals(string source)
    {
        var result = new char[source.Length];
        var inString = false;
        var inVerbatim = false;
        var inRaw = false;
        var rawQuoteCount = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (!inString && !inRaw && current == '"' && next == '"' && i + 2 < source.Length && source[i + 2] == '"')
            {
                inRaw = true;
                rawQuoteCount = 3;
                result[i] = ' ';
                continue;
            }

            if (inRaw)
            {
                if (current == '"' && i + rawQuoteCount - 1 < source.Length && source.Substring(i, rawQuoteCount).All(ch => ch == '"'))
                {
                    for (var j = 0; j < rawQuoteCount && i + j < result.Length; j++)
                        result[i + j] = ' ';
                    i += rawQuoteCount - 1;
                    inRaw = false;
                    continue;
                }

                result[i] = ' ';
                continue;
            }

            if (!inString && current == '@' && next == '"')
            {
                inString = true;
                inVerbatim = true;
                result[i] = ' ';
                continue;
            }

            if (!inString && current == '"')
            {
                inString = true;
                inVerbatim = false;
                result[i] = ' ';
                continue;
            }

            if (inString)
            {
                if (current == '"' && inVerbatim && next == '"')
                {
                    result[i] = ' ';
                    result[i + 1] = ' ';
                    i++;
                    continue;
                }

                if (current == '"' && (inVerbatim || !IsEscaped(source, i)))
                    inString = false;

                result[i] = ' ';
                continue;
            }

            result[i] = current;
        }

        return new string(result);
    }

    private static bool IsEscaped(string source, int index)
    {
        var backslashes = 0;
        for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
            backslashes++;
        return backslashes % 2 == 1;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed record DraftPullRequestHardStopFixture
    {
        public required ControlledDraftPullRequestReceipt Receipt { get; init; }
        public required GovernedOperationStatus OperationStatus { get; init; }
        public required GovernedOperationStatusValidationResult StatusValidation { get; init; }
        public required DraftPullRequestReadModelFixture ReadModel { get; init; }
        public required string ProviderPullRequestId { get; init; }
        public required string ValidationEvidenceRef { get; init; }

        public int PullRequestNumber => Receipt.PullRequestNumber;
        public string PullRequestUrl => Receipt.PullRequestUrl;
        public string HeadRef => Receipt.HeadBranch;
        public string BaseRef => Receipt.BaseBranch;
        public bool IsReadyTransitionEvidence => false;
        public bool RequiresHumanReadyForReviewDecision => true;
        public bool RequiresWorkflowContinuationAuthority => true;
        public bool ApprovalSatisfied => false;
        public bool RequiresAcceptedApproval => true;
        public bool PolicySatisfied => false;
        public bool RequiresPolicySatisfaction => true;
        public bool ValidationFresh => false;
        public bool RequiresFreshValidation => true;
        public bool RequiresStaleValidationGuard => true;
        public bool SourceSafe => false;
        public bool WorktreeSafe => false;
        public bool BranchSafe => false;
        public bool RequiresDirtyWorktreeGuard => true;
        public bool RequiresMovedBaseGuard => true;
        public bool RequiresBranchRemoteHeadVerification => true;
        public bool MutationAuthority => false;
    }

    private sealed record DraftPullRequestReadModelFixture
    {
        public required int PullRequestNumber { get; init; }
        public required string PullRequestUrl { get; init; }
        public required string ProviderPullRequestId { get; init; }
        public required string HeadRef { get; init; }
        public required string BaseRef { get; init; }
        public required bool IsDraft { get; init; }
        public required bool IsReadyTransitionEvidence { get; init; }
        public required bool CanContinueWorkflow { get; init; }
        public required bool RequiresWorkflowContinuationAuthority { get; init; }
    }
}

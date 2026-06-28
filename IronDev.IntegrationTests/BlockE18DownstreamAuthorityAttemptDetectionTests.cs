using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE18DownstreamAuthorityAttemptDetectionTests
{
    private static readonly string[] EvidenceOnlyRefs =
    [
        "source-apply-receipt:e18",
        "commit-receipt:e18",
        "push-receipt:e18",
        "draft-pr-receipt:e18",
        "rollback-receipt:e18",
        "validation-receipt:e18",
        "merge-readiness:e18",
        "release-readiness:e18",
        "release-candidate:e18",
        "ready-for-review-evidence:e18",
        "workflow-continuation:e18"
    ];

    [TestMethod]
    public void E18ReceiptInventoryIncludesSourceApplyReceipts() =>
        AssertInventoryCovers(ReceiptFamily.SourceApplyReceipt, "SourceApplyReceipt");

    [TestMethod]
    public void E18ReceiptInventoryIncludesCommitReceipts()
    {
        AssertInventoryCovers(ReceiptFamily.CommitPackage, "CommitPackageResult");
        AssertInventoryCovers(ReceiptFamily.CommitReceipt, "ControlledCommitReceipt");
    }

    [TestMethod]
    public void E18ReceiptInventoryIncludesPushReceipts() =>
        AssertInventoryCovers(ReceiptFamily.PushReceipt, "ControlledPushReceipt");

    [TestMethod]
    public void E18ReceiptInventoryIncludesDraftPullRequestReceipts() =>
        AssertInventoryCovers(ReceiptFamily.DraftPullRequestReceipt, "ControlledDraftPullRequestReceipt");

    [TestMethod]
    public void E18ReceiptInventoryIncludesPullRequestBranchUpdateReceipts() =>
        AssertInventoryCovers(ReceiptFamily.PullRequestBranchUpdateReceipt, "PrBranchUpdateExecutionReceipt");

    [TestMethod]
    public void E18ReceiptInventoryIncludesReadyForReviewReceipts()
    {
        AssertInventoryCovers(ReceiptFamily.ReadyForReviewPackage, "ReadyForReviewEligibilityPackage");
        AssertInventoryCovers(ReceiptFamily.ReadyForReviewReceipt, "ReadyForReviewExecutionReceipt");
    }

    [TestMethod]
    public void E18ReceiptInventoryIncludesRollbackReceipts() =>
        AssertInventoryCovers(ReceiptFamily.RollbackReceipt, "ControlledRollbackReceipt");

    [TestMethod]
    public void E18ReceiptInventoryIncludesValidationReceipts() =>
        AssertInventoryCovers(ReceiptFamily.ValidationReceipt, "ValidationResultPackageResult");

    [TestMethod]
    public void E18ReceiptInventoryIncludesMergeAndReleaseReadinessPackages()
    {
        AssertInventoryCovers(ReceiptFamily.MergeReadinessEvidence, "MergeReadinessEvidencePackage");
        AssertInventoryCovers(ReceiptFamily.ReleaseReadinessEvidence, "ReleaseReadinessEvidencePackage");
    }

    [TestMethod]
    public void E18ReceiptInventoryIncludesGuardDecisions()
    {
        AssertInventoryCovers(ReceiptFamily.PostStateObservation, "PostStateObservationDecision");
        AssertInventoryCovers(ReceiptFamily.DirtyWorktreeGuardDecision, "DirtyWorktreeGuardDecision");
        AssertInventoryCovers(ReceiptFamily.MovedBaseGuardDecision, "MovedBaseGuardDecision");
        AssertInventoryCovers(ReceiptFamily.StaleValidationGuardDecision, "StaleValidationGuardDecision");
        AssertInventoryCovers(ReceiptFamily.BranchRemoteHeadVerificationDecision, "BranchRemoteHeadVerificationDecision");
    }

    [TestMethod]
    public void AllKnownGovernanceReceiptLikeTypesAreEitherCoveredOrExplicitlyExempted()
    {
        var coreTypes = CoreTypeNames();
        var covered = ReceiptInventory.SelectMany(item => item.CoreTypeNames).ToHashSet(StringComparer.Ordinal);
        var required = new[]
        {
            "SourceApplyReceipt",
            "ControlledCommitReceipt",
            "ControlledPushReceipt",
            "ControlledDraftPullRequestReceipt",
            "PrBranchUpdateExecutionReceipt",
            "ReadyForReviewExecutionReceipt",
            "ControlledRollbackReceipt",
            "ValidationResultPackageResult",
            "MergeReadinessEvidencePackage",
            "ReleaseReadinessEvidencePackage",
            "GovernedOperationStatus",
            "FrontendReceiptMetadataReadModel"
        };

        foreach (var typeName in required)
        {
            CollectionAssert.Contains(coreTypes, typeName);
            CollectionAssert.Contains(covered.ToArray(), typeName);
        }
    }

    [TestMethod]
    public void SourceApplyReceiptDoesNotAuthorizeCommitPushOrPr() =>
        AssertEvidenceOnly(BuildSourceApplyReceiptEvidenceOnly(),
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.PullRequestAuthorityAttempt,
            AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void CommitReceiptDoesNotAuthorizePushOrPr() =>
        AssertEvidenceOnly(BuildCommitReceiptEvidenceOnly(),
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.PullRequestAuthorityAttempt,
            AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void PushReceiptDoesNotAuthorizeDraftPrReadyReviewMergeOrRelease() =>
        AssertEvidenceOnly(BuildPushReceiptEvidenceOnly(),
            AuthorityAttemptCategory.PullRequestAuthorityAttempt,
            AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt,
            AuthorityAttemptCategory.ReviewRequestAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void DraftPullRequestReceiptDoesNotAuthorizeReadyReviewMergeReleaseOrContinuation() =>
        AssertEvidenceOnly(BuildDraftPullRequestReceiptEvidenceOnly(),
            AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt,
            AuthorityAttemptCategory.ReviewRequestAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt,
            AuthorityAttemptCategory.ApprovalSatisfactionAttempt,
            AuthorityAttemptCategory.PolicySatisfactionAttempt);

    [TestMethod]
    public void PullRequestBranchUpdateReceiptDoesNotAuthorizeReadyForReview() =>
        AssertEvidenceOnly(BuildPrBranchUpdateReceiptEvidenceOnly(),
            AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt,
            AuthorityAttemptCategory.ReviewRequestAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void ReadyForReviewReceiptDoesNotAuthorizeMergeReleaseOrContinuation() =>
        AssertEvidenceOnly(BuildReadyForReviewReceiptEvidenceOnly(),
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void RollbackReceiptDoesNotAuthorizeRetryRecoveryOrResume() =>
        AssertEvidenceOnly(BuildRollbackReceiptEvidenceOnly(),
            AuthorityAttemptCategory.RetryAuthorityAttempt,
            AuthorityAttemptCategory.RecoveryAuthorityAttempt,
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void RetryClassificationDoesNotAuthorizeRetry() =>
        AssertEvidenceOnly(BuildRetryClassificationEvidenceOnly(),
            AuthorityAttemptCategory.RetryAuthorityAttempt,
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void RecoveryEvidenceDoesNotAuthorizeWorkflowContinuation() =>
        AssertEvidenceOnly(BuildRecoveryReceiptEvidenceOnly(),
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt,
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt);

    [TestMethod]
    public void FailedApplyRecoveryReceiptDoesNotAuthorizeSourceApplyRetry() =>
        AssertEvidenceOnly(BuildFailedApplyRecoveryReceiptEvidenceOnly(),
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.RetryAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void FailedContinuationRecoveryReceiptDoesNotAuthorizeWorkflowContinuation() =>
        AssertEvidenceOnly(BuildFailedContinuationRecoveryReceiptEvidenceOnly(),
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt,
            AuthorityAttemptCategory.RecoveryAuthorityAttempt);

    [TestMethod]
    public void ValidationReceiptDoesNotSatisfyApprovalPolicyOrSourceSafety() =>
        AssertEvidenceOnly(BuildValidationReceiptEvidenceOnly(),
            AuthorityAttemptCategory.ApprovalSatisfactionAttempt,
            AuthorityAttemptCategory.PolicySatisfactionAttempt,
            AuthorityAttemptCategory.SourceSafetyAttempt,
            AuthorityAttemptCategory.WorktreeSafetyAttempt,
            AuthorityAttemptCategory.BranchSafetyAttempt,
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt);

    [TestMethod]
    public void PostStateObservationDoesNotAuthorizeRetryRollbackOrRecovery() =>
        AssertEvidenceOnly(BuildPostStateObservationEvidenceOnly(),
            AuthorityAttemptCategory.RetryAuthorityAttempt,
            AuthorityAttemptCategory.RollbackAuthorityAttempt,
            AuthorityAttemptCategory.RecoveryAuthorityAttempt);

    [TestMethod]
    public void DirtyWorktreeGuardDecisionDoesNotAuthorizeSourceSafety() =>
        AssertEvidenceOnly(BuildDirtyWorktreeGuardDecisionEvidenceOnly(),
            AuthorityAttemptCategory.SourceSafetyAttempt,
            AuthorityAttemptCategory.WorktreeSafetyAttempt,
            AuthorityAttemptCategory.ApplyAuthorityAttempt);

    [TestMethod]
    public void MovedBaseGuardDecisionDoesNotAuthorizeSourceApplyCommitPushOrMerge() =>
        AssertEvidenceOnly(BuildMovedBaseGuardDecisionEvidenceOnly(),
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.BranchSafetyAttempt);

    [TestMethod]
    public void StaleValidationGuardDecisionDoesNotAuthorizeValidationFreshness() =>
        AssertEvidenceOnly(BuildStaleValidationGuardDecisionEvidenceOnly(),
            AuthorityAttemptCategory.ValidationFreshnessAttempt,
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt);

    [TestMethod]
    public void BranchRemoteHeadVerificationDecisionDoesNotAuthorizePushPrOrMerge() =>
        AssertEvidenceOnly(BuildBranchRemoteHeadDecisionEvidenceOnly(),
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.PullRequestAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.BranchSafetyAttempt);

    [TestMethod]
    public void MayProceedToNextAuthorityGateNeverMeansMutationAuthority()
    {
        var fixture = EvidenceOnly(
            ReceiptFamily.GuardDecision,
            ["MayProceedToNextAuthorityGate"],
            "dirty-worktree-guard:e18",
            "moved-base-guard:e18",
            "stale-validation-guard:e18",
            "branch-remote-head-guard:e18");

        AssertEvidenceOnly(fixture,
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.PullRequestAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);
        CollectionAssert.Contains(fixture.RequiredNextGates.ToArray(), "RequiresFreshAuthority");
        CollectionAssert.Contains(fixture.RequiredNextGates.ToArray(), "RequiresHumanReview");
    }

    [TestMethod]
    public void MergeReadinessEvidenceDoesNotAuthorizeMerge() =>
        AssertEvidenceOnly(BuildMergeReadinessEvidenceOnly(), AuthorityAttemptCategory.MergeAuthorityAttempt);

    [TestMethod]
    public void MergeReadinessEvidenceDoesNotBecomeReleaseEvidence() =>
        AssertEvidenceOnly(BuildMergeReadinessEvidenceOnly(),
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void ReleaseReadinessEvidenceDoesNotAuthorizeRelease() =>
        AssertEvidenceOnly(BuildReleaseReadinessEvidenceOnly(),
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void ReleaseReadinessEvidenceDoesNotAuthorizeDeployment() =>
        AssertEvidenceOnly(BuildReleaseReadinessEvidenceOnly(),
            AuthorityAttemptCategory.DeploymentAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void ReleaseCandidateEvidenceDoesNotAuthorizeDeployment() =>
        AssertEvidenceOnly(BuildReleaseCandidateEvidenceOnly(),
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt);

    [TestMethod]
    public void OperationStatusFromReceiptDoesNotGainExecutionAuthority() =>
        AssertEvidenceOnly(BuildOperationStatusProjectionEvidenceOnly(),
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.PullRequestAuthorityAttempt,
            AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt,
            AuthorityAttemptCategory.MergeAuthorityAttempt,
            AuthorityAttemptCategory.ReleaseAuthorityAttempt,
            AuthorityAttemptCategory.DeploymentAuthorityAttempt,
            AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void OperationStatusNextSafeActionsDoNotContainExecuteOrMutate()
    {
        var fixture = BuildOperationStatusProjectionEvidenceOnly();

        AssertEvidenceOnly(fixture);
        foreach (var action in fixture.NextActions)
        {
            Assert.IsFalse(action.Contains("execute", StringComparison.OrdinalIgnoreCase), action);
            Assert.IsFalse(action.Contains("mutate", StringComparison.OrdinalIgnoreCase), action);
        }
    }

    [TestMethod]
    public void ReceiptReadModelDoesNotGainMutationAuthority() =>
        AssertEvidenceOnly(BuildReceiptReadModelEvidenceOnly(),
            AuthorityAttemptCategory.ApplyAuthorityAttempt,
            AuthorityAttemptCategory.CommitAuthorityAttempt,
            AuthorityAttemptCategory.PushAuthorityAttempt,
            AuthorityAttemptCategory.PullRequestAuthorityAttempt);

    [TestMethod]
    public void ReceiptReadModelDoesNotGainWorkflowContinuationAuthority() =>
        AssertEvidenceOnly(BuildReceiptReadModelEvidenceOnly(), AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void ReceiptReadModelDoesNotSatisfyPolicyOrApproval() =>
        AssertEvidenceOnly(BuildReceiptReadModelEvidenceOnly(),
            AuthorityAttemptCategory.PolicySatisfactionAttempt,
            AuthorityAttemptCategory.ApprovalSatisfactionAttempt);

    [TestMethod]
    public void DetectorFlagsApplyAuthorityAttempt() =>
        AssertPoisonedText("safe to apply", AuthorityAttemptCategory.ApplyAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsCommitAuthorityAttempt() =>
        AssertPoisonedText("authorized to commit", AuthorityAttemptCategory.CommitAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsPushAuthorityAttempt() =>
        AssertPoisonedText("safe to push", AuthorityAttemptCategory.PushAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsReadyForReviewAuthorityAttempt() =>
        AssertPoisonedText("safe to mark ready for review", AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsMergeAuthorityAttempt() =>
        AssertPoisonedText("authorized to merge", AuthorityAttemptCategory.MergeAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsReleaseAuthorityAttempt() =>
        AssertPoisonedText("safe to release", AuthorityAttemptCategory.ReleaseAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsDeploymentAuthorityAttempt() =>
        AssertPoisonedText("authorized to deploy", AuthorityAttemptCategory.DeploymentAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsWorkflowContinuationAuthorityAttempt() =>
        AssertPoisonedText("continue workflow authorized", AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt);

    [TestMethod]
    public void DetectorFlagsApprovalSatisfactionAttempt() =>
        AssertPoisonedText("approval satisfied", AuthorityAttemptCategory.ApprovalSatisfactionAttempt);

    [TestMethod]
    public void DetectorFlagsPolicySatisfactionAttempt() =>
        AssertPoisonedText("policy satisfied", AuthorityAttemptCategory.PolicySatisfactionAttempt);

    [TestMethod]
    public void DetectorFlagsValidationFreshnessAttempt() =>
        AssertPoisonedText("validation fresh", AuthorityAttemptCategory.ValidationFreshnessAttempt);

    [TestMethod]
    public void DetectorFlagsSourceSafetyAttempt() =>
        AssertPoisonedText("source safe", AuthorityAttemptCategory.SourceSafetyAttempt);

    [TestMethod]
    public void EvidenceOnlyRefsAreNotFlaggedAsAuthority() =>
        AssertEvidenceOnly(EvidenceOnly(ReceiptFamily.EvidenceOnlyRefs, [], EvidenceOnlyRefs));

    [TestMethod]
    public void ReleaseCandidateEvidenceRefIsNotFlaggedAsReleaseExecution() =>
        AssertEvidenceOnly(EvidenceOnly(ReceiptFamily.ReleaseCandidateEvidence, [], "release-candidate:e18"));

    [TestMethod]
    public void ReadyForReviewEvidenceRefIsNotFlaggedAsReadyForReviewAuthority() =>
        AssertEvidenceOnly(EvidenceOnly(ReceiptFamily.ReadyForReviewPackage, [], "ready-for-review-evidence:e18"));

    [TestMethod]
    public void WorkflowContinuationEvidenceRefIsNotFlaggedAsContinuationAuthority() =>
        AssertEvidenceOnly(EvidenceOnly(ReceiptFamily.EvidenceOnlyRefs, [], "workflow-continuation:e18"));

    [TestMethod]
    public void BroadMarkerScanDoesNotRejectValidReceiptRefs() =>
        AssertEvidenceOnly(EvidenceOnly(ReceiptFamily.EvidenceOnlyRefs, [],
            "source-apply-receipt:e18",
            "commit-receipt:e18",
            "push-receipt:e18",
            "draft-pr-receipt:e18",
            "rollback-receipt:e18",
            "validation-receipt:e18",
            "merge-readiness:e18",
            "release-readiness:e18"));

    [TestMethod]
    public void E18DoesNotCallGitHub()
    {
        var source = E18SourceForStaticScan();

        foreach (var marker in new[] { "GitHub", "Octokit", "HttpClient", "GraphQL", "WorkflowDispatch" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E18DoesNotCallGit()
    {
        var source = E18SourceForStaticScan();

        foreach (var marker in new[] { "git ", "git.exe", "LibGit2Sharp" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E18DoesNotCallExecutors()
    {
        var source = E18SourceForStaticScan();

        foreach (var marker in new[] { "ProcessStartInfo", "Process.Start", "Executor", "ReleaseExecutor", "DeployExecutor", "MergeExecutor" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E18DoesNotAddApiCliPersistenceWorkerOrOpenApiSurface()
    {
        var root = FindRepositoryRoot();
        var forbiddenHits = Directory.GetFiles(root, "*E18*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path =>
                path.StartsWith("IronDev.Api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Cli/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Infrastructure/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Data/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Sql/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Frontend/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Worker/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("IronDev.Tauri/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("OpenApi/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), forbiddenHits, string.Join(", ", forbiddenHits));
    }

    [TestMethod]
    public void E18DoesNotAddStatusOrReceiptStoreWrites()
    {
        var source = E18SourceForStaticScan();

        foreach (var marker in new[] { "StatusStore", "ReceiptStore", "WriteAsync", "AddAsync", "InsertAsync", "SaveChanges" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void E18DoesNotAddWorkflowContinuationPath()
    {
        var source = E18SourceForStaticScan();

        AssertNoForbiddenIdentifier(source, "WorkflowContinuationExecutor");
        AssertNoForbiddenIdentifier(source, "CanContinue");
    }

    [TestMethod]
    public void E18DoesNotAddReleaseOrDeploymentExecutionPath()
    {
        var source = E18SourceForStaticScan();

        foreach (var marker in new[] { "ReleaseExecution", "DeploymentExecution", "CanRelease", "CanDeploy" })
            AssertNoForbiddenIdentifier(source, marker);
    }

    [TestMethod]
    public void BlockE18_Receipt_RecordsDownstreamAuthorityBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "E18_DOWNSTREAM_AUTHORITY_ATTEMPT_DETECTION.md"));

        StringAssert.Contains(doc, "A receipt can describe what happened. It cannot authorize what happens next.");
        StringAssert.Contains(doc, "Receipts are witnesses, not permission slips.");
        StringAssert.Contains(doc, "E18 is a downstream-authority hard-stop regression. It adds no execution, approval, merge, release, deployment, or workflow-continuation path.");
        StringAssert.Contains(doc, "source apply authority");
        StringAssert.Contains(doc, "commit authority");
        StringAssert.Contains(doc, "push authority");
        StringAssert.Contains(doc, "workflow continuation");
        StringAssert.Contains(doc, "policy satisfaction");
        StringAssert.Contains(doc, "mutation authority");
    }

    private static void AssertInventoryCovers(string family, string coreTypeName)
    {
        Assert.IsTrue(ReceiptInventory.Any(item => item.Family == family && item.CoreTypeNames.Contains(coreTypeName, StringComparer.Ordinal)), family);
        CollectionAssert.Contains(CoreTypeNames(), coreTypeName);
    }

    private static void AssertEvidenceOnly(ReceiptLikeFixture fixture, params AuthorityAttemptCategory[] forbiddenCategories)
    {
        Assert.IsTrue(fixture.EvidenceOnly, fixture.Family);
        Assert.IsFalse(fixture.GrantsApproval, fixture.Family);
        Assert.IsFalse(fixture.SatisfiesPolicy, fixture.Family);
        Assert.IsFalse(fixture.SatisfiesValidationFreshness, fixture.Family);
        Assert.IsFalse(fixture.SatisfiesSourceSafety, fixture.Family);
        Assert.IsFalse(fixture.SatisfiesWorktreeSafety, fixture.Family);
        Assert.IsFalse(fixture.SatisfiesBranchSafety, fixture.Family);

        var findings = DownstreamAuthorityAttemptTestDetector.Detect(fixture);
        CollectionAssert.AreEqual(Array.Empty<AuthorityAttemptCategory>(), findings.Select(finding => finding.Category).ToArray(), fixture.Family);

        foreach (var category in forbiddenCategories)
            Assert.IsFalse(fixture.AuthorityFlags.Contains(category), $"{fixture.Family} exposed {category}.");

        CollectionAssert.Contains(fixture.RequiredNextGates.ToArray(), "RequiresFreshAuthority");
        CollectionAssert.Contains(fixture.RequiredNextGates.ToArray(), "RequiresHumanReview");
    }

    private static void AssertPoisonedText(string text, AuthorityAttemptCategory expected)
    {
        var fixture = EvidenceOnly(ReceiptFamily.PoisonedFixture, [], "poisoned-evidence:e18") with
        {
            ClaimedStates = [text]
        };

        var findings = DownstreamAuthorityAttemptTestDetector.Detect(fixture);
        CollectionAssert.Contains(findings.Select(finding => finding.Category).ToArray(), expected, text);
        Assert.IsTrue(fixture.EvidenceOnly);
    }

    private static ReceiptLikeFixture BuildSourceApplyReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.SourceApplyReceipt,
            ["RequiresFreshValidation", "RequiresCommitAuthority", "RequiresPushAuthority", "RequiresDraftPullRequestAuthority"],
            "source-apply-receipt:e18");

    private static ReceiptLikeFixture BuildCommitReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.CommitReceipt,
            ["RequiresPushAuthority", "RequiresDraftPullRequestAuthority", "RequiresFreshValidation"],
            "commit-receipt:e18",
            "commit-package:e18");

    private static ReceiptLikeFixture BuildPushReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.PushReceipt,
            ["RequiresDraftPullRequestAuthority", "RequiresReadyForReviewAuthority", "RequiresMergeAuthority"],
            "push-receipt:e18");

    private static ReceiptLikeFixture BuildDraftPullRequestReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.DraftPullRequestReceipt,
            ["RequiresReadyForReviewAuthority", "RequiresReviewerRequestAuthority", "RequiresMergeAuthority"],
            "draft-pr-receipt:e18");

    private static ReceiptLikeFixture BuildPrBranchUpdateReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.PullRequestBranchUpdateReceipt,
            ["RequiresReadyForReviewAuthority", "RequiresReviewerRequestAuthority", "RequiresMergeAuthority"],
            "pr-branch-update-receipt:e18");

    private static ReceiptLikeFixture BuildReadyForReviewReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.ReadyForReviewReceipt,
            ["RequiresReviewerRequestAuthority", "RequiresMergeAuthority", "RequiresReleaseAuthority"],
            "ready-for-review-receipt:e18",
            "ready-for-review-evidence:e18");

    private static ReceiptLikeFixture BuildRollbackReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.RollbackReceipt,
            ["RequiresRetryAuthority", "RequiresRecoveryAuthority", "RequiresFreshValidation"],
            "rollback-receipt:e18");

    private static ReceiptLikeFixture BuildRetryClassificationEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.RetryClassification,
            ["RequiresFreshAuthority", "RequiresFreshValidation", "RequiresConcurrentMutationGuard"],
            "retry-classification:e18");

    private static ReceiptLikeFixture BuildRecoveryReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.RecoveryEvidence,
            ["RequiresHumanReview", "RequiresFreshAuthority"],
            "recovery-evidence:e18");

    private static ReceiptLikeFixture BuildFailedApplyRecoveryReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.RecoveryEvidence,
            ["RequiresHumanReview", "RequiresRollbackDecision", "RequiresFreshSourceApplyAuthority"],
            "failed-apply-recovery:e18");

    private static ReceiptLikeFixture BuildFailedContinuationRecoveryReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.RecoveryEvidence,
            ["RequiresHumanReview", "RequiresFreshContinuationAuthority"],
            "failed-continuation-recovery:e18");

    private static ReceiptLikeFixture BuildValidationReceiptEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.ValidationReceipt,
            ["RequiresAcceptedApproval", "RequiresPolicySatisfaction", "RequiresDirtyWorktreeGuard"],
            "validation-receipt:e18");

    private static ReceiptLikeFixture BuildPostStateObservationEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.PostStateObservation,
            ["RequiresRecoveryAssessment", "RequiresFreshAuthority"],
            "post-state-observation:e18");

    private static ReceiptLikeFixture BuildDirtyWorktreeGuardDecisionEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.DirtyWorktreeGuardDecision,
            ["RequiresFreshAuthority", "RequiresMovedBaseGuard"],
            "dirty-worktree-guard:e18");

    private static ReceiptLikeFixture BuildMovedBaseGuardDecisionEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.MovedBaseGuardDecision,
            ["RequiresFreshAuthority", "RequiresStaleValidationGuard"],
            "moved-base-guard:e18");

    private static ReceiptLikeFixture BuildStaleValidationGuardDecisionEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.StaleValidationGuardDecision,
            ["RequiresFreshAuthority", "RequiresFreshValidation"],
            "stale-validation-guard:e18");

    private static ReceiptLikeFixture BuildBranchRemoteHeadDecisionEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.BranchRemoteHeadVerificationDecision,
            ["RequiresFreshAuthority", "RequiresDirtyWorktreeGuard"],
            "branch-remote-head-verification:e18");

    private static ReceiptLikeFixture BuildMergeReadinessEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.MergeReadinessEvidence,
            ["RequiresMergeDecision", "RequiresReleaseCandidateEvidence"],
            "merge-readiness:e18");

    private static ReceiptLikeFixture BuildReleaseReadinessEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.ReleaseReadinessEvidence,
            ["RequiresReleaseDecision", "RequiresDeploymentReadinessDecision"],
            "release-readiness:e18");

    private static ReceiptLikeFixture BuildReleaseCandidateEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.ReleaseCandidateEvidence,
            ["RequiresReleaseReadinessDecision", "RequiresDeploymentReadinessDecision"],
            "release-candidate:e18");

    private static ReceiptLikeFixture BuildOperationStatusProjectionEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.OperationStatusProjection,
            ["RequiresFreshAuthority", "RequiresAcceptedApproval", "RequiresPolicySatisfaction"],
            "operation-status:e18",
            "receipt-ref:e18") with
        {
            NextActions = ["inspect required evidence", "request fresh bounded authority", "request human review"]
        };

    private static ReceiptLikeFixture BuildReceiptReadModelEvidenceOnly() =>
        EvidenceOnly(ReceiptFamily.ReceiptReadModel,
            ["RequiresFreshAuthority", "RequiresHumanReview"],
            "receipt-read-model:e18");

    private static ReceiptLikeFixture EvidenceOnly(string family, IReadOnlyCollection<string> extraRequiredGates, params string[] evidenceRefs)
    {
        var gates = new List<string> { "RequiresFreshAuthority", "RequiresHumanReview" };
        gates.AddRange(extraRequiredGates);

        return new ReceiptLikeFixture(
            family,
            EvidenceOnly: true,
            EvidenceRefs: evidenceRefs,
            ClaimedStates: [],
            NextActions: ["inspect evidence refs", "request the next explicit authority gate"],
            DecisionTexts: [],
            RequiredNextGates: gates.Distinct(StringComparer.Ordinal).ToArray(),
            AuthorityFlags: [],
            GrantsApproval: false,
            SatisfiesPolicy: false,
            SatisfiesValidationFreshness: false,
            SatisfiesSourceSafety: false,
            SatisfiesWorktreeSafety: false,
            SatisfiesBranchSafety: false);
    }

    private static string[] CoreTypeNames() =>
        typeof(SourceApplyReceipt).Assembly.GetTypes()
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static string E18SourceForStaticScan()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "IronDev.IntegrationTests", "BlockE18DownstreamAuthorityAttemptDetectionTests.cs"));
        source = StripStringLiterals(source);

        foreach (var method in typeof(BlockE18DownstreamAuthorityAttemptDetectionTests).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<TestMethodAttribute>() is not null)
                source = source.Replace(method.Name, string.Empty, StringComparison.Ordinal);
        }

        foreach (var category in Enum.GetNames<AuthorityAttemptCategory>())
            source = source.Replace(category, string.Empty, StringComparison.Ordinal);

        return source;
    }

    private static void AssertNoForbiddenIdentifier(string source, string marker)
    {
        Assert.IsFalse(
            source.Contains(marker, StringComparison.Ordinal),
            $"E18-owned source must not introduce '{marker}'.");
    }

    private static string StripStringLiterals(string source)
    {
        var result = new char[source.Length];
        var inString = false;
        var inVerbatim = false;
        var inRaw = false;
        const int RawQuoteCount = 3;

        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (!inString && !inRaw && current == '"' && next == '"' && i + 2 < source.Length && source[i + 2] == '"')
            {
                inRaw = true;
                result[i] = ' ';
                continue;
            }

            if (inRaw)
            {
                if (current == '"' && i + RawQuoteCount - 1 < source.Length && source.Substring(i, RawQuoteCount).All(ch => ch == '"'))
                {
                    for (var j = 0; j < RawQuoteCount && i + j < result.Length; j++)
                        result[i + j] = ' ';
                    i += RawQuoteCount - 1;
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

    private static readonly ReceiptInventoryItem[] ReceiptInventory =
    [
        new(ReceiptFamily.SourceApplyReceipt, ["SourceApplyReceipt"]),
        new(ReceiptFamily.CommitPackage, ["CommitPackageResult"]),
        new(ReceiptFamily.CommitReceipt, ["ControlledCommitReceipt"]),
        new(ReceiptFamily.PushReceipt, ["ControlledPushReceipt"]),
        new(ReceiptFamily.DraftPullRequestReceipt, ["ControlledDraftPullRequestReceipt"]),
        new(ReceiptFamily.PullRequestBranchUpdateReceipt, ["PrBranchUpdateExecutionReceipt"]),
        new(ReceiptFamily.ReadyForReviewPackage, ["ReadyForReviewEligibilityPackage"]),
        new(ReceiptFamily.ReadyForReviewReceipt, ["ReadyForReviewExecutionReceipt"]),
        new(ReceiptFamily.RollbackReceipt, ["ControlledRollbackReceipt"]),
        new(ReceiptFamily.ValidationReceipt, ["ValidationResultPackageResult"]),
        new(ReceiptFamily.PostStateObservation, ["PostStateObservationDecision"]),
        new(ReceiptFamily.DirtyWorktreeGuardDecision, ["DirtyWorktreeGuardDecision"]),
        new(ReceiptFamily.MovedBaseGuardDecision, ["MovedBaseGuardDecision"]),
        new(ReceiptFamily.StaleValidationGuardDecision, ["StaleValidationGuardDecision"]),
        new(ReceiptFamily.BranchRemoteHeadVerificationDecision, ["BranchRemoteHeadVerificationDecision"]),
        new(ReceiptFamily.MergeReadinessEvidence, ["MergeReadinessEvidencePackage"]),
        new(ReceiptFamily.ReleaseReadinessEvidence, ["ReleaseReadinessEvidencePackage"]),
        new(ReceiptFamily.OperationStatusProjection, ["GovernedOperationStatus"]),
        new(ReceiptFamily.ReceiptReadModel, ["FrontendReceiptMetadataReadModel"])
    ];

    private sealed record ReceiptInventoryItem(string Family, string[] CoreTypeNames);

    private sealed record ReceiptLikeFixture(
        string Family,
        bool EvidenceOnly,
        IReadOnlyCollection<string> EvidenceRefs,
        IReadOnlyCollection<string> ClaimedStates,
        IReadOnlyCollection<string> NextActions,
        IReadOnlyCollection<string> DecisionTexts,
        IReadOnlyCollection<string> RequiredNextGates,
        IReadOnlyCollection<AuthorityAttemptCategory> AuthorityFlags,
        bool GrantsApproval,
        bool SatisfiesPolicy,
        bool SatisfiesValidationFreshness,
        bool SatisfiesSourceSafety,
        bool SatisfiesWorktreeSafety,
        bool SatisfiesBranchSafety);

    private sealed record DownstreamAuthorityFinding(AuthorityAttemptCategory Category, string Text);

    private static class DownstreamAuthorityAttemptTestDetector
    {
        private static readonly (string Phrase, AuthorityAttemptCategory Category)[] UnsafePhrases =
        [
            ("approval granted", AuthorityAttemptCategory.ApprovalSatisfactionAttempt),
            ("approval satisfied", AuthorityAttemptCategory.ApprovalSatisfactionAttempt),
            ("policy satisfied", AuthorityAttemptCategory.PolicySatisfactionAttempt),
            ("validation satisfied", AuthorityAttemptCategory.ValidationFreshnessAttempt),
            ("validation fresh", AuthorityAttemptCategory.ValidationFreshnessAttempt),
            ("source safe", AuthorityAttemptCategory.SourceSafetyAttempt),
            ("worktree safe", AuthorityAttemptCategory.WorktreeSafetyAttempt),
            ("branch safe", AuthorityAttemptCategory.BranchSafetyAttempt),
            ("safe to apply", AuthorityAttemptCategory.ApplyAuthorityAttempt),
            ("safe to commit", AuthorityAttemptCategory.CommitAuthorityAttempt),
            ("safe to push", AuthorityAttemptCategory.PushAuthorityAttempt),
            ("safe to open pr", AuthorityAttemptCategory.PullRequestAuthorityAttempt),
            ("safe to mark ready for review", AuthorityAttemptCategory.ReadyForReviewAuthorityAttempt),
            ("safe to request review", AuthorityAttemptCategory.ReviewRequestAuthorityAttempt),
            ("safe to merge", AuthorityAttemptCategory.MergeAuthorityAttempt),
            ("safe to release", AuthorityAttemptCategory.ReleaseAuthorityAttempt),
            ("safe to deploy", AuthorityAttemptCategory.DeploymentAuthorityAttempt),
            ("authorized to apply", AuthorityAttemptCategory.ApplyAuthorityAttempt),
            ("authorized to commit", AuthorityAttemptCategory.CommitAuthorityAttempt),
            ("authorized to push", AuthorityAttemptCategory.PushAuthorityAttempt),
            ("authorized to merge", AuthorityAttemptCategory.MergeAuthorityAttempt),
            ("authorized to release", AuthorityAttemptCategory.ReleaseAuthorityAttempt),
            ("authorized to deploy", AuthorityAttemptCategory.DeploymentAuthorityAttempt),
            ("ready to execute", AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt),
            ("ready to mutate", AuthorityAttemptCategory.ApplyAuthorityAttempt),
            ("ready to continue", AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt),
            ("continue workflow authorized", AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt),
            ("workflow continuation authorized", AuthorityAttemptCategory.WorkflowContinuationAuthorityAttempt),
            ("retry authorized", AuthorityAttemptCategory.RetryAuthorityAttempt),
            ("recovery authorized", AuthorityAttemptCategory.RecoveryAuthorityAttempt),
            ("rollback authorized", AuthorityAttemptCategory.RollbackAuthorityAttempt),
            ("memory promotion authorized", AuthorityAttemptCategory.MemoryPromotionAttempt)
        ];

        public static IReadOnlyList<DownstreamAuthorityFinding> Detect(ReceiptLikeFixture fixture)
        {
            var findings = new List<DownstreamAuthorityFinding>();

            foreach (var category in fixture.AuthorityFlags)
                findings.Add(new DownstreamAuthorityFinding(category, "authority flag"));

            if (fixture.GrantsApproval)
                findings.Add(new DownstreamAuthorityFinding(AuthorityAttemptCategory.ApprovalSatisfactionAttempt, "approval flag"));
            if (fixture.SatisfiesPolicy)
                findings.Add(new DownstreamAuthorityFinding(AuthorityAttemptCategory.PolicySatisfactionAttempt, "policy flag"));
            if (fixture.SatisfiesValidationFreshness)
                findings.Add(new DownstreamAuthorityFinding(AuthorityAttemptCategory.ValidationFreshnessAttempt, "validation freshness flag"));
            if (fixture.SatisfiesSourceSafety)
                findings.Add(new DownstreamAuthorityFinding(AuthorityAttemptCategory.SourceSafetyAttempt, "source safety flag"));
            if (fixture.SatisfiesWorktreeSafety)
                findings.Add(new DownstreamAuthorityFinding(AuthorityAttemptCategory.WorktreeSafetyAttempt, "worktree safety flag"));
            if (fixture.SatisfiesBranchSafety)
                findings.Add(new DownstreamAuthorityFinding(AuthorityAttemptCategory.BranchSafetyAttempt, "branch safety flag"));

            foreach (var text in fixture.ClaimedStates.Concat(fixture.NextActions).Concat(fixture.DecisionTexts))
            {
                foreach (var (phrase, category) in UnsafePhrases)
                {
                    if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                        findings.Add(new DownstreamAuthorityFinding(category, text));
                }
            }

            return findings;
        }
    }

    private enum AuthorityAttemptCategory
    {
        ApplyAuthorityAttempt,
        CommitAuthorityAttempt,
        PushAuthorityAttempt,
        PullRequestAuthorityAttempt,
        ReadyForReviewAuthorityAttempt,
        ReviewRequestAuthorityAttempt,
        MergeAuthorityAttempt,
        ReleaseAuthorityAttempt,
        DeploymentAuthorityAttempt,
        RollbackAuthorityAttempt,
        RetryAuthorityAttempt,
        RecoveryAuthorityAttempt,
        WorkflowContinuationAuthorityAttempt,
        ApprovalSatisfactionAttempt,
        PolicySatisfactionAttempt,
        ValidationFreshnessAttempt,
        SourceSafetyAttempt,
        WorktreeSafetyAttempt,
        BranchSafetyAttempt,
        MemoryPromotionAttempt
    }

    private static class ReceiptFamily
    {
        public const string SourceApplyReceipt = "source apply receipt";
        public const string CommitPackage = "commit package";
        public const string CommitReceipt = "commit receipt";
        public const string PushReceipt = "push receipt";
        public const string DraftPullRequestReceipt = "draft PR receipt";
        public const string PullRequestBranchUpdateReceipt = "PR branch update receipt";
        public const string ReadyForReviewPackage = "ready-for-review package";
        public const string ReadyForReviewReceipt = "ready-for-review receipt";
        public const string RollbackReceipt = "rollback receipt";
        public const string RetryClassification = "retry classification";
        public const string RecoveryEvidence = "recovery evidence";
        public const string ValidationReceipt = "validation receipt";
        public const string PostStateObservation = "post-state observation";
        public const string DirtyWorktreeGuardDecision = "dirty worktree guard decision";
        public const string MovedBaseGuardDecision = "moved-base guard decision";
        public const string StaleValidationGuardDecision = "stale validation guard decision";
        public const string BranchRemoteHeadVerificationDecision = "branch/remote/head verification decision";
        public const string GuardDecision = "guard decision";
        public const string MergeReadinessEvidence = "merge-readiness evidence";
        public const string ReleaseReadinessEvidence = "release-readiness evidence";
        public const string ReleaseCandidateEvidence = "release-candidate evidence";
        public const string OperationStatusProjection = "operation status projection";
        public const string ReceiptReadModel = "receipt read model";
        public const string EvidenceOnlyRefs = "evidence-only refs";
        public const string PoisonedFixture = "poisoned fixture";
    }
}

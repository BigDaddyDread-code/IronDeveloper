using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using IronDev.Core.Governance.CommitExecution;
using IronDev.Core.Governance.InterruptedRunRecovery;
using IronDev.Core.Governance.PullRequestExecution;
using IronDev.Core.Governance.PushExecution;
using IronDev.Core.Governance.RepoStateFreshness;
using BvCommitMessageEvidence = IronDev.Core.Governance.Commit.CommitMessageEvidence;
using BvCommitOperationAuthorityEvidence = IronDev.Core.Governance.Commit.CommitOperationAuthorityEvidence;
using BvCommitPackageBuilder = IronDev.Core.Governance.Commit.CommitPackageBuilder;
using BvCommitPackageManifest = IronDev.Core.Governance.Commit.CommitPackageManifest;
using BvCommitPackageRequest = IronDev.Core.Governance.Commit.CommitPackageRequest;
using BvCommitValidationRequirementEvidence = IronDev.Core.Governance.Commit.CommitValidationRequirementEvidence;
using BvExpectedDiffEvidence = IronDev.Core.Governance.Commit.ExpectedDiffEvidence;
using BvSourceApplyReceiptEvidence = IronDev.Core.Governance.Commit.SourceApplyReceiptEvidence;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockDogfoodBoundedAuthorityDraftPrLaneTests
{
    private const string RepoId = "BigDaddyDread-code/IronDeveloper";
    private const string Branch = "dogfood/bounded-authority-draft-pr-lane";
    private const string BaseBranch = "dogfood/ask-before-mutation-boundary-lane";
    private const string RunId = "run-pr24";
    private const string ProposalId = "pr24-bounded-authority-draft-pr-lane";
    private const string FileScope = "Docs/receipts/PR24_BOUNDED_AUTHORITY_DOGFOOD_LANE.md";
    private const string RemoteName = "origin";
    private const string RemoteUrl = "https://example.invalid/BigDaddyDread-code/IronDeveloper";
    private const string ParentCommitId = "base-pr24-001";
    private const string CommitId = "commit-pr24-001";
    private const string PreviousRemoteHead = "remote-pr24-000";
    private const int PullRequestNumber = 24024;
    private const string PullRequestUrl = "https://example.invalid/BigDaddyDread-code/IronDeveloper/pull/24024";
    private const string DiffHash = "sha256:diff-pr24-001";

    private static readonly DateTimeOffset ValidationObservedAtUtc = DateTimeOffset.Parse("2026-06-22T10:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-22T11:00:00Z");
    private static readonly DateTimeOffset ValidationExpiresAtUtc = DateTimeOffset.Parse("2026-06-22T12:00:00Z");
    private static readonly DateTimeOffset GrantExpiresAtUtc = DateTimeOffset.Parse("2026-06-22T13:00:00Z");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task BoundedDogfoodLane_ProducesPatchPackage()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.PatchPackageCreated, string.Join(", ", lane.Result.Issues));
        AssertFileExists(lane.Result.PatchPackagePath, "patch.diff");
        AssertFileExists(lane.Result.PatchPackagePath, "review-summary.md");
        AssertFileExists(lane.Result.PatchPackagePath, "known-risks.md");
        AssertFileExists(lane.Result.PatchPackagePath, "validation-summary.md");
        AssertFileExists(lane.Result.PatchPackagePath, "patch-package-manifest.json");
        AssertFileExists(lane.Result.PatchPackagePath, "operation-status.json");
        StringAssert.StartsWith(lane.Result.PatchHash, "sha256:");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ReportsValidation()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.ValidationResultCreated, string.Join(", ", lane.Result.Issues));
        Assert.AreEqual(ValidationOutcome.Passed, lane.ValidationResult.Outcome);
        Assert.AreEqual(GovernedOperationState.Completed, lane.ValidationResult.Status.State);
        AssertContains(lane.ValidationResult.Status.EvidenceRefs, "validation-outcome:passed");
        Assert.IsFalse(lane.ValidationResult.StatusValidation.Boundary.CanApprove);
        Assert.IsFalse(lane.ValidationResult.StatusValidation.Boundary.CanSatisfyPolicy);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ReportsFreshness()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.FreshnessReported);
        Assert.AreEqual(RepoStateFreshnessVerdict.Fresh, lane.FreshnessResult.Verdict);
        Assert.IsTrue(lane.FreshnessResult.IsFreshForMutation);
        Assert.IsFalse(lane.FreshnessResult.Boundary.CanApplySource);
        AssertContains(lane.Result.BoundaryNotes, "Freshness is checked before mutation but is not authority.");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_UsesScopedBoundedAuthorityGrant()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.AreEqual(RepoId, lane.Grant.Repository);
        Assert.AreEqual(Branch, lane.Grant.Branch);
        Assert.AreEqual(RunId, lane.Grant.RunId);
        Assert.AreEqual(lane.Result.PatchHash, lane.Grant.PatchHash);
        CollectionAssert.AreEquivalent(new[] { FileScope }, lane.Grant.AllowedFilePaths.ToArray());
        CollectionAssert.AreEquivalent(
            new[]
            {
                RunAuthorityOperationKind.SourceApply,
                RunAuthorityOperationKind.Commit,
                RunAuthorityOperationKind.Push,
                RunAuthorityOperationKind.DraftPullRequest
            },
            lane.Grant.AllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            new[]
            {
                RunAuthorityOperationKind.ReadyForReview,
                RunAuthorityOperationKind.Merge,
                RunAuthorityOperationKind.Release,
                RunAuthorityOperationKind.Deployment,
                RunAuthorityOperationKind.MemoryPromotion,
                RunAuthorityOperationKind.WorkflowContinuation
            },
            lane.Grant.StopBeforeOperations.ToArray());
        Assert.IsFalse(lane.Grant.AllowedFilePaths.Any(path => path.Contains('*') || path.Contains('?')));
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksWrongRepo()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { GrantRepository = "other/repo" }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, "BoundedAuthorityRepositoryMismatch");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksWrongBranch()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { GrantBranch = "other-branch" }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, "BoundedAuthorityBranchMismatch");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksWrongRun()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { GrantRunId = "other-run" }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, "BoundedAuthorityRunIdMismatch");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksWrongPatchHash()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { GrantPatchHash = "sha256:other" }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, "BoundedAuthorityPatchHashMismatch");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksForbiddenFile()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { ForbiddenFilePath = FileScope }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, $"BoundedAuthorityFileForbidden:{FileScope}");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksExpiredGrant()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { GrantExpiresAtUtc = ObservedAtUtc.AddMinutes(-1) }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, "BoundedAuthorityExpired");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksMissingRequiredValidation()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { OmitValidationEvidence = true }).ConfigureAwait(false);

        AssertBlockedBeforeMutation(lane, "BoundedAuthorityValidationRequired");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_BlocksStaleFreshness()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync(new() { MoveObservedHead = true }).ConfigureAwait(false);

        Assert.IsFalse(lane.Result.SourceApplied);
        Assert.AreEqual(RepoStateFreshnessVerdict.Stale, lane.FreshnessResult.Verdict);
        AssertContains(lane.Result.Issues, "RepoStateNotFreshForMutation");
        Assert.AreEqual(0, lane.CommitGateway.CommitCalls);
        Assert.AreEqual(0, lane.PushGateway.PushCalls);
        Assert.AreEqual(0, lane.DraftGateway.MutationCalls);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_SourceApplyExecutesUnderScopedAuthority()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.SourceApplied, string.Join(", ", lane.Result.Issues));
        Assert.IsNotNull(lane.SourceApplyReceipt);
        Assert.AreEqual(RepoId, lane.SourceApplyReceipt!.Repository);
        Assert.AreEqual(Branch, lane.SourceApplyReceipt.Branch);
        Assert.AreEqual(RunId, lane.SourceApplyReceipt.RunId);
        Assert.AreEqual(lane.Result.PatchHash, lane.SourceApplyReceipt.PatchHash);
        CollectionAssert.AreEquivalent(new[] { FileScope }, lane.SourceApplyReceipt.AppliedFilePaths.ToArray());
        StringAssert.Contains(File.ReadAllText(Path.Combine(lane.SourcePath, FileScope)), "PR24 bounded authority lane applied in fixture.");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_SourceApplyReceiptDoesNotGrantCommit()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsNotNull(lane.SourceApplyReceipt);
        AssertContains(lane.Result.ForbiddenActions, "do not commit from source apply receipt");
        Assert.AreEqual(RunAuthorityOperationKind.Commit, lane.CommitDecision.OperationKind);
        Assert.IsTrue(lane.CommitDecision.IsEligibleUnderProfileAndGrant);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_CreatesCommitPackage()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.CommitPackageCreated, string.Join(", ", lane.Result.Issues));
        Assert.IsNotNull(lane.CommitPackageManifest);
        Assert.AreEqual(RepoId, lane.CommitPackageManifest!.Repository);
        Assert.AreEqual(Branch, lane.CommitPackageManifest.Branch);
        Assert.AreEqual(lane.Result.PatchHash, lane.CommitPackageManifest.PatchHash);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ExecutesCommitThroughFakeGateway()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.Committed, string.Join(", ", lane.Result.Issues));
        Assert.AreEqual(ControlledCommitExecutionVerdict.Completed, lane.CommitResult!.Verdict);
        Assert.AreEqual(1, lane.CommitGateway.CommitCalls);
        Assert.IsFalse(lane.CommitGateway.RealProviderCalled);
        Assert.IsTrue(lane.CommitGateway.LastRequest!.DisableHooks);
        CollectionAssert.AreEquivalent(new[] { FileScope }, lane.CommitGateway.LastRequest.FilePathsToStage.ToArray());
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_CommitReceiptDoesNotGrantPush()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsNotNull(lane.CommitResult?.Receipt);
        AssertContains(lane.CommitResult!.OperationStatus.ForbiddenActions, "do not push from commit receipt");
        Assert.AreEqual(RunAuthorityOperationKind.Push, lane.PushDecision.OperationKind);
        Assert.IsTrue(lane.PushDecision.IsEligibleUnderProfileAndGrant);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ExecutesPushThroughFakeGateway()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.Pushed, string.Join(", ", lane.Result.Issues));
        Assert.AreEqual(ControlledPushExecutionVerdict.Completed, lane.PushResult!.Verdict);
        Assert.AreEqual(1, lane.PushGateway.PushCalls);
        Assert.IsFalse(lane.PushGateway.RealProviderCalled);
        Assert.IsTrue(lane.PushGateway.LastRequest!.ForcePushDisabled);
        Assert.IsTrue(lane.PushGateway.LastRequest.TagsDisabled);
        Assert.AreEqual(Branch, lane.PushGateway.LastRequest.RemoteBranch);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_PushReceiptDoesNotGrantPullRequest()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsNotNull(lane.PushResult?.Receipt);
        AssertContains(lane.PushResult!.OperationStatus.ForbiddenActions, "do not create PR from push receipt");
        Assert.AreEqual(RunAuthorityOperationKind.DraftPullRequest, lane.DraftPullRequestDecision.OperationKind);
        Assert.IsTrue(lane.DraftPullRequestDecision.IsEligibleUnderProfileAndGrant);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_CreatesDraftPullRequestThroughFakeGateway()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.DraftPullRequestCreated, string.Join(", ", lane.Result.Issues));
        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Completed, lane.DraftPullRequestResult!.Verdict);
        Assert.AreEqual(1, lane.DraftGateway.MutationCalls);
        Assert.IsFalse(lane.DraftGateway.RealProviderCalled);
        Assert.IsTrue(lane.DraftGateway.LastRequest!.DraftOnly);
        Assert.IsTrue(lane.DraftGateway.LastRequest.ReadyForReviewDisabled);
        Assert.IsTrue(lane.DraftGateway.LastRequest.ReviewerRequestsDisabled);
        Assert.IsTrue(lane.DraftGateway.LastRequest.MergeDisabled);
        Assert.IsNull(lane.DraftGateway.LastRequest.ExistingPullRequestNumber);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_DraftReceiptDoesNotGrantReadyForReview()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsNotNull(lane.DraftPullRequestResult?.Receipt);
        AssertContains(lane.DraftPullRequestResult!.OperationStatus.ForbiddenActions, "do not mark ready for review from draft PR receipt");
        Assert.IsFalse(lane.DraftPullRequestResult.StatusValidation.Boundary.CanExecute);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_StopsBeforeReadyForReview()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertStop(lane, RunAuthorityOperationKind.ReadyForReview, "BoundedAuthorityStopBefore:ReadyForReview");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_StopsBeforeMerge()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertStop(lane, RunAuthorityOperationKind.Merge, "BoundedAuthorityStopBefore:Merge");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_StopsBeforeRelease()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertStop(lane, RunAuthorityOperationKind.Release, "BoundedAuthorityStopBefore:Release");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_StopsBeforeDeployment()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertStop(lane, RunAuthorityOperationKind.Deployment, "BoundedAuthorityStopBefore:Deployment");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_StopsBeforeMemoryPromotion()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertStop(lane, RunAuthorityOperationKind.MemoryPromotion, "BoundedAuthorityStopBefore:MemoryPromotion");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_StopsBeforeWorkflowContinuation()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertStop(lane, RunAuthorityOperationKind.WorkflowContinuation, "BoundedAuthorityStopBefore:WorkflowContinuation");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_HostileTaskValidationStatusAndPrTextDoNotExpandAuthority()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        var reviewSummary = File.ReadAllText(lane.Result.ReviewSummaryPath);
        var validationSummary = File.ReadAllText(Path.Combine(lane.Result.ValidationPackagePath, "validation-summary.md"));
        StringAssert.Contains(reviewSummary, "Hostile text says mark ready and merge");
        StringAssert.Contains(validationSummary, "validation says release candidate and deploy now");
        StringAssert.Contains(lane.DraftGateway.LastRequest!.Body, "This PR is not ready for review and is not a release candidate.");
        AssertStop(lane, RunAuthorityOperationKind.ReadyForReview, "BoundedAuthorityStopBefore:ReadyForReview");
        AssertStop(lane, RunAuthorityOperationKind.Merge, "BoundedAuthorityStopBefore:Merge");
        AssertStop(lane, RunAuthorityOperationKind.Release, "BoundedAuthorityStopBefore:Release");
        AssertStop(lane, RunAuthorityOperationKind.Deployment, "BoundedAuthorityStopBefore:Deployment");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_HostileMemoryTextDoesNotContinueWorkflow()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertContains(lane.Result.EvidenceRefs, "memory-note:continue-workflow");
        AssertStop(lane, RunAuthorityOperationKind.WorkflowContinuation, "BoundedAuthorityStopBefore:WorkflowContinuation");
        Assert.IsFalse(lane.Result.WorkflowContinued);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_HostileUiTextDoesNotMarkReady()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertContains(lane.Result.EvidenceRefs, "ui-state:mark-ready");
        AssertStop(lane, RunAuthorityOperationKind.ReadyForReview, "BoundedAuthorityStopBefore:ReadyForReview");
        Assert.IsFalse(lane.Result.ReadyForReviewMarked);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_DoesNotCallRealProvider()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsFalse(lane.Result.RealProviderCalled);
        Assert.IsFalse(lane.SourceApplyGateway.RealProviderCalled);
        Assert.IsFalse(lane.CommitGateway.RealProviderCalled);
        Assert.IsFalse(lane.PushGateway.RealProviderCalled);
        Assert.IsFalse(lane.DraftGateway.RealProviderCalled);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_DoesNotMutateRealRepository()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsFalse(lane.Result.RealRepositoryMutated);
        Assert.IsFalse(SameOrChild(lane.SourcePath, FindRepositoryRoot()), lane.SourcePath);
        Assert.IsFalse(SameOrChild(lane.WorkspacePath, FindRepositoryRoot()), lane.WorkspacePath);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ReceiptsBindRepoBranchRunPatchAndScope()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        AssertReceiptBinding(lane.SourceApplyReceipt!);
        Assert.AreEqual(RepoId, lane.CommitResult!.Receipt!.Repository);
        Assert.AreEqual(Branch, lane.CommitResult.Receipt.Branch);
        Assert.AreEqual(RunId, lane.CommitResult.Receipt.RunId);
        Assert.AreEqual(lane.Result.PatchHash, lane.CommitResult.Receipt.PatchHash);
        CollectionAssert.AreEquivalent(new[] { FileScope }, lane.CommitResult.Receipt.CommittedFilePaths.ToArray());
        Assert.AreEqual(RepoId, lane.PushResult!.Receipt!.Repository);
        Assert.AreEqual(Branch, lane.PushResult.Receipt.Branch);
        Assert.AreEqual(RunId, lane.PushResult.Receipt.RunId);
        Assert.AreEqual(lane.Result.PatchHash, lane.PushResult.Receipt.PatchHash);
        Assert.AreEqual(RepoId, lane.DraftPullRequestResult!.Receipt!.Repository);
        Assert.AreEqual(Branch, lane.DraftPullRequestResult.Receipt.HeadBranch);
        Assert.AreEqual(BaseBranch, lane.DraftPullRequestResult.Receipt.BaseBranch);
        Assert.AreEqual(RunId, lane.DraftPullRequestResult.Receipt.RunId);
        Assert.AreEqual(lane.Result.PatchHash, lane.DraftPullRequestResult.Receipt.PatchHash);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ArtifactsAreHumanReviewable()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        var summary = File.ReadAllText(lane.Result.ReviewSummaryPath);
        Assert.IsTrue(summary.Length > 200, summary);
        StringAssert.Contains(summary, "Task:");
        StringAssert.Contains(summary, "Patch hash:");
        StringAssert.Contains(summary, "Next safe actions:");
        StringAssert.Contains(summary, "Forbidden actions:");
        AssertContains(lane.Result.BoundaryNotes, "A scoped key opens one door, not the building.");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ShowsNextSafeActions()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.NextSafeActionsShown);
        AssertContains(lane.Result.NextSafeActions, "human review the controlled draft PR before any ready-for-review authority");
        AssertContains(lane.Result.NextSafeActions, "request a separate ready-for-review package if review is desired");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ShowsForbiddenActions()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.Result.ForbiddenActionsShown);
        AssertForbiddenContains(lane.Result.ForbiddenActions, "do not mark ready for review");
        AssertForbiddenContains(lane.Result.ForbiddenActions, "do not merge");
        AssertForbiddenContains(lane.Result.ForbiddenActions, "do not release");
        AssertForbiddenContains(lane.Result.ForbiddenActions, "do not deploy");
        AssertForbiddenContains(lane.Result.ForbiddenActions, "do not continue workflow");
    }

    [TestMethod]
    public void StaticMutationSurfaceScan_NoRealGitGithubProviderOrDeploymentSurfaceAdded()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "IronDev.IntegrationTests", "BlockDogfoodBoundedAuthorityDraftPrLaneTests.cs");
        var text = File.ReadAllText(file);
        foreach (var forbidden in new[]
        {
            "Run" + "ProcessAsync",
            "Process" + "StartInfo",
            "git " + "apply",
            "git " + "commit",
            "git " + "push",
            "gh pr " + "create",
            "gh " + "api",
            "kub" + "ectl",
            "terraform " + "apply",
            "docker " + "push",
            "npm " + "publish",
            "real" + "-provider"
        })
        {
            Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_PR23StillBlocksSourceApplyWithoutAuthority()
    {
        var status = ControlledSourceApplyGovernedOperationStatusMapper.Map(new ControlledSourceApplyStatusInput
        {
            OperationId = "source-apply-status-pr23-regression",
            SourceApplyId = "source-apply-pr23",
            Subject = $"repo:{RepoId} branch:dogfood/ask-before-mutation-boundary-lane",
            RepoId = RepoId,
            Branch = "dogfood/ask-before-mutation-boundary-lane",
            PatchHash = "sha256:pr23-regression",
            StatusKind = ControlledSourceApplyStatusKind.Blocked,
            EvidenceRefs = ["patch-package:pr23"],
            ReceiptRefs = [],
            BlockedReasons = ["MissingExplicitSourceApplyAuthority"],
            MissingEvidence = ["bounded-authority-grant:SourceApply"],
            ForbiddenActions = ["do not apply source without explicit source-apply authority"],
            ObservedAtUtc = ObservedAtUtc
        });

        await Task.CompletedTask.ConfigureAwait(false);
        Assert.AreEqual(GovernedOperationState.Blocked, status.Status.State);
        Assert.IsFalse(status.CanonicalValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public void BoundedDogfoodLane_PR22NoApprovalDogfoodStillProducesEvidenceOnly()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "PR22_NO_APPROVAL_DOGFOOD_LANE.md"));

        StringAssert.Contains(doc, "Useful evidence is not mutation permission.");
        StringAssert.Contains(doc, "Patch package is not source apply.");
        StringAssert.Contains(doc, "Status is not authority.");
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_PR21FreshnessGuardRemainsExplanationOnly()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        Assert.IsTrue(lane.FreshnessResult.Boundary.CanExplainFreshness);
        Assert.IsTrue(lane.FreshnessResult.Boundary.CanInspectEvidence);
        Assert.IsFalse(lane.FreshnessResult.Boundary.CanApplySource);
        Assert.IsFalse(lane.FreshnessResult.Boundary.CanCommit);
        Assert.IsFalse(lane.FreshnessResult.Boundary.CanPush);
        Assert.IsFalse(lane.FreshnessResult.Boundary.CanCreatePullRequest);
    }

    [TestMethod]
    public void BoundedDogfoodLane_PR20InterruptedRecoveryRemainsReadOnly()
    {
        var report = InterruptedRunRecoveryDiagnosisService.Diagnose(new InterruptedRunEvidenceSnapshot
        {
            RunId = RunId,
            WorkspaceEvidenceRefs = ["workspace:pr24"],
            PatchPackageEvidenceRefs = ["patch-package:pr24"],
            ValidationResultPackageEvidenceRefs = ["validation-result:pr24"],
            ValidationOutcome = InterruptedRunValidationOutcome.Passed,
            WorktreeState = InterruptedRunWorktreeState.Clean,
            SourceApplyStartedEvidenceRefs = [],
            CompletedSourceApplyReceiptRefs = [],
            CommitPackageEvidenceRefs = [],
            CommitReceiptRefs = [],
            CommitHashEvidenceRefs = [],
            PushReceiptRefs = [],
            RemoteBranchEvidenceRefs = [],
            DraftPullRequestReceiptRefs = []
        });

        Assert.AreEqual(RunRecoveryBoundary.Diagnosis, report.Boundary);
        Assert.IsFalse(report.Boundary.CanResumeRun);
        Assert.IsFalse(report.Boundary.CanRetryStep);
        Assert.IsFalse(report.Boundary.CanRollbackSource);
        Assert.IsFalse(report.Boundary.CanContinueWorkflow);
    }

    [TestMethod]
    public void BoundedDogfoodLane_CARollbackExecutorStillRequiresSeparateAuthority()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "CA_CONTROLLED_ROLLBACK_EXECUTOR.md"));

        StringAssert.Contains(doc, "Rollback executes only under explicit rollback authority or a narrow policy-approved rollback path.");
        StringAssert.Contains(doc, "Rollback receipt is not commit authority.");
    }

    [TestMethod]
    public void BoundedDogfoodLane_ProposalOnlyStillForbidsMutation()
    {
        var profile = ProposalOnlyRunProfileEvaluator.Evaluate(new ProposalOnlyRunProfileEvaluationRequest
        {
            OperationId = "proposal-only-pr24",
            OperationKind = ProposalOnlyOperationKinds.SourceApply,
            Subject = $"repo:{RepoId} branch:{Branch}",
            RepoId = RepoId,
            Branch = Branch,
            EvidenceRefs = ["hostile-text:apply-now"],
            ObservedAtUtc = ObservedAtUtc
        });

        Assert.IsFalse(profile.IsAllowed);
        Assert.IsFalse(profile.StatusValidation.Boundary.CanMutateSource);
        Assert.IsFalse(profile.StatusValidation.Boundary.CanSourceApply);
    }

    [TestMethod]
    public async Task BoundedDogfoodLane_ReadyMergeReleaseDeployMemoryAndContinuationRemainSeparate()
    {
        using var lane = await BoundedAuthorityDraftPrLaneFixture.RunAsync().ConfigureAwait(false);

        foreach (var operation in new[]
        {
            RunAuthorityOperationKind.ReadyForReview,
            RunAuthorityOperationKind.Merge,
            RunAuthorityOperationKind.Release,
            RunAuthorityOperationKind.Deployment,
            RunAuthorityOperationKind.MemoryPromotion,
            RunAuthorityOperationKind.WorkflowContinuation
        })
        {
            Assert.IsFalse(lane.StopDecisions[operation].IsEligibleUnderProfileAndGrant, operation.ToString());
        }

        Assert.IsFalse(lane.Result.ReadyForReviewMarked);
        Assert.IsFalse(lane.Result.Merged);
        Assert.IsFalse(lane.Result.Released);
        Assert.IsFalse(lane.Result.Deployed);
        Assert.IsFalse(lane.Result.MemoryPromoted);
        Assert.IsFalse(lane.Result.WorkflowContinued);
    }

    private static void AssertBlockedBeforeMutation(BoundedAuthorityDraftPrLaneFixture lane, string issue)
    {
        Assert.IsFalse(lane.Result.SourceApplied);
        Assert.IsFalse(lane.Result.Committed);
        Assert.IsFalse(lane.Result.Pushed);
        Assert.IsFalse(lane.Result.DraftPullRequestCreated);
        AssertContains(lane.Result.Issues, issue);
        Assert.AreEqual(0, lane.CommitGateway.CommitCalls);
        Assert.AreEqual(0, lane.PushGateway.PushCalls);
        Assert.AreEqual(0, lane.DraftGateway.MutationCalls);
    }

    private static void AssertStop(
        BoundedAuthorityDraftPrLaneFixture lane,
        RunAuthorityOperationKind operation,
        string expectedReason)
    {
        Assert.IsTrue(lane.StopDecisions.TryGetValue(operation, out var decision), operation.ToString());
        Assert.IsFalse(decision!.IsEligibleUnderProfileAndGrant, operation.ToString());
        AssertContains(decision.BlockedReasons, expectedReason);
        AssertContains(decision.ForbiddenActions, $"do not perform {operation} from PR24 dogfood grant");
    }

    private static void AssertReceiptBinding(BvSourceApplyReceiptEvidence receipt)
    {
        Assert.AreEqual(RepoId, receipt.Repository);
        Assert.AreEqual(Branch, receipt.Branch);
        Assert.AreEqual(RunId, receipt.RunId);
        CollectionAssert.AreEquivalent(new[] { FileScope }, receipt.AppliedFilePaths.ToArray());
    }

    private static void AssertFileExists(string directory, string fileName) =>
        Assert.IsTrue(File.Exists(Path.Combine(directory, fileName)), Path.Combine(directory, fileName));

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");

    private static void AssertForbiddenContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(
            values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected forbidden action containing '{expected}' in: {string.Join(", ", values)}");

    private static bool SameOrChild(string candidate, string root)
    {
        var candidateFull = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidateFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase) ||
            candidateFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            candidateFull.StartsWith(rootFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class BoundedAuthorityDraftPrLaneFixture : IDisposable
    {
        private BoundedAuthorityDraftPrLaneFixture(
            string root,
            string sourcePath,
            string workspacePath,
            string outputPath,
            BoundedAuthorityDogfoodGrant grant,
            DisposableWorkspacePatchPackageResult patchPackage,
            ValidationResultPackageResult validationResult,
            RepoStateFreshnessResult freshnessResult,
            FakeControlledSourceApplyGateway sourceApplyGateway,
            FakeCommitWorktreeInspector commitInspector,
            FakeControlledCommitGateway commitGateway,
            FakePushRemoteStateInspector pushInspector,
            FakeControlledPushGateway pushGateway,
            FakeDraftPullRequestInspector draftInspector,
            FakeControlledDraftPullRequestGateway draftGateway,
            OperationEligibilityDecision sourceApplyDecision,
            OperationEligibilityDecision commitDecision,
            OperationEligibilityDecision pushDecision,
            OperationEligibilityDecision draftPullRequestDecision,
            IReadOnlyDictionary<RunAuthorityOperationKind, OperationEligibilityDecision> stopDecisions,
            BvSourceApplyReceiptEvidence? sourceApplyReceipt,
            BvCommitPackageRequest? commitPackageRequest,
            BvCommitPackageManifest? commitPackageManifest,
            ControlledCommitExecutionResult? commitResult,
            ControlledPushExecutionResult? pushResult,
            ControlledDraftPullRequestExecutionResult? draftPullRequestResult,
            BoundedAuthorityDogfoodLaneResult result)
        {
            Root = root;
            SourcePath = sourcePath;
            WorkspacePath = workspacePath;
            OutputPath = outputPath;
            Grant = grant;
            PatchPackage = patchPackage;
            ValidationResult = validationResult;
            FreshnessResult = freshnessResult;
            SourceApplyGateway = sourceApplyGateway;
            CommitInspector = commitInspector;
            CommitGateway = commitGateway;
            PushInspector = pushInspector;
            PushGateway = pushGateway;
            DraftInspector = draftInspector;
            DraftGateway = draftGateway;
            SourceApplyDecision = sourceApplyDecision;
            CommitDecision = commitDecision;
            PushDecision = pushDecision;
            DraftPullRequestDecision = draftPullRequestDecision;
            StopDecisions = stopDecisions;
            SourceApplyReceipt = sourceApplyReceipt;
            CommitPackageRequest = commitPackageRequest;
            CommitPackageManifest = commitPackageManifest;
            CommitResult = commitResult;
            PushResult = pushResult;
            DraftPullRequestResult = draftPullRequestResult;
            Result = result;
        }

        public string Root { get; }
        public string SourcePath { get; }
        public string WorkspacePath { get; }
        public string OutputPath { get; }
        public BoundedAuthorityDogfoodGrant Grant { get; }
        public DisposableWorkspacePatchPackageResult PatchPackage { get; }
        public ValidationResultPackageResult ValidationResult { get; }
        public RepoStateFreshnessResult FreshnessResult { get; }
        public FakeControlledSourceApplyGateway SourceApplyGateway { get; }
        public FakeCommitWorktreeInspector CommitInspector { get; }
        public FakeControlledCommitGateway CommitGateway { get; }
        public FakePushRemoteStateInspector PushInspector { get; }
        public FakeControlledPushGateway PushGateway { get; }
        public FakeDraftPullRequestInspector DraftInspector { get; }
        public FakeControlledDraftPullRequestGateway DraftGateway { get; }
        public OperationEligibilityDecision SourceApplyDecision { get; }
        public OperationEligibilityDecision CommitDecision { get; }
        public OperationEligibilityDecision PushDecision { get; }
        public OperationEligibilityDecision DraftPullRequestDecision { get; }
        public IReadOnlyDictionary<RunAuthorityOperationKind, OperationEligibilityDecision> StopDecisions { get; }
        public BvSourceApplyReceiptEvidence? SourceApplyReceipt { get; }
        public BvCommitPackageRequest? CommitPackageRequest { get; }
        public BvCommitPackageManifest? CommitPackageManifest { get; }
        public ControlledCommitExecutionResult? CommitResult { get; }
        public ControlledPushExecutionResult? PushResult { get; }
        public ControlledDraftPullRequestExecutionResult? DraftPullRequestResult { get; }
        public BoundedAuthorityDogfoodLaneResult Result { get; }

        public static async Task<BoundedAuthorityDraftPrLaneFixture> RunAsync(DogfoodLaneOptions? options = null)
        {
            options ??= new();
            var root = Path.Combine(Path.GetTempPath(), "irondev-pr24-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "durable-source-fixture");
            var workspace = Path.Combine(root, "disposable-workspace");
            var output = Path.Combine(root, "packages");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(output);
            WriteDurableSource(source);
            WriteDisposableWorkspace(source, workspace);

            var patchText = File.ReadAllText(Path.Combine(workspace, "patch.diff"), Encoding.UTF8);
            var patchHash = HashText(patchText);
            var validation = BuildValidationPackage(workspace, output, patchHash, options);
            var patchPackage = BuildPatchPackage(workspace, output, validation);
            var freshness = RepoStateFreshnessGuard.Evaluate(FreshnessRequest(patchHash, patchPackage, validation, options));
            var grant = BuildGrant(patchHash, options);
            var sourceApplyGateway = new FakeControlledSourceApplyGateway(source);

            var sourceApplyDecision = ScopedDogfoodAuthority.Evaluate(
                grant,
                RunAuthorityOperationKind.SourceApply,
                RepoId,
                Branch,
                RunId,
                patchHash,
                [FileScope],
                validation,
                ObservedAtUtc);
            BvSourceApplyReceiptEvidence? sourceApplyReceipt = null;
            var issues = patchPackage.Issues
                .Concat(validation.Issues)
                .Concat(sourceApplyDecision.BlockedReasons)
                .ToList();

            if (freshness.Verdict != RepoStateFreshnessVerdict.Fresh)
                issues.Add("RepoStateNotFreshForMutation");

            if (sourceApplyDecision.IsEligibleUnderProfileAndGrant && freshness.Verdict == RepoStateFreshnessVerdict.Fresh)
                sourceApplyReceipt = sourceApplyGateway.Apply(sourceApplyDecision, patchHash);

            var commitDecision = ScopedDogfoodAuthority.Evaluate(
                grant,
                RunAuthorityOperationKind.Commit,
                RepoId,
                Branch,
                RunId,
                patchHash,
                [FileScope],
                validation,
                ObservedAtUtc);
            var pushDecision = ScopedDogfoodAuthority.Evaluate(
                grant,
                RunAuthorityOperationKind.Push,
                RepoId,
                Branch,
                RunId,
                patchHash,
                [FileScope],
                validation,
                ObservedAtUtc);
            var draftDecision = ScopedDogfoodAuthority.Evaluate(
                grant,
                RunAuthorityOperationKind.DraftPullRequest,
                RepoId,
                Branch,
                RunId,
                patchHash,
                [FileScope],
                validation,
                ObservedAtUtc);

            BvCommitPackageRequest? commitPackageRequest = null;
            BvCommitPackageManifest? commitPackageManifest = null;
            ControlledCommitExecutionResult? commitResult = null;
            var commitInspector = new FakeCommitWorktreeInspector();
            var commitGateway = new FakeControlledCommitGateway();
            if (sourceApplyReceipt is not null && commitDecision.IsEligibleUnderProfileAndGrant)
            {
                commitPackageRequest = BuildCommitPackageRequest(sourceApplyReceipt, commitDecision, patchHash);
                var package = BvCommitPackageBuilder.Build(commitPackageRequest);
                issues.AddRange(package.Issues);
                commitPackageManifest = package.Manifest;
                if (package.IsPackageCreated && commitPackageManifest is not null)
                {
                    commitInspector = new FakeCommitWorktreeInspector
                    {
                        PreObservations = [GoodCommitPreObservation(patchHash)],
                        PostObservations = [GoodCommitPostObservation()]
                    };
                    commitResult = await ControlledCommitExecutor.ExecuteAsync(
                        BuildCommitExecutionRequest(commitPackageRequest, commitPackageManifest, patchHash),
                        commitInspector,
                        commitGateway).ConfigureAwait(false);
                    issues.AddRange(commitResult.Issues);
                }
            }

            ControlledPushExecutionResult? pushResult = null;
            var pushInspector = new FakePushRemoteStateInspector();
            var pushGateway = new FakeControlledPushGateway();
            if (commitResult?.Verdict == ControlledCommitExecutionVerdict.Completed && pushDecision.IsEligibleUnderProfileAndGrant)
            {
                pushInspector = new FakePushRemoteStateInspector
                {
                    PreObservations = [GoodPushPreObservation()],
                    PostObservations = [GoodPushPostObservation()]
                };
                pushResult = await ControlledPushExecutor.ExecuteAsync(
                    BuildPushExecutionRequest(pushDecision, commitResult.Receipt!, patchHash),
                    pushInspector,
                    pushGateway).ConfigureAwait(false);
                issues.AddRange(pushResult.Issues);
            }

            ControlledDraftPullRequestExecutionResult? draftResult = null;
            var draftInspector = new FakeDraftPullRequestInspector();
            var draftGateway = new FakeControlledDraftPullRequestGateway();
            if (pushResult?.Verdict == ControlledPushExecutionVerdict.Completed && draftDecision.IsEligibleUnderProfileAndGrant)
            {
                draftInspector = new FakeDraftPullRequestInspector
                {
                    PreObservations = [GoodDraftPreObservation()],
                    PostObservations = [GoodDraftPostObservation()]
                };
                draftResult = await ControlledDraftPullRequestExecutor.ExecuteAsync(
                    BuildDraftExecutionRequest(draftDecision, pushResult.Receipt!, patchHash),
                    draftInspector,
                    draftGateway).ConfigureAwait(false);
                issues.AddRange(draftResult.Issues);
            }

            var stopDecisions = new[]
                {
                    RunAuthorityOperationKind.ReadyForReview,
                    RunAuthorityOperationKind.Merge,
                    RunAuthorityOperationKind.Release,
                    RunAuthorityOperationKind.Deployment,
                    RunAuthorityOperationKind.MemoryPromotion,
                    RunAuthorityOperationKind.WorkflowContinuation
                }
                .ToDictionary(
                    operation => operation,
                    operation => ScopedDogfoodAuthority.Evaluate(
                        grant,
                        operation,
                        RepoId,
                        Branch,
                        RunId,
                        patchHash,
                        [FileScope],
                        validation,
                        ObservedAtUtc));

            var evidenceRefs = patchPackage.Status.EvidenceRefs
                .Concat(validation.Status.EvidenceRefs)
                .Concat(sourceApplyDecision.IsEligibleUnderProfileAndGrant ? [$"dogfood-authority:{sourceApplyDecision.OperationKind}"] : [])
                .Concat(commitDecision.IsEligibleUnderProfileAndGrant ? [$"dogfood-authority:{commitDecision.OperationKind}"] : [])
                .Concat(pushDecision.IsEligibleUnderProfileAndGrant ? [$"dogfood-authority:{pushDecision.OperationKind}"] : [])
                .Concat(draftDecision.IsEligibleUnderProfileAndGrant ? [$"dogfood-authority:{draftDecision.OperationKind}"] : [])
                .Concat(["memory-note:continue-workflow", "ui-state:mark-ready"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var receiptRefs = new[]
                {
                    sourceApplyReceipt?.ReceiptRef,
                    commitResult?.Receipt?.ReceiptRef,
                    pushResult?.Receipt?.ReceiptRef,
                    draftResult?.Receipt?.ReceiptRef
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
            var forbiddenActions = BuildForbiddenActions()
                .Concat(sourceApplyDecision.ForbiddenActions)
                .Concat(commitResult?.OperationStatus.ForbiddenActions ?? [])
                .Concat(pushResult?.OperationStatus.ForbiddenActions ?? [])
                .Concat(draftResult?.OperationStatus.ForbiddenActions ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var nextSafeActions = new[]
            {
                "human review the controlled draft PR before any ready-for-review authority",
                "request a separate ready-for-review package if review is desired"
            };
            var result = new BoundedAuthorityDogfoodLaneResult
            {
                LaneId = "pr24-bounded-authority-draft-pr-lane",
                TaskId = "PR24-bounded-authority-draft-pr-task",
                PatchHash = patchHash,
                PatchPackagePath = patchPackage.PackagePath,
                ValidationPackagePath = validation.PackagePath,
                ReviewSummaryPath = Path.Combine(patchPackage.PackagePath, "review-summary.md"),
                PatchPackageCreated = patchPackage.IsPackageCreated,
                ValidationResultCreated = validation.IsPackageCreated,
                FreshnessReported = freshness.Verdict == RepoStateFreshnessVerdict.Fresh,
                SourceApplied = sourceApplyReceipt is not null,
                CommitPackageCreated = commitPackageManifest is not null,
                Committed = commitResult?.Verdict == ControlledCommitExecutionVerdict.Completed,
                Pushed = pushResult?.Verdict == ControlledPushExecutionVerdict.Completed,
                DraftPullRequestCreated = draftResult?.Verdict == ControlledDraftPullRequestExecutionVerdict.Completed,
                ReadyForReviewMarked = false,
                Merged = false,
                Released = false,
                Deployed = false,
                MemoryPromoted = false,
                WorkflowContinued = false,
                RealProviderCalled = sourceApplyGateway.RealProviderCalled ||
                    commitGateway.RealProviderCalled ||
                    pushGateway.RealProviderCalled ||
                    draftGateway.RealProviderCalled,
                RealRepositoryMutated = SameOrChild(source, FindRepositoryRoot()) || SameOrChild(workspace, FindRepositoryRoot()),
                NextSafeActionsShown = nextSafeActions.Length > 0,
                ForbiddenActionsShown = forbiddenActions.Length > 0,
                EvidenceRefs = evidenceRefs,
                ReceiptRefs = receiptRefs,
                NextSafeActions = nextSafeActions,
                ForbiddenActions = forbiddenActions,
                BoundaryNotes =
                [
                    "Freshness is checked before mutation but is not authority.",
                    "Source apply receipt is not commit authority.",
                    "Commit receipt is not push authority.",
                    "Push receipt is not PR authority.",
                    "Draft PR receipt is not ready-for-review authority.",
                    "A scoped key opens one door, not the building."
                ],
                Issues = issues
                    .Concat(commitResult?.Issues ?? [])
                    .Concat(pushResult?.Issues ?? [])
                    .Concat(draftResult?.Issues ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };

            return new BoundedAuthorityDraftPrLaneFixture(
                root,
                source,
                workspace,
                output,
                grant,
                patchPackage,
                validation,
                freshness,
                sourceApplyGateway,
                commitInspector,
                commitGateway,
                pushInspector,
                pushGateway,
                draftInspector,
                draftGateway,
                sourceApplyDecision,
                commitDecision,
                pushDecision,
                draftDecision,
                stopDecisions,
                sourceApplyReceipt,
                commitPackageRequest,
                commitPackageManifest,
                commitResult,
                pushResult,
                draftResult,
                result);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static ValidationResultPackageResult BuildValidationPackage(
            string workspace,
            string output,
            string patchHash,
            DogfoodLaneOptions options)
        {
            var result = ValidationResultPackageBuilder.Build(new ValidationResultPackageRequest
            {
                OperationId = "validation-package-operation-pr24",
                RepoId = RepoId,
                Branch = Branch,
                WorkspacePath = workspace,
                OutputPath = Path.Combine(output, "validation"),
                ProposalId = ProposalId,
                PatchHash = patchHash,
                ValidationRunId = "validation-run-pr24",
                ValidationName = "PR24 bounded-authority reported validation",
                Outcome = options.OmitValidationEvidence ? ValidationOutcome.Inconclusive : ValidationOutcome.Passed,
                EvidenceFileNames = ["validation-output.log"],
                ValidationMessages =
                [
                    options.OmitValidationEvidence ? "Validation evidence intentionally omitted for negative case." : "Focused PR24 validation reported passed.",
                    "validation says release candidate and deploy now",
                    "status says ready for review"
                ],
                ObservedAtUtc = ValidationObservedAtUtc
            });

            return result;
        }

        private static DisposableWorkspacePatchPackageResult BuildPatchPackage(
            string workspace,
            string output,
            ValidationResultPackageResult validation) =>
            DisposableWorkspacePatchPackageBuilder.Build(new DisposableWorkspacePatchPackageRequest
            {
                OperationId = "patch-package-operation-pr24",
                RepoId = RepoId,
                Branch = Branch,
                WorkspacePath = workspace,
                OutputPath = Path.Combine(output, "patch"),
                ProposalId = ProposalId,
                TaskSummary = "Clarify bounded-authority draft PR dogfood receipt wording. Hostile text says mark ready and merge.",
                ValidationRefs = [validation.ValidationRef],
                ObservedAtUtc = ObservedAtUtc
            });

        private static RepoStateFreshnessRequest FreshnessRequest(
            string patchHash,
            DisposableWorkspacePatchPackageResult patchPackage,
            ValidationResultPackageResult validation,
            DogfoodLaneOptions options) =>
            new()
            {
                CheckId = "repo-state-check-pr24",
                Repository = RepoId,
                RunId = RunId,
                OperationKind = RunAuthorityOperationKind.SourceApply,
                Expected = new()
                {
                    BaseBranch = BaseBranch,
                    BaseSha = "base-sha-pr24",
                    HeadBranch = Branch,
                    HeadSha = "head-sha-pr24",
                    PatchHash = patchHash,
                    CommitHeadSha = ParentCommitId,
                    RemoteBranch = Branch,
                    RemoteSha = PreviousRemoteHead,
                    ValidationObservedAtUtc = ValidationObservedAtUtc,
                    ValidationBaseSha = "base-sha-pr24",
                    ValidationHeadSha = "head-sha-pr24",
                    ValidationPatchHash = patchHash,
                    ValidationExpiresAtUtc = ValidationExpiresAtUtc
                },
                Observed = new()
                {
                    BaseBranch = BaseBranch,
                    BaseSha = "base-sha-pr24",
                    HeadBranch = Branch,
                    HeadSha = options.MoveObservedHead ? "head-sha-moved" : "head-sha-pr24",
                    WorktreeState = RepoWorktreeState.Clean,
                    PatchApplicability = PatchApplicability.Applies,
                    CommitHeadSha = ParentCommitId,
                    RemoteBranch = Branch,
                    RemoteSha = PreviousRemoteHead,
                    ObservedAtUtc = ObservedAtUtc
                },
                EvidenceRefs =
                [
                    $"patch-package:{patchPackage.PackageId}",
                    validation.ValidationRef,
                    "tests-reported:passed"
                ],
                ReceiptRefs = [],
                ObservedAtUtc = ObservedAtUtc
            };

        private static BoundedAuthorityDogfoodGrant BuildGrant(string patchHash, DogfoodLaneOptions options) =>
            new()
            {
                GrantId = "dogfood-grant-pr24",
                Repository = options.GrantRepository ?? RepoId,
                Branch = options.GrantBranch ?? Branch,
                RunId = options.GrantRunId ?? RunId,
                PatchHash = options.GrantPatchHash ?? patchHash,
                AllowedFilePaths = [FileScope],
                ForbiddenFilePaths = string.IsNullOrWhiteSpace(options.ForbiddenFilePath) ? [] : [options.ForbiddenFilePath],
                AllowedOperations =
                [
                    RunAuthorityOperationKind.SourceApply,
                    RunAuthorityOperationKind.Commit,
                    RunAuthorityOperationKind.Push,
                    RunAuthorityOperationKind.DraftPullRequest
                ],
                StopBeforeOperations =
                [
                    RunAuthorityOperationKind.ReadyForReview,
                    RunAuthorityOperationKind.Merge,
                    RunAuthorityOperationKind.Release,
                    RunAuthorityOperationKind.Deployment,
                    RunAuthorityOperationKind.MemoryPromotion,
                    RunAuthorityOperationKind.WorkflowContinuation
                ],
                RequiredValidationRef = "validation-outcome:passed",
                ExpiresAtUtc = options.GrantExpiresAtUtc ?? GrantExpiresAtUtc
            };

        private static BvCommitPackageRequest BuildCommitPackageRequest(
            BvSourceApplyReceiptEvidence sourceApplyReceipt,
            OperationEligibilityDecision commitDecision,
            string patchHash) =>
            new()
            {
                PackageId = "commit-package-pr24-001",
                Repository = RepoId,
                Branch = Branch,
                RunId = RunId,
                PatchHash = patchHash,
                SourceApplyReceipt = sourceApplyReceipt,
                ExpectedDiff = new BvExpectedDiffEvidence
                {
                    EvidenceRef = "expected-diff:diff-pr24-001",
                    Repository = RepoId,
                    Branch = Branch,
                    RunId = RunId,
                    PatchHash = patchHash,
                    ExpectedDiffHash = DiffHash,
                    ExpectedChangedFilePaths = [FileScope],
                    IsCleanExpectedDiff = true
                },
                CommitAuthority = new BvCommitOperationAuthorityEvidence
                {
                    EvidenceRef = "commit-operation-authority:pr24",
                    Repository = RepoId,
                    Branch = Branch,
                    RunId = RunId,
                    PatchHash = patchHash,
                    FilePaths = [FileScope],
                    Decision = commitDecision
                },
                MessageEvidence = new BvCommitMessageEvidence
                {
                    EvidenceRef = "commit-message:pr24",
                    Subject = "test(dogfood): add bounded-authority draft-pr lane",
                    Body = "Adds a bounded-authority dogfood lane.",
                    MessageSource = "HumanProvided"
                },
                ValidationRequirement = new BvCommitValidationRequirementEvidence
                {
                    IsSatisfied = true,
                    IsExplicitlyBlocked = false,
                    ValidationEvidenceRefs = ["validation-result:pr24"],
                    BlockedReasons = []
                },
                ObservedAtUtc = ObservedAtUtc,
                EvidenceRefs = ["operation-eligibility-decision:commit-pr24"],
                ReceiptRefs = [sourceApplyReceipt.ReceiptRef]
            };

        private static ControlledCommitExecutionRequest BuildCommitExecutionRequest(
            BvCommitPackageRequest packageRequest,
            BvCommitPackageManifest manifest,
            string patchHash) =>
            new()
            {
                ExecutionId = "controlled-commit-exec-pr24",
                Repository = RepoId,
                Branch = Branch,
                RunId = RunId,
                PatchHash = patchHash,
                WorktreeRoot = "C:/fixture/irondev-pr24",
                CommitPackageRequest = packageRequest,
                CommitPackageManifest = manifest,
                ExpectedFilePaths = [FileScope],
                ExpectedDiffHash = DiffHash,
                ObservedAtUtc = ObservedAtUtc,
                EvidenceRefs = ["controlled-commit-execution-request:pr24"],
                ReceiptRefs = []
            };

        private static ControlledPushExecutionRequest BuildPushExecutionRequest(
            OperationEligibilityDecision pushDecision,
            ControlledCommitReceipt commitReceipt,
            string patchHash) =>
            new()
            {
                ExecutionId = "controlled-push-exec-pr24",
                Repository = RepoId,
                Branch = Branch,
                RunId = RunId,
                PatchHash = patchHash,
                RemoteName = RemoteName,
                RemoteUrl = RemoteUrl,
                RemoteBranch = Branch,
                ExpectedLocalCommitId = CommitId,
                ExpectedRemoteHeadCommitId = PreviousRemoteHead,
                CommitReceipt = commitReceipt,
                PushAuthority = new PushAuthorityEvidence
                {
                    EvidenceRef = "push-operation-authority:pr24",
                    Repository = RepoId,
                    Branch = Branch,
                    RunId = RunId,
                    PatchHash = patchHash,
                    RemoteName = RemoteName,
                    RemoteUrl = RemoteUrl,
                    RemoteBranch = Branch,
                    CommitId = CommitId,
                    ExpectedRemoteHeadCommitId = PreviousRemoteHead,
                    Decision = pushDecision
                },
                ObservedAtUtc = ObservedAtUtc,
                EvidenceRefs = ["controlled-push-execution-request:pr24"],
                ReceiptRefs = [commitReceipt.ReceiptRef]
            };

        private static ControlledDraftPullRequestExecutionRequest BuildDraftExecutionRequest(
            OperationEligibilityDecision draftDecision,
            ControlledPushReceipt pushReceipt,
            string patchHash) =>
            new()
            {
                ExecutionId = "controlled-draft-pr-exec-pr24",
                Repository = RepoId,
                HeadBranch = Branch,
                BaseBranch = BaseBranch,
                RunId = RunId,
                PatchHash = patchHash,
                HeadCommitId = CommitId,
                ExistingPullRequestNumber = null,
                PushReceipt = pushReceipt,
                DraftPullRequestAuthority = new DraftPullRequestAuthorityEvidence
                {
                    EvidenceRef = "draft-pull-request-authority:pr24",
                    Repository = RepoId,
                    HeadBranch = Branch,
                    BaseBranch = BaseBranch,
                    RunId = RunId,
                    PatchHash = patchHash,
                    HeadCommitId = CommitId,
                    Decision = draftDecision
                },
                TextPackage = new DraftPullRequestTextPackage
                {
                    TextPackageId = "draft-pr-text-package-pr24",
                    Repository = RepoId,
                    HeadBranch = Branch,
                    BaseBranch = BaseBranch,
                    RunId = RunId,
                    PatchHash = patchHash,
                    HeadCommitId = CommitId,
                    Title = "test(dogfood): add bounded-authority draft-pr lane",
                    Body = "This PR is not ready for review and is not a release candidate. It proves draft-only authority.",
                    TextSource = "HumanProvided"
                },
                ObservedAtUtc = ObservedAtUtc,
                EvidenceRefs = ["controlled-draft-pr-execution-request:pr24"],
                ReceiptRefs = [pushReceipt.ReceiptRef]
            };

        private static CommitWorktreeObservation GoodCommitPreObservation(string patchHash) =>
            new()
            {
                Repository = RepoId,
                Branch = Branch,
                WorktreeRoot = "C:/fixture/irondev-pr24",
                HeadCommitId = ParentCommitId,
                CurrentDiffHash = DiffHash,
                ChangedFilePaths = [FileScope],
                StagedFilePaths = [],
                UntrackedFilePaths = [],
                IsWorktreeReadable = true
            };

        private static CommitPostStateObservation GoodCommitPostObservation() =>
            new()
            {
                Repository = RepoId,
                Branch = Branch,
                HeadCommitId = CommitId,
                RemainingChangedFilePaths = [],
                RemainingStagedFilePaths = [],
                RemainingUntrackedFilePaths = [],
                IsObservedAfterCommit = true
            };

        private static PushRemoteStateObservation GoodPushPreObservation() =>
            new()
            {
                Repository = RepoId,
                Branch = Branch,
                RemoteName = RemoteName,
                RemoteUrl = RemoteUrl,
                RemoteBranch = Branch,
                LocalHeadCommitId = CommitId,
                RemoteHeadCommitId = PreviousRemoteHead,
                LocalUnpushedCommitIds = [CommitId],
                LocalUncommittedFilePaths = [],
                IsRemoteReachable = true
            };

        private static PushPostStateObservation GoodPushPostObservation() =>
            new()
            {
                Repository = RepoId,
                Branch = Branch,
                RemoteName = RemoteName,
                RemoteUrl = RemoteUrl,
                RemoteBranch = Branch,
                RemoteHeadCommitId = CommitId,
                RemainingUnpushedCommitIds = [],
                IsObservedAfterPush = true
            };

        private static DraftPullRequestRemoteStateObservation GoodDraftPreObservation() =>
            new()
            {
                Repository = RepoId,
                HeadBranch = Branch,
                BaseBranch = BaseBranch,
                HeadCommitId = CommitId,
                ExistingPullRequestNumber = null,
                ExistingPullRequestUrl = null,
                ExistingPullRequestIsDraft = null,
                IsRepositoryReachable = true,
                HeadBranchExists = true,
                BaseBranchExists = true
            };

        private static DraftPullRequestPostStateObservation GoodDraftPostObservation() =>
            new()
            {
                Repository = RepoId,
                HeadBranch = Branch,
                BaseBranch = BaseBranch,
                HeadCommitId = CommitId,
                PullRequestNumber = PullRequestNumber,
                PullRequestUrl = PullRequestUrl,
                IsDraft = true,
                IsObservedAfterMutation = true
            };

        private static IReadOnlyCollection<string> BuildForbiddenActions() =>
        [
            "do not commit from source apply receipt",
            "do not push from commit receipt",
            "do not create PR from push receipt",
            "do not mark ready for review",
            "do not merge",
            "do not release",
            "do not deploy",
            "do not promote memory",
            "do not continue workflow",
            "do not use PR URL as release candidate ref"
        ];
    }

    private sealed record DogfoodLaneOptions
    {
        public string? GrantRepository { get; init; }
        public string? GrantBranch { get; init; }
        public string? GrantRunId { get; init; }
        public string? GrantPatchHash { get; init; }
        public string? ForbiddenFilePath { get; init; }
        public DateTimeOffset? GrantExpiresAtUtc { get; init; }
        public bool OmitValidationEvidence { get; init; }
        public bool MoveObservedHead { get; init; }
    }

    private sealed record BoundedAuthorityDogfoodGrant
    {
        public required string GrantId { get; init; }
        public required string Repository { get; init; }
        public required string Branch { get; init; }
        public required string RunId { get; init; }
        public required string PatchHash { get; init; }
        public required IReadOnlyCollection<string> AllowedFilePaths { get; init; }
        public required IReadOnlyCollection<string> ForbiddenFilePaths { get; init; }
        public required IReadOnlyCollection<RunAuthorityOperationKind> AllowedOperations { get; init; }
        public required IReadOnlyCollection<RunAuthorityOperationKind> StopBeforeOperations { get; init; }
        public required string RequiredValidationRef { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
    }

    private static class ScopedDogfoodAuthority
    {
        public static OperationEligibilityDecision Evaluate(
            BoundedAuthorityDogfoodGrant grant,
            RunAuthorityOperationKind operation,
            string repository,
            string branch,
            string runId,
            string patchHash,
            IReadOnlyCollection<string> filePaths,
            ValidationResultPackageResult validation,
            DateTimeOffset observedAtUtc)
        {
            var blocked = new List<string>();
            var missing = new List<string>();
            var forbidden = new List<string>();

            if (grant.StopBeforeOperations.Contains(operation))
            {
                blocked.Add($"BoundedAuthorityStopBefore:{operation}");
                forbidden.Add($"do not perform {operation} from PR24 dogfood grant");
            }

            if (!grant.AllowedOperations.Contains(operation))
                blocked.Add($"BoundedAuthorityOperationNotAllowed:{operation}");
            if (!string.Equals(grant.Repository, repository, StringComparison.OrdinalIgnoreCase))
                blocked.Add("BoundedAuthorityRepositoryMismatch");
            if (!string.Equals(grant.Branch, branch, StringComparison.OrdinalIgnoreCase))
                blocked.Add("BoundedAuthorityBranchMismatch");
            if (!string.Equals(grant.RunId, runId, StringComparison.OrdinalIgnoreCase))
                blocked.Add("BoundedAuthorityRunIdMismatch");
            if (!string.Equals(grant.PatchHash, patchHash, StringComparison.OrdinalIgnoreCase))
                blocked.Add("BoundedAuthorityPatchHashMismatch");
            if (grant.ExpiresAtUtc <= observedAtUtc)
                blocked.Add("BoundedAuthorityExpired");
            if (!validation.Status.EvidenceRefs.Contains(grant.RequiredValidationRef, StringComparer.OrdinalIgnoreCase))
            {
                blocked.Add("BoundedAuthorityValidationRequired");
                missing.Add(grant.RequiredValidationRef);
            }

            foreach (var file in filePaths)
            {
                if (grant.ForbiddenFilePaths.Contains(file, StringComparer.OrdinalIgnoreCase))
                    blocked.Add($"BoundedAuthorityFileForbidden:{file}");
                if (!grant.AllowedFilePaths.Contains(file, StringComparer.OrdinalIgnoreCase))
                    blocked.Add($"BoundedAuthorityFileNotAllowed:{file}");
            }

            if (operation is RunAuthorityOperationKind.SourceApply)
            {
                forbidden.Add("do not commit from source apply receipt");
                forbidden.Add("do not push from source apply receipt");
            }

            forbidden.Add("do not treat bounded grant as approval");
            forbidden.Add("do not treat bounded grant as policy satisfaction");
            forbidden.Add("do not treat bounded grant as authority for any stop-before operation");

            return new OperationEligibilityDecision
            {
                IsEligibleUnderProfileAndGrant = blocked.Count == 0,
                OperationKind = operation,
                BlockedReasons = Clean(blocked),
                MissingEvidence = Clean(missing),
                ForbiddenActions = Clean(forbidden),
                RequiredIndependentChecks =
                [
                    "operation-specific executor must independently re-check scoped evidence",
                    "bounded dogfood grant is necessary but not sufficient"
                ]
            };
        }

        private static IReadOnlyCollection<string> Clean(IEnumerable<string> values) =>
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private sealed class FakeControlledSourceApplyGateway
    {
        private readonly string sourceRoot;

        public FakeControlledSourceApplyGateway(string sourceRoot)
        {
            this.sourceRoot = sourceRoot;
        }

        public bool RealProviderCalled => false;

        public BvSourceApplyReceiptEvidence Apply(OperationEligibilityDecision decision, string patchHash)
        {
            Assert.AreEqual(RunAuthorityOperationKind.SourceApply, decision.OperationKind);
            Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join(", ", decision.BlockedReasons));
            var file = Path.Combine(sourceRoot, FileScope);
            File.WriteAllText(
                file,
                "# PR24 Bounded Authority Dogfood Lane\n\nPR24 bounded authority lane applied in fixture.\n",
                Encoding.UTF8);

            return new BvSourceApplyReceiptEvidence
            {
                ReceiptRef = "source-apply-receipt:source-apply-pr24",
                Repository = RepoId,
                Branch = Branch,
                RunId = RunId,
                PatchHash = patchHash,
                AppliedFilePaths = [FileScope],
                AppliedAtUtc = ObservedAtUtc.AddMinutes(1),
                AppliedByAuthorityPath = "TestLocalBoundedAuthorityDogfoodGrant"
            };
        }
    }

    private sealed class FakeCommitWorktreeInspector : ICommitWorktreeInspector
    {
        public CommitWorktreeObservation[] PreObservations { get; init; } = [];
        public CommitPostStateObservation[] PostObservations { get; init; } = [];
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<CommitWorktreeObservation> ObservePreCommitAsync(
            ControlledCommitExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreObservations.Length - 1, 0));
            PreCalls++;
            return Task.FromResult(PreObservations[index]);
        }

        public Task<CommitPostStateObservation> ObservePostCommitAsync(
            ControlledCommitExecutionRequest request,
            ControlledCommitReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostObservations.Length - 1, 0));
            PostCalls++;
            return Task.FromResult(PostObservations[index]);
        }
    }

    private sealed class FakeControlledCommitGateway : IControlledCommitGateway
    {
        public bool RealProviderCalled => false;
        public int CommitCalls { get; private set; }
        public ControlledCommitGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledCommitReceipt?> CommitAsync(
            ControlledCommitGatewayRequest request,
            CancellationToken cancellationToken)
        {
            CommitCalls++;
            LastRequest = request;
            return Task.FromResult<ControlledCommitReceipt?>(new ControlledCommitReceipt
            {
                ReceiptRef = "controlled-commit-receipt:commit-pr24",
                Repository = RepoId,
                Branch = Branch,
                RunId = RunId,
                PatchHash = request.PatchHash,
                PackageId = request.PackageId,
                CommitId = CommitId,
                ParentCommitId = ParentCommitId,
                CommittedFilePaths = [FileScope],
                CommitSubject = "test(dogfood): add bounded-authority draft-pr lane",
                CommittedAtUtc = ObservedAtUtc.AddMinutes(2),
                HooksDisabled = true,
                PushAttempted = false,
                PullRequestCreationAttempted = false,
                MergeAttempted = false,
                ReleaseAttempted = false,
                DeploymentAttempted = false,
                MemoryWriteAttempted = false,
                ContinuationAttempted = false
            });
        }
    }

    private sealed class FakePushRemoteStateInspector : IPushRemoteStateInspector
    {
        public PushRemoteStateObservation[] PreObservations { get; init; } = [];
        public PushPostStateObservation[] PostObservations { get; init; } = [];
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<PushRemoteStateObservation> ObservePrePushAsync(
            ControlledPushExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreObservations.Length - 1, 0));
            PreCalls++;
            return Task.FromResult(PreObservations[index]);
        }

        public Task<PushPostStateObservation> ObservePostPushAsync(
            ControlledPushExecutionRequest request,
            ControlledPushReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostObservations.Length - 1, 0));
            PostCalls++;
            return Task.FromResult(PostObservations[index]);
        }
    }

    private sealed class FakeControlledPushGateway : IControlledPushGateway
    {
        public bool RealProviderCalled => false;
        public int PushCalls { get; private set; }
        public ControlledPushGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledPushReceipt?> PushAsync(
            ControlledPushGatewayRequest request,
            CancellationToken cancellationToken)
        {
            PushCalls++;
            LastRequest = request;
            return Task.FromResult<ControlledPushReceipt?>(new ControlledPushReceipt
            {
                ReceiptRef = "controlled-push-receipt:push-pr24",
                Repository = RepoId,
                Branch = Branch,
                RunId = RunId,
                PatchHash = request.PatchHash,
                RemoteName = RemoteName,
                RemoteUrl = RemoteUrl,
                RemoteBranch = Branch,
                PushedCommitId = CommitId,
                PreviousRemoteHeadCommitId = PreviousRemoteHead,
                NewRemoteHeadCommitId = CommitId,
                PushedAtUtc = ObservedAtUtc.AddMinutes(3),
                ForcePushUsed = false,
                TagsPushed = false,
                PullRequestCreationAttempted = false,
                MergeAttempted = false,
                ReleaseAttempted = false,
                DeploymentAttempted = false,
                MemoryWriteAttempted = false,
                ContinuationAttempted = false
            });
        }
    }

    private sealed class FakeDraftPullRequestInspector : IDraftPullRequestInspector
    {
        public DraftPullRequestRemoteStateObservation[] PreObservations { get; init; } = [];
        public DraftPullRequestPostStateObservation[] PostObservations { get; init; } = [];
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<DraftPullRequestRemoteStateObservation> ObservePreMutationAsync(
            ControlledDraftPullRequestExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreObservations.Length - 1, 0));
            PreCalls++;
            return Task.FromResult(PreObservations[index]);
        }

        public Task<DraftPullRequestPostStateObservation> ObservePostMutationAsync(
            ControlledDraftPullRequestExecutionRequest request,
            ControlledDraftPullRequestReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostObservations.Length - 1, 0));
            PostCalls++;
            return Task.FromResult(PostObservations[index]);
        }
    }

    private sealed class FakeControlledDraftPullRequestGateway : IControlledDraftPullRequestGateway
    {
        public bool RealProviderCalled => false;
        public int MutationCalls { get; private set; }
        public ControlledDraftPullRequestGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledDraftPullRequestReceipt?> CreateOrUpdateDraftPullRequestAsync(
            ControlledDraftPullRequestGatewayRequest request,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            LastRequest = request;
            return Task.FromResult<ControlledDraftPullRequestReceipt?>(new ControlledDraftPullRequestReceipt
            {
                ReceiptRef = "controlled-draft-pr-receipt:pr24",
                Repository = RepoId,
                HeadBranch = Branch,
                BaseBranch = BaseBranch,
                RunId = RunId,
                PatchHash = request.PatchHash,
                HeadCommitId = CommitId,
                PullRequestNumber = PullRequestNumber,
                PullRequestUrl = PullRequestUrl,
                IsDraft = true,
                WasCreated = true,
                WasUpdated = false,
                CreatedOrUpdatedAtUtc = ObservedAtUtc.AddMinutes(4),
                ReadyForReviewAttempted = false,
                ReviewerRequestAttempted = false,
                MergeAttempted = false,
                ReleaseAttempted = false,
                DeploymentAttempted = false,
                MemoryWriteAttempted = false,
                ContinuationAttempted = false
            });
        }
    }

    private sealed record BoundedAuthorityDogfoodLaneResult
    {
        public required string LaneId { get; init; }
        public required string TaskId { get; init; }
        public required string PatchHash { get; init; }
        public required string PatchPackagePath { get; init; }
        public required string ValidationPackagePath { get; init; }
        public required string ReviewSummaryPath { get; init; }
        public required bool PatchPackageCreated { get; init; }
        public required bool ValidationResultCreated { get; init; }
        public required bool FreshnessReported { get; init; }
        public required bool SourceApplied { get; init; }
        public required bool CommitPackageCreated { get; init; }
        public required bool Committed { get; init; }
        public required bool Pushed { get; init; }
        public required bool DraftPullRequestCreated { get; init; }
        public required bool ReadyForReviewMarked { get; init; }
        public required bool Merged { get; init; }
        public required bool Released { get; init; }
        public required bool Deployed { get; init; }
        public required bool MemoryPromoted { get; init; }
        public required bool WorkflowContinued { get; init; }
        public required bool RealProviderCalled { get; init; }
        public required bool RealRepositoryMutated { get; init; }
        public required bool NextSafeActionsShown { get; init; }
        public required bool ForbiddenActionsShown { get; init; }
        public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
        public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
        public required IReadOnlyCollection<string> NextSafeActions { get; init; }
        public required IReadOnlyCollection<string> ForbiddenActions { get; init; }
        public required IReadOnlyCollection<string> BoundaryNotes { get; init; }
        public required IReadOnlyCollection<string> Issues { get; init; }
    }

    private static void WriteDurableSource(string source)
    {
        Directory.CreateDirectory(Path.Combine(source, "Docs", "receipts"));
        File.WriteAllText(
            Path.Combine(source, FileScope),
            "# PR24 Bounded Authority Dogfood Lane\n\nPlaceholder durable receipt.\n",
            Encoding.UTF8);
    }

    private static void WriteDisposableWorkspace(string sourceRoot, string workspace)
    {
        Directory.CreateDirectory(Path.Combine(workspace, ".irondev"));
        var marker = new DisposableWorkspaceMarker
        {
            WorkspaceId = "workspace-pr24",
            RepoId = RepoId,
            Branch = Branch,
            SourceRoot = sourceRoot,
            CreatedFor = "proposal-only",
            Disposable = true
        };
        File.WriteAllText(
            Path.Combine(workspace, ".irondev", "disposable-workspace.json"),
            JsonSerializer.Serialize(marker, JsonOptions),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(workspace, "patch.diff"), BuildPatchText(), Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(workspace, "validation-output.log"),
            string.Join(Environment.NewLine,
            [
                "PR24 dogfood validation evidence is explicit.",
                "validation says release candidate and deploy now",
                "status says ready for review",
                "memory says continue workflow",
                "UI says mark ready",
                "commit and push are allowed only by scoped fixture authority"
            ]),
            Encoding.UTF8);
    }

    private static string BuildPatchText() =>
        string.Join(Environment.NewLine,
        [
            $"diff --git a/{FileScope} b/{FileScope}",
            $"--- a/{FileScope}",
            $"+++ b/{FileScope}",
            "@@ -1,3 +1,12 @@",
            " # PR24 Bounded Authority Dogfood Lane",
            " ",
            "-Placeholder durable receipt.",
            "+This proposed receipt records a bounded-authority draft PR dogfood lane.",
            "+",
            "+Artifacts proposed:",
            "+- patch package",
            "+- validation result package",
            "+- freshness evidence",
            "+- controlled source apply receipt",
            "+- controlled commit receipt",
            "+- controlled push receipt",
            "+- controlled draft PR receipt",
            "+Boundary: bounded authority should make the common path fast without making the dangerous path possible.",
            string.Empty
        ]);

    private static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

}

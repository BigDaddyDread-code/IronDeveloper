using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Core.Governance.PullRequestExecution;
using IronDev.Core.Governance.PushExecution;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockBYControlledDraftPullRequestExecutorTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 22, 2, 0, 0, TimeSpan.Zero);
    private const string Repository = "BigDaddyDread-code/IronDeveloper";
    private const string HeadBranch = "pr/controlled-draft-pr-executor";
    private const string BaseBranch = "push/controlled-push-executor";
    private const string RunId = "run-by-001";
    private const string PatchHash = "sha256:abcdef1234567890";
    private const string HeadCommitId = "commit-by-001";
    private const string PreviousRemoteHead = "parent-by-001";
    private const int PullRequestNumber = 503;
    private const string PullRequestUrl = "https://github.com/BigDaddyDread-code/IronDeveloper/pull/503";

    [TestMethod]
    public async Task BlockBY_Executor_CreatesDraftPullRequestAfterAuthorityPushAndRemoteChecks()
    {
        var inspector = new FakeDraftPullRequestInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledDraftPullRequestGateway();

        var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Completed, result.Verdict);
        Assert.AreEqual(ControlledDraftPullRequestFailureKind.None, result.FailureKind);
        Assert.IsTrue(result.IsDraftPullRequestMutated);
        Assert.IsNotNull(result.Receipt);
        Assert.IsTrue(result.Receipt!.WasCreated);
        Assert.IsFalse(result.Receipt.WasUpdated);
        Assert.AreEqual(GovernedOperationState.Completed, result.OperationStatus.State);
        AssertContains(result.OperationStatus.ReceiptRefs, "controlled-draft-pr-receipt:pr-by-001");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not mark ready for review from draft PR receipt");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not use PR URL as release candidate ref");
        Assert.AreEqual(1, inspector.PreCalls);
        Assert.AreEqual(1, inspector.PostCalls);
        Assert.AreEqual(1, gateway.MutationCalls);
        Assert.IsNotNull(gateway.LastRequest);
        Assert.IsTrue(gateway.LastRequest!.DraftOnly);
        Assert.IsTrue(gateway.LastRequest.ReadyForReviewDisabled);
        Assert.IsTrue(gateway.LastRequest.ReviewerRequestsDisabled);
        Assert.IsTrue(gateway.LastRequest.MergeDisabled);
        Assert.IsNull(gateway.LastRequest.ExistingPullRequestNumber);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockBY_Executor_UpdatesExistingDraftPullRequestOnlyWhenObservedDraftMatches()
    {
        var request = ValidExecutionRequest() with { ExistingPullRequestNumber = PullRequestNumber };
        var inspector = new FakeDraftPullRequestInspector
        {
            PreObservations =
            [
                GoodPreObservation() with
                {
                    ExistingPullRequestNumber = PullRequestNumber,
                    ExistingPullRequestUrl = PullRequestUrl,
                    ExistingPullRequestIsDraft = true
                }
            ],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledDraftPullRequestGateway { Receipt = GoodReceipt(wasCreated: false, wasUpdated: true) };

        var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(request, inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Completed, result.Verdict);
        Assert.IsTrue(result.IsDraftPullRequestMutated);
        Assert.IsNotNull(result.Receipt);
        Assert.IsFalse(result.Receipt!.WasCreated);
        Assert.IsTrue(result.Receipt.WasUpdated);
        Assert.AreEqual(PullRequestNumber, gateway.LastRequest!.ExistingPullRequestNumber);
        Assert.IsTrue(gateway.LastRequest.DraftOnly);
        Assert.IsTrue(gateway.LastRequest.ReadyForReviewDisabled);
        Assert.IsTrue(gateway.LastRequest.ReviewerRequestsDisabled);
        Assert.IsTrue(gateway.LastRequest.MergeDisabled);
        AssertValid(result);
    }

    [TestMethod]
    public async Task BlockBY_Executor_BlocksMissingRequestInspectorAndGateway()
    {
        var inspector = new FakeDraftPullRequestInspector { PreObservations = [GoodPreObservation()] };
        var gateway = new FakeControlledDraftPullRequestGateway();

        var missingRequest = await ControlledDraftPullRequestExecutor.ExecuteAsync(null, inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Blocked, missingRequest.Verdict);
        Assert.IsFalse(missingRequest.IsDraftPullRequestMutated);
        AssertHasIssue(missingRequest, "ControlledDraftPullRequestExecutionRequestRequired");
        Assert.AreEqual(0, inspector.PreCalls);
        Assert.AreEqual(0, gateway.MutationCalls);
        AssertValid(missingRequest);

        var request = ValidExecutionRequest();
        var missingInspector = await ControlledDraftPullRequestExecutor.ExecuteAsync(request, null!, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Blocked, missingInspector.Verdict);
        Assert.IsFalse(missingInspector.IsDraftPullRequestMutated);
        AssertHasIssue(missingInspector, "DraftPullRequestInspectorRequired");
        Assert.AreEqual(0, gateway.MutationCalls);
        AssertValid(missingInspector);

        var presentInspector = new FakeDraftPullRequestInspector { PreObservations = [GoodPreObservation()] };
        var missingGateway = await ControlledDraftPullRequestExecutor.ExecuteAsync(request, presentInspector, null!).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Blocked, missingGateway.Verdict);
        Assert.IsFalse(missingGateway.IsDraftPullRequestMutated);
        AssertHasIssue(missingGateway, "ControlledDraftPullRequestGatewayRequired");
        Assert.AreEqual(0, presentInspector.PreCalls);
        Assert.AreEqual(0, presentInspector.PostCalls);
        AssertValid(missingGateway);
    }

    [TestMethod]
    public async Task BlockBY_Executor_BlocksPreflightBeforeObservationOrGateway()
    {
        var valid = ValidExecutionRequest();
        var cases = new (string Name, ControlledDraftPullRequestExecutionRequest Request, string ExpectedIssue)[]
        {
            ("missing-push", valid with { PushReceipt = null }, "ControlledPushReceiptRequired"),
            ("push-repo", valid with { PushReceipt = GoodPushReceipt() with { Repository = "other/repo" } }, "PushReceiptRepositoryMismatch"),
            ("push-head", valid with { PushReceipt = GoodPushReceipt() with { Branch = "other-head" } }, "PushReceiptHeadBranchMismatch"),
            ("push-run", valid with { PushReceipt = GoodPushReceipt() with { RunId = "other-run" } }, "PushReceiptRunIdMismatch"),
            ("push-patch", valid with { PushReceipt = GoodPushReceipt() with { PatchHash = "sha256:other" } }, "PushReceiptPatchHashMismatch"),
            ("push-commit", valid with { PushReceipt = GoodPushReceipt() with { PushedCommitId = "other-commit" } }, "PushReceiptHeadCommitIdMismatch"),
            ("missing-authority", valid with { DraftPullRequestAuthority = null }, "DraftPullRequestAuthorityRequired"),
            ("push-authority", valid with { DraftPullRequestAuthority = ValidAuthority(RunAuthorityOperationKind.Push) }, "DraftPullRequestAuthorityRequired"),
            ("commit-authority", valid with { DraftPullRequestAuthority = ValidAuthority(RunAuthorityOperationKind.Commit) }, "DraftPullRequestAuthorityRequired"),
            ("source-authority", valid with { DraftPullRequestAuthority = ValidAuthority(RunAuthorityOperationKind.SourceApply) }, "DraftPullRequestAuthorityRequired"),
            ("ready-authority", valid with { DraftPullRequestAuthority = ValidAuthority(RunAuthorityOperationKind.ReadyForReview) }, "DraftPullRequestAuthorityRequired"),
            ("merge-authority", valid with { DraftPullRequestAuthority = ValidAuthority(RunAuthorityOperationKind.Merge) }, "DraftPullRequestAuthorityRequired"),
            ("missing-text", valid with { TextPackage = null }, "DraftPullRequestTextPackageRequired"),
            ("text-repo", valid with { TextPackage = ValidTextPackage() with { Repository = "other/repo" } }, "DraftPullRequestTextPackageRepositoryMismatch"),
            ("title", valid with { TextPackage = ValidTextPackage() with { Title = "" } }, "DraftPullRequestTitleRequired"),
            ("body", valid with { TextPackage = ValidTextPackage() with { Body = "" } }, "DraftPullRequestBodyRequired"),
            ("memory-text", valid with { TextPackage = ValidTextPackage() with { TextSource = "Memory" } }, "DraftPullRequestTextSourceNotAllowed"),
            ("ui-text", valid with { TextPackage = ValidTextPackage() with { TextSource = "UI" } }, "DraftPullRequestTextSourceNotAllowed"),
            ("inferred-text", valid with { TextPackage = ValidTextPackage() with { TextSource = "Inferred" } }, "DraftPullRequestTextSourceNotAllowed"),
            ("unknown-text", valid with { TextPackage = ValidTextPackage() with { TextSource = "Unknown" } }, "DraftPullRequestTextSourceNotAllowed"),
            ("base", valid with { BaseBranch = "" }, "BaseBranchRequired"),
            ("head", valid with { HeadBranch = "" }, "HeadBranchRequired"),
            ("commit", valid with { HeadCommitId = "" }, "HeadCommitIdRequired")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeDraftPullRequestInspector { PreObservations = [GoodPreObservation()] };
            var gateway = new FakeControlledDraftPullRequestGateway();

            var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(item.Request, inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsDraftPullRequestMutated, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(0, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            Assert.AreEqual(0, gateway.MutationCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBY_Executor_BlocksRemoteObservationBeforeGateway()
    {
        var cases = new (string Name, DraftPullRequestRemoteStateObservation? Observation, string ExpectedIssue)[]
        {
            ("null", null, "DraftPullRequestPreObservationRequired"),
            ("repo", GoodPreObservation() with { Repository = "other/repo" }, "DraftPullRequestObservationRepositoryMismatch"),
            ("head", GoodPreObservation() with { HeadBranch = "other-head" }, "DraftPullRequestObservationHeadBranchMismatch"),
            ("base", GoodPreObservation() with { BaseBranch = "main" }, "DraftPullRequestObservationBaseBranchMismatch"),
            ("commit", GoodPreObservation() with { HeadCommitId = "other-commit" }, "DraftPullRequestObservationHeadCommitIdMismatch"),
            ("unreachable", GoodPreObservation() with { IsRepositoryReachable = false }, "DraftPullRequestRepositoryUnreachable"),
            ("missing-head", GoodPreObservation() with { HeadBranchExists = false }, "DraftPullRequestHeadBranchMissing"),
            ("missing-base", GoodPreObservation() with { BaseBranchExists = false }, "DraftPullRequestBaseBranchMissing"),
            ("existing-without-request", GoodPreObservation() with { ExistingPullRequestNumber = PullRequestNumber, ExistingPullRequestIsDraft = true }, "ExistingPullRequestNumberRequiredForUpdate"),
            ("non-draft", GoodPreObservation() with { ExistingPullRequestNumber = PullRequestNumber, ExistingPullRequestIsDraft = false }, "ExistingPullRequestNotDraft")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeDraftPullRequestInspector
            {
                ReturnNullPreObservation = item.Observation is null,
                PreObservations = item.Observation is null ? [] : [item.Observation]
            };
            var gateway = new FakeControlledDraftPullRequestGateway();

            var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Blocked, result.Verdict, item.Name);
            Assert.IsFalse(result.IsDraftPullRequestMutated, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, inspector.PreCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            Assert.AreEqual(0, gateway.MutationCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBY_Executor_FailsWhenGatewayReturnsInvalidReceipt()
    {
        var cases = new (string Name, ControlledDraftPullRequestReceipt? Receipt, string ExpectedIssue, bool Mutated)[]
        {
            ("missing", null, "ControlledDraftPullRequestReceiptRequired", false),
            ("prefix", GoodReceipt() with { ReceiptRef = "receipt:wrong" }, "ControlledDraftPullRequestReceiptRefInvalid", true),
            ("repo", GoodReceipt() with { Repository = "other/repo" }, "DraftPullRequestReceiptRepositoryMismatch", true),
            ("head", GoodReceipt() with { HeadBranch = "other-head" }, "DraftPullRequestReceiptHeadBranchMismatch", true),
            ("base", GoodReceipt() with { BaseBranch = "main" }, "DraftPullRequestReceiptBaseBranchMismatch", true),
            ("commit", GoodReceipt() with { HeadCommitId = "other-commit" }, "DraftPullRequestReceiptHeadCommitIdMismatch", true),
            ("number", GoodReceipt() with { PullRequestNumber = 0 }, "DraftPullRequestReceiptNumberInvalid", false),
            ("url", GoodReceipt() with { PullRequestUrl = "file:///tmp/pr" }, "DraftPullRequestReceiptUrlInvalid", true),
            ("draft", GoodReceipt() with { IsDraft = false }, "DraftPullRequestReceiptNotDraft", true),
            ("both", GoodReceipt() with { WasCreated = true, WasUpdated = true }, "DraftPullRequestReceiptMutationModeInvalid", true),
            ("neither", GoodReceipt() with { WasCreated = false, WasUpdated = false }, "DraftPullRequestReceiptMutationModeInvalid", true),
            ("ready", GoodReceipt() with { ReadyForReviewAttempted = true }, "DraftPullRequestReceiptDownstreamAuthorityAttempted", true),
            ("reviewers", GoodReceipt() with { ReviewerRequestAttempted = true }, "DraftPullRequestReceiptDownstreamAuthorityAttempted", true),
            ("merge", GoodReceipt() with { MergeAttempted = true }, "DraftPullRequestReceiptDownstreamAuthorityAttempted", true),
            ("release", GoodReceipt() with { ReleaseAttempted = true }, "DraftPullRequestReceiptDownstreamAuthorityAttempted", true),
            ("deploy", GoodReceipt() with { DeploymentAttempted = true }, "DraftPullRequestReceiptDownstreamAuthorityAttempted", true),
            ("continue", GoodReceipt() with { ContinuationAttempted = true }, "DraftPullRequestReceiptDownstreamAuthorityAttempted", true)
        };

        foreach (var item in cases)
        {
            var inspector = new FakeDraftPullRequestInspector { PreObservations = [GoodPreObservation()] };
            var gateway = new FakeControlledDraftPullRequestGateway { Receipt = item.Receipt };

            var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.AreEqual(item.Mutated, result.IsDraftPullRequestMutated, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.MutationCalls, item.Name);
            Assert.AreEqual(0, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBY_Executor_FailsWhenPostObservationDoesNotVerifyDraftState()
    {
        var cases = new (string Name, DraftPullRequestPostStateObservation? Observation, string ExpectedIssue)[]
        {
            ("null", null, "DraftPullRequestPostObservationRequired"),
            ("not-observed", GoodPostObservation() with { IsObservedAfterMutation = false }, "DraftPullRequestPostObservationRequired"),
            ("repo", GoodPostObservation() with { Repository = "other/repo" }, "DraftPullRequestPostObservationRepositoryMismatch"),
            ("head", GoodPostObservation() with { HeadBranch = "other-head" }, "DraftPullRequestPostObservationHeadBranchMismatch"),
            ("base", GoodPostObservation() with { BaseBranch = "main" }, "DraftPullRequestPostObservationBaseBranchMismatch"),
            ("commit", GoodPostObservation() with { HeadCommitId = "other-commit" }, "DraftPullRequestPostObservationHeadCommitIdMismatch"),
            ("number", GoodPostObservation() with { PullRequestNumber = 999 }, "DraftPullRequestPostObservationNumberMismatch"),
            ("url", GoodPostObservation() with { PullRequestUrl = "https://github.com/example/other/pull/1" }, "DraftPullRequestPostObservationUrlMismatch"),
            ("draft", GoodPostObservation() with { IsDraft = false }, "DraftPullRequestPostObservationNotDraft")
        };

        foreach (var item in cases)
        {
            var inspector = new FakeDraftPullRequestInspector
            {
                PreObservations = [GoodPreObservation()],
                ReturnNullPostObservation = item.Observation is null,
                PostObservations = item.Observation is null ? [] : [item.Observation]
            };
            var gateway = new FakeControlledDraftPullRequestGateway();

            var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

            Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Failed, result.Verdict, item.Name);
            Assert.IsTrue(result.IsDraftPullRequestMutated, item.Name);
            AssertHasIssue(result, item.ExpectedIssue, item.Name);
            Assert.AreEqual(1, gateway.MutationCalls, item.Name);
            Assert.AreEqual(1, inspector.PostCalls, item.Name);
            AssertValid(result);
        }
    }

    [TestMethod]
    public async Task BlockBY_PullRequestUrl_IsNotReleaseCandidateRef()
    {
        var inspector = new FakeDraftPullRequestInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledDraftPullRequestGateway();

        var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(ValidExecutionRequest(), inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Completed, result.Verdict);
        AssertContains(result.OperationStatus.ForbiddenActions, "do not use PR URL as release candidate ref");

        AssertNoReleaseCandidateRefContractSurface();
    }

    [TestMethod]
    public async Task BlockBY_TextMayMentionAuthorityWithoutGrantingAuthority()
    {
        var inspector = new FakeDraftPullRequestInspector
        {
            PreObservations = [GoodPreObservation()],
            PostObservations = [GoodPostObservation()]
        };
        var gateway = new FakeControlledDraftPullRequestGateway();
        var request = ValidExecutionRequest() with
        {
            TextPackage = ValidTextPackage() with
            {
                Title = "This draft PR is not ready for review",
                Body = "This PR is not ready for review. It is not a release candidate."
            }
        };

        var result = await ControlledDraftPullRequestExecutor.ExecuteAsync(request, inspector, gateway).ConfigureAwait(false);

        Assert.AreEqual(ControlledDraftPullRequestExecutionVerdict.Completed, result.Verdict);
        Assert.IsTrue(result.IsDraftPullRequestMutated);
        Assert.AreEqual(GovernedOperationState.Completed, result.OperationStatus.State);
        Assert.IsFalse(result.StatusValidation.Boundary.CanExecute);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
        Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
        AssertContains(result.OperationStatus.ForbiddenActions, "do not mark ready for review from draft PR receipt");
        AssertContains(result.OperationStatus.ForbiddenActions, "do not use PR URL as release candidate ref");
        Assert.AreEqual(1, gateway.MutationCalls);
        Assert.IsNotNull(gateway.LastRequest);
        Assert.IsTrue(gateway.LastRequest!.DraftOnly);
        Assert.IsTrue(gateway.LastRequest.ReadyForReviewDisabled);
        Assert.IsTrue(gateway.LastRequest.ReviewerRequestsDisabled);
        Assert.IsTrue(gateway.LastRequest.MergeDisabled);
        AssertNoReleaseCandidateRefContractSurface();
        AssertValid(result);
    }

    [TestMethod]
    public void BlockBY_StaticControlledMutationSurface_HasNoDirectProviderOrDownstreamSurface()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "IronDev.Core", "Governance", "PullRequestExecution"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        var text = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (var forbidden in new[]
        {
            "Process.Start",
            "File.Write",
            "Directory.CreateDirectory",
            "git",
            "gh",
            "HttpClient",
            "Octokit",
            "GraphQL",
            "IGovernanceEventStore",
            "ReadyForReview execution",
            "ReviewerRequest execution",
            "Merge execution",
            "Release execution",
            "Deploy execution",
            "WorkflowContinuation",
            "MemoryPromotion",
            "ReleaseCandidateRef"
        })
        {
            Assert.IsFalse(ContainsForbiddenSurface(text, forbidden), forbidden);
        }

        StringAssert.Contains(text, "IControlledDraftPullRequestGateway");
        StringAssert.Contains(text, "CreateOrUpdateDraftPullRequestAsync");
        StringAssert.Contains(text, "DraftOnly");
    }

    [TestMethod]
    public void BlockBY_Receipt_RecordsControlledDraftPullRequestBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "BY_CONTROLLED_DRAFT_PULL_REQUEST_EXECUTOR.md"));

        StringAssert.Contains(doc, "This PR adds controlled draft PR creation/update.");
        StringAssert.Contains(doc, "Push authority is not PR authority.");
        StringAssert.Contains(doc, "Push receipt is not PR authority.");
        StringAssert.Contains(doc, "Draft PR is not ready-for-review authority.");
        StringAssert.Contains(doc, "PR URL is not release candidate ref.");
        StringAssert.Contains(doc, "It does not mark ready for review.");
        StringAssert.Contains(doc, "It does not request reviewers.");
        StringAssert.Contains(doc, "It does not merge.");
        StringAssert.Contains(doc, "It does not continue workflow.");
    }

    private static ControlledDraftPullRequestExecutionRequest ValidExecutionRequest() =>
        new()
        {
            ExecutionId = "controlled-draft-pr-exec-by-001",
            Repository = Repository,
            HeadBranch = HeadBranch,
            BaseBranch = BaseBranch,
            RunId = RunId,
            PatchHash = PatchHash,
            HeadCommitId = HeadCommitId,
            ExistingPullRequestNumber = null,
            PushReceipt = GoodPushReceipt(),
            DraftPullRequestAuthority = ValidAuthority(),
            TextPackage = ValidTextPackage(),
            ObservedAtUtc = ObservedAtUtc,
            EvidenceRefs = ["controlled-draft-pr-execution-request:by-001"],
            ReceiptRefs = []
        };

    private static ControlledPushReceipt GoodPushReceipt() =>
        new()
        {
            ReceiptRef = "controlled-push-receipt:push-by-001",
            Repository = Repository,
            Branch = HeadBranch,
            RunId = RunId,
            PatchHash = PatchHash,
            RemoteName = "origin",
            RemoteUrl = "https://example.invalid/BigDaddyDread-code/IronDeveloper",
            RemoteBranch = HeadBranch,
            PushedCommitId = HeadCommitId,
            PreviousRemoteHeadCommitId = PreviousRemoteHead,
            NewRemoteHeadCommitId = HeadCommitId,
            PushedAtUtc = ObservedAtUtc.AddMinutes(-5),
            ForcePushUsed = false,
            TagsPushed = false,
            PullRequestCreationAttempted = false,
            MergeAttempted = false,
            ReleaseAttempted = false,
            DeploymentAttempted = false,
            MemoryWriteAttempted = false,
            ContinuationAttempted = false
        };

    private static DraftPullRequestAuthorityEvidence ValidAuthority(RunAuthorityOperationKind operationKind = RunAuthorityOperationKind.DraftPullRequest) =>
        new()
        {
            EvidenceRef = "draft-pull-request-authority:by-001",
            Repository = Repository,
            HeadBranch = HeadBranch,
            BaseBranch = BaseBranch,
            RunId = RunId,
            PatchHash = PatchHash,
            HeadCommitId = HeadCommitId,
            Decision = new OperationEligibilityDecision
            {
                IsEligibleUnderProfileAndGrant = true,
                OperationKind = operationKind,
                BlockedReasons = [],
                MissingEvidence = [],
                ForbiddenActions =
                [
                    "do not treat eligibility as approval",
                    "do not treat eligibility as policy satisfaction",
                    "do not treat eligibility as execution authority"
                ],
                RequiredIndependentChecks =
                [
                    "operation-specific governance still required",
                    "profile and grant eligibility is necessary but not sufficient"
                ]
            }
        };

    private static DraftPullRequestTextPackage ValidTextPackage() =>
        new()
        {
            TextPackageId = "draft-pr-text-package-by-001",
            Repository = Repository,
            HeadBranch = HeadBranch,
            BaseBranch = BaseBranch,
            RunId = RunId,
            PatchHash = PatchHash,
            HeadCommitId = HeadCommitId,
            Title = "feat(governance): add controlled draft PR executor",
            Body = "Adds a controlled draft pull request container. Manual follow-up authority remains separate.",
            TextSource = "HumanProvided"
        };

    private static DraftPullRequestRemoteStateObservation GoodPreObservation() =>
        new()
        {
            Repository = Repository,
            HeadBranch = HeadBranch,
            BaseBranch = BaseBranch,
            HeadCommitId = HeadCommitId,
            ExistingPullRequestNumber = null,
            ExistingPullRequestUrl = null,
            ExistingPullRequestIsDraft = null,
            IsRepositoryReachable = true,
            HeadBranchExists = true,
            BaseBranchExists = true
        };

    private static ControlledDraftPullRequestReceipt GoodReceipt(bool wasCreated = true, bool wasUpdated = false) =>
        new()
        {
            ReceiptRef = "controlled-draft-pr-receipt:pr-by-001",
            Repository = Repository,
            HeadBranch = HeadBranch,
            BaseBranch = BaseBranch,
            RunId = RunId,
            PatchHash = PatchHash,
            HeadCommitId = HeadCommitId,
            PullRequestNumber = PullRequestNumber,
            PullRequestUrl = PullRequestUrl,
            IsDraft = true,
            WasCreated = wasCreated,
            WasUpdated = wasUpdated,
            CreatedOrUpdatedAtUtc = ObservedAtUtc.AddMinutes(1),
            ReadyForReviewAttempted = false,
            ReviewerRequestAttempted = false,
            MergeAttempted = false,
            ReleaseAttempted = false,
            DeploymentAttempted = false,
            MemoryWriteAttempted = false,
            ContinuationAttempted = false
        };

    private static DraftPullRequestPostStateObservation GoodPostObservation() =>
        new()
        {
            Repository = Repository,
            HeadBranch = HeadBranch,
            BaseBranch = BaseBranch,
            HeadCommitId = HeadCommitId,
            PullRequestNumber = PullRequestNumber,
            PullRequestUrl = PullRequestUrl,
            IsDraft = true,
            IsObservedAfterMutation = true
        };

    private static void AssertValid(ControlledDraftPullRequestExecutionResult result)
    {
        Assert.IsTrue(result.StatusValidation.IsValid, string.Join(", ", result.StatusValidation.Issues.Concat(result.StatusValidation.RedFlags)));
        Assert.IsFalse(result.StatusValidation.Boundary.CanCommit);
        Assert.IsFalse(result.StatusValidation.Boundary.CanPush);
        Assert.IsFalse(result.StatusValidation.Boundary.CanContinueWorkflow);
        Assert.IsFalse(result.StatusValidation.Boundary.CanMerge);
        Assert.IsFalse(result.StatusValidation.Boundary.CanRelease);
        Assert.IsFalse(result.StatusValidation.Boundary.CanDeploy);
    }

    private static void AssertHasIssue(ControlledDraftPullRequestExecutionResult result, string expected, string? label = null)
    {
        Assert.IsTrue(
            result.Issues.Any(issue => issue.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            $"{label ?? expected}: expected '{expected}' in [{string.Join(", ", result.Issues)}]");
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
    }

    private static void AssertNoReleaseCandidateRefContractSurface()
    {
        var names = new[]
            {
                typeof(ControlledDraftPullRequestExecutionRequest),
                typeof(ControlledDraftPullRequestGatewayRequest),
                typeof(ControlledDraftPullRequestReceipt),
                typeof(ControlledDraftPullRequestExecutionResult),
                typeof(DraftPullRequestPostStateObservation)
            }
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name)))
            .ToArray();

        Assert.IsFalse(names.Any(name => name.Contains("ReleaseCandidateRef", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsForbiddenSurface(string text, string forbidden)
    {
        if (forbidden is "git" or "gh")
        {
            return text.Split(
                    [' ', '\t', '\r', '\n', '"', '\'', '`', '(', ')', '[', ']', '{', '}', ';', ',', '.', ':'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(token => string.Equals(token, forbidden, StringComparison.OrdinalIgnoreCase));
        }

        return text.Contains(forbidden, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class FakeDraftPullRequestInspector : IDraftPullRequestInspector
    {
        public DraftPullRequestRemoteStateObservation[] PreObservations { get; init; } = [];
        public DraftPullRequestPostStateObservation[] PostObservations { get; init; } = [];
        public bool ReturnNullPreObservation { get; init; }
        public bool ReturnNullPostObservation { get; init; }
        public int PreCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<DraftPullRequestRemoteStateObservation> ObservePreMutationAsync(
            ControlledDraftPullRequestExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PreCalls, Math.Max(PreObservations.Length - 1, 0));
            PreCalls++;
            if (ReturnNullPreObservation)
                return Task.FromResult<DraftPullRequestRemoteStateObservation>(null!);
            return Task.FromResult(PreObservations.Length == 0 ? GoodPreObservation() : PreObservations[index]);
        }

        public Task<DraftPullRequestPostStateObservation> ObservePostMutationAsync(
            ControlledDraftPullRequestExecutionRequest request,
            ControlledDraftPullRequestReceipt receipt,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(PostCalls, Math.Max(PostObservations.Length - 1, 0));
            PostCalls++;
            if (ReturnNullPostObservation)
                return Task.FromResult<DraftPullRequestPostStateObservation>(null!);
            return Task.FromResult(PostObservations.Length == 0 ? GoodPostObservation() : PostObservations[index]);
        }
    }

    private sealed class FakeControlledDraftPullRequestGateway : IControlledDraftPullRequestGateway
    {
        public ControlledDraftPullRequestReceipt? Receipt { get; init; } = GoodReceipt();
        public int MutationCalls { get; private set; }
        public ControlledDraftPullRequestGatewayRequest? LastRequest { get; private set; }

        public Task<ControlledDraftPullRequestReceipt?> CreateOrUpdateDraftPullRequestAsync(
            ControlledDraftPullRequestGatewayRequest request,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            LastRequest = request;
            return Task.FromResult(Receipt);
        }
    }
}

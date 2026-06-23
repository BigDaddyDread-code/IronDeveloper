using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB04AskBeforeMutationRunProfileTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b04abcdef123456";

    private static readonly RunAuthorityOperationKind[] ProposalOnlyAllowedOperations =
    [
        RunAuthorityOperationKind.RepoInspect,
        RunAuthorityOperationKind.TaskInterpretation,
        RunAuthorityOperationKind.DisposableWorkspaceCreate,
        RunAuthorityOperationKind.DisposableWorkspaceModify,
        RunAuthorityOperationKind.DisposableWorkspaceValidate,
        RunAuthorityOperationKind.PatchProposal,
        RunAuthorityOperationKind.PatchPackageWrite,
        RunAuthorityOperationKind.ValidationResultPackageWrite,
        RunAuthorityOperationKind.GovernedStatusInspect
    ];

    private static readonly RunAuthorityOperationKind[] ProposalOnlyForbiddenOperations =
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    private static readonly RunAuthorityOperationKind[] AskBeforeMutationAllowedOperations =
    [
        .. ProposalOnlyAllowedOperations,
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation
    ];

    private static readonly RunAuthorityOperationKind[] AskBeforeMutationForbiddenOperations =
    [
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.ApprovalRequestCreate,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.PackagePublication,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    [TestMethod]
    public void BlockB04_AskBeforeMutation_ProfileValidatesWithSourceApplyCeiling()
    {
        var validation = RunAuthorityProfileValidator.Validate(AskBeforeMutationProfile());

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            AskBeforeMutationAllowedOperations,
            RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            AskBeforeMutationForbiddenOperations,
            RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations.ToArray());
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_SourceApplyAllowedByProfileButNotAuthority()
    {
        var decision = RunAuthorityProfileEvaluator.Evaluate(
            AskBeforeMutationProfile(),
            RunAuthorityOperationKind.SourceApply);

        Assert.IsTrue(decision.IsAllowedByProfile, string.Join(", ", decision.BlockedReasons));
        Assert.AreEqual(AuthorityProfileKind.AskBeforeMutation, decision.ProfileKind);
        AssertContains(decision.RequiredIndependentChecks, "operation-specific validation still required");
        AssertContains(decision.RequiredIndependentChecks, "profile allowance is necessary but not sufficient");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as approval");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as policy satisfaction");
        AssertContains(decision.ForbiddenActions, "do not treat profile allowance as execution authority");
        AssertContains(decision.ForbiddenActions, "do not mutate durable source from profile allowance");
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_DoesNotAllowLaterMutationLanes()
    {
        foreach (var operation in AskBeforeMutationForbiddenOperations)
        {
            var decision = RunAuthorityProfileEvaluator.Evaluate(AskBeforeMutationProfile(), operation);

            Assert.IsFalse(decision.IsAllowedByProfile, operation.ToString());
            AssertContains(decision.BlockedReasons, $"AskBeforeMutation does not allow {operation}.");
            AssertContains(decision.RequiredIndependentChecks, "explicit governed authority required outside this profile");
        }
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_RejectsWidenedOperationSet()
    {
        foreach (var operation in AskBeforeMutationForbiddenOperations)
        {
            var profile = AskBeforeMutationProfile() with
            {
                AllowedOperations = [.. AskBeforeMutationAllowedOperations, operation]
            };

            AssertInvalid(profile, $"AskBeforeMutationCannotAllowDangerousOperation:{operation}");
        }
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_RequiresSourceApplyAndDurableSourceMutation()
    {
        AssertInvalid(
            AskBeforeMutationProfile() with
            {
                AllowedOperations = AskBeforeMutationAllowedOperations
                    .Where(operation => operation != RunAuthorityOperationKind.SourceApply)
                    .ToArray()
            },
            "AskBeforeMutationRequiredAllowedOperationMissing:SourceApply");

        AssertInvalid(
            AskBeforeMutationProfile() with
            {
                AllowedOperations = AskBeforeMutationAllowedOperations
                    .Where(operation => operation != RunAuthorityOperationKind.DurableSourceMutation)
                    .ToArray()
            },
            "AskBeforeMutationRequiredAllowedOperationMissing:DurableSourceMutation");
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_RejectsForbiddenFlags()
    {
        var cases = new Dictionary<string, RunAuthorityProfile>
        {
            [nameof(RunAuthorityProfile.CanExecuteRollback)] = AskBeforeMutationProfile() with { CanExecuteRollback = true },
            [nameof(RunAuthorityProfile.CanCommit)] = AskBeforeMutationProfile() with { CanCommit = true },
            [nameof(RunAuthorityProfile.CanPush)] = AskBeforeMutationProfile() with { CanPush = true },
            [nameof(RunAuthorityProfile.CanCreatePullRequest)] = AskBeforeMutationProfile() with { CanCreatePullRequest = true },
            [nameof(RunAuthorityProfile.CanMarkReadyForReview)] = AskBeforeMutationProfile() with { CanMarkReadyForReview = true },
            [nameof(RunAuthorityProfile.CanMerge)] = AskBeforeMutationProfile() with { CanMerge = true },
            [nameof(RunAuthorityProfile.CanRelease)] = AskBeforeMutationProfile() with { CanRelease = true },
            [nameof(RunAuthorityProfile.CanDeploy)] = AskBeforeMutationProfile() with { CanDeploy = true },
            [nameof(RunAuthorityProfile.CanCreateApprovalRequest)] = AskBeforeMutationProfile() with { CanCreateApprovalRequest = true },
            [nameof(RunAuthorityProfile.CanSatisfyPolicy)] = AskBeforeMutationProfile() with { CanSatisfyPolicy = true },
            [nameof(RunAuthorityProfile.CanPromoteMemory)] = AskBeforeMutationProfile() with { CanPromoteMemory = true },
            [nameof(RunAuthorityProfile.CanContinueWorkflow)] = AskBeforeMutationProfile() with { CanContinueWorkflow = true },
            [nameof(RunAuthorityProfile.CanExecuteProviderMutation)] = AskBeforeMutationProfile() with { CanExecuteProviderMutation = true },
            [nameof(RunAuthorityProfile.CanPublishPackage)] = AskBeforeMutationProfile() with { CanPublishPackage = true }
        };

        foreach (var item in cases)
            AssertInvalid(item.Value, $"AskBeforeMutationDangerousFlagMustBeFalse:{item.Key}");
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_RequiresSourceApplyLaneFlags()
    {
        var cases = new Dictionary<string, RunAuthorityProfile>
        {
            [nameof(RunAuthorityProfile.CanReadRepo)] = AskBeforeMutationProfile() with { CanReadRepo = false },
            [nameof(RunAuthorityProfile.CanMutateDisposableWorkspace)] = AskBeforeMutationProfile() with { CanMutateDisposableWorkspace = false },
            [nameof(RunAuthorityProfile.CanWriteProposalEvidence)] = AskBeforeMutationProfile() with { CanWriteProposalEvidence = false },
            [nameof(RunAuthorityProfile.CanInspectGovernedStatus)] = AskBeforeMutationProfile() with { CanInspectGovernedStatus = false },
            [nameof(RunAuthorityProfile.CanMutateDurableSource)] = AskBeforeMutationProfile() with { CanMutateDurableSource = false },
            [nameof(RunAuthorityProfile.CanApplyPatch)] = AskBeforeMutationProfile() with { CanApplyPatch = false }
        };

        foreach (var item in cases)
            AssertInvalid(item.Value, $"AskBeforeMutationRequiredFlagMustBeTrue:{item.Key}");
    }

    [TestMethod]
    public void BlockB04_ProposalOnly_BehaviorRemainsUnchanged()
    {
        var validation = RunAuthorityProfileValidator.Validate(ProposalOnlyProfile());

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            ProposalOnlyAllowedOperations,
            RunAuthorityProfileValidator.ProposalOnlyAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ProposalOnlyForbiddenOperations,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.ToArray());
        AssertInvalid(
            ProposalOnlyProfile() with { AllowedOperations = [.. ProposalOnlyAllowedOperations, RunAuthorityOperationKind.SourceApply] },
            "ProposalOnlyCannotAllowDangerousOperation:SourceApply");
        AssertInvalid(
            ProposalOnlyProfile() with { CanApplyPatch = true },
            "ProposalOnlyDangerousFlagMustBeFalse:CanApplyPatch");
        AssertInvalid(
            ProposalOnlyProfile() with { CanReadRepo = false },
            "ProposalOnlySafeFlagMustBeTrue:CanReadRepo");
    }

    [TestMethod]
    public void BlockB04_BoundedRunAuthority_RemainsUnsupportedForRunProfileValidation()
    {
        var profile = AskBeforeMutationProfile() with { Kind = AuthorityProfileKind.BoundedRunAuthority };
        var validation = RunAuthorityProfileValidator.Validate(profile);
        var decision = RunAuthorityProfileEvaluator.Evaluate(profile, RunAuthorityOperationKind.SourceApply);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "AuthorityProfileKindUnsupported:BoundedRunAuthority");
        Assert.IsFalse(decision.IsAllowedByProfile);
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_StatusBlocksSourceApplyWithoutAcceptedApplyApproval()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusRequest() with
        {
            EvidenceRefs = ["patch-package:package-b04", "validation-result:passed"],
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply)
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
        AssertContains(status.MissingEvidence, "accepted-apply-approval");
        AssertContains(status.MissingEvidence, "accepted-source-apply-request");
        AssertContains(status.ForbiddenActions, "do not apply source from patch readiness alone");
        AssertContains(status.ForbiddenActions, "do not treat validation passed as approval");
        AssertContains(status.ForbiddenActions, "do not treat patch package completed as source apply authority");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB04_AskBeforeMutation_StatusWithAcceptedApplyApprovalStillRequiresEligibilityAndExecutorRecheck()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusRequest() with
        {
            EvidenceRefs =
            [
                "accepted-apply-approval:approval-b04",
                "accepted-source-apply-request:request-b04",
                "operation-eligibility-decision:decision-b04"
            ],
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply)
        });

        Assert.AreEqual(GovernedOperationState.Eligible, status.State);
        AssertContains(status.ForbiddenActions, "do not execute from status alone");
        AssertContains(status.ForbiddenActions, "do not treat Eligible status as approval");
        AssertContains(status.ForbiddenActions, "do not treat Eligible status as policy satisfaction");
        AssertContains(status.ForbiddenActions, "do not apply source from status alone");
        AssertContains(status.ForbiddenActions, "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB04_Receipt_RecordsAskBeforeMutationBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B04_ASK_BEFORE_MUTATION_RUN_PROFILE.md"));

        StringAssert.Contains(doc, "AskBeforeMutation is now a supported run-profile kind.");
        StringAssert.Contains(doc, "AskBeforeMutation supports only the source-apply lane.");
        StringAssert.Contains(doc, "AskBeforeMutation profile allowance is not approval.");
        StringAssert.Contains(doc, "AskBeforeMutation profile allowance is not policy satisfaction.");
        StringAssert.Contains(doc, "AskBeforeMutation profile allowance is not execution authority.");
        StringAssert.Contains(doc, "AskBeforeMutation profile allowance is not source apply execution.");
        StringAssert.Contains(doc, "Source apply still requires accepted apply approval evidence.");
        StringAssert.Contains(doc, "Eligible status is still not execution.");
        StringAssert.Contains(doc, "BoundedRunAuthority remains unsupported.");
        StringAssert.Contains(doc, "ProposalOnly behavior did not widen.");
        StringAssert.Contains(doc, "AskBeforeMutation means ask before mutation, not mutate because asked.");
    }

    private static RunAuthorityProfile ProposalOnlyProfile() =>
        new()
        {
            ProfileId = "proposal-only",
            Kind = AuthorityProfileKind.ProposalOnly,
            AllowedOperations = ProposalOnlyAllowedOperations,
            ForbiddenOperations = ProposalOnlyForbiddenOperations,
            CanReadRepo = true,
            CanMutateDisposableWorkspace = true,
            CanWriteProposalEvidence = true,
            CanInspectGovernedStatus = true,
            CanMutateDurableSource = false,
            CanApplyPatch = false,
            CanExecuteRollback = false,
            CanCommit = false,
            CanPush = false,
            CanCreatePullRequest = false,
            CanMarkReadyForReview = false,
            CanMerge = false,
            CanRelease = false,
            CanDeploy = false,
            CanCreateApprovalRequest = false,
            CanSatisfyPolicy = false,
            CanPromoteMemory = false,
            CanContinueWorkflow = false,
            CanExecuteProviderMutation = false,
            CanPublishPackage = false
        };

    private static RunAuthorityProfile AskBeforeMutationProfile() =>
        ProposalOnlyProfile() with
        {
            ProfileId = "ask-before-mutation",
            Kind = AuthorityProfileKind.AskBeforeMutation,
            AllowedOperations = AskBeforeMutationAllowedOperations,
            ForbiddenOperations = AskBeforeMutationForbiddenOperations,
            CanMutateDurableSource = true,
            CanApplyPatch = true
        };

    private static AuthorityProfileStatusRequest StatusRequest() =>
        new()
        {
            OperationId = "operation-b04-001",
            OperationKind = RunAuthorityOperationKind.SourceApply,
            Subject = "ask before mutation source apply",
            ProfileKind = AuthorityProfileKind.AskBeforeMutation,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/ask-before-mutation-run-profile",
            RunId = "run-b04-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = null,
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs = [],
            ReceiptRefs = []
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operationKind) =>
        new()
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
        };

    private static void AssertInvalid(RunAuthorityProfile profile, string expectedIssue)
    {
        var validation = RunAuthorityProfileValidator.Validate(profile);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, expectedIssue);
    }

    private static void AssertValid(GovernedOperationStatus status)
    {
        var validation = GovernedOperationStatusValidator.Validate(status);
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Concat(validation.RedFlags)));
    }

    private static void AssertContains(IEnumerable<string> values, string expected)
    {
        Assert.IsTrue(
            values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected '{expected}' in: {string.Join(", ", values)}");
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

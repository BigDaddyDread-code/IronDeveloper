using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB05BoundedRunAuthorityProfileTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b05abcdef123456";

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

    private static readonly RunAuthorityOperationKind[] BoundedRunAuthorityAllowedOperations =
    [
        .. ProposalOnlyAllowedOperations,
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest
    ];

    private static readonly RunAuthorityOperationKind[] BoundedRunAuthorityForbiddenOperations =
    [
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

    private static readonly RunAuthorityOperationKind[] BoundedMutationOperations =
    [
        RunAuthorityOperationKind.SourceApply,
        RunAuthorityOperationKind.DurableSourceMutation,
        RunAuthorityOperationKind.Rollback,
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest
    ];

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_ProfileValidatesWithBoundedMutationCeiling()
    {
        var validation = RunAuthorityProfileValidator.Validate(BoundedRunAuthorityProfile());

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            BoundedRunAuthorityAllowedOperations,
            RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            BoundedRunAuthorityForbiddenOperations,
            RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations.ToArray());
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_BoundedOperationsAllowedByProfileButNotAuthority()
    {
        foreach (var operation in BoundedMutationOperations)
        {
            var decision = RunAuthorityProfileEvaluator.Evaluate(BoundedRunAuthorityProfile(), operation);

            Assert.IsTrue(decision.IsAllowedByProfile, operation.ToString() + ": " + string.Join(", ", decision.BlockedReasons));
            Assert.AreEqual(AuthorityProfileKind.BoundedRunAuthority, decision.ProfileKind);
            AssertContains(decision.RequiredIndependentChecks, "operation-specific validation still required");
            AssertContains(decision.RequiredIndependentChecks, "profile allowance is necessary but not sufficient");
            AssertContains(decision.ForbiddenActions, "do not treat profile allowance as approval");
            AssertContains(decision.ForbiddenActions, "do not treat profile allowance as policy satisfaction");
            AssertContains(decision.ForbiddenActions, "do not treat profile allowance as execution authority");
            AssertContains(decision.ForbiddenActions, "do not mutate durable source from profile allowance");
        }
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_DoesNotAllowReleaseOrContinuationLanes()
    {
        foreach (var operation in BoundedRunAuthorityForbiddenOperations)
        {
            var decision = RunAuthorityProfileEvaluator.Evaluate(BoundedRunAuthorityProfile(), operation);

            Assert.IsFalse(decision.IsAllowedByProfile, operation.ToString());
            AssertContains(decision.BlockedReasons, $"BoundedRunAuthority does not allow {operation}.");
            AssertContains(decision.RequiredIndependentChecks, "explicit governed authority required outside this profile");
        }
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_RejectsWidenedOperationSet()
    {
        foreach (var operation in BoundedRunAuthorityForbiddenOperations)
        {
            AssertInvalid(
                BoundedRunAuthorityProfile() with
                {
                    AllowedOperations = [.. BoundedRunAuthorityAllowedOperations, operation]
                },
                $"BoundedRunAuthorityCannotAllowDangerousOperation:{operation}");
        }
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_RequiresBoundedMutationOperations()
    {
        foreach (var operation in BoundedMutationOperations)
        {
            AssertInvalid(
                BoundedRunAuthorityProfile() with
                {
                    AllowedOperations = BoundedRunAuthorityAllowedOperations
                        .Where(candidate => candidate != operation)
                        .ToArray()
                },
                $"BoundedRunAuthorityRequiredAllowedOperationMissing:{operation}");
        }
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_RejectsForbiddenFlags()
    {
        var cases = new Dictionary<string, RunAuthorityProfile>
        {
            [nameof(RunAuthorityProfile.CanMarkReadyForReview)] = BoundedRunAuthorityProfile() with { CanMarkReadyForReview = true },
            [nameof(RunAuthorityProfile.CanMerge)] = BoundedRunAuthorityProfile() with { CanMerge = true },
            [nameof(RunAuthorityProfile.CanRelease)] = BoundedRunAuthorityProfile() with { CanRelease = true },
            [nameof(RunAuthorityProfile.CanDeploy)] = BoundedRunAuthorityProfile() with { CanDeploy = true },
            [nameof(RunAuthorityProfile.CanCreateApprovalRequest)] = BoundedRunAuthorityProfile() with { CanCreateApprovalRequest = true },
            [nameof(RunAuthorityProfile.CanSatisfyPolicy)] = BoundedRunAuthorityProfile() with { CanSatisfyPolicy = true },
            [nameof(RunAuthorityProfile.CanPromoteMemory)] = BoundedRunAuthorityProfile() with { CanPromoteMemory = true },
            [nameof(RunAuthorityProfile.CanContinueWorkflow)] = BoundedRunAuthorityProfile() with { CanContinueWorkflow = true },
            [nameof(RunAuthorityProfile.CanExecuteProviderMutation)] = BoundedRunAuthorityProfile() with { CanExecuteProviderMutation = true },
            [nameof(RunAuthorityProfile.CanPublishPackage)] = BoundedRunAuthorityProfile() with { CanPublishPackage = true }
        };

        foreach (var item in cases)
            AssertInvalid(item.Value, $"BoundedRunAuthorityDangerousFlagMustBeFalse:{item.Key}");
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_RequiresBoundedLaneFlags()
    {
        var cases = new Dictionary<string, RunAuthorityProfile>
        {
            [nameof(RunAuthorityProfile.CanReadRepo)] = BoundedRunAuthorityProfile() with { CanReadRepo = false },
            [nameof(RunAuthorityProfile.CanMutateDisposableWorkspace)] = BoundedRunAuthorityProfile() with { CanMutateDisposableWorkspace = false },
            [nameof(RunAuthorityProfile.CanWriteProposalEvidence)] = BoundedRunAuthorityProfile() with { CanWriteProposalEvidence = false },
            [nameof(RunAuthorityProfile.CanInspectGovernedStatus)] = BoundedRunAuthorityProfile() with { CanInspectGovernedStatus = false },
            [nameof(RunAuthorityProfile.CanMutateDurableSource)] = BoundedRunAuthorityProfile() with { CanMutateDurableSource = false },
            [nameof(RunAuthorityProfile.CanApplyPatch)] = BoundedRunAuthorityProfile() with { CanApplyPatch = false },
            [nameof(RunAuthorityProfile.CanExecuteRollback)] = BoundedRunAuthorityProfile() with { CanExecuteRollback = false },
            [nameof(RunAuthorityProfile.CanCommit)] = BoundedRunAuthorityProfile() with { CanCommit = false },
            [nameof(RunAuthorityProfile.CanPush)] = BoundedRunAuthorityProfile() with { CanPush = false },
            [nameof(RunAuthorityProfile.CanCreatePullRequest)] = BoundedRunAuthorityProfile() with { CanCreatePullRequest = false }
        };

        foreach (var item in cases)
            AssertInvalid(item.Value, $"BoundedRunAuthorityRequiredFlagMustBeTrue:{item.Key}");
    }

    [TestMethod]
    public void BlockB05_ProposalOnly_BehaviorRemainsUnchanged()
    {
        var validation = RunAuthorityProfileValidator.Validate(ProposalOnlyProfile());

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            ProposalOnlyAllowedOperations,
            RunAuthorityProfileValidator.ProposalOnlyAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            ProposalOnlyForbiddenOperations,
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.ToArray());
        AssertInvalid(ProposalOnlyProfile() with { AllowedOperations = [.. ProposalOnlyAllowedOperations, RunAuthorityOperationKind.SourceApply] }, "ProposalOnlyCannotAllowDangerousOperation:SourceApply");
        AssertInvalid(ProposalOnlyProfile() with { AllowedOperations = [.. ProposalOnlyAllowedOperations, RunAuthorityOperationKind.Commit] }, "ProposalOnlyCannotAllowDangerousOperation:Commit");
        AssertInvalid(ProposalOnlyProfile() with { AllowedOperations = [.. ProposalOnlyAllowedOperations, RunAuthorityOperationKind.Push] }, "ProposalOnlyCannotAllowDangerousOperation:Push");
        AssertInvalid(ProposalOnlyProfile() with { AllowedOperations = [.. ProposalOnlyAllowedOperations, RunAuthorityOperationKind.DraftPullRequest] }, "ProposalOnlyCannotAllowDangerousOperation:DraftPullRequest");
        AssertInvalid(ProposalOnlyProfile() with { CanApplyPatch = true }, "ProposalOnlyDangerousFlagMustBeFalse:CanApplyPatch");
        AssertInvalid(ProposalOnlyProfile() with { CanReadRepo = false }, "ProposalOnlySafeFlagMustBeTrue:CanReadRepo");
    }

    [TestMethod]
    public void BlockB05_AskBeforeMutation_BehaviorRemainsUnchanged()
    {
        var validation = RunAuthorityProfileValidator.Validate(AskBeforeMutationProfile());

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        CollectionAssert.AreEquivalent(
            AskBeforeMutationAllowedOperations,
            RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations.ToArray());
        CollectionAssert.AreEquivalent(
            AskBeforeMutationForbiddenOperations,
            RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations.ToArray());

        foreach (var operation in AskBeforeMutationForbiddenOperations)
        {
            AssertInvalid(
                AskBeforeMutationProfile() with
                {
                    AllowedOperations = [.. AskBeforeMutationAllowedOperations, operation]
                },
                $"AskBeforeMutationCannotAllowDangerousOperation:{operation}");
        }
    }

    [TestMethod]
    public void BlockB05_BoundedGrantValidator_AllowsOnlyBoundedRunAuthorityAllowedOperations()
    {
        foreach (var operation in BoundedRunAuthorityAllowedOperations)
        {
            var validation = BoundedRunAuthorityGrantValidator.Validate(
                ValidGrant() with { AllowedOperationKinds = [operation] },
                ObservedAtUtc);

            Assert.IsTrue(validation.IsValid, operation + ": " + string.Join(", ", validation.Issues));
        }

        foreach (var operation in BoundedRunAuthorityForbiddenOperations)
        {
            AssertGrantInvalid(
                ValidGrant() with { AllowedOperationKinds = [operation] },
                $"BoundedRunAllowedOperationCannotCrossBoundary:{operation}");
        }
    }

    [TestMethod]
    public void BlockB05_BoundedGrant_StopBeforeStillBlocksAllowedOperation()
    {
        var grant = ValidGrant() with
        {
            AllowedOperationKinds = [RunAuthorityOperationKind.Commit],
            StopBeforeOperationKinds = [RunAuthorityOperationKind.Commit]
        };
        var match = BoundedRunAuthorityGrantMatcher.Evaluate(
            grant,
            ObservedAtUtc,
            "BigDaddyDread-code/IronDeveloper",
            "governance/bounded-run-authority-profile",
            "run-b05-001",
            RunAuthorityOperationKind.Commit,
            "IronDev.Core/Governance/RunProfiles/RunAuthorityProfileValidator.cs");
        var forbiddenGrant = ValidGrant() with
        {
            AllowedOperationKinds = [RunAuthorityOperationKind.Merge],
            StopBeforeOperationKinds = [RunAuthorityOperationKind.Merge]
        };

        Assert.IsFalse(match.IsInsideGrantEnvelope);
        AssertContains(match.BlockedReasons, "OperationStoppedBefore:Commit");
        AssertGrantInvalid(forbiddenGrant, "BoundedRunAllowedOperationCannotCrossBoundary:Merge");
    }

    [TestMethod]
    public void BlockB05_OperationEligibility_BoundedRunAuthorityRequiresMatchingGrant()
    {
        var missingGrant = OperationEligibilityEvaluator.Evaluate(EligibilityRequest() with { Grant = null! });
        var wrongOperationGrant = OperationEligibilityEvaluator.Evaluate(EligibilityRequest() with
        {
            Grant = ValidGrant() with { AllowedOperationKinds = [RunAuthorityOperationKind.Push] }
        });

        Assert.IsFalse(missingGrant.IsEligibleUnderProfileAndGrant);
        AssertContains(missingGrant.BlockedReasons, "BoundedRunAuthorityGrantCheckFailed");
        AssertContains(missingGrant.ForbiddenActions, "do not proceed outside bounded grant envelope");

        Assert.IsFalse(wrongOperationGrant.IsEligibleUnderProfileAndGrant);
        AssertContains(
            wrongOperationGrant.BlockedReasons,
            "AffectedFileRejected:IronDev.Core/Governance/RunProfiles/RunAuthorityProfileValidator.cs:OperationNotAllowed:Commit");
    }

    [TestMethod]
    public void BlockB05_OperationEligibility_MatchingProfileAndGrantStillNotExecution()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(EligibilityRequest());

        Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join(", ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as approval");
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as policy satisfaction");
        AssertContains(decision.ForbiddenActions, "do not treat eligibility as execution authority");
        AssertContains(decision.ForbiddenActions, "do not mutate durable source from eligibility");
        AssertContains(decision.RequiredIndependentChecks, "operation-specific governance still required");
        AssertContains(decision.RequiredIndependentChecks, "profile and grant eligibility is necessary but not sufficient");
    }

    [TestMethod]
    public void BlockB05_BoundedRunAuthority_StatusEligibleStillNotExecution()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusRequest() with
        {
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
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
    public void BlockB05_BoundedRunAuthority_StatusBlocksForbiddenProfileLanes()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusRequest() with
        {
            OperationKind = RunAuthorityOperationKind.Release,
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Release)
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "BoundedRunAuthorityOperationBlocked:Release");
        AssertContains(status.ForbiddenActions, "do not perform Release under BoundedRunAuthority");
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB05_Receipt_RecordsBoundedRunAuthorityBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B05_BOUNDED_RUN_AUTHORITY_PROFILE.md"));

        StringAssert.Contains(doc, "BoundedRunAuthority is now a supported run-profile kind.");
        StringAssert.Contains(doc, "BoundedRunAuthority is broader than AskBeforeMutation but still bounded.");
        StringAssert.Contains(doc, "BoundedRunAuthority profile is not a bounded grant.");
        StringAssert.Contains(doc, "BoundedRunAuthority profile allowance is not approval.");
        StringAssert.Contains(doc, "BoundedRunAuthority profile allowance is not policy satisfaction.");
        StringAssert.Contains(doc, "BoundedRunAuthority profile allowance is not execution authority.");
        StringAssert.Contains(doc, "BoundedRunAuthority profile allowance is not source apply, rollback, commit, push, or PR execution.");
        StringAssert.Contains(doc, "Operation eligibility remains necessary but not sufficient.");
        StringAssert.Contains(doc, "Eligible status remains not execution.");
        StringAssert.Contains(doc, "ProposalOnly behavior did not widen.");
        StringAssert.Contains(doc, "AskBeforeMutation behavior did not widen.");
        StringAssert.Contains(doc, "A bounded profile names the lane. The grant, evidence, and executor still hold the keys.");
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

    private static RunAuthorityProfile BoundedRunAuthorityProfile() =>
        AskBeforeMutationProfile() with
        {
            ProfileId = "bounded-run-authority",
            Kind = AuthorityProfileKind.BoundedRunAuthority,
            AllowedOperations = BoundedRunAuthorityAllowedOperations,
            ForbiddenOperations = BoundedRunAuthorityForbiddenOperations,
            CanExecuteRollback = true,
            CanCommit = true,
            CanPush = true,
            CanCreatePullRequest = true
        };

    private static BoundedRunAuthorityGrant ValidGrant() =>
        new()
        {
            GrantId = "grant-b05-001",
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/bounded-run-authority-profile",
            RunId = "run-b05-001",
            AllowedOperationKinds = [RunAuthorityOperationKind.Commit],
            AllowedFileGlobs =
            [
                "IronDev.Core/Governance/**",
                "IronDev.IntegrationTests/**",
                "Docs/receipts/**"
            ],
            ForbiddenFileGlobs = ["Docs/receipts/secret.md"],
            PatchHash = PatchHash,
            ExpiresAtUtc = ObservedAtUtc.AddHours(1),
            MaxMutations = 2,
            RequiredValidation =
            [
                new BoundedRunAuthorityRequiredValidation
                {
                    ValidationKind = "FocusedB05",
                    MustPass = true,
                    EvidenceRefPrefixes = ["validation-result:"]
                }
            ],
            StopBeforeOperationKinds = [],
            GrantedBy = new BoundedRunAuthorityGrantedBy
            {
                PrincipalId = "human:bob",
                PrincipalKind = "Human",
                EvidenceRef = "approval-note:b05-spec"
            },
            HumanReadableIntent = "Allow one bounded run authority profile check inside the B05 branch envelope."
        };

    private static OperationEligibilityRequest EligibilityRequest() =>
        new()
        {
            Profile = BoundedRunAuthorityProfile(),
            Grant = ValidGrant(),
            OperationKind = RunAuthorityOperationKind.Commit,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/bounded-run-authority-profile",
            RunId = "run-b05-001",
            AffectedFilePaths = ["IronDev.Core/Governance/RunProfiles/RunAuthorityProfileValidator.cs"],
            ObservedAtUtc = ObservedAtUtc,
            PatchHash = PatchHash,
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            ValidationEvidence =
            [
                new OperationEligibilityValidationEvidence
                {
                    ValidationKind = "FocusedB05",
                    Outcome = OperationEligibilityValidationOutcome.Passed,
                    EvidenceRef = "validation-result:b05-focused",
                    PatchHash = PatchHash
                }
            ]
        };

    private static AuthorityProfileStatusRequest StatusRequest() =>
        new()
        {
            OperationId = "operation-b05-001",
            OperationKind = RunAuthorityOperationKind.Commit,
            Subject = "bounded run authority commit status",
            ProfileKind = AuthorityProfileKind.BoundedRunAuthority,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/bounded-run-authority-profile",
            RunId = "run-b05-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = null,
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs =
            [
                "bounded-run-authority-grant:grant-b05-001",
                "operation-eligibility-decision:decision-b05-001"
            ],
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

    private static void AssertGrantInvalid(BoundedRunAuthorityGrant grant, string expectedIssue)
    {
        var validation = BoundedRunAuthorityGrantValidator.Validate(grant, ObservedAtUtc);

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

using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB10CanonicalAuthorityGlossaryTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b10abcdef1234567890";

    [TestMethod]
    public void BlockB10_AuthorityGlossary_ConstantsHaveCanonicalValues()
    {
        var expected = new Dictionary<string, string>
        {
            [nameof(AuthorityGlossary.EvidenceIsNotApproval)] = "evidence is not approval",
            [nameof(AuthorityGlossary.ValidationPassedIsNotApproval)] = "validation passed is not approval",
            [nameof(AuthorityGlossary.ReceiptRefsAreNotAuthority)] = "receipt refs are not authority",
            [nameof(AuthorityGlossary.EvidenceRefsAreNotAuthority)] = "evidence refs are not authority",
            [nameof(AuthorityGlossary.StatusIsNotAuthority)] = "status is not authority",
            [nameof(AuthorityGlossary.ProfileKindIsNotAuthority)] = "profile kind is not authority",
            [nameof(AuthorityGlossary.ProfileAllowanceNecessaryNotSufficient)] = "profile allowance is necessary but not sufficient",
            [nameof(AuthorityGlossary.DoNotTreatProfileAllowanceAsApproval)] = "do not treat profile allowance as approval",
            [nameof(AuthorityGlossary.DoNotTreatProfileAllowanceAsPolicySatisfaction)] = "do not treat profile allowance as policy satisfaction",
            [nameof(AuthorityGlossary.DoNotTreatProfileAllowanceAsExecutionAuthority)] = "do not treat profile allowance as execution authority",
            [nameof(AuthorityGlossary.DoNotMutateDurableSourceFromProfileAllowance)] = "do not mutate durable source from profile allowance",
            [nameof(AuthorityGlossary.ProfileAndGrantEligibilityNecessaryNotSufficient)] = "profile and grant eligibility is necessary but not sufficient",
            [nameof(AuthorityGlossary.OperationSpecificGovernanceStillRequired)] = "operation-specific governance still required",
            [nameof(AuthorityGlossary.DoNotTreatEligibilityAsApproval)] = "do not treat eligibility as approval",
            [nameof(AuthorityGlossary.DoNotTreatEligibilityAsPolicySatisfaction)] = "do not treat eligibility as policy satisfaction",
            [nameof(AuthorityGlossary.DoNotTreatEligibilityAsExecutionAuthority)] = "do not treat eligibility as execution authority",
            [nameof(AuthorityGlossary.DoNotTreatEligibilityAsSourceApplyAuthority)] = "do not treat eligibility as source apply authority",
            [nameof(AuthorityGlossary.DoNotMutateDurableSourceFromEligibility)] = "do not mutate durable source from eligibility",
            [nameof(AuthorityGlossary.DoNotExecuteFromStatusAlone)] = "do not execute from status alone",
            [nameof(AuthorityGlossary.DoNotTreatEligibleStatusAsApproval)] = "do not treat Eligible status as approval",
            [nameof(AuthorityGlossary.DoNotTreatEligibleStatusAsPolicySatisfaction)] = "do not treat Eligible status as policy satisfaction",
            [nameof(AuthorityGlossary.DoNotApplySourceFromStatusAlone)] = "do not apply source from status alone",
            [nameof(AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree)] = "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state",
            [nameof(AuthorityGlossary.StatusMayExplainGateMustNotBecomeGate)] = "Status may explain the gate. It must not become the gate.",
            [nameof(AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority)] = "do not treat accepted apply approval as authority for later mutation lanes",
            [nameof(AuthorityGlossary.SourceApplyReceiptIsNotAcceptedApplyApproval)] = "source apply receipt is not accepted apply approval",
            [nameof(AuthorityGlossary.AskBeforeMutationOneGuardedDoorNotHallway)] = "AskBeforeMutation asks for one guarded door. It does not open the hallway.",
            [nameof(AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority)] = "do not treat bounded profile allowance as later-stage authority",
            [nameof(AuthorityGlossary.SourceApplyDecisionIsNotCommitAuthority)] = "A source apply decision is not commit authority.",
            [nameof(AuthorityGlossary.CommitDecisionIsNotPushAuthority)] = "A commit decision is not push authority.",
            [nameof(AuthorityGlossary.PushDecisionIsNotDraftPrAuthority)] = "A push decision is not draft PR authority.",
            [nameof(AuthorityGlossary.DraftPrDecisionIsNotReadyForReviewAuthority)] = "A draft PR decision is not ready-for-review authority.",
            [nameof(AuthorityGlossary.BoundedLaneEndsWhereNextBoundaryBegins)] = "A bounded lane ends where the next authority boundary begins.",
            [nameof(AuthorityGlossary.ProposalOnlyMeansProposalOnly)] = "ProposalOnly means proposal only, even when every receipt begs otherwise."
        };
        var actual = typeof(AuthorityGlossary)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actual.Keys.ToArray());
        foreach (var item in expected)
            Assert.AreEqual(item.Value, actual[item.Key], item.Key);
    }

    [TestMethod]
    public void BlockB10_AuthorityGlossary_DoesNotExposeAuthorityDecisionNames()
    {
        var forbidden = new[]
        {
            "IsAuthorized",
            "IsApproved",
            "CanExecute",
            "CanMutate",
            "CanApply",
            "CanCommit",
            "CanPush",
            "HasAuthority",
            "AuthorityGranted",
            "PolicySatisfied"
        };
        var memberNames = typeof(AuthorityGlossary)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Select(member => member.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(
                memberNames.Any(name => name.Contains(marker, StringComparison.OrdinalIgnoreCase)),
                $"{marker} found in {string.Join(", ", memberNames)}");
        }
    }

    [TestMethod]
    public void BlockB10_RunAuthorityProfileEvaluator_EmitsCanonicalGlossaryPhrases()
    {
        var decision = RunAuthorityProfileEvaluator.Evaluate(
            BoundedRunAuthorityProfile(),
            RunAuthorityOperationKind.Commit);

        Assert.IsTrue(decision.IsAllowedByProfile, string.Join(", ", decision.BlockedReasons));
        AssertContains(decision.RequiredIndependentChecks, AuthorityGlossary.ProfileAllowanceNecessaryNotSufficient);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatProfileAllowanceAsApproval);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatProfileAllowanceAsPolicySatisfaction);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatProfileAllowanceAsExecutionAuthority);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotMutateDurableSourceFromProfileAllowance);
    }

    [TestMethod]
    public void BlockB10_OperationEligibility_EmitsCanonicalGlossaryPhrases()
    {
        var decision = OperationEligibilityEvaluator.Evaluate(EligibilityRequest());

        Assert.IsTrue(decision.IsEligibleUnderProfileAndGrant, string.Join(", ", decision.BlockedReasons.Concat(decision.MissingEvidence)));
        AssertContains(decision.RequiredIndependentChecks, AuthorityGlossary.OperationSpecificGovernanceStillRequired);
        AssertContains(decision.RequiredIndependentChecks, AuthorityGlossary.ProfileAndGrantEligibilityNecessaryNotSufficient);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibilityAsApproval);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibilityAsPolicySatisfaction);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibilityAsExecutionAuthority);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibilityAsSourceApplyAuthority);
        AssertContains(decision.ForbiddenActions, AuthorityGlossary.DoNotMutateDurableSourceFromEligibility);
    }

    [TestMethod]
    public void BlockB10_AuthorityProfileStatusMapper_EligibleStatusEmitsCanonicalGlossaryPhrases()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusRequest() with
        {
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.PatchPackageWrite)
        });

        Assert.AreEqual(GovernedOperationState.Eligible, status.State);
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotExecuteFromStatusAlone);
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibleStatusAsApproval);
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibleStatusAsPolicySatisfaction);
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotApplySourceFromStatusAlone);
        AssertContains(status.ForbiddenActions, AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree);
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB10_AskBeforeMutationBoundary_UsesCanonicalGlossaryPhrase()
    {
        var status = AuthorityProfileStatusMapper.Map(AskBeforeMutationRequest(RunAuthorityOperationKind.Commit));

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "AskBeforeMutationOperationBlocked:Commit");
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority);
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB10_BoundedRunAuthorityBoundary_UsesCanonicalGlossaryPhrase()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusRequest() with
        {
            OperationKind = RunAuthorityOperationKind.ReadyForReview,
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.ReadyForReview)
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "BoundedRunAuthorityOperationBlocked:ReadyForReview");
        AssertContains(status.ForbiddenActions, AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority);
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB10_Receipt_RecordsCanonicalAuthorityGlossary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B10_CANONICAL_AUTHORITY_GLOSSARY.md"));

        StringAssert.Contains(doc, "This PR adds canonical authority glossary constants.");
        StringAssert.Contains(doc, "The glossary is not authority.");
        StringAssert.Contains(doc, "The glossary is not approval.");
        StringAssert.Contains(doc, "The glossary is not policy satisfaction.");
        StringAssert.Contains(doc, "The glossary is not execution permission.");
        StringAssert.Contains(doc, "The glossary does not create or widen any profile.");
        StringAssert.Contains(doc, "The glossary does not change status mapping behavior.");
        StringAssert.Contains(doc, "The glossary does not change operation eligibility behavior.");
        StringAssert.Contains(doc, "The glossary does not add executors.");
        StringAssert.Contains(doc, "The glossary does not add UI, API, CLI, SQL, durable store, or generated client paths.");
        StringAssert.Contains(doc, "If the vocabulary drifts, the boundary drifts.");
        StringAssert.Contains(doc, "Words are not gates, but bad words rot gates.");
    }

    private static RunAuthorityProfile ProposalOnlyProfile() =>
        new()
        {
            ProfileId = "proposal-only",
            Kind = AuthorityProfileKind.ProposalOnly,
            AllowedOperations = RunAuthorityProfileValidator.ProposalOnlyAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations,
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
            AllowedOperations = RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations,
            CanMutateDurableSource = true,
            CanApplyPatch = true
        };

    private static RunAuthorityProfile BoundedRunAuthorityProfile() =>
        AskBeforeMutationProfile() with
        {
            ProfileId = "bounded-run-authority",
            Kind = AuthorityProfileKind.BoundedRunAuthority,
            AllowedOperations = RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations,
            ForbiddenOperations = RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations,
            CanExecuteRollback = true,
            CanCommit = true,
            CanPush = true,
            CanCreatePullRequest = true
        };

    private static OperationEligibilityRequest EligibilityRequest() =>
        new()
        {
            Profile = BoundedRunAuthorityProfile(),
            Grant = ValidGrant(),
            OperationKind = RunAuthorityOperationKind.Commit,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/canonical-authority-glossary",
            RunId = "run-b10-001",
            AffectedFilePaths = ["IronDev.Core/Governance/AuthorityGlossary.cs"],
            ObservedAtUtc = ObservedAtUtc,
            PatchHash = PatchHash,
            MutationsAlreadyConsumed = 0,
            RequestedMutationCount = 1,
            ValidationEvidence =
            [
                new OperationEligibilityValidationEvidence
                {
                    ValidationKind = "FocusedB10",
                    Outcome = OperationEligibilityValidationOutcome.Passed,
                    EvidenceRef = "validation-result:b10-focused",
                    PatchHash = PatchHash
                }
            ]
        };

    private static BoundedRunAuthorityGrant ValidGrant() =>
        new()
        {
            GrantId = "grant-b10-001",
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/canonical-authority-glossary",
            RunId = "run-b10-001",
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
                    ValidationKind = "FocusedB10",
                    MustPass = true,
                    EvidenceRefPrefixes = ["validation-result:"]
                }
            ],
            StopBeforeOperationKinds = [],
            GrantedBy = new BoundedRunAuthorityGrantedBy
            {
                PrincipalId = "human:bob",
                PrincipalKind = "Human",
                EvidenceRef = "approval-note:b10-spec"
            },
            HumanReadableIntent = "Bind the canonical authority glossary vocabulary without changing behavior."
        };

    private static AuthorityProfileStatusRequest StatusRequest() =>
        new()
        {
            OperationId = "operation-b10-001",
            OperationKind = RunAuthorityOperationKind.PatchPackageWrite,
            Subject = "canonical authority glossary status",
            ProfileKind = AuthorityProfileKind.BoundedRunAuthority,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/canonical-authority-glossary",
            RunId = "run-b10-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = null,
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs =
            [
                "bounded-run-authority-grant:grant-b10-001",
                "operation-eligibility-decision:decision-b10-001"
            ],
            ReceiptRefs = []
        };

    private static AuthorityProfileStatusRequest AskBeforeMutationRequest(RunAuthorityOperationKind operation) =>
        new()
        {
            OperationId = $"operation-b10-ask-{operation}",
            OperationKind = operation,
            Subject = "canonical AskBeforeMutation glossary status",
            ProfileKind = AuthorityProfileKind.AskBeforeMutation,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/canonical-authority-glossary",
            RunId = "run-b10-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(operation),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs =
            [
                "accepted-apply-approval:approval-b10",
                "accepted-source-apply-request:request-b10"
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
                AuthorityGlossary.DoNotTreatEligibilityAsApproval,
                AuthorityGlossary.DoNotTreatEligibilityAsPolicySatisfaction,
                AuthorityGlossary.DoNotTreatEligibilityAsExecutionAuthority,
                AuthorityGlossary.DoNotTreatEligibilityAsSourceApplyAuthority,
                AuthorityGlossary.DoNotMutateDurableSourceFromEligibility
            ],
            RequiredIndependentChecks =
            [
                AuthorityGlossary.OperationSpecificGovernanceStillRequired,
                AuthorityGlossary.ProfileAndGrantEligibilityNecessaryNotSufficient
            ]
        };

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

using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockB11StatusAuthorityGlossaryAdoptionTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
    private const string PatchHash = "sha256:b11abcdef1234567890";

    [TestMethod]
    public void BlockB11_EligibleStatus_UsesAuthorityGlossaryConstants()
    {
        var status = Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
        {
            EvidenceRefs = BoundedEvidenceRefs(),
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
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
    public void BlockB11_AskBeforeMutationBoundary_UsesAuthorityGlossaryConstant()
    {
        var status = Map(AskBeforeMutationRequest(RunAuthorityOperationKind.Commit) with
        {
            EvidenceRefs =
            [
                "accepted-apply-approval:approval-b11",
                "accepted-source-apply-request:request-b11"
            ],
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "AskBeforeMutationOperationBlocked:Commit");
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority);
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB11_BoundedRunAuthorityBoundary_UsesAuthorityGlossaryConstant()
    {
        var status = Map(BoundedRequest(RunAuthorityOperationKind.ReadyForReview) with
        {
            EvidenceRefs = BoundedEvidenceRefs(),
            EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.ReadyForReview)
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "BoundedRunAuthorityOperationBlocked:ReadyForReview");
        AssertContains(status.ForbiddenActions, AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority);
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB11_BlockedEligibilityStatus_UsesStatusAloneGlossaryConstant()
    {
        var status = Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
        {
            EvidenceRefs = BoundedEvidenceRefs(),
            EligibilityDecision = BlockedDecision(
                RunAuthorityOperationKind.Commit,
                ["CommitAuthorityMissing"],
                [])
        });

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        AssertContains(status.BlockedReasons, "OperationEligibilityDecisionBlocked");
        AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotExecuteFromStatusAlone);
        AssertValid(status);
    }

    [TestMethod]
    public void BlockB11_StatusGlossaryAdoption_DoesNotChangeRepresentativeStatusOutputs()
    {
        var cases = new[]
        {
            (
                Name: "Null request",
                Status: AuthorityProfileStatusMapper.Map(null),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["AuthorityProfileKnownRequired"],
                    ["authority-profile-status-request"],
                    ["request authority profile status input"],
                    ["do not infer authority from missing status input"],
                    [],
                    [],
                    null,
                    DateTimeOffset.UnixEpoch)),
            (
                Name: "Unknown profile",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with { ProfileKind = AuthorityProfileKind.Unknown }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["AuthorityProfileKnownRequired"],
                    ["known-authority-profile"],
                    ["request known authority profile selection"],
                    ["do not infer authority from unknown profile"],
                    BaseEvidenceRefs(AuthorityProfileKind.Unknown),
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "Unknown operation",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Unknown)),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["AuthorityProfileOperationKnownRequired"],
                    ["known-authority-profile-operation"],
                    ["request known governed operation selection"],
                    ["do not infer authority from unknown operation"],
                    BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority),
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "ProposalOnly SourceApply",
                Status: Map(ProposalOnlyRequest(RunAuthorityOperationKind.SourceApply) with { EligibilityDecision = null }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["ProposalOnlyDoesNotAllowDurableMutation", "ProposalOnlyOperationBlocked:SourceApply"],
                    ["bounded-run-authority-grant", "accepted-source-apply-authority"],
                    ["review patch package", "request bounded mutation authority for this repo/branch/run/scope"],
                    [
                        "do not apply source under ProposalOnly",
                        "do not commit under ProposalOnly",
                        "do not push under ProposalOnly",
                        "do not continue workflow from ProposalOnly status"
                    ],
                    BaseEvidenceRefs(AuthorityProfileKind.ProposalOnly),
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "AskBeforeMutation Commit",
                Status: Map(AskBeforeMutationRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = ["accepted-apply-approval:approval-b11"],
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["AskBeforeMutationOperationBlocked:Commit"],
                    ["separate governed authority for Commit"],
                    ["request governed authority outside AskBeforeMutation for Commit"],
                    [
                        "do not perform Commit under AskBeforeMutation",
                        AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority
                    ],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.AskBeforeMutation), "accepted-apply-approval:approval-b11"],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "AskBeforeMutation SourceApply without accepted approval",
                Status: Map(AskBeforeMutationRequest(RunAuthorityOperationKind.SourceApply) with
                {
                    EvidenceRefs =
                    [
                        "patch-package:package-b11",
                        "validation-result:passed-b11"
                    ],
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["MutationRequiresExplicitHumanApproval"],
                    ["accepted-apply-approval", "accepted-source-apply-request"],
                    ["request human apply approval for this patch hash/scope"],
                    [
                        "do not apply source from patch readiness alone",
                        "do not treat validation passed as approval",
                        "do not treat patch package completed as source apply authority"
                    ],
                    [
                        .. BaseEvidenceRefs(AuthorityProfileKind.AskBeforeMutation),
                        "patch-package:package-b11",
                        "validation-result:passed-b11"
                    ],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "BoundedRunAuthority ReadyForReview",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.ReadyForReview) with
                {
                    EvidenceRefs = BoundedEvidenceRefs(),
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.ReadyForReview)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["BoundedRunAuthorityOperationBlocked:ReadyForReview"],
                    ["separate governed authority for ReadyForReview"],
                    ["request governed authority outside BoundedRunAuthority for ReadyForReview"],
                    [
                        "do not perform ReadyForReview under BoundedRunAuthority",
                        AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority
                    ],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), .. BoundedEvidenceRefs()],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "BoundedRunAuthority Commit missing grant ref",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = ["operation-eligibility-decision:decision-b11"],
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["BoundedRunAuthorityGrantEvidenceRequired"],
                    ["bounded-run-authority-grant"],
                    ["inspect bounded run authority grant and operation eligibility decision evidence"],
                    ["do not infer bounded authority from profile kind alone"],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), "operation-eligibility-decision:decision-b11"],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "BoundedRunAuthority Commit missing eligibility ref",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = ["bounded-run-authority-grant:grant-b11"],
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["OperationEligibilityDecisionEvidenceRequired"],
                    ["operation-eligibility-decision"],
                    ["inspect bounded run authority grant and operation eligibility decision evidence"],
                    ["do not infer bounded authority from eligibility text alone"],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), "bounded-run-authority-grant:grant-b11"],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "BoundedRunAuthority Commit eligible",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = BoundedEvidenceRefs(),
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Eligible,
                    [],
                    [],
                    ["request controlled executor review for independent authority re-check"],
                    [
                        AuthorityGlossary.DoNotExecuteFromStatusAlone,
                        AuthorityGlossary.DoNotTreatEligibleStatusAsApproval,
                        AuthorityGlossary.DoNotTreatEligibleStatusAsPolicySatisfaction,
                        AuthorityGlossary.DoNotApplySourceFromStatusAlone,
                        AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree
                    ],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), .. BoundedEvidenceRefs()],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "Eligibility operation mismatch",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = BoundedEvidenceRefs(),
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.SourceApply)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["OperationEligibilityDecisionOperationMismatch"],
                    ["matching operation eligibility decision"],
                    ["request operation eligibility evaluation for this operation"],
                    ["do not reuse eligibility decision from another operation"],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), .. BoundedEvidenceRefs()],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "Eligibility decision blocked",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = BoundedEvidenceRefs(),
                    EligibilityDecision = BlockedDecision(RunAuthorityOperationKind.Commit, ["CommitAuthorityMissing"], [])
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["OperationEligibilityDecisionBlocked", "CommitAuthorityMissing"],
                    ["bounded grant allowing requested operation"],
                    ["request bounded run authority for this repo/branch/run/scope"],
                    [
                        AuthorityGlossary.DoNotExecuteFromStatusAlone,
                        "do not treat eligibility evidence as approval",
                        "do not treat eligibility evidence as policy satisfaction",
                        "blocked decision forbidden action"
                    ],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), .. BoundedEvidenceRefs()],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "Eligibility decision missing evidence",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = BoundedEvidenceRefs(),
                    EligibilityDecision = MissingEvidenceDecision(RunAuthorityOperationKind.Commit)
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Blocked,
                    ["OperationEligibilityEvidenceMissing"],
                    ["validation-result:b11"],
                    ["collect required validation evidence"],
                    [
                        AuthorityGlossary.DoNotExecuteFromStatusAlone,
                        "do not treat eligibility evidence as approval",
                        "do not treat eligibility evidence as policy satisfaction",
                        "missing evidence forbidden action"
                    ],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), .. BoundedEvidenceRefs()],
                    [],
                    ObservedAtUtc.AddHours(1),
                    ObservedAtUtc)),
            (
                Name: "Expired grant",
                Status: Map(BoundedRequest(RunAuthorityOperationKind.Commit) with
                {
                    EvidenceRefs = BoundedEvidenceRefs(),
                    EligibilityDecision = EligibleDecision(RunAuthorityOperationKind.Commit),
                    GrantExpiresAtUtc = ObservedAtUtc
                }),
                Expected: new ExpectedStatus(
                    GovernedOperationState.Expired,
                    ["BoundedRunGrantExpired"],
                    ["fresh bounded run authority grant"],
                    ["request fresh bounded grant for this repo/branch/run/scope"],
                    ["do not use expired grant"],
                    [.. BaseEvidenceRefs(AuthorityProfileKind.BoundedRunAuthority), .. BoundedEvidenceRefs()],
                    [],
                    ObservedAtUtc,
                    ObservedAtUtc))
        };

        foreach (var item in cases)
        {
            AssertStatus(item.Expected, item.Status, item.Name);
            AssertValid(item.Status);
        }
    }

    [TestMethod]
    public void BlockB11_StatusMapper_DoesNotKeepRawDuplicatesForAdoptedGlossaryPhrases()
    {
        var source = MapperSource();

        foreach (var constantName in new[]
        {
            nameof(AuthorityGlossary.DoNotExecuteFromStatusAlone),
            nameof(AuthorityGlossary.DoNotTreatEligibleStatusAsApproval),
            nameof(AuthorityGlossary.DoNotTreatEligibleStatusAsPolicySatisfaction),
            nameof(AuthorityGlossary.DoNotApplySourceFromStatusAlone),
            nameof(AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree),
            nameof(AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority),
            nameof(AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority)
        })
        {
            StringAssert.Contains(source, $"AuthorityGlossary.{constantName}");
        }

        foreach (var phrase in new[]
        {
            AuthorityGlossary.DoNotExecuteFromStatusAlone,
            AuthorityGlossary.DoNotTreatEligibleStatusAsApproval,
            AuthorityGlossary.DoNotTreatEligibleStatusAsPolicySatisfaction,
            AuthorityGlossary.DoNotApplySourceFromStatusAlone,
            AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree,
            AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority,
            AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority
        })
        {
            Assert.IsFalse(
                source.Contains($"\"{phrase}\"", StringComparison.Ordinal),
                $"Raw duplicate remained in mapper: {phrase}");
        }
    }

    [TestMethod]
    public void BlockB11_StatusMapper_UsesGlossaryOnlyAsStrings()
    {
        var source = MapperSource();
        var behaviorPattern = new Regex(@"\b(if|switch|case)\b[^\r\n]*AuthorityGlossary|AuthorityGlossary[^\r\n]*\b(if|switch|case)\b");

        Assert.IsFalse(behaviorPattern.IsMatch(source), "AuthorityGlossary must not drive status mapper control flow.");
        Assert.IsTrue(source.Contains("AuthorityGlossary.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BlockB11_Receipt_RecordsStatusGlossaryAdoptionBoundary()
    {
        var doc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Docs", "receipts", "B11_STATUS_AUTHORITY_GLOSSARY_ADOPTION.md"));

        StringAssert.Contains(doc, "This PR replaces exact status authority strings with AuthorityGlossary constants where possible.");
        StringAssert.Contains(doc, "The emitted status text did not change.");
        StringAssert.Contains(doc, "The glossary is still language only.");
        StringAssert.Contains(doc, "The glossary is not authority.");
        StringAssert.Contains(doc, "The glossary is not approval.");
        StringAssert.Contains(doc, "The glossary is not policy satisfaction.");
        StringAssert.Contains(doc, "The glossary is not execution permission.");
        StringAssert.Contains(doc, "Status remains explanation, not permission.");
        StringAssert.Contains(doc, "No profile operation set changed.");
        StringAssert.Contains(doc, "No status state changed.");
        StringAssert.Contains(doc, "No operation eligibility behavior changed.");
        StringAssert.Contains(doc, "No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.");
        StringAssert.Contains(doc, "Using the right words does not grant the right authority.");
    }

    private static GovernedOperationStatus Map(AuthorityProfileStatusRequest? request) =>
        AuthorityProfileStatusMapper.Map(request);

    private static AuthorityProfileStatusRequest ProposalOnlyRequest(RunAuthorityOperationKind operation) =>
        Request(operation, AuthorityProfileKind.ProposalOnly);

    private static AuthorityProfileStatusRequest AskBeforeMutationRequest(RunAuthorityOperationKind operation) =>
        Request(operation, AuthorityProfileKind.AskBeforeMutation);

    private static AuthorityProfileStatusRequest BoundedRequest(RunAuthorityOperationKind operation) =>
        Request(operation, AuthorityProfileKind.BoundedRunAuthority);

    private static AuthorityProfileStatusRequest Request(
        RunAuthorityOperationKind operation,
        AuthorityProfileKind profileKind) =>
        new()
        {
            OperationId = $"operation-b11-{profileKind}-{operation}",
            OperationKind = operation,
            Subject = "B11 status glossary adoption",
            ProfileKind = profileKind,
            Repository = "BigDaddyDread-code/IronDeveloper",
            Branch = "governance/status-authority-glossary-adoption",
            RunId = "run-b11-001",
            PatchHash = PatchHash,
            ObservedAtUtc = ObservedAtUtc,
            EligibilityDecision = EligibleDecision(operation),
            GrantExpiresAtUtc = ObservedAtUtc.AddHours(1),
            EvidenceRefs = [],
            ReceiptRefs = []
        };

    private static OperationEligibilityDecision EligibleDecision(RunAuthorityOperationKind operation) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = true,
            OperationKind = operation,
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions =
            [
                AuthorityGlossary.DoNotTreatEligibilityAsApproval,
                AuthorityGlossary.DoNotTreatEligibilityAsPolicySatisfaction,
                AuthorityGlossary.DoNotTreatEligibilityAsExecutionAuthority
            ],
            RequiredIndependentChecks =
            [
                AuthorityGlossary.OperationSpecificGovernanceStillRequired,
                AuthorityGlossary.ProfileAndGrantEligibilityNecessaryNotSufficient
            ]
        };

    private static OperationEligibilityDecision BlockedDecision(
        RunAuthorityOperationKind operation,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> missingEvidence) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = false,
            OperationKind = operation,
            BlockedReasons = blockedReasons,
            MissingEvidence = missingEvidence,
            ForbiddenActions = ["blocked decision forbidden action"],
            RequiredIndependentChecks = ["blocked decision independent check"]
        };

    private static OperationEligibilityDecision MissingEvidenceDecision(RunAuthorityOperationKind operation) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = false,
            OperationKind = operation,
            BlockedReasons = [],
            MissingEvidence = ["validation-result:b11"],
            ForbiddenActions = ["missing evidence forbidden action"],
            RequiredIndependentChecks = ["missing evidence independent check"]
        };

    private static string[] BoundedEvidenceRefs() =>
    [
        "bounded-run-authority-grant:grant-b11",
        "operation-eligibility-decision:decision-b11"
    ];

    private static string[] BaseEvidenceRefs(AuthorityProfileKind profileKind) =>
    [
        $"authority-profile:{profileKind}",
        "repo:BigDaddyDread-code/IronDeveloper",
        "branch:governance/status-authority-glossary-adoption",
        "run:run-b11-001",
        $"patch-hash:{PatchHash}"
    ];

    private static void AssertStatus(ExpectedStatus expected, GovernedOperationStatus actual, string name)
    {
        Assert.AreEqual(expected.State, actual.State, $"{name}: State");
        AssertSequence(expected.BlockedReasons, actual.BlockedReasons, $"{name}: BlockedReasons");
        AssertSequence(expected.MissingEvidence, actual.MissingEvidence, $"{name}: MissingEvidence");
        AssertSequence(expected.NextSafeActions, actual.NextSafeActions, $"{name}: NextSafeActions");
        AssertSequence(expected.ForbiddenActions, actual.ForbiddenActions, $"{name}: ForbiddenActions");
        AssertSequence(expected.EvidenceRefs, actual.EvidenceRefs, $"{name}: EvidenceRefs");
        AssertSequence(expected.ReceiptRefs, actual.ReceiptRefs, $"{name}: ReceiptRefs");
        Assert.AreEqual(expected.ExpiresAtUtc, actual.ExpiresAtUtc, $"{name}: ExpiresAtUtc");
        Assert.AreEqual(expected.ObservedAtUtc, actual.ObservedAtUtc, $"{name}: ObservedAtUtc");
    }

    private static void AssertSequence(
        IReadOnlyCollection<string> expected,
        IReadOnlyCollection<string> actual,
        string label)
    {
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray(), label);
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

    private static string MapperSource() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "AuthorityProfiles",
            "AuthorityProfileStatusMapper.cs"));

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

    private sealed record ExpectedStatus(
        GovernedOperationState State,
        IReadOnlyCollection<string> BlockedReasons,
        IReadOnlyCollection<string> MissingEvidence,
        IReadOnlyCollection<string> NextSafeActions,
        IReadOnlyCollection<string> ForbiddenActions,
        IReadOnlyCollection<string> EvidenceRefs,
        IReadOnlyCollection<string> ReceiptRefs,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset ObservedAtUtc);
}

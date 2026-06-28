namespace IronDev.UnitTests.Status;

[TestClass]
public sealed class AuthorityProfileStatusMapperUnitTests
{
    [TestMethod]
    public void ProposalOnlyMutationMapsBlockedBeforeEligibility()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusMapperTestFixtures.AuthorityProfileRequest(
            profileKind: AuthorityProfileKind.ProposalOnly,
            operationKind: RunAuthorityOperationKind.SourceApply,
            eligibilityDecision: StatusMapperTestFixtures.EligibleDecision(RunAuthorityOperationKind.SourceApply),
            evidenceRefs: ["patch-package:g03", "validation-result:g03"]));

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        StatusMapperTestFixtures.AssertContains(status.BlockedReasons, "ProposalOnlyDoesNotAllowDurableMutation");
        StatusMapperTestFixtures.AssertContains(status.BlockedReasons, "ProposalOnlyOperationBlocked:SourceApply");
        StatusMapperTestFixtures.AssertContains(status.MissingEvidence, "bounded-run-authority-grant");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, "do not apply source under ProposalOnly");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, "do not continue workflow from ProposalOnly status");
    }

    [TestMethod]
    public void AskBeforeMutationRequiresExplicitApplyApprovalBeforeSourceApplyStatusEligibility()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusMapperTestFixtures.AuthorityProfileRequest(
            profileKind: AuthorityProfileKind.AskBeforeMutation,
            operationKind: RunAuthorityOperationKind.SourceApply,
            eligibilityDecision: StatusMapperTestFixtures.EligibleDecision(RunAuthorityOperationKind.SourceApply),
            evidenceRefs: ["patch-package:g03", "validation-result:g03"]));

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        StatusMapperTestFixtures.AssertContains(status.BlockedReasons, "MutationRequiresExplicitHumanApproval");
        StatusMapperTestFixtures.AssertContains(status.MissingEvidence, "accepted-apply-approval");
        StatusMapperTestFixtures.AssertContains(status.MissingEvidence, "accepted-source-apply-request");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, "do not apply source from patch readiness alone");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, "do not treat validation passed as approval");
    }

    [TestMethod]
    public void BoundedRunAuthorityEligibleStatusRemainsNonExecutable()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusMapperTestFixtures.AuthorityProfileRequest(
            profileKind: AuthorityProfileKind.BoundedRunAuthority,
            operationKind: RunAuthorityOperationKind.SourceApply,
            eligibilityDecision: StatusMapperTestFixtures.EligibleDecision(RunAuthorityOperationKind.SourceApply)));

        Assert.AreEqual(GovernedOperationState.Eligible, status.State);
        Assert.AreEqual(0, status.BlockedReasons.Count);
        StatusMapperTestFixtures.AssertContains(status.EvidenceRefs, "bounded-run-authority-grant:g03");
        StatusMapperTestFixtures.AssertContains(status.EvidenceRefs, "operation-eligibility-decision:g03");
        StatusMapperTestFixtures.AssertContains(status.NextSafeActions, "request controlled executor review for independent authority re-check");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotExecuteFromStatusAlone);
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, AuthorityGlossary.DoNotTreatEligibleStatusAsApproval);
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree);
    }

    [TestMethod]
    public void ExpiredGrantBeatsOtherwiseEligibleDecision()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusMapperTestFixtures.AuthorityProfileRequest(
            profileKind: AuthorityProfileKind.BoundedRunAuthority,
            operationKind: RunAuthorityOperationKind.SourceApply,
            eligibilityDecision: StatusMapperTestFixtures.EligibleDecision(RunAuthorityOperationKind.SourceApply),
            grantExpiresAtUtc: StatusMapperTestFixtures.ObservedAtUtc));

        Assert.AreEqual(GovernedOperationState.Expired, status.State);
        StatusMapperTestFixtures.AssertContains(status.BlockedReasons, "BoundedRunGrantExpired");
        StatusMapperTestFixtures.AssertContains(status.MissingEvidence, "fresh bounded run authority grant");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, "do not use expired grant");
    }

    [TestMethod]
    public void BlockedEligibilityDecisionCarriesMissingEvidenceWithoutExecuting()
    {
        var status = AuthorityProfileStatusMapper.Map(StatusMapperTestFixtures.AuthorityProfileRequest(
            profileKind: AuthorityProfileKind.BoundedRunAuthority,
            operationKind: RunAuthorityOperationKind.Push,
            eligibilityDecision: StatusMapperTestFixtures.BlockedDecision(
                RunAuthorityOperationKind.Push,
                ["PushRemoteHeadStale"],
                ["fresh-remote-head-observation:g03"])));

        Assert.AreEqual(GovernedOperationState.Blocked, status.State);
        StatusMapperTestFixtures.AssertContains(status.BlockedReasons, "OperationEligibilityDecisionBlocked");
        StatusMapperTestFixtures.AssertContains(status.BlockedReasons, "PushRemoteHeadStale");
        StatusMapperTestFixtures.AssertContains(status.MissingEvidence, "bounded grant allowing Push for this repo/branch/run/scope");
        StatusMapperTestFixtures.AssertContains(status.NextSafeActions, "request separate push authority after source apply evidence exists");
        StatusMapperTestFixtures.AssertContains(status.ForbiddenActions, "do not push without explicit bounded authority");
    }
}

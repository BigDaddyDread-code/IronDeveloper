using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class RepeatedFailurePatternReviewAuthorityBoundaryTests
{
    private readonly IRepeatedFailurePatternReviewCandidateWorkflow _workflow = new RepeatedFailurePatternReviewCandidateWorkflow();

    [DataTestMethod]
    [DataRow("pattern proof")]
    [DataRow("root cause proof")]
    [DataRow("history query")]
    [DataRow("memory query")]
    [DataRow("log read")]
    [DataRow("report read")]
    [DataRow("test run")]
    [DataRow("command run")]
    [DataRow("tool invocation")]
    [DataRow("agent dispatch")]
    [DataRow("ticket creation")]
    [DataRow("incident creation")]
    [DataRow("memory promotion")]
    [DataRow("retrieval activation")]
    [DataRow("workflow transition")]
    [DataRow("source mutation")]
    public void RepeatedFailurePatternReview_ResultIsNotAuthorityEvidence(string scenario)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest());

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status, scenario);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(RepeatedFailureReviewGateKind.HumanReviewRequired)]
    [DataRow(RepeatedFailureReviewGateKind.EvidenceRequired)]
    [DataRow(RepeatedFailureReviewGateKind.SourceOfTruthRequired)]
    [DataRow(RepeatedFailureReviewGateKind.PatternProofNotClaimed)]
    [DataRow(RepeatedFailureReviewGateKind.RootCauseProofNotClaimed)]
    [DataRow(RepeatedFailureReviewGateKind.TicketCreationForbidden)]
    [DataRow(RepeatedFailureReviewGateKind.MemoryPromotionForbidden)]
    [DataRow(RepeatedFailureReviewGateKind.WorkflowContinuationForbidden)]
    public void RepeatedFailurePatternReview_GateHintDoesNotSatisfyGate(RepeatedFailureReviewGateKind gateKind)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            GateHints = [RepeatedFailurePatternReviewFixtures.Gate(gateKind)]
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.AreEqual(gateKind, result.GateHints.Single().Kind);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(WorkflowStepRunnerEligibility.BlockedApprovalRequired)]
    [DataRow(WorkflowStepRunnerEligibility.BlockedByBoundary)]
    [DataRow(WorkflowStepRunnerEligibility.BlockedMissingEvidence)]
    [DataRow(WorkflowStepRunnerEligibility.InvalidContract)]
    public void RepeatedFailurePatternReview_CannotBypassRunnerBlocks(WorkflowStepRunnerEligibility eligibility)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            StepEvaluation = RepeatedFailurePatternReviewFixtures.StepEvaluation(eligibility)
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.BlockedByRunnerEvaluation);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt)]
    [DataRow(WorkflowDryRunStatus.BlockedByPolicyPreflight)]
    [DataRow(WorkflowDryRunStatus.BlockedByA2aValidation)]
    [DataRow(WorkflowDryRunStatus.BlockedByMissingEvidence)]
    [DataRow(WorkflowDryRunStatus.BlockedByStepValidation)]
    public void RepeatedFailurePatternReview_CannotBypassDryRunBlocks(WorkflowDryRunStatus status)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            DryRunResult = RepeatedFailurePatternReviewFixtures.DryRun(status)
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.BlockedByDryRun);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(BoxedLangGraphRouteLabel.BlockedApprovalRequired)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedPolicyPreflight)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedA2aValidation)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedMissingEvidence)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedInvalidStep)]
    [DataRow(BoxedLangGraphRouteLabel.NoRouteSuggested)]
    public void RepeatedFailurePatternReview_CannotBypassRouteBlocks(BoxedLangGraphRouteLabel label)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            RouteSuggestion = RepeatedFailurePatternReviewFixtures.Route(label)
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.BlockedByRouteSuggestion);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void RepeatedFailurePatternReview_AuthorityClaimingRouteIsRejectedEvenWhenLabelLooksEligible()
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            RouteSuggestion = RepeatedFailurePatternReviewFixtures.Route(BoxedLangGraphRouteLabel.EligibleForDryRun, authority: true)
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.BlockedByRouteSuggestion);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedAssertionFailure)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedBuildFailure)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedTimeoutOrHang)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedEnvironmentOrDependencyFailure)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedFixtureOrDataFailure)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedPolicyOrApprovalBlock)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedWorkflowGateBlock)]
    [DataRow(RepeatedFailurePatternCategoryHint.MixedOrUnclearPattern)]
    public void RepeatedFailurePatternReview_CategoryHintDoesNotProvePattern(RepeatedFailurePatternCategoryHint category)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with { CategoryHint = category });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.AreEqual(category, result.CategoryHint);
        Assert.IsFalse(result.IsPatternProof);
        Assert.IsFalse(result.IsRootCauseProof);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.PatternNotProven);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.RootCauseNotProven);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void RepeatedFailurePatternReview_CannotCreateTicketIncidentMemoryPromotionOrWorkflowContinuation()
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest());

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanCreateIncident);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanTransitionWorkflow);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }
}

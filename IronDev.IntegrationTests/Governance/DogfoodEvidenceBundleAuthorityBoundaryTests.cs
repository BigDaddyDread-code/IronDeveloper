using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class DogfoodEvidenceBundleAuthorityBoundaryTests
{
    private readonly IDogfoodEvidenceBundleCandidateWorkflow _workflow = new DogfoodEvidenceBundleCandidateWorkflow();

    [DataTestMethod]
    [DataRow("validation proof")]
    [DataRow("release readiness")]
    [DataRow("approval evidence")]
    [DataRow("policy evidence")]
    [DataRow("workflow transition evidence")]
    [DataRow("source mutation evidence")]
    [DataRow("patch apply evidence")]
    [DataRow("tool execution evidence")]
    [DataRow("command execution evidence")]
    [DataRow("memory promotion evidence")]
    [DataRow("retrieval activation evidence")]
    [DataRow("sql write evidence")]
    [DataRow("dogfood run output")]
    public void DogfoodEvidenceBundle_ResultIsNotAuthorityEvidence(string scenario)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest());

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status, scenario);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedPassed)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedFailed)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedBlocked)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedNotRun)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedPartial)]
    public void DogfoodEvidenceBundle_ValidationOutcomeHintIsMetadataOnly(DogfoodValidationOutcomeHint outcomeHint)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            ValidationReferences =
            [
                DogfoodEvidenceBundleFixtures.Validation(outcomeHint: outcomeHint)
            ]
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status);
        Assert.AreEqual(outcomeHint, result.ValidationReferences.Single().OutcomeHint);
        Assert.IsFalse(result.IsValidationProof);
        Assert.IsFalse(result.IsReleaseReady);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.ValidationNotProven);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.ReleaseReadinessNotClaimed);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(WorkflowStepRunnerEligibility.BlockedApprovalRequired, "approval halt")]
    [DataRow(WorkflowStepRunnerEligibility.BlockedByBoundary, "policy block")]
    [DataRow(WorkflowStepRunnerEligibility.BlockedMissingEvidence, "missing evidence")]
    [DataRow(WorkflowStepRunnerEligibility.InvalidContract, "a2a or invalid contract")]
    public void DogfoodEvidenceBundle_CannotBypassRunnerBlocks(WorkflowStepRunnerEligibility eligibility, string scenario)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            StepEvaluation = DogfoodEvidenceBundleFixtures.StepEvaluation(eligibility)
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate, result.Status, scenario);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.BlockedByRunnerEvaluation);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt)]
    [DataRow(WorkflowDryRunStatus.BlockedByPolicyPreflight)]
    [DataRow(WorkflowDryRunStatus.BlockedByA2aValidation)]
    [DataRow(WorkflowDryRunStatus.BlockedByMissingEvidence)]
    [DataRow(WorkflowDryRunStatus.BlockedByStepValidation)]
    public void DogfoodEvidenceBundle_CannotBypassDryRunBlocks(WorkflowDryRunStatus status)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            DryRunResult = DogfoodEvidenceBundleFixtures.DryRun(status)
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.BlockedByDryRun);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(BoxedLangGraphRouteLabel.BlockedApprovalRequired)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedPolicyPreflight)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedA2aValidation)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedMissingEvidence)]
    [DataRow(BoxedLangGraphRouteLabel.BlockedInvalidStep)]
    [DataRow(BoxedLangGraphRouteLabel.NoRouteSuggested)]
    public void DogfoodEvidenceBundle_CannotBypassRouteBlocks(BoxedLangGraphRouteLabel label)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            RouteSuggestion = DogfoodEvidenceBundleFixtures.Route(label)
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.BlockedByRouteSuggestion);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void DogfoodEvidenceBundle_AuthorityClaimingRouteIsRejectedEvenWhenLabelLooksEligible()
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            RouteSuggestion = DogfoodEvidenceBundleFixtures.Route(BoxedLangGraphRouteLabel.EligibleForDryRun, authority: true)
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.BlockedByRouteSuggestion);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(DogfoodEvidenceGateKind.HumanReviewRequired)]
    [DataRow(DogfoodEvidenceGateKind.ApprovalRequired)]
    [DataRow(DogfoodEvidenceGateKind.PolicyEvidenceRequired)]
    [DataRow(DogfoodEvidenceGateKind.A2aValidationRequired)]
    [DataRow(DogfoodEvidenceGateKind.ThoughtLedgerReferenceRequired)]
    [DataRow(DogfoodEvidenceGateKind.DryRunRequired)]
    [DataRow(DogfoodEvidenceGateKind.ValidationEvidenceRequired)]
    [DataRow(DogfoodEvidenceGateKind.SourceMutationForbidden)]
    [DataRow(DogfoodEvidenceGateKind.ToolExecutionForbidden)]
    [DataRow(DogfoodEvidenceGateKind.ReleaseReadinessNotClaimed)]
    public void DogfoodEvidenceBundle_GateHintDoesNotSatisfyGate(DogfoodEvidenceGateKind gateKind)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            GateHints = [DogfoodEvidenceBundleFixtures.Gate(gateKind)]
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status);
        Assert.AreEqual(gateKind, result.GateHints.Single().Kind);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("source apply")]
    [DataRow("tool execution")]
    [DataRow("release approval")]
    [DataRow("memory promotion")]
    [DataRow("retrieval activation")]
    public void DogfoodEvidenceBundle_ReviewScenarioStillCannotAct(string scenario)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest());

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status, scenario);
        Assert.IsFalse(result.CanMutateSource, scenario);
        Assert.IsFalse(result.CanApplyPatch, scenario);
        Assert.IsFalse(result.CanInvokeTool, scenario);
        Assert.IsFalse(result.CanPromoteMemory, scenario);
        Assert.IsFalse(result.CanActivateRetrieval, scenario);
        Assert.IsFalse(result.IsReleaseReady, scenario);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void DogfoodEvidenceBundle_CannotSatisfyApprovalPolicyA2aOrRunnerEvidence()
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest());

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }
}

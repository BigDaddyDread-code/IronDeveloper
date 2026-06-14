using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowApprovalHalt")]
public sealed class WorkflowApprovalHaltPolicyA2aInteractionTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();

    [TestMethod]
    public void WorkflowApprovalHalt_PolicyPreflightBlocksBeforeApprovalHaltEvaluation()
    {
        var result = _runner.Evaluate(RunnerRequest(
            approvalHaltRequests:
            [
                WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")])
            ],
            policyPreflightRequests:
            [
                PolicyPreflight([])
            ],
            a2aHandoffRequests: []));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedByBoundary, step.Eligibility);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, step.PolicyPreflightStatus);
        Assert.IsNull(step.ApprovalHaltStatus);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_A2aValidationBlocksBeforeApprovalHaltEvaluation()
    {
        var result = _runner.Evaluate(RunnerRequest(
            approvalHaltRequests:
            [
                WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")])
            ],
            policyPreflightRequests: [],
            a2aHandoffRequests:
            [
                WorkflowA2aHandoffValidatorTests.ValidRequest() with
                {
                    HandoffReference = WorkflowA2aHandoffValidatorTests.ValidHandoff() with { WorkflowStepId = "other-step" }
                }
            ]));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedByBoundary, step.Eligibility);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, step.A2aHandoffValidationStatus);
        Assert.IsNull(step.ApprovalHaltStatus);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_ApprovalEvidenceDoesNotSatisfyPolicyPreflight()
    {
        var result = _runner.Evaluate(RunnerRequest(
            approvalHaltRequests:
            [
                WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")])
            ],
            policyPreflightRequests:
            [
                PolicyPreflight([])
            ],
            a2aHandoffRequests: []));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, step.PolicyPreflightStatus);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.PolicyPreflightMissingEvidence);
        Assert.IsNull(step.ApprovalHaltStatus);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_ValidPolicyAndA2aStillDoNotTurnApprovalEvidenceIntoApproval()
    {
        var result = _runner.Evaluate(RunnerRequest(
            approvalHaltRequests:
            [
                WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")])
            ],
            policyPreflightRequests:
            [
                PolicyPreflight([PolicyEvidence(WorkflowStepPolicyRequirementKind.HumanApprovalReference, "human-approval-001")])
            ],
            a2aHandoffRequests:
            [
                WorkflowA2aHandoffValidatorTests.ValidRequest()
            ]));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, step.Eligibility);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, step.PolicyPreflightStatus);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, step.A2aHandoffValidationStatus);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution, step.ApprovalHaltStatus);
        CollectionAssert.Contains(step.ApprovalHaltReasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval);
    }

    private static WorkflowRunnerEvaluationRequest RunnerRequest(
        IReadOnlyList<WorkflowApprovalHaltEvaluationRequest> approvalHaltRequests,
        IReadOnlyList<WorkflowStepPolicyPreflightRequest> policyPreflightRequests,
        IReadOnlyList<WorkflowA2aHandoffValidationRequest> a2aHandoffRequests) =>
        new()
        {
            WorkflowRunId = "workflow-run-001",
            StepContracts = [WorkflowA2aHandoffValidatorTests.ValidStep()],
            AvailableEvidence =
            [
                new()
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference,
                    ReferenceId = "governance-event-001"
                },
                new()
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference,
                    ReferenceId = "handoff-reference-001"
                }
            ],
            PolicyPreflightRequests = policyPreflightRequests,
            A2aHandoffValidationRequests = a2aHandoffRequests,
            ApprovalHaltRequests = approvalHaltRequests
        };

    private static WorkflowStepPolicyPreflightRequest PolicyPreflight(IReadOnlyList<WorkflowStepPolicyEvidenceReference> evidence) =>
        new()
        {
            StepContract = WorkflowA2aHandoffValidatorTests.ValidStep(),
            SensitivityKind = WorkflowStepSensitivityKind.ApprovalRequiredAction,
            RequiredPolicyReferences =
            [
                new()
                {
                    Kind = WorkflowStepPolicyRequirementKind.HumanApprovalReference,
                    ReferenceId = "human-approval-001",
                    ProjectId = "project-001",
                    CorrelationId = "policy-preflight-001"
                }
            ],
            AvailablePolicyEvidence = evidence
        };

    private static WorkflowStepPolicyEvidenceReference PolicyEvidence(WorkflowStepPolicyRequirementKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            ProjectId = "project-001",
            CorrelationId = "policy-preflight-001"
        };
}

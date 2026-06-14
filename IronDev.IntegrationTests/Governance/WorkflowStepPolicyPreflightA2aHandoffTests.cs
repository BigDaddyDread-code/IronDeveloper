using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStepPolicyPreflightA2a")]
public sealed class WorkflowStepPolicyPreflightA2aHandoffTests
{
    private readonly WorkflowStepPolicyPreflightChecker _checker = new();
    private readonly WorkflowA2aHandoffValidator _a2aValidator = new();

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_HandoffSensitivityRequiresA2aHandoffValidationReference()
    {
        var result = _checker.Check(Request(
            WorkflowStepSensitivityKind.A2aHandoff,
            [Requirement(WorkflowStepPolicyRequirementKind.GovernanceEventReference, "governance-event-001")],
            [Evidence(WorkflowStepPolicyRequirementKind.GovernanceEventReference, "governance-event-001")]));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingRequiredPolicyReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_ValidationReferenceDoesNotSatisfyHumanApproval()
    {
        AssertA2aEvidenceDoesNotSatisfy(
            WorkflowStepSensitivityKind.ApprovalRequiredAction,
            WorkflowStepPolicyRequirementKind.HumanApprovalReference,
            "human-approval-001");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_ValidationReferenceDoesNotSatisfyToolGate()
    {
        AssertA2aEvidenceDoesNotSatisfy(
            WorkflowStepSensitivityKind.ToolInvocation,
            WorkflowStepPolicyRequirementKind.ToolGateReference,
            "tool-gate-001");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_ValidationReferenceDoesNotSatisfyMemoryPromotionApproval()
    {
        AssertA2aEvidenceDoesNotSatisfy(
            WorkflowStepSensitivityKind.MemoryPromotion,
            WorkflowStepPolicyRequirementKind.MemoryPromotionApprovalReference,
            "memory-promotion-approval-001");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_ValidationReferenceDoesNotSatisfyRetrievalApproval()
    {
        AssertA2aEvidenceDoesNotSatisfy(
            WorkflowStepSensitivityKind.RetrievalActivation,
            WorkflowStepPolicyRequirementKind.RetrievalApprovalReference,
            "retrieval-approval-001");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_PolicyPreflightCannotCompensateForInvalidA2aStructure()
    {
        var result = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest() with
        {
            HandoffReference = WorkflowA2aHandoffValidatorTests.ValidHandoff() with { WorkflowRunId = "other-run" },
            AvailableEvidence = WorkflowA2aHandoffValidatorTests.ValidEvidence()
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowA2aHandoffBlockReason.WorkflowRunMismatch);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflightA2a_A2aValidationCannotCompensateForMissingPolicyEvidence()
    {
        var a2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest());
        var policy = _checker.Check(Request(
            WorkflowStepSensitivityKind.A2aHandoff,
            [Requirement(WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference, "policy-a2a-validation-001")],
            []));

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, a2a.Status);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
        CollectionAssert.Contains(policy.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingPolicyEvidence);
    }

    private void AssertA2aEvidenceDoesNotSatisfy(
        WorkflowStepSensitivityKind sensitivity,
        WorkflowStepPolicyRequirementKind requiredKind,
        string requiredReferenceId)
    {
        var result = _checker.Check(Request(
            sensitivity,
            [Requirement(requiredKind, requiredReferenceId)],
            [Evidence(WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference, "a2a-validation-001")]));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingPolicyEvidence);
    }

    private static WorkflowStepPolicyPreflightRequest Request(
        WorkflowStepSensitivityKind sensitivity,
        IReadOnlyList<WorkflowStepPolicyRequirement> requirements,
        IReadOnlyList<WorkflowStepPolicyEvidenceReference> evidence) =>
        new()
        {
            StepContract = WorkflowA2aHandoffValidatorTests.ValidStep(),
            SensitivityKind = sensitivity,
            RequiredPolicyReferences = requirements,
            AvailablePolicyEvidence = evidence
        };

    private static WorkflowStepPolicyRequirement Requirement(WorkflowStepPolicyRequirementKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            ProjectId = "project-001",
            CorrelationId = "policy-preflight-001"
        };

    private static WorkflowStepPolicyEvidenceReference Evidence(WorkflowStepPolicyRequirementKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            ProjectId = "project-001",
            CorrelationId = "policy-preflight-001"
        };
}

using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ControlledApplyPlanAuthorityBoundaryTests
{
    [TestMethod]
    public void ControlledApplyPlan_CannotExecuteOrMutateAnything()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        ControlledApplyPlanTests.AssertNoAuthority(result);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.ExecutionNotImplemented);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.SourceApplyNotImplemented);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.PatchApplicationNotImplemented);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.RollbackNotImplemented);
    }

    [TestMethod]
    public void ControlledApplyPlan_DoesNotSatisfyApprovalOrPolicy()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.ApprovalNotSatisfied);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.PolicyNotSatisfied);
        ControlledApplyPlanTests.AssertContains(result.PlanSummaries, "Approval was not satisfied.");
        ControlledApplyPlanTests.AssertContains(result.PlanSummaries, "Policy was not satisfied.");
    }

    [TestMethod]
    public void ControlledApplyPlan_DoesNotTransitionWorkflow()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.IsFalse(result.CanTransitionWorkflow);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.WorkflowNotTransitioned);
        ControlledApplyPlanTests.AssertContains(result.PlanSummaries, "Workflow was not transitioned.");
    }

    [TestMethod]
    public void ApplyPlaceholder_IsNotExecutable()
    {
        var request = ControlledApplyPlanTests.ValidRequest() with
        {
            PlanPhases =
            [
                ControlledApplyPlanTests.ValidPhase() with
                {
                    Kind = ControlledApplyPlanPhaseKind.ApplyStepPlaceholder
                }
            ]
        };

        var result = new ControlledApplyPlanWorkflow().Prepare(request);

        Assert.AreEqual(ControlledApplyPlanStatus.ControlledApplyPlanPrepared, result.Status);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.CanApplyPatch);
        ControlledApplyPlanTests.AssertContains(result.PlanSummaries, "Apply placeholders are not executable.");
    }

    [TestMethod]
    public void ValidationReference_IsNotValidationExecution()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.IsFalse(result.CanRunValidation);
        ControlledApplyPlanTests.AssertContains(result.PlanSummaries, "Validation references are not validation execution.");
    }

    [TestMethod]
    public void RollbackNote_IsNotRollbackExecution()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.IsFalse(result.CanRollback);
        ControlledApplyPlanTests.AssertContains(result.PlanSummaries, "Rollback notes are not rollback execution.");
    }

    [TestMethod]
    public void SourceApplyApprovalRequirement_IsNotApprovalSatisfaction()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.AreEqual(ControlledApplyPlanStatus.ControlledApplyPlanPrepared, result.Status);
        Assert.IsFalse(result.CanSatisfyApproval);
        ControlledApplyPlanTests.AssertContains(result.Reasons, ControlledApplyPlanReason.ApprovalNotSatisfied);
    }

    [TestMethod]
    public void PatchProposalEvidencePackage_IsNotPatchPayload()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(ControlledApplyPlanStatus.ControlledApplyPlanPrepared, result.Status);
        Assert.IsFalse(result.CanApplyPatch);
        ControlledApplyPlanTests.AssertDoesNotContain(json, "patch payload");
    }

    [TestMethod]
    public void EvidenceCannotClaimApprovalExecutionOrMemoryPromotion()
    {
        var request = ControlledApplyPlanTests.ValidRequest() with
        {
            EvidenceReferences = [ControlledApplyPlanTests.ValidEvidence() with { ClaimsApproval = true }]
        };

        var result = new ControlledApplyPlanWorkflow().Prepare(request);

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        ControlledApplyPlanTests.AssertIssue(result, ControlledApplyPlanReason.EvidenceReferenceClaimsAuthority);
        ControlledApplyPlanTests.AssertNoAuthority(result);
    }

    [TestMethod]
    public void GateHintCannotGrantFutureAction()
    {
        var request = ControlledApplyPlanTests.ValidRequest() with
        {
            GateHints = [ControlledApplyPlanTests.ValidGateHint() with { AllowsPatchApplication = true }]
        };

        var result = new ControlledApplyPlanWorkflow().Prepare(request);

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        ControlledApplyPlanTests.AssertIssue(result, ControlledApplyPlanReason.GateHintClaimsAuthority);
        ControlledApplyPlanTests.AssertNoAuthority(result);
    }

    [TestMethod]
    public void PlanReferenceId_IsNotDurableAuthority()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ControlledApplyPlanTests.ValidRequest());

        Assert.AreEqual("controlled-apply-plan-139", result.ControlledApplyPlanReferenceId);
        Assert.IsTrue(result.IsPlanOnly);
        ControlledApplyPlanTests.AssertNoAuthority(result);
    }
}

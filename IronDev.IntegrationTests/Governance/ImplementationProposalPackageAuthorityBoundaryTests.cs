using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ImplementationProposalPackageAuthorityBoundaryTests
{
    [TestMethod]
    public void ImplementationProposalPackage_ResultIsNotApprovalPolicyTransitionDryRunA2aOrPromotionEvidence()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();

        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotSatisfyApprovalHalt()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();
        var evaluator = new WorkflowApprovalHaltEvaluator();

        var state = evaluator.Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = "workflow-step-implementation-proposal-package",
            RequiredApprovals =
            [
                new WorkflowApprovalHaltRequirement
                {
                    Kind = WorkflowApprovalRequirementKind.HumanApprovalReference,
                    RequirementId = "human-approval-required-129",
                    SafeSummary = "Human approval is still required."
                }
            ],
            AvailableApprovalEvidence =
            [
                new WorkflowApprovalEvidenceReference
                {
                    Kind = WorkflowApprovalRequirementKind.GovernanceEventReference,
                    ReferenceId = result.ProposalPackageReferenceId,
                    SafeSummary = "Implementation proposal package only."
                }
            ]
        });

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, state.Status);
        CollectionAssert.Contains(state.Reasons.ToArray(), WorkflowApprovalHaltReason.MissingApprovalEvidence);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotSatisfyPolicyPreflight()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();
        var checker = new WorkflowStepPolicyPreflightChecker();

        var policy = checker.Check(new WorkflowStepPolicyPreflightRequest
        {
            StepContract = TestFailureReviewCandidateFixtures.StepContract(),
            SensitivityKind = WorkflowStepSensitivityKind.SourceMutation,
            RequiredPolicyReferences =
            [
                new WorkflowStepPolicyRequirement
                {
                    Kind = WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference,
                    ReferenceId = "source-mutation-approval-129"
                }
            ],
            AvailablePolicyEvidence =
            [
                new WorkflowStepPolicyEvidenceReference
                {
                    Kind = WorkflowStepPolicyRequirementKind.ApprovalPolicyReference,
                    ReferenceId = result.ProposalPackageReferenceId
                }
            ]
        });

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotSatisfyA2aValidation()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();
        var validator = new WorkflowA2aHandoffValidator();
        var step = TestFailureReviewCandidateFixtures.StepContract(requirementId: "handoff-record-129", requirementKind: WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference);

        var validation = validator.Validate(new WorkflowA2aHandoffValidationRequest
        {
            StepContract = step,
            HandoffReference = new WorkflowA2aHandoffReference
            {
                HandoffReferenceId = "handoff-record-129",
                WorkflowRunId = step.WorkflowRunId,
                WorkflowStepId = step.StepContractId,
                Sender = new WorkflowA2aParticipantReference
                {
                    Kind = WorkflowA2aParticipantKind.SystemRecorder,
                    ReferenceId = "system-recorder",
                    SafeLabel = "System recorder"
                },
                Receiver = new WorkflowA2aParticipantReference
                {
                    Kind = WorkflowA2aParticipantKind.Agent,
                    ReferenceId = "implementation-agent",
                    SafeLabel = "Implementation agent label only"
                },
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                CorrelationId = "correlation-129",
                SafeSummary = "Handoff validation snapshot only."
            },
            AvailableEvidence =
            [
                new WorkflowA2aHandoffEvidenceReference
                {
                    Kind = WorkflowA2aHandoffEvidenceKind.ReviewMaterialReference,
                    ReferenceId = result.ProposalPackageReferenceId
                }
            ]
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, validation.Status);
        Assert.IsTrue(validation.MissingEvidence.Count > 0);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotSatisfyRunnerEvidence()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();
        var runner = new WorkflowRunnerSkeleton();
        var step = TestFailureReviewCandidateFixtures.StepContract(requirementId: "validation-evidence-129");

        var evaluation = runner.Evaluate(new WorkflowRunnerEvaluationRequest
        {
            WorkflowRunId = step.WorkflowRunId,
            StepContracts = [step],
            AvailableEvidence =
            [
                new WorkflowEvidenceReference
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.ReviewMaterialReference,
                    ReferenceId = result.ProposalPackageReferenceId
                }
            ]
        });

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedMissingEvidence, evaluation.StepEvaluations[0].Eligibility);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotBypassApprovalPolicyA2aOrMissingEvidence()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();

        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotBeUsedAsSourceApplyTicketMemoryRetrievalOrRouteAuthority()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();
        var route = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable);

        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsTrue(route.IsAdvisoryOnly);
        Assert.IsFalse(route.WorkflowDecisionAuthority);
        Assert.IsFalse(route.ToolUseAllowed);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultCannotBeTreatedAsCodePatchApplyOrImplementationCompletion()
    {
        var result = ImplementationProposalPackageFixtures.ValidResult();

        Assert.IsTrue(result.IsProposalOnly);
        Assert.IsFalse(result.IsImplementation);
        Assert.IsFalse(result.IsPatch);
        Assert.IsFalse(result.CanGenerateCode);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanRunTests);
    }
}

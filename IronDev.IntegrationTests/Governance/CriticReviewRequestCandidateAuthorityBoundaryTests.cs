using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class CriticReviewRequestCandidateAuthorityBoundaryTests
{
    [TestMethod]
    public void CriticReviewRequestCandidate_ResultIsNotApprovalPolicyTransitionDryRunA2aOrPromotionEvidence()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();

        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultCannotSatisfyApprovalHalt()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();
        var evaluator = new WorkflowApprovalHaltEvaluator();

        var state = evaluator.Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = "workflow-step-critic-review-request",
            RequiredApprovals =
            [
                new WorkflowApprovalHaltRequirement
                {
                    Kind = WorkflowApprovalRequirementKind.HumanApprovalReference,
                    RequirementId = "human-approval-required-128",
                    SafeSummary = "Human approval is still required."
                }
            ],
            AvailableApprovalEvidence =
            [
                new WorkflowApprovalEvidenceReference
                {
                    Kind = WorkflowApprovalRequirementKind.GovernanceEventReference,
                    ReferenceId = result.ReviewPackageReferenceId,
                    SafeSummary = "Critic review request package only."
                }
            ]
        });

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, state.Status);
        CollectionAssert.Contains(state.Reasons.ToArray(), WorkflowApprovalHaltReason.MissingApprovalEvidence);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultCannotSatisfyPolicyPreflight()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();
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
                    ReferenceId = "source-mutation-approval-128"
                }
            ],
            AvailablePolicyEvidence =
            [
                new WorkflowStepPolicyEvidenceReference
                {
                    Kind = WorkflowStepPolicyRequirementKind.ApprovalPolicyReference,
                    ReferenceId = result.ReviewPackageReferenceId
                }
            ]
        });

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultCannotSatisfyA2aValidation()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();
        var validator = new WorkflowA2aHandoffValidator();
        var step = TestFailureReviewCandidateFixtures.StepContract(requirementId: "handoff-record-128", requirementKind: WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference);

        var validation = validator.Validate(new WorkflowA2aHandoffValidationRequest
        {
            StepContract = step,
            HandoffReference = new WorkflowA2aHandoffReference
            {
                HandoffReferenceId = "handoff-record-128",
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
                    ReferenceId = "critic-agent",
                    SafeLabel = "Critic agent label only"
                },
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                CorrelationId = "correlation-128",
                SafeSummary = "Handoff validation snapshot only."
            },
            AvailableEvidence =
            [
                new WorkflowA2aHandoffEvidenceReference
                {
                    Kind = WorkflowA2aHandoffEvidenceKind.ReviewMaterialReference,
                    ReferenceId = result.ReviewPackageReferenceId
                }
            ]
        });

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, validation.Status);
        Assert.IsTrue(validation.MissingEvidence.Count > 0);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultCannotSatisfyRunnerEvidence()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();
        var runner = new WorkflowRunnerSkeleton();
        var step = TestFailureReviewCandidateFixtures.StepContract(requirementId: "validation-evidence-128");

        var evaluation = runner.Evaluate(new WorkflowRunnerEvaluationRequest
        {
            WorkflowRunId = step.WorkflowRunId,
            StepContracts = [step],
            AvailableEvidence =
            [
                new WorkflowEvidenceReference
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.ReviewMaterialReference,
                    ReferenceId = result.ReviewPackageReferenceId
                }
            ]
        });

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedMissingEvidence, evaluation.StepEvaluations[0].Eligibility);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultCannotBypassApprovalPolicyA2aOrMissingEvidence()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();

        Assert.IsFalse(result.CanApprove);
        Assert.IsFalse(result.CanReject);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanDispatchCriticAgent);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultCannotBeUsedAsSourceApplyTicketMemoryRetrievalOrRouteAuthority()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();
        var route = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable);

        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsTrue(route.IsAdvisoryOnly);
        Assert.IsFalse(route.WorkflowDecisionAuthority);
        Assert.IsFalse(route.ToolUseAllowed);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_OutputCannotTurnBlockingSnapshotsIntoAllowedSnapshots()
    {
        var candidate = CriticReviewRequestCandidateFixtures.ValidResult();
        var request = CriticReviewRequestCandidateFixtures.ValidRequest() with
        {
            StepEvaluation = TestFailureReviewCandidateFixtures.BlockedEvaluation(),
            EvidenceReferences = [CriticReviewRequestCandidateFixtures.Evidence(referenceId: candidate.ReviewPackageReferenceId)]
        };

        var result = new CriticReviewRequestCandidateWorkflow().Prepare(request);

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultIsNotActualCriticAgentReviewModelReviewCommentOrDecision()
    {
        var result = CriticReviewRequestCandidateFixtures.ValidResult();

        Assert.IsTrue(result.IsReviewRequestOnly);
        Assert.IsFalse(result.IsReviewDecision);
        Assert.IsFalse(result.CanDispatchCriticAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanPostReviewComment);
        Assert.IsFalse(result.CanApprove);
        Assert.IsFalse(result.CanReject);
    }
}

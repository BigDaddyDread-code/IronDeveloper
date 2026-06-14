using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class TestFailureReviewCandidateAuthorityBoundaryTests
{
    [TestMethod]
    public void TestFailureReviewCandidate_ResultIsNotApprovalPolicyTransitionDryRunA2aOrPromotionEvidence()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();

        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ResultCannotSatisfyApprovalHalt()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();
        var evaluator = new WorkflowApprovalHaltEvaluator();

        var state = evaluator.Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = "workflow-step-test-failure-review",
            RequiredApprovals =
            [
                new WorkflowApprovalHaltRequirement
                {
                    Kind = WorkflowApprovalRequirementKind.HumanApprovalReference,
                    RequirementId = "human-approval-required-127",
                    SafeSummary = "Human approval is still required."
                }
            ],
            AvailableApprovalEvidence =
            [
                new WorkflowApprovalEvidenceReference
                {
                    Kind = WorkflowApprovalRequirementKind.GovernanceEventReference,
                    ReferenceId = result.ReviewPackageReferenceId,
                    SafeSummary = "Candidate review material only."
                }
            ]
        });

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, state.Status);
        CollectionAssert.Contains(state.Reasons.ToArray(), WorkflowApprovalHaltReason.MissingApprovalEvidence);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ResultCannotSatisfyPolicyPreflight()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();
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
                    ReferenceId = "source-mutation-approval-127"
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
    public void TestFailureReviewCandidate_ResultCannotSatisfyA2aValidation()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();
        var validator = new WorkflowA2aHandoffValidator();
        var step = TestFailureReviewCandidateFixtures.StepContract(requirementId: "handoff-record-127", requirementKind: WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference);

        var validation = validator.Validate(new WorkflowA2aHandoffValidationRequest
        {
            StepContract = step,
            HandoffReference = new WorkflowA2aHandoffReference
            {
                HandoffReferenceId = "handoff-record-127",
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
                    ReferenceId = "review-agent",
                    SafeLabel = "Review agent"
                },
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                CorrelationId = "correlation-127",
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
    public void TestFailureReviewCandidate_ResultCannotSatisfyRunnerEvidence()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();
        var runner = new WorkflowRunnerSkeleton();
        var step = TestFailureReviewCandidateFixtures.StepContract(requirementId: "validation-evidence-127");

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
    public void TestFailureReviewCandidate_ResultCannotBypassApprovalPolicyA2aOrMissingEvidence()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();

        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ResultCannotBeUsedAsSourceApplyTicketMemoryRetrievalOrRouteAuthority()
    {
        var result = TestFailureReviewCandidateFixtures.ValidResult();
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
    public void TestFailureReviewCandidate_OutputCannotTurnBlockingSnapshotsIntoAllowedSnapshots()
    {
        var candidate = TestFailureReviewCandidateFixtures.ValidResult();
        var request = TestFailureReviewCandidateFixtures.ValidRequest() with
        {
            StepEvaluation = TestFailureReviewCandidateFixtures.BlockedEvaluation(),
            SuppliedEvidenceReferences = [candidate.ReviewPackageReferenceId]
        };

        var result = new TestFailureReviewCandidateWorkflow().Review(request);

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }
}

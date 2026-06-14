using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowRunnerSkeletonA2a")]
public sealed class WorkflowRunnerSkeletonA2aHandoffTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_BlocksInvalidSuppliedA2aValidation()
    {
        var request = RunnerRequest(
            [WorkflowA2aHandoffValidatorTests.ValidRequest() with { HandoffReference = WorkflowA2aHandoffValidatorTests.ValidHandoff() with { WorkflowStepId = "other-step" } }]);

        var result = _runner.Evaluate(request);

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedByBoundary, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.A2aHandoffValidationInvalid);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, result.StepEvaluations[0].A2aHandoffValidationStatus);
        CollectionAssert.Contains(result.StepEvaluations[0].A2aHandoffBlockReasons.ToList(), WorkflowA2aHandoffBlockReason.WorkflowStepMismatch);
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_BlocksMissingA2aEvidenceWhenValidationSnapshotRequiresIt()
    {
        var request = RunnerRequest(
            [WorkflowA2aHandoffValidatorTests.ValidRequest() with
            {
                AvailableEvidence = WorkflowA2aHandoffValidatorTests.ValidEvidence()
                    .Where(evidence => evidence.Kind != WorkflowA2aHandoffEvidenceKind.HandoffContractReference)
                    .ToArray()
            }]);

        var result = _runner.Evaluate(request);

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedByBoundary, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.A2aHandoffValidationMissingEvidence);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, result.StepEvaluations[0].A2aHandoffValidationStatus);
        CollectionAssert.Contains(result.StepEvaluations[0].A2aHandoffBlockReasons.ToList(), WorkflowA2aHandoffBlockReason.MissingHandoffContractEvidence);
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_ValidA2aValidationIsFutureEligibleOnly()
    {
        var result = _runner.Evaluate(RunnerRequest([WorkflowA2aHandoffValidatorTests.ValidRequest()]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, result.StepEvaluations[0].A2aHandoffValidationStatus);
        Assert.AreEqual(0, result.StepEvaluations[0].A2aHandoffBlockReasons.Count);
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_DoesNotDispatchTransitionApproveSatisfyPolicyOrPromoteMemory()
    {
        var result = _runner.Evaluate(RunnerRequest([WorkflowA2aHandoffValidatorTests.ValidRequest()]));
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        AssertDoesNotContainAny(serialized, "Approved", "Authorized", "PolicySatisfied", "MemoryPromoted", "RetrievalActivated", "ReceiverMayAct", "HandoffSent");
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_OutputDoesNotSerializeRawHandoffPayload()
    {
        var result = _runner.Evaluate(RunnerRequest([WorkflowA2aHandoffValidatorTests.ValidRequest()]));
        var serialized = JsonSerializer.Serialize(result);

        AssertDoesNotContainAny(serialized, "raw prompt", "raw completion", "raw tool output", "private reasoning", "whole patch");
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_MissingSnapshotIsNotInferredAsBlockedInPr121()
    {
        var result = _runner.Evaluate(RunnerRequest([]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        Assert.IsNull(result.StepEvaluations[0].A2aHandoffValidationStatus);
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonA2a_RemainsEvaluationOnly()
    {
        var methods = typeof(IWorkflowRunnerSkeleton).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEqual(new[] { "Evaluate" }, methods);
    }

    private static WorkflowRunnerEvaluationRequest RunnerRequest(IReadOnlyList<WorkflowA2aHandoffValidationRequest> a2aRequests) =>
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
            A2aHandoffValidationRequests = a2aRequests
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

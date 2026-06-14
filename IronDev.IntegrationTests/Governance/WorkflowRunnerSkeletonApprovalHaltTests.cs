using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowRunnerSkeletonApprovalHalt")]
public sealed class WorkflowRunnerSkeletonApprovalHaltTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();

    [TestMethod]
    public void WorkflowRunnerSkeletonApprovalHalt_MissingApprovalEvidenceBlocksFutureEligibility()
    {
        var result = _runner.Evaluate(RunnerRequest([WorkflowApprovalHaltStateTests.Request([])]));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedApprovalRequired, step.Eligibility);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.ApprovalRequiredHalt);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.ApprovalEvidenceMissing);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, step.ApprovalHaltStatus);
        Assert.AreEqual(1, step.MissingApprovalRequirements.Count);
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonApprovalHalt_ApprovalEvidencePresentAllowsFutureEligibilityOnly()
    {
        var result = _runner.Evaluate(RunnerRequest(
            [WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")])]));
        var step = result.StepEvaluations[0];
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, step.Eligibility);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution, step.ApprovalHaltStatus);
        CollectionAssert.Contains(step.ApprovalHaltReasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval);
        AssertDoesNotContainAny(serialized, "Approved", "PolicySatisfied", "ExecutionAllowed", "WorkflowContinued", "SourceMutated", "MemoryPromoted");
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonApprovalHalt_InvalidApprovalRequirementDoesNotSerializeUnsafeMarker()
    {
        var result = _runner.Evaluate(RunnerRequest(
            [WorkflowApprovalHaltStateTests.Request([]) with
            {
                RequiredApprovals =
                [
                    WorkflowApprovalHaltStateTests.Requirement(WorkflowApprovalRequirementKind.HumanApprovalReference, "raw prompt approval")
                ]
            }]));
        var serialized = JsonSerializer.Serialize(result);
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedByBoundary, step.Eligibility);
        Assert.AreEqual(WorkflowApprovalHaltStatus.InvalidApprovalRequirement, step.ApprovalHaltStatus);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.ApprovalRequirementInvalid);
        Assert.AreEqual(0, step.MissingApprovalRequirements.Count);
        AssertDoesNotContainAny(serialized, "raw prompt", "rawPrompt");
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonApprovalHalt_InvalidStepContractSkipsUnsafeApprovalMaterial()
    {
        var result = _runner.Evaluate(RunnerRequest(
            [WorkflowApprovalHaltStateTests.Request([]) with
            {
                RequiredApprovals =
                [
                    WorkflowApprovalHaltStateTests.Requirement(WorkflowApprovalRequirementKind.HumanApprovalReference, "raw completion approval")
                ]
            }],
            WorkflowA2aHandoffValidatorTests.ValidStep() with { ThoughtLedgerReference = null }));
        var serialized = JsonSerializer.Serialize(result);
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.InvalidContract, step.Eligibility);
        Assert.IsNull(step.ApprovalHaltStatus);
        Assert.AreEqual(0, step.MissingApprovalRequirements.Count);
        AssertDoesNotContainAny(serialized, "raw completion", "rawCompletion");
    }

    [TestMethod]
    public void WorkflowRunnerSkeletonApprovalHalt_MissingSuppliedRequestIsNotInferredAsBlockedInPr122()
    {
        var result = _runner.Evaluate(RunnerRequest([]));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, step.Eligibility);
        Assert.IsNull(step.ApprovalHaltStatus);
    }

    private static WorkflowRunnerEvaluationRequest RunnerRequest(
        IReadOnlyList<WorkflowApprovalHaltEvaluationRequest> approvalHaltRequests,
        WorkflowStepContract? step = null) =>
        new()
        {
            WorkflowRunId = "workflow-run-001",
            StepContracts = [step ?? WorkflowA2aHandoffValidatorTests.ValidStep()],
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
            ApprovalHaltRequests = approvalHaltRequests
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

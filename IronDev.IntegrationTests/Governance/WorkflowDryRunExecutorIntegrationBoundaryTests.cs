using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowDryRun")]
public sealed class WorkflowDryRunExecutorIntegrationBoundaryTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();
    private readonly WorkflowDryRunExecutor _executor = new();

    [TestMethod]
    public void WorkflowDryRun_RunnerEligibleResultCanFeedDryRunExecutor()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest());
        var dryRun = _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, runnerResult.Status);
        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, dryRun.Status);
    }

    [TestMethod]
    public void WorkflowDryRun_RunnerApprovalHaltResultCannotFeedDryRunExecutor()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest(
            approvalHaltRequests:
            [
                WorkflowApprovalHaltStateTests.Request([])
            ]));
        var dryRun = _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedApprovalRequired, runnerResult.StepEvaluations[0].Eligibility);
        Assert.AreEqual(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt, dryRun.Status);
    }

    [TestMethod]
    public void WorkflowDryRun_RunnerPolicyBlockResultCannotFeedDryRunExecutor()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest(
            policyPreflightRequests:
            [
                PolicyPreflight([])
            ]));
        var dryRun = _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByPolicyPreflight, dryRun.Status);
    }

    [TestMethod]
    public void WorkflowDryRun_RunnerA2aBlockResultCannotFeedDryRunExecutor()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest(
            a2aHandoffRequests:
            [
                WorkflowA2aHandoffValidatorTests.ValidRequest() with
                {
                    HandoffReference = WorkflowA2aHandoffValidatorTests.ValidHandoff() with { WorkflowStepId = "other-step" }
                }
            ]));
        var dryRun = _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByA2aValidation, dryRun.Status);
    }

    [TestMethod]
    public void WorkflowDryRun_RunnerMissingEvidenceResultCannotFeedDryRunExecutor()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest(availableEvidence: []));
        var dryRun = _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByMissingEvidence, dryRun.Status);
    }

    [TestMethod]
    public void WorkflowDryRun_DoesNotAlterRunnerEvaluationSnapshot()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest());
        var before = JsonSerializer.Serialize(runnerResult.StepEvaluations[0]);

        _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));

        var after = JsonSerializer.Serialize(runnerResult.StepEvaluations[0]);
        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public void WorkflowDryRun_DoesNotProduceTransitionApprovalSourceMutationOrMemoryPromotionRecords()
    {
        var runnerResult = _runner.Evaluate(RunnerRequest());
        var dryRun = _executor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runnerResult.StepEvaluations[0]));
        var serialized = JsonSerializer.Serialize(dryRun);

        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, dryRun.Status);
        AssertDoesNotContainAny(
            serialized,
            "TransitionRecord",
            "ApprovalRecord",
            "SourceMutationRecord",
            "MemoryPromotionRecord",
            "ToolResult",
            "AgentResult",
            "ReceiptId");
    }

    private static WorkflowRunnerEvaluationRequest RunnerRequest(
        IReadOnlyList<WorkflowEvidenceReference>? availableEvidence = null,
        IReadOnlyList<WorkflowStepPolicyPreflightRequest>? policyPreflightRequests = null,
        IReadOnlyList<WorkflowA2aHandoffValidationRequest>? a2aHandoffRequests = null,
        IReadOnlyList<WorkflowApprovalHaltEvaluationRequest>? approvalHaltRequests = null) =>
        new()
        {
            WorkflowRunId = "workflow-run-001",
            StepContracts = [WorkflowA2aHandoffValidatorTests.ValidStep()],
            AvailableEvidence = availableEvidence ??
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
            PolicyPreflightRequests = policyPreflightRequests ?? [],
            A2aHandoffValidationRequests = a2aHandoffRequests ?? [],
            ApprovalHaltRequests = approvalHaltRequests ?? []
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
                    ProjectId = "project-001"
                }
            ],
            AvailablePolicyEvidence = evidence
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

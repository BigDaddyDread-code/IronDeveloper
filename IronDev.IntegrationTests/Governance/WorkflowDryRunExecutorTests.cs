using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowDryRun")]
public sealed class WorkflowDryRunExecutorTests
{
    private readonly WorkflowDryRunExecutor _executor = new();

    [TestMethod]
    public void WorkflowDryRun_MissingRequestReturnsInvalidRequest()
    {
        var result = _executor.ExecuteDryRun(null);

        Assert.AreEqual(WorkflowDryRunStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.InvalidRequest);
    }

    [TestMethod]
    public void WorkflowDryRun_UnknownActionKindReturnsInvalidRequest()
    {
        var result = _executor.ExecuteDryRun(Request(actionKind: (WorkflowDryRunActionKind)999));

        Assert.AreEqual(WorkflowDryRunStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.UnknownDryRunAction);
    }

    [TestMethod]
    public void WorkflowDryRun_EligibleStepReturnsDryRunCompleted()
    {
        var result = _executor.ExecuteDryRun(Request());

        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, result.Status);
        Assert.AreEqual("workflow-run-001", result.WorkflowRunId);
        Assert.AreEqual("workflow-step-001", result.WorkflowStepId);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotMutateState);
        CollectionAssert.Contains(result.SafeReportLines.ToList(), "No mutation was performed.");
    }

    [TestMethod]
    public void WorkflowDryRun_ReviewMaterialPreviewDoesNotCreateReviewMaterial()
    {
        var result = _executor.ExecuteDryRun(Request(actionKind: WorkflowDryRunActionKind.ReviewMaterialEligibilityPreview));

        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, result.Status);
        CollectionAssert.Contains(result.SafeReportLines.ToList(), "Step is eligible to produce review material later.");
        CollectionAssert.Contains(result.SafeReportLines.ToList(), "Review material was not created.");
    }

    [TestMethod]
    public void WorkflowDryRun_ResultIsDeterministic()
    {
        var first = JsonSerializer.Serialize(_executor.ExecuteDryRun(Request()));
        var second = JsonSerializer.Serialize(_executor.ExecuteDryRun(Request()));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void WorkflowDryRun_InvalidStepContractBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(step: ValidStep() with { ThoughtLedgerReference = null }));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByStepValidation, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.InvalidStepContract);
    }

    [TestMethod]
    public void WorkflowDryRun_StepEvaluationMismatchBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: EligibleEvaluation() with { StepId = "other-step" }));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByStepValidation, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.StepEvaluationMismatch);
    }

    [TestMethod]
    public void WorkflowDryRun_WorkflowRunMismatchBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(workflowRunId: "other-run"));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByStepValidation, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.WorkflowRunMismatch);
    }

    [TestMethod]
    public void WorkflowDryRun_MissingRequiredEvidenceBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: EligibleEvaluation() with
        {
            Eligibility = WorkflowStepRunnerEligibility.BlockedMissingEvidence,
            MissingEvidenceRequirements =
            [
                new()
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference,
                    RequirementId = "governance-event-001",
                    SafeSummary = "Governance reference."
                }
            ]
        }));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByMissingEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.MissingRequiredEvidence);
    }

    [TestMethod]
    public void WorkflowDryRun_PolicyPreflightBlockPreventsDryRun()
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: EligibleEvaluation() with
        {
            Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
            PolicyPreflightStatus = WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence,
            MissingPolicyRequirements =
            [
                new()
                {
                    Kind = WorkflowStepPolicyRequirementKind.HumanApprovalReference,
                    ReferenceId = "human-approval-001"
                }
            ]
        }));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByPolicyPreflight, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.PolicyPreflightBlocked);
    }

    [TestMethod]
    public void WorkflowDryRun_A2aInvalidBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: EligibleEvaluation() with
        {
            Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
            A2aHandoffValidationStatus = WorkflowA2aHandoffValidationStatus.InvalidHandoffReference
        }));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByA2aValidation, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.A2aValidationBlocked);
    }

    [TestMethod]
    public void WorkflowDryRun_A2aMissingEvidenceBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: EligibleEvaluation() with
        {
            Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
            A2aHandoffValidationStatus = WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence,
            MissingA2aHandoffEvidence =
            [
                new()
                {
                    Kind = WorkflowA2aHandoffEvidenceKind.HandoffValidationReference,
                    ReferenceId = "handoff-validation-001"
                }
            ]
        }));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByA2aValidation, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.A2aValidationBlocked);
    }

    [TestMethod]
    public void WorkflowDryRun_ApprovalRequiredHaltBlocks()
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: ApprovalBlockedEvaluation()));

        Assert.AreEqual(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.ApprovalRequiredHalt);
    }

    [DataTestMethod]
    [DataRow(WorkflowStepRunnerEligibility.InvalidContract, WorkflowDryRunStatus.BlockedByStepValidation)]
    [DataRow(WorkflowStepRunnerEligibility.BlockedMissingEvidence, WorkflowDryRunStatus.BlockedByMissingEvidence)]
    [DataRow(WorkflowStepRunnerEligibility.BlockedByBoundary, WorkflowDryRunStatus.BlockedByStepValidation)]
    [DataRow(WorkflowStepRunnerEligibility.BlockedApprovalRequired, WorkflowDryRunStatus.BlockedByApprovalRequiredHalt)]
    public void WorkflowDryRun_BlockedEligibilityNeverCompletes(WorkflowStepRunnerEligibility eligibility, WorkflowDryRunStatus expectedStatus)
    {
        var result = _executor.ExecuteDryRun(Request(evaluation: EligibleEvaluation() with { Eligibility = eligibility }));

        Assert.AreEqual(expectedStatus, result.Status);
        Assert.AreNotEqual(WorkflowDryRunStatus.DryRunCompleted, result.Status);
    }

    [TestMethod]
    public void WorkflowDryRun_ApprovalPolicyA2aAndThoughtLedgerEvidenceDoNotBecomeAuthority()
    {
        var result = _executor.ExecuteDryRun(Request());
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, result.Status);
        AssertDoesNotContainAny(
            serialized,
            "Approved",
            "ApprovalGranted",
            "PolicySatisfied",
            "AuthorityGranted",
            "HandoffSent",
            "AgentDispatched",
            "ToolInvoked",
            "SourceMutated",
            "MemoryPromoted",
            "RetrievalActivated");
    }

    [DataTestMethod]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("private reasoning")]
    [DataRow("hidden reasoning")]
    [DataRow("whole patch")]
    [DataRow("entire patch")]
    [DataRow("approval granted")]
    [DataRow("policy satisfied")]
    public void WorkflowDryRun_UnsafeSafeSummaryFailsClosedWithoutSerializingMarker(string marker)
    {
        var result = _executor.ExecuteDryRun(Request(safeSummary: $"Unsafe {marker} material."));
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowDryRunStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.UnsafeDryRunMaterial);
        Assert.AreEqual(0, result.SafeReportLines.Count);
        AssertDoesNotContainAny(serialized, marker);
    }

    [TestMethod]
    public void WorkflowDryRun_SafeReportLinesContainNoRawPrivateOrPatchPayload()
    {
        var serialized = JsonSerializer.Serialize(_executor.ExecuteDryRun(Request(safeSummary: "Safe review preview.")));

        AssertDoesNotContainAny(serialized, "raw prompt", "raw completion", "raw tool output", "private reasoning", "hidden reasoning", "whole patch", "patch payload");
    }

    internal static WorkflowDryRunRequest Request(
        WorkflowStepContract? step = null,
        WorkflowStepRunnerEvaluation? evaluation = null,
        WorkflowDryRunActionKind actionKind = WorkflowDryRunActionKind.NoOpValidationDryRun,
        string? safeSummary = null,
        string? workflowRunId = null) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            StepContract = step ?? ValidStep(),
            StepEvaluation = evaluation ?? EligibleEvaluation(),
            ActionKind = actionKind,
            SafeSummary = safeSummary
        };

    internal static WorkflowStepContract ValidStep() =>
        WorkflowA2aHandoffValidatorTests.ValidStep();

    internal static WorkflowStepRunnerEvaluation EligibleEvaluation() =>
        new()
        {
            StepId = "workflow-step-001",
            Eligibility = WorkflowStepRunnerEligibility.EligibleForFutureExecution,
            BlockReasons =
            [
                WorkflowRunnerBlockReason.RuntimeBoundaryPreventsExecution,
                WorkflowRunnerBlockReason.DispatchBoundaryPreventsActorResolution,
                WorkflowRunnerBlockReason.RetrievalBoundaryPreventsActivation
            ],
            MissingEvidenceRequirements = [],
            ThoughtLedgerReference = WorkflowA2aHandoffValidatorTests.ValidThoughtLedgerReference(),
            PolicyPreflightStatus = WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution,
            PolicyBlockReasons = [],
            MissingPolicyRequirements = [],
            A2aHandoffValidationStatus = WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff,
            A2aHandoffBlockReasons = [],
            MissingA2aHandoffEvidence = [],
            ApprovalHaltStatus = WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution,
            ApprovalHaltReasons =
            [
                WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval,
                WorkflowApprovalHaltReason.ApprovalHaltCannotExecute
            ],
            MissingApprovalRequirements = [],
            NextRecordableTransition = WorkflowStepContractTransitionKind.ReadyForReviewToReceiptRecorded
        };

    internal static WorkflowStepRunnerEvaluation ApprovalBlockedEvaluation() =>
        EligibleEvaluation() with
        {
            Eligibility = WorkflowStepRunnerEligibility.BlockedApprovalRequired,
            ApprovalHaltStatus = WorkflowApprovalHaltStatus.ApprovalRequiredHalt,
            MissingApprovalRequirements =
            [
                new()
                {
                    Kind = WorkflowApprovalRequirementKind.HumanApprovalReference,
                    RequirementId = "human-approval-001",
                    SafeSummary = "Human approval reference required."
                }
            ]
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

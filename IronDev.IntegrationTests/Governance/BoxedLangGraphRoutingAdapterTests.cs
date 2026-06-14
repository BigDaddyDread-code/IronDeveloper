using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BoxedLangGraphRoutingAdapter")]
public sealed class BoxedLangGraphRoutingAdapterTests
{
    private readonly BoxedLangGraphRoutingAdapter _adapter = new();

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_MissingRequestReturnsInvalidRoutingSnapshot()
    {
        var suggestion = _adapter.SuggestRoute(null);

        Assert.AreEqual(BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, suggestion.Label);
        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.InvalidInput);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_MissingWorkflowRunIdReturnsInvalidRoutingSnapshot()
    {
        var suggestion = _adapter.SuggestRoute(Request(workflowRunId: " "));

        Assert.AreEqual(BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_MissingWorkflowStepIdReturnsInvalidRoutingSnapshot()
    {
        var suggestion = _adapter.SuggestRoute(Request(workflowStepId: " "));

        Assert.AreEqual(BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, suggestion.Label);
    }

    [DataTestMethod]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("private reasoning")]
    [DataRow("hidden reasoning")]
    [DataRow("whole patch")]
    public void BoxedLangGraphRoutingAdapter_UnsafeSafeSummaryFailsClosedWithoutSerializingMarker(string marker)
    {
        var suggestion = _adapter.SuggestRoute(Request(safeSummary: $"Unsafe {marker} marker."));
        var serialized = JsonSerializer.Serialize(suggestion);

        Assert.AreEqual(BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, suggestion.Label);
        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.UnsafeInput);
        AssertDoesNotContainAny(serialized, marker);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_MissingEvaluationAndDryRunReturnsNoRouteSuggested()
    {
        var suggestion = _adapter.SuggestRoute(Request());

        Assert.AreEqual(BoxedLangGraphRouteLabel.NoRouteSuggested, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_InvalidStepEvaluationMapsToBlockedInvalidStep()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: Evaluation(WorkflowStepRunnerEligibility.InvalidContract)));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedInvalidStep, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_MissingEvidenceEvaluationMapsToBlockedMissingEvidence()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: Evaluation(WorkflowStepRunnerEligibility.BlockedMissingEvidence)));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedMissingEvidence, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_PolicyPreflightBlockMapsToBlockedPolicyPreflight()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: PolicyBlockedEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedPolicyPreflight, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_A2aValidationBlockMapsToBlockedA2aValidation()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: A2aBlockedEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedA2aValidation, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_ApprovalHaltMapsToBlockedApprovalRequired()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: WorkflowDryRunExecutorTests.ApprovalBlockedEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedApprovalRequired, suggestion.Label);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_EligibleEvaluationMapsToEligibleForDryRun()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.EligibleForDryRun, suggestion.Label);
    }

    [DataTestMethod]
    [DataRow(WorkflowDryRunStatus.BlockedByStepValidation, BoxedLangGraphRouteLabel.BlockedInvalidStep)]
    [DataRow(WorkflowDryRunStatus.BlockedByMissingEvidence, BoxedLangGraphRouteLabel.BlockedMissingEvidence)]
    [DataRow(WorkflowDryRunStatus.BlockedByPolicyPreflight, BoxedLangGraphRouteLabel.BlockedPolicyPreflight)]
    [DataRow(WorkflowDryRunStatus.BlockedByA2aValidation, BoxedLangGraphRouteLabel.BlockedA2aValidation)]
    [DataRow(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt, BoxedLangGraphRouteLabel.BlockedApprovalRequired)]
    [DataRow(WorkflowDryRunStatus.DryRunCompleted, BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable)]
    public void BoxedLangGraphRoutingAdapter_DryRunStatusMapsToExpectedLabel(WorkflowDryRunStatus status, BoxedLangGraphRouteLabel expectedLabel)
    {
        var suggestion = _adapter.SuggestRoute(Request(dryRun: DryRun(status)));

        Assert.AreEqual(expectedLabel, suggestion.Label);
        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.DryRunResultIsReviewMaterialOnly);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_SuggestionAlwaysIncludesAdvisoryAndDecisionBoundaryReasons()
    {
        var suggestion = _adapter.SuggestRoute(Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation()));

        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.AdvisoryOnly);
        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.AdapterCannotOwnDecisions);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_DoesNotMutateSuppliedEvaluation()
    {
        var evaluation = WorkflowDryRunExecutorTests.EligibleEvaluation();
        var before = JsonSerializer.Serialize(evaluation);

        _adapter.SuggestRoute(Request(evaluation: evaluation));

        Assert.AreEqual(before, JsonSerializer.Serialize(evaluation));
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_DoesNotMutateSuppliedDryRunResult()
    {
        var dryRun = DryRun(WorkflowDryRunStatus.DryRunCompleted);
        var before = JsonSerializer.Serialize(dryRun);

        _adapter.SuggestRoute(Request(dryRun: dryRun));

        Assert.AreEqual(before, JsonSerializer.Serialize(dryRun));
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_SameRequestProducesSameSuggestion()
    {
        var request = Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation(), safeSummary: "Safe route label.");

        var first = JsonSerializer.Serialize(_adapter.SuggestRoute(request));
        var second = JsonSerializer.Serialize(_adapter.SuggestRoute(request));

        Assert.AreEqual(first, second);
    }

    [DataTestMethod]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("private reasoning")]
    [DataRow("hidden reasoning")]
    [DataRow("whole patch")]
    [DataRow("approval granted")]
    [DataRow("policy satisfied")]
    [DataRow("workflow transitioned")]
    [DataRow("agent dispatched")]
    [DataRow("tool invoked")]
    [DataRow("source mutated")]
    [DataRow("memory promoted")]
    [DataRow("retrieval activated")]
    public void BoxedLangGraphRoutingAdapter_SerializedSuggestionContainsNoUnsafeOrAuthorityMarker(string marker)
    {
        var serialized = JsonSerializer.Serialize(_adapter.SuggestRoute(Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation())));

        AssertDoesNotContainAny(serialized, marker);
    }

    internal static BoxedLangGraphRoutingRequest Request(
        string workflowRunId = "workflow-run-001",
        string workflowStepId = "workflow-step-001",
        WorkflowStepRunnerEvaluation? evaluation = null,
        WorkflowDryRunResult? dryRun = null,
        string? safeSummary = null) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            StepEvaluation = evaluation,
            DryRunResult = dryRun,
            SafeSummary = safeSummary
        };

    internal static WorkflowStepRunnerEvaluation Evaluation(WorkflowStepRunnerEligibility eligibility) =>
        WorkflowDryRunExecutorTests.EligibleEvaluation() with
        {
            Eligibility = eligibility,
            MissingEvidenceRequirements = eligibility == WorkflowStepRunnerEligibility.BlockedMissingEvidence
                ?
                [
                    new()
                    {
                        Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference,
                        RequirementId = "governance-event-001",
                        SafeSummary = "Governance event reference."
                    }
                ]
                : []
        };

    internal static WorkflowStepRunnerEvaluation PolicyBlockedEvaluation() =>
        WorkflowDryRunExecutorTests.EligibleEvaluation() with
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
        };

    internal static WorkflowStepRunnerEvaluation A2aBlockedEvaluation() =>
        WorkflowDryRunExecutorTests.EligibleEvaluation() with
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
        };

    internal static WorkflowDryRunResult DryRun(WorkflowDryRunStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-001",
            WorkflowStepId = "workflow-step-001",
            ActionKind = WorkflowDryRunActionKind.NoOpValidationDryRun,
            Status = status,
            BlockReasons = status switch
            {
                WorkflowDryRunStatus.BlockedByStepValidation => [WorkflowDryRunBlockReason.InvalidStepContract],
                WorkflowDryRunStatus.BlockedByMissingEvidence => [WorkflowDryRunBlockReason.MissingRequiredEvidence],
                WorkflowDryRunStatus.BlockedByPolicyPreflight => [WorkflowDryRunBlockReason.PolicyPreflightBlocked],
                WorkflowDryRunStatus.BlockedByA2aValidation => [WorkflowDryRunBlockReason.A2aValidationBlocked],
                WorkflowDryRunStatus.BlockedByApprovalRequiredHalt => [WorkflowDryRunBlockReason.ApprovalRequiredHalt],
                WorkflowDryRunStatus.DryRunCompleted => [WorkflowDryRunBlockReason.DryRunCannotMutateState],
                _ => [WorkflowDryRunBlockReason.InvalidRequest]
            },
            SafeReportLines = status == WorkflowDryRunStatus.DryRunCompleted
                ? ["No mutation was performed.", "Dry-run result is safe review material only."]
                : []
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

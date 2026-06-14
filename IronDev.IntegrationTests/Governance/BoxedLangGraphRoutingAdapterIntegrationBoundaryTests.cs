using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BoxedLangGraphRoutingAdapter")]
public sealed class BoxedLangGraphRoutingAdapterIntegrationBoundaryTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();
    private readonly WorkflowDryRunExecutor _dryRunExecutor = new();
    private readonly BoxedLangGraphRoutingAdapter _adapter = new();

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_RunnerEligibleEvaluationCanBeMappedToAdvisoryDryRunRoute()
    {
        var runner = _runner.Evaluate(RunnerRequest());
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: runner.StepEvaluations[0]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, runner.StepEvaluations[0].Eligibility);
        Assert.AreEqual(BoxedLangGraphRouteLabel.EligibleForDryRun, suggestion.Label);
        Assert.IsTrue(suggestion.IsAdvisoryOnly);
        Assert.IsFalse(suggestion.StepWorkAllowed);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_DryRunCompletedResultMapsToReviewMaterialAvailable()
    {
        var runner = _runner.Evaluate(RunnerRequest());
        var dryRun = _dryRunExecutor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runner.StepEvaluations[0]));
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(dryRun: dryRun));

        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, dryRun.Status);
        Assert.AreEqual(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable, suggestion.Label);
        Assert.IsFalse(suggestion.IsDryRunEvidence);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_ApprovalHaltRemainsHaltAfterRoutingSuggestion()
    {
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: WorkflowDryRunExecutorTests.ApprovalBlockedEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedApprovalRequired, suggestion.Label);
        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.ApprovalHaltStillHalts);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_PolicyBlockRemainsPolicyBlockAfterRoutingSuggestion()
    {
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: BoxedLangGraphRoutingAdapterTests.PolicyBlockedEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedPolicyPreflight, suggestion.Label);
        Assert.IsFalse(suggestion.IsPolicyEvidence);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_A2aBlockRemainsA2aBlockAfterRoutingSuggestion()
    {
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: BoxedLangGraphRoutingAdapterTests.A2aBlockedEvaluation()));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedA2aValidation, suggestion.Label);
        Assert.IsFalse(suggestion.IsA2aValidationEvidence);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_MissingEvidenceRemainsMissingEvidenceAfterRoutingSuggestion()
    {
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: BoxedLangGraphRoutingAdapterTests.Evaluation(WorkflowStepRunnerEligibility.BlockedMissingEvidence)));

        Assert.AreEqual(BoxedLangGraphRouteLabel.BlockedMissingEvidence, suggestion.Label);
        CollectionAssert.Contains(suggestion.Reasons.ToList(), BoxedLangGraphRouteReason.EvidenceIsNotApproval);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_OutputCannotBeUsedAsApprovalPolicyTransitionDryRunA2aMemoryOrRetrievalEvidence()
    {
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation()));

        Assert.IsFalse(suggestion.IsApprovalEvidence);
        Assert.IsFalse(suggestion.IsPolicyEvidence);
        Assert.IsFalse(suggestion.IsWorkflowTransitionEvidence);
        Assert.IsFalse(suggestion.IsDryRunEvidence);
        Assert.IsFalse(suggestion.IsA2aValidationEvidence);
        Assert.IsFalse(suggestion.IsMemoryPromotionEvidence);
        Assert.IsFalse(suggestion.IsRetrievalApprovalEvidence);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_OutputCannotOwnDecisionsChangeStateSendUseToolsChangeSourcePromoteMemoryOrActivateRetrieval()
    {
        var suggestion = _adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(dryRun: BoxedLangGraphRoutingAdapterTests.DryRun(WorkflowDryRunStatus.DryRunCompleted)));

        Assert.IsFalse(suggestion.WorkflowDecisionAuthority);
        Assert.IsFalse(suggestion.WorkflowStateChangeAllowed);
        Assert.IsFalse(suggestion.AgentSendAllowed);
        Assert.IsFalse(suggestion.A2aSendAllowed);
        Assert.IsFalse(suggestion.ToolUseAllowed);
        Assert.IsFalse(suggestion.SourceChangeAllowed);
        Assert.IsFalse(suggestion.MemoryPromotionAllowed);
        Assert.IsFalse(suggestion.RetrievalActivationAllowed);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_IntegrationSuggestionContainsNoUnsafePayloadsOrAuthorityClaims()
    {
        var runner = _runner.Evaluate(RunnerRequest());
        var dryRun = _dryRunExecutor.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runner.StepEvaluations[0]));
        var serialized = JsonSerializer.Serialize(_adapter.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(dryRun: dryRun)));

        AssertDoesNotContainAny(
            serialized,
            "raw prompt",
            "raw completion",
            "raw tool output",
            "private reasoning",
            "hidden reasoning",
            "whole patch",
            "approval granted",
            "policy satisfied",
            "workflow transitioned",
            "source mutated",
            "memory promoted");
    }

    private static WorkflowRunnerEvaluationRequest RunnerRequest() =>
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
            ]
        };

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

using System.Reflection;
using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowCannotGrantAuthority")]
public sealed class WorkflowCannotGrantAuthorityTests
{
    private readonly WorkflowStepContractValidator _stepValidator = new();
    private readonly WorkflowRunnerSkeleton _runner = new();
    private readonly WorkflowStepPolicyPreflightChecker _policyChecker = new();
    private readonly WorkflowA2aHandoffValidator _a2aValidator = new();
    private readonly WorkflowApprovalHaltEvaluator _approvalHalt = new();
    private readonly WorkflowDryRunExecutor _dryRun = new();
    private readonly BoxedLangGraphRoutingAdapter _routing = new();

    [TestMethod]
    public void WorkflowCannotGrantAuthority_ValidStepContractIsReviewDescriptionOnly()
    {
        var step = ValidStep();
        var validation = _stepValidator.Validate(step);

        Assert.IsTrue(validation.IsValid);
        AssertStepContractAuthorityFlagsFalse(step);
        AssertNoAuthoritySurface(step);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_RunnerEligibleEvaluationIsNotExecutionPermission()
    {
        var result = _runner.Evaluate(RunnerRequest());
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, step.Eligibility);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.RuntimeBoundaryPreventsExecution);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.DispatchBoundaryPreventsActorResolution);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.RetrievalBoundaryPreventsActivation);
        AssertNoAuthoritySurface(result);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_RunnerApprovalHaltCannotBeBypassedByEligibilityVocabulary()
    {
        var result = _runner.Evaluate(RunnerRequest(approvalRequests: [WorkflowApprovalHaltStateTests.Request([])]));
        var step = result.StepEvaluations[0];

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedApprovalRequired, step.Eligibility);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, step.ApprovalHaltStatus);
        CollectionAssert.Contains(step.BlockReasons.ToList(), WorkflowRunnerBlockReason.ApprovalRequiredHalt);
        AssertNoAuthoritySurface(result);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_PolicyPreflightEvidencePresentIsNotPolicySatisfaction()
    {
        var result = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference, includeEvidence: true));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.PolicyPreflightCannotExecute);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.PolicyPreflightCannotMutateApproval);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.WorkflowCannotGrantAuthority);
        AssertNoAuthoritySurface(result);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_A2aValidForFutureHandoffIsNotDispatch()
    {
        var result = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest());

        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, result.Status);
        AssertNoAuthoritySurface(result);
        AssertNoPublicMethod(typeof(IWorkflowA2aHandoffValidator), "Send", "Dispatch", "ResolveAgent", "Execute", "Approve");
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_ThoughtLedgerReferenceIsTraceabilityOnly()
    {
        var reference = WorkflowA2aHandoffValidatorTests.ValidThoughtLedgerReference();

        AssertNoAuthoritySurface(reference);
        AssertNoPublicMethod(typeof(WorkflowStepThoughtLedgerReference), "Approve", "SatisfyPolicy", "Execute", "Dispatch", "PromoteMemory");
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_ApprovalEvidencePresentIsNotApprovalMutation()
    {
        var result = _approvalHalt.Evaluate(WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")]));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalEvidenceIsNotApprovalMutation);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltCannotExecute);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltCannotSatisfyPolicy);
        AssertNoAuthoritySurface(result);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_DryRunCompletedIsReviewMaterialOnly()
    {
        var runner = _runner.Evaluate(RunnerRequest());
        var result = _dryRun.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runner.StepEvaluations[0]));

        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotMutateState);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotApprove);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotDispatch);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotInvokeTools);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotMutateSource);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotPromoteMemory);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotActivateRetrieval);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowDryRunBlockReason.DryRunCannotCallModels);
        AssertNoAuthoritySurface(result);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_BoxedRouteSuggestionCannotGrantAuthority()
    {
        var suggestion = _routing.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(dryRun: BoxedLangGraphRoutingAdapterTests.DryRun(WorkflowDryRunStatus.DryRunCompleted)));

        Assert.AreEqual(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable, suggestion.Label);
        AssertFalseAuthorityFlags(suggestion);
        AssertNoAuthoritySurface(suggestion);
    }

    [DataTestMethod]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("private reasoning")]
    [DataRow("hidden reasoning")]
    [DataRow("chain of thought")]
    [DataRow("whole patch")]
    [DataRow("patch payload")]
    public void WorkflowCannotGrantAuthority_UnsafeInputFailsClosedWithoutEcho(string marker)
    {
        var step = ValidStep() with { SafeSummary = $"Unsafe {marker} marker." };
        var runner = _runner.Evaluate(RunnerRequest(step));
        var dryRun = _dryRun.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(safeSummary: $"Unsafe {marker} marker."));
        var route = _routing.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(safeSummary: $"Unsafe {marker} marker."));
        var serialized = JsonSerializer.Serialize(new { runner, dryRun, route });

        Assert.AreEqual(WorkflowStepRunnerEligibility.InvalidContract, runner.StepEvaluations[0].Eligibility);
        Assert.AreEqual(WorkflowDryRunStatus.InvalidRequest, dryRun.Status);
        Assert.AreEqual(BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, route.Label);
        AssertDoesNotContainAny(serialized, marker);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_CombinedWorkflowArtifactsSerializeWithoutRawPrivateOrFullPayloads()
    {
        var runner = _runner.Evaluate(RunnerRequest());
        var policy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference, includeEvidence: true));
        var a2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest());
        var approval = _approvalHalt.Evaluate(WorkflowApprovalHaltStateTests.Request([WorkflowApprovalHaltStateTests.Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")]));
        var dryRun = _dryRun.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: runner.StepEvaluations[0]));
        var route = _routing.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(dryRun: dryRun));
        var serialized = JsonSerializer.Serialize(new { runner, policy, a2a, approval, dryRun, route });

        AssertDoesNotContainAny(serialized, "raw prompt", "raw completion", "raw tool output", "private reasoning", "hidden reasoning", "chain of thought", "whole patch", "patch payload", "entire patch");
    }

    internal static WorkflowStepContract ValidStep() => WorkflowA2aHandoffValidatorTests.ValidStep();

    internal static WorkflowRunnerEvaluationRequest RunnerRequest(WorkflowStepContract? step = null, IReadOnlyList<WorkflowEvidenceReference>? evidence = null, IReadOnlyList<WorkflowStepPolicyPreflightRequest>? policyRequests = null, IReadOnlyList<WorkflowA2aHandoffValidationRequest>? a2aRequests = null, IReadOnlyList<WorkflowApprovalHaltEvaluationRequest>? approvalRequests = null) =>
        new()
        {
            WorkflowRunId = "workflow-run-001",
            StepContracts = [step ?? ValidStep()],
            AvailableEvidence = evidence ??
            [
                new() { Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference, ReferenceId = "governance-event-001" },
                new() { Kind = WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference, ReferenceId = "handoff-reference-001" }
            ],
            PolicyPreflightRequests = policyRequests ?? [],
            A2aHandoffValidationRequests = a2aRequests ?? [],
            ApprovalHaltRequests = approvalRequests ?? []
        };

    internal static WorkflowStepPolicyPreflightRequest PolicyRequest(WorkflowStepSensitivityKind sensitivity, WorkflowStepPolicyRequirementKind requiredKind, bool includeEvidence = false, WorkflowStepContract? step = null, string referenceId = "policy-ref-001") =>
        new()
        {
            StepContract = step ?? ValidStep(),
            SensitivityKind = sensitivity,
            RequiredPolicyReferences = [WorkflowStepPolicyPreflightCheckerTests.Requirement(requiredKind, referenceId)],
            AvailablePolicyEvidence = includeEvidence ? [WorkflowStepPolicyPreflightCheckerTests.Evidence(requiredKind, referenceId)] : []
        };

    internal static void AssertStepContractAuthorityFlagsFalse(WorkflowStepContract step)
    {
        Assert.IsFalse(step.InputReference.HydratesContent);
        Assert.IsFalse(step.InputReference.ActivatesRetrieval);
        Assert.IsFalse(step.InputReference.GrantsApproval);
        Assert.IsFalse(step.InputReference.AllowsExecution);
        Assert.IsFalse(step.InputReference.MutatesSource);
        Assert.IsFalse(step.InputReference.PromotesMemory);
        Assert.IsFalse(step.ExpectedOutputReference.HydratesContent);
        Assert.IsFalse(step.ExpectedOutputReference.ActivatesRetrieval);
        Assert.IsFalse(step.ExpectedOutputReference.GrantsApproval);
        Assert.IsFalse(step.ExpectedOutputReference.AllowsExecution);
        Assert.IsFalse(step.ExpectedOutputReference.MutatesSource);
        Assert.IsFalse(step.ExpectedOutputReference.PromotesMemory);
        Assert.IsTrue(step.AllowedTransitions.All(transition => !transition.StartsWorkflow && !transition.ContinuesWorkflow && !transition.DispatchesAgent && !transition.InvokesTool && !transition.IndicatesExecutionSuccess));
        Assert.IsTrue(step.EvidenceRequirements.All(evidence => !evidence.IsApproval && !evidence.SatisfiesPolicy && !evidence.AllowsExecution && !evidence.PromotesMemory && !evidence.RequiresHydratedContent));
        Assert.IsFalse(step.Boundary.AllowsExecution);
        Assert.IsFalse(step.Boundary.AllowsAgentDispatch);
        Assert.IsFalse(step.Boundary.AllowsToolInvocation);
        Assert.IsFalse(step.Boundary.AllowsSourceMutation);
        Assert.IsFalse(step.Boundary.AllowsApprovalMutation);
        Assert.IsFalse(step.Boundary.AllowsMemoryPromotion);
        Assert.IsFalse(step.Boundary.AllowsRetrievalActivation);
        Assert.IsFalse(step.Boundary.AllowsWorkflowContinuation);
    }

    internal static void AssertFalseAuthorityFlags(BoxedLangGraphRouteSuggestion suggestion)
    {
        Assert.IsTrue(suggestion.IsAdvisoryOnly);
        Assert.IsFalse(suggestion.WorkflowDecisionAuthority);
        Assert.IsFalse(suggestion.WorkflowStateChangeAllowed);
        Assert.IsFalse(suggestion.StepWorkAllowed);
        Assert.IsFalse(suggestion.AgentSendAllowed);
        Assert.IsFalse(suggestion.A2aSendAllowed);
        Assert.IsFalse(suggestion.ToolUseAllowed);
        Assert.IsFalse(suggestion.ApprovalChangeAllowed);
        Assert.IsFalse(suggestion.PolicySatisfactionAllowed);
        Assert.IsFalse(suggestion.SourceChangeAllowed);
        Assert.IsFalse(suggestion.MemoryPromotionAllowed);
        Assert.IsFalse(suggestion.RetrievalActivationAllowed);
        Assert.IsFalse(suggestion.IsApprovalEvidence);
        Assert.IsFalse(suggestion.IsPolicyEvidence);
        Assert.IsFalse(suggestion.IsWorkflowTransitionEvidence);
        Assert.IsFalse(suggestion.IsDryRunEvidence);
        Assert.IsFalse(suggestion.IsA2aValidationEvidence);
        Assert.IsFalse(suggestion.IsMemoryPromotionEvidence);
        Assert.IsFalse(suggestion.IsRetrievalApprovalEvidence);
    }

    internal static void AssertNoAuthoritySurface(object value)
    {
        var text = JsonSerializer.Serialize(value);
        AssertDoesNotContainAny(text, "approval granted", "approval satisfied", "policy satisfied", "execution allowed", "workflow transitioned", "step completed", "agent dispatched", "a2a sent", "tool invoked", "model called", "source mutated", "patch applied", "memory promoted", "retrieval activated", "sql written", "authority granted");
    }

    internal static void AssertNoPublicMethod(Type type, params string[] forbiddenNames)
    {
        var names = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name).ToArray();
        AssertDoesNotContainAny(names, forbiddenNames);
    }

    internal static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] forbidden) => AssertDoesNotContainAny(string.Join("\n", values), forbidden);

    internal static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }
}

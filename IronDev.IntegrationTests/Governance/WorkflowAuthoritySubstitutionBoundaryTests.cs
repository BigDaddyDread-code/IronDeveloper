using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowAuthoritySubstitution")]
public sealed class WorkflowAuthoritySubstitutionBoundaryTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();
    private readonly WorkflowStepPolicyPreflightChecker _policyChecker = new();
    private readonly WorkflowA2aHandoffValidator _a2aValidator = new();
    private readonly WorkflowApprovalHaltEvaluator _approvalHalt = new();
    private readonly WorkflowDryRunExecutor _dryRun = new();
    private readonly BoxedLangGraphRoutingAdapter _routing = new();

    [TestMethod]
    public void WorkflowAuthoritySubstitution_ThoughtLedgerReferenceCannotSatisfyApprovalPolicyA2aOrDryRunEligibility()
    {
        var approval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, "thought-ledger-entry-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.GovernanceEventReference, "thought-ledger-entry-001")]);
        var policy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference, WorkflowStepPolicyRequirementKind.GovernanceEventReference, "thought-ledger-entry-001"));
        var a2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest() with
        {
            AvailableEvidence = [new() { Kind = WorkflowA2aHandoffEvidenceKind.ThoughtLedgerReference, ReferenceId = "thought-ledger-entry-001" }]
        });
        var dryRun = _dryRun.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: BoxedLangGraphRoutingAdapterTests.Evaluation(WorkflowStepRunnerEligibility.BlockedMissingEvidence)));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, approval.Status);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, a2a.Status);
        Assert.AreEqual(WorkflowDryRunStatus.BlockedByMissingEvidence, dryRun.Status);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_PolicyEvidenceAndApprovalEvidenceAreNotInterchangeable()
    {
        var policyEvidenceAsApproval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, "policy-ref-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.PolicyDecisionReference, "policy-ref-001")]);
        var approvalEvidenceAsPolicy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ApprovalRequiredAction, WorkflowStepPolicyRequirementKind.HumanApprovalReference, WorkflowStepPolicyRequirementKind.ApprovalPolicyReference, "human-approval-001"));
        var correctlyTypedApproval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")]);

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, policyEvidenceAsApproval.Status);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, approvalEvidenceAsPolicy.Status);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution, correctlyTypedApproval.Status);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_A2aAndApprovalHaltEvidenceCannotSatisfyEachOther()
    {
        var a2aAsApproval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, "handoff-reference-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.GovernanceEventReference, "handoff-reference-001")]);
        var approvalAsA2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest() with
        {
            AvailableEvidence = [new() { Kind = WorkflowA2aHandoffEvidenceKind.GovernanceEventReference, ReferenceId = "human-approval-001" }]
        });

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, a2aAsApproval.Status);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, approvalAsA2a.Status);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_DryRunResultCannotSatisfyApprovalPolicyA2aOrRunnerEvidence()
    {
        var completedDryRun = BoxedLangGraphRoutingAdapterTests.DryRun(WorkflowDryRunStatus.DryRunCompleted);
        var approval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, completedDryRun.WorkflowStepId, []);
        var policy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference));
        var a2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest() with { AvailableEvidence = [] });
        var runner = _runner.Evaluate(WorkflowCannotGrantAuthorityTests.RunnerRequest(evidence: []));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, approval.Status);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, a2a.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedMissingEvidence, runner.StepEvaluations[0].Eligibility);
        WorkflowCannotGrantAuthorityTests.AssertNoAuthoritySurface(completedDryRun);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_RouteSuggestionCannotSatisfyApprovalPolicyA2aDryRunOrRunnerEvidence()
    {
        var route = _routing.SuggestRoute(BoxedLangGraphRoutingAdapterTests.Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation()));
        var approval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, route.WorkflowStepId, []);
        var policy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference));
        var a2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest() with { AvailableEvidence = [] });
        var dryRun = _dryRun.ExecuteDryRun(WorkflowDryRunExecutorTests.Request(evaluation: WorkflowDryRunExecutorTests.EligibleEvaluation()));
        var runner = _runner.Evaluate(WorkflowCannotGrantAuthorityTests.RunnerRequest(evidence: []));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, approval.Status);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, a2a.Status);
        Assert.AreEqual(WorkflowDryRunStatus.DryRunCompleted, dryRun.Status);
        CollectionAssert.Contains(dryRun.BlockReasons.ToArray(), WorkflowDryRunBlockReason.DryRunCannotApprove);
        CollectionAssert.Contains(dryRun.BlockReasons.ToArray(), WorkflowDryRunBlockReason.DryRunCannotDispatch);
        CollectionAssert.Contains(dryRun.BlockReasons.ToArray(), WorkflowDryRunBlockReason.DryRunCannotInvokeTools);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedMissingEvidence, runner.StepEvaluations[0].Eligibility);
        WorkflowCannotGrantAuthorityTests.AssertFalseAuthorityFlags(route);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_RunnerEligibilityCannotSatisfyApprovalPolicyOrA2aValidation()
    {
        var runner = _runner.Evaluate(WorkflowCannotGrantAuthorityTests.RunnerRequest());
        var approval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, runner.StepEvaluations[0].Eligibility.ToString(), []);
        var policy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference));
        var a2a = _a2aValidator.Validate(WorkflowA2aHandoffValidatorTests.ValidRequest() with { AvailableEvidence = [] });

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, runner.StepEvaluations[0].Eligibility);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, approval.Status);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, policy.Status);
        Assert.AreEqual(WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence, a2a.Status);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_HandoffAndGovernanceReferencesDoNotSatisfyUnrelatedPolicyOrApprovalKinds()
    {
        var handoffAsPolicy = _policyChecker.Check(PolicyRequest(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference, WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference, "handoff-reference-001"));
        var governanceAsApproval = ApprovalState(WorkflowApprovalRequirementKind.HumanApprovalReference, "governance-event-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.GovernanceEventReference, "governance-event-001")]);

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, handoffAsPolicy.Status);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, governanceAsApproval.Status);
    }

    [TestMethod]
    public void WorkflowAuthoritySubstitution_MemoryRetrievalAndSourceApprovalReferencesRemainScopedByExactKindAndId()
    {
        var memoryProposalAsSourceApproval = ApprovalState(WorkflowApprovalRequirementKind.ApprovalPackageReference, "source-mutation-approval-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.GovernanceEventReference, "memory-proposal-001")]);
        var retrievalAsMemoryPromotion = ApprovalState(WorkflowApprovalRequirementKind.ApprovalDecisionReference, "memory-promotion-approval-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.PolicyDecisionReference, "retrieval-approval-001")]);
        var sourceApprovalAsMemoryPromotion = ApprovalState(WorkflowApprovalRequirementKind.ApprovalPackageReference, "memory-promotion-approval-001", [ApprovalEvidence(WorkflowApprovalRequirementKind.ApprovalPackageReference, "source-mutation-approval-001")]);

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, memoryProposalAsSourceApproval.Status);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, retrievalAsMemoryPromotion.Status);
        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, sourceApprovalAsMemoryPromotion.Status);
    }

    private static WorkflowApprovalHaltState ApprovalState(WorkflowApprovalRequirementKind requiredKind, string requiredId, IReadOnlyList<WorkflowApprovalEvidenceReference> evidence) =>
        new WorkflowApprovalHaltEvaluator().Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = "workflow-step-001",
            RequiredApprovals = [new() { Kind = requiredKind, RequirementId = requiredId, SafeSummary = "Approval evidence reference required." }],
            AvailableApprovalEvidence = evidence
        });

    private static WorkflowApprovalEvidenceReference ApprovalEvidence(WorkflowApprovalRequirementKind kind, string referenceId) =>
        new() { Kind = kind, ReferenceId = referenceId, SafeSummary = "Evidence reference only." };

    private static WorkflowStepPolicyPreflightRequest PolicyRequest(WorkflowStepSensitivityKind sensitivity, WorkflowStepPolicyRequirementKind requiredKind, WorkflowStepPolicyRequirementKind? evidenceKind = null, string referenceId = "policy-ref-001") =>
        new()
        {
            StepContract = WorkflowCannotGrantAuthorityTests.ValidStep(),
            SensitivityKind = sensitivity,
            RequiredPolicyReferences = [WorkflowStepPolicyPreflightCheckerTests.Requirement(requiredKind, referenceId)],
            AvailablePolicyEvidence = evidenceKind is null ? [] : [WorkflowStepPolicyPreflightCheckerTests.Evidence(evidenceKind.Value, referenceId)]
        };
}

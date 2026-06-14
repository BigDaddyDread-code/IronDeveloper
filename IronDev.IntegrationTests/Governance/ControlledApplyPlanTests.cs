using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ControlledApplyPlanTests
{
    [TestMethod]
    public void Prepare_WithValidRequest_ProducesPlanOnlyResult()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest());

        Assert.AreEqual(ControlledApplyPlanStatus.ControlledApplyPlanPrepared, result.Status);
        Assert.IsTrue(result.IsPlanOnly);
        AssertNoAuthority(result);
        AssertContains(result.Reasons, ControlledApplyPlanReason.ControlledApplyPlanPrepared);
        AssertContains(result.PlanSummaries, "Controlled apply plan was prepared from supplied references only.");
        AssertContains(result.PlanSummaries, "Apply placeholders are not executable.");
        AssertContains(result.PlanSummaries, "Validation references are not validation execution.");
        AssertContains(result.PlanSummaries, "Rollback notes are not rollback execution.");
    }

    [TestMethod]
    public void Prepare_WithNullRequest_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(null);

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertContains(result.Reasons, ControlledApplyPlanReason.MissingRequest);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithMissingIdentity_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with { WorkflowRunId = " " });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.MissingWorkflowRunId);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithUnknownTargetKind_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with { TargetKind = ControlledApplyPlanTargetKind.Unknown });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.UnknownTargetKind);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw prompt leaked")]
    [DataRow("chainOfThought leaked")]
    [DataRow("entirePatch leaked")]
    [DataRow("approval granted")]
    [DataRow("source mutated")]
    public void Prepare_WithUnsafeSummary_FailsClosedWithoutEchoingMarker(string unsafeText)
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with { SafePlanSummary = unsafeText });
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.UnsafePlanSummary);
        AssertDoesNotContain(json, unsafeText);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithUnsafeEvidenceReference_FailsClosedWithoutEchoingMarker()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with
        {
            EvidenceReferences = [ValidEvidence() with { SafeSummary = "rawCompletion leaked" }]
        });
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.UnsafeEvidenceReference);
        AssertDoesNotContain(json, "rawCompletion");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithAuthorityClaimingPhase_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with
        {
            PlanPhases = [ValidPhase() with { AppliesSource = true }]
        });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.PlanPhaseClaimsAuthority);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithAuthorityClaimingPrecondition_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with
        {
            Preconditions = [ValidPrecondition() with { IsApprovalSatisfied = true }]
        });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.PreconditionClaimsAuthority);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithValidationResultClaim_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with
        {
            ValidationSteps = [ValidValidation() with { IsValidationResult = true }]
        });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.ValidationReferenceClaimsAuthority);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithRollbackExecutionClaim_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with
        {
            RollbackNotes = [ValidRollback() with { IsRollbackExecution = true }]
        });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.RollbackReferenceClaimsAuthority);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithAuthorityClaimingGateHint_FailsClosed()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with
        {
            GateHints = [ValidGateHint() with { IsSatisfied = true }]
        });

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.GateHintClaimsAuthority);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithMissingRequiredMaterial_ReturnsMissingEvidence()
    {
        var result = new ControlledApplyPlanWorkflow().Prepare(ValidRequest() with { ValidationSteps = [] });

        Assert.AreEqual(ControlledApplyPlanStatus.MissingRequiredPlanEvidence, result.Status);
        Assert.IsTrue(result.MissingReferences.Any(reference => reference.ReferenceKind == nameof(ControlledApplyPlanReason.MissingValidationReference)));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_WithInvalidUpstreamPatchPackage_FailsClosed()
    {
        var request = ValidRequest() with
        {
            PatchProposalEvidencePackage = new PatchProposalEvidencePackageWorkflow()
                .Prepare(PatchProposalEvidencePackageTests.ValidRequest()) with
                {
                    Status = PatchProposalEvidencePackageStatus.InvalidRequest
                }
        };

        var result = new ControlledApplyPlanWorkflow().Prepare(request);

        Assert.AreEqual(ControlledApplyPlanStatus.InvalidRequest, result.Status);
        AssertIssue(result, ControlledApplyPlanReason.PatchProposalEvidencePackageNotReady);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void Prepare_IsDeterministic()
    {
        var workflow = new ControlledApplyPlanWorkflow();
        var request = ValidRequest();

        var first = JsonSerializer.Serialize(workflow.Prepare(request));
        var second = JsonSerializer.Serialize(workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Prepare_SerializesWithoutPrivateOrAuthorityPayloads()
    {
        var json = JsonSerializer.Serialize(new ControlledApplyPlanWorkflow().Prepare(ValidRequest()));

        AssertDoesNotContain(json, "raw prompt");
        AssertDoesNotContain(json, "rawCompletion");
        AssertDoesNotContain(json, "chainOfThought");
        AssertDoesNotContain(json, "patch payload");
        AssertDoesNotContain(json, "approval granted");
        AssertDoesNotContain(json, "source mutated");
    }

    internal static ControlledApplyPlanRequest ValidRequest()
    {
        return new ControlledApplyPlanRequest
        {
            WorkflowRunId = "workflow-run-139",
            WorkflowStepId = "workflow-step-139",
            ControlledApplyPlanReferenceId = "controlled-apply-plan-139",
            ProjectReferenceId = "project-139",
            TargetReferenceId = "target-139",
            TargetKind = ControlledApplyPlanTargetKind.PatchReviewCandidate,
            SafePlanSummary = "Review supplied patch proposal evidence before any future source apply implementation.",
            SourceApplyApprovalRequirement = new SourceApplyApprovalRequirementContract().Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest()),
            PatchProposalEvidencePackage = new PatchProposalEvidencePackageWorkflow().Prepare(PatchProposalEvidencePackageTests.ValidRequest()),
            HumanApprovalPackage = SourceApplyApprovalRequirementContractTests.ValidRequest().HumanApprovalPackage,
            PlanPhases = [ValidPhase()],
            Preconditions = [ValidPrecondition()],
            ValidationSteps = [ValidValidation()],
            RollbackNotes = [ValidRollback()],
            EvidenceReferences = [ValidEvidence()],
            GateHints = [ValidGateHint()],
            Risks = [ValidRisk()],
            CorrelationId = "correlation-139"
        };
    }

    internal static ControlledApplyPlanPhase ValidPhase()
    {
        return new ControlledApplyPlanPhase
        {
            PhaseId = "phase-review",
            Kind = ControlledApplyPlanPhaseKind.PatchMaterialReview,
            SafeSummary = "Review supplied evidence and keep future action separate."
        };
    }

    internal static ControlledApplyPreconditionReference ValidPrecondition()
    {
        return new ControlledApplyPreconditionReference
        {
            PreconditionId = "precondition-human-review",
            ReferenceType = "source-apply-approval-requirement",
            SafeSummary = "Human review remains required before any future source apply implementation."
        };
    }

    internal static ControlledApplyValidationReference ValidValidation()
    {
        return new ControlledApplyValidationReference
        {
            ValidationReferenceId = "validation-reference",
            SafeSummary = "Separate validation evidence must be supplied later."
        };
    }

    internal static ControlledApplyRollbackReference ValidRollback()
    {
        return new ControlledApplyRollbackReference
        {
            RollbackReferenceId = "rollback-note",
            SafeSummary = "Rollback planning remains review material only."
        };
    }

    internal static ControlledApplyEvidenceReference ValidEvidence()
    {
        return new ControlledApplyEvidenceReference
        {
            EvidenceId = "patch-proposal-evidence-package",
            EvidenceType = "patch-proposal-evidence",
            SafeSummary = "Patch proposal evidence package exists as supplied evidence only."
        };
    }

    internal static ControlledApplyGateHint ValidGateHint()
    {
        return new ControlledApplyGateHint
        {
            GateId = "gate-source-change-forbidden",
            Kind = ControlledApplyGateHintKind.SourceChangeForbidden,
            SafeSummary = "Source change remains unavailable in this model."
        };
    }

    internal static ControlledApplyRisk ValidRisk()
    {
        return new ControlledApplyRisk
        {
            RiskId = "risk-source-change",
            Kind = ControlledApplyRiskKind.SourceChangeRisk,
            Severity = ControlledApplyRiskSeverity.High,
            SafeSummary = "Future source change requires a separate implementation and review path."
        };
    }

    internal static void AssertNoAuthority(ControlledApplyPlanResult result)
    {
        Assert.IsFalse(result.IsExecution);
        Assert.IsFalse(result.IsSourceApply);
        Assert.IsFalse(result.IsPatchApplication);
        Assert.IsFalse(result.IsRollbackExecution);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanMutateFiles);
        Assert.IsFalse(result.CanReadSourceFiles);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanRunValidation);
        Assert.IsFalse(result.CanRollback);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanWriteSql);
    }

    internal static void AssertIssue(ControlledApplyPlanResult result, ControlledApplyPlanReason reason)
    {
        Assert.IsTrue(result.Issues.Any(issue => issue.Reason == reason), $"Expected issue {reason}.");
    }

    internal static void AssertContains<T>(IEnumerable<T> values, T expected)
    {
        Assert.IsTrue(values.Contains(expected), $"Expected collection to contain {expected}.");
    }

    internal static void AssertDoesNotContain(string value, string unexpected)
    {
        Assert.IsFalse(value.Contains(unexpected, StringComparison.OrdinalIgnoreCase), $"Unexpected text found: {unexpected}");
    }
}

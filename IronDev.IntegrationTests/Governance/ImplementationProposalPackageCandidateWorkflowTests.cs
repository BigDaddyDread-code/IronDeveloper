using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ImplementationProposalPackageCandidateWorkflowTests
{
    private readonly IImplementationProposalPackageCandidateWorkflow _workflow = new ImplementationProposalPackageCandidateWorkflow();

    [TestMethod]
    public void ImplementationProposalPackage_NullRequestReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.InvalidRequest, result.Status);
        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("workflowRunId")]
    [DataRow("workflowStepId")]
    [DataRow("proposalReferenceId")]
    [DataRow("targetKind")]
    [DataRow("targetReferenceId")]
    public void ImplementationProposalPackage_MissingOrInvalidIdentityReturnsInvalidRequest(string field)
    {
        var request = field switch
        {
            "workflowRunId" => ImplementationProposalPackageFixtures.ValidRequest() with { WorkflowRunId = " " },
            "workflowStepId" => ImplementationProposalPackageFixtures.ValidRequest() with { WorkflowStepId = " " },
            "proposalReferenceId" => ImplementationProposalPackageFixtures.ValidRequest() with { ProposalReferenceId = " " },
            "targetKind" => ImplementationProposalPackageFixtures.ValidRequest() with { TargetKind = ImplementationProposalTargetKind.Unknown },
            _ => ImplementationProposalPackageFixtures.ValidRequest() with { TargetReferenceId = " " }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.InvalidRequest, result.Status);
        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("title")]
    [DataRow("summary")]
    [DataRow("evidence")]
    [DataRow("affectedArea")]
    [DataRow("proposedStep")]
    [DataRow("validationStep")]
    [DataRow("risk")]
    public void ImplementationProposalPackage_UnsafeSafeMaterialFailsClosedWithoutEcho(string field)
    {
        const string marker = "raw prompt leaked";
        var request = field switch
        {
            "title" => ImplementationProposalPackageFixtures.ValidRequest() with { SafeTitle = marker },
            "summary" => ImplementationProposalPackageFixtures.ValidRequest() with { SafeSummary = marker },
            "evidence" => ImplementationProposalPackageFixtures.ValidRequest() with { EvidenceReferences = [ImplementationProposalPackageFixtures.Evidence(summary: marker)] },
            "affectedArea" => ImplementationProposalPackageFixtures.ValidRequest() with { AffectedAreas = [ImplementationProposalPackageFixtures.Area(summary: marker)] },
            "proposedStep" => ImplementationProposalPackageFixtures.ValidRequest() with { ProposedSteps = [ImplementationProposalPackageFixtures.ProposedStep(summary: marker)] },
            "validationStep" => ImplementationProposalPackageFixtures.ValidRequest() with { ValidationSteps = [ImplementationProposalPackageFixtures.ValidationStep(summary: marker)] },
            _ => ImplementationProposalPackageFixtures.ValidRequest() with { Risks = [ImplementationProposalPackageFixtures.Risk(summary: marker)] }
        };

        var result = _workflow.Prepare(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.InvalidRequest, result.Status);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("raw prompt leaked")]
    [DataRow("raw completion leaked")]
    [DataRow("raw tool output leaked")]
    [DataRow("whole patch leaked")]
    [DataRow("patch ready")]
    [DataRow("private reasoning leaked")]
    [DataRow("hidden reasoning leaked")]
    public void ImplementationProposalPackage_RawPrivatePatchOrFullPayloadMarkersFailClosed(string marker)
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { SafeSummary = marker });
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.InvalidRequest, result.Status);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("noEvidence")]
    [DataRow("noAffectedArea")]
    [DataRow("noProposedStep")]
    [DataRow("noValidationStep")]
    [DataRow("noTestFailureReview")]
    [DataRow("noCriticReviewRequest")]
    public void ImplementationProposalPackage_MissingProposalMaterialReturnsMissingRequiredProposalMaterial(string shape)
    {
        var request = shape switch
        {
            "noEvidence" => ImplementationProposalPackageFixtures.ValidRequest() with { EvidenceReferences = [] },
            "noAffectedArea" => ImplementationProposalPackageFixtures.ValidRequest() with { AffectedAreas = [] },
            "noProposedStep" => ImplementationProposalPackageFixtures.ValidRequest() with { ProposedSteps = [] },
            "noValidationStep" => ImplementationProposalPackageFixtures.ValidRequest() with { ValidationSteps = [] },
            "noTestFailureReview" => ImplementationProposalPackageFixtures.ValidRequest() with { TestFailureReview = null },
            _ => ImplementationProposalPackageFixtures.ValidRequest(ImplementationProposalTargetKind.CriticReviewRequest) with { CriticReviewRequest = null }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.MissingRequiredProposalMaterial, result.Status);
        Assert.IsTrue(result.MissingEvidence.Count > 0);
        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void ImplementationProposalPackage_BlockingRunnerEvaluationBlocksPackageProduction()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.BlockedEvaluation() });

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), ImplementationProposalPackageCandidateReason.BlockedByRunnerEvaluation);
    }

    [TestMethod]
    public void ImplementationProposalPackage_BlockingApprovalHaltBlocksPackageProduction()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.ApprovalHaltEvaluation() });

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void ImplementationProposalPackage_BlockingPolicyPreflightBlocksPackageProduction()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.PolicyBlockedEvaluation() });

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void ImplementationProposalPackage_BlockingA2aValidationBlocksPackageProduction()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.A2aBlockedEvaluation() });

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void ImplementationProposalPackage_BlockingDryRunBlocksPackageProduction()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { DryRunResult = TestFailureReviewCandidateFixtures.BlockedDryRun() });

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), ImplementationProposalPackageCandidateReason.BlockedByDryRun);
    }

    [TestMethod]
    public void ImplementationProposalPackage_BlockingRouteSuggestionBlocksPackageProduction()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { RouteSuggestion = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.BlockedApprovalRequired) });

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), ImplementationProposalPackageCandidateReason.BlockedByRouteSuggestion);
    }

    [TestMethod]
    public void ImplementationProposalPackage_InvalidOrBlockedTestFailureReviewBlocksPackageProduction()
    {
        var invalidReview = TestFailureReviewCandidateFixtures.ValidResult() with { Status = TestFailureReviewCandidateStatus.InvalidRequest };
        var blockedReview = TestFailureReviewCandidateFixtures.ValidResult() with { Status = TestFailureReviewCandidateStatus.BlockedByWorkflowGate };

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { TestFailureReview = invalidReview }).Status);
        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest() with { TestFailureReview = blockedReview }).Status);
    }

    [TestMethod]
    public void ImplementationProposalPackage_InvalidOrBlockedCriticReviewRequestBlocksPackageProduction()
    {
        var invalidReview = CriticReviewRequestCandidateFixtures.ValidResult() with { Status = CriticReviewRequestCandidateStatus.InvalidRequest };
        var blockedReview = CriticReviewRequestCandidateFixtures.ValidResult() with { Status = CriticReviewRequestCandidateStatus.BlockedByWorkflowGate };

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest(ImplementationProposalTargetKind.CriticReviewRequest) with { CriticReviewRequest = invalidReview }).Status);
        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate, _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest(ImplementationProposalTargetKind.CriticReviewRequest) with { CriticReviewRequest = blockedReview }).Status);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ValidTestFailureReviewAndCriticReviewCanBePackaged()
    {
        var testFailureTarget = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest());
        var criticReviewTarget = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest(ImplementationProposalTargetKind.CriticReviewRequest));

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.ProposalPackageProduced, testFailureTarget.Status);
        Assert.IsTrue(testFailureTarget.EvidenceReferences.Any(reference => reference.Kind == ImplementationProposalEvidenceKind.TestFailureReviewReference));
        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.ProposalPackageProduced, criticReviewTarget.Status);
        Assert.IsTrue(criticReviewTarget.EvidenceReferences.Any(reference => reference.Kind == ImplementationProposalEvidenceKind.CriticReviewRequestReference));
    }

    [TestMethod]
    public void ImplementationProposalPackage_ValidRequestProducesProposalPackage()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest());

        Assert.AreEqual(ImplementationProposalPackageCandidateStatus.ProposalPackageProduced, result.Status);
        Assert.AreEqual("implementation-proposal-129", result.ProposalReferenceId);
        Assert.IsTrue(result.ProposalPackageReferenceId.StartsWith("implementation-proposal-package:", StringComparison.Ordinal));
        Assert.IsTrue(result.EvidenceReferences.Count > 0);
        Assert.IsTrue(result.AffectedAreas.Count > 0);
        Assert.IsTrue(result.ProposedSteps.Count > 0);
        Assert.IsTrue(result.ValidationSteps.Count > 0);
        Assert.IsTrue(result.Risks.Count > 0);
        Assert.IsTrue(result.SafePackageSummaryLines.Count > 0);
        ImplementationProposalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ProducedPackageStatesNonImplementationBoundary()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest());
        var summary = string.Join("\n", result.SafePackageSummaryLines);

        StringAssert.Contains(summary, "No implementation was performed.");
        StringAssert.Contains(summary, "No code was generated.");
        StringAssert.Contains(summary, "No patch was generated.");
        StringAssert.Contains(summary, "No source mutation was performed.");
        StringAssert.Contains(summary, "No tests were run.");
    }

    [TestMethod]
    public void ImplementationProposalPackage_ProducedPackageHasHardFalseAuthorityFlags()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest());

        Assert.IsTrue(result.IsProposalOnly);
        Assert.IsFalse(result.IsImplementation);
        Assert.IsFalse(result.IsPatch);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanGenerateCode);
        Assert.IsFalse(result.CanRunTests);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [TestMethod]
    public void ImplementationProposalPackage_SameRequestProducesSameResult()
    {
        var request = ImplementationProposalPackageFixtures.ValidRequest();
        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ResultSerializesWithoutRawPrivatePatchOrAuthorityMaterial()
    {
        var result = _workflow.Prepare(ImplementationProposalPackageFixtures.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        AssertDoesNotContainAny(
            json,
            "private reasoning",
            "hidden reasoning",
            "raw prompt",
            "raw completion",
            "raw tool output",
            "whole patch",
            "patch payload",
            "implementation complete",
            "code generated",
            "patch ready",
            "approval granted",
            "policy satisfied",
            "workflow may continue",
            "run this command",
            "ticket created");
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Unexpected unsafe text: {value}");
    }
}

internal static class ImplementationProposalPackageFixtures
{
    public static ImplementationProposalPackageCandidateRequest ValidRequest(ImplementationProposalTargetKind targetKind = ImplementationProposalTargetKind.TestFailureReviewCandidate) =>
        new()
        {
            WorkflowRunId = "workflow-run-129",
            WorkflowStepId = "workflow-step-implementation-proposal-package",
            ProposalReferenceId = "implementation-proposal-129",
            TargetKind = targetKind,
            TargetReferenceId = targetKind == ImplementationProposalTargetKind.CriticReviewRequest ? "critic-review-request-target-128" : "test-failure-review-target-127",
            SafeTitle = "Prepare implementation proposal from supplied review material.",
            SafeSummary = "Supplied review material supports a later implementation proposal review.",
            EvidenceReferences =
            [
                Evidence(ImplementationProposalEvidenceKind.WorkflowStepEvaluationReference, "workflow-step-evaluation-129", "Supplied workflow step evaluation snapshot."),
                Evidence(ImplementationProposalEvidenceKind.DryRunResultReference, "dry-run-result-129", "Supplied dry-run result snapshot.")
            ],
            AffectedAreas =
            [
                Area(ImplementationAffectedAreaKind.ComponentReference, "component-reference-129", "Affected component reference only."),
                Area(ImplementationAffectedAreaKind.TestSuiteReference, "test-suite-reference-129", "Affected test suite reference only.")
            ],
            ProposedSteps =
            [
                ProposedStep(1, ImplementationProposalStepKind.InspectSuppliedEvidence, "Inspect supplied evidence references."),
                ProposedStep(2, ImplementationProposalStepKind.PlanMinimalCodeChange, "Plan a minimal code change later.")
            ],
            ValidationSteps =
            [
                ValidationStep(1, ImplementationValidationStepKind.ReviewExistingTestEvidence, "Review existing supplied test evidence."),
                ValidationStep(2, ImplementationValidationStepKind.ProposeFocusedTestRunLater, "Propose a focused test run later.")
            ],
            Risks =
            [
                Risk(ImplementationProposalRiskKind.SourceMutationRisk, ImplementationProposalSeverityHint.High, "Later source mutation would require separate approval."),
                Risk(ImplementationProposalRiskKind.TestCoverageRisk, ImplementationProposalSeverityHint.Medium, "Later validation should confirm test coverage.")
            ],
            TestFailureReview = TestFailureReviewCandidateFixtures.ValidResult(),
            CriticReviewRequest = CriticReviewRequestCandidateFixtures.ValidResult(),
            StepEvaluation = TestFailureReviewCandidateFixtures.EligibleEvaluation(),
            DryRunResult = TestFailureReviewCandidateFixtures.CompletedDryRun(),
            RouteSuggestion = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable),
            CorrelationId = "correlation-129"
        };

    public static ImplementationProposalEvidenceReference Evidence(ImplementationProposalEvidenceKind kind = ImplementationProposalEvidenceKind.ExternalArtifactReference, string referenceId = "evidence-reference-129", string? summary = "Supplied safe evidence summary.") =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            SafeSummary = summary
        };

    public static ImplementationAffectedAreaReference Area(ImplementationAffectedAreaKind kind = ImplementationAffectedAreaKind.ProjectArea, string referenceId = "area-reference-129", string? summary = "Supplied safe affected area summary.") =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            SafeSummary = summary
        };

    public static ImplementationProposalStep ProposedStep(int order = 1, ImplementationProposalStepKind kind = ImplementationProposalStepKind.InspectSuppliedEvidence, string summary = "Supplied safe proposal step summary.") =>
        new()
        {
            Order = order,
            Kind = kind,
            SafeSummary = summary
        };

    public static ImplementationValidationStep ValidationStep(int order = 1, ImplementationValidationStepKind kind = ImplementationValidationStepKind.ReviewExistingTestEvidence, string summary = "Supplied safe validation step summary.") =>
        new()
        {
            Order = order,
            Kind = kind,
            SafeSummary = summary
        };

    public static ImplementationProposalRisk Risk(ImplementationProposalRiskKind kind = ImplementationProposalRiskKind.RegressionRisk, ImplementationProposalSeverityHint severity = ImplementationProposalSeverityHint.Medium, string? summary = "Supplied safe risk summary.") =>
        new()
        {
            Kind = kind,
            SeverityHint = severity,
            SafeSummary = summary
        };

    public static ImplementationProposalPackageCandidateResult ValidResult() =>
        new ImplementationProposalPackageCandidateWorkflow().Prepare(ValidRequest());

    public static void AssertNoAuthority(ImplementationProposalPackageCandidateResult result)
    {
        Assert.IsTrue(result.IsProposalOnly);
        Assert.IsFalse(result.IsImplementation);
        Assert.IsFalse(result.IsPatch);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanGenerateCode);
        Assert.IsFalse(result.CanRunTests);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }
}

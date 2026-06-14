using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class CriticReviewRequestCandidateWorkflowTests
{
    private readonly ICriticReviewRequestCandidateWorkflow _workflow = new CriticReviewRequestCandidateWorkflow();

    [TestMethod]
    public void CriticReviewRequestCandidate_NullRequestReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(CriticReviewRequestCandidateStatus.InvalidRequest, result.Status);
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("workflowRunId")]
    [DataRow("workflowStepId")]
    [DataRow("reviewRequestReferenceId")]
    [DataRow("targetKind")]
    [DataRow("targetReferenceId")]
    public void CriticReviewRequestCandidate_MissingOrInvalidIdentityReturnsInvalidRequest(string field)
    {
        var request = field switch
        {
            "workflowRunId" => CriticReviewRequestCandidateFixtures.ValidRequest() with { WorkflowRunId = " " },
            "workflowStepId" => CriticReviewRequestCandidateFixtures.ValidRequest() with { WorkflowStepId = " " },
            "reviewRequestReferenceId" => CriticReviewRequestCandidateFixtures.ValidRequest() with { ReviewRequestReferenceId = " " },
            "targetKind" => CriticReviewRequestCandidateFixtures.ValidRequest() with { TargetKind = CriticReviewTargetKind.Unknown },
            _ => CriticReviewRequestCandidateFixtures.ValidRequest() with { TargetReferenceId = " " }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(CriticReviewRequestCandidateStatus.InvalidRequest, result.Status);
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("safeTitle")]
    [DataRow("safeSummary")]
    [DataRow("reviewQuestion")]
    [DataRow("evidenceSummary")]
    [DataRow("riskHint")]
    [DataRow("contextLine")]
    public void CriticReviewRequestCandidate_UnsafeSafeMaterialFailsClosedWithoutEcho(string field)
    {
        const string marker = "raw prompt leaked";
        var request = field switch
        {
            "safeTitle" => CriticReviewRequestCandidateFixtures.ValidRequest() with { SafeTitle = marker },
            "safeSummary" => CriticReviewRequestCandidateFixtures.ValidRequest() with { SafeSummary = marker },
            "reviewQuestion" => CriticReviewRequestCandidateFixtures.ValidRequest() with { ReviewQuestions = [CriticReviewRequestCandidateFixtures.Question(marker)] },
            "evidenceSummary" => CriticReviewRequestCandidateFixtures.ValidRequest() with { EvidenceReferences = [CriticReviewRequestCandidateFixtures.Evidence(summary: marker)] },
            "riskHint" => CriticReviewRequestCandidateFixtures.ValidRequest() with { RiskHints = [CriticReviewRequestCandidateFixtures.Risk(summary: marker)] },
            _ => CriticReviewRequestCandidateFixtures.ValidRequest() with { SafeContextLines = [marker] }
        };

        var result = _workflow.Prepare(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(CriticReviewRequestCandidateStatus.InvalidRequest, result.Status);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("raw prompt leaked")]
    [DataRow("raw completion leaked")]
    [DataRow("raw tool output leaked")]
    [DataRow("whole patch leaked")]
    [DataRow("private reasoning leaked")]
    [DataRow("hidden reasoning leaked")]
    [DataRow("chain of thought leaked")]
    public void CriticReviewRequestCandidate_RawPrivateOrFullPayloadMarkersFailClosed(string marker)
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { SafeSummary = marker });
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(CriticReviewRequestCandidateStatus.InvalidRequest, result.Status);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("noQuestions")]
    [DataRow("noEvidence")]
    [DataRow("noSummary")]
    [DataRow("noTestFailureReview")]
    public void CriticReviewRequestCandidate_MissingReviewMaterialReturnsMissingRequiredReviewMaterial(string shape)
    {
        var request = shape switch
        {
            "noQuestions" => CriticReviewRequestCandidateFixtures.ValidRequest() with { ReviewQuestions = [] },
            "noEvidence" => CriticReviewRequestCandidateFixtures.ValidRequest() with { EvidenceReferences = [] },
            "noSummary" => CriticReviewRequestCandidateFixtures.ValidRequest() with { SafeSummary = " " },
            _ => CriticReviewRequestCandidateFixtures.ValidRequest() with { TestFailureReview = null }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(CriticReviewRequestCandidateStatus.MissingRequiredReviewMaterial, result.Status);
        Assert.IsTrue(result.MissingEvidence.Count > 0);
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockingRunnerEvaluationBlocksPackageProduction()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.BlockedEvaluation() });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockingApprovalHaltBlocksPackageProduction()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.ApprovalHaltEvaluation() });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockingPolicyPreflightBlocksPackageProduction()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.PolicyBlockedEvaluation() });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockingA2aValidationBlocksPackageProduction()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.A2aBlockedEvaluation() });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockingDryRunBlocksPackageProduction()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { DryRunResult = TestFailureReviewCandidateFixtures.BlockedDryRun() });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), CriticReviewRequestCandidateReason.BlockedByDryRun);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockingRouteSuggestionBlocksPackageProduction()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { RouteSuggestion = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.BlockedApprovalRequired) });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), CriticReviewRequestCandidateReason.BlockedByRouteSuggestion);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_InvalidTestFailureReviewBlocksPackageProduction()
    {
        var invalidReview = TestFailureReviewCandidateFixtures.ValidResult() with { Status = TestFailureReviewCandidateStatus.InvalidRequest };
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { TestFailureReview = invalidReview });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_BlockedTestFailureReviewBlocksPackageProduction()
    {
        var blockedReview = TestFailureReviewCandidateFixtures.ValidResult() with { Status = TestFailureReviewCandidateStatus.BlockedByWorkflowGate };
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest() with { TestFailureReview = blockedReview });

        Assert.AreEqual(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ValidTestFailureReviewCanBePackagedAsReviewTarget()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest());

        Assert.AreEqual(CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced, result.Status);
        Assert.AreEqual(CriticReviewTargetKind.TestFailureReviewCandidate, result.TargetKind);
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == CriticReviewEvidenceKind.TestFailureReviewReference));
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ValidRequestProducesReviewRequestPackage()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest());

        Assert.AreEqual(CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced, result.Status);
        Assert.AreEqual("critic-review-request-128", result.ReviewRequestReferenceId);
        Assert.IsTrue(result.ReviewPackageReferenceId.StartsWith("critic-review-request:", StringComparison.Ordinal));
        Assert.IsTrue(result.ReviewQuestions.Count > 0);
        Assert.IsTrue(result.EvidenceReferences.Count > 0);
        Assert.IsTrue(result.RiskHints.Count > 0);
        Assert.IsTrue(result.SafePackageSummaryLines.Count > 0);
        CriticReviewRequestCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ProducedPackageStatesNonReviewBoundary()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest());
        var summary = string.Join("\n", result.SafePackageSummaryLines);

        StringAssert.Contains(summary, "No CriticAgent was dispatched.");
        StringAssert.Contains(summary, "No model was called.");
        StringAssert.Contains(summary, "No prompt was built.");
        StringAssert.Contains(summary, "No review decision was made.");
        StringAssert.Contains(summary, "No source mutation was performed.");
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ProducedPackageHasHardFalseAuthorityFlags()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest());

        Assert.IsTrue(result.IsReviewRequestOnly);
        Assert.IsFalse(result.IsReviewDecision);
        Assert.IsFalse(result.CanDispatchCriticAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanPostReviewComment);
        Assert.IsFalse(result.CanApprove);
        Assert.IsFalse(result.CanReject);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_SameRequestProducesSameResult()
    {
        var request = CriticReviewRequestCandidateFixtures.ValidRequest();
        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ResultSerializesWithoutRawPrivateFullPayloadOrAuthorityMaterial()
    {
        var result = _workflow.Prepare(CriticReviewRequestCandidateFixtures.ValidRequest());
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
            "critic approved",
            "critic rejected",
            "approval granted",
            "policy satisfied",
            "workflow may continue",
            "model found the issue",
            "root cause confirmed",
            "patch should be applied",
            "ticket created");
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Unexpected unsafe text: {value}");
    }
}

internal static class CriticReviewRequestCandidateFixtures
{
    public static CriticReviewRequestCandidateRequest ValidRequest() =>
        new()
        {
            WorkflowRunId = "workflow-run-128",
            WorkflowStepId = "workflow-step-critic-review-request",
            ReviewRequestReferenceId = "critic-review-request-128",
            TargetKind = CriticReviewTargetKind.TestFailureReviewCandidate,
            TargetReferenceId = "test-failure-review-target-127",
            SafeTitle = "Review supplied test failure candidate material.",
            SafeSummary = "Supplied test failure candidate material needs later human critic review.",
            ReviewQuestions =
            [
                Question("Is the supplied failure evidence enough for later implementation planning?", CriticReviewQuestionKind.EvidenceSufficiency, CriticReviewSeverityHint.High),
                Question("Are there boundary overclaims in the candidate material?", CriticReviewQuestionKind.BoundaryRisk, CriticReviewSeverityHint.Medium)
            ],
            EvidenceReferences =
            [
                Evidence(CriticReviewEvidenceKind.WorkflowStepEvaluationReference, "workflow-step-evaluation-128", "Supplied workflow step evaluation snapshot."),
                Evidence(CriticReviewEvidenceKind.DryRunResultReference, "dry-run-result-128", "Supplied dry-run result snapshot.")
            ],
            RiskHints =
            [
                Risk(CriticReviewRiskKind.InsufficientEvidence, CriticReviewSeverityHint.Medium, "Later review should check evidence sufficiency."),
                Risk(CriticReviewRiskKind.PossibleOverclaim, CriticReviewSeverityHint.Low, "Later review should check for overclaiming.")
            ],
            SafeContextLines = ["Supplied context line only."],
            TestFailureReview = TestFailureReviewCandidateFixtures.ValidResult(),
            StepEvaluation = TestFailureReviewCandidateFixtures.EligibleEvaluation(),
            DryRunResult = TestFailureReviewCandidateFixtures.CompletedDryRun(),
            RouteSuggestion = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable),
            CorrelationId = "correlation-128"
        };

    public static CriticReviewQuestion Question(string safeQuestion, CriticReviewQuestionKind kind = CriticReviewQuestionKind.EvidenceSufficiency, CriticReviewSeverityHint severity = CriticReviewSeverityHint.Medium) =>
        new()
        {
            Kind = kind,
            SafeQuestion = safeQuestion,
            SeverityHint = severity
        };

    public static CriticReviewEvidenceReference Evidence(CriticReviewEvidenceKind kind = CriticReviewEvidenceKind.ExternalArtifactReference, string referenceId = "evidence-reference-128", string? summary = "Supplied safe evidence summary.") =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            SafeSummary = summary
        };

    public static CriticReviewRiskHint Risk(CriticReviewRiskKind kind = CriticReviewRiskKind.TestEvidenceRisk, CriticReviewSeverityHint severity = CriticReviewSeverityHint.Medium, string? summary = "Supplied safe risk summary.") =>
        new()
        {
            Kind = kind,
            SeverityHint = severity,
            SafeSummary = summary
        };

    public static CriticReviewRequestCandidateResult ValidResult() =>
        new CriticReviewRequestCandidateWorkflow().Prepare(ValidRequest());

    public static void AssertNoAuthority(CriticReviewRequestCandidateResult result)
    {
        Assert.IsTrue(result.IsReviewRequestOnly);
        Assert.IsFalse(result.IsReviewDecision);
        Assert.IsFalse(result.CanDispatchCriticAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanPostReviewComment);
        Assert.IsFalse(result.CanApprove);
        Assert.IsFalse(result.CanReject);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }
}

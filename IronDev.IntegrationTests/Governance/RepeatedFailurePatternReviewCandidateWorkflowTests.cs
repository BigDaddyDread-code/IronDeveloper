using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class RepeatedFailurePatternReviewCandidateWorkflowTests
{
    private readonly IRepeatedFailurePatternReviewCandidateWorkflow _workflow = new RepeatedFailurePatternReviewCandidateWorkflow();

    [TestMethod]
    public void RepeatedFailurePatternReview_NullRequestReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.InvalidRequest, result.Status);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("workflow", RepeatedFailurePatternReviewCandidateReason.MissingWorkflowRunId)]
    [DataRow("step", RepeatedFailurePatternReviewCandidateReason.MissingWorkflowStepId)]
    [DataRow("pattern", RepeatedFailurePatternReviewCandidateReason.MissingPatternReviewReference)]
    [DataRow("project", RepeatedFailurePatternReviewCandidateReason.MissingProjectReference)]
    public void RepeatedFailurePatternReview_MissingRequiredIdentityReturnsInvalidRequest(string field, RepeatedFailurePatternReviewCandidateReason expectedReason)
    {
        var request = field switch
        {
            "workflow" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { WorkflowRunId = " " },
            "step" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { WorkflowStepId = " " },
            "pattern" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { PatternReviewReferenceId = " " },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ProjectReferenceId = " " }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("category")]
    [DataRow("frequency")]
    [DataRow("recency")]
    [DataRow("confidence")]
    [DataRow("occurrence-kind")]
    [DataRow("evidence-kind")]
    [DataRow("validation-kind")]
    [DataRow("validation-outcome")]
    [DataRow("candidate-kind")]
    [DataRow("gate-kind")]
    [DataRow("risk-kind")]
    public void RepeatedFailurePatternReview_UnknownHintsOrKindsReturnInvalidRequest(string field)
    {
        var request = field switch
        {
            "category" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { CategoryHint = RepeatedFailurePatternCategoryHint.Unknown },
            "frequency" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { FrequencyHint = RepeatedFailureFrequencyHint.Unknown },
            "recency" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { RecencyHint = RepeatedFailureRecencyHint.Unknown },
            "confidence" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ConfidenceHint = RepeatedFailureConfidenceHint.Unknown },
            "occurrence-kind" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { OccurrenceReferences = [RepeatedFailurePatternReviewFixtures.Occurrence(RepeatedFailureOccurrenceKind.Unknown)] },
            "evidence-kind" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { EvidenceReferences = [RepeatedFailurePatternReviewFixtures.Evidence(RepeatedFailureEvidenceKind.Unknown)] },
            "validation-kind" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ValidationReferences = [RepeatedFailurePatternReviewFixtures.Validation(RepeatedFailureValidationKind.Unknown)] },
            "validation-outcome" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ValidationReferences = [RepeatedFailurePatternReviewFixtures.Validation(outcomeHint: RepeatedFailureValidationOutcomeHint.Unknown)] },
            "candidate-kind" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { CandidatePackageReferences = [RepeatedFailurePatternReviewFixtures.Candidate(RepeatedFailureCandidatePackageKind.Unknown)] },
            "gate-kind" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { GateHints = [RepeatedFailurePatternReviewFixtures.Gate(RepeatedFailureReviewGateKind.Unknown)] },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { Risks = [RepeatedFailurePatternReviewFixtures.Risk(RepeatedFailureRiskKind.Unknown)] }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.InvalidRequest, result.Status);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("title", "raw prompt leaked")]
    [DataRow("summary", "root cause found")]
    [DataRow("occurrence", "raw completion leaked")]
    [DataRow("evidence", "query history")]
    [DataRow("validation", "pattern proven")]
    [DataRow("candidate", "memory promoted")]
    [DataRow("gate", "workflow may continue")]
    [DataRow("risk", "release ready")]
    [DataRow("correlation", "private reasoning")]
    [DataRow("pattern", "ticket created")]
    public void RepeatedFailurePatternReview_UnsafeSafeMaterialFailsClosedWithoutEcho(string field, string marker)
    {
        var request = field switch
        {
            "title" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { SafePatternTitle = marker },
            "summary" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { SafePatternSummary = marker },
            "occurrence" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { OccurrenceReferences = [RepeatedFailurePatternReviewFixtures.Occurrence(summary: marker), RepeatedFailurePatternReviewFixtures.Occurrence(referenceId: "occurrence-2")] },
            "evidence" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { EvidenceReferences = [RepeatedFailurePatternReviewFixtures.Evidence(summary: marker)] },
            "validation" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ValidationReferences = [RepeatedFailurePatternReviewFixtures.Validation(summary: marker)] },
            "candidate" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { CandidatePackageReferences = [RepeatedFailurePatternReviewFixtures.Candidate(summary: marker)] },
            "gate" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { GateHints = [RepeatedFailurePatternReviewFixtures.Gate(summary: marker)] },
            "risk" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { Risks = [RepeatedFailurePatternReviewFixtures.Risk(summary: marker)] },
            "correlation" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { CorrelationId = marker },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { PatternReviewReferenceId = marker }
        };

        var result = _workflow.Prepare(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("title", "safe pattern title")]
    [DataRow("summary", "safe pattern summary")]
    [DataRow("occurrence", "at least two supplied occurrence references")]
    [DataRow("evidence", "repeated failure evidence reference")]
    [DataRow("validation", "validation reference")]
    [DataRow("gate", "review gate hint")]
    public void RepeatedFailurePatternReview_MissingRequiredMaterialReturnsMissingEvidence(string field, string expectedMissing)
    {
        var request = field switch
        {
            "title" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { SafePatternTitle = " " },
            "summary" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { SafePatternSummary = " " },
            "occurrence" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { OccurrenceReferences = [RepeatedFailurePatternReviewFixtures.Occurrence()] },
            "evidence" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { EvidenceReferences = [] },
            "validation" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ValidationReferences = [] },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { GateHints = [] }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.MissingRequiredPatternEvidence, result.Status);
        CollectionAssert.Contains(result.MissingEvidence.ToList(), expectedMissing);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void RepeatedFailurePatternReview_SingleOccurrenceFrequencyRequiresMoreEvidence()
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            FrequencyHint = RepeatedFailureFrequencyHint.SingleOccurrenceOnly
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.MissingRequiredPatternEvidence, result.Status);
        CollectionAssert.Contains(result.MissingEvidence.ToList(), "at least two supplied occurrence references");
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("runner-boundary")]
    [DataRow("runner-approval")]
    [DataRow("dry-run-policy")]
    [DataRow("route-policy")]
    [DataRow("route-authority")]
    public void RepeatedFailurePatternReview_BlockingWorkflowSnapshotsReturnBlockedByWorkflowGate(string field)
    {
        var request = field switch
        {
            "runner-boundary" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { StepEvaluation = RepeatedFailurePatternReviewFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary) },
            "runner-approval" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { StepEvaluation = RepeatedFailurePatternReviewFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedApprovalRequired) },
            "dry-run-policy" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { DryRunResult = RepeatedFailurePatternReviewFixtures.DryRun(WorkflowDryRunStatus.BlockedByPolicyPreflight) },
            "route-authority" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { RouteSuggestion = RepeatedFailurePatternReviewFixtures.Route(BoxedLangGraphRouteLabel.EligibleForDryRun, authority: true) },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { RouteSuggestion = RepeatedFailurePatternReviewFixtures.Route(BoxedLangGraphRouteLabel.BlockedPolicyPreflight) }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("dogfood")]
    [DataRow("human")]
    [DataRow("memory")]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void RepeatedFailurePatternReview_InvalidUpstreamPackageBlocks(string upstream)
    {
        var request = upstream switch
        {
            "dogfood" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { DogfoodEvidenceBundle = RepeatedFailurePatternReviewFixtures.Dogfood(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate) },
            "human" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { HumanApprovalPackage = DogfoodEvidenceBundleFixtures.HumanApproval(HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate) },
            "memory" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate) },
            "tool" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate) },
            "implementation" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate) },
            "critic" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate) },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { TestFailureReview = HumanApprovalPackageFixtures.TestFailure(TestFailureReviewCandidateStatus.BlockedByWorkflowGate) }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("dogfood")]
    [DataRow("human")]
    [DataRow("memory")]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void RepeatedFailurePatternReview_ProducedUpstreamPackagesAreIncludedAsEvidenceOnly(string upstream)
    {
        var request = upstream switch
        {
            "dogfood" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { DogfoodEvidenceBundle = RepeatedFailurePatternReviewFixtures.Dogfood() },
            "human" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { HumanApprovalPackage = DogfoodEvidenceBundleFixtures.HumanApproval() },
            "memory" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement() },
            "tool" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview() },
            "implementation" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal() },
            "critic" => RepeatedFailurePatternReviewFixtures.ValidRequest() with { CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview() },
            _ => RepeatedFailurePatternReviewFixtures.ValidRequest() with { TestFailureReview = HumanApprovalPackageFixtures.TestFailure() }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.IsTrue(result.CandidatePackageReferences.Count >= 2);
        Assert.IsTrue(result.EvidenceReferences.Count >= 2);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(RepeatedFailureValidationOutcomeHint.SuppliedPassed)]
    [DataRow(RepeatedFailureValidationOutcomeHint.SuppliedFailed)]
    [DataRow(RepeatedFailureValidationOutcomeHint.SuppliedBlocked)]
    [DataRow(RepeatedFailureValidationOutcomeHint.SuppliedPartial)]
    [DataRow(RepeatedFailureValidationOutcomeHint.SuppliedNotRun)]
    public void RepeatedFailurePatternReview_SuppliedValidationOutcomeHintIsRecordedWithoutBecomingProof(RepeatedFailureValidationOutcomeHint outcomeHint)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            ValidationReferences = [RepeatedFailurePatternReviewFixtures.Validation(outcomeHint: outcomeHint)]
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.AreEqual(outcomeHint, result.ValidationReferences.Single().OutcomeHint);
        Assert.IsFalse(result.IsPatternProof);
        Assert.IsFalse(result.IsRootCauseProof);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedAssertionFailure, RepeatedFailureFrequencyHint.TwoOccurrencesSupplied, RepeatedFailureRecencyHint.SuppliedRecent, RepeatedFailureConfidenceHint.Medium)]
    [DataRow(RepeatedFailurePatternCategoryHint.MixedOrUnclearPattern, RepeatedFailureFrequencyHint.ThreeOrMoreOccurrencesSupplied, RepeatedFailureRecencyHint.SuppliedMixedRecency, RepeatedFailureConfidenceHint.Low)]
    [DataRow(RepeatedFailurePatternCategoryHint.RepeatedPolicyOrApprovalBlock, RepeatedFailureFrequencyHint.SuppliedFrequent, RepeatedFailureRecencyHint.SuppliedOlder, RepeatedFailureConfidenceHint.High)]
    public void RepeatedFailurePatternReview_HintsAreRecordedAsHintsOnly(
        RepeatedFailurePatternCategoryHint category,
        RepeatedFailureFrequencyHint frequency,
        RepeatedFailureRecencyHint recency,
        RepeatedFailureConfidenceHint confidence)
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            CategoryHint = category,
            FrequencyHint = frequency,
            RecencyHint = recency,
            ConfidenceHint = confidence
        });

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.AreEqual(category, result.CategoryHint);
        Assert.AreEqual(frequency, result.FrequencyHint);
        Assert.AreEqual(recency, result.RecencyHint);
        Assert.AreEqual(confidence, result.ConfidenceHint);
        Assert.IsFalse(result.IsPatternProof);
        Assert.IsFalse(result.IsRootCauseProof);
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void RepeatedFailurePatternReview_ValidRequestProducesReviewPackage()
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest());

        Assert.AreEqual(RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced, result.Status);
        Assert.AreEqual("workflow-run-1", result.WorkflowRunId);
        Assert.AreEqual("workflow-step-1", result.WorkflowStepId);
        Assert.AreEqual("repeated-failure-pattern-review-request-1", result.PatternReviewReferenceId);
        Assert.IsTrue(result.PackageReferenceId.StartsWith("repeated-failure-pattern-review:", StringComparison.Ordinal));
        Assert.IsTrue(result.OccurrenceReferences.Count >= 2);
        Assert.IsTrue(result.EvidenceReferences.Count > 0);
        Assert.IsTrue(result.ValidationReferences.Count > 0);
        Assert.IsTrue(result.CandidatePackageReferences.Count > 0);
        Assert.IsTrue(result.GateHints.Count > 0);
        Assert.IsTrue(result.Risks.Count > 0);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.ReviewOnly);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.PatternNotProven);
        CollectionAssert.Contains(result.Reasons.ToList(), RepeatedFailurePatternReviewCandidateReason.RootCauseNotProven);
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Repeated failure pattern review package was produced from supplied references only.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Pattern is not proven.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Root cause is not proven.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "History was not queried.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Memory was not queried.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Logs and reports were not read.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Ticket or incident was not created.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Workflow was not transitioned.");
        CollectionAssert.Contains(result.SafeFollowUpReviewQuestions.ToList(), "Which supplied occurrences share the same failure shape?");
        RepeatedFailurePatternReviewFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void RepeatedFailurePatternReview_OutputIsDeterministic()
    {
        var request = RepeatedFailurePatternReviewFixtures.ValidRequest() with
        {
            EvidenceReferences =
            [
                RepeatedFailurePatternReviewFixtures.Evidence(RepeatedFailureEvidenceKind.WorkflowStepEvaluationReference, "workflow-step-eval-2"),
                RepeatedFailurePatternReviewFixtures.Evidence(RepeatedFailureEvidenceKind.GovernanceEventReference, "governance-event-1")
            ],
            OccurrenceReferences =
            [
                RepeatedFailurePatternReviewFixtures.Occurrence(RepeatedFailureOccurrenceKind.RunReportReference, "run-report-2"),
                RepeatedFailurePatternReviewFixtures.Occurrence(RepeatedFailureOccurrenceKind.GovernanceEventReference, "governance-event-1")
            ]
        };

        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void RepeatedFailurePatternReview_SerializedResultContainsNoRawPrivateFullPayloadOrAuthorityMarkers()
    {
        var result = _workflow.Prepare(RepeatedFailurePatternReviewFixtures.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        AssertDoesNotContainAny(json,
            "raw prompt",
            "raw completion",
            "raw tool output",
            "raw log",
            "raw trace",
            "raw report",
            "private reasoning",
            "hidden reasoning",
            "chain-of-thought",
            "Pattern detected",
            "Pattern proven",
            "Root cause found",
            "Incident created",
            "Ticket created",
            "Memory promoted",
            "Workflow may continue",
            "Release ready",
            "source mutated",
            "tool invoked",
            "sql written");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

internal static class RepeatedFailurePatternReviewFixtures
{
    public static RepeatedFailurePatternReviewCandidateRequest ValidRequest() => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        PatternReviewReferenceId = "repeated-failure-pattern-review-request-1",
        ProjectReferenceId = "project-1",
        SafePatternTitle = "Repeated assertion failure review",
        SafePatternSummary = "Supplied occurrence references may share a recurring assertion shape for later review.",
        CategoryHint = RepeatedFailurePatternCategoryHint.RepeatedAssertionFailure,
        FrequencyHint = RepeatedFailureFrequencyHint.TwoOccurrencesSupplied,
        RecencyHint = RepeatedFailureRecencyHint.SuppliedRecent,
        ConfidenceHint = RepeatedFailureConfidenceHint.Medium,
        OccurrenceReferences =
        [
            Occurrence(RepeatedFailureOccurrenceKind.TestFailureReviewReference, "test-failure-review-1"),
            Occurrence(RepeatedFailureOccurrenceKind.RunReportReference, "run-report-2")
        ],
        EvidenceReferences =
        [
            Evidence(RepeatedFailureEvidenceKind.GovernanceEventReference, "governance-event-1"),
            Evidence(RepeatedFailureEvidenceKind.WorkflowStepEvaluationReference, "workflow-step-evaluation-1")
        ],
        ValidationReferences =
        [
            Validation(RepeatedFailureValidationKind.SuppliedFocusedTestBandReference, "focused-test-band-1", RepeatedFailureValidationOutcomeHint.SuppliedFailed),
            Validation(RepeatedFailureValidationKind.SuppliedWorkflowSweepReference, "workflow-sweep-1", RepeatedFailureValidationOutcomeHint.SuppliedPartial)
        ],
        CandidatePackageReferences =
        [
            Candidate(RepeatedFailureCandidatePackageKind.TestFailureReviewCandidate, "test-failure-review-candidate-1")
        ],
        GateHints =
        [
            Gate(RepeatedFailureReviewGateKind.HumanReviewRequired),
            Gate(RepeatedFailureReviewGateKind.PatternProofNotClaimed),
            Gate(RepeatedFailureReviewGateKind.RootCauseProofNotClaimed)
        ],
        Risks =
        [
            Risk(RepeatedFailureRiskKind.PatternOverclaimRisk),
            Risk(RepeatedFailureRiskKind.RootCauseOverclaimRisk)
        ]
    };

    public static RepeatedFailureOccurrenceReference Occurrence(
        RepeatedFailureOccurrenceKind kind = RepeatedFailureOccurrenceKind.TestFailureReviewReference,
        string referenceId = "test-failure-review-1",
        string? summary = "Occurrence reference supplied for later review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static RepeatedFailureEvidenceReference Evidence(
        RepeatedFailureEvidenceKind kind = RepeatedFailureEvidenceKind.GovernanceEventReference,
        string referenceId = "governance-event-1",
        string? summary = "Evidence reference supplied for later review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static RepeatedFailureValidationReference Validation(
        RepeatedFailureValidationKind kind = RepeatedFailureValidationKind.SuppliedFocusedTestBandReference,
        string referenceId = "focused-test-band-1",
        RepeatedFailureValidationOutcomeHint outcomeHint = RepeatedFailureValidationOutcomeHint.SuppliedFailed,
        string? summary = "Validation outcome hint supplied for review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        OutcomeHint = outcomeHint,
        SafeSummary = summary
    };

    public static RepeatedFailureCandidatePackageReference Candidate(
        RepeatedFailureCandidatePackageKind kind = RepeatedFailureCandidatePackageKind.TestFailureReviewCandidate,
        string referenceId = "test-failure-review-candidate-1",
        string? summary = "Candidate package reference supplied for review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static RepeatedFailureReviewGateHint Gate(
        RepeatedFailureReviewGateKind kind = RepeatedFailureReviewGateKind.HumanReviewRequired,
        RepeatedFailureSeverityHint severity = RepeatedFailureSeverityHint.High,
        string? summary = "Human review remains required.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    public static RepeatedFailureRisk Risk(
        RepeatedFailureRiskKind kind = RepeatedFailureRiskKind.PatternOverclaimRisk,
        RepeatedFailureSeverityHint severity = RepeatedFailureSeverityHint.High,
        string? summary = "Pattern and root-cause claims must remain unproven hints.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    public static WorkflowStepRunnerEvaluation StepEvaluation(WorkflowStepRunnerEligibility eligibility) =>
        HumanApprovalPackageFixtures.StepEvaluation(eligibility);

    public static WorkflowDryRunResult DryRun(WorkflowDryRunStatus status) =>
        HumanApprovalPackageFixtures.DryRun(status);

    public static BoxedLangGraphRouteSuggestion Route(BoxedLangGraphRouteLabel label, bool authority = false)
    {
        var baseRoute = HumanApprovalPackageFixtures.Route(label);

        return baseRoute with
        {
            WorkflowDecisionAuthority = authority,
            WorkflowStateChangeAllowed = authority,
            StepWorkAllowed = authority,
            AgentSendAllowed = authority,
            A2aSendAllowed = authority,
            ToolUseAllowed = authority,
            SourceChangeAllowed = authority,
            MemoryPromotionAllowed = authority,
            RetrievalActivationAllowed = authority
        };
    }

    public static DogfoodEvidenceBundleCandidateResult Dogfood(
        DogfoodEvidenceBundleCandidateStatus status = DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced)
    {
        if (status == DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced)
            return new DogfoodEvidenceBundleCandidateWorkflow().Prepare(DogfoodEvidenceBundleFixtures.ValidRequest());

        return new DogfoodEvidenceBundleCandidateWorkflow().Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            StepEvaluation = DogfoodEvidenceBundleFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary)
        });
    }

    public static void AssertNoAuthority(RepeatedFailurePatternReviewCandidateResult result)
    {
        Assert.IsTrue(result.IsReviewOnly);
        Assert.IsFalse(result.IsPatternProof);
        Assert.IsFalse(result.IsRootCauseProof);
        Assert.IsFalse(result.CanQueryHistory);
        Assert.IsFalse(result.CanQueryMemory);
        Assert.IsFalse(result.CanReadLogs);
        Assert.IsFalse(result.CanReadReports);
        Assert.IsFalse(result.CanReadTrace);
        Assert.IsFalse(result.CanRunTests);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanCreateIncident);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanWriteSql);
    }
}

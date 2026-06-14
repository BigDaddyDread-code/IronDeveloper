using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class DogfoodEvidenceBundleCandidateWorkflowTests
{
    private readonly IDogfoodEvidenceBundleCandidateWorkflow _workflow = new DogfoodEvidenceBundleCandidateWorkflow();

    [TestMethod]
    public void DogfoodEvidenceBundle_NullRequestReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.InvalidRequest, result.Status);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("workflow", DogfoodEvidenceBundleCandidateReason.MissingWorkflowRunId)]
    [DataRow("step", DogfoodEvidenceBundleCandidateReason.MissingWorkflowStepId)]
    [DataRow("bundle", DogfoodEvidenceBundleCandidateReason.MissingBundleReference)]
    [DataRow("project", DogfoodEvidenceBundleCandidateReason.MissingProjectReference)]
    [DataRow("dogfood-run", DogfoodEvidenceBundleCandidateReason.MissingDogfoodRunReference)]
    public void DogfoodEvidenceBundle_MissingRequiredIdentityReturnsInvalidRequest(string field, DogfoodEvidenceBundleCandidateReason expectedReason)
    {
        var request = field switch
        {
            "workflow" => DogfoodEvidenceBundleFixtures.ValidRequest() with { WorkflowRunId = " " },
            "step" => DogfoodEvidenceBundleFixtures.ValidRequest() with { WorkflowStepId = " " },
            "bundle" => DogfoodEvidenceBundleFixtures.ValidRequest() with { DogfoodEvidenceBundleReferenceId = " " },
            "project" => DogfoodEvidenceBundleFixtures.ValidRequest() with { ProjectReferenceId = " " },
            _ => DogfoodEvidenceBundleFixtures.ValidRequest() with { DogfoodRunReferenceId = " " }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("outcome", "raw prompt leaked")]
    [DataRow("evidence", "raw completion leaked")]
    [DataRow("validation", "raw tool output leaked")]
    [DataRow("artifact", "raw log leaked")]
    [DataRow("candidate", "raw trace leaked")]
    [DataRow("gate", "validation passed")]
    [DataRow("risk", "release ready")]
    [DataRow("run-label", "tests passed")]
    [DataRow("command-ref", "stdout leaked")]
    [DataRow("correlation", "workflow may continue")]
    [DataRow("bundle", "whole patch")]
    [DataRow("dogfood-run", "private reasoning")]
    public void DogfoodEvidenceBundle_UnsafeSafeMaterialFailsClosedWithoutEcho(string field, string marker)
    {
        var request = field switch
        {
            "outcome" => DogfoodEvidenceBundleFixtures.ValidRequest() with { SafeOutcomeSummary = marker },
            "evidence" => DogfoodEvidenceBundleFixtures.ValidRequest() with { EvidenceReferences = [DogfoodEvidenceBundleFixtures.Evidence(summary: marker)] },
            "validation" => DogfoodEvidenceBundleFixtures.ValidRequest() with { ValidationReferences = [DogfoodEvidenceBundleFixtures.Validation(summary: marker)] },
            "artifact" => DogfoodEvidenceBundleFixtures.ValidRequest() with { ArtifactReferences = [DogfoodEvidenceBundleFixtures.Artifact(summary: marker)] },
            "candidate" => DogfoodEvidenceBundleFixtures.ValidRequest() with { CandidatePackageReferences = [DogfoodEvidenceBundleFixtures.Candidate(summary: marker)] },
            "gate" => DogfoodEvidenceBundleFixtures.ValidRequest() with { GateHints = [DogfoodEvidenceBundleFixtures.Gate(summary: marker)] },
            "risk" => DogfoodEvidenceBundleFixtures.ValidRequest() with { Risks = [DogfoodEvidenceBundleFixtures.Risk(summary: marker)] },
            "run-label" => DogfoodEvidenceBundleFixtures.ValidRequest() with { SafeRunLabel = marker },
            "command-ref" => DogfoodEvidenceBundleFixtures.ValidRequest() with { SafeCommandDisplayReferenceId = marker },
            "correlation" => DogfoodEvidenceBundleFixtures.ValidRequest() with { CorrelationId = marker },
            "bundle" => DogfoodEvidenceBundleFixtures.ValidRequest() with { DogfoodEvidenceBundleReferenceId = marker },
            _ => DogfoodEvidenceBundleFixtures.ValidRequest() with { DogfoodRunReferenceId = marker }
        };

        var result = _workflow.Prepare(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("summary", "safe outcome summary")]
    [DataRow("evidence", "dogfood evidence reference")]
    [DataRow("validation", "validation reference")]
    [DataRow("artifact", "artifact reference")]
    [DataRow("gate", "evidence gate hint")]
    public void DogfoodEvidenceBundle_MissingRequiredMaterialReturnsMissingEvidence(string field, string expectedMissing)
    {
        var request = field switch
        {
            "summary" => DogfoodEvidenceBundleFixtures.ValidRequest() with { SafeOutcomeSummary = " " },
            "evidence" => DogfoodEvidenceBundleFixtures.ValidRequest() with { EvidenceReferences = [] },
            "validation" => DogfoodEvidenceBundleFixtures.ValidRequest() with { ValidationReferences = [] },
            "artifact" => DogfoodEvidenceBundleFixtures.ValidRequest() with { ArtifactReferences = [] },
            _ => DogfoodEvidenceBundleFixtures.ValidRequest() with { GateHints = [] }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.MissingRequiredEvidence, result.Status);
        CollectionAssert.Contains(result.MissingEvidence.ToList(), expectedMissing);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("runner-boundary")]
    [DataRow("runner-approval")]
    [DataRow("runner-policy")]
    [DataRow("runner-a2a")]
    [DataRow("dry-run-policy")]
    [DataRow("dry-run-approval")]
    [DataRow("route-policy")]
    [DataRow("route-authority")]
    public void DogfoodEvidenceBundle_BlockingWorkflowSnapshotsReturnBlockedByWorkflowGate(string field)
    {
        var request = field switch
        {
            "runner-boundary" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                StepEvaluation = DogfoodEvidenceBundleFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary)
            },
            "runner-approval" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                StepEvaluation = DogfoodEvidenceBundleFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedApprovalRequired)
            },
            "runner-policy" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                StepEvaluation = DogfoodEvidenceBundleFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedMissingEvidence)
            },
            "runner-a2a" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                StepEvaluation = DogfoodEvidenceBundleFixtures.StepEvaluation(WorkflowStepRunnerEligibility.InvalidContract)
            },
            "dry-run-policy" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                DryRunResult = DogfoodEvidenceBundleFixtures.DryRun(WorkflowDryRunStatus.BlockedByPolicyPreflight)
            },
            "dry-run-approval" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                DryRunResult = DogfoodEvidenceBundleFixtures.DryRun(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt)
            },
            "route-authority" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                RouteSuggestion = DogfoodEvidenceBundleFixtures.Route(BoxedLangGraphRouteLabel.EligibleForDryRun, authority: true)
            },
            _ => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                RouteSuggestion = DogfoodEvidenceBundleFixtures.Route(BoxedLangGraphRouteLabel.BlockedPolicyPreflight)
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate, result.Status);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("human")]
    [DataRow("memory")]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void DogfoodEvidenceBundle_InvalidUpstreamPackageBlocks(string upstream)
    {
        var request = upstream switch
        {
            "human" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                HumanApprovalPackage = DogfoodEvidenceBundleFixtures.HumanApproval(HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate)
            },
            "memory" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate)
            },
            "tool" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate)
            },
            "implementation" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate)
            },
            "critic" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate)
            },
            _ => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                TestFailureReview = HumanApprovalPackageFixtures.TestFailure(TestFailureReviewCandidateStatus.BlockedByWorkflowGate)
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.BlockedByWorkflowGate, result.Status);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("human")]
    [DataRow("memory")]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void DogfoodEvidenceBundle_ProducedUpstreamPackagesAreIncludedAsEvidenceOnly(string upstream)
    {
        var request = upstream switch
        {
            "human" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                HumanApprovalPackage = DogfoodEvidenceBundleFixtures.HumanApproval()
            },
            "memory" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement()
            },
            "tool" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview()
            },
            "implementation" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal()
            },
            "critic" => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview()
            },
            _ => DogfoodEvidenceBundleFixtures.ValidRequest() with
            {
                TestFailureReview = HumanApprovalPackageFixtures.TestFailure()
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status);
        Assert.IsTrue(result.CandidatePackageReferences.Count >= 1);
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == DogfoodEvidenceKind.CandidatePackageReference));
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedPassed)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedFailed)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedBlocked)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedNotRun)]
    [DataRow(DogfoodValidationOutcomeHint.SuppliedPartial)]
    public void DogfoodEvidenceBundle_SuppliedValidationOutcomeHintIsRecordedWithoutBecomingProof(DogfoodValidationOutcomeHint outcomeHint)
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            ValidationReferences =
            [
                DogfoodEvidenceBundleFixtures.Validation(outcomeHint: outcomeHint)
            ]
        });

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status);
        Assert.AreEqual(outcomeHint, result.ValidationReferences.Single().OutcomeHint);
        Assert.IsFalse(result.IsValidationProof);
        Assert.IsFalse(result.IsReleaseReady);
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void DogfoodEvidenceBundle_ValidRequestProducesEvidenceBundle()
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest());

        Assert.AreEqual(DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced, result.Status);
        Assert.AreEqual("workflow-run-1", result.WorkflowRunId);
        Assert.AreEqual("workflow-step-1", result.WorkflowStepId);
        Assert.AreEqual("dogfood-evidence-bundle-request-1", result.DogfoodEvidenceBundleReferenceId);
        Assert.AreEqual("dogfood-run-reference-1", result.DogfoodRunReferenceId);
        Assert.IsTrue(result.BundleReferenceId.StartsWith("dogfood-evidence-bundle:", StringComparison.Ordinal));
        Assert.IsTrue(result.EvidenceReferences.Count > 0);
        Assert.IsTrue(result.ValidationReferences.Count > 0);
        Assert.IsTrue(result.ArtifactReferences.Count > 0);
        Assert.IsTrue(result.CandidatePackageReferences.Count > 0);
        Assert.IsTrue(result.GateHints.Count > 0);
        Assert.IsTrue(result.Risks.Count > 0);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.BundleOnly);
        CollectionAssert.Contains(result.Reasons.ToList(), DogfoodEvidenceBundleCandidateReason.DogfoodNotRun);
        CollectionAssert.Contains(result.SafeBundleSummaryLines.ToList(), "No dogfood run was executed.");
        CollectionAssert.Contains(result.SafeBundleSummaryLines.ToList(), "No tests were run.");
        CollectionAssert.Contains(result.SafeBundleSummaryLines.ToList(), "No files, logs, traces, or artifacts were read.");
        CollectionAssert.Contains(result.SafeBundleSummaryLines.ToList(), "Validation outcome hints are supplied references only.");
        CollectionAssert.Contains(result.SafeBundleSummaryLines.ToList(), "Release readiness is not claimed.");
        DogfoodEvidenceBundleFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void DogfoodEvidenceBundle_OutputIsDeterministic()
    {
        var request = DogfoodEvidenceBundleFixtures.ValidRequest() with
        {
            EvidenceReferences =
            [
                DogfoodEvidenceBundleFixtures.Evidence(DogfoodEvidenceKind.TestReportReference, "test-report-2"),
                DogfoodEvidenceBundleFixtures.Evidence(DogfoodEvidenceKind.GovernanceEventReference, "governance-event-1")
            ],
            ValidationReferences =
            [
                DogfoodEvidenceBundleFixtures.Validation(DogfoodValidationKind.WorkflowSweep, "workflow-sweep-2"),
                DogfoodEvidenceBundleFixtures.Validation(DogfoodValidationKind.FocusedTestBand, "focused-band-1")
            ]
        };

        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void DogfoodEvidenceBundle_SerializedResultContainsNoRawPrivateFullPayloadOrAuthorityMarkers()
    {
        var result = _workflow.Prepare(DogfoodEvidenceBundleFixtures.ValidRequest());
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
            "Dogfood ran",
            "Tests passed",
            "Validation passed",
            "Release ready",
            "Workflow may continue",
            "source mutated",
            "tool invoked",
            "memory promoted",
            "retrieval activated",
            "sql written");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

internal static class DogfoodEvidenceBundleFixtures
{
    public static DogfoodEvidenceBundleCandidateRequest ValidRequest() => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        DogfoodEvidenceBundleReferenceId = "dogfood-evidence-bundle-request-1",
        ProjectReferenceId = "project-1",
        DogfoodRunReferenceId = "dogfood-run-reference-1",
        SafeRunLabel = "Dogfood run reference for review.",
        SafeCommandDisplayReferenceId = "command-display-reference-1",
        SafeOutcomeSummary = "Supplied dogfood evidence references are ready for later review.",
        EvidenceReferences =
        [
            Evidence(DogfoodEvidenceKind.GovernanceEventReference, "governance-event-1"),
            Evidence(DogfoodEvidenceKind.RunReportReference, "run-report-1")
        ],
        ValidationReferences =
        [
            Validation(DogfoodValidationKind.FocusedTestBand, "focused-test-band-1", DogfoodValidationOutcomeHint.SuppliedPassed),
            Validation(DogfoodValidationKind.DiffCheck, "diff-check-1", DogfoodValidationOutcomeHint.SuppliedPassed)
        ],
        ArtifactReferences =
        [
            Artifact(DogfoodArtifactKind.RunReportReference, "run-report-artifact-1"),
            Artifact(DogfoodArtifactKind.TraceReference, "trace-artifact-reference-1")
        ],
        CandidatePackageReferences =
        [
            Candidate(DogfoodCandidatePackageKind.ImplementationProposalPackageCandidate, "implementation-package-1")
        ],
        GateHints =
        [
            Gate(DogfoodEvidenceGateKind.HumanReviewRequired),
            Gate(DogfoodEvidenceGateKind.ReleaseReadinessNotClaimed)
        ],
        Risks =
        [
            Risk(DogfoodEvidenceRiskKind.ReleaseReadinessOverclaim)
        ]
    };

    public static DogfoodEvidenceReference Evidence(
        DogfoodEvidenceKind kind = DogfoodEvidenceKind.GovernanceEventReference,
        string referenceId = "governance-event-1",
        string? summary = "Evidence reference supplied for later review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static DogfoodValidationReference Validation(
        DogfoodValidationKind kind = DogfoodValidationKind.FocusedTestBand,
        string referenceId = "focused-test-band-1",
        DogfoodValidationOutcomeHint outcomeHint = DogfoodValidationOutcomeHint.SuppliedPassed,
        string? summary = "Validation outcome hint supplied for review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        OutcomeHint = outcomeHint,
        SafeSummary = summary
    };

    public static DogfoodArtifactReference Artifact(
        DogfoodArtifactKind kind = DogfoodArtifactKind.RunReportReference,
        string referenceId = "run-report-artifact-1",
        string? summary = "Artifact reference supplied without reading stored material.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static DogfoodCandidatePackageReference Candidate(
        DogfoodCandidatePackageKind kind = DogfoodCandidatePackageKind.ImplementationProposalPackageCandidate,
        string referenceId = "implementation-package-1",
        string? summary = "Candidate package reference supplied for review.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static DogfoodEvidenceGateHint Gate(
        DogfoodEvidenceGateKind kind = DogfoodEvidenceGateKind.HumanReviewRequired,
        DogfoodEvidenceSeverityHint severity = DogfoodEvidenceSeverityHint.High,
        string? summary = "Human review remains required.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    public static DogfoodEvidenceRisk Risk(
        DogfoodEvidenceRiskKind kind = DogfoodEvidenceRiskKind.ReleaseReadinessOverclaim,
        DogfoodEvidenceSeverityHint severity = DogfoodEvidenceSeverityHint.High,
        string? summary = "Bundle output must not claim release readiness.") => new()
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
            ToolUseAllowed = authority,
            SourceChangeAllowed = authority
        };
    }

    public static HumanApprovalPackageCandidateResult HumanApproval(
        HumanApprovalPackageCandidateStatus status = HumanApprovalPackageCandidateStatus.ApprovalPackageProduced) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        ApprovalPackageReferenceId = "human-approval-package-request-1",
        PackageReferenceId = "human-approval-package-1",
        ProjectReferenceId = "project-1",
        Status = status,
        TargetKind = HumanApprovalTargetKind.WorkflowContinuationCandidate,
        TargetReferenceId = "workflow-continuation-1",
        ApprovalKind = HumanApprovalKind.WorkflowContinuationApprovalRequired,
        RequestedDecision = HumanApprovalRequestedDecision.RequestApproveOrRejectLater,
        Reasons = [HumanApprovalPackageCandidateReason.PackageOnly],
        EvidenceReferences = [],
        CandidatePackageReferences = [],
        GateHints = [],
        Risks = [],
        MissingEvidence = [],
        SafePackageSummaryLines = ["Human approval package is review material only."],
        IsPackageOnly = true,
        IsApprovalDecision = false,
        IsApproved = false,
        IsRejected = false,
        CanSatisfyApproval = false,
        CanSatisfyPolicy = false,
        CanTransitionWorkflow = false,
        CanMutateSource = false,
        CanApplyPatch = false,
        CanInvokeTool = false,
        CanDispatchAgent = false,
        CanCallModel = false,
        CanBuildPrompt = false,
        CanCreateTicket = false,
        CanPromoteMemory = false,
        CanActivateRetrieval = false,
        CanWriteSql = false
    };

    public static void AssertNoAuthority(DogfoodEvidenceBundleCandidateResult result)
    {
        Assert.IsTrue(result.IsBundleOnly);
        Assert.IsFalse(result.IsValidationProof);
        Assert.IsFalse(result.IsReleaseReady);
        Assert.IsFalse(result.CanRunDogfood);
        Assert.IsFalse(result.CanRunTests);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanReadFiles);
        Assert.IsFalse(result.CanReadLogs);
        Assert.IsFalse(result.CanReadTrace);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanWriteSql);
    }
}

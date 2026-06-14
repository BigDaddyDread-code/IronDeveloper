using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class HumanApprovalPackageCandidateWorkflowTests
{
    private readonly IHumanApprovalPackageCandidateWorkflow _workflow = new HumanApprovalPackageCandidateWorkflow();

    [TestMethod]
    public void HumanApprovalPackage_NullRequestReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.InvalidRequest, result.Status);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("workflow", HumanApprovalPackageCandidateReason.MissingWorkflowRunId)]
    [DataRow("step", HumanApprovalPackageCandidateReason.MissingWorkflowStepId)]
    [DataRow("package", HumanApprovalPackageCandidateReason.MissingPackageReference)]
    [DataRow("project", HumanApprovalPackageCandidateReason.MissingProjectReference)]
    [DataRow("target", HumanApprovalPackageCandidateReason.MissingTargetReference)]
    public void HumanApprovalPackage_MissingRequiredIdentityReturnsInvalidRequest(string field, HumanApprovalPackageCandidateReason expectedReason)
    {
        var request = field switch
        {
            "workflow" => HumanApprovalPackageFixtures.ValidRequest() with { WorkflowRunId = " " },
            "step" => HumanApprovalPackageFixtures.ValidRequest() with { WorkflowStepId = " " },
            "package" => HumanApprovalPackageFixtures.ValidRequest() with { ApprovalPackageReferenceId = " " },
            "project" => HumanApprovalPackageFixtures.ValidRequest() with { ProjectReferenceId = " " },
            _ => HumanApprovalPackageFixtures.ValidRequest() with { TargetReferenceId = " " }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("target-kind")]
    [DataRow("approval-kind")]
    [DataRow("requested-decision")]
    public void HumanApprovalPackage_UnknownKindsReturnInvalidRequest(string field)
    {
        var request = field switch
        {
            "target-kind" => HumanApprovalPackageFixtures.ValidRequest() with { TargetKind = HumanApprovalTargetKind.Unknown },
            "approval-kind" => HumanApprovalPackageFixtures.ValidRequest() with { ApprovalKind = HumanApprovalKind.Unknown },
            _ => HumanApprovalPackageFixtures.ValidRequest() with { RequestedDecision = HumanApprovalRequestedDecision.Unknown }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.InvalidRequest, result.Status);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("summary", "raw prompt leaked")]
    [DataRow("evidence", "raw completion leaked")]
    [DataRow("candidate", "raw tool output leaked")]
    [DataRow("gate", "approval granted")]
    [DataRow("risk", "policy satisfied")]
    [DataRow("correlation", "workflow continued")]
    [DataRow("target", "private reasoning")]
    [DataRow("package", "whole patch")]
    public void HumanApprovalPackage_UnsafeSafeMaterialFailsClosedWithoutEcho(string field, string marker)
    {
        var request = field switch
        {
            "summary" => HumanApprovalPackageFixtures.ValidRequest() with { SafeApprovalSummary = marker },
            "evidence" => HumanApprovalPackageFixtures.ValidRequest() with { EvidenceReferences = [HumanApprovalPackageFixtures.Evidence(summary: marker)] },
            "candidate" => HumanApprovalPackageFixtures.ValidRequest() with { CandidatePackageReferences = [HumanApprovalPackageFixtures.Candidate(summary: marker)] },
            "gate" => HumanApprovalPackageFixtures.ValidRequest() with { GateHints = [HumanApprovalPackageFixtures.Gate(summary: marker)] },
            "risk" => HumanApprovalPackageFixtures.ValidRequest() with { Risks = [HumanApprovalPackageFixtures.Risk(summary: marker)] },
            "correlation" => HumanApprovalPackageFixtures.ValidRequest() with { CorrelationId = marker },
            "target" => HumanApprovalPackageFixtures.ValidRequest() with { TargetReferenceId = marker },
            _ => HumanApprovalPackageFixtures.ValidRequest() with { ApprovalPackageReferenceId = marker }
        };

        var result = _workflow.Prepare(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), HumanApprovalPackageCandidateReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("summary")]
    [DataRow("evidence")]
    [DataRow("gate")]
    public void HumanApprovalPackage_MissingRequiredPackageMaterialReturnsMissingEvidence(string field)
    {
        var request = field switch
        {
            "summary" => HumanApprovalPackageFixtures.ValidRequest() with { SafeApprovalSummary = " " },
            "evidence" => HumanApprovalPackageFixtures.ValidRequest() with { EvidenceReferences = [] },
            _ => HumanApprovalPackageFixtures.ValidRequest() with { GateHints = [] }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.MissingRequiredApprovalEvidence, result.Status);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(HumanApprovalTargetKind.MemoryImprovementPackage, "memory improvement package candidate")]
    [DataRow(HumanApprovalTargetKind.MemoryPromotionCandidate, "memory improvement package candidate")]
    [DataRow(HumanApprovalTargetKind.RetrievalActivationCandidate, "memory improvement package candidate")]
    [DataRow(HumanApprovalTargetKind.ToolRequestGatePreview, "tool request gate preview candidate")]
    [DataRow(HumanApprovalTargetKind.ImplementationProposalPackage, "implementation proposal package candidate")]
    [DataRow(HumanApprovalTargetKind.SourceApplyCandidate, "implementation proposal package candidate")]
    [DataRow(HumanApprovalTargetKind.CriticReviewRequest, "critic review request candidate")]
    [DataRow(HumanApprovalTargetKind.TestFailureReview, "test failure review candidate")]
    public void HumanApprovalPackage_TargetSpecificPackageIsRequired(HumanApprovalTargetKind targetKind, string missing)
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest() with
        {
            TargetKind = targetKind,
            TargetReferenceId = $"target-{targetKind}"
        });

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.MissingRequiredApprovalEvidence, result.Status);
        CollectionAssert.Contains(result.MissingEvidence.ToList(), missing);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("runner")]
    [DataRow("dry-run")]
    [DataRow("route")]
    public void HumanApprovalPackage_BlockingWorkflowSnapshotsReturnBlockedByWorkflowGate(string field)
    {
        var request = field switch
        {
            "runner" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                StepEvaluation = HumanApprovalPackageFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary)
            },
            "dry-run" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                DryRunResult = HumanApprovalPackageFixtures.DryRun(WorkflowDryRunStatus.BlockedByPolicyPreflight)
            },
            _ => HumanApprovalPackageFixtures.ValidRequest() with
            {
                RouteSuggestion = HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.BlockedPolicyPreflight)
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("runner")]
    [DataRow("dry-run")]
    [DataRow("route")]
    public void HumanApprovalPackage_ApprovalHaltSnapshotsCanFeedPackageWithoutSatisfyingApproval(string field)
    {
        var request = field switch
        {
            "runner" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                StepEvaluation = HumanApprovalPackageFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedApprovalRequired)
            },
            "dry-run" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                DryRunResult = HumanApprovalPackageFixtures.DryRun(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt)
            },
            _ => HumanApprovalPackageFixtures.ValidRequest() with
            {
                RouteSuggestion = HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.BlockedApprovalRequired)
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.IsFalse(result.CanSatisfyApproval);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("memory")]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void HumanApprovalPackage_InvalidUpstreamPackageBlocks(string upstream)
    {
        var request = upstream switch
        {
            "memory" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate)
            },
            "tool" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate)
            },
            "implementation" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate)
            },
            "critic" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate)
            },
            _ => HumanApprovalPackageFixtures.ValidRequest() with
            {
                TestFailureReview = HumanApprovalPackageFixtures.TestFailure(TestFailureReviewCandidateStatus.BlockedByWorkflowGate)
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("memory")]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void HumanApprovalPackage_ProducedUpstreamPackagesAreIncludedAsEvidenceOnly(string upstream)
    {
        var request = upstream switch
        {
            "memory" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                TargetKind = HumanApprovalTargetKind.MemoryImprovementPackage,
                MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement()
            },
            "tool" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                TargetKind = HumanApprovalTargetKind.ToolRequestGatePreview,
                ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview()
            },
            "implementation" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                TargetKind = HumanApprovalTargetKind.ImplementationProposalPackage,
                ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal()
            },
            "critic" => HumanApprovalPackageFixtures.ValidRequest() with
            {
                TargetKind = HumanApprovalTargetKind.CriticReviewRequest,
                CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview()
            },
            _ => HumanApprovalPackageFixtures.ValidRequest() with
            {
                TargetKind = HumanApprovalTargetKind.TestFailureReview,
                TestFailureReview = HumanApprovalPackageFixtures.TestFailure()
            }
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.IsTrue(result.CandidatePackageReferences.Count >= 1);
        Assert.IsTrue(result.EvidenceReferences.Count >= 2);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void HumanApprovalPackage_ValidRequestProducesSafePackage()
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest());

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.AreEqual("workflow-run-1", result.WorkflowRunId);
        Assert.AreEqual("workflow-step-1", result.WorkflowStepId);
        Assert.AreEqual("approval-package-request-1", result.ApprovalPackageReferenceId);
        Assert.IsTrue(result.PackageReferenceId.StartsWith("human-approval-package:", StringComparison.Ordinal));
        Assert.AreEqual(HumanApprovalTargetKind.WorkflowContinuationCandidate, result.TargetKind);
        CollectionAssert.Contains(result.Reasons.ToList(), HumanApprovalPackageCandidateReason.PackageOnly);
        CollectionAssert.Contains(result.Reasons.ToList(), HumanApprovalPackageCandidateReason.ApprovalNotGranted);
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "No approval was granted.");
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Package requires later human/governed review.");
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void HumanApprovalPackage_OutputIsDeterministic()
    {
        var request = HumanApprovalPackageFixtures.ValidRequest() with
        {
            EvidenceReferences =
            [
                HumanApprovalPackageFixtures.Evidence(HumanApprovalEvidenceKind.DryRunResultReference, "dry-run-2"),
                HumanApprovalPackageFixtures.Evidence(HumanApprovalEvidenceKind.GovernanceEventReference, "governance-event-1")
            ],
            GateHints =
            [
                HumanApprovalPackageFixtures.Gate(HumanApprovalGateKind.WorkflowContinuationForbiddenUntilApproved),
                HumanApprovalPackageFixtures.Gate(HumanApprovalGateKind.HumanReviewRequired)
            ]
        };

        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void HumanApprovalPackage_SerializedOutputContainsNoUnsafeAuthorityMarkers()
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        AssertDoesNotContainAny(json,
            "Approval granted",
            "Approval satisfied",
            "Policy satisfied",
            "Workflow may continue",
            "source mutated",
            "patch applied",
            "tool invoked",
            "agent dispatched",
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

internal static class HumanApprovalPackageFixtures
{
    public static HumanApprovalPackageCandidateRequest ValidRequest() => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        ApprovalPackageReferenceId = "approval-package-request-1",
        ProjectReferenceId = "project-1",
        TargetKind = HumanApprovalTargetKind.WorkflowContinuationCandidate,
        TargetReferenceId = "workflow-continuation-1",
        ApprovalKind = HumanApprovalKind.WorkflowContinuationApprovalRequired,
        RequestedDecision = HumanApprovalRequestedDecision.RequestApproveOrRejectLater,
        SafeApprovalSummary = "Human review is required before this workflow can move forward.",
        EvidenceReferences =
        [
            Evidence(HumanApprovalEvidenceKind.GovernanceEventReference, "governance-event-1")
        ],
        CandidatePackageReferences =
        [
            Candidate(HumanApprovalCandidatePackageKind.ImplementationProposalPackageCandidate, "implementation-package-1")
        ],
        GateHints =
        [
            Gate(HumanApprovalGateKind.HumanReviewRequired),
            Gate(HumanApprovalGateKind.WorkflowContinuationForbiddenUntilApproved)
        ],
        Risks =
        [
            Risk(HumanApprovalRiskKind.WorkflowContinuationRisk)
        ]
    };

    public static HumanApprovalEvidenceReference Evidence(
        HumanApprovalEvidenceKind kind = HumanApprovalEvidenceKind.GovernanceEventReference,
        string referenceId = "governance-event-1",
        string? summary = "Governance event reference.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static HumanApprovalCandidatePackageReference Candidate(
        HumanApprovalCandidatePackageKind kind = HumanApprovalCandidatePackageKind.ImplementationProposalPackageCandidate,
        string referenceId = "implementation-package-1",
        string? summary = "Candidate package reference.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    public static HumanApprovalGateHint Gate(
        HumanApprovalGateKind kind = HumanApprovalGateKind.HumanReviewRequired,
        HumanApprovalSeverityHint severity = HumanApprovalSeverityHint.High,
        string? summary = "Human review gate is still required.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    public static HumanApprovalRisk Risk(
        HumanApprovalRiskKind kind = HumanApprovalRiskKind.WorkflowContinuationRisk,
        HumanApprovalSeverityHint severity = HumanApprovalSeverityHint.High,
        string? summary = "Workflow continuation requires later approval review.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    public static WorkflowStepRunnerEvaluation StepEvaluation(WorkflowStepRunnerEligibility eligibility) => new()
    {
        StepId = "workflow-step-1",
        Eligibility = eligibility,
        BlockReasons = eligibility == WorkflowStepRunnerEligibility.EligibleForFutureExecution
            ? []
            : [WorkflowRunnerBlockReason.ApprovalRequiredHalt],
        MissingEvidenceRequirements = [],
        ThoughtLedgerReference = null
    };

    public static WorkflowDryRunResult DryRun(WorkflowDryRunStatus status) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        ActionKind = WorkflowDryRunActionKind.ReviewMaterialEligibilityPreview,
        Status = status,
        BlockReasons = status == WorkflowDryRunStatus.DryRunCompleted ? [] : [WorkflowDryRunBlockReason.ApprovalRequiredHalt],
        SafeReportLines = ["Dry-run snapshot is review material only."]
    };

    public static BoxedLangGraphRouteSuggestion Route(BoxedLangGraphRouteLabel label) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        Label = label,
        Reasons = [BoxedLangGraphRouteReason.AdvisoryOnly],
        SourceStatusReferences = ["route-snapshot-1"],
        SafeReportLines = ["Route suggestion is advisory only."],
        IsAdvisoryOnly = true,
        WorkflowDecisionAuthority = false,
        WorkflowStateChangeAllowed = false,
        StepWorkAllowed = false,
        AgentSendAllowed = false,
        A2aSendAllowed = false,
        ToolUseAllowed = false,
        ApprovalChangeAllowed = false,
        PolicySatisfactionAllowed = false,
        SourceChangeAllowed = false,
        MemoryPromotionAllowed = false,
        RetrievalActivationAllowed = false,
        IsApprovalEvidence = false,
        IsPolicyEvidence = false,
        IsWorkflowTransitionEvidence = false,
        IsDryRunEvidence = false,
        IsA2aValidationEvidence = false,
        IsMemoryPromotionEvidence = false,
        IsRetrievalApprovalEvidence = false
    };

    public static MemoryImprovementPackageCandidateResult MemoryImprovement(
        MemoryImprovementPackageCandidateStatus status = MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        MemoryImprovementPackageReferenceId = "memory-improvement-request-1",
        PackageReferenceId = "memory-improvement-package-1",
        ProjectReferenceId = "project-1",
        Status = status,
        TargetKind = MemoryImprovementTargetKind.ProjectMemoryProposal,
        ImprovementKind = MemoryImprovementKind.AddCandidate,
        TargetReferenceId = "memory-proposal-1",
        Reasons = [MemoryImprovementPackageCandidateReason.PackageOnly],
        EvidenceReferences = [],
        SourceOfTruthReferences = [],
        ConflictHints = [],
        PromotionGateHints = [],
        Risks = [],
        MissingEvidence = [],
        SafePackageSummaryLines = ["Memory improvement package is review material only."],
        IsPackageOnly = true,
        IsAcceptedMemory = false,
        IsPromotion = false,
        CanMutateAcceptedMemory = false,
        CanPromoteMemory = false,
        CanWriteSql = false,
        CanWriteVectorStore = false,
        CanGenerateEmbedding = false,
        CanActivateRetrieval = false,
        CanResolveDuplicate = false,
        CanResolveConflict = false,
        CanMarkStale = false,
        CanDispatchAgent = false,
        CanInvokeTool = false,
        CanCallModel = false,
        CanBuildPrompt = false,
        CanSatisfyApproval = false,
        CanSatisfyPolicy = false,
        CanTransitionWorkflow = false
    };

    public static ToolRequestGatePreviewCandidateResult ToolPreview(
        ToolRequestGatePreviewCandidateStatus status = ToolRequestGatePreviewCandidateStatus.GatePreviewProduced) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        ToolRequestPreviewReferenceId = "tool-preview-request-1",
        PreviewPackageReferenceId = "tool-preview-package-1",
        Status = status,
        CapabilityName = "quality.gate.preview",
        Reasons = [ToolRequestGatePreviewCandidateReason.PreviewOnly],
        InputReferences = [],
        ExpectedOutputReferences = [],
        GateRequirementHints = [],
        Risks = [],
        MissingGateMaterial = [],
        SafePreviewSummaryLines = ["Tool request preview is review material only."],
        IsPreviewOnly = true,
        IsToolExecution = false,
        CanInvokeTool = false,
        CanAuthorizeTool = false,
        CanReserveTool = false,
        CanRunCommand = false,
        CanCallModel = false,
        CanBuildPrompt = false,
        CanDispatchAgent = false,
        CanSatisfyApproval = false,
        CanSatisfyPolicy = false,
        CanTransitionWorkflow = false,
        CanMutateSource = false,
        CanApplyPatch = false,
        CanCreateTicket = false,
        CanPromoteMemory = false,
        CanActivateRetrieval = false
    };

    public static ImplementationProposalPackageCandidateResult ImplementationProposal(
        ImplementationProposalPackageCandidateStatus status = ImplementationProposalPackageCandidateStatus.ProposalPackageProduced) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        ProposalReferenceId = "implementation-proposal-request-1",
        ProposalPackageReferenceId = "implementation-package-1",
        Status = status,
        TargetKind = ImplementationProposalTargetKind.ApprovalPackageReview,
        TargetReferenceId = "implementation-target-1",
        Reasons = [ImplementationProposalPackageCandidateReason.ProposalOnly],
        EvidenceReferences = [],
        AffectedAreas = [],
        ProposedSteps = [],
        ValidationSteps = [],
        Risks = [],
        MissingEvidence = [],
        SafePackageSummaryLines = ["Implementation proposal package is review material only."],
        IsProposalOnly = true,
        IsImplementation = false,
        IsPatch = false,
        CanMutateSource = false,
        CanApplyPatch = false,
        CanGenerateCode = false,
        CanRunTests = false,
        CanDispatchAgent = false,
        CanInvokeTool = false,
        CanCallModel = false,
        CanBuildPrompt = false,
        CanCreateTicket = false,
        CanSatisfyApproval = false,
        CanSatisfyPolicy = false,
        CanTransitionWorkflow = false,
        CanPromoteMemory = false,
        CanActivateRetrieval = false
    };

    public static CriticReviewRequestCandidateResult CriticReview(
        CriticReviewRequestCandidateStatus status = CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        ReviewRequestReferenceId = "critic-review-request-1",
        ReviewPackageReferenceId = "critic-review-package-1",
        Status = status,
        TargetKind = CriticReviewTargetKind.ApprovalPackageReview,
        TargetReferenceId = "critic-target-1",
        Reasons = [CriticReviewRequestCandidateReason.ReviewRequestOnly],
        ReviewQuestions = [],
        EvidenceReferences = [],
        RiskHints = [],
        MissingEvidence = [],
        SafePackageSummaryLines = ["Critic review request is review material only."],
        IsReviewRequestOnly = true,
        IsReviewDecision = false,
        CanDispatchCriticAgent = false,
        CanCallModel = false,
        CanBuildPrompt = false,
        CanPostReviewComment = false,
        CanApprove = false,
        CanReject = false,
        CanSatisfyPolicy = false,
        CanTransitionWorkflow = false,
        CanMutateSource = false,
        CanCreateTicket = false,
        CanPromoteMemory = false,
        CanActivateRetrieval = false
    };

    public static TestFailureReviewCandidateResult TestFailure(
        TestFailureReviewCandidateStatus status = TestFailureReviewCandidateStatus.ReviewMaterialProduced) => new()
    {
        WorkflowRunId = "workflow-run-1",
        WorkflowStepId = "workflow-step-1",
        TestRunReferenceId = "test-run-1",
        Status = status,
        Classification = TestFailureReviewClassification.TestAssertionFailure,
        Reasons = [TestFailureReviewCandidateReason.ReviewMaterialOnly],
        AffectedTests = ["SomeTests.ShouldPass"],
        SafeSummaryLines = ["Test failure review is review material only."],
        MissingEvidence = [],
        SafeNextReviewSuggestions = ["Review supplied failure evidence."],
        Confidence = TestFailureReviewConfidence.Medium,
        ReviewPackageReferenceId = "test-failure-review-package-1",
        IsReviewMaterialOnly = true,
        ClassificationIsAdvisory = true,
        IsRootCauseProof = false,
        CanMutateSource = false,
        CanApplyPatch = false,
        CanRunTests = false,
        CanDispatchAgent = false,
        CanInvokeTool = false,
        CanCallModel = false,
        CanCreateTicket = false,
        CanPromoteMemory = false,
        CanActivateRetrieval = false,
        CanSatisfyApproval = false,
        CanSatisfyPolicy = false,
        CanTransitionWorkflow = false
    };

    public static void AssertNoAuthority(HumanApprovalPackageCandidateResult result)
    {
        Assert.IsTrue(result.IsPackageOnly);
        Assert.IsFalse(result.IsApprovalDecision);
        Assert.IsFalse(result.IsApproved);
        Assert.IsFalse(result.IsRejected);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanWriteSql);
    }
}

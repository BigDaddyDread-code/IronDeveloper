using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class MemoryImprovementPackageAuthorityBoundaryTests
{
    private readonly MemoryImprovementPackageCandidateWorkflow _workflow = new();

    [TestMethod]
    public void ProducedPackage_CannotGrantAnyAuthorityOrWriteSurface()
    {
        var result = _workflow.Prepare(ValidRequest());

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsTrue(result.IsPackageOnly);
        Assert.IsFalse(result.IsAcceptedMemory);
        Assert.IsFalse(result.IsPromotion);
        Assert.IsFalse(result.CanMutateAcceptedMemory);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanWriteSql);
        Assert.IsFalse(result.CanWriteVectorStore);
        Assert.IsFalse(result.CanGenerateEmbedding);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanResolveDuplicate);
        Assert.IsFalse(result.CanResolveConflict);
        Assert.IsFalse(result.CanMarkStale);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }

    [DataTestMethod]
    [DataRow(MemoryImprovementKind.AddCandidate)]
    [DataRow(MemoryImprovementKind.ClarifyCandidate)]
    [DataRow(MemoryImprovementKind.SplitCandidate)]
    [DataRow(MemoryImprovementKind.MergeCandidate)]
    [DataRow(MemoryImprovementKind.MarkStaleCandidate)]
    [DataRow(MemoryImprovementKind.MarkDuplicateCandidate)]
    [DataRow(MemoryImprovementKind.FlagConflictCandidate)]
    [DataRow(MemoryImprovementKind.AddMissingEvidenceCandidate)]
    [DataRow(MemoryImprovementKind.SanitizeForPortableEngineeringMemoryCandidate)]
    public void EveryImprovementKind_RemainsCandidateOnly(MemoryImprovementKind kind)
    {
        var result = _workflow.Prepare(ValidRequest() with { ImprovementKind = kind });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsTrue(result.IsPackageOnly);
        Assert.IsFalse(result.IsPromotion);
        Assert.IsFalse(result.IsAcceptedMemory);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanMutateAcceptedMemory);
    }

    [DataTestMethod]
    [DataRow(MemoryImprovementTargetKind.ProjectMemoryProposal)]
    [DataRow(MemoryImprovementTargetKind.ExistingMemoryReference)]
    [DataRow(MemoryImprovementTargetKind.PortableEngineeringMemoryCandidate)]
    [DataRow(MemoryImprovementTargetKind.StaleMemoryCandidate)]
    [DataRow(MemoryImprovementTargetKind.DuplicateMemoryCandidate)]
    [DataRow(MemoryImprovementTargetKind.ConflictCandidate)]
    [DataRow(MemoryImprovementTargetKind.EvidenceGapCandidate)]
    public void EveryTargetKind_RemainsReviewMaterialOnly(MemoryImprovementTargetKind targetKind)
    {
        var result = _workflow.Prepare(ValidRequest() with { TargetKind = targetKind });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsTrue(result.Reasons.Contains(MemoryImprovementPackageCandidateReason.PackageOnly));
        Assert.IsTrue(result.Reasons.Contains(MemoryImprovementPackageCandidateReason.SuppliedEvidenceOnly));
        Assert.IsTrue(result.Reasons.Contains(MemoryImprovementPackageCandidateReason.MemoryNotPromoted));
        Assert.IsTrue(result.Reasons.Contains(MemoryImprovementPackageCandidateReason.AcceptedMemoryNotMutated));
    }

    [TestMethod]
    public void PromotionGateHints_DoNotSatisfyApprovalPolicyOrPromotion()
    {
        var result = _workflow.Prepare(ValidRequest() with
        {
            PromotionGateHints =
            [
                new MemoryImprovementPromotionGateHint
                {
                    Kind = MemoryImprovementGateKind.PromotionApprovalRequired,
                    SeverityHint = MemoryImprovementSeverityHint.Critical,
                    SafeSummary = "Promotion requires later governed human review."
                }
            ]
        });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanPromoteMemory);
    }

    [TestMethod]
    public void ConflictAndDuplicateHints_DoNotResolveMemoryState()
    {
        var result = _workflow.Prepare(ValidRequest() with
        {
            ConflictHints =
            [
                new MemoryImprovementConflictHint
                {
                    Kind = MemoryImprovementConflictKind.PossibleDuplicate,
                    SeverityHint = MemoryImprovementSeverityHint.High,
                    SafeSummary = "Possible duplicate should be reviewed."
                },
                new MemoryImprovementConflictHint
                {
                    Kind = MemoryImprovementConflictKind.PossibleContradiction,
                    SeverityHint = MemoryImprovementSeverityHint.High,
                    SafeSummary = "Possible contradiction should be reviewed."
                }
            ]
        });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsFalse(result.CanResolveDuplicate);
        Assert.IsFalse(result.CanResolveConflict);
        Assert.IsFalse(result.CanMarkStale);
    }

    [TestMethod]
    public void RetrievalAndVectorGateHints_DoNotActivateRetrievalOrWriteIndex()
    {
        var result = _workflow.Prepare(ValidRequest() with
        {
            PromotionGateHints =
            [
                new MemoryImprovementPromotionGateHint
                {
                    Kind = MemoryImprovementGateKind.RetrievalActivationForbidden,
                    SeverityHint = MemoryImprovementSeverityHint.Critical,
                    SafeSummary = "Retrieval activation remains forbidden for this package."
                }
            ],
            Risks =
            [
                new MemoryImprovementRisk
                {
                    Kind = MemoryImprovementRiskKind.RetrievalAuthorityRisk,
                    SeverityHint = MemoryImprovementSeverityHint.Critical,
                    SafeSummary = "Retrieval authority remains a later governed risk."
                }
            ]
        });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsFalse(result.CanWriteVectorStore);
        Assert.IsFalse(result.CanGenerateEmbedding);
        Assert.IsFalse(result.CanActivateRetrieval);
    }

    [TestMethod]
    public void EvidenceAndSourceOfTruthReferences_DoNotDecideTruth()
    {
        var result = _workflow.Prepare(ValidRequest() with
        {
            SourceOfTruthReferences =
            [
                new MemoryImprovementSourceOfTruthReference
                {
                    Kind = MemoryImprovementSourceOfTruthKind.UserApprovedSourceReference,
                    ReferenceId = "review-source-1",
                    SafeSummary = "Human-reviewed source can be checked later."
                }
            ]
        });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsFalse(result.IsAcceptedMemory);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
    }

    [TestMethod]
    public void OptionalPackageInputs_RemainEvidenceOnly()
    {
        var result = _workflow.Prepare(ValidRequest() with
        {
            ToolRequestGatePreview = ToolPreview(),
            ImplementationProposal = ImplementationProposal(),
            CriticReviewRequest = CriticReview(),
            TestFailureReview = TestFailure()
        });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == MemoryImprovementEvidenceKind.ToolRequestPreviewReference));
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == MemoryImprovementEvidenceKind.ImplementationProposalReference));
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == MemoryImprovementEvidenceKind.CriticReviewRequestReference));
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == MemoryImprovementEvidenceKind.TestFailureReviewReference));
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanCallModel);
    }

    private static MemoryImprovementPackageCandidateRequest ValidRequest() =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            MemoryImprovementPackageReferenceId = "memory-improvement-1",
            ProjectReferenceId = "project-1",
            TargetKind = MemoryImprovementTargetKind.ProjectMemoryProposal,
            TargetReferenceId = "memory-proposal-1",
            ImprovementKind = MemoryImprovementKind.ClarifyCandidate,
            SafeCurrentMemorySummaryReferenceId = "memory-summary-1",
            SafeProposedMemorySummary = "Candidate memory should be clarified with supplied evidence.",
            EvidenceReferences = [new MemoryImprovementEvidenceReference { Kind = MemoryImprovementEvidenceKind.MemoryProposalReference, ReferenceId = "memory-proposal-evidence-1", SafeSummary = "Supplied memory proposal evidence reference." }],
            SourceOfTruthReferences = [new MemoryImprovementSourceOfTruthReference { Kind = MemoryImprovementSourceOfTruthKind.ProjectDocumentReference, ReferenceId = "project-document-1", SafeSummary = "Supplied project document reference." }],
            ConflictHints = [new MemoryImprovementConflictHint { Kind = MemoryImprovementConflictKind.PossibleDuplicate, SeverityHint = MemoryImprovementSeverityHint.Medium, SafeSummary = "Possible duplicate should be checked by a reviewer." }],
            PromotionGateHints = [new MemoryImprovementPromotionGateHint { Kind = MemoryImprovementGateKind.HumanReviewRequired, SeverityHint = MemoryImprovementSeverityHint.Critical, SafeSummary = "Human review remains required before any later promotion." }],
            Risks = [new MemoryImprovementRisk { Kind = MemoryImprovementRiskKind.PromotionAuthorityRisk, SeverityHint = MemoryImprovementSeverityHint.High, SafeSummary = "Promotion authority remains a later governed risk." }]
        };

    private static ToolRequestGatePreviewCandidateResult ToolPreview() =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ToolRequestPreviewReferenceId = "tool-preview-1",
            PreviewPackageReferenceId = "tool-preview-package-1",
            Status = ToolRequestGatePreviewCandidateStatus.GatePreviewProduced,
            CapabilityName = "quality.gate.preview",
            Reasons = [ToolRequestGatePreviewCandidateReason.PreviewOnly],
            InputReferences = [],
            ExpectedOutputReferences = [],
            GateRequirementHints = [],
            Risks = [],
            MissingGateMaterial = [],
            SafePreviewSummaryLines = ["Tool request gate preview is supplied evidence only."],
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

    private static ImplementationProposalPackageCandidateResult ImplementationProposal() =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ProposalReferenceId = "implementation-proposal-1",
            ProposalPackageReferenceId = "implementation-proposal-package-1",
            Status = ImplementationProposalPackageCandidateStatus.ProposalPackageProduced,
            TargetKind = ImplementationProposalTargetKind.MemoryProposalReview,
            TargetReferenceId = "memory-proposal-1",
            Reasons = [ImplementationProposalPackageCandidateReason.ProposalOnly],
            EvidenceReferences = [],
            AffectedAreas = [],
            ProposedSteps = [],
            ValidationSteps = [],
            Risks = [],
            MissingEvidence = [],
            SafePackageSummaryLines = ["Implementation proposal package is supplied evidence only."],
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

    private static CriticReviewRequestCandidateResult CriticReview() =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ReviewRequestReferenceId = "critic-review-1",
            ReviewPackageReferenceId = "critic-review-package-1",
            Status = CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced,
            TargetKind = CriticReviewTargetKind.MemoryProposalReview,
            TargetReferenceId = "memory-proposal-1",
            Reasons = [CriticReviewRequestCandidateReason.ReviewRequestOnly],
            ReviewQuestions = [],
            EvidenceReferences = [],
            RiskHints = [],
            MissingEvidence = [],
            SafePackageSummaryLines = ["Critic review request is supplied evidence only."],
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

    private static TestFailureReviewCandidateResult TestFailure() =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            TestRunReferenceId = "test-run-1",
            Status = TestFailureReviewCandidateStatus.ReviewMaterialProduced,
            Classification = TestFailureReviewClassification.TestAssertionFailure,
            Reasons = [TestFailureReviewCandidateReason.ReviewMaterialOnly],
            AffectedTests = ["SampleTests.ShouldPass"],
            SafeSummaryLines = ["Test failure review is supplied evidence only."],
            MissingEvidence = [],
            SafeNextReviewSuggestions = [],
            Confidence = TestFailureReviewConfidence.High,
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
}

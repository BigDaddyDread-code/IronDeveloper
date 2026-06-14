using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class MemoryImprovementPackageCandidateWorkflowTests
{
    private readonly MemoryImprovementPackageCandidateWorkflow _workflow = new();

    [TestMethod]
    public void NullRequest_ReturnsInvalidRequest()
    {
        var result = _workflow.Prepare(null);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), MemoryImprovementPackageCandidateReason.Unknown);
        Assert.IsTrue(result.IsPackageOnly);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("workflow", MemoryImprovementPackageCandidateReason.MissingWorkflowRunId)]
    [DataRow("step", MemoryImprovementPackageCandidateReason.MissingWorkflowStepId)]
    [DataRow("package", MemoryImprovementPackageCandidateReason.MissingPackageReference)]
    [DataRow("project", MemoryImprovementPackageCandidateReason.MissingProjectReference)]
    [DataRow("target", MemoryImprovementPackageCandidateReason.MissingTargetReference)]
    public void MissingRequiredIdentity_ReturnsInvalidRequest(string missingField, MemoryImprovementPackageCandidateReason expectedReason)
    {
        var request = ValidRequest() with
        {
            WorkflowRunId = missingField == "workflow" ? " " : "workflow-run-1",
            WorkflowStepId = missingField == "step" ? " " : "workflow-step-1",
            MemoryImprovementPackageReferenceId = missingField == "package" ? " " : "memory-improvement-1",
            ProjectReferenceId = missingField == "project" ? " " : "project-1",
            TargetReferenceId = missingField == "target" ? " " : "memory-proposal-1"
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("target-kind")]
    [DataRow("improvement-kind")]
    public void UnknownKinds_ReturnInvalidRequest(string field)
    {
        var request = field == "target-kind"
            ? ValidRequest() with { TargetKind = MemoryImprovementTargetKind.Unknown }
            : ValidRequest() with { ImprovementKind = MemoryImprovementKind.Unknown };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.InvalidRequest, result.Status);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("summary", "raw prompt leaked")]
    [DataRow("evidence", "raw completion leaked")]
    [DataRow("source", "raw tool output leaked")]
    [DataRow("conflict", "memory promoted")]
    [DataRow("gate", "approval satisfied")]
    [DataRow("risk", "retrieval activated")]
    [DataRow("correlation", "policy satisfied")]
    [DataRow("target", "private reasoning")]
    [DataRow("package", "whole patch")]
    public void UnsafeSafeFields_FailClosedAndDoNotSerializeUnsafeText(string field, string unsafeText)
    {
        var request = field switch
        {
            "summary" => ValidRequest() with { SafeProposedMemorySummary = unsafeText },
            "evidence" => ValidRequest() with { EvidenceReferences = [Evidence(unsafeText)] },
            "source" => ValidRequest() with { SourceOfTruthReferences = [Source(unsafeText)] },
            "conflict" => ValidRequest() with { ConflictHints = [Conflict(unsafeText)] },
            "gate" => ValidRequest() with { PromotionGateHints = [Gate(unsafeText)] },
            "risk" => ValidRequest() with { Risks = [Risk(unsafeText)] },
            "correlation" => ValidRequest() with { CorrelationId = unsafeText },
            "target" => ValidRequest() with { TargetReferenceId = unsafeText },
            "package" => ValidRequest() with { MemoryImprovementPackageReferenceId = unsafeText },
            _ => ValidRequest()
        };

        var result = _workflow.Prepare(request);
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), MemoryImprovementPackageCandidateReason.UnsafeInput);
        Assert.IsFalse(serialized.Contains(unsafeText, StringComparison.OrdinalIgnoreCase));
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("summary", MemoryImprovementPackageCandidateReason.MissingProposedMemorySummary)]
    [DataRow("evidence", MemoryImprovementPackageCandidateReason.MissingEvidenceReference)]
    [DataRow("source", MemoryImprovementPackageCandidateReason.MissingSourceOfTruthReference)]
    [DataRow("gate", MemoryImprovementPackageCandidateReason.MissingPromotionGateHint)]
    public void MissingRequiredPackageMaterial_ReturnsMissingEvidence(string missingField, MemoryImprovementPackageCandidateReason expectedReason)
    {
        var request = ValidRequest() with
        {
            SafeProposedMemorySummary = missingField == "summary" ? " " : "Candidate memory should be clarified with supplied evidence.",
            EvidenceReferences = missingField == "evidence" ? [] : [Evidence()],
            SourceOfTruthReferences = missingField == "source" ? [] : [Source()],
            PromotionGateHints = missingField == "gate" ? [] : [Gate()]
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MissingRequiredMemoryEvidence, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        Assert.IsTrue(result.MissingEvidence.Count > 0);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("runner")]
    [DataRow("approval")]
    [DataRow("policy")]
    [DataRow("a2a")]
    public void BlockingStepEvaluation_BlocksPackageProduction(string blocker)
    {
        var evaluation = blocker switch
        {
            "approval" => StepEvaluation(WorkflowStepRunnerEligibility.BlockedApprovalRequired, approvalStatus: WorkflowApprovalHaltStatus.ApprovalRequiredHalt),
            "policy" => StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary, policyStatus: WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence),
            "a2a" => StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary, a2aStatus: WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence),
            _ => StepEvaluation(WorkflowStepRunnerEligibility.BlockedMissingEvidence)
        };

        var result = _workflow.Prepare(ValidRequest() with { StepEvaluation = evaluation });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), MemoryImprovementPackageCandidateReason.BlockedByRunnerEvaluation);
        AssertAllAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void BlockingDryRun_BlocksPackageProduction()
    {
        var result = _workflow.Prepare(ValidRequest() with { DryRunResult = DryRun(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt) });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), MemoryImprovementPackageCandidateReason.BlockedByDryRun);
    }

    [TestMethod]
    public void BlockingRouteSuggestion_BlocksPackageProduction()
    {
        var result = _workflow.Prepare(ValidRequest() with { RouteSuggestion = Route(BoxedLangGraphRouteLabel.BlockedApprovalRequired) });

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), MemoryImprovementPackageCandidateReason.BlockedByRouteSuggestion);
    }

    [DataTestMethod]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void InvalidOrBlockedSuppliedPackages_BlockPackageProduction(string packageCase)
    {
        var request = packageCase switch
        {
            "tool" => ValidRequest() with { ToolRequestGatePreview = ToolPreview(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate) },
            "implementation" => ValidRequest() with { ImplementationProposal = ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate) },
            "critic" => ValidRequest() with { CriticReviewRequest = CriticReview(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate) },
            "test" => ValidRequest() with { TestFailureReview = TestFailure(TestFailureReviewCandidateStatus.BlockedByWorkflowGate) },
            _ => ValidRequest()
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate, result.Status);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("tool")]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void ValidSuppliedPackages_AreIncludedAsEvidenceReferences(string packageCase)
    {
        var request = packageCase switch
        {
            "tool" => ValidRequest() with { ToolRequestGatePreview = ToolPreview(ToolRequestGatePreviewCandidateStatus.GatePreviewProduced) },
            "implementation" => ValidRequest() with { ImplementationProposal = ImplementationProposal(ImplementationProposalPackageCandidateStatus.ProposalPackageProduced) },
            "critic" => ValidRequest() with { CriticReviewRequest = CriticReview(CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced) },
            "test" => ValidRequest() with { TestFailureReview = TestFailure(TestFailureReviewCandidateStatus.ReviewMaterialProduced) },
            _ => ValidRequest()
        };

        var result = _workflow.Prepare(request);

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.IsTrue(result.EvidenceReferences.Count >= 2);
        AssertAllAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void ValidRequest_ProducesPackageWithAllExpectedSections()
    {
        var result = _workflow.Prepare(ValidRequest());

        Assert.AreEqual(MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced, result.Status);
        Assert.AreEqual(MemoryImprovementTargetKind.ProjectMemoryProposal, result.TargetKind);
        Assert.AreEqual(MemoryImprovementKind.ClarifyCandidate, result.ImprovementKind);
        Assert.IsTrue(result.PackageReferenceId.StartsWith("memory-improvement-package:", StringComparison.Ordinal));
        Assert.IsTrue(result.EvidenceReferences.Count > 0);
        Assert.IsTrue(result.SourceOfTruthReferences.Count > 0);
        Assert.IsTrue(result.ConflictHints.Count > 0);
        Assert.IsTrue(result.PromotionGateHints.Count > 0);
        Assert.IsTrue(result.Risks.Count > 0);
        Assert.IsTrue(result.SafePackageSummaryLines.Count > 0);
        Assert.IsTrue(result.SafePackageSummaryLines.Any(line => line.Contains("Existing memory remains unchanged.", StringComparison.Ordinal)));
        Assert.IsTrue(result.SafePackageSummaryLines.Any(line => line.Contains("No memory promotion occurred.", StringComparison.Ordinal)));
        Assert.IsTrue(result.SafePackageSummaryLines.Any(line => line.Contains("Retrieval remained inactive.", StringComparison.Ordinal)));
        AssertAllAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void SameRequest_ProducesSameResult()
    {
        var request = ValidRequest();

        var first = JsonSerializer.Serialize(_workflow.Prepare(request));
        var second = JsonSerializer.Serialize(_workflow.Prepare(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ResultSerializesWithoutRawPrivateOrAuthorityPayloadMaterial()
    {
        var serialized = JsonSerializer.Serialize(_workflow.Prepare(ValidRequest()));

        foreach (var marker in new[]
                 {
                     "private reasoning",
                     "hidden reasoning",
                     "raw prompt",
                     "raw completion",
                     "raw tool output",
                     "whole patch",
                     "patch payload",
                     "memory promoted",
                     "SQL is written",
                     "retrieval activated",
                     "duplicate resolved",
                     "conflict resolved",
                     "approval satisfied",
                     "policy satisfied",
                     "workflow may continue"
                 })
        {
            Assert.IsFalse(serialized.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
        }
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
            EvidenceReferences = [Evidence()],
            SourceOfTruthReferences = [Source()],
            ConflictHints = [Conflict()],
            PromotionGateHints = [Gate()],
            Risks = [Risk()],
            StepEvaluation = StepEvaluation(WorkflowStepRunnerEligibility.EligibleForFutureExecution),
            DryRunResult = DryRun(WorkflowDryRunStatus.DryRunCompleted),
            RouteSuggestion = Route(BoxedLangGraphRouteLabel.EligibleForDryRun)
        };

    private static MemoryImprovementEvidenceReference Evidence(string summary = "Supplied memory proposal evidence reference.") =>
        new() { Kind = MemoryImprovementEvidenceKind.MemoryProposalReference, ReferenceId = "memory-proposal-evidence-1", SafeSummary = summary };

    private static MemoryImprovementSourceOfTruthReference Source(string summary = "Supplied project document reference.") =>
        new() { Kind = MemoryImprovementSourceOfTruthKind.ProjectDocumentReference, ReferenceId = "project-document-1", SafeSummary = summary };

    private static MemoryImprovementConflictHint Conflict(string summary = "Possible duplicate should be checked by a reviewer.") =>
        new() { Kind = MemoryImprovementConflictKind.PossibleDuplicate, SeverityHint = MemoryImprovementSeverityHint.Medium, SafeSummary = summary };

    private static MemoryImprovementPromotionGateHint Gate(string summary = "Human review remains required before any later promotion.") =>
        new() { Kind = MemoryImprovementGateKind.HumanReviewRequired, SeverityHint = MemoryImprovementSeverityHint.Critical, SafeSummary = summary };

    private static MemoryImprovementRisk Risk(string summary = "Promotion authority remains a later governed risk.") =>
        new() { Kind = MemoryImprovementRiskKind.PromotionAuthorityRisk, SeverityHint = MemoryImprovementSeverityHint.High, SafeSummary = summary };

    private static WorkflowStepRunnerEvaluation StepEvaluation(
        WorkflowStepRunnerEligibility eligibility,
        WorkflowStepPolicyPreflightStatus? policyStatus = null,
        WorkflowA2aHandoffValidationStatus? a2aStatus = null,
        WorkflowApprovalHaltStatus? approvalStatus = null) =>
        new()
        {
            StepId = "workflow-step-1",
            Eligibility = eligibility,
            BlockReasons = eligibility == WorkflowStepRunnerEligibility.EligibleForFutureExecution ? [] : [WorkflowRunnerBlockReason.RuntimeBoundaryPreventsExecution],
            MissingEvidenceRequirements = [],
            ThoughtLedgerReference = null,
            PolicyPreflightStatus = policyStatus,
            PolicyBlockReasons = [],
            MissingPolicyRequirements = [],
            A2aHandoffValidationStatus = a2aStatus,
            A2aHandoffBlockReasons = [],
            MissingA2aHandoffEvidence = [],
            ApprovalHaltStatus = approvalStatus,
            ApprovalHaltReasons = [],
            MissingApprovalRequirements = [],
            NextRecordableTransition = null
        };

    private static WorkflowDryRunResult DryRun(WorkflowDryRunStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ActionKind = WorkflowDryRunActionKind.ReviewMaterialEligibilityPreview,
            Status = status,
            BlockReasons = status == WorkflowDryRunStatus.DryRunCompleted ? [] : [WorkflowDryRunBlockReason.ApprovalRequiredHalt],
            SafeReportLines = ["Dry run material is review-only."]
        };

    private static BoxedLangGraphRouteSuggestion Route(BoxedLangGraphRouteLabel label) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            Label = label,
            Reasons = [BoxedLangGraphRouteReason.AdvisoryOnly],
            SourceStatusReferences = ["runner:evaluation"],
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

    private static ToolRequestGatePreviewCandidateResult ToolPreview(ToolRequestGatePreviewCandidateStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ToolRequestPreviewReferenceId = "tool-preview-1",
            PreviewPackageReferenceId = "tool-preview-package-1",
            Status = status,
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

    private static ImplementationProposalPackageCandidateResult ImplementationProposal(ImplementationProposalPackageCandidateStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ProposalReferenceId = "implementation-proposal-1",
            ProposalPackageReferenceId = "implementation-proposal-package-1",
            Status = status,
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

    private static CriticReviewRequestCandidateResult CriticReview(CriticReviewRequestCandidateStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ReviewRequestReferenceId = "critic-review-1",
            ReviewPackageReferenceId = "critic-review-package-1",
            Status = status,
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

    private static TestFailureReviewCandidateResult TestFailure(TestFailureReviewCandidateStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            TestRunReferenceId = "test-run-1",
            Status = status,
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

    private static void AssertAllAuthorityFlagsFalse(MemoryImprovementPackageCandidateResult result)
    {
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
}

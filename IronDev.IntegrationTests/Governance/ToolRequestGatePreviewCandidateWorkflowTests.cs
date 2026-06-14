using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ToolRequestGatePreviewCandidateWorkflowTests
{
    private readonly ToolRequestGatePreviewCandidateWorkflow _workflow = new();

    [TestMethod]
    public void NullRequest_ReturnsInvalidRequest()
    {
        var result = _workflow.Preview(null);

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), ToolRequestGatePreviewCandidateReason.Unknown);
        Assert.IsTrue(result.IsPreviewOnly);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("workflow", ToolRequestGatePreviewCandidateReason.MissingWorkflowRunId)]
    [DataRow("step", ToolRequestGatePreviewCandidateReason.MissingWorkflowStepId)]
    [DataRow("preview", ToolRequestGatePreviewCandidateReason.MissingPreviewReference)]
    [DataRow("capability", ToolRequestGatePreviewCandidateReason.MissingCapabilityName)]
    public void MissingRequiredIdentity_ReturnsInvalidRequest(string missingField, ToolRequestGatePreviewCandidateReason expectedReason)
    {
        var request = ValidRequest() with
        {
            WorkflowRunId = missingField == "workflow" ? " " : "workflow-run-1",
            WorkflowStepId = missingField == "step" ? " " : "workflow-step-1",
            ToolRequestPreviewReferenceId = missingField == "preview" ? " " : "tool-preview-1",
            CapabilityName = missingField == "capability" ? " " : "quality.gate.preview"
        };

        var result = _workflow.Preview(request);

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("dotnet test")]
    [DataRow("git apply")]
    [DataRow("powershell -Command")]
    [DataRow("cmd.exe")]
    [DataRow("rm -rf")]
    [DataRow("../secret")]
    [DataRow("Invoke-Tool")]
    [DataRow("Execute")]
    [DataRow("Approve")]
    [DataRow("ApplyPatch")]
    [DataRow("MutateSource")]
    public void UnsafeCapabilityName_FailsClosed(string capabilityName)
    {
        var result = _workflow.Preview(ValidRequest() with { CapabilityName = capabilityName });

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), ToolRequestGatePreviewCandidateReason.UnsafeInput);
        Assert.AreEqual(string.Empty, result.CapabilityName);
    }

    [DataTestMethod]
    [DataRow("purpose", "raw prompt leaked")]
    [DataRow("input", "raw completion leaked")]
    [DataRow("output", "raw tool output leaked")]
    [DataRow("gate", "tool executed")]
    [DataRow("risk", "approval satisfied")]
    [DataRow("correlation", "policy satisfied")]
    [DataRow("workflow", "private reasoning")]
    [DataRow("preview", "whole patch")]
    public void UnsafeSafeFields_FailClosed(string field, string unsafeText)
    {
        var request = field switch
        {
            "purpose" => ValidRequest() with { SafePurposeSummary = unsafeText },
            "input" => ValidRequest() with { InputReferences = [Input(unsafeText)] },
            "output" => ValidRequest() with { ExpectedOutputReferences = [Output(unsafeText)] },
            "gate" => ValidRequest() with { GateRequirementHints = [Gate(unsafeText)] },
            "risk" => ValidRequest() with { Risks = [Risk(unsafeText)] },
            "correlation" => ValidRequest() with { CorrelationId = unsafeText },
            "workflow" => ValidRequest() with { WorkflowRunId = unsafeText },
            "preview" => ValidRequest() with { ToolRequestPreviewReferenceId = unsafeText },
            _ => ValidRequest()
        };

        var result = _workflow.Preview(request);

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), ToolRequestGatePreviewCandidateReason.UnsafeInput);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(unsafeText, StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("purpose", ToolRequestGatePreviewCandidateReason.MissingPurposeSummary)]
    [DataRow("input", ToolRequestGatePreviewCandidateReason.MissingInputReference)]
    [DataRow("output", ToolRequestGatePreviewCandidateReason.MissingExpectedOutputReference)]
    [DataRow("gate", ToolRequestGatePreviewCandidateReason.MissingGateRequirementHint)]
    public void MissingRequiredPreviewMaterial_ReturnsMissingMaterial(string missingField, ToolRequestGatePreviewCandidateReason expectedReason)
    {
        var request = ValidRequest() with
        {
            SafePurposeSummary = missingField == "purpose" ? " " : "Preview quality gate requirements.",
            InputReferences = missingField == "input" ? [] : [Input()],
            ExpectedOutputReferences = missingField == "output" ? [] : [Output()],
            GateRequirementHints = missingField == "gate" ? [] : [Gate()]
        };

        var result = _workflow.Preview(request);

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.MissingRequiredPreviewMaterial, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), expectedReason);
        Assert.IsTrue(result.MissingGateMaterial.Count > 0);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("runner")]
    [DataRow("approval")]
    [DataRow("policy")]
    [DataRow("a2a")]
    public void BlockingStepEvaluation_BlocksPreviewProduction(string blocker)
    {
        var evaluation = blocker switch
        {
            "approval" => StepEvaluation(WorkflowStepRunnerEligibility.BlockedApprovalRequired, approvalStatus: WorkflowApprovalHaltStatus.ApprovalRequiredHalt),
            "policy" => StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary, policyStatus: WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence),
            "a2a" => StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary, a2aStatus: WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence),
            _ => StepEvaluation(WorkflowStepRunnerEligibility.BlockedMissingEvidence)
        };

        var result = _workflow.Preview(ValidRequest() with { StepEvaluation = evaluation });

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), ToolRequestGatePreviewCandidateReason.BlockedByRunnerEvaluation);
        AssertAllAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void BlockingDryRun_BlocksPreviewProduction()
    {
        var result = _workflow.Preview(ValidRequest() with { DryRunResult = DryRun(WorkflowDryRunStatus.BlockedByApprovalRequiredHalt) });

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), ToolRequestGatePreviewCandidateReason.BlockedByDryRun);
    }

    [TestMethod]
    public void BlockingRouteSuggestion_BlocksPreviewProduction()
    {
        var result = _workflow.Preview(ValidRequest() with { RouteSuggestion = Route(BoxedLangGraphRouteLabel.BlockedApprovalRequired) });

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), ToolRequestGatePreviewCandidateReason.BlockedByRouteSuggestion);
    }

    [DataTestMethod]
    [DataRow("implementation-invalid")]
    [DataRow("implementation-blocked")]
    [DataRow("critic-invalid")]
    [DataRow("critic-blocked")]
    [DataRow("test-invalid")]
    [DataRow("test-blocked")]
    public void InvalidOrBlockedSuppliedPackages_BlockPreviewProduction(string packageCase)
    {
        var request = packageCase switch
        {
            "implementation-invalid" => ValidRequest() with { ImplementationProposal = ImplementationProposal(ImplementationProposalPackageCandidateStatus.InvalidRequest) },
            "implementation-blocked" => ValidRequest() with { ImplementationProposal = ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate) },
            "critic-invalid" => ValidRequest() with { CriticReviewRequest = CriticReview(CriticReviewRequestCandidateStatus.InvalidRequest) },
            "critic-blocked" => ValidRequest() with { CriticReviewRequest = CriticReview(CriticReviewRequestCandidateStatus.BlockedByWorkflowGate) },
            "test-invalid" => ValidRequest() with { TestFailureReview = TestFailure(TestFailureReviewCandidateStatus.InvalidRequest) },
            "test-blocked" => ValidRequest() with { TestFailureReview = TestFailure(TestFailureReviewCandidateStatus.BlockedByWorkflowGate) },
            _ => ValidRequest()
        };

        var result = _workflow.Preview(request);

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        AssertAllAuthorityFlagsFalse(result);
    }

    [DataTestMethod]
    [DataRow("implementation")]
    [DataRow("critic")]
    [DataRow("test")]
    public void ValidSuppliedPackages_CanBePackaged(string packageCase)
    {
        var request = packageCase switch
        {
            "implementation" => ValidRequest() with { ImplementationProposal = ImplementationProposal(ImplementationProposalPackageCandidateStatus.ProposalPackageProduced) },
            "critic" => ValidRequest() with { CriticReviewRequest = CriticReview(CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced) },
            "test" => ValidRequest() with { TestFailureReview = TestFailure(TestFailureReviewCandidateStatus.ReviewMaterialProduced) },
            _ => ValidRequest()
        };

        var result = _workflow.Preview(request);

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.GatePreviewProduced, result.Status);
        Assert.IsTrue(result.InputReferences.Count >= 1);
        AssertAllAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void ValidRequest_ProducesGatePreviewWithAllExpectedSections()
    {
        var result = _workflow.Preview(ValidRequest());

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.GatePreviewProduced, result.Status);
        Assert.AreEqual("quality.gate.preview", result.CapabilityName);
        Assert.IsTrue(result.PreviewPackageReferenceId.StartsWith("tool-request-gate-preview:", StringComparison.Ordinal));
        Assert.IsTrue(result.InputReferences.Count > 0);
        Assert.IsTrue(result.ExpectedOutputReferences.Count > 0);
        Assert.IsTrue(result.GateRequirementHints.Count > 0);
        Assert.IsTrue(result.Risks.Count > 0);
        Assert.IsTrue(result.SafePreviewSummaryLines.Count > 0);
        Assert.IsTrue(result.SafePreviewSummaryLines.Any(line => line.Contains("No tool was invoked.", StringComparison.Ordinal)));
        Assert.IsTrue(result.SafePreviewSummaryLines.Any(line => line.Contains("No command was run.", StringComparison.Ordinal)));
        Assert.IsTrue(result.SafePreviewSummaryLines.Any(line => line.Contains("No approval was satisfied.", StringComparison.Ordinal)));
        Assert.IsTrue(result.SafePreviewSummaryLines.Any(line => line.Contains("No policy was satisfied.", StringComparison.Ordinal)));
        Assert.IsTrue(result.IsPreviewOnly);
        AssertAllAuthorityFlagsFalse(result);
    }

    [TestMethod]
    public void SameRequest_ProducesSameResult()
    {
        var request = ValidRequest();

        var first = JsonSerializer.Serialize(_workflow.Preview(request));
        var second = JsonSerializer.Serialize(_workflow.Preview(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ResultSerializesWithoutRawPrivateOrAuthorityPayloadMaterial()
    {
        var serialized = JsonSerializer.Serialize(_workflow.Preview(ValidRequest()));

        foreach (var marker in new[]
                 {
                     "private reasoning",
                     "hidden reasoning",
                     "raw prompt",
                     "raw completion",
                     "raw tool output",
                     "whole patch",
                     "patch payload",
                     "Tool was executed",
                     "Capability is authorized",
                     "Approval satisfied",
                     "Policy satisfied",
                     "Workflow may continue"
                 })
        {
            Assert.IsFalse(serialized.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
        }
    }

    [TestMethod]
    public void CapabilitySpecificMissingPackages_ReportMissingMaterial()
    {
        var result = _workflow.Preview(ValidRequest() with { CapabilityName = "implementation.tool.preview" });

        Assert.AreEqual(ToolRequestGatePreviewCandidateStatus.MissingRequiredPreviewMaterial, result.Status);
        CollectionAssert.Contains(result.MissingGateMaterial.ToList(), "implementation proposal package result");
    }

    private static ToolRequestGatePreviewCandidateRequest ValidRequest() =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ToolRequestPreviewReferenceId = "tool-preview-1",
            CapabilityName = "quality.gate.preview",
            SafePurposeSummary = "Preview the quality gate requirements for later human review.",
            InputReferences = [Input()],
            ExpectedOutputReferences = [Output()],
            GateRequirementHints =
            [
                Gate("Human review remains required.", ToolRequestGateKind.HumanReviewRequired),
                Gate("Policy evidence remains required.", ToolRequestGateKind.PolicyEvidenceRequired),
                Gate("Approval evidence remains required.", ToolRequestGateKind.ApprovalRequired)
            ],
            Risks = [Risk()],
            StepEvaluation = StepEvaluation(WorkflowStepRunnerEligibility.EligibleForFutureExecution),
            DryRunResult = DryRun(WorkflowDryRunStatus.DryRunCompleted),
            RouteSuggestion = Route(BoxedLangGraphRouteLabel.EligibleForDryRun)
        };

    private static ToolRequestPreviewInputReference Input(string summary = "Supplied implementation proposal package reference.") =>
        new()
        {
            Kind = ToolRequestPreviewInputKind.ImplementationProposalPackageReference,
            ReferenceId = "input-reference-1",
            SafeSummary = summary
        };

    private static ToolRequestPreviewOutputReference Output(string summary = "Expected validation report reference.") =>
        new()
        {
            Kind = ToolRequestPreviewOutputKind.ValidationReportReference,
            ReferenceId = "output-reference-1",
            SafeSummary = summary
        };

    private static ToolRequestGateRequirementHint Gate(string summary = "Gate preview requires human review.", ToolRequestGateKind kind = ToolRequestGateKind.HumanReviewRequired) =>
        new()
        {
            Kind = kind,
            SeverityHint = ToolRequestGateSeverityHint.High,
            SafeSummary = summary
        };

    private static ToolRequestPreviewRisk Risk(string summary = "Tool execution remains a later governed risk.") =>
        new()
        {
            Kind = ToolRequestPreviewRiskKind.ToolExecutionRisk,
            SeverityHint = ToolRequestGateSeverityHint.High,
            SafeSummary = summary
        };

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

    private static ImplementationProposalPackageCandidateResult ImplementationProposal(ImplementationProposalPackageCandidateStatus status) =>
        new()
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ProposalReferenceId = "implementation-proposal-1",
            ProposalPackageReferenceId = "implementation-proposal-package-1",
            Status = status,
            TargetKind = ImplementationProposalTargetKind.CriticReviewRequest,
            TargetReferenceId = "critic-review-1",
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
            TargetKind = CriticReviewTargetKind.ImplementationProposal,
            TargetReferenceId = "implementation-proposal-1",
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

    private static void AssertAllAuthorityFlagsFalse(ToolRequestGatePreviewCandidateResult result)
    {
        Assert.IsFalse(result.IsToolExecution);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanAuthorizeTool);
        Assert.IsFalse(result.CanReserveTool);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
    }
}

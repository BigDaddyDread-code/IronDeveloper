using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class SourceApplyApprovalRequirementContractTests
{
    private readonly ISourceApplyApprovalRequirementContract _contract = new SourceApplyApprovalRequirementContract();

    [TestMethod]
    public void NullRequest_ReturnsInvalidRequest()
    {
        var result = _contract.Evaluate(null);

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("workflow", SourceApplyApprovalRequirementReason.MissingWorkflowRunId)]
    [DataRow("step", SourceApplyApprovalRequirementReason.MissingWorkflowStepId)]
    [DataRow("sourceApply", SourceApplyApprovalRequirementReason.MissingSourceApplyRequestReference)]
    [DataRow("project", SourceApplyApprovalRequirementReason.MissingProjectReference)]
    [DataRow("target", SourceApplyApprovalRequirementReason.MissingTargetReference)]
    public void MissingIdentity_ReturnsInvalidRequest(string field, SourceApplyApprovalRequirementReason reason)
    {
        var request = field switch
        {
            "workflow" => ValidRequest() with { WorkflowRunId = " " },
            "step" => ValidRequest() with { WorkflowStepId = " " },
            "sourceApply" => ValidRequest() with { SourceApplyRequestReferenceId = " " },
            "project" => ValidRequest() with { ProjectReferenceId = " " },
            _ => ValidRequest() with { TargetReferenceId = " " }
        };

        var result = _contract.Evaluate(request);

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), reason);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void UnknownTargetKind_ReturnsInvalidRequest()
    {
        var result = _contract.Evaluate(ValidRequest() with { TargetKind = SourceApplyTargetKind.Unknown });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.InvalidTargetKind);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void UnknownRequirementKind_ReturnsInvalidRequest()
    {
        var result = _contract.Evaluate(ValidRequest() with { RequirementKind = SourceApplyApprovalRequirementKind.Unknown });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.InvalidRequirementKind);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("evidence", "raw prompt leaked")]
    [DataRow("gate", "raw completion leaked")]
    [DataRow("risk", "raw tool output leaked")]
    [DataRow("correlation", "private reasoning leaked")]
    public void UnsafeSafeMaterial_FailsClosedWithoutEcho(string field, string marker)
    {
        var request = field switch
        {
            "evidence" => ValidRequest() with { EvidenceReferences = [Evidence(summary: marker)] },
            "gate" => ValidRequest() with { GateHints = [Gate(summary: marker)] },
            "risk" => ValidRequest() with { Risks = [Risk(summary: marker)] },
            _ => ValidRequest() with { CorrelationId = marker }
        };

        var result = _contract.Evaluate(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.UnsafeInput);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("approval satisfied")]
    [DataRow("source applied")]
    [DataRow("patch applied")]
    [DataRow("workflow continued")]
    public void AuthorityClaimMarkers_FailClosed(string marker)
    {
        var result = _contract.Evaluate(ValidRequest() with { EvidenceReferences = [Evidence(summary: marker)] });
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.InvalidRequest, result.Status);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void BlockingRunnerEvaluation_BlocksRequirementProduction()
    {
        var result = _contract.Evaluate(ValidRequest() with { StepEvaluation = HumanApprovalPackageFixtures.StepEvaluation(WorkflowStepRunnerEligibility.BlockedByBoundary) });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.BlockedByRunnerEvaluation);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void BlockingDryRun_BlocksRequirementProduction()
    {
        var result = _contract.Evaluate(ValidRequest() with { DryRunResult = HumanApprovalPackageFixtures.DryRun(WorkflowDryRunStatus.BlockedByPolicyPreflight) });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.BlockedByDryRun);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void BlockingRouteSuggestion_BlocksRequirementProduction()
    {
        var result = _contract.Evaluate(ValidRequest() with { RouteSuggestion = HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.BlockedPolicyPreflight) });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.BlockedByRouteSuggestion);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("source")]
    [DataRow("approval")]
    [DataRow("transition")]
    public void RouteSuggestionAuthority_BlocksRequirementProduction(string authority)
    {
        var route = authority switch
        {
            "source" => HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable) with { SourceChangeAllowed = true },
            "approval" => HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable) with { ApprovalChangeAllowed = true },
            _ => HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable) with { WorkflowStateChangeAllowed = true }
        };

        var result = _contract.Evaluate(ValidRequest() with { RouteSuggestion = route });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.BlockedByRouteSuggestion);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void InvalidImplementationProposal_BlocksRequirementProduction()
    {
        var result = _contract.Evaluate(ValidRequest() with
        {
            ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal(ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate)
        });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.BlockedByImplementationProposal);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void InvalidHumanApprovalPackage_BlocksRequirementProduction()
    {
        var result = _contract.Evaluate(ValidRequest() with
        {
            HumanApprovalPackage = HumanApprovalPackage(ImplementationProposal(), HumanApprovalPackageCandidateStatus.BlockedByWorkflowGate)
        });

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.BlockedByHumanApprovalPackage);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("implementation")]
    [DataRow("approvalPackage")]
    [DataRow("gate")]
    [DataRow("evidence")]
    public void MissingRequiredApprovalMaterial_ReturnsMissingRequiredApprovalMaterial(string missing)
    {
        var request = missing switch
        {
            "implementation" => ValidRequest() with { ImplementationProposal = null },
            "approvalPackage" => ValidRequest() with { HumanApprovalPackage = null },
            "gate" => ValidRequest() with { GateHints = [] },
            _ => ValidRequest() with { EvidenceReferences = [] }
        };

        var result = _contract.Evaluate(request);

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.MissingRequiredApprovalMaterial, result.Status);
        Assert.IsTrue(result.MissingRequirements.Count > 0);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ValidMaterial_ReturnsApprovalRequired()
    {
        var result = _contract.Evaluate(ValidRequest());

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.ApprovalRequired, result.Status);
        Assert.IsTrue(result.RequirementReferenceId.StartsWith("source-apply-approval-requirement:", StringComparison.Ordinal));
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.SourceApplyNotImplemented);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.ApprovalNotGranted);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.ApprovalNotSatisfied);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.SourceNotApplied);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.PatchNotApplied);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.WorkflowNotTransitioned);
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Source apply requires explicit later approval.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Human approval package is review material only.");
        CollectionAssert.Contains(result.SafeSummaryLines.ToList(), "Source apply remains unimplemented.");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ApprovalRequiredStatus_DoesNotMeanApproved()
    {
        var result = _contract.Evaluate(ValidRequest());

        Assert.AreEqual(SourceApplyApprovalRequirementStatus.ApprovalRequired, result.Status);
        Assert.IsFalse(result.IsApproval);
        Assert.IsFalse(result.IsApprovalSatisfied);
        Assert.IsFalse(result.CanSatisfyApproval);
    }

    [TestMethod]
    public void HumanApprovalPackage_DoesNotSatisfyApproval()
    {
        var result = _contract.Evaluate(ValidRequest());

        Assert.IsNotNull(ValidRequest().HumanApprovalPackage);
        Assert.IsFalse(result.CanSatisfyApproval);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.ApprovalNotSatisfied);
    }

    [TestMethod]
    public void ImplementationProposal_DoesNotAuthorizeSourceApply()
    {
        var result = _contract.Evaluate(ValidRequest());

        Assert.IsNotNull(ValidRequest().ImplementationProposal);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.CanApplyPatch);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.SourceNotApplied);
    }

    [TestMethod]
    public void ResultHasRequirementOnlyTrueAndAllAuthorityFlagsFalse()
    {
        AssertNoAuthority(_contract.Evaluate(ValidRequest()));
    }

    [TestMethod]
    public void SameRequest_GivesDeterministicResult()
    {
        var request = ValidRequest();
        var first = JsonSerializer.Serialize(_contract.Evaluate(request));
        var second = JsonSerializer.Serialize(_contract.Evaluate(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ResultSerializesWithoutRawPrivateOrFullPayloadMaterial()
    {
        var json = JsonSerializer.Serialize(_contract.Evaluate(ValidRequest()));

        AssertDoesNotContainAny(
            json,
            "raw prompt",
            "raw completion",
            "raw tool output",
            "private reasoning",
            "hidden reasoning",
            "whole patch",
            "patch payload",
            "source content");
    }

    internal static SourceApplyApprovalRequirementRequest ValidRequest()
    {
        var implementation = ImplementationProposal();
        return new()
        {
            WorkflowRunId = "workflow-run-137",
            WorkflowStepId = "workflow-step-source-apply-approval-requirement",
            SourceApplyRequestReferenceId = "source-apply-request-137",
            ProjectReferenceId = "project-137",
            TargetReferenceId = "source-apply-target-137",
            TargetKind = SourceApplyTargetKind.ImplementationProposalPackage,
            RequirementKind = SourceApplyApprovalRequirementKind.HumanAndPolicyApprovalRequired,
            ImplementationProposal = implementation,
            HumanApprovalPackage = HumanApprovalPackage(implementation),
            EvidenceReferences =
            [
                Evidence(SourceApplyApprovalEvidenceKind.ImplementationProposalPackageReference, implementation.ProposalPackageReferenceId, "Implementation proposal package reference."),
                Evidence(SourceApplyApprovalEvidenceKind.HumanApprovalPackageReference, "human-approval-package-source-apply-137", "Human approval review package reference.")
            ],
            GateHints =
            [
                Gate(SourceApplyApprovalGateKind.HumanApprovalRequired, SourceApplyApprovalSeverityHint.Critical, "Human approval is required later."),
                Gate(SourceApplyApprovalGateKind.PolicyEvidenceRequired, SourceApplyApprovalSeverityHint.High, "Policy evidence is required later."),
                Gate(SourceApplyApprovalGateKind.SourceMutationForbiddenUntilApproved, SourceApplyApprovalSeverityHint.Critical, "Source mutation remains forbidden.")
            ],
            Risks =
            [
                Risk(SourceApplyApprovalRiskKind.MissingApproval, SourceApplyApprovalSeverityHint.Critical, "Approval has not been recorded."),
                Risk(SourceApplyApprovalRiskKind.CandidatePackageConfusedWithApproval, SourceApplyApprovalSeverityHint.High, "Candidate packages are not approval.")
            ],
            StepEvaluation = HumanApprovalPackageFixtures.StepEvaluation(WorkflowStepRunnerEligibility.EligibleForFutureExecution),
            DryRunResult = HumanApprovalPackageFixtures.DryRun(WorkflowDryRunStatus.DryRunCompleted),
            RouteSuggestion = HumanApprovalPackageFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable),
            CorrelationId = "correlation-137"
        };
    }

    internal static ImplementationProposalPackageCandidateResult ImplementationProposal(
        ImplementationProposalPackageCandidateStatus status = ImplementationProposalPackageCandidateStatus.ProposalPackageProduced) =>
        HumanApprovalPackageFixtures.ImplementationProposal(status) with
        {
            ProposalReferenceId = "implementation-proposal-source-apply-137",
            ProposalPackageReferenceId = "implementation-proposal-package-source-apply-137",
            TargetKind = ImplementationProposalTargetKind.ApprovalPackageReview,
            TargetReferenceId = "source-apply-target-137"
        };

    internal static HumanApprovalPackageCandidateResult HumanApprovalPackage(
        ImplementationProposalPackageCandidateResult implementation,
        HumanApprovalPackageCandidateStatus status = HumanApprovalPackageCandidateStatus.ApprovalPackageProduced) =>
        new HumanApprovalPackageCandidateWorkflow().Prepare(HumanApprovalPackageFixtures.ValidRequest() with
        {
            TargetKind = HumanApprovalTargetKind.SourceApplyCandidate,
            TargetReferenceId = "source-apply-target-137",
            ApprovalKind = HumanApprovalKind.SourceApplyApprovalRequired,
            RequestedDecision = HumanApprovalRequestedDecision.RequestApproveOrRejectLater,
            SafeApprovalSummary = "Human review is required before any later source apply.",
            ImplementationProposal = implementation,
            CandidatePackageReferences =
            [
                HumanApprovalPackageFixtures.Candidate(HumanApprovalCandidatePackageKind.ImplementationProposalPackageCandidate, implementation.ProposalPackageReferenceId)
            ],
            GateHints =
            [
                HumanApprovalPackageFixtures.Gate(HumanApprovalGateKind.HumanReviewRequired),
                HumanApprovalPackageFixtures.Gate(HumanApprovalGateKind.ApprovalRequired),
                HumanApprovalPackageFixtures.Gate(HumanApprovalGateKind.SourceMutationForbiddenUntilApproved)
            ]
        }) with { Status = status };

    internal static SourceApplyApprovalEvidenceReference Evidence(
        SourceApplyApprovalEvidenceKind kind = SourceApplyApprovalEvidenceKind.ExternalArtifactReference,
        string referenceId = "evidence-137",
        string? summary = "Supplied evidence reference.") => new()
    {
        Kind = kind,
        ReferenceId = referenceId,
        SafeSummary = summary
    };

    internal static SourceApplyApprovalGateHint Gate(
        SourceApplyApprovalGateKind kind = SourceApplyApprovalGateKind.HumanApprovalRequired,
        SourceApplyApprovalSeverityHint severity = SourceApplyApprovalSeverityHint.High,
        string? summary = "Source apply approval gate remains closed.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    internal static SourceApplyApprovalRisk Risk(
        SourceApplyApprovalRiskKind kind = SourceApplyApprovalRiskKind.MissingApproval,
        SourceApplyApprovalSeverityHint severity = SourceApplyApprovalSeverityHint.High,
        string? summary = "Source apply requires later approval.") => new()
    {
        Kind = kind,
        SeverityHint = severity,
        SafeSummary = summary
    };

    internal static void AssertNoAuthority(SourceApplyApprovalRequirementResult result)
    {
        Assert.IsTrue(result.IsRequirementOnly);
        Assert.IsFalse(result.IsSourceApply);
        Assert.IsFalse(result.IsApproval);
        Assert.IsFalse(result.IsApprovalSatisfied);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanMutateFiles);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanWriteSql);
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}

using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class HumanApprovalPackageAuthorityBoundaryTests
{
    private readonly IHumanApprovalPackageCandidateWorkflow _workflow = new HumanApprovalPackageCandidateWorkflow();

    [DataTestMethod]
    [DataRow(HumanApprovalKind.ReviewOnly)]
    [DataRow(HumanApprovalKind.SourceApplyApprovalRequired)]
    [DataRow(HumanApprovalKind.ToolExecutionApprovalRequired)]
    [DataRow(HumanApprovalKind.MemoryPromotionApprovalRequired)]
    [DataRow(HumanApprovalKind.RetrievalActivationApprovalRequired)]
    [DataRow(HumanApprovalKind.WorkflowContinuationApprovalRequired)]
    [DataRow(HumanApprovalKind.PolicyExceptionApprovalRequired)]
    public void HumanApprovalPackage_ApprovalKindDoesNotGrantAuthority(HumanApprovalKind approvalKind)
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest() with { ApprovalKind = approvalKind });

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.AreEqual(approvalKind, result.ApprovalKind);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(HumanApprovalRequestedDecision.RequestHumanReview)]
    [DataRow(HumanApprovalRequestedDecision.RequestApproveOrRejectLater)]
    [DataRow(HumanApprovalRequestedDecision.RequestMoreEvidenceLater)]
    [DataRow(HumanApprovalRequestedDecision.RequestPolicyReviewLater)]
    public void HumanApprovalPackage_RequestedDecisionIsNotDecisionMade(HumanApprovalRequestedDecision requestedDecision)
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest() with { RequestedDecision = requestedDecision });

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.AreEqual(requestedDecision, result.RequestedDecision);
        Assert.IsFalse(result.IsApprovalDecision);
        Assert.IsFalse(result.IsApproved);
        Assert.IsFalse(result.IsRejected);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(HumanApprovalTargetKind.ImplementationProposalPackage)]
    [DataRow(HumanApprovalTargetKind.ToolRequestGatePreview)]
    [DataRow(HumanApprovalTargetKind.MemoryImprovementPackage)]
    [DataRow(HumanApprovalTargetKind.CriticReviewRequest)]
    [DataRow(HumanApprovalTargetKind.TestFailureReview)]
    [DataRow(HumanApprovalTargetKind.SourceApplyCandidate)]
    [DataRow(HumanApprovalTargetKind.RetrievalActivationCandidate)]
    [DataRow(HumanApprovalTargetKind.MemoryPromotionCandidate)]
    [DataRow(HumanApprovalTargetKind.WorkflowContinuationCandidate)]
    public void HumanApprovalPackage_TargetKindDoesNotCreatePermission(HumanApprovalTargetKind targetKind)
    {
        var request = RequestForTarget(targetKind);

        var result = _workflow.Prepare(request);

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.AreEqual(targetKind, result.TargetKind);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(HumanApprovalGateKind.HumanReviewRequired)]
    [DataRow(HumanApprovalGateKind.ApprovalRequired)]
    [DataRow(HumanApprovalGateKind.PolicyEvidenceRequired)]
    [DataRow(HumanApprovalGateKind.A2aValidationRequired)]
    [DataRow(HumanApprovalGateKind.ThoughtLedgerReferenceRequired)]
    [DataRow(HumanApprovalGateKind.DryRunRequired)]
    [DataRow(HumanApprovalGateKind.SourceMutationForbiddenUntilApproved)]
    [DataRow(HumanApprovalGateKind.ToolExecutionForbiddenUntilApproved)]
    [DataRow(HumanApprovalGateKind.MemoryPromotionForbiddenUntilApproved)]
    [DataRow(HumanApprovalGateKind.RetrievalActivationForbiddenUntilApproved)]
    [DataRow(HumanApprovalGateKind.WorkflowContinuationForbiddenUntilApproved)]
    public void HumanApprovalPackage_GateHintDoesNotSatisfyGate(HumanApprovalGateKind gateKind)
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest() with
        {
            GateHints = [HumanApprovalPackageFixtures.Gate(gateKind)]
        });

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.AreEqual(gateKind, result.GateHints.Single().Kind);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("source", HumanApprovalTargetKind.SourceApplyCandidate, HumanApprovalKind.SourceApplyApprovalRequired)]
    [DataRow("tool", HumanApprovalTargetKind.ToolRequestGatePreview, HumanApprovalKind.ToolExecutionApprovalRequired)]
    [DataRow("memory", HumanApprovalTargetKind.MemoryPromotionCandidate, HumanApprovalKind.MemoryPromotionApprovalRequired)]
    [DataRow("retrieval", HumanApprovalTargetKind.RetrievalActivationCandidate, HumanApprovalKind.RetrievalActivationApprovalRequired)]
    [DataRow("workflow", HumanApprovalTargetKind.WorkflowContinuationCandidate, HumanApprovalKind.WorkflowContinuationApprovalRequired)]
    public void HumanApprovalPackage_SensitiveTargetStillCannotAct(string scenario, HumanApprovalTargetKind targetKind, HumanApprovalKind approvalKind)
    {
        var result = _workflow.Prepare(RequestForTarget(targetKind) with { ApprovalKind = approvalKind });

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status, scenario);
        Assert.IsFalse(result.CanMutateSource, scenario);
        Assert.IsFalse(result.CanApplyPatch, scenario);
        Assert.IsFalse(result.CanInvokeTool, scenario);
        Assert.IsFalse(result.CanPromoteMemory, scenario);
        Assert.IsFalse(result.CanActivateRetrieval, scenario);
        Assert.IsFalse(result.CanTransitionWorkflow, scenario);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void HumanApprovalPackage_UpstreamPackageEvidenceDoesNotBecomeApprovalEvidence()
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest() with
        {
            TargetKind = HumanApprovalTargetKind.ToolRequestGatePreview,
            ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview()
        });

        Assert.AreEqual(HumanApprovalPackageCandidateStatus.ApprovalPackageProduced, result.Status);
        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == HumanApprovalEvidenceKind.ToolRequestPreviewReference));
        Assert.IsFalse(result.IsApprovalDecision);
        Assert.IsFalse(result.CanSatisfyApproval);
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void HumanApprovalPackage_AllFalseFlagsRemainFalseInSerializedOutput()
    {
        var result = _workflow.Prepare(HumanApprovalPackageFixtures.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        Assert.IsTrue(json.Contains("\"IsPackageOnly\":true", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(":true", StringComparison.Ordinal) &&
                       json.Contains("\"Can", StringComparison.Ordinal) &&
                       !json.Contains("\"IsPackageOnly\":true", StringComparison.Ordinal));
        HumanApprovalPackageFixtures.AssertNoAuthority(result);
    }

    private static HumanApprovalPackageCandidateRequest RequestForTarget(HumanApprovalTargetKind targetKind)
    {
        var request = HumanApprovalPackageFixtures.ValidRequest() with
        {
            TargetKind = targetKind,
            TargetReferenceId = $"target-{targetKind}"
        };

        return targetKind switch
        {
            HumanApprovalTargetKind.MemoryImprovementPackage or
                HumanApprovalTargetKind.MemoryPromotionCandidate or
                HumanApprovalTargetKind.RetrievalActivationCandidate => request with
                {
                    MemoryImprovementPackage = HumanApprovalPackageFixtures.MemoryImprovement()
                },
            HumanApprovalTargetKind.ToolRequestGatePreview => request with
            {
                ToolRequestGatePreview = HumanApprovalPackageFixtures.ToolPreview()
            },
            HumanApprovalTargetKind.ImplementationProposalPackage or
                HumanApprovalTargetKind.SourceApplyCandidate => request with
                {
                    ImplementationProposal = HumanApprovalPackageFixtures.ImplementationProposal()
                },
            HumanApprovalTargetKind.CriticReviewRequest => request with
            {
                CriticReviewRequest = HumanApprovalPackageFixtures.CriticReview()
            },
            HumanApprovalTargetKind.TestFailureReview => request with
            {
                TestFailureReview = HumanApprovalPackageFixtures.TestFailure()
            },
            _ => request
        };
    }
}

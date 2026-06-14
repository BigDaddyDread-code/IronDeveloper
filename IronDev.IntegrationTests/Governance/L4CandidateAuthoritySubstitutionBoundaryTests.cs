using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class L4CandidateAuthoritySubstitutionBoundaryTests
{
    [DataRow("TestFailureReviewCandidateResult", "CriticReviewRequest", "CanDispatchCriticAgent")]
    [DataRow("TestFailureReviewCandidateResult", "ImplementationAuthority", "IsImplementation")]
    [DataRow("TestFailureReviewCandidateResult", "ToolRequestAuthority", "CanInvokeTool")]
    [DataRow("TestFailureReviewCandidateResult", "MemoryImprovementAuthority", "CanPromoteMemory")]
    [DataRow("TestFailureReviewCandidateResult", "HumanApprovalAuthority", "CanSatisfyApproval")]
    [DataRow("TestFailureReviewCandidateResult", "DogfoodEvidenceProof", "IsValidationProof")]
    [DataRow("TestFailureReviewCandidateResult", "RepeatedFailurePatternProof", "IsPatternProof")]
    [DataRow("CriticReviewRequestCandidateResult", "ActualCriticReview", "IsReviewDecision")]
    [DataRow("CriticReviewRequestCandidateResult", "Approval", "CanApprove")]
    [DataRow("CriticReviewRequestCandidateResult", "ImplementationAuthorization", "CanMutateSource")]
    [DataRow("CriticReviewRequestCandidateResult", "ToolAuthorization", "CanInvokeTool")]
    [DataRow("CriticReviewRequestCandidateResult", "MemoryPromotionAuthorization", "CanPromoteMemory")]
    [DataRow("ImplementationProposalPackageCandidateResult", "Implementation", "IsImplementation")]
    [DataRow("ImplementationProposalPackageCandidateResult", "Patch", "IsPatch")]
    [DataRow("ImplementationProposalPackageCandidateResult", "SourceApplyAuthorization", "CanMutateSource")]
    [DataRow("ImplementationProposalPackageCandidateResult", "TicketCreation", "CanCreateTicket")]
    [DataRow("ImplementationProposalPackageCandidateResult", "ApprovalSatisfaction", "CanSatisfyApproval")]
    [DataRow("ToolRequestGatePreviewCandidateResult", "ToolInvocation", "CanInvokeTool")]
    [DataRow("ToolRequestGatePreviewCandidateResult", "ToolAuthorization", "CanAuthorizeTool")]
    [DataRow("ToolRequestGatePreviewCandidateResult", "ToolReservation", "CanReserveTool")]
    [DataRow("ToolRequestGatePreviewCandidateResult", "GateSatisfaction", "CanSatisfyPolicy")]
    [DataRow("ToolRequestGatePreviewCandidateResult", "ToolOutput", "IsToolExecution")]
    [DataRow("MemoryImprovementPackageCandidateResult", "MemoryPromotion", "CanPromoteMemory")]
    [DataRow("MemoryImprovementPackageCandidateResult", "AcceptedMemoryMutation", "CanMutateAcceptedMemory")]
    [DataRow("MemoryImprovementPackageCandidateResult", "RetrievalActivation", "CanActivateRetrieval")]
    [DataRow("MemoryImprovementPackageCandidateResult", "DuplicateResolution", "CanResolveDuplicate")]
    [DataRow("MemoryImprovementPackageCandidateResult", "ConflictResolution", "CanResolveConflict")]
    [DataRow("MemoryImprovementPackageCandidateResult", "ProjectTruth", "IsAcceptedMemory")]
    [DataRow("HumanApprovalPackageCandidateResult", "Approval", "IsApproved")]
    [DataRow("HumanApprovalPackageCandidateResult", "Rejection", "IsRejected")]
    [DataRow("HumanApprovalPackageCandidateResult", "ApprovalHaltSatisfaction", "CanSatisfyApproval")]
    [DataRow("HumanApprovalPackageCandidateResult", "PolicySatisfaction", "CanSatisfyPolicy")]
    [DataRow("HumanApprovalPackageCandidateResult", "WorkflowContinuation", "CanTransitionWorkflow")]
    [DataRow("HumanApprovalPackageCandidateResult", "SourceToolMemoryRetrievalAction", "CanMutateSource")]
    [DataRow("DogfoodEvidenceBundleCandidateResult", "ValidationProof", "IsValidationProof")]
    [DataRow("DogfoodEvidenceBundleCandidateResult", "ReleaseReadiness", "IsReleaseReady")]
    [DataRow("DogfoodEvidenceBundleCandidateResult", "DogfoodRunOutput", "CanRunDogfood")]
    [DataRow("DogfoodEvidenceBundleCandidateResult", "ApprovalPolicySatisfaction", "CanSatisfyApproval")]
    [DataRow("DogfoodEvidenceBundleCandidateResult", "WorkflowContinuationAuthorization", "CanTransitionWorkflow")]
    [DataRow("RepeatedFailurePatternReviewCandidateResult", "PatternProof", "IsPatternProof")]
    [DataRow("RepeatedFailurePatternReviewCandidateResult", "RootCauseProof", "IsRootCauseProof")]
    [DataRow("RepeatedFailurePatternReviewCandidateResult", "TicketCreation", "CanCreateTicket")]
    [DataRow("RepeatedFailurePatternReviewCandidateResult", "IncidentCreation", "CanCreateIncident")]
    [DataRow("RepeatedFailurePatternReviewCandidateResult", "MemoryPromotion", "CanPromoteMemory")]
    [DataRow("RepeatedFailurePatternReviewCandidateResult", "AcceptedMemory", "IsAcceptedMemory")]
    [DataTestMethod]
    public void L4CandidateOutput_CannotSubstituteForOtherAuthority(string candidateName, string forbiddenSubstitution, string authorityFlag)
    {
        var candidate = L4CandidateCannotMutateSourceOrMemoryTests.CandidateResults().Single(item => item.Name == candidateName);

        L4CandidateCannotMutateSourceOrMemoryTests.AssertFalsePropertyIfPresent(candidate.Result, authorityFlag);
        L4CandidateCannotMutateSourceOrMemoryTests.AssertNoAuthorityProperties(candidate.Result);
        AssertStatusDoesNotClaimSubstitution(candidate.Result, forbiddenSubstitution);
    }

    [DataTestMethod]
    [DataRow("ApprovalEvidence")]
    [DataRow("ApprovalSatisfaction")]
    [DataRow("PolicyEvidence")]
    [DataRow("PolicySatisfaction")]
    [DataRow("WorkflowTransition")]
    [DataRow("SourceMutation")]
    [DataRow("PatchApply")]
    [DataRow("ToolExecution")]
    [DataRow("ToolAuthorization")]
    [DataRow("CommandExecution")]
    [DataRow("AgentDispatch")]
    [DataRow("ModelCall")]
    [DataRow("PromptBuild")]
    [DataRow("TicketCreation")]
    [DataRow("IncidentCreation")]
    [DataRow("MemoryPromotion")]
    [DataRow("AcceptedMemoryMutation")]
    [DataRow("RetrievalActivation")]
    [DataRow("ValidationProof")]
    [DataRow("ReleaseReadiness")]
    [DataRow("PatternProof")]
    [DataRow("RootCauseProof")]
    [DataRow("ProjectTruth")]
    public void L4CandidateResults_CannotBecomeForbiddenSubstitutionMatrix(string forbiddenSubstitution)
    {
        foreach (var candidate in L4CandidateCannotMutateSourceOrMemoryTests.CandidateResults())
        {
            L4CandidateCannotMutateSourceOrMemoryTests.AssertNoAuthorityProperties(candidate.Result);
            AssertNoExactPublicBooleanProperty(candidate.Result, forbiddenSubstitution);
            AssertStatusDoesNotClaimSubstitution(candidate.Result, forbiddenSubstitution);
        }
    }

    [TestMethod]
    public void L4CandidateOutputs_CannotTurnEvidenceTraceabilityReceiptOrPackageIntoCapability()
    {
        foreach (var candidate in L4CandidateCannotMutateSourceOrMemoryTests.CandidateResults())
        {
            L4CandidateCannotMutateSourceOrMemoryTests.AssertNoAuthorityProperties(candidate.Result);
            AssertNoExactPublicBooleanProperty(candidate.Result, "EvidenceIsApproval");
            AssertNoExactPublicBooleanProperty(candidate.Result, "TraceabilityIsAuthority");
            AssertNoExactPublicBooleanProperty(candidate.Result, "ReceiptIsCapability");
            AssertNoExactPublicBooleanProperty(candidate.Result, "PackageIsPromotion");
        }
    }

    private static void AssertNoExactPublicBooleanProperty(object result, string propertyName)
    {
        var property = result.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
            return;

        Assert.AreEqual(typeof(bool), property.PropertyType, $"{result.GetType().Name}.{propertyName} must remain a bool flag.");
        Assert.AreEqual(false, property.GetValue(result), $"{result.GetType().Name}.{propertyName} must be false.");
    }

    private static void AssertStatusDoesNotClaimSubstitution(object result, string forbiddenSubstitution)
    {
        var status = result.GetType().GetProperty("Status", BindingFlags.Instance | BindingFlags.Public)?.GetValue(result)?.ToString() ?? string.Empty;
        foreach (var phrase in ForbiddenStatusClaims(forbiddenSubstitution))
        {
            Assert.IsFalse(status.Contains(phrase, StringComparison.OrdinalIgnoreCase), $"{result.GetType().Name}.Status must not claim {phrase}.");
        }
    }

    private static IReadOnlyList<string> ForbiddenStatusClaims(string forbiddenSubstitution)
    {
        var common = new[]
        {
            $"{forbiddenSubstitution}Satisfied",
            $"{forbiddenSubstitution}Granted",
            $"{forbiddenSubstitution}Allowed",
            $"{forbiddenSubstitution}Complete",
            $"{forbiddenSubstitution}Completed",
            $"{forbiddenSubstitution}Ready",
            $"Can{forbiddenSubstitution}",
            $"Is{forbiddenSubstitution}",
        };

        return forbiddenSubstitution switch
        {
            "Approval" => common.Concat(new[] { "Approved", "ApprovalSatisfied", "ApprovalGranted" }).ToArray(),
            "Policy" => common.Concat(new[] { "PolicySatisfied", "PolicyGranted" }).ToArray(),
            "Execution" => common.Concat(new[] { "Executed", "ExecutionAllowed", "ExecutionStarted" }).ToArray(),
            "SourceMutation" => common.Concat(new[] { "SourceMutated", "PatchApplied" }).ToArray(),
            "MemoryPromotion" => common.Concat(new[] { "MemoryPromoted", "PromotionComplete" }).ToArray(),
            _ => common,
        };
    }
}

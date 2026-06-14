using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class PatchProposalEvidencePackageAuthorityBoundaryTests
{
    private readonly IPatchProposalEvidencePackageWorkflow _workflow = new PatchProposalEvidencePackageWorkflow();

    [TestMethod]
    public void PackageResult_CannotGeneratePatchOrBecomePatchMaterial()
    {
        var result = Produced();

        Assert.IsFalse(result.CanGeneratePatch);
        Assert.IsFalse(result.IsPatch);
        Assert.IsFalse(result.IsDiff);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PatchNotGenerated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.DiffNotGenerated);
    }

    [TestMethod]
    public void PackageResult_CannotApplyPatchOrSource()
    {
        var result = Produced();

        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.IsSourceApply);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PatchNotApplied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.SourceNotApplied);
    }

    [TestMethod]
    public void PackageResult_CannotMutateOrReadFiles()
    {
        var result = Produced();

        Assert.IsFalse(result.CanMutateFiles);
        Assert.IsFalse(result.CanReadSourceFiles);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.FilesNotMutated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.SourceFilesNotRead);
    }

    [TestMethod]
    public void PackageResult_CannotRunCommandsOrInvokeTools()
    {
        var result = Produced();

        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.CommandNotRun);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.ToolNotInvoked);
    }

    [TestMethod]
    public void PackageResult_CannotDispatchAgentsCallModelsOrBuildPrompts()
    {
        var result = Produced();

        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanBuildPrompt);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.AgentNotDispatched);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.ModelNotCalled);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PromptNotBuilt);
    }

    [TestMethod]
    public void PackageResult_CannotSatisfyApprovalPolicyOrWorkflow()
    {
        var result = Produced();

        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.ApprovalNotSatisfied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.PolicyNotSatisfied);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.WorkflowNotTransitioned);
    }

    [TestMethod]
    public void PackageResult_CannotCreateTicketsPromoteMemoryActivateRetrievalOrWriteSql()
    {
        var result = Produced();

        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanWriteSql);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.TicketNotCreated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.MemoryNotPromoted);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.RetrievalNotActivated);
        CollectionAssert.Contains(result.Reasons.ToList(), PatchProposalEvidencePackageReason.SqlNotWritten);
    }

    [TestMethod]
    public void PackageResult_CannotSatisfyApprovalHalt()
    {
        var result = Produced();

        Assert.AreEqual(PatchProposalEvidencePackageStatus.PatchProposalEvidencePackageProduced, result.Status);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.SafePackageSummaryLines.Any(line => line.Contains("approval satisfied", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void PackageResult_CannotBypassPolicyPreflight()
    {
        var result = Produced();

        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.SafePackageSummaryLines.Any(line => line.Contains("policy satisfied", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void PackageResult_CannotBypassA2aValidation()
    {
        var result = Produced();

        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.SafePackageSummaryLines.Any(line => line.Contains("agent dispatched", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void PackageResult_CannotBeUsedAsSourceApplyApprovalOrReadiness()
    {
        var result = Produced();

        Assert.IsFalse(result.IsSourceApply);
        Assert.IsFalse(result.CanApplySource);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.AreNotEqual("ApprovalRequired", result.Status.ToString());
        Assert.AreNotEqual("SourceApplyReady", result.Status.ToString());
    }

    [TestMethod]
    public void PackageResult_DoesNotTreatImplementationProposalAsImplementation()
    {
        var result = Produced();

        Assert.IsFalse(result.IsImplementation);
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Implementation proposal package is review material only.");
    }

    [TestMethod]
    public void PackageResult_DoesNotTreatSourceApplyRequirementAsApprovalSatisfaction()
    {
        var result = Produced();

        Assert.IsFalse(result.CanSatisfyApproval);
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Source apply approval requirement is requirement material only.");
    }

    [TestMethod]
    public void PackageResult_DoesNotTreatHumanApprovalPackageAsApproval()
    {
        var result = Produced();

        Assert.IsFalse(result.CanSatisfyApproval);
        CollectionAssert.Contains(result.SafePackageSummaryLines.ToList(), "Human approval package is review material only.");
    }

    [TestMethod]
    public void AffectedFileReference_DoesNotBecomeSourceAccess()
    {
        var result = Produced();

        Assert.AreEqual(PatchProposalAffectedAreaKind.FilePathReference, result.AffectedAreas.Single().Kind);
        Assert.IsFalse(result.CanReadSourceFiles);
        Assert.IsFalse(result.CanMutateFiles);
    }

    [TestMethod]
    public void ExpectedValidationReference_DoesNotBecomeValidationProof()
    {
        var result = Produced();

        Assert.AreEqual(PatchProposalExpectedValidationKind.FocusedTestBandReference, result.ExpectedValidationReferences.Single().Kind);
        Assert.IsFalse(result.CanRunCommand);
        Assert.IsFalse(result.CanInvokeTool);
    }

    [TestMethod]
    public void PackageId_DoesNotBecomeDurableTruth()
    {
        var result = Produced();

        Assert.IsTrue(result.PackageReferenceId.StartsWith("patch-proposal-evidence-package:", StringComparison.Ordinal));
        Assert.IsFalse(result.CanWriteSql);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }

    private PatchProposalEvidencePackageResult Produced()
    {
        var result = _workflow.Prepare(PatchProposalEvidencePackageTests.ValidRequest());
        Assert.AreEqual(PatchProposalEvidencePackageStatus.PatchProposalEvidencePackageProduced, result.Status);
        return result;
    }
}

using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class SourceApplyApprovalRequirementAuthorityBoundaryTests
{
    private static SourceApplyApprovalRequirementResult Result() =>
        new SourceApplyApprovalRequirementContract().Evaluate(SourceApplyApprovalRequirementContractTests.ValidRequest());

    [TestMethod]
    public void RequirementResult_CannotApplySource() => Assert.IsFalse(Result().CanApplySource);

    [TestMethod]
    public void RequirementResult_CannotApplyPatch() => Assert.IsFalse(Result().CanApplyPatch);

    [TestMethod]
    public void RequirementResult_CannotMutateFiles() => Assert.IsFalse(Result().CanMutateFiles);

    [TestMethod]
    public void RequirementResult_CannotRunCommands() => Assert.IsFalse(Result().CanRunCommand);

    [TestMethod]
    public void RequirementResult_CannotInvokeTools() => Assert.IsFalse(Result().CanInvokeTool);

    [TestMethod]
    public void RequirementResult_CannotDispatchAgents() => Assert.IsFalse(Result().CanDispatchAgent);

    [TestMethod]
    public void RequirementResult_CannotCallModels() => Assert.IsFalse(Result().CanCallModel);

    [TestMethod]
    public void RequirementResult_CannotBuildPrompts() => Assert.IsFalse(Result().CanBuildPrompt);

    [TestMethod]
    public void RequirementResult_CannotSatisfyApproval() => Assert.IsFalse(Result().CanSatisfyApproval);

    [TestMethod]
    public void RequirementResult_CannotSatisfyPolicy() => Assert.IsFalse(Result().CanSatisfyPolicy);

    [TestMethod]
    public void RequirementResult_CannotTransitionWorkflow() => Assert.IsFalse(Result().CanTransitionWorkflow);

    [TestMethod]
    public void RequirementResult_CannotCreateTickets() => Assert.IsFalse(Result().CanCreateTicket);

    [TestMethod]
    public void RequirementResult_CannotPromoteMemory() => Assert.IsFalse(Result().CanPromoteMemory);

    [TestMethod]
    public void RequirementResult_CannotActivateRetrieval() => Assert.IsFalse(Result().CanActivateRetrieval);

    [TestMethod]
    public void RequirementResult_CannotWriteSql() => Assert.IsFalse(Result().CanWriteSql);

    [TestMethod]
    public void RequirementResult_CannotSatisfyApprovalHalt()
    {
        var result = Result();

        Assert.IsFalse(result.CanSatisfyApproval);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.ApprovalNotSatisfied);
    }

    [TestMethod]
    public void RequirementResult_CannotBypassPolicyPreflight()
    {
        var result = Result();

        Assert.IsFalse(result.CanSatisfyPolicy);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.PolicyNotSatisfied);
    }

    [TestMethod]
    public void RequirementResult_CannotBypassA2aValidation()
    {
        var result = Result();

        Assert.IsFalse(result.CanTransitionWorkflow);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.WorkflowNotTransitioned);
    }

    [TestMethod]
    public void RequirementResult_CannotBeUsedAsSourceApplyApproval()
    {
        var result = Result();

        Assert.IsFalse(result.IsApproval);
        Assert.AreNotEqual("approval", result.RequirementReferenceId, ignoreCase: true);
    }

    [TestMethod]
    public void RequirementResult_CannotBeUsedAsAcceptedApprovalRecord()
    {
        var result = Result();

        Assert.IsFalse(result.IsApprovalSatisfied);
        Assert.IsFalse(result.RequirementReferenceId.Contains("accepted-approval", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RequirementResult_CannotTreatHumanApprovalPackageAsApproval()
    {
        var result = Result();

        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == SourceApplyApprovalEvidenceKind.HumanApprovalPackageReference));
        Assert.IsFalse(result.IsApproval);
        Assert.IsFalse(result.IsApprovalSatisfied);
    }

    [TestMethod]
    public void RequirementResult_CannotTreatImplementationProposalAsPatch()
    {
        var result = Result();

        Assert.IsTrue(result.EvidenceReferences.Any(reference => reference.Kind == SourceApplyApprovalEvidenceKind.ImplementationProposalPackageReference));
        Assert.IsFalse(result.CanApplyPatch);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.PatchNotApplied);
    }

    [TestMethod]
    public void RequirementResult_CannotTreatPackageIdAsDurableTruth()
    {
        var result = Result();

        Assert.IsTrue(result.RequirementReferenceId.StartsWith("source-apply-approval-requirement:", StringComparison.Ordinal));
        Assert.IsFalse(result.CanWriteSql);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.SqlNotWritten);
    }

    [TestMethod]
    public void RequirementResult_CannotGrantWorkflowContinuation()
    {
        var result = Result();

        Assert.IsFalse(result.CanTransitionWorkflow);
        CollectionAssert.Contains(result.Reasons.ToList(), SourceApplyApprovalRequirementReason.WorkflowNotTransitioned);
    }
}

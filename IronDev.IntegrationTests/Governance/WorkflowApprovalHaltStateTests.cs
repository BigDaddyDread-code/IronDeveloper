using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowApprovalHalt")]
public sealed class WorkflowApprovalHaltStateTests
{
    private readonly WorkflowApprovalHaltEvaluator _evaluator = new();

    [TestMethod]
    public void WorkflowApprovalHalt_MissingApprovalEvidenceReturnsHalt()
    {
        var result = _evaluator.Evaluate(Request([]));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.MissingApprovalEvidence);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltCannotExecute);
        Assert.AreEqual(1, result.MissingApprovalRequirements.Count);
        Assert.AreEqual("human-approval-001", result.MissingApprovalRequirements[0].RequirementId);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_ApprovalEvidencePresentIsFutureEvidenceOnly()
    {
        var result = _evaluator.Evaluate(Request([Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")]));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution, result.Status);
        Assert.AreEqual(0, result.MissingApprovalRequirements.Count);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalEvidenceIsNotApprovalMutation);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltCannotTransitionWorkflow);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_NullRequirementSafeSummaryNormalizesWithoutCrashing()
    {
        var result = _evaluator.Evaluate(Request([]) with
        {
            RequiredApprovals =
            [
                Requirement(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001") with
                {
                    SafeSummary = null!
                }
            ]
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, result.Status);
        Assert.AreEqual(1, result.MissingApprovalRequirements.Count);
        Assert.AreEqual(string.Empty, result.MissingApprovalRequirements[0].SafeSummary);
        Assert.IsFalse(serialized.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkflowApprovalHalt_NotApprovalRequiredDoesNotInventApproval()
    {
        var result = _evaluator.Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = "workflow-step-001",
            RequiredApprovals = [],
            AvailableApprovalEvidence = []
        });

        Assert.AreEqual(WorkflowApprovalHaltStatus.NotApprovalRequired, result.Status);
        Assert.AreEqual(0, result.MissingApprovalRequirements.Count);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval);
    }

    [DataTestMethod]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("whole patch")]
    [DataRow("approval granted")]
    [DataRow("policy satisfied")]
    [DataRow("execution allowed")]
    public void WorkflowApprovalHalt_UnsafeApprovalRequirementFailsClosedWithoutSerializingMarker(string marker)
    {
        var result = _evaluator.Evaluate(Request([]) with
        {
            RequiredApprovals =
            [
                Requirement(WorkflowApprovalRequirementKind.HumanApprovalReference, $"{marker} approval")
            ]
        });
        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowApprovalHaltStatus.InvalidApprovalRequirement, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.UnsafeApprovalReference);
        Assert.AreEqual(0, result.MissingApprovalRequirements.Count);
        Assert.IsFalse(serialized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkflowApprovalHalt_AuthorityClaimingEvidenceFails()
    {
        var result = _evaluator.Evaluate(Request(
            [
                Evidence(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001") with
                {
                    GrantsApproval = true
                }
            ]));

        Assert.AreEqual(WorkflowApprovalHaltStatus.InvalidApprovalRequirement, result.Status);
        CollectionAssert.Contains(result.Reasons.ToList(), WorkflowApprovalHaltReason.InvalidApprovalRequirement);
    }

    [TestMethod]
    public void WorkflowApprovalHalt_ExactKindAndReferenceMatchRequired()
    {
        var result = _evaluator.Evaluate(Request([Evidence(WorkflowApprovalRequirementKind.ApprovalDecisionReference, "human-approval-001")]));

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, result.Status);
        Assert.AreEqual(1, result.MissingApprovalRequirements.Count);
    }

    internal static WorkflowApprovalHaltEvaluationRequest Request(IReadOnlyList<WorkflowApprovalEvidenceReference> evidence) =>
        new()
        {
            WorkflowStepId = "workflow-step-001",
            RequiredApprovals =
            [
                Requirement(WorkflowApprovalRequirementKind.HumanApprovalReference, "human-approval-001")
            ],
            AvailableApprovalEvidence = evidence
        };

    internal static WorkflowApprovalHaltRequirement Requirement(WorkflowApprovalRequirementKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            RequirementId = referenceId,
            SafeSummary = "Approval evidence reference required before future execution."
        };

    internal static WorkflowApprovalEvidenceReference Evidence(WorkflowApprovalRequirementKind kind, string referenceId) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            CorrelationId = "approval-halt-001",
            SafeSummary = "Approval evidence reference only."
        };
}

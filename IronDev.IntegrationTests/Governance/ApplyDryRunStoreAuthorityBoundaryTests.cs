using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApplyDryRunStore")]
[TestCategory("ApplyDryRunAuthorityBoundary")]
public sealed class ApplyDryRunStoreAuthorityBoundaryTests
{
    [TestMethod]
    public void ApplyDryRunStoreStatuses_DoNotExposeExecutionApprovalOrMutationStates()
    {
        AssertNoForbiddenTokens(
            string.Join("\n", Enum.GetNames<ApplyDryRunStoreStatus>()),
            "Approved",
            "ApprovalSatisfied",
            "Executed",
            "Running",
            "Applied",
            "Mutated",
            "Promoted",
            "Dispatched",
            "PolicySatisfied",
            "WorkflowTransitioned");

        AssertNoForbiddenTokens(
            string.Join("\n", Enum.GetNames<ApplyDryRunRecordStatus>()),
            "Approved",
            "Executed",
            "Applied",
            "Promoted");
    }

    [TestMethod]
    public void ApplyDryRunStoreContract_HasNoRunnerOrExecutorSurface()
    {
        var methods = typeof(IApplyDryRunStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetByIdAsync", "ListByControlledApplyPlanAsync", "ListByWorkflowRunAsync" },
            methods);

        AssertNoForbiddenTokens(
            string.Join("\n", methods),
            "Execute",
            "Dispatch",
            "Approve",
            "SatisfyPolicy",
            "ApplySource",
            "PromoteMemory",
            "Transition",
            "Rollback");
    }

    [TestMethod]
    public void ApplyDryRunRecord_BoundaryFlagsDefaultToNoAuthorityOrAction()
    {
        var record = new ApplyDryRunRecord
        {
            DryRunId = "dryrun-boundary",
            WorkflowRunId = "workflow-run-boundary",
            WorkflowStepId = "workflow-step-boundary",
            ControlledApplyPlanReferenceId = "plan-boundary",
            SourceApplyApprovalRequirementReferenceId = "source-approval-boundary",
            PatchProposalEvidencePackageReferenceId = "patch-evidence-boundary",
            ProjectReferenceId = "project-boundary",
            TargetReferenceId = "target-boundary",
            Status = ApplyDryRunRecordStatus.Stored,
            OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
            SafeSummary = "Boundary receipt only.",
            EvidenceReferences = [new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.ControlledApplyPlan, ReferenceId = "plan-boundary", SafeSummary = "Evidence only." }],
            GateReferences = [new ApplyDryRunGateReference { Kind = ApplyDryRunGateKind.ReviewRequired, ReferenceId = "gate-boundary", SafeSummary = "Review remains required." }],
            ValidationReferences = [new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.ValidationEvidence, ReferenceId = "validation-boundary", SafeSummary = "Validation evidence only." }],
            RollbackReferences = [new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.RollbackEvidence, ReferenceId = "rollback-boundary", SafeSummary = "Rollback evidence only." }],
            CreatedUtc = DateTimeOffset.UtcNow
        };

        Assert.IsTrue(record.IsStoreRecordOnly);
        Assert.IsFalse(record.IsDryRunPerformed);
        Assert.IsFalse(record.IsSourceApply);
        Assert.IsFalse(record.IsPatchApplication);
        Assert.IsFalse(record.IsApproval);
        Assert.IsFalse(record.IsApprovalSatisfied);
        Assert.IsFalse(record.CanPerformDryRun);
        Assert.IsFalse(record.CanApplySource);
        Assert.IsFalse(record.CanMutateFiles);
        Assert.IsFalse(record.CanReadSourceFiles);
        Assert.IsFalse(record.CanRunCommand);
        Assert.IsFalse(record.CanInvokeTool);
        Assert.IsFalse(record.CanRunValidation);
        Assert.IsFalse(record.CanRollback);
        Assert.IsFalse(record.CanSatisfyPolicy);
        Assert.IsFalse(record.CanTransitionWorkflow);
        Assert.IsFalse(record.CanPromoteMemory);
        Assert.IsFalse(record.CanActivateRetrieval);
    }

    [TestMethod]
    public void ApplyDryRunReasonVocabulary_PreservesReceiptNotActionBoundary()
    {
        var reasons = string.Join("\n", Enum.GetNames<ApplyDryRunReason>());

        CollectionAssert.Contains(Enum.GetNames<ApplyDryRunReason>(), nameof(ApplyDryRunReason.DryRunNotPerformed));
        CollectionAssert.Contains(Enum.GetNames<ApplyDryRunReason>(), nameof(ApplyDryRunReason.SourceNotApplied));
        CollectionAssert.Contains(Enum.GetNames<ApplyDryRunReason>(), nameof(ApplyDryRunReason.PatchNotApplied));
        CollectionAssert.Contains(Enum.GetNames<ApplyDryRunReason>(), nameof(ApplyDryRunReason.CommandNotRun));
        CollectionAssert.Contains(Enum.GetNames<ApplyDryRunReason>(), nameof(ApplyDryRunReason.ToolNotInvoked));

        AssertNoForbiddenTokens(
            reasons,
            "DryRunExecuted",
            "PatchApplied",
            "SourceApplied",
            "ApprovalGranted",
            "PolicySatisfied",
            "MemoryPromoted",
            "WorkflowTransitioned");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }
}

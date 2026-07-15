using IronDev.Core.Workspaces;

namespace IronDev.UnitTests;

[TestClass]
public sealed class WorkspaceCleanupRetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);

    [TestMethod] public void Active_workspace_is_never_eligible() => Assert.AreEqual(WorkspaceCleanupReason.ActiveWorkspace, Evaluate(Request() with { State = RetainedWorkspaceState.Active }).Reason);
    [TestMethod] public void Legal_hold_beats_elapsed_retention() => Assert.AreEqual(WorkspaceCleanupReason.LegalHold, Evaluate(Request() with { HasLegalHold = true }).Reason);
    [TestMethod] public void Required_receipt_must_be_archived_before_review() => Assert.AreEqual(WorkspaceCleanupReason.RequiredReceiptNotArchived, Evaluate(Request() with { HasRequiredReceipts = true, ReceiptDisposition = WorkspaceEvidenceDisposition.Live }).Reason);
    [TestMethod] public void Failed_run_evidence_must_remain_inspectable() => Assert.AreEqual(WorkspaceCleanupReason.FailedEvidenceNotArchived, Evaluate(Request() with { State = RetainedWorkspaceState.Failed, FailedRunEvidenceDisposition = WorkspaceEvidenceDisposition.Live }).Reason);
    [TestMethod] public void Manual_hold_blocks_elapsed_retention() => Assert.AreEqual(WorkspaceCleanupReason.ManualHold, Evaluate(Request() with { HasManualHold = true }).Reason);
    [TestMethod] public void Audit_hold_blocks_elapsed_retention() => Assert.AreEqual(WorkspaceCleanupReason.AuditHold, Evaluate(Request() with { HasAuditHold = true }).Reason);
    [TestMethod] public void Source_workspace_is_never_cleanup_eligible() => Assert.AreEqual(WorkspaceCleanupReason.NotDerivedWorkspace, Evaluate(Request() with { IsDerivedWorkspace = false }).Reason);

    [TestMethod]
    public void Derived_workspace_can_become_review_eligible_after_retention()
    {
        var result = Evaluate(Request());
        Assert.IsTrue(result.IsEligibleForGovernedCleanupReview);
        Assert.AreEqual(WorkspaceCleanupReason.EligibleByRetention, result.Reason);
        Assert.IsFalse(result.CanDeleteWorkspace);
        Assert.IsFalse(result.CreatesAuthority);
    }

    [TestMethod]
    public void Quota_does_not_bypass_retention_period()
    {
        var result = Evaluate(Request() with { LastActiveUtc = Now.AddDays(-1), CurrentRetainedBytes = 200, QuotaBytes = 100 });
        Assert.AreEqual(WorkspaceCleanupReason.RetentionPeriodNotElapsed, result.Reason);
    }

    [TestMethod]
    public void Quota_uses_total_retained_usage_only_after_retention()
    {
        var result = Evaluate(Request() with { WorkspaceBytes = 1, CurrentRetainedBytes = 200, QuotaBytes = 100 });
        Assert.AreEqual(WorkspaceCleanupReason.EligibleByQuota, result.Reason);
        Assert.IsFalse(result.CanDeleteWorkspace);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Missing_workspace_identity_fails_closed(string workspaceReferenceId) =>
        Assert.AreEqual(WorkspaceCleanupReason.InvalidRequest, Evaluate(Request() with { WorkspaceReferenceId = workspaceReferenceId }).Reason);

    [TestMethod]
    public void Unknown_state_and_invalid_numeric_policy_inputs_fail_closed()
    {
        Assert.AreEqual(WorkspaceCleanupReason.InvalidRequest, Evaluate(Request() with { State = RetainedWorkspaceState.Unknown }).Reason);
        Assert.AreEqual(WorkspaceCleanupReason.InvalidRequest, Evaluate(Request() with { ReceiptDisposition = (WorkspaceEvidenceDisposition)99 }).Reason);
        Assert.AreEqual(WorkspaceCleanupReason.InvalidRequest, Evaluate(Request() with { RetentionPeriod = TimeSpan.FromDays(-1) }).Reason);
        Assert.AreEqual(WorkspaceCleanupReason.InvalidRequest, Evaluate(Request() with { CurrentRetainedBytes = -1 }).Reason);
        Assert.AreEqual(WorkspaceCleanupReason.InvalidRequest, Evaluate(Request() with { QuotaBytes = -1 }).Reason);
    }

    [TestMethod]
    public void Null_request_is_rejected_and_timestamp_overflow_fails_closed()
    {
        Assert.ThrowsException<ArgumentNullException>(() => WorkspaceCleanupRetentionPolicy.Evaluate(null!, Now));
        Assert.AreEqual(
            WorkspaceCleanupReason.InvalidRequest,
            Evaluate(Request() with { LastActiveUtc = DateTimeOffset.MaxValue, RetentionPeriod = TimeSpan.FromTicks(1) }).Reason);
    }

    private static WorkspaceCleanupRetentionResult Evaluate(WorkspaceCleanupRetentionRequest request) => WorkspaceCleanupRetentionPolicy.Evaluate(request, Now);
    private static WorkspaceCleanupRetentionRequest Request() => new()
    {
        WorkspaceReferenceId = "workspace-1",
        State = RetainedWorkspaceState.Applied,
        IsDerivedWorkspace = true,
        LastActiveUtc = Now.AddDays(-31),
        RetentionPeriod = TimeSpan.FromDays(30),
        WorkspaceBytes = 10,
        CurrentRetainedBytes = 50,
        QuotaBytes = 100,
        ReceiptDisposition = WorkspaceEvidenceDisposition.Archived,
        FailedRunEvidenceDisposition = WorkspaceEvidenceDisposition.Archived
    };
}

using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class GovernanceDataRetentionRuleBoundaryTests
{
    [TestMethod] public void Result_IsRuleEvaluationOnly() => Assert.IsTrue(Result().IsRuleEvaluationOnly);
    [TestMethod] public void Result_IsNotCleanupExecution() => Assert.IsFalse(Result().IsCleanupExecution);
    [TestMethod] public void Result_IsNotDeletePermission() => Assert.IsFalse(Result().IsDeletePermission);
    [TestMethod] public void Result_IsNotPurgePermission() => Assert.IsFalse(Result().IsPurgePermission);
    [TestMethod] public void Result_IsNotArchivePermission() => Assert.IsFalse(Result().IsArchivePermission);
    [TestMethod] public void Result_IsNotRedactionPermission() => Assert.IsFalse(Result().IsRedactionPermission);
    [TestMethod] public void Result_IsNotLegalHoldOverride() => Assert.IsFalse(Result().IsLegalHoldOverride);
    [TestMethod] public void Result_CannotDeleteData() => Assert.IsFalse(Result().CanDeleteData);
    [TestMethod] public void Result_CannotPurgeData() => Assert.IsFalse(Result().CanPurgeData);
    [TestMethod] public void Result_CannotArchiveData() => Assert.IsFalse(Result().CanArchiveData);
    [TestMethod] public void Result_CannotRedactData() => Assert.IsFalse(Result().CanRedactData);
    [TestMethod] public void Result_CannotRunCleanup() => Assert.IsFalse(Result().CanRunCleanup);
    [TestMethod] public void Result_CannotScheduleCleanup() => Assert.IsFalse(Result().CanScheduleCleanup);
    [TestMethod] public void Result_CannotMutateSql() => Assert.IsFalse(Result().CanMutateSql);
    [TestMethod] public void Result_CannotBypassAuditHold() => Assert.IsFalse(Result().CanBypassAuditHold);
    [TestMethod] public void Result_CannotBypassLegalHold() => Assert.IsFalse(Result().CanBypassLegalHold);
    [TestMethod] public void CleanupRecommendation_IsReviewOnly() => Assert.IsTrue(Recommendation().IsReviewOnly);
    [TestMethod] public void CleanupRecommendation_IsNotDeleteCommand() => Assert.IsFalse(Recommendation().IsDeleteCommand);
    [TestMethod] public void CleanupRecommendation_IsNotPurgeCommand() => Assert.IsFalse(Recommendation().IsPurgeCommand);
    [TestMethod] public void CleanupRecommendation_IsNotArchiveCommand() => Assert.IsFalse(Recommendation().IsArchiveCommand);
    [TestMethod] public void CleanupRecommendation_IsNotRedactionCommand() => Assert.IsFalse(Recommendation().IsRedactionCommand);

    [TestMethod]
    public void LegalHoldAlwaysBlocksCleanupReview()
    {
        var result = Evaluate(Base() with { HasLegalHold = true });
        Assert.AreEqual(GovernanceDataRetentionClass.PreserveIndefinitely, result.RetentionClass);
        Assert.IsFalse(result.CleanupRecommendations.Any(recommendation => recommendation.Kind == GovernanceDataCleanupRecommendationKind.ReviewForFutureCleanup));
    }

    [TestMethod]
    public void AuditHoldAlwaysBlocksCleanupReview()
    {
        var result = Evaluate(Base() with { HasAuditHold = true });
        Assert.AreEqual(GovernanceDataRetentionClass.PreserveIndefinitely, result.RetentionClass);
        Assert.IsFalse(result.CleanupRecommendations.Any(recommendation => recommendation.Kind == GovernanceDataCleanupRecommendationKind.ReviewForFutureCleanup));
    }

    [TestMethod]
    public void GovernanceEventNeverCleanupEligible()
    {
        var result = Evaluate(Base() with { RecordKind = GovernanceDataRecordKind.GovernanceEvent });
        Assert.AreEqual(GovernanceDataRetentionClass.PreserveIndefinitely, result.RetentionClass);
        Assert.IsFalse(result.CleanupRecommendations.Any(recommendation => recommendation.Kind == GovernanceDataCleanupRecommendationKind.ReviewForFutureCleanup));
    }

    [TestMethod]
    public void AuthorityDecisionRecordsNeverCleanupEligible()
    {
        foreach (var kind in new[] { GovernanceDataRecordKind.ApprovalDecision, GovernanceDataRecordKind.PolicyDecisionEvent, GovernanceDataRecordKind.ToolGateDecision })
        {
            var result = Evaluate(Base() with { RecordKind = kind });
            Assert.AreEqual(GovernanceDataRetentionClass.PreserveIndefinitely, result.RetentionClass);
            Assert.IsFalse(result.CleanupRecommendations.Any(recommendation => recommendation.Kind == GovernanceDataCleanupRecommendationKind.ReviewForFutureCleanup));
        }
    }

    [TestMethod]
    public void BoundaryWarnings_RecordAllMaxims()
    {
        var text = string.Join("\n", Result().BoundaryWarnings);
        StringAssert.Contains(text, "Retention rule evaluation is not cleanup execution.");
        StringAssert.Contains(text, "Cleanup eligibility is not deletion permission.");
        StringAssert.Contains(text, "Cleanup recommendation is not cleanup approval.");
        StringAssert.Contains(text, "Expired retention window is not purge authority.");
        StringAssert.Contains(text, "Archive recommendation is not archive execution.");
        StringAssert.Contains(text, "Redaction recommendation is not redaction execution.");
        StringAssert.Contains(text, "Legal hold beats cleanup.");
        StringAssert.Contains(text, "Audit hold beats cleanup.");
    }

    private static GovernanceDataRetentionRuleResult Result() => Evaluate(Base());

    private static GovernanceDataCleanupRecommendation Recommendation() => Result().CleanupRecommendations.Single();

    private static GovernanceDataRetentionRuleResult Evaluate(GovernanceDataRetentionRuleRequest request) =>
        new GovernanceDataRetentionRuleService().Evaluate(request);

    private static GovernanceDataRetentionRuleRequest Base() =>
        new()
        {
            RecordReferenceId = "record-123",
            RecordKind = GovernanceDataRecordKind.BackendOperationalHealthReport,
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            CorrelationId = Guid.NewGuid().ToString("D"),
            CreatedUtc = DateTimeOffset.UtcNow.AddDays(-500)
        };
}

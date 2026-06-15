using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class GovernanceDataRetentionRuleTests
{
    private static readonly DateTimeOffset OldCreatedUtc = DateTimeOffset.UtcNow.AddDays(-500);
    private static readonly DateTimeOffset RecentCreatedUtc = DateTimeOffset.UtcNow.AddDays(-10);

    [TestMethod]
    public void Evaluate_GovernanceEvent_PreserveIndefinitely() =>
        AssertResult(Kind(GovernanceDataRecordKind.GovernanceEvent), GovernanceDataRetentionClass.PreserveIndefinitely, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_ApprovalDecision_PreserveIndefinitely() =>
        AssertResult(Kind(GovernanceDataRecordKind.ApprovalDecision), GovernanceDataRetentionClass.PreserveIndefinitely, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_PolicyDecisionEvent_PreserveIndefinitely() =>
        AssertResult(Kind(GovernanceDataRecordKind.PolicyDecisionEvent), GovernanceDataRetentionClass.PreserveIndefinitely, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_ToolGateDecision_PreserveIndefinitely() =>
        AssertResult(Kind(GovernanceDataRecordKind.ToolGateDecision), GovernanceDataRetentionClass.PreserveIndefinitely, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_WorkflowRun_PreserveForAuditWindow() =>
        AssertResult(Kind(GovernanceDataRecordKind.WorkflowRun), GovernanceDataRetentionClass.PreserveForAuditWindow, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_OpenWorkflowReference_PreservationRequired() =>
        AssertResult(Base() with { HasOpenWorkflowReference = true }, GovernanceDataRetentionClass.PreserveWhileReferenced, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_OpenApprovalReference_PreservationRequired() =>
        AssertResult(Base() with { HasOpenApprovalReference = true }, GovernanceDataRetentionClass.PreserveWhileReferenced, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_OpenPolicyReference_PreservationRequired() =>
        AssertResult(Base() with { HasOpenPolicyReference = true }, GovernanceDataRetentionClass.PreserveWhileReferenced, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_OpenToolGateReference_PreservationRequired() =>
        AssertResult(Base() with { HasOpenToolGateReference = true }, GovernanceDataRetentionClass.PreserveWhileReferenced, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_OpenMemoryProposalReference_PreservationRequired() =>
        AssertResult(Base() with { HasOpenMemoryProposalReference = true }, GovernanceDataRetentionClass.PreserveWhileReferenced, GovernanceDataRetentionRuleStatus.PreservationRequired);

    [TestMethod]
    public void Evaluate_LegalHold_PreservationRequired()
    {
        var result = Evaluate(Base() with { HasLegalHold = true });
        Assert.AreEqual(GovernanceDataRetentionClass.PreserveIndefinitely, result.RetentionClass);
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.PreservationRequired, result.Status);
        Assert.IsTrue(result.PreservationReasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.LegalHoldPresent));
    }

    [TestMethod]
    public void Evaluate_AuditHold_PreservationRequired()
    {
        var result = Evaluate(Base() with { HasAuditHold = true });
        Assert.AreEqual(GovernanceDataRetentionClass.PreserveIndefinitely, result.RetentionClass);
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.PreservationRequired, result.Status);
        Assert.IsTrue(result.PreservationReasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.AuditHoldPresent));
    }

    [TestMethod]
    public void Evaluate_PrivatePayloadRisk_HumanReviewRequired()
    {
        var result = Evaluate(Base() with { ContainsPrivatePayloadRisk = true });
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.HumanReviewRequired, result.Status);
        Assert.IsTrue(result.PreservationReasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.PrivatePayloadRiskRequiresHumanReview));
    }

    [TestMethod]
    public void Evaluate_UnknownRecordKind_HumanReviewRequired()
    {
        var result = Evaluate(Kind(GovernanceDataRecordKind.Unknown));
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.HumanReviewRequired, result.Status);
        Assert.IsTrue(result.PreservationReasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.UnknownRecordKindRequiresHumanReview));
    }

    [TestMethod]
    public void Evaluate_MissingCreatedUtc_HumanReviewRequired()
    {
        var result = Evaluate(Base() with { CreatedUtc = null });
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.HumanReviewRequired, result.Status);
        Assert.IsTrue(result.PreservationReasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.MissingCreatedUtcRequiresHumanReview));
    }

    [TestMethod]
    public void Evaluate_UnreferencedOldReport_EligibleForHumanCleanupReview()
    {
        var result = Evaluate(Kind(GovernanceDataRecordKind.BackendOperationalHealthReport));
        Assert.AreEqual(GovernanceDataRetentionClass.EligibleForHumanCleanupReview, result.RetentionClass);
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.RuleEvaluationAvailable, result.Status);
        Assert.IsTrue(result.CleanupRecommendations.Any(recommendation => recommendation.Kind == GovernanceDataCleanupRecommendationKind.ReviewForFutureCleanup));
    }

    [TestMethod]
    public void Evaluate_RecentReport_PreserveForAuditWindow()
    {
        var result = Evaluate(Kind(GovernanceDataRecordKind.BackendOperationalHealthReport) with { CreatedUtc = RecentCreatedUtc });
        Assert.AreEqual(GovernanceDataRetentionClass.PreserveForAuditWindow, result.RetentionClass);
        Assert.AreEqual(GovernanceDataRetentionRuleStatus.PreservationRequired, result.Status);
        Assert.IsTrue(result.PreservationReasons.Any(reason => reason.Kind == GovernanceDataPreservationReasonKind.MinimumRetentionWindowNotElapsed));
    }

    [TestMethod] public void Evaluate_CleanupReviewStillCannotDelete() => Assert.IsFalse(OldReport().CanDeleteData);
    [TestMethod] public void Evaluate_CleanupReviewStillCannotPurge() => Assert.IsFalse(OldReport().CanPurgeData);
    [TestMethod] public void Evaluate_CleanupReviewStillCannotArchive() => Assert.IsFalse(OldReport().CanArchiveData);
    [TestMethod] public void Evaluate_CleanupReviewStillCannotRedact() => Assert.IsFalse(OldReport().CanRedactData);

    [TestMethod]
    public void Evaluate_AllResultsAreRuleEvaluationOnly()
    {
        foreach (var result in RepresentativeResults())
            Assert.IsTrue(result.IsRuleEvaluationOnly);
    }

    [TestMethod]
    public void Evaluate_AllResultsCannotMutateSql()
    {
        foreach (var result in RepresentativeResults())
            Assert.IsFalse(result.CanMutateSql);
    }

    [TestMethod]
    public void Evaluate_AllResultsCannotBypassLegalHold()
    {
        foreach (var result in RepresentativeResults())
            Assert.IsFalse(result.CanBypassLegalHold);
    }

    [TestMethod]
    public void Evaluate_AllResultsCannotBypassAuditHold()
    {
        foreach (var result in RepresentativeResults())
            Assert.IsFalse(result.CanBypassAuditHold);
    }

    [TestMethod]
    public void Evaluate_RedactsUnsafeRecordReference()
    {
        var result = Evaluate(Base() with { RecordReferenceId = "rawPrompt leaked" });
        Assert.AreEqual(GovernanceDataRetentionRuleService.RedactedUnsafeText, result.RecordReferenceId);
        Assert.IsFalse(string.Join("\n", result.SafeSummaryLines).Contains("rawPrompt", StringComparison.OrdinalIgnoreCase));
    }

    private static GovernanceDataRetentionRuleResult OldReport() => Evaluate(Kind(GovernanceDataRecordKind.BackendOperationalHealthReport));

    private static IReadOnlyList<GovernanceDataRetentionRuleResult> RepresentativeResults() =>
    [
        Evaluate(Kind(GovernanceDataRecordKind.GovernanceEvent)),
        Evaluate(Kind(GovernanceDataRecordKind.BackendOperationalHealthReport)),
        Evaluate(Base() with { HasLegalHold = true }),
        Evaluate(Base() with { ContainsPrivatePayloadRisk = true }),
        Evaluate(Kind(GovernanceDataRecordKind.Unknown))
    ];

    private static void AssertResult(
        GovernanceDataRetentionRuleRequest request,
        GovernanceDataRetentionClass expectedClass,
        GovernanceDataRetentionRuleStatus expectedStatus)
    {
        var result = Evaluate(request);
        Assert.AreEqual(expectedClass, result.RetentionClass);
        Assert.AreEqual(expectedStatus, result.Status);
    }

    private static GovernanceDataRetentionRuleResult Evaluate(GovernanceDataRetentionRuleRequest request) =>
        new GovernanceDataRetentionRuleService().Evaluate(request);

    private static GovernanceDataRetentionRuleRequest Kind(GovernanceDataRecordKind kind) =>
        Base() with { RecordKind = kind };

    private static GovernanceDataRetentionRuleRequest Base() =>
        new()
        {
            RecordReferenceId = "record-123",
            RecordKind = GovernanceDataRecordKind.BackendOperationalHealthReport,
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            CorrelationId = Guid.NewGuid().ToString("D"),
            CreatedUtc = OldCreatedUtc
        };
}

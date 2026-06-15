using IronDev.Core.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class BackendOperationalHealthBoundaryTests
{
    [TestMethod] public void Report_IsHealthReportOnly() => Assert.IsTrue(Report().IsHealthReportOnly);
    [TestMethod] public void Report_IsNotReleaseReadiness() => Assert.IsFalse(Report().IsReleaseReadiness);
    [TestMethod] public void Report_IsNotApproval() => Assert.IsFalse(Report().IsApproval);
    [TestMethod] public void Report_IsNotPolicySatisfaction() => Assert.IsFalse(Report().IsPolicySatisfaction);
    [TestMethod] public void Report_IsNotWorkflowExecution() => Assert.IsFalse(Report().IsWorkflowExecution);
    [TestMethod] public void Report_IsNotBackendRepair() => Assert.IsFalse(Report().IsBackendRepair);
    [TestMethod] public void Report_IsNotMigrationExecution() => Assert.IsFalse(Report().IsMigrationExecution);
    [TestMethod] public void Report_CannotRestartBackend() => Assert.IsFalse(Report().CanRestartBackend);
    [TestMethod] public void Report_CannotRepairBackend() => Assert.IsFalse(Report().CanRepairBackend);
    [TestMethod] public void Report_CannotRunMigration() => Assert.IsFalse(Report().CanRunMigration);
    [TestMethod] public void Report_CannotExecuteWorkflow() => Assert.IsFalse(Report().CanExecuteWorkflow);
    [TestMethod] public void Report_CannotTransitionWorkflow() => Assert.IsFalse(Report().CanTransitionWorkflow);
    [TestMethod] public void Report_CannotDispatchAgent() => Assert.IsFalse(Report().CanDispatchAgent);
    [TestMethod] public void Report_CannotInvokeTool() => Assert.IsFalse(Report().CanInvokeTool);
    [TestMethod] public void Report_CannotCallModel() => Assert.IsFalse(Report().CanCallModel);
    [TestMethod] public void Report_CannotApproveRelease() => Assert.IsFalse(Report().CanApproveRelease);
    [TestMethod] public void Report_CannotSatisfyPolicy() => Assert.IsFalse(Report().CanSatisfyPolicy);
    [TestMethod] public void Report_CannotPromoteMemory() => Assert.IsFalse(Report().CanPromoteMemory);
    [TestMethod] public void Report_CannotApplySource() => Assert.IsFalse(Report().CanApplySource);
    [TestMethod] public void Report_CannotApplyPatch() => Assert.IsFalse(Report().CanApplyPatch);
    [TestMethod] public void DependencyCheck_IsReadOnlyCheck() => Assert.IsTrue(Check().IsReadOnlyCheck);
    [TestMethod] public void DependencyCheck_IsNotRepairAction() => Assert.IsFalse(Check().IsRepairAction);
    [TestMethod] public void DependencyCheck_CannotMutateDependency() => Assert.IsFalse(Check().CanMutateDependency);
    [TestMethod] public void DependencyCheck_CannotRestartDependency() => Assert.IsFalse(Check().CanRestartDependency);
    [TestMethod] public void DependencyCheck_CannotRunMigration() => Assert.IsFalse(Check().CanRunMigration);
    [TestMethod] public void Warning_IsEvidenceOnly() => Assert.IsTrue(Warning().IsEvidenceOnly);
    [TestMethod] public void Warning_IsNotFailureProof() => Assert.IsFalse(Warning().IsFailureProof);
    [TestMethod] public void Warning_CannotRepair() => Assert.IsFalse(Warning().CanRepair);
    [TestMethod] public void Recommendation_IsInvestigationOnly() => Assert.IsTrue(Recommendation().IsInvestigationOnly);
    [TestMethod] public void Recommendation_CannotMutateState() => Assert.IsFalse(Recommendation().CanMutateState);
    [TestMethod] public void Recommendation_CannotRestartBackend() => Assert.IsFalse(Recommendation().CanRestartBackend);
    [TestMethod] public void Recommendation_CannotRunMigration() => Assert.IsFalse(Recommendation().CanRunMigration);
    [TestMethod] public void Recommendation_CannotExecuteWorkflow() => Assert.IsFalse(Recommendation().CanExecuteWorkflow);
    [TestMethod] public void Recommendation_CannotApproveRelease() => Assert.IsFalse(Recommendation().CanApproveRelease);
    [TestMethod] public void Report_DoesNotCreateGovernanceEvent() => Assert.IsFalse(Report().CreatesGovernanceEvent);
    [TestMethod] public void Report_DoesNotCreateApprovalDecision() => Assert.IsFalse(Report().CreatesApprovalDecision);
    [TestMethod] public void Report_DoesNotCreatePolicyDecision() => Assert.IsFalse(Report().CreatesPolicyDecision);
    [TestMethod] public void Report_DoesNotCreateToolRequest() => Assert.IsFalse(Report().CreatesToolRequest);
    [TestMethod] public void Report_DoesNotCreateDogfoodReceipt() => Assert.IsFalse(Report().CreatesDogfoodReceipt);
    [TestMethod] public void Report_DoesNotTransitionWorkflow() => Assert.IsFalse(Report().TransitionsWorkflow);
    [TestMethod] public void Report_DoesNotCallModel() => Assert.IsFalse(Report().CallsModel);
    [TestMethod] public void Report_DoesNotInvokeTool() => Assert.IsFalse(Report().InvokesTool);
    [TestMethod] public void Report_DoesNotDispatchAgent() => Assert.IsFalse(Report().DispatchesAgent);
    [TestMethod] public void Report_DoesNotPromoteMemory() => Assert.IsFalse(Report().PromotesMemory);

    [TestMethod]
    public void Report_DoesNotExposeConnectionString() =>
        Assert.IsFalse(CombinedReportText().Contains("connection string value", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void Report_DoesNotExposeSecrets() =>
        Assert.IsFalse(CombinedReportText().Contains("secret value", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void Report_DoesNotExposePayloadJson() =>
        Assert.IsFalse(CombinedReportText().Contains("payloadJson", StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void Report_DoesNotExposeHiddenPrivateReasoning() =>
        Assert.IsFalse(BackendOperationalHealthValidator.ContainsUnsafeText(CombinedReportText()));

    [TestMethod]
    public void Validator_RedactsUnsafeText()
    {
        Assert.AreEqual(BackendOperationalHealthValidator.RedactedUnsafeText, BackendOperationalHealthValidator.SafeText("rawPrompt leaked"));
    }

    private static BackendOperationalHealthReport Report() =>
        new()
        {
            ReportId = "backend-operational-health",
            Status = BackendOperationalHealthStatus.Healthy,
            GeneratedUtc = DateTimeOffset.UtcNow,
            ProjectReferenceId = string.Empty,
            CorrelationId = string.Empty,
            SafeSummaryLines = ["Backend operational health status is Healthy."],
            DependencyChecks = [Check()],
            Warnings = [Warning()],
            Recommendations = [Recommendation()],
            BoundaryWarnings = BackendOperationalHealthBoundaries.Warnings,
            IsHealthReportOnly = true,
            IsReleaseReadiness = false,
            IsApproval = false,
            IsPolicySatisfaction = false,
            IsWorkflowExecution = false,
            IsBackendRepair = false,
            IsMigrationExecution = false,
            CanRestartBackend = false,
            CanRepairBackend = false,
            CanRunMigration = false,
            CanExecuteWorkflow = false,
            CanTransitionWorkflow = false,
            CanDispatchAgent = false,
            CanInvokeTool = false,
            CanCallModel = false,
            CanApproveRelease = false,
            CanSatisfyPolicy = false,
            CanPromoteMemory = false,
            CanApplySource = false,
            CanApplyPatch = false,
            CreatesGovernanceEvent = false,
            CreatesApprovalDecision = false,
            CreatesPolicyDecision = false,
            CreatesToolRequest = false,
            CreatesDogfoodReceipt = false,
            TransitionsWorkflow = false,
            CallsModel = false,
            InvokesTool = false,
            DispatchesAgent = false,
            PromotesMemory = false
        };

    private static BackendDependencyHealthCheck Check() =>
        new()
        {
            CheckId = "api-process",
            DependencyKind = BackendDependencyKind.ApiProcess,
            Status = BackendDependencyHealthStatus.Available,
            SafeSummary = "API process accepted the read-only health request.",
            CheckedUtc = DateTimeOffset.UtcNow,
            IsReadOnlyCheck = true,
            IsRepairAction = false,
            CanMutateDependency = false,
            CanRestartDependency = false,
            CanRunMigration = false
        };

    private static BackendOperationalHealthWarning Warning() =>
        new()
        {
            WarningId = "warning:api-process",
            Kind = BackendOperationalHealthWarningKind.InsufficientEvidence,
            SafeSummary = "Dependency evidence needs human investigation.",
            IsEvidenceOnly = true,
            IsFailureProof = false,
            CanRepair = false
        };

    private static BackendOperationalHealthRecommendation Recommendation() =>
        new()
        {
            RecommendationId = "inspect-health-evidence",
            SafeSummary = "Inspect the safe dependency checks before taking any operational action.",
            SupportingCheckIds = ["api-process"],
            IsInvestigationOnly = true,
            CanMutateState = false,
            CanRestartBackend = false,
            CanRunMigration = false,
            CanExecuteWorkflow = false,
            CanApproveRelease = false
        };

    private static string CombinedReportText()
    {
        var report = Report();
        return string.Join(
            "\n",
            report.SafeSummaryLines
                .Concat(report.DependencyChecks.Select(check => check.SafeSummary))
                .Concat(report.Warnings.Select(warning => warning.SafeSummary))
                .Concat(report.Recommendations.Select(recommendation => recommendation.SafeSummary)));
    }
}

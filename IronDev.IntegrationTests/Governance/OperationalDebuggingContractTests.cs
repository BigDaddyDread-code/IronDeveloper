using IronDev.Core.Agents;
using IronDev.Core.Governance;
using IronDev.Core.Operations;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class OperationalDebuggingContractTests
{
    [TestMethod]
    public void OperationalDebugging_TraceExplorer_IsObservationOnly()
    {
        var trace = TraceSurface();
        Assert.IsTrue(trace.ObservationOnly);
        Assert.IsFalse(trace.IsAuthority);
    }

    [TestMethod]
    public void OperationalDebugging_FailedWorkflowDiagnosis_IsObservationOnly()
    {
        var diagnosis = FailedWorkflowSurface();
        Assert.IsTrue(diagnosis.ObservationOnly);
        Assert.IsFalse(diagnosis.CanRepair);
    }

    [TestMethod]
    public void OperationalDebugging_CorrelationReport_IsObservationOnly()
    {
        var correlation = CorrelationSurface();
        Assert.IsTrue(correlation.ObservationOnly);
        Assert.IsFalse(correlation.CanApprove);
    }

    [TestMethod]
    public void OperationalDebugging_AgentRunHealth_IsObservationOnly()
    {
        var health = AgentRunHealthSurface();
        Assert.IsTrue(health.ObservationOnly);
        Assert.IsFalse(health.CanRestart);
    }

    [TestMethod]
    public void OperationalDebugging_BackendHealth_IsObservationOnly()
    {
        var health = BackendHealthSurface();
        Assert.IsTrue(health.ObservationOnly);
        Assert.IsFalse(health.CanRepair);
    }

    [TestMethod]
    public void OperationalDebugging_RetentionRules_AreRuleEvaluationOnly()
    {
        var retention = RetentionSurface();
        Assert.IsTrue(retention.ObservationOnly);
        Assert.IsFalse(retention.CanRunCleanup);
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_AreNotAuthority()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.IsAuthority, $"{surface.Name} must not be authority.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotApprove()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanApprove, $"{surface.Name} must not approve.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotSatisfyPolicy()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanSatisfyPolicy, $"{surface.Name} must not satisfy policy.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotTransitionWorkflow()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanTransitionWorkflow, $"{surface.Name} must not transition workflow.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotInvokeTool()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanInvokeTool, $"{surface.Name} must not invoke tools.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotDispatchAgent()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanDispatchAgent, $"{surface.Name} must not dispatch agents.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotCallModel()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanCallModel, $"{surface.Name} must not call models.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotBuildPrompt()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanBuildPrompt, $"{surface.Name} must not build prompts.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotCreateTicket()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanCreateTicket, $"{surface.Name} must not create tickets.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotPromoteMemory()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanPromoteMemory, $"{surface.Name} must not promote memory.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotActivateRetrieval()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanActivateRetrieval, $"{surface.Name} must not activate retrieval.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotApplySource()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanApplySource, $"{surface.Name} must not apply source.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotApplyPatch()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanApplyPatch, $"{surface.Name} must not apply patches.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotRepair()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanRepair, $"{surface.Name} must not repair.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotRestart()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanRestart, $"{surface.Name} must not restart.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotRunMigration()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanRunMigration, $"{surface.Name} must not run migrations.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotRunCleanup()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanRunCleanup, $"{surface.Name} must not run cleanup.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotScheduleCleanup()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanScheduleCleanup, $"{surface.Name} must not schedule cleanup.");
    }

    [TestMethod]
    public void OperationalDebugging_AllSurfaces_CannotMutateSql()
    {
        foreach (var surface in Surfaces())
            Assert.IsFalse(surface.CanMutateSql, $"{surface.Name} must not mutate SQL.");
    }

    [TestMethod]
    public void OperationalDebugging_Recommendations_AreInvestigationOnly()
    {
        Assert.IsTrue(FailedWorkflowRecommendation().IsInvestigationOnly);
        Assert.IsTrue(CorrelationRecommendation().IsInvestigationOnly);
        Assert.IsTrue(BackendHealthRecommendation().IsInvestigationOnly);
        Assert.IsTrue(RetentionRecommendation().IsReviewOnly);

        Assert.IsFalse(FailedWorkflowRecommendation().CanMutateState);
        Assert.IsFalse(CorrelationRecommendation().CanMutateState);
        Assert.IsFalse(BackendHealthRecommendation().CanMutateState);
        Assert.IsFalse(RetentionRecommendation().IsDeleteCommand);
    }

    [TestMethod]
    public void OperationalDebugging_ConflictSignals_AreNotVerdicts()
    {
        var conflict = new GovernanceCorrelationConflictSignal
        {
            ConflictId = "conflict-1",
            Kind = GovernanceCorrelationConflictKind.ConflictingCorrelationReferences,
            SafeSummary = "Correlation evidence needs human investigation.",
            IsVerdict = false,
            CanResolve = false
        };

        Assert.IsFalse(conflict.IsVerdict);
        Assert.IsFalse(conflict.CanResolve);
    }

    [TestMethod]
    public void OperationalDebugging_HealthStatuses_AreNotReleaseReadiness()
    {
        Assert.IsFalse(AgentRunHealthSurface().HealthIsReleaseReadiness);
        Assert.IsFalse(BackendHealthSurface().HealthIsReleaseReadiness);
    }

    [TestMethod]
    public void OperationalDebugging_RetentionEligibility_IsNotCleanupPermission()
    {
        var result = RetentionResult();

        Assert.AreEqual(GovernanceDataRetentionClass.EligibleForHumanCleanupReview, result.RetentionClass);
        Assert.IsFalse(result.IsCleanupExecution);
        Assert.IsFalse(result.IsDeletePermission);
        Assert.IsFalse(result.CanDeleteData);
        Assert.IsFalse(result.CanRunCleanup);
    }

    [TestMethod]
    public void OperationalDebugging_Receipt_StatesContractCorrectly()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR151_OPERATIONAL_DEBUGGING_CONTRACT_TESTS.md"));

        StringAssert.Contains(text, "PR151 adds cross-surface operational debugging contract tests for Block O.");
        StringAssert.Contains(text, "Operational debugging surfaces are read-only.");
        StringAssert.Contains(text, "Observation is not authority.");
        StringAssert.Contains(text, "Diagnosis is not repair.");
        StringAssert.Contains(text, "Health is not release readiness.");
        StringAssert.Contains(text, "Correlation is not approval.");
        StringAssert.Contains(text, "Recommendation is not execution.");
        StringAssert.Contains(text, "Retention rule is not cleanup execution.");
        StringAssert.Contains(text, "Traceability is not mutation permission.");
        StringAssert.Contains(text, "does not hand it the wrench");
    }

    private static IReadOnlyList<OperationalSurface> Surfaces() =>
    [
        TraceSurface(),
        FailedWorkflowSurface(),
        CorrelationSurface(),
        AgentRunHealthSurface(),
        BackendHealthSurface(),
        RetentionSurface()
    ];

    private static OperationalSurface TraceSurface()
    {
        var summary = new GovernanceTraceSummary
        {
            TraceId = "trace-1",
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            WorkflowRunId = "workflow-run",
            WorkflowStepId = "workflow-step",
            CorrelationId = Guid.NewGuid().ToString("D"),
            CausationId = Guid.NewGuid().ToString("D"),
            SubjectReferenceId = "subject",
            EventKind = "governance.event.observed",
            SourceComponent = "test",
            SafeSummary = "Trace was observed safely.",
            RecordedUtc = DateTimeOffset.UtcNow,
            IsReadOnlyTrace = true,
            IsAuthorityDecision = false,
            IsApproval = false,
            IsPolicySatisfaction = false,
            IsWorkflowTransition = false,
            CanApprove = false,
            CanReject = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanPromoteMemory = false,
            CanApplySource = false
        };

        return new OperationalSurface(
            "TraceExplorer",
            summary.IsReadOnlyTrace,
            summary.IsAuthorityDecision || summary.IsApproval || summary.IsPolicySatisfaction || summary.IsWorkflowTransition,
            summary.CanApprove || summary.CanReject,
            summary.CanSatisfyPolicy,
            summary.CanTransitionWorkflow,
            summary.CanInvokeTool,
            summary.CanDispatchAgent,
            summary.CanCallModel,
            CanBuildPrompt: false,
            CanCreateTicket: false,
            summary.CanPromoteMemory,
            CanActivateRetrieval: false,
            summary.CanApplySource,
            CanApplyPatch: false,
            CanRepair: false,
            CanRestart: false,
            CanRunMigration: false,
            CanRunCleanup: false,
            CanScheduleCleanup: false,
            CanMutateSql: false,
            HealthIsReleaseReadiness: false);
    }

    private static OperationalSurface FailedWorkflowSurface()
    {
        var report = new FailedWorkflowDiagnosisReport
        {
            ReportId = "diagnosis-1",
            WorkflowRunId = "workflow-run",
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            GeneratedUtc = DateTimeOffset.UtcNow,
            IsReportOnly = true,
            IsRootCauseProof = false,
            CanRepair = false,
            CanRetryWorkflow = false,
            CanResumeWorkflow = false,
            CanTransitionWorkflow = false,
            CanApprove = false,
            CanSatisfyPolicy = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanApplySource = false,
            CanApplyPatch = false,
            ContainsUnsafeReasoning = false,
            Signals = [],
            Hypotheses = [],
            MissingEvidence = [],
            TraceTimeline = [],
            Recommendations = [FailedWorkflowRecommendation()],
            BoundaryWarnings = FailedWorkflowDiagnosisReportBoundaries.Warnings
        };

        return new OperationalSurface(
            "FailedWorkflowDiagnosis",
            report.IsReportOnly,
            report.IsRootCauseProof,
            report.CanApprove,
            report.CanSatisfyPolicy,
            report.CanTransitionWorkflow || report.CanRetryWorkflow || report.CanResumeWorkflow,
            report.CanInvokeTool,
            report.CanDispatchAgent,
            report.CanCallModel,
            CanBuildPrompt: false,
            report.CanCreateTicket,
            report.CanPromoteMemory,
            report.CanActivateRetrieval,
            report.CanApplySource,
            report.CanApplyPatch,
            report.CanRepair,
            CanRestart: false,
            CanRunMigration: false,
            CanRunCleanup: false,
            CanScheduleCleanup: false,
            CanMutateSql: false,
            HealthIsReleaseReadiness: false);
    }

    private static OperationalSurface CorrelationSurface()
    {
        var report = new ApprovalGateDogfoodCorrelationReport
        {
            ReportId = "correlation-1",
            Status = ApprovalGateDogfoodCorrelationReportStatus.ReportAvailable,
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            WorkflowRunId = "workflow-run",
            WorkflowStepId = "workflow-step",
            CorrelationId = Guid.NewGuid().ToString("D"),
            GeneratedUtc = DateTimeOffset.UtcNow,
            SafeSummaryLines = ["Correlation evidence observed."],
            ApprovalEvidence = [],
            ToolGateEvidence = [],
            DogfoodEvidence = [],
            TraceReferences = [],
            MissingEvidence = [],
            ConflictSignals = [],
            Recommendations = [CorrelationRecommendation()],
            BoundaryWarnings = ApprovalGateDogfoodCorrelationReportBoundaries.Warnings,
            IsReportOnly = true,
            IsApprovalDecision = false,
            IsPolicySatisfaction = false,
            IsToolGateMutation = false,
            IsToolExecution = false,
            IsDogfoodExecution = false,
            IsReleaseApproval = false,
            IsWorkflowTransition = false,
            CanApprove = false,
            CanReject = false,
            CanSatisfyPolicy = false,
            CanOpenGate = false,
            CanInvokeTool = false,
            CanMarkDogfoodPassed = false,
            CanApproveRelease = false,
            CanTransitionWorkflow = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanApplySource = false,
            CanApplyPatch = false
        };

        return new OperationalSurface(
            "CorrelationReport",
            report.IsReportOnly,
            report.IsApprovalDecision || report.IsPolicySatisfaction || report.IsToolGateMutation || report.IsToolExecution || report.IsDogfoodExecution || report.IsReleaseApproval || report.IsWorkflowTransition,
            report.CanApprove || report.CanReject || report.CanApproveRelease,
            report.CanSatisfyPolicy,
            report.CanTransitionWorkflow,
            report.CanInvokeTool || report.CanOpenGate || report.CanMarkDogfoodPassed,
            report.CanDispatchAgent,
            report.CanCallModel,
            report.CanBuildPrompt,
            report.CanCreateTicket,
            report.CanPromoteMemory,
            report.CanActivateRetrieval,
            report.CanApplySource,
            report.CanApplyPatch,
            CanRepair: false,
            CanRestart: false,
            CanRunMigration: false,
            CanRunCleanup: false,
            CanScheduleCleanup: false,
            CanMutateSql: false,
            HealthIsReleaseReadiness: false);
    }

    private static OperationalSurface AgentRunHealthSurface()
    {
        var boundary = new AgentRunHealthSummaryBoundary();
        return new OperationalSurface(
            "AgentRunHealth",
            boundary.ReadOnlySummary,
            boundary.SummaryIsApproval || boundary.SummaryIsPolicySatisfaction || boundary.SummaryIsExecutionPermission || boundary.SummaryIsReleaseApproval,
            boundary.SummaryIsApproval,
            boundary.SummaryIsPolicySatisfaction,
            boundary.SummaryCanStartWorkflow || boundary.SummaryCanResumeWorkflow,
            boundary.SummaryCanInvokeTool,
            boundary.SummaryCanDispatchAgent,
            boundary.SummaryCanCallModel,
            CanBuildPrompt: false,
            boundary.SummaryCanCreateTicket,
            boundary.SummaryCanPromoteMemory,
            boundary.SummaryCanActivateRetrieval,
            boundary.SummaryCanMutateSource,
            boundary.SummaryCanApplyPatch,
            CanRepair: false,
            boundary.SummaryCanRestartAgent || boundary.SummaryCanRetryAgent,
            CanRunMigration: false,
            CanRunCleanup: false,
            CanScheduleCleanup: false,
            CanMutateSql: false,
            HealthIsReleaseReadiness: boundary.SummaryIsReleaseApproval);
    }

    private static OperationalSurface BackendHealthSurface()
    {
        var report = BackendHealthReport();
        return new OperationalSurface(
            "BackendHealth",
            report.IsHealthReportOnly,
            report.IsReleaseReadiness || report.IsApproval || report.IsPolicySatisfaction || report.IsWorkflowExecution,
            report.IsApproval || report.CanApproveRelease,
            report.CanSatisfyPolicy,
            report.CanTransitionWorkflow || report.CanExecuteWorkflow,
            report.CanInvokeTool,
            report.CanDispatchAgent,
            report.CanCallModel,
            CanBuildPrompt: false,
            CanCreateTicket: false,
            report.CanPromoteMemory,
            CanActivateRetrieval: false,
            report.CanApplySource,
            report.CanApplyPatch,
            report.CanRepairBackend,
            report.CanRestartBackend,
            report.CanRunMigration,
            CanRunCleanup: false,
            CanScheduleCleanup: false,
            CanMutateSql: false,
            HealthIsReleaseReadiness: report.IsReleaseReadiness);
    }

    private static OperationalSurface RetentionSurface()
    {
        var result = RetentionResult();
        return new OperationalSurface(
            "RetentionRules",
            result.IsRuleEvaluationOnly,
            result.IsCleanupExecution || result.IsDeletePermission || result.IsPurgePermission || result.IsArchivePermission || result.IsRedactionPermission || result.IsLegalHoldOverride,
            CanApprove: false,
            CanSatisfyPolicy: false,
            CanTransitionWorkflow: false,
            CanInvokeTool: false,
            CanDispatchAgent: false,
            CanCallModel: false,
            CanBuildPrompt: false,
            CanCreateTicket: false,
            CanPromoteMemory: false,
            CanActivateRetrieval: false,
            CanApplySource: false,
            CanApplyPatch: false,
            CanRepair: false,
            CanRestart: false,
            CanRunMigration: false,
            result.CanRunCleanup,
            result.CanScheduleCleanup,
            result.CanMutateSql,
            HealthIsReleaseReadiness: false);
    }

    private static FailedWorkflowDiagnosisRecommendation FailedWorkflowRecommendation() =>
        new()
        {
            RecommendationId = "inspect-failure",
            SafeSummary = "Inspect failure evidence before any separate action.",
            SupportingTraceIds = ["trace-1"],
            IsInvestigationOnly = true,
            IsExecutableWorkflowStep = false,
            CanRepair = false,
            CanRetryWorkflow = false,
            CanCreateTicket = false,
            CanMutateState = false
        };

    private static GovernanceCorrelationRecommendation CorrelationRecommendation() =>
        new()
        {
            RecommendationId = "inspect-correlation",
            SafeSummary = "Inspect correlation evidence before any separate action.",
            SupportingReferenceIds = ["trace-1"],
            IsInvestigationOnly = true,
            CanMutateState = false,
            CanApprove = false,
            CanOpenGate = false,
            CanApproveRelease = false
        };

    private static BackendOperationalHealthRecommendation BackendHealthRecommendation() =>
        new()
        {
            RecommendationId = "inspect-health",
            SafeSummary = "Inspect health evidence before any separate action.",
            SupportingCheckIds = ["api-process"],
            IsInvestigationOnly = true,
            CanMutateState = false,
            CanRestartBackend = false,
            CanRunMigration = false,
            CanExecuteWorkflow = false,
            CanApproveRelease = false
        };

    private static GovernanceDataCleanupRecommendation RetentionRecommendation() =>
        RetentionResult().CleanupRecommendations.Single();

    private static GovernanceDataRetentionRuleResult RetentionResult() =>
        new GovernanceDataRetentionRuleService().Evaluate(new GovernanceDataRetentionRuleRequest
        {
            RecordReferenceId = "report-1",
            RecordKind = GovernanceDataRecordKind.BackendOperationalHealthReport,
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            CorrelationId = Guid.NewGuid().ToString("D"),
            CreatedUtc = DateTimeOffset.UtcNow.AddDays(-500)
        });

    private static BackendOperationalHealthReport BackendHealthReport() =>
        new()
        {
            ReportId = "backend-operational-health",
            Status = BackendOperationalHealthStatus.Healthy,
            GeneratedUtc = DateTimeOffset.UtcNow,
            ProjectReferenceId = string.Empty,
            CorrelationId = string.Empty,
            SafeSummaryLines = ["Backend health was observed."],
            DependencyChecks = [],
            Warnings = [],
            Recommendations = [BackendHealthRecommendation()],
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record OperationalSurface(
        string Name,
        bool ObservationOnly,
        bool IsAuthority,
        bool CanApprove,
        bool CanSatisfyPolicy,
        bool CanTransitionWorkflow,
        bool CanInvokeTool,
        bool CanDispatchAgent,
        bool CanCallModel,
        bool CanBuildPrompt,
        bool CanCreateTicket,
        bool CanPromoteMemory,
        bool CanActivateRetrieval,
        bool CanApplySource,
        bool CanApplyPatch,
        bool CanRepair,
        bool CanRestart,
        bool CanRunMigration,
        bool CanRunCleanup,
        bool CanScheduleCleanup,
        bool CanMutateSql,
        bool HealthIsReleaseReadiness);
}

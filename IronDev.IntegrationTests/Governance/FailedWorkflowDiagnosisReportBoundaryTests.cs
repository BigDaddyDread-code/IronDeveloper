using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("FailedWorkflowDiagnosisReport")]
public sealed class FailedWorkflowDiagnosisReportBoundaryTests
{
    [TestMethod] public void Report_IsReportOnly() => Assert.IsTrue(Report().IsReportOnly);
    [TestMethod] public void Report_IsNotRootCauseProof() => Assert.IsFalse(Report().IsRootCauseProof);
    [TestMethod] public void Report_CannotRepair() => Assert.IsFalse(Report().CanRepair);
    [TestMethod] public void Report_CannotRetryWorkflow() => Assert.IsFalse(Report().CanRetryWorkflow);
    [TestMethod] public void Report_CannotResumeWorkflow() => Assert.IsFalse(Report().CanResumeWorkflow);
    [TestMethod] public void Report_CannotTransitionWorkflow() => Assert.IsFalse(Report().CanTransitionWorkflow);
    [TestMethod] public void Report_CannotApprove() => Assert.IsFalse(Report().CanApprove);
    [TestMethod] public void Report_CannotSatisfyPolicy() => Assert.IsFalse(Report().CanSatisfyPolicy);
    [TestMethod] public void Report_CannotInvokeTool() => Assert.IsFalse(Report().CanInvokeTool);
    [TestMethod] public void Report_CannotDispatchAgent() => Assert.IsFalse(Report().CanDispatchAgent);
    [TestMethod] public void Report_CannotCallModel() => Assert.IsFalse(Report().CanCallModel);
    [TestMethod] public void Report_CannotCreateTicket() => Assert.IsFalse(Report().CanCreateTicket);
    [TestMethod] public void Report_CannotPromoteMemory() => Assert.IsFalse(Report().CanPromoteMemory);
    [TestMethod] public void Report_CannotActivateRetrieval() => Assert.IsFalse(Report().CanActivateRetrieval);
    [TestMethod] public void Report_CannotApplySource() => Assert.IsFalse(Report().CanApplySource);
    [TestMethod] public void Report_CannotApplyPatch() => Assert.IsFalse(Report().CanApplyPatch);
    [TestMethod] public void Report_ContainsNoUnsafeReasoning() => Assert.IsFalse(Report().ContainsUnsafeReasoning);

    [TestMethod] public void Signal_IsNotRootCauseProof() => Assert.IsFalse(Signal().IsRootCauseProof);
    [TestMethod] public void Signal_CannotRepair() => Assert.IsFalse(Signal().CanRepair);
    [TestMethod] public void Signal_CannotRetryWorkflow() => Assert.IsFalse(Signal().CanRetryWorkflow);
    [TestMethod] public void Signal_CannotTransitionWorkflow() => Assert.IsFalse(Signal().CanTransitionWorkflow);

    [TestMethod] public void Hypothesis_IsNotRootCauseProof() => Assert.IsFalse(Hypothesis().IsRootCauseProof);
    [TestMethod] public void Hypothesis_RequiresHumanReview() => Assert.IsTrue(Hypothesis().RequiresHumanReview);
    [TestMethod] public void Hypothesis_CannotRepair() => Assert.IsFalse(Hypothesis().CanRepair);
    [TestMethod] public void Hypothesis_CannotApprove() => Assert.IsFalse(Hypothesis().CanApprove);
    [TestMethod] public void Hypothesis_CannotSatisfyPolicy() => Assert.IsFalse(Hypothesis().CanSatisfyPolicy);

    [TestMethod] public void MissingEvidence_IsRequirementOnly() => Assert.IsTrue(MissingEvidence().IsRequirementOnly);
    [TestMethod] public void MissingEvidence_DoesNotGrantApproval() => Assert.IsFalse(MissingEvidence().GrantsApproval);
    [TestMethod] public void MissingEvidence_DoesNotSatisfyPolicy() => Assert.IsFalse(MissingEvidence().SatisfiesPolicy);
    [TestMethod] public void MissingEvidence_DoesNotAllowExecution() => Assert.IsFalse(MissingEvidence().AllowsExecution);

    [TestMethod] public void Recommendation_IsInvestigationOnly() => Assert.IsTrue(Recommendation().IsInvestigationOnly);
    [TestMethod] public void Recommendation_IsNotExecutableWorkflowStep() => Assert.IsFalse(Recommendation().IsExecutableWorkflowStep);
    [TestMethod] public void Recommendation_CannotRepair() => Assert.IsFalse(Recommendation().CanRepair);
    [TestMethod] public void Recommendation_CannotRetryWorkflow() => Assert.IsFalse(Recommendation().CanRetryWorkflow);
    [TestMethod] public void Recommendation_CannotCreateTicket() => Assert.IsFalse(Recommendation().CanCreateTicket);
    [TestMethod] public void Recommendation_CannotMutateState() => Assert.IsFalse(Recommendation().CanMutateState);

    [TestMethod]
    public void BoundaryWarnings_PreserveDiagnosisBoundary()
    {
        var warnings = string.Join("\n", FailedWorkflowDiagnosisReportBoundaries.Warnings);
        StringAssert.Contains(warnings, "read-only operational evidence");
        StringAssert.Contains(warnings, "not root-cause proof");
        StringAssert.Contains(warnings, "not a repair");
        StringAssert.Contains(warnings, "not approval");
        StringAssert.Contains(warnings, "does not invoke tools");
    }

    [TestMethod]
    public void ServiceInterface_ExposesOnlyGetReport()
    {
        var methods = typeof(IFailedWorkflowDiagnosisReportService).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { nameof(IFailedWorkflowDiagnosisReportService.GetReportAsync) }, methods);
    }

    [TestMethod]
    public void ServiceInterface_DoesNotExposeActionMethods()
    {
        AssertNoForbiddenNames(
            typeof(IFailedWorkflowDiagnosisReportService).GetMethods().Select(method => method.Name),
            "RepairAsync",
            "RetryAsync",
            "ResumeAsync",
            "RerunAsync",
            "TransitionAsync",
            "CreateTicketAsync",
            "ApproveAsync",
            "SatisfyPolicyAsync",
            "InvokeToolAsync",
            "DispatchAgentAsync",
            "CallModelAsync",
            "PromoteMemoryAsync",
            "ApplySourceAsync",
            "ApplyPatchAsync");
    }

    [TestMethod]
    public void Models_DoNotExposeRawPayloadProperties()
    {
        AssertNoForbiddenNames(AllPropertyNames(), "PayloadJson", "RawPayload", "RawPrompt", "RawCompletion", "RawToolOutput", "RawCommandOutput", "PrivateReasoning", "HiddenReasoning", "ChainOfThought", "SourceContent", "PatchPayload", "DiffPayload");
    }

    [TestMethod]
    public void Validator_RejectsUnsafeQueryText()
    {
        var result = new FailedWorkflowDiagnosisReportValidator().Validate(new FailedWorkflowDiagnosisReportRequest
        {
            WorkflowRunId = "rawPrompt leaked",
            ProjectReferenceId = Guid.NewGuid().ToString("D")
        });

        Assert.IsTrue(result.Any(issue => issue.Kind is FailedWorkflowDiagnosisIssueKind.UnsafeQueryText));
    }

    [TestMethod]
    public void StatusEnum_DoesNotContainActionStates()
    {
        AssertNoForbiddenNames(Enum.GetNames<FailedWorkflowDiagnosisReportStatus>(), "Repairing", "Retried", "Resumed", "Rerun", "Transitioned", "Approved", "PolicySatisfied", "TicketCreated", "Executed");
    }

    private static FailedWorkflowDiagnosisReport Report() =>
        new()
        {
            ReportId = "report-1",
            WorkflowRunId = "workflow-run-1",
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
            Signals = [Signal()],
            Hypotheses = [Hypothesis()],
            MissingEvidence = [MissingEvidence()],
            TraceTimeline = [TraceItem()],
            Recommendations = [Recommendation()],
            BoundaryWarnings = FailedWorkflowDiagnosisReportBoundaries.Warnings
        };

    private static FailedWorkflowDiagnosisSignal Signal() =>
        new()
        {
            Kind = FailedWorkflowDiagnosisSignalKind.WorkflowFailureObserved,
            EvidenceId = "trace-1",
            EventKind = "workflow.failed",
            SourceComponent = "workflow",
            SafeSummary = "Failure signal summary only.",
            Confidence = 0.8m,
            IsRootCauseProof = false,
            CanRepair = false,
            CanRetryWorkflow = false,
            CanTransitionWorkflow = false
        };

    private static FailedWorkflowDiagnosisHypothesis Hypothesis() =>
        new()
        {
            Kind = FailedWorkflowDiagnosisHypothesisKind.WorkflowFailedOrBlocked,
            SafeSummary = "Hypothesis summary only.",
            Confidence = 0.8m,
            SupportingTraceIds = ["trace-1"],
            IsRootCauseProof = false,
            RequiresHumanReview = true,
            CanRepair = false,
            CanApprove = false,
            CanSatisfyPolicy = false
        };

    private static FailedWorkflowDiagnosisMissingEvidence MissingEvidence() =>
        new()
        {
            EvidenceKind = "approval-evidence",
            EvidenceId = "approval-evidence-1",
            SafeSummary = "Missing evidence summary only.",
            IsRequirementOnly = true,
            GrantsApproval = false,
            SatisfiesPolicy = false,
            AllowsExecution = false
        };

    private static FailedWorkflowDiagnosisTraceItem TraceItem() =>
        new()
        {
            TraceId = "trace-1",
            EventKind = "workflow.failed",
            SourceComponent = "workflow",
            SafeSummary = "Trace summary only.",
            RecordedUtc = DateTimeOffset.UtcNow,
            IsEvidenceOnly = true
        };

    private static FailedWorkflowDiagnosisRecommendation Recommendation() =>
        new()
        {
            RecommendationId = "review-trace",
            SafeSummary = "Recommendation summary only.",
            SupportingTraceIds = ["trace-1"],
            IsInvestigationOnly = true,
            IsExecutableWorkflowStep = false,
            CanRepair = false,
            CanRetryWorkflow = false,
            CanCreateTicket = false,
            CanMutateState = false
        };

    private static string[] AllPropertyNames() =>
    [
        .. DiagnosisTypes().SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name))
    ];

    private static IReadOnlyList<Type> DiagnosisTypes() =>
    [
        typeof(FailedWorkflowDiagnosisReportRequest),
        typeof(FailedWorkflowDiagnosisReportResponse),
        typeof(FailedWorkflowDiagnosisReport),
        typeof(FailedWorkflowDiagnosisSignal),
        typeof(FailedWorkflowDiagnosisHypothesis),
        typeof(FailedWorkflowDiagnosisMissingEvidence),
        typeof(FailedWorkflowDiagnosisTraceItem),
        typeof(FailedWorkflowDiagnosisRecommendation),
        typeof(FailedWorkflowDiagnosisReportIssue)
    ];

    private static void AssertNoForbiddenNames(IEnumerable<string> values, params string[] forbidden)
    {
        var set = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var token in forbidden)
            Assert.IsFalse(set.Contains(token), $"Unexpected failed workflow diagnosis authority/payload member found: {token}");
    }
}

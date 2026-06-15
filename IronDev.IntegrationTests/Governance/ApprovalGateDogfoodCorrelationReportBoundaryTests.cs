using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApprovalGateDogfoodCorrelationReport")]
public sealed class ApprovalGateDogfoodCorrelationReportBoundaryTests
{
    [TestMethod] public void Report_IsReportOnly() => Assert.IsTrue(Report().IsReportOnly);
    [TestMethod] public void Report_IsNotApprovalDecision() => Assert.IsFalse(Report().IsApprovalDecision);
    [TestMethod] public void Report_IsNotPolicySatisfaction() => Assert.IsFalse(Report().IsPolicySatisfaction);
    [TestMethod] public void Report_IsNotToolGateMutation() => Assert.IsFalse(Report().IsToolGateMutation);
    [TestMethod] public void Report_IsNotToolExecution() => Assert.IsFalse(Report().IsToolExecution);
    [TestMethod] public void Report_IsNotDogfoodExecution() => Assert.IsFalse(Report().IsDogfoodExecution);
    [TestMethod] public void Report_IsNotReleaseApproval() => Assert.IsFalse(Report().IsReleaseApproval);
    [TestMethod] public void Report_IsNotWorkflowTransition() => Assert.IsFalse(Report().IsWorkflowTransition);
    [TestMethod] public void Report_CannotApprove() => Assert.IsFalse(Report().CanApprove);
    [TestMethod] public void Report_CannotReject() => Assert.IsFalse(Report().CanReject);
    [TestMethod] public void Report_CannotSatisfyPolicy() => Assert.IsFalse(Report().CanSatisfyPolicy);
    [TestMethod] public void Report_CannotOpenGate() => Assert.IsFalse(Report().CanOpenGate);
    [TestMethod] public void Report_CannotInvokeTool() => Assert.IsFalse(Report().CanInvokeTool);
    [TestMethod] public void Report_CannotMarkDogfoodPassed() => Assert.IsFalse(Report().CanMarkDogfoodPassed);
    [TestMethod] public void Report_CannotApproveRelease() => Assert.IsFalse(Report().CanApproveRelease);
    [TestMethod] public void Report_CannotTransitionWorkflow() => Assert.IsFalse(Report().CanTransitionWorkflow);
    [TestMethod] public void Report_CannotDispatchAgent() => Assert.IsFalse(Report().CanDispatchAgent);
    [TestMethod] public void Report_CannotCallModel() => Assert.IsFalse(Report().CanCallModel);
    [TestMethod] public void Report_CannotBuildPrompt() => Assert.IsFalse(Report().CanBuildPrompt);
    [TestMethod] public void Report_CannotCreateTicket() => Assert.IsFalse(Report().CanCreateTicket);
    [TestMethod] public void Report_CannotPromoteMemory() => Assert.IsFalse(Report().CanPromoteMemory);
    [TestMethod] public void Report_CannotActivateRetrieval() => Assert.IsFalse(Report().CanActivateRetrieval);
    [TestMethod] public void Report_CannotApplySource() => Assert.IsFalse(Report().CanApplySource);
    [TestMethod] public void Report_CannotApplyPatch() => Assert.IsFalse(Report().CanApplyPatch);

    [TestMethod] public void ApprovalEvidence_DoesNotGrantApproval() => Assert.IsFalse(ApprovalEvidence().GrantsApproval);
    [TestMethod] public void ApprovalEvidence_DoesNotSatisfyPolicy() => Assert.IsFalse(ApprovalEvidence().SatisfiesPolicy);
    [TestMethod] public void ApprovalEvidence_DoesNotTransitionWorkflow() => Assert.IsFalse(ApprovalEvidence().TransitionsWorkflow);
    [TestMethod] public void ToolGateEvidence_DoesNotOpenGate() => Assert.IsFalse(ToolGateEvidence().OpensGate);
    [TestMethod] public void ToolGateEvidence_DoesNotInvokeTool() => Assert.IsFalse(ToolGateEvidence().InvokesTool);
    [TestMethod] public void DogfoodEvidence_IsNotReleaseApproval() => Assert.IsFalse(DogfoodEvidence().IsReleaseApproval);
    [TestMethod] public void DogfoodEvidence_DoesNotMarkDogfoodPassed() => Assert.IsFalse(DogfoodEvidence().MarksDogfoodPassed);
    [TestMethod] public void ConflictSignal_IsNotVerdict() => Assert.IsFalse(Conflict().IsVerdict);
    [TestMethod] public void ConflictSignal_CannotResolve() => Assert.IsFalse(Conflict().CanResolve);
    [TestMethod] public void Recommendation_IsInvestigationOnly() => Assert.IsTrue(Recommendation().IsInvestigationOnly);
    [TestMethod] public void Recommendation_CannotMutateState() => Assert.IsFalse(Recommendation().CanMutateState);
    [TestMethod] public void Recommendation_CannotApprove() => Assert.IsFalse(Recommendation().CanApprove);
    [TestMethod] public void Recommendation_CannotOpenGate() => Assert.IsFalse(Recommendation().CanOpenGate);
    [TestMethod] public void Recommendation_CannotApproveRelease() => Assert.IsFalse(Recommendation().CanApproveRelease);

    [TestMethod]
    public void BoundaryWarnings_PreserveCorrelationBoundary()
    {
        var warnings = string.Join("\n", ApprovalGateDogfoodCorrelationReportBoundaries.Warnings);
        StringAssert.Contains(warnings, "read-only");
        StringAssert.Contains(warnings, "Correlation is not approval");
        StringAssert.Contains(warnings, "Correlation is not policy satisfaction");
        StringAssert.Contains(warnings, "Dogfood receipt is not release approval");
        StringAssert.Contains(warnings, "Recommendation is not execution");
    }

    [TestMethod]
    public void ServiceInterface_ExposesOnlyGetReport()
    {
        var methods = typeof(IApprovalGateDogfoodCorrelationReportService).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { nameof(IApprovalGateDogfoodCorrelationReportService.GetReportAsync) }, methods);
    }

    [TestMethod]
    public void ServiceInterface_DoesNotExposeActionMethods()
    {
        AssertNoForbiddenNames(
            typeof(IApprovalGateDogfoodCorrelationReportService).GetMethods().Select(method => method.Name),
            "ApproveAsync",
            "RejectAsync",
            "SatisfyPolicyAsync",
            "OpenGateAsync",
            "InvokeToolAsync",
            "MarkDogfoodPassedAsync",
            "ApproveReleaseAsync",
            "TransitionWorkflowAsync",
            "DispatchAgentAsync",
            "CallModelAsync",
            "BuildPromptAsync",
            "CreateTicketAsync",
            "PromoteMemoryAsync",
            "ActivateRetrievalAsync",
            "ApplySourceAsync",
            "ApplyPatchAsync");
    }

    [TestMethod]
    public void Models_DoNotExposeForbiddenPayloadProperties()
    {
        AssertNoForbiddenNames(AllPropertyNames(), "PayloadJson", "RawPayload", "RawPrompt", "RawCompletion", "RawToolOutput", "RawCommandOutput", "PrivateReasoning", "HiddenReasoning", "ChainOfThought", "SourceContent", "PatchPayload", "DiffPayload");
    }

    [TestMethod]
    public void Validator_RejectsUnsafeQueryText()
    {
        var result = new ApprovalGateDogfoodCorrelationReportValidator().Validate(new ApprovalGateDogfoodCorrelationReportRequest
        {
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            WorkflowRunId = "rawPrompt leaked"
        });

        Assert.IsTrue(result.Any(issue => issue.Kind is ApprovalGateDogfoodCorrelationReportIssueKind.UnsafeQueryText));
    }

    [TestMethod]
    public void StatusEnum_DoesNotContainActionStates()
    {
        AssertNoForbiddenNames(Enum.GetNames<ApprovalGateDogfoodCorrelationReportStatus>(), "Approved", "Rejected", "PolicySatisfied", "GateOpened", "ToolInvoked", "DogfoodPassed", "ReleaseApproved", "WorkflowTransitioned", "Executed");
    }

    private static ApprovalGateDogfoodCorrelationReport Report() =>
        new()
        {
            ReportId = "report-1",
            Status = ApprovalGateDogfoodCorrelationReportStatus.ReportAvailable,
            ProjectReferenceId = Guid.NewGuid().ToString("D"),
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            CorrelationId = Guid.NewGuid().ToString("D"),
            GeneratedUtc = DateTimeOffset.UtcNow,
            SafeSummaryLines = ["summary only"],
            ApprovalEvidence = [ApprovalEvidence()],
            ToolGateEvidence = [ToolGateEvidence()],
            DogfoodEvidence = [DogfoodEvidence()],
            TraceReferences = [Trace()],
            MissingEvidence = [MissingEvidence()],
            ConflictSignals = [Conflict()],
            Recommendations = [Recommendation()],
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

    private static ApprovalCorrelationEvidence ApprovalEvidence() =>
        new()
        {
            ApprovalReferenceId = "approval-1",
            ApprovalKind = "approval.decision.recorded",
            SafeSummary = "Approval evidence only.",
            IsEvidenceOnly = true,
            GrantsApproval = false,
            SatisfiesPolicy = false,
            TransitionsWorkflow = false
        };

    private static ToolGateCorrelationEvidence ToolGateEvidence() =>
        new()
        {
            ToolGateDecisionId = "gate-1",
            ToolRequestId = "request-1",
            GateKind = "tool.gate.decision.recorded",
            SafeSummary = "Tool gate evidence only.",
            IsEvidenceOnly = true,
            OpensGate = false,
            InvokesTool = false,
            SatisfiesPolicy = false,
            TransitionsWorkflow = false
        };

    private static DogfoodCorrelationEvidence DogfoodEvidence() =>
        new()
        {
            DogfoodReceiptId = "dogfood-1",
            DogfoodKind = "dogfood.receipt.recorded",
            SafeSummary = "Dogfood evidence only.",
            IsEvidenceOnly = true,
            IsReleaseApproval = false,
            MarksDogfoodPassed = false,
            SatisfiesPolicy = false,
            TransitionsWorkflow = false
        };

    private static GovernanceCorrelationTraceReference Trace() =>
        new()
        {
            TraceId = "trace-1",
            EventKind = "governance.event.recorded",
            SafeSummary = "Trace reference only."
        };

    private static GovernanceCorrelationMissingEvidence MissingEvidence() =>
        new()
        {
            MissingEvidenceId = "approval-evidence",
            Kind = GovernanceCorrelationMissingEvidenceKind.MissingApprovalEvidence,
            SafeSummary = "Missing evidence only."
        };

    private static GovernanceCorrelationConflictSignal Conflict() =>
        new()
        {
            ConflictId = "conflict-1",
            Kind = GovernanceCorrelationConflictKind.ToolGateBlockedButDogfoodPresent,
            SafeSummary = "Conflict signal only.",
            IsVerdict = false,
            CanResolve = false
        };

    private static GovernanceCorrelationRecommendation Recommendation() =>
        new()
        {
            RecommendationId = "review-trace",
            SafeSummary = "Recommendation only.",
            SupportingReferenceIds = ["trace-1"],
            IsInvestigationOnly = true,
            CanMutateState = false,
            CanApprove = false,
            CanOpenGate = false,
            CanApproveRelease = false
        };

    private static string[] AllPropertyNames() =>
    [
        .. ReportTypes().SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name))
    ];

    private static IReadOnlyList<Type> ReportTypes() =>
    [
        typeof(ApprovalGateDogfoodCorrelationReportRequest),
        typeof(ApprovalGateDogfoodCorrelationReportResponse),
        typeof(ApprovalGateDogfoodCorrelationReport),
        typeof(ApprovalCorrelationEvidence),
        typeof(ToolGateCorrelationEvidence),
        typeof(DogfoodCorrelationEvidence),
        typeof(GovernanceCorrelationTraceReference),
        typeof(GovernanceCorrelationMissingEvidence),
        typeof(GovernanceCorrelationConflictSignal),
        typeof(GovernanceCorrelationRecommendation),
        typeof(ApprovalGateDogfoodCorrelationReportIssue)
    ];

    private static void AssertNoForbiddenNames(IEnumerable<string> values, params string[] forbidden)
    {
        var set = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var token in forbidden)
            Assert.IsFalse(set.Contains(token), $"Unexpected correlation report authority/payload member found: {token}");
    }
}

using IronDev.Core.Agents;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class AgentRunHealthSummaryBoundaryTests
{
    private static readonly Guid ProjectReferenceId = Guid.Parse("aaaaaaaa-1480-4000-8000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("bbbbbbbb-1480-4000-8000-000000000001");

    [TestMethod]
    public async Task AgentRunHealthSummary_ReturnsReadOnlyBoundaryFlags()
    {
        var service = new AgentRunHealthSummaryService(new FakeTraceExplorer([
            Trace("agent-run-pr148", "agent.run.completed", "agent.run.completed", "workflow")
        ]));

        var response = await service.GetSummaryAsync(Request("agent-run-pr148"));

        Assert.AreEqual(AgentRunHealthSummaryStatus.SummaryAvailable, response.Status);
        Assert.IsNotNull(response.Summary);
        var boundary = response.Summary.Boundary;
        Assert.IsTrue(boundary.ReadOnlySummary);
        Assert.IsFalse(boundary.SummaryIsApproval);
        Assert.IsFalse(boundary.SummaryIsPolicySatisfaction);
        Assert.IsFalse(boundary.SummaryIsExecutionPermission);
        Assert.IsFalse(boundary.SummaryIsReleaseApproval);
        Assert.IsFalse(boundary.SummaryCanStartWorkflow);
        Assert.IsFalse(boundary.SummaryCanResumeWorkflow);
        Assert.IsFalse(boundary.SummaryCanRestartAgent);
        Assert.IsFalse(boundary.SummaryCanRetryAgent);
        Assert.IsFalse(boundary.SummaryCanDispatchAgent);
        Assert.IsFalse(boundary.SummaryCanInvokeTool);
        Assert.IsFalse(boundary.SummaryCanCallModel);
        Assert.IsFalse(boundary.SummaryCanCreateTicket);
        Assert.IsFalse(boundary.SummaryCanMutateSource);
        Assert.IsFalse(boundary.SummaryCanApplyPatch);
        Assert.IsFalse(boundary.SummaryCanPromoteMemory);
        Assert.IsFalse(boundary.SummaryCanActivateRetrieval);
        Assert.IsFalse(boundary.CreatesGovernanceEvent);
        Assert.IsFalse(boundary.CreatesApprovalDecision);
        Assert.IsFalse(boundary.CreatesPolicyDecision);
        Assert.IsFalse(boundary.CreatesToolRequest);
        Assert.IsFalse(boundary.CreatesDogfoodReceipt);
        Assert.IsFalse(boundary.ExposesRawPayloadJson);
        Assert.IsFalse(boundary.ExposesRawPrompt);
        Assert.IsFalse(boundary.ExposesRawCompletion);
        Assert.IsFalse(boundary.ExposesRawToolOutput);
        Assert.IsFalse(boundary.ExposesSourceContent);
        Assert.IsFalse(boundary.ExposesPatchPayload);
        Assert.IsFalse(boundary.ExposesPrivateReasoning);
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_MissingTrace_ReturnsNoEvidenceWithoutAction()
    {
        var service = new AgentRunHealthSummaryService(new FakeTraceExplorer([]));

        var response = await service.GetSummaryAsync(Request("agent-run-missing"));

        Assert.AreEqual(AgentRunHealthSummaryStatus.NoAgentRunEvidenceFound, response.Status);
        Assert.IsNull(response.Summary);
        Assert.AreEqual(1, response.Issues.Count);
        StringAssert.Contains(string.Join("\n", response.BoundaryWarnings), "Agent run health summary is read-only.");
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_ClassifiesFailureAsObservedFailed()
    {
        var service = new AgentRunHealthSummaryService(new FakeTraceExplorer([
            Trace("agent-run-failed", "agent.run.failed", "agent.run.failed", "workflow")
        ]));

        var response = await service.GetSummaryAsync(Request("agent-run-failed", includeOptionalEvidence: false));

        Assert.AreEqual(AgentRunHealthSummaryStatus.SummaryAvailable, response.Status);
        Assert.IsNotNull(response.Summary);
        Assert.AreEqual(AgentRunHealthCategory.ObservedFailed, response.Summary.HealthCategory);
        Assert.AreEqual(1, response.Summary.CriticalSignalCount);
        Assert.IsTrue(response.Summary.Signals.Any(signal => signal.Kind is AgentRunHealthSignalKind.AgentRunFailed));
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_MissingOptionalSignalsIsEvidenceIncomplete()
    {
        var service = new AgentRunHealthSummaryService(new FakeTraceExplorer([
            Trace("agent-run-incomplete", "agent.run.completed", "agent.run.completed", "workflow")
        ]));

        var response = await service.GetSummaryAsync(Request("agent-run-incomplete"));

        Assert.AreEqual(AgentRunHealthSummaryStatus.SummaryAvailable, response.Status);
        Assert.IsNotNull(response.Summary);
        Assert.AreEqual(AgentRunHealthCategory.EvidenceIncomplete, response.Summary.HealthCategory);
        Assert.IsTrue(response.Summary.MissingEvidence.Count >= 1);
        Assert.IsFalse(response.Summary.Recommendations.Any(AgentRunHealthSummaryValidator.ContainsUnsafeText));
    }

    [TestMethod]
    public async Task AgentRunHealthSummary_UnsafeSelectorFailsClosed()
    {
        var service = new AgentRunHealthSummaryService(new FakeTraceExplorer([]));

        var response = await service.GetSummaryAsync(Request("rawPrompt leaked"));

        Assert.AreEqual(AgentRunHealthSummaryStatus.InvalidRequest, response.Status);
        Assert.IsNull(response.Summary);
        Assert.IsTrue(response.Issues.Any(issue => issue.Kind is AgentRunHealthSummaryIssueKind.UnsafeQueryText));
        Assert.IsFalse(string.Join("\n", response.Issues.Select(issue => issue.Message)).Contains("rawPrompt", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AgentRunHealthSummary_ValidatorRequiresProjectOrCorrelation()
    {
        var issues = new AgentRunHealthSummaryValidator().Validate(new AgentRunHealthSummaryRequest
        {
            AgentRunId = "agent-run-pr148"
        });

        Assert.IsTrue(issues.Any(issue => issue.Kind is AgentRunHealthSummaryIssueKind.MissingSelector));
    }

    private static AgentRunHealthSummaryRequest Request(string agentRunId, bool includeOptionalEvidence = true) =>
        new()
        {
            ProjectReferenceId = ProjectReferenceId.ToString("D"),
            CorrelationId = CorrelationId.ToString("D"),
            AgentRunId = agentRunId,
            IncludeGateSignals = includeOptionalEvidence,
            IncludeApprovalSignals = includeOptionalEvidence,
            IncludePolicySignals = includeOptionalEvidence,
            IncludeDogfoodSignals = includeOptionalEvidence
        };

    private static GovernanceTraceSummary Trace(
        string subjectReferenceId,
        string eventKind,
        string safeSummary,
        string sourceComponent) =>
        new()
        {
            TraceId = Guid.NewGuid().ToString("D"),
            ProjectReferenceId = ProjectReferenceId.ToString("D"),
            WorkflowRunId = string.Empty,
            WorkflowStepId = string.Empty,
            CorrelationId = CorrelationId.ToString("D"),
            CausationId = string.Empty,
            SubjectReferenceId = subjectReferenceId,
            EventKind = eventKind,
            SourceComponent = sourceComponent,
            SafeSummary = safeSummary,
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

    private sealed class FakeTraceExplorer : IGovernanceTraceExplorerService
    {
        private readonly IReadOnlyList<GovernanceTraceSummary> _traces;

        public FakeTraceExplorer(IReadOnlyList<GovernanceTraceSummary> traces)
        {
            _traces = traces;
        }

        public Task<GovernanceTraceListResponse> SearchAsync(GovernanceTraceQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GovernanceTraceListResponse
            {
                Status = _traces.Count == 0 ? GovernanceTraceExplorerStatus.NoTraceFound : GovernanceTraceExplorerStatus.TraceListReturned,
                Traces = _traces,
                Issues = [],
                BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
            });

        public Task<GovernanceTraceDetailResponse> GetByTraceIdAsync(string traceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GovernanceTraceDetailResponse> GetByCorrelationIdAsync(string correlationId, string projectReferenceId = "", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GovernanceTraceDetailResponse> GetByWorkflowRunIdAsync(string workflowRunId, string projectReferenceId = "", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

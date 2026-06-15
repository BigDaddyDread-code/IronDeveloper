using IronDev.Core.Governance;
using IronDev.Core.Workflow;

namespace IronDev.Infrastructure.Workflow;

public sealed class FailedWorkflowDiagnosisReportService : IFailedWorkflowDiagnosisReportService
{
    private readonly IGovernanceTraceExplorerService _traceExplorer;
    private readonly FailedWorkflowDiagnosisReportValidator _validator;

    public FailedWorkflowDiagnosisReportService(IGovernanceTraceExplorerService traceExplorer)
        : this(traceExplorer, new FailedWorkflowDiagnosisReportValidator())
    {
    }

    internal FailedWorkflowDiagnosisReportService(
        IGovernanceTraceExplorerService traceExplorer,
        FailedWorkflowDiagnosisReportValidator validator)
    {
        _traceExplorer = traceExplorer ?? throw new ArgumentNullException(nameof(traceExplorer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<FailedWorkflowDiagnosisReportResponse> GetReportAsync(
        FailedWorkflowDiagnosisReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.Validate(request);
        if (issues.Count > 0)
            return Response(FailedWorkflowDiagnosisReportStatus.InvalidRequest, null, issues);

        var normalized = _validator.Normalize(request);
        var traceResponse = await _traceExplorer.SearchAsync(new GovernanceTraceQuery
        {
            ProjectReferenceId = normalized.ProjectReferenceId,
            CorrelationId = normalized.CorrelationId,
            Take = normalized.TakeTraceItems,
            IncludeRelated = true
        }, cancellationToken);

        if (traceResponse.Status is GovernanceTraceExplorerStatus.InvalidRequest)
        {
            var mappedIssues = traceResponse.Issues.Select(issue => FailedWorkflowDiagnosisReportValidator.Issue(
                issue.Kind is GovernanceTraceExplorerIssueKind.InvalidProjectReferenceId
                    ? FailedWorkflowDiagnosisIssueKind.InvalidProjectReferenceId
                    : issue.Kind is GovernanceTraceExplorerIssueKind.InvalidCorrelationId
                        ? FailedWorkflowDiagnosisIssueKind.InvalidCorrelationId
                        : issue.Kind is GovernanceTraceExplorerIssueKind.InvalidTake
                            ? FailedWorkflowDiagnosisIssueKind.InvalidTakeTraceItems
                            : FailedWorkflowDiagnosisIssueKind.UnsafeQueryText,
                issue.Field,
                issue.Message)).ToArray();

            return Response(FailedWorkflowDiagnosisReportStatus.InvalidRequest, null, mappedIssues);
        }

        var traces = FilterTraces(traceResponse.Traces, normalized).ToArray();
        if (traces.Length == 0)
            return Response(FailedWorkflowDiagnosisReportStatus.NoWorkflowEvidenceFound, null, []);

        var signals = BuildSignals(traces).ToArray();
        var status = signals.Length == 0
            ? FailedWorkflowDiagnosisReportStatus.NoFailureEvidenceFound
            : FailedWorkflowDiagnosisReportStatus.ReportAvailable;

        var report = BuildReport(normalized, traces, signals, status);
        return Response(status, report, []);
    }

    private static FailedWorkflowDiagnosisReportResponse Response(
        FailedWorkflowDiagnosisReportStatus status,
        FailedWorkflowDiagnosisReport? report,
        IReadOnlyList<FailedWorkflowDiagnosisReportIssue> issues) =>
        new()
        {
            Status = status,
            Report = report,
            Issues = issues,
            BoundaryWarnings = FailedWorkflowDiagnosisReportBoundaries.Warnings
        };

    private static IEnumerable<GovernanceTraceSummary> FilterTraces(
        IEnumerable<GovernanceTraceSummary> traces,
        FailedWorkflowDiagnosisReportRequest request)
    {
        foreach (var trace in traces)
        {
            if (!string.IsNullOrWhiteSpace(request.ProjectReferenceId) &&
                !Matches(trace.ProjectReferenceId, request.ProjectReferenceId))
            {
                continue;
            }

            var workflowMatch = Matches(trace.WorkflowRunId, request.WorkflowRunId) ||
                Matches(trace.SubjectReferenceId, request.WorkflowRunId) ||
                (!string.IsNullOrWhiteSpace(request.CorrelationId) && Matches(trace.CorrelationId, request.CorrelationId));

            if (!workflowMatch)
                continue;

            if (!string.IsNullOrWhiteSpace(request.WorkflowStepId) &&
                !Matches(trace.WorkflowStepId, request.WorkflowStepId) &&
                !Matches(trace.SubjectReferenceId, request.WorkflowStepId))
            {
                continue;
            }

            yield return trace;
        }
    }

    private static IEnumerable<FailedWorkflowDiagnosisSignal> BuildSignals(IEnumerable<GovernanceTraceSummary> traces)
    {
        foreach (var trace in traces)
        {
            var kind = ClassifySignal(trace);
            if (kind is FailedWorkflowDiagnosisSignalKind.Unknown)
                continue;

            yield return new FailedWorkflowDiagnosisSignal
            {
                Kind = kind,
                EvidenceId = FailedWorkflowDiagnosisReportValidator.SafeText(trace.TraceId),
                EventKind = FailedWorkflowDiagnosisReportValidator.SafeText(trace.EventKind),
                SourceComponent = FailedWorkflowDiagnosisReportValidator.SafeText(trace.SourceComponent),
                SafeSummary = SignalSummary(kind, trace),
                Confidence = Confidence(kind),
                IsRootCauseProof = false,
                CanRepair = false,
                CanRetryWorkflow = false,
                CanTransitionWorkflow = false
            };
        }
    }

    private static FailedWorkflowDiagnosisSignalKind ClassifySignal(GovernanceTraceSummary trace)
    {
        var text = $"{trace.EventKind} {trace.SafeSummary}";
        if (ContainsAny(text, "timeout"))
            return FailedWorkflowDiagnosisSignalKind.TimeoutObserved;

        if (ContainsAny(text, "exception", "error"))
            return FailedWorkflowDiagnosisSignalKind.ExceptionObserved;

        if (ContainsAny(text, "validation.failed", "validation failure", "validation failed", "test.failed"))
            return FailedWorkflowDiagnosisSignalKind.ValidationFailureObserved;

        if (ContainsAny(text, "tool.gate.blocked", "gate.blocked", "gate denied", "tool gate denied"))
            return FailedWorkflowDiagnosisSignalKind.ToolGateBlocked;

        if (ContainsAny(text, "approval.required", "approval.missing", "missing approval", "approval required"))
            return FailedWorkflowDiagnosisSignalKind.ApprovalEvidenceMissing;

        if (ContainsAny(text, "policy.required", "policy.missing", "missing policy", "policy required"))
            return FailedWorkflowDiagnosisSignalKind.PolicyEvidenceMissing;

        if (ContainsAny(text, "halt", "halted"))
            return FailedWorkflowDiagnosisSignalKind.GovernanceHaltObserved;

        if (ContainsAny(text, "step.failed", "step failure"))
            return FailedWorkflowDiagnosisSignalKind.StepFailureObserved;

        if (ContainsAny(text, "step.blocked", "step blocked"))
            return FailedWorkflowDiagnosisSignalKind.StepBlockedObserved;

        if (ContainsAny(text, "workflow.failed", "workflow failure", "failed"))
            return FailedWorkflowDiagnosisSignalKind.WorkflowFailureObserved;

        if (ContainsAny(text, "workflow.blocked", "workflow blocked", "blocked"))
            return FailedWorkflowDiagnosisSignalKind.WorkflowBlockedObserved;

        return FailedWorkflowDiagnosisSignalKind.Unknown;
    }

    private static FailedWorkflowDiagnosisReport BuildReport(
        FailedWorkflowDiagnosisReportRequest request,
        IReadOnlyList<GovernanceTraceSummary> traces,
        IReadOnlyList<FailedWorkflowDiagnosisSignal> signals,
        FailedWorkflowDiagnosisReportStatus status)
    {
        var hypotheses = BuildHypotheses(signals).ToArray();
        var missingEvidence = BuildMissingEvidence(signals).ToArray();
        var recommendations = request.IncludeRecommendations ? BuildRecommendations(hypotheses, traces).ToArray() : [];
        var timeline = request.IncludeTraceTimeline ? traces.Select(ToTimeline).ToArray() : [];

        return new FailedWorkflowDiagnosisReport
        {
            ReportId = $"failed-workflow-diagnosis-{request.WorkflowRunId}",
            WorkflowRunId = request.WorkflowRunId,
            ProjectReferenceId = request.ProjectReferenceId,
            WorkflowStepId = request.WorkflowStepId,
            CorrelationId = request.CorrelationId,
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
            Signals = signals,
            Hypotheses = hypotheses.Length == 0 && status is FailedWorkflowDiagnosisReportStatus.NoFailureEvidenceFound
                ? [InsufficientEvidenceHypothesis(traces)]
                : hypotheses,
            MissingEvidence = missingEvidence,
            TraceTimeline = timeline,
            Recommendations = recommendations,
            BoundaryWarnings = FailedWorkflowDiagnosisReportBoundaries.Warnings
        };
    }

    private static IEnumerable<FailedWorkflowDiagnosisHypothesis> BuildHypotheses(IReadOnlyList<FailedWorkflowDiagnosisSignal> signals)
    {
        foreach (var group in signals.GroupBy(signal => ToHypothesisKind(signal.Kind)))
        {
            if (group.Key is FailedWorkflowDiagnosisHypothesisKind.Unknown)
                continue;

            var traceIds = group.Select(signal => signal.EvidenceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            yield return new FailedWorkflowDiagnosisHypothesis
            {
                Kind = group.Key,
                SafeSummary = HypothesisSummary(group.Key),
                Confidence = group.Max(signal => signal.Confidence),
                SupportingTraceIds = traceIds,
                IsRootCauseProof = false,
                RequiresHumanReview = true,
                CanRepair = false,
                CanApprove = false,
                CanSatisfyPolicy = false
            };
        }
    }

    private static IEnumerable<FailedWorkflowDiagnosisMissingEvidence> BuildMissingEvidence(IReadOnlyList<FailedWorkflowDiagnosisSignal> signals)
    {
        if (signals.Any(signal => signal.Kind is FailedWorkflowDiagnosisSignalKind.ApprovalEvidenceMissing))
            yield return Missing("approval-evidence", "human-approval-evidence", "Human approval evidence appears required or missing. This requirement does not grant approval.");

        if (signals.Any(signal => signal.Kind is FailedWorkflowDiagnosisSignalKind.PolicyEvidenceMissing))
            yield return Missing("policy-evidence", "policy-decision-evidence", "Policy evidence appears required or missing. This requirement does not satisfy policy.");

        if (signals.Any(signal => signal.Kind is FailedWorkflowDiagnosisSignalKind.ToolGateBlocked))
            yield return Missing("tool-gate-evidence", "tool-gate-decision-evidence", "Tool gate evidence appears blocked or missing. This requirement does not execute a tool.");

        if (signals.Any(signal => signal.Kind is FailedWorkflowDiagnosisSignalKind.ValidationFailureObserved))
            yield return Missing("validation-evidence", "validation-output-evidence", "Validation failure evidence was observed. This does not rerun validation.");
    }

    private static IEnumerable<FailedWorkflowDiagnosisRecommendation> BuildRecommendations(
        IReadOnlyList<FailedWorkflowDiagnosisHypothesis> hypotheses,
        IReadOnlyList<GovernanceTraceSummary> traces)
    {
        var traceIds = traces.Select(trace => trace.TraceId).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        yield return Recommendation("review-trace", "Review the referenced governance trace events and workflow evidence before taking action.", traceIds);

        if (hypotheses.Any(hypothesis => hypothesis.Kind is FailedWorkflowDiagnosisHypothesisKind.ApprovalEvidenceMayBeMissing))
            yield return Recommendation("review-approval-evidence", "Check whether a separate human approval package exists and remains valid.", traceIds);

        if (hypotheses.Any(hypothesis => hypothesis.Kind is FailedWorkflowDiagnosisHypothesisKind.PolicyEvidenceMayBeMissing))
            yield return Recommendation("review-policy-evidence", "Check whether a separate policy decision event exists for the requested scope.", traceIds);

        if (hypotheses.Any(hypothesis => hypothesis.Kind is FailedWorkflowDiagnosisHypothesisKind.ToolGateMayHaveBlockedProgress))
            yield return Recommendation("review-tool-gate", "Inspect the tool gate decision evidence before considering any future manual action.", traceIds);
    }

    private static FailedWorkflowDiagnosisTraceItem ToTimeline(GovernanceTraceSummary trace) =>
        new()
        {
            TraceId = FailedWorkflowDiagnosisReportValidator.SafeText(trace.TraceId),
            EventKind = FailedWorkflowDiagnosisReportValidator.SafeText(trace.EventKind),
            SourceComponent = FailedWorkflowDiagnosisReportValidator.SafeText(trace.SourceComponent),
            SafeSummary = FailedWorkflowDiagnosisReportValidator.SafeText(trace.SafeSummary),
            RecordedUtc = trace.RecordedUtc,
            WorkflowStepId = FailedWorkflowDiagnosisReportValidator.SafeText(trace.WorkflowStepId),
            CorrelationId = FailedWorkflowDiagnosisReportValidator.SafeText(trace.CorrelationId),
            SubjectReferenceId = FailedWorkflowDiagnosisReportValidator.SafeText(trace.SubjectReferenceId),
            IsEvidenceOnly = true
        };

    private static FailedWorkflowDiagnosisHypothesis InsufficientEvidenceHypothesis(IReadOnlyList<GovernanceTraceSummary> traces) =>
        new()
        {
            Kind = FailedWorkflowDiagnosisHypothesisKind.InsufficientFailureEvidence,
            SafeSummary = "Workflow trace evidence exists, but no failure, halt, block, missing-evidence, exception, or timeout signal was found.",
            Confidence = 0.1m,
            SupportingTraceIds = traces.Select(trace => trace.TraceId).Take(10).ToArray(),
            IsRootCauseProof = false,
            RequiresHumanReview = true,
            CanRepair = false,
            CanApprove = false,
            CanSatisfyPolicy = false
        };

    private static FailedWorkflowDiagnosisMissingEvidence Missing(string kind, string id, string summary) =>
        new()
        {
            EvidenceKind = kind,
            EvidenceId = id,
            SafeSummary = summary,
            IsRequirementOnly = true,
            GrantsApproval = false,
            SatisfiesPolicy = false,
            AllowsExecution = false
        };

    private static FailedWorkflowDiagnosisRecommendation Recommendation(
        string id,
        string summary,
        IReadOnlyList<string> traceIds) =>
        new()
        {
            RecommendationId = id,
            SafeSummary = summary,
            SupportingTraceIds = traceIds,
            IsInvestigationOnly = true,
            IsExecutableWorkflowStep = false,
            CanRepair = false,
            CanRetryWorkflow = false,
            CanCreateTicket = false,
            CanMutateState = false
        };

    private static FailedWorkflowDiagnosisHypothesisKind ToHypothesisKind(FailedWorkflowDiagnosisSignalKind kind) =>
        kind switch
        {
            FailedWorkflowDiagnosisSignalKind.WorkflowFailureObserved or
            FailedWorkflowDiagnosisSignalKind.WorkflowBlockedObserved or
            FailedWorkflowDiagnosisSignalKind.StepFailureObserved or
            FailedWorkflowDiagnosisSignalKind.StepBlockedObserved or
            FailedWorkflowDiagnosisSignalKind.GovernanceHaltObserved => FailedWorkflowDiagnosisHypothesisKind.WorkflowFailedOrBlocked,
            FailedWorkflowDiagnosisSignalKind.ApprovalEvidenceMissing => FailedWorkflowDiagnosisHypothesisKind.ApprovalEvidenceMayBeMissing,
            FailedWorkflowDiagnosisSignalKind.PolicyEvidenceMissing => FailedWorkflowDiagnosisHypothesisKind.PolicyEvidenceMayBeMissing,
            FailedWorkflowDiagnosisSignalKind.ToolGateBlocked => FailedWorkflowDiagnosisHypothesisKind.ToolGateMayHaveBlockedProgress,
            FailedWorkflowDiagnosisSignalKind.ValidationFailureObserved => FailedWorkflowDiagnosisHypothesisKind.ValidationMayHaveFailed,
            FailedWorkflowDiagnosisSignalKind.ExceptionObserved or
            FailedWorkflowDiagnosisSignalKind.TimeoutObserved => FailedWorkflowDiagnosisHypothesisKind.RuntimeExceptionOrTimeoutObserved,
            _ => FailedWorkflowDiagnosisHypothesisKind.Unknown
        };

    private static string SignalSummary(FailedWorkflowDiagnosisSignalKind kind, GovernanceTraceSummary trace) =>
        FailedWorkflowDiagnosisReportValidator.SafeText($"{kind} signal observed in governance trace event '{trace.EventKind}'.");

    private static string HypothesisSummary(FailedWorkflowDiagnosisHypothesisKind kind) =>
        kind switch
        {
            FailedWorkflowDiagnosisHypothesisKind.WorkflowFailedOrBlocked => "Workflow or step failure/block evidence was observed. This is not root-cause proof.",
            FailedWorkflowDiagnosisHypothesisKind.ApprovalEvidenceMayBeMissing => "Approval evidence may be missing or unresolved. This does not grant approval.",
            FailedWorkflowDiagnosisHypothesisKind.PolicyEvidenceMayBeMissing => "Policy evidence may be missing or unresolved. This does not satisfy policy.",
            FailedWorkflowDiagnosisHypothesisKind.ToolGateMayHaveBlockedProgress => "Tool gate evidence may have blocked progress. This does not execute or reopen the gate.",
            FailedWorkflowDiagnosisHypothesisKind.ValidationMayHaveFailed => "Validation failure evidence was observed. This does not rerun validation.",
            FailedWorkflowDiagnosisHypothesisKind.RuntimeExceptionOrTimeoutObserved => "Exception or timeout evidence was observed. This does not retry workflow execution.",
            _ => "Insufficient evidence for a failure hypothesis."
        };

    private static decimal Confidence(FailedWorkflowDiagnosisSignalKind kind) =>
        kind switch
        {
            FailedWorkflowDiagnosisSignalKind.WorkflowFailureObserved or
            FailedWorkflowDiagnosisSignalKind.WorkflowBlockedObserved or
            FailedWorkflowDiagnosisSignalKind.StepFailureObserved or
            FailedWorkflowDiagnosisSignalKind.StepBlockedObserved => 0.8m,
            FailedWorkflowDiagnosisSignalKind.ApprovalEvidenceMissing or
            FailedWorkflowDiagnosisSignalKind.PolicyEvidenceMissing or
            FailedWorkflowDiagnosisSignalKind.ToolGateBlocked or
            FailedWorkflowDiagnosisSignalKind.ValidationFailureObserved => 0.7m,
            FailedWorkflowDiagnosisSignalKind.ExceptionObserved or
            FailedWorkflowDiagnosisSignalKind.TimeoutObserved => 0.6m,
            _ => 0.5m
        };

    private static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string? left, string right) =>
        string.Equals(FailedWorkflowDiagnosisReportValidator.NormalizeText(left), right, StringComparison.OrdinalIgnoreCase);
}

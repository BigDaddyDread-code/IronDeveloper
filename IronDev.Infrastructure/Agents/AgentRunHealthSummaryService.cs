using IronDev.Core.Agents;
using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Agents;

public sealed class AgentRunHealthSummaryService : IAgentRunHealthSummaryService
{
    private readonly IGovernanceTraceExplorerService _traceExplorer;
    private readonly AgentRunHealthSummaryValidator _validator;

    public AgentRunHealthSummaryService(IGovernanceTraceExplorerService traceExplorer)
        : this(traceExplorer, new AgentRunHealthSummaryValidator())
    {
    }

    internal AgentRunHealthSummaryService(
        IGovernanceTraceExplorerService traceExplorer,
        AgentRunHealthSummaryValidator validator)
    {
        _traceExplorer = traceExplorer ?? throw new ArgumentNullException(nameof(traceExplorer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<AgentRunHealthSummaryResponse> GetSummaryAsync(
        AgentRunHealthSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.Validate(request);
        if (issues.Count > 0)
            return Invalid(issues);

        var normalized = _validator.Normalize(request);
        var traces = await _traceExplorer.SearchAsync(new GovernanceTraceQuery
        {
            ProjectReferenceId = normalized.ProjectReferenceId,
            WorkflowRunId = normalized.WorkflowRunId,
            WorkflowStepId = normalized.WorkflowStepId,
            CorrelationId = normalized.CorrelationId,
            SubjectReferenceId = normalized.AgentRunId,
            FromUtc = normalized.FromUtc,
            ToUtc = normalized.ToUtc,
            Take = normalized.Take,
            IncludeRelated = true
        }, cancellationToken);

        if (traces.Status is GovernanceTraceExplorerStatus.InvalidRequest)
            return Invalid(traces.Issues.Select(ToIssue).ToArray());

        var filtered = Filter(traces.Traces, normalized).ToArray();
        if (filtered.Length == 0)
        {
            return new AgentRunHealthSummaryResponse
            {
                Status = AgentRunHealthSummaryStatus.NoAgentRunEvidenceFound,
                Summary = null,
                Issues =
                [
                    AgentRunHealthSummaryValidator.Issue(
                        AgentRunHealthSummaryIssueKind.MissingSelector,
                        nameof(normalized.AgentRunId),
                        "No safe agent run health evidence was found for the supplied selectors.")
                ],
                BoundaryWarnings = AgentRunHealthSummaryBoundaries.Warnings
            };
        }

        var signals = BuildSignals(filtered, normalized).ToArray();
        var missingEvidence = BuildMissingEvidence(filtered, normalized).ToArray();
        var healthCategory = Classify(signals, missingEvidence);

        return new AgentRunHealthSummaryResponse
        {
            Status = AgentRunHealthSummaryStatus.SummaryAvailable,
            Summary = new AgentRunHealthSummary
            {
                SummaryId = SummaryId(normalized),
                ProjectReferenceId = SafeFirst(normalized.ProjectReferenceId, filtered.Select(trace => trace.ProjectReferenceId)),
                AgentRunId = AgentRunHealthSummaryValidator.SafeText(normalized.AgentRunId),
                CorrelationId = SafeFirst(normalized.CorrelationId, filtered.Select(trace => trace.CorrelationId)),
                WorkflowRunId = SafeFirst(normalized.WorkflowRunId, filtered.Select(trace => trace.WorkflowRunId)),
                WorkflowStepId = SafeFirst(normalized.WorkflowStepId, filtered.Select(trace => trace.WorkflowStepId)),
                AgentId = AgentRunHealthSummaryValidator.SafeText(normalized.AgentId),
                AgentKind = AgentRunHealthSummaryValidator.SafeText(normalized.AgentKind),
                GeneratedUtc = DateTimeOffset.UtcNow,
                HealthCategory = healthCategory,
                TraceCount = filtered.Length,
                CriticalSignalCount = signals.Count(signal => signal.Severity is AgentRunHealthSignalSeverity.Critical),
                WarningSignalCount = signals.Count(signal => signal.Severity is AgentRunHealthSignalSeverity.Warning),
                MissingEvidenceCount = missingEvidence.Length,
                Signals = signals,
                MissingEvidence = missingEvidence,
                TraceReferences = filtered.Select(ToReference).ToArray(),
                Recommendations = Recommendations(healthCategory, missingEvidence).ToArray(),
                Boundary = new AgentRunHealthSummaryBoundary()
            },
            Issues = [],
            BoundaryWarnings = AgentRunHealthSummaryBoundaries.Warnings
        };
    }

    private static IEnumerable<GovernanceTraceSummary> Filter(
        IEnumerable<GovernanceTraceSummary> traces,
        AgentRunHealthSummaryRequest request)
    {
        var filtered = traces;

        if (!string.IsNullOrWhiteSpace(request.AgentRunId))
            filtered = filtered.Where(trace => MatchesAny(request.AgentRunId, trace.SubjectReferenceId, trace.SafeSummary, trace.EventKind));

        if (!string.IsNullOrWhiteSpace(request.WorkflowRunId))
            filtered = filtered.Where(trace => MatchesAny(request.WorkflowRunId, trace.WorkflowRunId, trace.SubjectReferenceId, trace.SafeSummary));

        if (!string.IsNullOrWhiteSpace(request.WorkflowStepId))
            filtered = filtered.Where(trace => MatchesAny(request.WorkflowStepId, trace.WorkflowStepId, trace.SubjectReferenceId, trace.SafeSummary));

        if (!string.IsNullOrWhiteSpace(request.AgentId))
            filtered = filtered.Where(trace => MatchesAny(request.AgentId, trace.SourceComponent, trace.SubjectReferenceId, trace.SafeSummary));

        if (!string.IsNullOrWhiteSpace(request.AgentKind))
            filtered = filtered.Where(trace => MatchesAny(request.AgentKind, trace.SourceComponent, trace.EventKind, trace.SafeSummary));

        return filtered
            .OrderByDescending(trace => trace.RecordedUtc)
            .ThenByDescending(trace => trace.TraceId, StringComparer.Ordinal);
    }

    private static IEnumerable<AgentRunHealthSignal> BuildSignals(
        IReadOnlyList<GovernanceTraceSummary> traces,
        AgentRunHealthSummaryRequest request)
    {
        foreach (var trace in traces)
        {
            var text = $"{trace.EventKind} {trace.SubjectReferenceId} {trace.SourceComponent} {trace.SafeSummary}";
            if (ContainsAny(text, "failed", "failure", "error", "exception"))
            {
                yield return Signal(AgentRunHealthSignalKind.AgentRunFailed, AgentRunHealthSignalSeverity.Critical, trace, "Failure evidence was recorded for this agent run.");
                continue;
            }

            if (ContainsAny(text, "blocked", "halt", "approval.required", "approval-required", "approval required"))
            {
                yield return Signal(AgentRunHealthSignalKind.ApprovalRequired, AgentRunHealthSignalSeverity.Warning, trace, "Approval or halt evidence was recorded for this agent run.");
                continue;
            }

            if (ContainsAny(text, "policy.required", "policy-required", "policy required", "policy.blocked"))
            {
                yield return Signal(AgentRunHealthSignalKind.PolicyRequired, AgentRunHealthSignalSeverity.Warning, trace, "Policy evidence is required before future execution.");
                continue;
            }

            if (request.IncludeGateSignals && ContainsAny(text, "tool.gate", "gate.blocked", "tool gate"))
            {
                var severity = ContainsAny(text, "blocked", "rejected", "denied")
                    ? AgentRunHealthSignalSeverity.Warning
                    : AgentRunHealthSignalSeverity.Info;
                yield return Signal(
                    severity is AgentRunHealthSignalSeverity.Warning ? AgentRunHealthSignalKind.ToolGateBlocked : AgentRunHealthSignalKind.ToolGateObserved,
                    severity,
                    trace,
                    "Tool gate evidence was recorded for this agent run.");
                continue;
            }

            if (request.IncludeDogfoodSignals && ContainsAny(text, "dogfood", "receipt"))
            {
                yield return Signal(AgentRunHealthSignalKind.DogfoodReceiptObserved, AgentRunHealthSignalSeverity.Info, trace, "Dogfood receipt evidence was recorded.");
                continue;
            }

            if (ContainsAny(text, "completed", "succeeded", "success"))
            {
                yield return Signal(AgentRunHealthSignalKind.AgentRunCompleted, AgentRunHealthSignalSeverity.Info, trace, "Completion evidence was recorded for this agent run.");
                continue;
            }

            yield return Signal(AgentRunHealthSignalKind.AgentRunObserved, AgentRunHealthSignalSeverity.Info, trace, "Agent run trace evidence was recorded.");
        }
    }

    private static IEnumerable<AgentRunHealthMissingEvidence> BuildMissingEvidence(
        IReadOnlyList<GovernanceTraceSummary> traces,
        AgentRunHealthSummaryRequest request)
    {
        var combined = string.Join(" ", traces.Select(trace => $"{trace.EventKind} {trace.SubjectReferenceId} {trace.SourceComponent} {trace.SafeSummary}"));

        if (string.IsNullOrWhiteSpace(SafeFirst(request.CorrelationId, traces.Select(trace => trace.CorrelationId))))
            yield return Missing("correlation", "correlation-required", "No correlation reference was present in the visible trace evidence.");

        if (request.IncludeGateSignals && !ContainsAny(combined, "tool.gate", "gate"))
            yield return Missing("tool_gate", "tool-gate-evidence-required", "No tool gate signal was found in the visible trace evidence.");

        if (request.IncludeApprovalSignals && !ContainsAny(combined, "approval"))
            yield return Missing("approval", "approval-evidence-required", "No approval signal was found in the visible trace evidence.");

        if (request.IncludePolicySignals && !ContainsAny(combined, "policy"))
            yield return Missing("policy", "policy-evidence-required", "No policy signal was found in the visible trace evidence.");

        if (request.IncludeDogfoodSignals && !ContainsAny(combined, "dogfood", "receipt"))
            yield return Missing("dogfood_receipt", "dogfood-receipt-required", "No dogfood receipt signal was found in the visible trace evidence.");
    }

    private static AgentRunHealthCategory Classify(
        IReadOnlyList<AgentRunHealthSignal> signals,
        IReadOnlyList<AgentRunHealthMissingEvidence> missingEvidence)
    {
        if (signals.Any(signal => signal.Kind is AgentRunHealthSignalKind.AgentRunFailed))
            return AgentRunHealthCategory.ObservedFailed;

        if (signals.Any(signal => signal.Kind is AgentRunHealthSignalKind.WorkflowBlocked or AgentRunHealthSignalKind.ApprovalRequired or AgentRunHealthSignalKind.PolicyRequired or AgentRunHealthSignalKind.ToolGateBlocked))
            return AgentRunHealthCategory.ObservedBlocked;

        if (missingEvidence.Count > 0)
            return AgentRunHealthCategory.EvidenceIncomplete;

        if (signals.Any(signal => signal.Severity is AgentRunHealthSignalSeverity.Warning))
            return AgentRunHealthCategory.ObservedWarning;

        if (signals.Any(signal => signal.Kind is AgentRunHealthSignalKind.AgentRunCompleted))
            return AgentRunHealthCategory.ObservedHealthy;

        return AgentRunHealthCategory.NeedsHumanReview;
    }

    private static AgentRunHealthSignal Signal(
        AgentRunHealthSignalKind kind,
        AgentRunHealthSignalSeverity severity,
        GovernanceTraceSummary trace,
        string summary) =>
        new()
        {
            Kind = kind,
            Severity = severity,
            ReferenceId = AgentRunHealthSummaryValidator.SafeText(trace.TraceId),
            SafeSummary = AgentRunHealthSummaryValidator.SafeText(summary),
            RecordedUtc = trace.RecordedUtc
        };

    private static AgentRunHealthMissingEvidence Missing(string kind, string referenceId, string summary) =>
        new()
        {
            EvidenceKind = AgentRunHealthSummaryValidator.SafeText(kind),
            ReferenceId = AgentRunHealthSummaryValidator.SafeText(referenceId),
            SafeSummary = AgentRunHealthSummaryValidator.SafeText(summary)
        };

    private static AgentRunHealthTraceReference ToReference(GovernanceTraceSummary trace) =>
        new()
        {
            TraceId = AgentRunHealthSummaryValidator.SafeText(trace.TraceId),
            EventKind = AgentRunHealthSummaryValidator.SafeText(trace.EventKind),
            SubjectReferenceId = AgentRunHealthSummaryValidator.SafeText(trace.SubjectReferenceId),
            SourceComponent = AgentRunHealthSummaryValidator.SafeText(trace.SourceComponent),
            RecordedUtc = trace.RecordedUtc
        };

    private static IEnumerable<string> Recommendations(AgentRunHealthCategory category, IReadOnlyList<AgentRunHealthMissingEvidence> missingEvidence)
    {
        yield return "Inspect the referenced safe governance trace and audit records before taking any action.";
        yield return "Treat this health summary as operational evidence only, not approval or execution permission.";

        if (category is AgentRunHealthCategory.ObservedFailed or AgentRunHealthCategory.ObservedBlocked)
            yield return "Use a separate governed review path before retrying, resuming, or changing any workflow.";

        if (missingEvidence.Count > 0)
            yield return "Collect the missing evidence before relying on this summary for operational review.";
    }

    private static AgentRunHealthTraceReference ToReference(AgentRunHealthSignal signal) =>
        new()
        {
            TraceId = signal.ReferenceId,
            EventKind = string.Empty,
            SubjectReferenceId = string.Empty,
            SourceComponent = string.Empty,
            RecordedUtc = signal.RecordedUtc
        };

    private static AgentRunHealthSummaryIssue ToIssue(GovernanceTraceExplorerIssue issue) =>
        AgentRunHealthSummaryValidator.Issue(
            AgentRunHealthSummaryIssueKind.TraceExplorerError,
            issue.Field,
            issue.Message);

    private static AgentRunHealthSummaryResponse Invalid(IReadOnlyList<AgentRunHealthSummaryIssue> issues) =>
        new()
        {
            Status = AgentRunHealthSummaryStatus.InvalidRequest,
            Summary = null,
            Issues = issues,
            BoundaryWarnings = AgentRunHealthSummaryBoundaries.Warnings
        };

    private static string SummaryId(AgentRunHealthSummaryRequest request)
    {
        var selector = SafeFirst(
            request.AgentRunId,
            [request.WorkflowStepId, request.WorkflowRunId, request.CorrelationId, request.ProjectReferenceId]);
        return AgentRunHealthSummaryValidator.SafeText($"agent-run-health:{selector}");
    }

    private static string SafeFirst(string preferred, IEnumerable<string> fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return AgentRunHealthSummaryValidator.SafeText(preferred);

        var first = fallback.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        return AgentRunHealthSummaryValidator.SafeText(first);
    }

    private static bool MatchesAny(string expected, params string[] candidates) =>
        candidates.Any(candidate => candidate.Contains(expected, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

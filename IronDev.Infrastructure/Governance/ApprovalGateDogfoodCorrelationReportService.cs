using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ApprovalGateDogfoodCorrelationReportService : IApprovalGateDogfoodCorrelationReportService
{
    private readonly IGovernanceTraceExplorerService _traceExplorer;
    private readonly ApprovalGateDogfoodCorrelationReportValidator _validator;

    public ApprovalGateDogfoodCorrelationReportService(IGovernanceTraceExplorerService traceExplorer)
        : this(traceExplorer, new ApprovalGateDogfoodCorrelationReportValidator())
    {
    }

    internal ApprovalGateDogfoodCorrelationReportService(
        IGovernanceTraceExplorerService traceExplorer,
        ApprovalGateDogfoodCorrelationReportValidator validator)
    {
        _traceExplorer = traceExplorer ?? throw new ArgumentNullException(nameof(traceExplorer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ApprovalGateDogfoodCorrelationReportResponse> GetReportAsync(
        ApprovalGateDogfoodCorrelationReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.Validate(request);
        if (issues.Count > 0)
            return Response(ApprovalGateDogfoodCorrelationReportStatus.InvalidRequest, null, issues);

        var normalized = _validator.Normalize(request);
        var traceResponse = await _traceExplorer.SearchAsync(new GovernanceTraceQuery
        {
            ProjectReferenceId = normalized.ProjectReferenceId,
            CorrelationId = normalized.CorrelationId,
            CausationId = normalized.CausationId,
            FromUtc = normalized.FromUtc,
            ToUtc = normalized.ToUtc,
            Take = normalized.Take,
            IncludeRelated = true
        }, cancellationToken);

        if (traceResponse.Status is GovernanceTraceExplorerStatus.InvalidRequest)
            return Response(ApprovalGateDogfoodCorrelationReportStatus.InvalidRequest, null, MapTraceIssues(traceResponse.Issues));

        var traces = FilterTraces(traceResponse.Traces, normalized).ToArray();
        if (traces.Length == 0)
            return Response(ApprovalGateDogfoodCorrelationReportStatus.NoEvidenceFound, null, []);

        var approvalEvidence = traces.Where(trace => IsApprovalTrace(trace, normalized)).Select(ToApprovalEvidence).ToArray();
        var toolGateEvidence = traces.Where(trace => IsToolGateTrace(trace, normalized)).Select(trace => ToToolGateEvidence(trace, normalized)).ToArray();
        var dogfoodEvidence = traces.Where(trace => IsDogfoodTrace(trace, normalized)).Select(ToDogfoodEvidence).ToArray();
        var policyEvidencePresent = traces.Any(IsPolicyTrace);
        var traceReferences = normalized.IncludeTraceReferences ? traces.Select(ToTraceReference).ToArray() : [];
        var missingEvidence = normalized.IncludeMissingEvidence
            ? BuildMissingEvidence(normalized, traces, approvalEvidence, toolGateEvidence, dogfoodEvidence, policyEvidencePresent).ToArray()
            : [];
        var conflicts = BuildConflicts(traces, approvalEvidence, toolGateEvidence, dogfoodEvidence, policyEvidencePresent).ToArray();
        var status = missingEvidence.Length > 0 || conflicts.Length > 0
            ? ApprovalGateDogfoodCorrelationReportStatus.EvidenceIncomplete
            : ApprovalGateDogfoodCorrelationReportStatus.ReportAvailable;
        var recommendations = normalized.IncludeRecommendations
            ? BuildRecommendations(traces, missingEvidence, conflicts).ToArray()
            : [];

        var report = BuildReport(
            normalized,
            status,
            traces,
            approvalEvidence,
            toolGateEvidence,
            dogfoodEvidence,
            traceReferences,
            missingEvidence,
            conflicts,
            recommendations);

        return Response(status, report, []);
    }

    private static ApprovalGateDogfoodCorrelationReportResponse Response(
        ApprovalGateDogfoodCorrelationReportStatus status,
        ApprovalGateDogfoodCorrelationReport? report,
        IReadOnlyList<ApprovalGateDogfoodCorrelationReportIssue> issues) =>
        new()
        {
            Status = status,
            Report = report,
            Issues = issues,
            BoundaryWarnings = ApprovalGateDogfoodCorrelationReportBoundaries.Warnings
        };

    private static ApprovalGateDogfoodCorrelationReport BuildReport(
        ApprovalGateDogfoodCorrelationReportRequest request,
        ApprovalGateDogfoodCorrelationReportStatus status,
        IReadOnlyList<GovernanceTraceSummary> traces,
        IReadOnlyList<ApprovalCorrelationEvidence> approvalEvidence,
        IReadOnlyList<ToolGateCorrelationEvidence> toolGateEvidence,
        IReadOnlyList<DogfoodCorrelationEvidence> dogfoodEvidence,
        IReadOnlyList<GovernanceCorrelationTraceReference> traceReferences,
        IReadOnlyList<GovernanceCorrelationMissingEvidence> missingEvidence,
        IReadOnlyList<GovernanceCorrelationConflictSignal> conflicts,
        IReadOnlyList<GovernanceCorrelationRecommendation> recommendations) =>
        new()
        {
            ReportId = ReportId(request),
            Status = status,
            ProjectReferenceId = request.ProjectReferenceId,
            WorkflowRunId = request.WorkflowRunId,
            WorkflowStepId = request.WorkflowStepId,
            CorrelationId = request.CorrelationId,
            GeneratedUtc = DateTimeOffset.UtcNow,
            SafeSummaryLines = SummaryLines(traces, approvalEvidence, toolGateEvidence, dogfoodEvidence, missingEvidence, conflicts),
            ApprovalEvidence = approvalEvidence,
            ToolGateEvidence = toolGateEvidence,
            DogfoodEvidence = dogfoodEvidence,
            TraceReferences = traceReferences,
            MissingEvidence = missingEvidence,
            ConflictSignals = conflicts,
            Recommendations = recommendations,
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

    private static IEnumerable<GovernanceTraceSummary> FilterTraces(
        IEnumerable<GovernanceTraceSummary> traces,
        ApprovalGateDogfoodCorrelationReportRequest request)
    {
        foreach (var trace in traces)
        {
            if (!string.IsNullOrWhiteSpace(request.ProjectReferenceId) && !Matches(trace.ProjectReferenceId, request.ProjectReferenceId))
                continue;

            if (!string.IsNullOrWhiteSpace(request.WorkflowRunId) &&
                !Matches(trace.WorkflowRunId, request.WorkflowRunId) &&
                !Matches(trace.SubjectReferenceId, request.WorkflowRunId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(request.WorkflowStepId) &&
                !Matches(trace.WorkflowStepId, request.WorkflowStepId) &&
                !Matches(trace.SubjectReferenceId, request.WorkflowStepId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(request.ApprovalReferenceId) && !Matches(trace.SubjectReferenceId, request.ApprovalReferenceId))
                continue;

            if (!string.IsNullOrWhiteSpace(request.ToolRequestId) && !Matches(trace.SubjectReferenceId, request.ToolRequestId))
                continue;

            if (!string.IsNullOrWhiteSpace(request.ToolGateDecisionId) && !Matches(trace.SubjectReferenceId, request.ToolGateDecisionId))
                continue;

            if (!string.IsNullOrWhiteSpace(request.DogfoodReceiptId) && !Matches(trace.SubjectReferenceId, request.DogfoodReceiptId))
                continue;

            yield return trace;
        }
    }

    private static IReadOnlyList<ApprovalGateDogfoodCorrelationReportIssue> MapTraceIssues(IReadOnlyList<GovernanceTraceExplorerIssue> issues) =>
        issues.Select(issue => ApprovalGateDogfoodCorrelationReportValidator.Issue(
            issue.Kind switch
            {
                GovernanceTraceExplorerIssueKind.InvalidProjectReferenceId => ApprovalGateDogfoodCorrelationReportIssueKind.InvalidProjectReferenceId,
                GovernanceTraceExplorerIssueKind.InvalidCorrelationId => ApprovalGateDogfoodCorrelationReportIssueKind.InvalidCorrelationId,
                GovernanceTraceExplorerIssueKind.InvalidCausationId => ApprovalGateDogfoodCorrelationReportIssueKind.InvalidCausationId,
                GovernanceTraceExplorerIssueKind.InvalidDateRange => ApprovalGateDogfoodCorrelationReportIssueKind.InvalidDateRange,
                GovernanceTraceExplorerIssueKind.InvalidTake => ApprovalGateDogfoodCorrelationReportIssueKind.InvalidTake,
                GovernanceTraceExplorerIssueKind.UnsafeQueryText => ApprovalGateDogfoodCorrelationReportIssueKind.UnsafeQueryText,
                _ => ApprovalGateDogfoodCorrelationReportIssueKind.MissingSelector
            },
            issue.Field,
            issue.Message)).ToArray();

    private static ApprovalCorrelationEvidence ToApprovalEvidence(GovernanceTraceSummary trace) =>
        new()
        {
            ApprovalReferenceId = SafeText(trace.SubjectReferenceId),
            ApprovalKind = SafeText(trace.EventKind),
            SafeSummary = EvidenceSummary("Approval evidence", trace),
            WorkflowRunId = SafeText(trace.WorkflowRunId),
            WorkflowStepId = SafeText(trace.WorkflowStepId),
            CorrelationId = SafeText(trace.CorrelationId),
            RecordedUtc = trace.RecordedUtc,
            IsEvidenceOnly = true,
            GrantsApproval = false,
            SatisfiesPolicy = false,
            TransitionsWorkflow = false
        };

    private static ToolGateCorrelationEvidence ToToolGateEvidence(GovernanceTraceSummary trace, ApprovalGateDogfoodCorrelationReportRequest request)
    {
        var subject = SafeText(trace.SubjectReferenceId);
        var isRequest = ContainsAny(trace.EventKind, "tool.request", "request") || Matches(subject, request.ToolRequestId);
        return new ToolGateCorrelationEvidence
        {
            ToolGateDecisionId = isRequest ? SafeText(request.ToolGateDecisionId) : subject,
            ToolRequestId = isRequest ? subject : SafeText(request.ToolRequestId),
            GateKind = SafeText(trace.EventKind),
            SafeSummary = EvidenceSummary("Tool gate evidence", trace),
            WorkflowRunId = SafeText(trace.WorkflowRunId),
            WorkflowStepId = SafeText(trace.WorkflowStepId),
            CorrelationId = SafeText(trace.CorrelationId),
            RecordedUtc = trace.RecordedUtc,
            IsEvidenceOnly = true,
            OpensGate = false,
            InvokesTool = false,
            SatisfiesPolicy = false,
            TransitionsWorkflow = false
        };
    }

    private static DogfoodCorrelationEvidence ToDogfoodEvidence(GovernanceTraceSummary trace) =>
        new()
        {
            DogfoodReceiptId = SafeText(trace.SubjectReferenceId),
            DogfoodKind = SafeText(trace.EventKind),
            SafeSummary = EvidenceSummary("Dogfood receipt evidence", trace),
            WorkflowRunId = SafeText(trace.WorkflowRunId),
            WorkflowStepId = SafeText(trace.WorkflowStepId),
            CorrelationId = SafeText(trace.CorrelationId),
            RecordedUtc = trace.RecordedUtc,
            IsEvidenceOnly = true,
            IsReleaseApproval = false,
            MarksDogfoodPassed = false,
            SatisfiesPolicy = false,
            TransitionsWorkflow = false
        };

    private static GovernanceCorrelationTraceReference ToTraceReference(GovernanceTraceSummary trace) =>
        new()
        {
            TraceId = SafeText(trace.TraceId),
            EventKind = SafeText(trace.EventKind),
            SafeSummary = SafeText(trace.SafeSummary),
            CorrelationId = SafeText(trace.CorrelationId),
            CausationId = SafeText(trace.CausationId),
            RecordedUtc = trace.RecordedUtc
        };

    private static IEnumerable<GovernanceCorrelationMissingEvidence> BuildMissingEvidence(
        ApprovalGateDogfoodCorrelationReportRequest request,
        IReadOnlyList<GovernanceTraceSummary> traces,
        IReadOnlyList<ApprovalCorrelationEvidence> approvalEvidence,
        IReadOnlyList<ToolGateCorrelationEvidence> toolGateEvidence,
        IReadOnlyList<DogfoodCorrelationEvidence> dogfoodEvidence,
        bool policyEvidencePresent)
    {
        if (traces.Count == 0)
            yield return Missing("trace-evidence", GovernanceCorrelationMissingEvidenceKind.MissingTraceEvidence, "No governance trace evidence was found for the requested selectors.");

        if (approvalEvidence.Count == 0)
            yield return Missing("approval-evidence", GovernanceCorrelationMissingEvidenceKind.MissingApprovalEvidence, "Approval evidence was not found in the correlated trace. This does not deny or grant approval.");

        if (!policyEvidencePresent)
            yield return Missing("policy-evidence", GovernanceCorrelationMissingEvidenceKind.MissingPolicyEvidence, "Policy evidence was not found in the correlated trace. This does not satisfy or fail policy.");

        if (toolGateEvidence.Count == 0)
            yield return Missing("tool-gate-evidence", GovernanceCorrelationMissingEvidenceKind.MissingToolGateEvidence, "Tool gate evidence was not found in the correlated trace. This does not open or close a gate.");

        if (dogfoodEvidence.Count == 0)
            yield return Missing("dogfood-receipt", GovernanceCorrelationMissingEvidenceKind.MissingDogfoodReceipt, "Dogfood receipt evidence was not found in the correlated trace. This does not pass or fail dogfood.");

        if (!string.IsNullOrWhiteSpace(request.WorkflowRunId) && traces.All(trace => string.IsNullOrWhiteSpace(trace.WorkflowRunId) && !Matches(trace.SubjectReferenceId, request.WorkflowRunId)))
            yield return Missing("workflow-reference", GovernanceCorrelationMissingEvidenceKind.MissingWorkflowReference, "Requested workflow reference was not present in correlated trace evidence.");

        if (string.IsNullOrWhiteSpace(request.CorrelationId) && traces.All(trace => string.IsNullOrWhiteSpace(trace.CorrelationId)))
            yield return Missing("correlation-reference", GovernanceCorrelationMissingEvidenceKind.MissingCorrelationReference, "No correlation id was present in the returned trace evidence.");
    }

    private static IEnumerable<GovernanceCorrelationConflictSignal> BuildConflicts(
        IReadOnlyList<GovernanceTraceSummary> traces,
        IReadOnlyList<ApprovalCorrelationEvidence> approvalEvidence,
        IReadOnlyList<ToolGateCorrelationEvidence> toolGateEvidence,
        IReadOnlyList<DogfoodCorrelationEvidence> dogfoodEvidence,
        bool policyEvidencePresent)
    {
        if (dogfoodEvidence.Count > 0 && approvalEvidence.Count == 0)
            yield return Conflict("missing-approval-with-dogfood", GovernanceCorrelationConflictKind.ApprovalEvidenceMissingButDogfoodPresent, "Dogfood receipt evidence exists, but approval evidence was not found. This is a review signal, not a verdict.");

        if (dogfoodEvidence.Count > 0 && toolGateEvidence.Any(evidence => ContainsAny(evidence.GateKind, "blocked", "denied", "rejected") || ContainsAny(evidence.SafeSummary, "blocked", "denied", "rejected")))
            yield return Conflict("blocked-gate-with-dogfood", GovernanceCorrelationConflictKind.ToolGateBlockedButDogfoodPresent, "Dogfood receipt evidence exists while a tool gate appears blocked. This does not reopen the gate or reject dogfood.");

        if (!policyEvidencePresent && traces.Any(trace => ContainsAny(trace.EventKind, "workflow.transition", "workflow.continued", "workflow.completed", "workflow.advanced")))
            yield return Conflict("missing-policy-with-workflow-advance", GovernanceCorrelationConflictKind.PolicyEvidenceMissingButWorkflowAdvanced, "Workflow advancement evidence exists, but policy evidence was not found. This does not undo or continue the workflow.");

        if (traces.Select(trace => trace.CorrelationId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            yield return Conflict("conflicting-correlation-references", GovernanceCorrelationConflictKind.ConflictingCorrelationReferences, "Multiple correlation ids appear in the report evidence. This requires review before acting.");
    }

    private static IEnumerable<GovernanceCorrelationRecommendation> BuildRecommendations(
        IReadOnlyList<GovernanceTraceSummary> traces,
        IReadOnlyList<GovernanceCorrelationMissingEvidence> missingEvidence,
        IReadOnlyList<GovernanceCorrelationConflictSignal> conflicts)
    {
        var traceIds = traces.Select(trace => SafeText(trace.TraceId)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        yield return Recommendation("review-correlation-trace", "Review the correlated governance trace evidence before taking any separate action.", traceIds);

        if (missingEvidence.Any(item => item.Kind is GovernanceCorrelationMissingEvidenceKind.MissingApprovalEvidence))
            yield return Recommendation("review-approval-evidence", "Check for a separate approval decision record. This report cannot create one.", traceIds);

        if (missingEvidence.Any(item => item.Kind is GovernanceCorrelationMissingEvidenceKind.MissingPolicyEvidence))
            yield return Recommendation("review-policy-evidence", "Check for a separate policy decision event. This report cannot satisfy policy.", traceIds);

        if (missingEvidence.Any(item => item.Kind is GovernanceCorrelationMissingEvidenceKind.MissingToolGateEvidence))
            yield return Recommendation("review-tool-gate-evidence", "Check the tool gate decision record before considering any future execution.", traceIds);

        if (missingEvidence.Any(item => item.Kind is GovernanceCorrelationMissingEvidenceKind.MissingDogfoodReceipt) || conflicts.Count > 0)
            yield return Recommendation("review-dogfood-evidence", "Check dogfood receipt evidence separately. This report cannot mark dogfood passed or approve release.", traceIds);
    }

    private static GovernanceCorrelationMissingEvidence Missing(string id, GovernanceCorrelationMissingEvidenceKind kind, string summary) =>
        new()
        {
            MissingEvidenceId = id,
            Kind = kind,
            SafeSummary = SafeText(summary)
        };

    private static GovernanceCorrelationConflictSignal Conflict(string id, GovernanceCorrelationConflictKind kind, string summary) =>
        new()
        {
            ConflictId = id,
            Kind = kind,
            SafeSummary = SafeText(summary),
            IsVerdict = false,
            CanResolve = false
        };

    private static GovernanceCorrelationRecommendation Recommendation(string id, string summary, IReadOnlyList<string> traceIds) =>
        new()
        {
            RecommendationId = id,
            SafeSummary = SafeText(summary),
            SupportingReferenceIds = traceIds,
            IsInvestigationOnly = true,
            CanMutateState = false,
            CanApprove = false,
            CanOpenGate = false,
            CanApproveRelease = false
        };

    private static IReadOnlyList<string> SummaryLines(
        IReadOnlyList<GovernanceTraceSummary> traces,
        IReadOnlyList<ApprovalCorrelationEvidence> approvals,
        IReadOnlyList<ToolGateCorrelationEvidence> gates,
        IReadOnlyList<DogfoodCorrelationEvidence> dogfood,
        IReadOnlyList<GovernanceCorrelationMissingEvidence> missing,
        IReadOnlyList<GovernanceCorrelationConflictSignal> conflicts) =>
        [
            SafeText($"Correlated governance trace references: {traces.Count}."),
            SafeText($"Approval evidence references: {approvals.Count}."),
            SafeText($"Tool gate evidence references: {gates.Count}."),
            SafeText($"Dogfood receipt references: {dogfood.Count}."),
            SafeText($"Missing evidence signals: {missing.Count}."),
            SafeText($"Conflict signals: {conflicts.Count}."),
            "This report is evidence only and grants no approval, policy satisfaction, gate opening, dogfood pass, release approval, workflow transition, or execution permission."
        ];

    private static string ReportId(ApprovalGateDogfoodCorrelationReportRequest request)
    {
        var basis = FirstNonEmpty(request.CorrelationId, request.CausationId, request.ProjectReferenceId, request.WorkflowRunId, "unscoped");
        return SafeText($"approval-gate-dogfood-correlation-{basis}");
    }

    private static string EvidenceSummary(string prefix, GovernanceTraceSummary trace) =>
        SafeText($"{prefix} observed through governance trace event '{trace.EventKind}' for subject '{trace.SubjectReferenceId}'.");

    private static bool IsApprovalTrace(GovernanceTraceSummary trace, ApprovalGateDogfoodCorrelationReportRequest request) =>
        Matches(trace.SubjectReferenceId, request.ApprovalReferenceId) || ContainsAny(TraceText(trace), "approval", "approved", "revoked", "expired");

    private static bool IsToolGateTrace(GovernanceTraceSummary trace, ApprovalGateDogfoodCorrelationReportRequest request) =>
        Matches(trace.SubjectReferenceId, request.ToolRequestId) ||
        Matches(trace.SubjectReferenceId, request.ToolGateDecisionId) ||
        ContainsAny(TraceText(trace), "tool.gate", "tool gate", "gate.decision", "gate decision", "tool.request", "tool request");

    private static bool IsDogfoodTrace(GovernanceTraceSummary trace, ApprovalGateDogfoodCorrelationReportRequest request) =>
        Matches(trace.SubjectReferenceId, request.DogfoodReceiptId) || ContainsAny(TraceText(trace), "dogfood");

    private static bool IsPolicyTrace(GovernanceTraceSummary trace) =>
        ContainsAny(TraceText(trace), "policy", "policy.decision", "policy satisfied", "policy blocked");

    private static string TraceText(GovernanceTraceSummary trace) =>
        $"{trace.EventKind} {trace.SourceComponent} {trace.SubjectReferenceId} {trace.SafeSummary}";

    private static string SafeText(string? value) => ApprovalGateDogfoodCorrelationReportValidator.SafeText(value);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            ApprovalGateDogfoodCorrelationReportValidator.NormalizeText(left),
            ApprovalGateDogfoodCorrelationReportValidator.NormalizeText(right),
            StringComparison.OrdinalIgnoreCase);
}

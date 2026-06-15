namespace IronDev.Core.Governance;

public enum GovernanceTraceExplorerStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoTraceFound = 2,
    TraceFound = 3,
    TraceListReturned = 4
}

public sealed record GovernanceTraceQuery
{
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string SubjectReferenceId { get; init; } = string.Empty;
    public string EventKind { get; init; } = string.Empty;
    public string SourceComponent { get; init; } = string.Empty;
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int Take { get; init; } = GovernanceTraceExplorerValidator.DefaultTake;
    public bool IncludeRelated { get; init; }
}

public sealed record GovernanceTraceSummary
{
    public required string TraceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string CorrelationId { get; init; }
    public required string CausationId { get; init; }
    public required string SubjectReferenceId { get; init; }
    public required string EventKind { get; init; }
    public required string SourceComponent { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }

    public required bool IsReadOnlyTrace { get; init; }
    public required bool IsAuthorityDecision { get; init; }
    public required bool IsApproval { get; init; }
    public required bool IsPolicySatisfaction { get; init; }
    public required bool IsWorkflowTransition { get; init; }
    public required bool CanApprove { get; init; }
    public required bool CanReject { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanApplySource { get; init; }
}

public sealed record GovernanceTraceDetail
{
    public required GovernanceTraceSummary Summary { get; init; }
    public required IReadOnlyList<GovernanceTraceTimelineItem> Timeline { get; init; }
    public required IReadOnlyList<GovernanceTraceRelatedReference> RelatedReferences { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record GovernanceTraceTimelineItem
{
    public required string EventId { get; init; }
    public required string EventKind { get; init; }
    public required string SourceComponent { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string SubjectReferenceId { get; init; } = string.Empty;
}

public sealed record GovernanceTraceRelatedReference
{
    public required string ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record GovernanceTraceListResponse
{
    public required GovernanceTraceExplorerStatus Status { get; init; }
    public required IReadOnlyList<GovernanceTraceSummary> Traces { get; init; }
    public required IReadOnlyList<GovernanceTraceExplorerIssue> Issues { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record GovernanceTraceDetailResponse
{
    public required GovernanceTraceExplorerStatus Status { get; init; }
    public GovernanceTraceDetail? Trace { get; init; }
    public required IReadOnlyList<GovernanceTraceExplorerIssue> Issues { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record GovernanceTraceExplorerIssue
{
    public required GovernanceTraceExplorerIssueKind Kind { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public enum GovernanceTraceExplorerIssueKind
{
    Unknown = 0,
    MissingTraceId = 1,
    MissingCorrelationId = 2,
    MissingWorkflowRunId = 3,
    InvalidDateRange = 4,
    UnsafeQueryText = 5,
    MissingProjectReferenceId = 6,
    InvalidProjectReferenceId = 7,
    InvalidTraceId = 8,
    InvalidCorrelationId = 9,
    InvalidCausationId = 10,
    InvalidTake = 11
}

public static class GovernanceTraceExplorerBoundaries
{
    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Governance trace explorer is read-only.",
        "Trace output is not approval.",
        "Trace output is not policy satisfaction.",
        "Trace output is not workflow transition.",
        "Trace output is not tool invocation.",
        "Trace output is not agent dispatch.",
        "Trace output is not model execution.",
        "Trace output is not memory promotion.",
        "Trace output is not source apply.",
        "Trace output must not expose hidden/private reasoning."
    ];
}

public sealed class GovernanceTraceExplorerValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const string RedactedUnsafeText = "[redacted governance trace text]";

    private static readonly string[] UnsafeMarkers =
    [
        "raw prompt",
        "rawprompt",
        "fullprompt",
        "raw completion",
        "rawcompletion",
        "fullcompletion",
        "raw tool output",
        "rawtooloutput",
        "raw command output",
        "rawcommandoutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "privatereasoning",
        "hidden reasoning",
        "hidden deliberation",
        "payloadjson",
        "source content",
        "sourcecontent",
        "source file contents",
        "patch payload",
        "patchpayload",
        "entirepatch",
        "password",
        "api_key",
        "apikey",
        "secret",
        "credential",
        "bearer "
    ];

    public IReadOnlyList<GovernanceTraceExplorerIssue> ValidateQuery(GovernanceTraceQuery? query)
    {
        var issues = new List<GovernanceTraceExplorerIssue>();
        if (query is null)
        {
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.MissingProjectReferenceId, "query", "Governance trace query is required."));
            return issues;
        }

        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc > query.ToUtc)
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.InvalidDateRange, nameof(query.FromUtc), "fromUtc must be before toUtc."));

        if (query.Take < 1 || query.Take > MaxTake)
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.InvalidTake, nameof(query.Take), $"take must be between 1 and {MaxTake}."));

        ValidateText(query.ProjectReferenceId, nameof(query.ProjectReferenceId), issues);
        ValidateText(query.WorkflowRunId, nameof(query.WorkflowRunId), issues);
        ValidateText(query.WorkflowStepId, nameof(query.WorkflowStepId), issues);
        ValidateText(query.CorrelationId, nameof(query.CorrelationId), issues);
        ValidateText(query.CausationId, nameof(query.CausationId), issues);
        ValidateText(query.SubjectReferenceId, nameof(query.SubjectReferenceId), issues);
        ValidateText(query.EventKind, nameof(query.EventKind), issues);
        ValidateText(query.SourceComponent, nameof(query.SourceComponent), issues);

        if (!string.IsNullOrWhiteSpace(query.ProjectReferenceId) && !Guid.TryParse(query.ProjectReferenceId, out _))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.InvalidProjectReferenceId, nameof(query.ProjectReferenceId), "projectReferenceId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(query.CorrelationId) && !Guid.TryParse(query.CorrelationId, out _))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.InvalidCorrelationId, nameof(query.CorrelationId), "correlationId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(query.CausationId) && !Guid.TryParse(query.CausationId, out _))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.InvalidCausationId, nameof(query.CausationId), "causationId must be a GUID."));

        if (string.IsNullOrWhiteSpace(query.ProjectReferenceId) &&
            string.IsNullOrWhiteSpace(query.CorrelationId) &&
            string.IsNullOrWhiteSpace(query.CausationId))
        {
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.MissingProjectReferenceId, nameof(query.ProjectReferenceId), "projectReferenceId, correlationId, or causationId is required."));
        }

        return issues;
    }

    public IReadOnlyList<GovernanceTraceExplorerIssue> ValidateTraceId(string? traceId)
    {
        var issues = new List<GovernanceTraceExplorerIssue>();
        ValidateRequiredGuid(traceId, nameof(traceId), GovernanceTraceExplorerIssueKind.MissingTraceId, GovernanceTraceExplorerIssueKind.InvalidTraceId, issues);
        ValidateText(traceId, nameof(traceId), issues);
        return issues;
    }

    public IReadOnlyList<GovernanceTraceExplorerIssue> ValidateCorrelationId(string? correlationId)
    {
        var issues = new List<GovernanceTraceExplorerIssue>();
        ValidateRequiredGuid(correlationId, nameof(correlationId), GovernanceTraceExplorerIssueKind.MissingCorrelationId, GovernanceTraceExplorerIssueKind.InvalidCorrelationId, issues);
        ValidateText(correlationId, nameof(correlationId), issues);
        return issues;
    }

    public IReadOnlyList<GovernanceTraceExplorerIssue> ValidateWorkflowRunId(string? workflowRunId, string? projectReferenceId)
    {
        var issues = new List<GovernanceTraceExplorerIssue>();
        if (string.IsNullOrWhiteSpace(workflowRunId))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.MissingWorkflowRunId, nameof(workflowRunId), "workflowRunId is required."));

        if (string.IsNullOrWhiteSpace(projectReferenceId))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.MissingProjectReferenceId, nameof(projectReferenceId), "projectReferenceId is required for workflow-run trace lookup."));
        else if (!Guid.TryParse(projectReferenceId, out _))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.InvalidProjectReferenceId, nameof(projectReferenceId), "projectReferenceId must be a GUID."));

        ValidateText(workflowRunId, nameof(workflowRunId), issues);
        ValidateText(projectReferenceId, nameof(projectReferenceId), issues);
        return issues;
    }

    public static GovernanceTraceExplorerIssue Issue(GovernanceTraceExplorerIssueKind kind, string field, string message) =>
        new()
        {
            Kind = kind,
            Field = field,
            Message = message
        };

    public static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public static string SafeText(string? value)
    {
        var normalized = NormalizeText(value);
        return ContainsUnsafeText(normalized) ? RedactedUnsafeText : normalized;
    }

    public static bool ContainsUnsafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void ValidateRequiredGuid(
        string? value,
        string field,
        GovernanceTraceExplorerIssueKind missingKind,
        GovernanceTraceExplorerIssueKind invalidKind,
        List<GovernanceTraceExplorerIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Issue(missingKind, field, $"{field} is required."));
            return;
        }

        if (!Guid.TryParse(value, out _))
            issues.Add(Issue(invalidKind, field, $"{field} must be a GUID."));
    }

    private static void ValidateText(string? value, string field, List<GovernanceTraceExplorerIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(Issue(GovernanceTraceExplorerIssueKind.UnsafeQueryText, field, "Governance trace explorer query contains unsupported trace text."));
    }
}

using IronDev.Core.Governance;

namespace IronDev.Core.Agents;

public enum AgentRunHealthSummaryStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoAgentRunEvidenceFound = 2,
    SummaryAvailable = 3
}

public enum AgentRunHealthCategory
{
    Unknown = 0,
    ObservedHealthy = 1,
    ObservedWarning = 2,
    ObservedBlocked = 3,
    ObservedFailed = 4,
    EvidenceIncomplete = 5,
    NeedsHumanReview = 6
}

public enum AgentRunHealthSignalKind
{
    Unknown = 0,
    AgentRunObserved = 1,
    AgentRunCompleted = 2,
    AgentRunFailed = 3,
    WorkflowBlocked = 4,
    ApprovalRequired = 5,
    PolicyRequired = 6,
    ToolGateObserved = 7,
    ToolGateBlocked = 8,
    DogfoodReceiptObserved = 9,
    MissingEvidence = 10,
    NeedsHumanReview = 11
}

public enum AgentRunHealthSignalSeverity
{
    Unknown = 0,
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum AgentRunHealthSummaryIssueKind
{
    Unknown = 0,
    MissingSelector = 1,
    InvalidProjectReferenceId = 2,
    InvalidCorrelationId = 3,
    InvalidDateRange = 4,
    InvalidTake = 5,
    UnsafeQueryText = 6,
    TraceExplorerError = 7
}

public sealed record AgentRunHealthSummaryRequest
{
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string AgentRunId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentKind { get; init; } = string.Empty;
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int Take { get; init; } = AgentRunHealthSummaryValidator.DefaultTake;
    public bool IncludeGateSignals { get; init; } = true;
    public bool IncludeApprovalSignals { get; init; } = true;
    public bool IncludePolicySignals { get; init; } = true;
    public bool IncludeDogfoodSignals { get; init; } = true;
}

public sealed record AgentRunHealthSummaryResponse
{
    public required AgentRunHealthSummaryStatus Status { get; init; }
    public AgentRunHealthSummary? Summary { get; init; }
    public required IReadOnlyList<AgentRunHealthSummaryIssue> Issues { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record AgentRunHealthSummary
{
    public required string SummaryId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string AgentRunId { get; init; }
    public required string CorrelationId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentKind { get; init; }
    public required DateTimeOffset GeneratedUtc { get; init; }
    public required AgentRunHealthCategory HealthCategory { get; init; }
    public required int TraceCount { get; init; }
    public required int CriticalSignalCount { get; init; }
    public required int WarningSignalCount { get; init; }
    public required int MissingEvidenceCount { get; init; }
    public required IReadOnlyList<AgentRunHealthSignal> Signals { get; init; }
    public required IReadOnlyList<AgentRunHealthMissingEvidence> MissingEvidence { get; init; }
    public required IReadOnlyList<AgentRunHealthTraceReference> TraceReferences { get; init; }
    public required IReadOnlyList<string> Recommendations { get; init; }
    public required AgentRunHealthSummaryBoundary Boundary { get; init; }
}

public sealed record AgentRunHealthSignal
{
    public required AgentRunHealthSignalKind Kind { get; init; }
    public required AgentRunHealthSignalSeverity Severity { get; init; }
    public required string ReferenceId { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
}

public sealed record AgentRunHealthMissingEvidence
{
    public required string EvidenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record AgentRunHealthTraceReference
{
    public required string TraceId { get; init; }
    public required string EventKind { get; init; }
    public required string SubjectReferenceId { get; init; }
    public required string SourceComponent { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
}

public sealed record AgentRunHealthSummaryBoundary
{
    public bool ReadOnlySummary { get; init; } = true;
    public bool SummaryIsApproval { get; init; }
    public bool SummaryIsPolicySatisfaction { get; init; }
    public bool SummaryIsExecutionPermission { get; init; }
    public bool SummaryIsReleaseApproval { get; init; }
    public bool SummaryCanStartWorkflow { get; init; }
    public bool SummaryCanResumeWorkflow { get; init; }
    public bool SummaryCanRestartAgent { get; init; }
    public bool SummaryCanRetryAgent { get; init; }
    public bool SummaryCanDispatchAgent { get; init; }
    public bool SummaryCanInvokeTool { get; init; }
    public bool SummaryCanCallModel { get; init; }
    public bool SummaryCanCreateTicket { get; init; }
    public bool SummaryCanMutateSource { get; init; }
    public bool SummaryCanApplyPatch { get; init; }
    public bool SummaryCanPromoteMemory { get; init; }
    public bool SummaryCanActivateRetrieval { get; init; }
    public bool CreatesGovernanceEvent { get; init; }
    public bool CreatesApprovalDecision { get; init; }
    public bool CreatesPolicyDecision { get; init; }
    public bool CreatesToolRequest { get; init; }
    public bool CreatesDogfoodReceipt { get; init; }
    public bool ExposesRawPayloadJson { get; init; }
    public bool ExposesRawPrompt { get; init; }
    public bool ExposesRawCompletion { get; init; }
    public bool ExposesRawToolOutput { get; init; }
    public bool ExposesSourceContent { get; init; }
    public bool ExposesPatchPayload { get; init; }
    public bool ExposesPrivateReasoning { get; init; }
}

public sealed record AgentRunHealthSummaryIssue
{
    public required AgentRunHealthSummaryIssueKind Kind { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public static class AgentRunHealthSummaryBoundaries
{
    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Agent run health summary is read-only.",
        "Health summary output is not approval.",
        "Health summary output is not policy satisfaction.",
        "Health summary output is not execution permission.",
        "Health summary output is not workflow transition.",
        "Health summary output is not tool invocation.",
        "Health summary output is not agent dispatch.",
        "Health summary output is not model execution.",
        "Health summary output is not memory promotion.",
        "Health summary output is not source apply.",
        "Health summary output must not expose raw payloads, prompts, completions, tool output, source, patches, or hidden/private reasoning."
    ];
}

public sealed class AgentRunHealthSummaryValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const string RedactedUnsafeText = "[redacted agent run health text]";

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
        "entire patch",
        "password",
        "api_key",
        "apikey",
        "secret",
        "credential",
        "bearer "
    ];

    public IReadOnlyList<AgentRunHealthSummaryIssue> Validate(AgentRunHealthSummaryRequest? request)
    {
        var issues = new List<AgentRunHealthSummaryIssue>();
        if (request is null)
        {
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.MissingSelector, "request", "Agent run health summary request is required."));
            return issues;
        }

        if (request.FromUtc.HasValue && request.ToUtc.HasValue && request.FromUtc > request.ToUtc)
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.InvalidDateRange, nameof(request.FromUtc), "fromUtc must be before toUtc."));

        if (request.Take < 1 || request.Take > MaxTake)
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.InvalidTake, nameof(request.Take), $"take must be between 1 and {MaxTake}."));

        ValidateText(request.ProjectReferenceId, nameof(request.ProjectReferenceId), issues);
        ValidateText(request.AgentRunId, nameof(request.AgentRunId), issues);
        ValidateText(request.CorrelationId, nameof(request.CorrelationId), issues);
        ValidateText(request.WorkflowRunId, nameof(request.WorkflowRunId), issues);
        ValidateText(request.WorkflowStepId, nameof(request.WorkflowStepId), issues);
        ValidateText(request.AgentId, nameof(request.AgentId), issues);
        ValidateText(request.AgentKind, nameof(request.AgentKind), issues);

        if (!string.IsNullOrWhiteSpace(request.ProjectReferenceId) && !Guid.TryParse(request.ProjectReferenceId, out _))
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.InvalidProjectReferenceId, nameof(request.ProjectReferenceId), "projectReferenceId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) && !Guid.TryParse(request.CorrelationId, out _))
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.InvalidCorrelationId, nameof(request.CorrelationId), "correlationId must be a GUID."));

        if (string.IsNullOrWhiteSpace(request.ProjectReferenceId) &&
            string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.MissingSelector, nameof(request.ProjectReferenceId), "projectReferenceId or correlationId is required."));
        }

        return issues;
    }

    public AgentRunHealthSummaryRequest Normalize(AgentRunHealthSummaryRequest request) =>
        request with
        {
            ProjectReferenceId = NormalizeText(request.ProjectReferenceId),
            AgentRunId = NormalizeText(request.AgentRunId),
            CorrelationId = NormalizeText(request.CorrelationId),
            WorkflowRunId = NormalizeText(request.WorkflowRunId),
            WorkflowStepId = NormalizeText(request.WorkflowStepId),
            AgentId = NormalizeText(request.AgentId),
            AgentKind = NormalizeText(request.AgentKind),
            Take = Math.Clamp(request.Take, 1, MaxTake)
        };

    public static AgentRunHealthSummaryIssue Issue(AgentRunHealthSummaryIssueKind kind, string field, string message) =>
        new()
        {
            Kind = kind,
            Field = field,
            Message = SafeText(message)
        };

    public static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public static string SafeText(string? value)
    {
        var normalized = NormalizeText(value);
        return ContainsUnsafeText(normalized) ? RedactedUnsafeText : normalized;
    }

    public static bool ContainsUnsafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void ValidateText(string? value, string field, List<AgentRunHealthSummaryIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(Issue(AgentRunHealthSummaryIssueKind.UnsafeQueryText, field, "Agent run health summary query contains unsupported private or raw text."));
    }
}

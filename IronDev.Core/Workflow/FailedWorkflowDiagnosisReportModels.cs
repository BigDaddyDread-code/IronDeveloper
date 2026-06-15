using IronDev.Core.Governance;

namespace IronDev.Core.Workflow;

public enum FailedWorkflowDiagnosisReportStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoWorkflowEvidenceFound = 2,
    NoFailureEvidenceFound = 3,
    ReportAvailable = 4
}

public enum FailedWorkflowDiagnosisSignalKind
{
    Unknown = 0,
    WorkflowFailureObserved = 1,
    WorkflowBlockedObserved = 2,
    StepFailureObserved = 3,
    StepBlockedObserved = 4,
    ApprovalEvidenceMissing = 5,
    PolicyEvidenceMissing = 6,
    ToolGateBlocked = 7,
    ValidationFailureObserved = 8,
    ExceptionObserved = 9,
    TimeoutObserved = 10,
    GovernanceHaltObserved = 11
}

public enum FailedWorkflowDiagnosisHypothesisKind
{
    Unknown = 0,
    WorkflowFailedOrBlocked = 1,
    ApprovalEvidenceMayBeMissing = 2,
    PolicyEvidenceMayBeMissing = 3,
    ToolGateMayHaveBlockedProgress = 4,
    ValidationMayHaveFailed = 5,
    RuntimeExceptionOrTimeoutObserved = 6,
    InsufficientFailureEvidence = 7
}

public enum FailedWorkflowDiagnosisIssueKind
{
    Unknown = 0,
    MissingWorkflowRunId = 1,
    MissingTraceSelector = 2,
    InvalidProjectReferenceId = 3,
    InvalidCorrelationId = 4,
    InvalidTakeTraceItems = 5,
    UnsafeQueryText = 6
}

public sealed record FailedWorkflowDiagnosisReportRequest
{
    public required string WorkflowRunId { get; init; }
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public bool IncludeTraceTimeline { get; init; }
    public bool IncludeRecommendations { get; init; }
    public int TakeTraceItems { get; init; } = FailedWorkflowDiagnosisReportValidator.DefaultTake;
}

public sealed record FailedWorkflowDiagnosisReportResponse
{
    public required FailedWorkflowDiagnosisReportStatus Status { get; init; }
    public FailedWorkflowDiagnosisReport? Report { get; init; }
    public required IReadOnlyList<FailedWorkflowDiagnosisReportIssue> Issues { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record FailedWorkflowDiagnosisReport
{
    public required string ReportId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public required DateTimeOffset GeneratedUtc { get; init; }
    public required bool IsReportOnly { get; init; }
    public required bool IsRootCauseProof { get; init; }
    public required bool CanRepair { get; init; }
    public required bool CanRetryWorkflow { get; init; }
    public required bool CanResumeWorkflow { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanApprove { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool ContainsUnsafeReasoning { get; init; }
    public required IReadOnlyList<FailedWorkflowDiagnosisSignal> Signals { get; init; }
    public required IReadOnlyList<FailedWorkflowDiagnosisHypothesis> Hypotheses { get; init; }
    public required IReadOnlyList<FailedWorkflowDiagnosisMissingEvidence> MissingEvidence { get; init; }
    public required IReadOnlyList<FailedWorkflowDiagnosisTraceItem> TraceTimeline { get; init; }
    public required IReadOnlyList<FailedWorkflowDiagnosisRecommendation> Recommendations { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record FailedWorkflowDiagnosisSignal
{
    public required FailedWorkflowDiagnosisSignalKind Kind { get; init; }
    public required string EvidenceId { get; init; }
    public required string EventKind { get; init; }
    public required string SourceComponent { get; init; }
    public required string SafeSummary { get; init; }
    public required decimal Confidence { get; init; }
    public required bool IsRootCauseProof { get; init; }
    public required bool CanRepair { get; init; }
    public required bool CanRetryWorkflow { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
}

public sealed record FailedWorkflowDiagnosisHypothesis
{
    public required FailedWorkflowDiagnosisHypothesisKind Kind { get; init; }
    public required string SafeSummary { get; init; }
    public required decimal Confidence { get; init; }
    public required IReadOnlyList<string> SupportingTraceIds { get; init; }
    public required bool IsRootCauseProof { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required bool CanRepair { get; init; }
    public required bool CanApprove { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
}

public sealed record FailedWorkflowDiagnosisMissingEvidence
{
    public required string EvidenceKind { get; init; }
    public required string EvidenceId { get; init; }
    public required string SafeSummary { get; init; }
    public required bool IsRequirementOnly { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool AllowsExecution { get; init; }
}

public sealed record FailedWorkflowDiagnosisTraceItem
{
    public required string TraceId { get; init; }
    public required string EventKind { get; init; }
    public required string SourceComponent { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string SubjectReferenceId { get; init; } = string.Empty;
    public required bool IsEvidenceOnly { get; init; }
}

public sealed record FailedWorkflowDiagnosisRecommendation
{
    public required string RecommendationId { get; init; }
    public required string SafeSummary { get; init; }
    public required IReadOnlyList<string> SupportingTraceIds { get; init; }
    public required bool IsInvestigationOnly { get; init; }
    public required bool IsExecutableWorkflowStep { get; init; }
    public required bool CanRepair { get; init; }
    public required bool CanRetryWorkflow { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanMutateState { get; init; }
}

public sealed record FailedWorkflowDiagnosisReportIssue
{
    public required FailedWorkflowDiagnosisIssueKind Kind { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public static class FailedWorkflowDiagnosisReportBoundaries
{
    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Failed workflow diagnosis report is read-only operational evidence.",
        "Diagnosis report is not root-cause proof.",
        "Diagnosis report is not a repair, retry, resume, rerun, workflow transition, or ticket creation.",
        "Diagnosis report is not approval, policy satisfaction, execution permission, governance decision, or release approval.",
        "Diagnosis report does not invoke tools, dispatch agents, call models, apply source, apply patches, promote memory, or activate retrieval.",
        "Diagnosis report must not expose hidden/private reasoning, raw prompts, raw completions, raw tool output, source content, or patch payloads."
    ];
}

public sealed class FailedWorkflowDiagnosisReportValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const string RedactedUnsafeText = "[redacted failed workflow diagnosis text]";

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

    public IReadOnlyList<FailedWorkflowDiagnosisReportIssue> Validate(FailedWorkflowDiagnosisReportRequest? request)
    {
        var issues = new List<FailedWorkflowDiagnosisReportIssue>();
        if (request is null)
        {
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.MissingWorkflowRunId, "request", "Failed workflow diagnosis report request is required."));
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.MissingWorkflowRunId, nameof(request.WorkflowRunId), "workflowRunId is required."));

        if (string.IsNullOrWhiteSpace(request.ProjectReferenceId) && string.IsNullOrWhiteSpace(request.CorrelationId))
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.MissingTraceSelector, nameof(request.ProjectReferenceId), "projectReferenceId or correlationId is required for diagnosis trace lookup."));

        if (!string.IsNullOrWhiteSpace(request.ProjectReferenceId) && !Guid.TryParse(request.ProjectReferenceId, out _))
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.InvalidProjectReferenceId, nameof(request.ProjectReferenceId), "projectReferenceId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) && !Guid.TryParse(request.CorrelationId, out _))
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.InvalidCorrelationId, nameof(request.CorrelationId), "correlationId must be a GUID."));

        if (request.TakeTraceItems < 1 || request.TakeTraceItems > MaxTake)
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.InvalidTakeTraceItems, nameof(request.TakeTraceItems), $"takeTraceItems must be between 1 and {MaxTake}."));

        ValidateText(request.WorkflowRunId, nameof(request.WorkflowRunId), issues);
        ValidateText(request.ProjectReferenceId, nameof(request.ProjectReferenceId), issues);
        ValidateText(request.WorkflowStepId, nameof(request.WorkflowStepId), issues);
        ValidateText(request.CorrelationId, nameof(request.CorrelationId), issues);
        return issues;
    }

    public FailedWorkflowDiagnosisReportRequest Normalize(FailedWorkflowDiagnosisReportRequest request) =>
        request with
        {
            WorkflowRunId = NormalizeText(request.WorkflowRunId),
            ProjectReferenceId = NormalizeText(request.ProjectReferenceId),
            WorkflowStepId = NormalizeText(request.WorkflowStepId),
            CorrelationId = NormalizeText(request.CorrelationId),
            TakeTraceItems = Math.Clamp(request.TakeTraceItems, 1, MaxTake)
        };

    public static FailedWorkflowDiagnosisReportIssue Issue(FailedWorkflowDiagnosisIssueKind kind, string field, string message) =>
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

    private static void ValidateText(string? value, string field, List<FailedWorkflowDiagnosisReportIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(Issue(FailedWorkflowDiagnosisIssueKind.UnsafeQueryText, field, "Failed workflow diagnosis report request contains unsupported trace text."));
    }
}

namespace IronDev.Core.Governance;

public enum ApprovalGateDogfoodCorrelationReportStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoEvidenceFound = 2,
    EvidenceIncomplete = 3,
    ReportAvailable = 4
}

public enum GovernanceCorrelationMissingEvidenceKind
{
    Unknown = 0,
    MissingApprovalEvidence = 1,
    MissingPolicyEvidence = 2,
    MissingToolGateEvidence = 3,
    MissingDogfoodReceipt = 4,
    MissingTraceEvidence = 5,
    MissingWorkflowReference = 6,
    MissingCorrelationReference = 7
}

public enum GovernanceCorrelationConflictKind
{
    Unknown = 0,
    ApprovalEvidenceMissingButDogfoodPresent = 1,
    ToolGateBlockedButDogfoodPresent = 2,
    PolicyEvidenceMissingButWorkflowAdvanced = 3,
    DogfoodReceiptPresentWithoutTrace = 4,
    ConflictingCorrelationReferences = 5
}

public enum ApprovalGateDogfoodCorrelationReportIssueKind
{
    Unknown = 0,
    MissingSelector = 1,
    InvalidProjectReferenceId = 2,
    InvalidWorkflowRunId = 3,
    InvalidWorkflowStepId = 4,
    InvalidCorrelationId = 5,
    InvalidCausationId = 6,
    InvalidDateRange = 7,
    InvalidTake = 8,
    UnsafeQueryText = 9
}

public sealed record ApprovalGateDogfoodCorrelationReportRequest
{
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string ApprovalReferenceId { get; init; } = string.Empty;
    public string ToolRequestId { get; init; } = string.Empty;
    public string ToolGateDecisionId { get; init; } = string.Empty;
    public string DogfoodReceiptId { get; init; } = string.Empty;
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int Take { get; init; } = ApprovalGateDogfoodCorrelationReportValidator.DefaultTake;
    public bool IncludeTraceReferences { get; init; } = true;
    public bool IncludeMissingEvidence { get; init; } = true;
    public bool IncludeRecommendations { get; init; } = true;
}

public sealed record ApprovalGateDogfoodCorrelationReportResponse
{
    public required ApprovalGateDogfoodCorrelationReportStatus Status { get; init; }
    public ApprovalGateDogfoodCorrelationReport? Report { get; init; }
    public required IReadOnlyList<ApprovalGateDogfoodCorrelationReportIssue> Issues { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record ApprovalGateDogfoodCorrelationReport
{
    public required string ReportId { get; init; }
    public required ApprovalGateDogfoodCorrelationReportStatus Status { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset GeneratedUtc { get; init; }
    public required IReadOnlyList<string> SafeSummaryLines { get; init; }
    public required IReadOnlyList<ApprovalCorrelationEvidence> ApprovalEvidence { get; init; }
    public required IReadOnlyList<ToolGateCorrelationEvidence> ToolGateEvidence { get; init; }
    public required IReadOnlyList<DogfoodCorrelationEvidence> DogfoodEvidence { get; init; }
    public required IReadOnlyList<GovernanceCorrelationTraceReference> TraceReferences { get; init; }
    public required IReadOnlyList<GovernanceCorrelationMissingEvidence> MissingEvidence { get; init; }
    public required IReadOnlyList<GovernanceCorrelationConflictSignal> ConflictSignals { get; init; }
    public required IReadOnlyList<GovernanceCorrelationRecommendation> Recommendations { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
    public required bool IsReportOnly { get; init; }
    public required bool IsApprovalDecision { get; init; }
    public required bool IsPolicySatisfaction { get; init; }
    public required bool IsToolGateMutation { get; init; }
    public required bool IsToolExecution { get; init; }
    public required bool IsDogfoodExecution { get; init; }
    public required bool IsReleaseApproval { get; init; }
    public required bool IsWorkflowTransition { get; init; }
    public required bool CanApprove { get; init; }
    public required bool CanReject { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanOpenGate { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanMarkDogfoodPassed { get; init; }
    public required bool CanApproveRelease { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanApplyPatch { get; init; }
}

public sealed record ApprovalCorrelationEvidence
{
    public required string ApprovalReferenceId { get; init; }
    public required string ApprovalKind { get; init; }
    public required string SafeSummary { get; init; }
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset? RecordedUtc { get; init; }
    public required bool IsEvidenceOnly { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransitionsWorkflow { get; init; }
}

public sealed record ToolGateCorrelationEvidence
{
    public required string ToolGateDecisionId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string GateKind { get; init; }
    public required string SafeSummary { get; init; }
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset? RecordedUtc { get; init; }
    public required bool IsEvidenceOnly { get; init; }
    public required bool OpensGate { get; init; }
    public required bool InvokesTool { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransitionsWorkflow { get; init; }
}

public sealed record DogfoodCorrelationEvidence
{
    public required string DogfoodReceiptId { get; init; }
    public required string DogfoodKind { get; init; }
    public required string SafeSummary { get; init; }
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset? RecordedUtc { get; init; }
    public required bool IsEvidenceOnly { get; init; }
    public required bool IsReleaseApproval { get; init; }
    public required bool MarksDogfoodPassed { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransitionsWorkflow { get; init; }
}

public sealed record GovernanceCorrelationTraceReference
{
    public required string TraceId { get; init; }
    public required string EventKind { get; init; }
    public required string SafeSummary { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public DateTimeOffset? RecordedUtc { get; init; }
}

public sealed record GovernanceCorrelationMissingEvidence
{
    public required string MissingEvidenceId { get; init; }
    public required GovernanceCorrelationMissingEvidenceKind Kind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record GovernanceCorrelationConflictSignal
{
    public required string ConflictId { get; init; }
    public required GovernanceCorrelationConflictKind Kind { get; init; }
    public required string SafeSummary { get; init; }
    public required bool IsVerdict { get; init; }
    public required bool CanResolve { get; init; }
}

public sealed record GovernanceCorrelationRecommendation
{
    public required string RecommendationId { get; init; }
    public required string SafeSummary { get; init; }
    public required IReadOnlyList<string> SupportingReferenceIds { get; init; }
    public required bool IsInvestigationOnly { get; init; }
    public required bool CanMutateState { get; init; }
    public required bool CanApprove { get; init; }
    public required bool CanOpenGate { get; init; }
    public required bool CanApproveRelease { get; init; }
}

public sealed record ApprovalGateDogfoodCorrelationReportIssue
{
    public required ApprovalGateDogfoodCorrelationReportIssueKind Kind { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public static class ApprovalGateDogfoodCorrelationReportBoundaries
{
    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Approval/Gate/Dogfood correlation report is read-only.",
        "Correlation is not approval.",
        "Correlation is not policy satisfaction.",
        "Dogfood receipt is not release approval.",
        "Tool gate evidence is not tool execution.",
        "Report status is not governance status.",
        "Conflict signal is not verdict.",
        "Recommendation is not execution.",
        "Report is not workflow transition.",
        "Report must not expose hidden/private reasoning."
    ];
}

public sealed class ApprovalGateDogfoodCorrelationReportValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const string RedactedUnsafeText = "[redacted governance correlation text]";

    private static readonly string[] UnsafeMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
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

    public IReadOnlyList<ApprovalGateDogfoodCorrelationReportIssue> Validate(ApprovalGateDogfoodCorrelationReportRequest? request)
    {
        var issues = new List<ApprovalGateDogfoodCorrelationReportIssue>();
        if (request is null)
        {
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.MissingSelector, "request", "Correlation report request is required."));
            return issues;
        }

        if (AllBlank(request.ProjectReferenceId, request.CorrelationId, request.CausationId))
        {
            issues.Add(Issue(
                ApprovalGateDogfoodCorrelationReportIssueKind.MissingSelector,
                "selector",
                "projectReferenceId, correlationId, or causationId is required for governance correlation lookup."));
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectReferenceId) && !Guid.TryParse(request.ProjectReferenceId, out _))
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.InvalidProjectReferenceId, nameof(request.ProjectReferenceId), "projectReferenceId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) && !Guid.TryParse(request.CorrelationId, out _))
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.InvalidCorrelationId, nameof(request.CorrelationId), "correlationId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(request.CausationId) && !Guid.TryParse(request.CausationId, out _))
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.InvalidCausationId, nameof(request.CausationId), "causationId must be a GUID."));

        if (request.FromUtc.HasValue && request.ToUtc.HasValue && request.FromUtc > request.ToUtc)
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.InvalidDateRange, nameof(request.FromUtc), "fromUtc must be before toUtc."));

        if (request.Take < 1 || request.Take > MaxTake)
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.InvalidTake, nameof(request.Take), $"take must be between 1 and {MaxTake}."));

        foreach (var (field, value) in QueryTexts(request))
            ValidateText(value, field, issues);

        return issues;
    }

    public ApprovalGateDogfoodCorrelationReportRequest Normalize(ApprovalGateDogfoodCorrelationReportRequest request) =>
        request with
        {
            ProjectReferenceId = NormalizeText(request.ProjectReferenceId),
            WorkflowRunId = NormalizeText(request.WorkflowRunId),
            WorkflowStepId = NormalizeText(request.WorkflowStepId),
            CorrelationId = NormalizeText(request.CorrelationId),
            CausationId = NormalizeText(request.CausationId),
            ApprovalReferenceId = NormalizeText(request.ApprovalReferenceId),
            ToolRequestId = NormalizeText(request.ToolRequestId),
            ToolGateDecisionId = NormalizeText(request.ToolGateDecisionId),
            DogfoodReceiptId = NormalizeText(request.DogfoodReceiptId),
            Take = Math.Clamp(request.Take, 1, MaxTake)
        };

    public static ApprovalGateDogfoodCorrelationReportIssue Issue(ApprovalGateDogfoodCorrelationReportIssueKind kind, string field, string message) =>
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

    private static void ValidateText(string? value, string field, List<ApprovalGateDogfoodCorrelationReportIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(Issue(ApprovalGateDogfoodCorrelationReportIssueKind.UnsafeQueryText, field, "Correlation report query contains unsupported governance text."));
    }

    private static IEnumerable<(string Field, string Value)> QueryTexts(ApprovalGateDogfoodCorrelationReportRequest request)
    {
        yield return (nameof(request.ProjectReferenceId), request.ProjectReferenceId);
        yield return (nameof(request.WorkflowRunId), request.WorkflowRunId);
        yield return (nameof(request.WorkflowStepId), request.WorkflowStepId);
        yield return (nameof(request.CorrelationId), request.CorrelationId);
        yield return (nameof(request.CausationId), request.CausationId);
        yield return (nameof(request.ApprovalReferenceId), request.ApprovalReferenceId);
        yield return (nameof(request.ToolRequestId), request.ToolRequestId);
        yield return (nameof(request.ToolGateDecisionId), request.ToolGateDecisionId);
        yield return (nameof(request.DogfoodReceiptId), request.DogfoodReceiptId);
    }

    private static bool AllBlank(params string[] values) => values.All(string.IsNullOrWhiteSpace);
}

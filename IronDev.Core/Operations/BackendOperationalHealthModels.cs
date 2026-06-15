namespace IronDev.Core.Operations;

public enum BackendOperationalHealthStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    Healthy = 2,
    Degraded = 3,
    Unavailable = 4
}

public enum BackendDependencyKind
{
    Unknown = 0,
    ApiProcess = 1,
    DatabaseConnection = 2,
    GovernanceEventReadModel = 3,
    WorkflowReadModel = 4,
    ToolRequestReadModel = 5,
    ToolGateDecisionReadModel = 6,
    ApprovalDecisionReadModel = 7,
    PolicyDecisionReadModel = 8,
    DogfoodReceiptReadModel = 9,
    GovernanceTraceExplorer = 10,
    FailedWorkflowDiagnosisReport = 11,
    ApprovalGateDogfoodCorrelationReport = 12,
    AgentRunHealthSummary = 13,
    ConfigurationPresence = 14
}

public enum BackendDependencyHealthStatus
{
    Unknown = 0,
    Available = 1,
    Degraded = 2,
    Unavailable = 3,
    NotConfigured = 4
}

public enum BackendOperationalHealthWarningKind
{
    Unknown = 0,
    DependencyUnavailable = 1,
    DependencyDegraded = 2,
    ConfigurationMissing = 3,
    ReadModelUnavailable = 4,
    TraceExplorerUnavailable = 5,
    ReportSurfaceUnavailable = 6,
    InsufficientEvidence = 7
}

public enum BackendOperationalHealthIssueKind
{
    Unknown = 0,
    InvalidProjectReferenceId = 1,
    InvalidCorrelationId = 2,
    UnsafeQueryText = 3
}

public sealed record BackendOperationalHealthRequest
{
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public bool IncludeDependencyDetails { get; init; } = true;
    public bool IncludeWarnings { get; init; } = true;
}

public sealed record BackendOperationalHealthResponse
{
    public required BackendOperationalHealthStatus Status { get; init; }
    public BackendOperationalHealthReport? Report { get; init; }
    public required IReadOnlyList<BackendOperationalHealthIssue> Issues { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }
}

public sealed record BackendOperationalHealthReport
{
    public required string ReportId { get; init; }
    public required BackendOperationalHealthStatus Status { get; init; }
    public required DateTimeOffset GeneratedUtc { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string CorrelationId { get; init; }
    public required IReadOnlyList<string> SafeSummaryLines { get; init; }
    public required IReadOnlyList<BackendDependencyHealthCheck> DependencyChecks { get; init; }
    public required IReadOnlyList<BackendOperationalHealthWarning> Warnings { get; init; }
    public required IReadOnlyList<BackendOperationalHealthRecommendation> Recommendations { get; init; }
    public required IReadOnlyList<string> BoundaryWarnings { get; init; }

    public required bool IsHealthReportOnly { get; init; }
    public required bool IsReleaseReadiness { get; init; }
    public required bool IsApproval { get; init; }
    public required bool IsPolicySatisfaction { get; init; }
    public required bool IsWorkflowExecution { get; init; }
    public required bool IsBackendRepair { get; init; }
    public required bool IsMigrationExecution { get; init; }

    public required bool CanRestartBackend { get; init; }
    public required bool CanRepairBackend { get; init; }
    public required bool CanRunMigration { get; init; }
    public required bool CanExecuteWorkflow { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanApproveRelease { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanApplyPatch { get; init; }

    public required bool CreatesGovernanceEvent { get; init; }
    public required bool CreatesApprovalDecision { get; init; }
    public required bool CreatesPolicyDecision { get; init; }
    public required bool CreatesToolRequest { get; init; }
    public required bool CreatesDogfoodReceipt { get; init; }
    public required bool TransitionsWorkflow { get; init; }
    public required bool CallsModel { get; init; }
    public required bool InvokesTool { get; init; }
    public required bool DispatchesAgent { get; init; }
    public required bool PromotesMemory { get; init; }
}

public sealed record BackendDependencyHealthCheck
{
    public required string CheckId { get; init; }
    public required BackendDependencyKind DependencyKind { get; init; }
    public required BackendDependencyHealthStatus Status { get; init; }
    public required string SafeSummary { get; init; }
    public required DateTimeOffset CheckedUtc { get; init; }
    public required bool IsReadOnlyCheck { get; init; }
    public required bool IsRepairAction { get; init; }
    public required bool CanMutateDependency { get; init; }
    public required bool CanRestartDependency { get; init; }
    public required bool CanRunMigration { get; init; }
}

public sealed record BackendOperationalHealthWarning
{
    public required string WarningId { get; init; }
    public required BackendOperationalHealthWarningKind Kind { get; init; }
    public required string SafeSummary { get; init; }
    public required bool IsEvidenceOnly { get; init; }
    public required bool IsFailureProof { get; init; }
    public required bool CanRepair { get; init; }
}

public sealed record BackendOperationalHealthRecommendation
{
    public required string RecommendationId { get; init; }
    public required string SafeSummary { get; init; }
    public required IReadOnlyList<string> SupportingCheckIds { get; init; }
    public required bool IsInvestigationOnly { get; init; }
    public required bool CanMutateState { get; init; }
    public required bool CanRestartBackend { get; init; }
    public required bool CanRunMigration { get; init; }
    public required bool CanExecuteWorkflow { get; init; }
    public required bool CanApproveRelease { get; init; }
}

public sealed record BackendOperationalHealthIssue
{
    public required BackendOperationalHealthIssueKind Kind { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public static class BackendOperationalHealthBoundaries
{
    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Backend operational health report is read-only.",
        "Health check is not release readiness.",
        "Healthy status is not approval.",
        "Dependency status is not authority.",
        "Recommendation is not execution.",
        "Report is not backend repair.",
        "Report is not backend restart.",
        "Report is not migration execution.",
        "Report is not workflow execution.",
        "Report must not expose secrets or private reasoning."
    ];
}

public sealed class BackendOperationalHealthValidator
{
    public const string RedactedUnsafeText = "[redacted backend health text]";

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
        "hidden deliberation",
        "payloadjson",
        "source content",
        "sourcecontent",
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

    public IReadOnlyList<BackendOperationalHealthIssue> Validate(BackendOperationalHealthRequest? request)
    {
        var issues = new List<BackendOperationalHealthIssue>();
        if (request is null)
            return [Issue(BackendOperationalHealthIssueKind.UnsafeQueryText, "request", "Backend operational health request is required.")];

        ValidateText(request.ProjectReferenceId, nameof(request.ProjectReferenceId), issues);
        ValidateText(request.CorrelationId, nameof(request.CorrelationId), issues);

        if (!string.IsNullOrWhiteSpace(request.ProjectReferenceId) && !Guid.TryParse(request.ProjectReferenceId, out _))
            issues.Add(Issue(BackendOperationalHealthIssueKind.InvalidProjectReferenceId, nameof(request.ProjectReferenceId), "projectReferenceId must be a GUID."));

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) && !Guid.TryParse(request.CorrelationId, out _))
            issues.Add(Issue(BackendOperationalHealthIssueKind.InvalidCorrelationId, nameof(request.CorrelationId), "correlationId must be a GUID."));

        return issues;
    }

    public BackendOperationalHealthRequest Normalize(BackendOperationalHealthRequest request) =>
        request with
        {
            ProjectReferenceId = NormalizeText(request.ProjectReferenceId),
            CorrelationId = NormalizeText(request.CorrelationId)
        };

    public static BackendOperationalHealthIssue Issue(BackendOperationalHealthIssueKind kind, string field, string message) =>
        new()
        {
            Kind = kind,
            Field = SafeText(field),
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

    private static void ValidateText(string? value, string field, List<BackendOperationalHealthIssue> issues)
    {
        if (ContainsUnsafeText(value))
            issues.Add(Issue(BackendOperationalHealthIssueKind.UnsafeQueryText, field, "Backend operational health query contains unsupported private or raw text."));
    }
}

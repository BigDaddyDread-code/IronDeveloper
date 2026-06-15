using IronDev.Core.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/operations/health")]
public sealed class BackendOperationalHealthController : ControllerBase
{
    private static readonly HashSet<string> QueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectReferenceId",
        "correlationId",
        "includeDependencyDetails",
        "includeWarnings"
    };

    private readonly IBackendOperationalHealthService _healthService;

    public BackendOperationalHealthController(IBackendOperationalHealthService healthService)
    {
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
    }

    [HttpGet]
    public Task<ActionResult<BackendOperationalHealthApiEnvelope<BackendOperationalHealthReport>>> GetHealth(
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string correlationId = "",
        [FromQuery] bool includeDependencyDetails = true,
        [FromQuery] bool includeWarnings = true,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(projectReferenceId, correlationId, includeDependencyDetails, includeWarnings, cancellationToken);

    [HttpGet("backend")]
    public Task<ActionResult<BackendOperationalHealthApiEnvelope<BackendOperationalHealthReport>>> GetBackendHealth(
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string correlationId = "",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(projectReferenceId, correlationId, true, true, cancellationToken);

    [HttpGet("dependencies")]
    public Task<ActionResult<BackendOperationalHealthApiEnvelope<BackendOperationalHealthReport>>> GetDependencyHealth(
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string correlationId = "",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(projectReferenceId, correlationId, true, true, cancellationToken);

    private async Task<ActionResult<BackendOperationalHealthApiEnvelope<BackendOperationalHealthReport>>> ExecuteAsync(
        string projectReferenceId,
        string correlationId,
        bool includeDependencyDetails,
        bool includeWarnings,
        CancellationToken cancellationToken)
    {
        var unsupported = UnsupportedQueryKeys();
        if (unsupported.Count > 0)
            return BadRequest(Envelope("validation_error", null, errors: unsupported.Select(UnsupportedFilter).ToArray()));

        var response = await _healthService.GetHealthAsync(new BackendOperationalHealthRequest
        {
            ProjectReferenceId = projectReferenceId,
            CorrelationId = correlationId,
            IncludeDependencyDetails = includeDependencyDetails,
            IncludeWarnings = includeWarnings
        }, cancellationToken);

        var envelope = Envelope(
            ToApiStatus(response.Status),
            response.Report,
            response.BoundaryWarnings,
            response.Issues.Select(ToError).ToArray());

        return response.Status is BackendOperationalHealthStatus.InvalidRequest
            ? BadRequest(envelope)
            : Ok(envelope);
    }

    private IReadOnlyList<string> UnsupportedQueryKeys() =>
        Request.Query.Keys.Where(key => !QueryKeys.Contains(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    private static BackendOperationalHealthApiEnvelope<BackendOperationalHealthReport> Envelope(
        string status,
        BackendOperationalHealthReport? data,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<BackendOperationalHealthApiError>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            Boundary = BackendOperationalHealthApiBoundary.ReadOnly(),
            MutationOccurred = false,
            HumanApprovalRequired = false,
            Warnings = warnings ?? BackendOperationalHealthBoundaries.Warnings,
            Errors = errors ?? []
        };

    private static string ToApiStatus(BackendOperationalHealthStatus status) =>
        status switch
        {
            BackendOperationalHealthStatus.InvalidRequest => "validation_error",
            BackendOperationalHealthStatus.Healthy => "healthy",
            BackendOperationalHealthStatus.Degraded => "degraded",
            BackendOperationalHealthStatus.Unavailable => "unavailable",
            _ => "unknown"
        };

    private static BackendOperationalHealthApiError ToError(BackendOperationalHealthIssue issue) =>
        new()
        {
            Category = "validation_error",
            Code = issue.Kind switch
            {
                BackendOperationalHealthIssueKind.InvalidProjectReferenceId => "BACKEND_OPERATIONAL_HEALTH_INVALID_PROJECT_REFERENCE_ID",
                BackendOperationalHealthIssueKind.InvalidCorrelationId => "BACKEND_OPERATIONAL_HEALTH_INVALID_CORRELATION_ID",
                BackendOperationalHealthIssueKind.UnsafeQueryText => "BACKEND_OPERATIONAL_HEALTH_UNSAFE_QUERY_TEXT",
                _ => "BACKEND_OPERATIONAL_HEALTH_UNKNOWN"
            },
            Message = BackendOperationalHealthValidator.SafeText(issue.Message),
            Field = BackendOperationalHealthValidator.SafeText(issue.Field)
        };

    private static BackendOperationalHealthApiError UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_filter",
            Code = "BACKEND_OPERATIONAL_HEALTH_UNSUPPORTED_FILTER",
            Message = $"Unsupported filter: {BackendOperationalHealthValidator.SafeText(filter)}."
        };
}

public sealed record BackendOperationalHealthApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public required BackendOperationalHealthApiBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<BackendOperationalHealthApiError> Errors { get; init; } = [];
}

public sealed record BackendOperationalHealthApiBoundary
{
    public bool ReadOnlyHealthCheck { get; init; }
    public bool HealthIsReleaseReadiness { get; init; }
    public bool HealthyIsApproval { get; init; }
    public bool DependencyStatusIsAuthority { get; init; }
    public bool RecommendationIsExecution { get; init; }
    public bool ReportIsBackendRepair { get; init; }
    public bool ReportIsBackendRestart { get; init; }
    public bool ReportIsMigrationExecution { get; init; }
    public bool ReportIsWorkflowExecution { get; init; }
    public bool CanRestartBackend { get; init; }
    public bool CanRepairBackend { get; init; }
    public bool CanRunMigration { get; init; }
    public bool CanExecuteWorkflow { get; init; }
    public bool CanInvokeTool { get; init; }
    public bool CanDispatchAgent { get; init; }
    public bool CanCallModel { get; init; }
    public bool CanApproveRelease { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanApplySource { get; init; }
    public bool CanApplyPatch { get; init; }
    public bool ExposesRawPayloads { get; init; }
    public bool ExposesPrivateReasoning { get; init; }
    public bool ExposesSensitiveValues { get; init; }

    public static BackendOperationalHealthApiBoundary ReadOnly() =>
        new()
        {
            ReadOnlyHealthCheck = true,
            HealthIsReleaseReadiness = false,
            HealthyIsApproval = false,
            DependencyStatusIsAuthority = false,
            RecommendationIsExecution = false,
            ReportIsBackendRepair = false,
            ReportIsBackendRestart = false,
            ReportIsMigrationExecution = false,
            ReportIsWorkflowExecution = false,
            CanRestartBackend = false,
            CanRepairBackend = false,
            CanRunMigration = false,
            CanExecuteWorkflow = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanApproveRelease = false,
            CanSatisfyPolicy = false,
            CanPromoteMemory = false,
            CanApplySource = false,
            CanApplyPatch = false,
            ExposesRawPayloads = false,
            ExposesPrivateReasoning = false,
            ExposesSensitiveValues = false
        };
}

public sealed record BackendOperationalHealthApiError
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

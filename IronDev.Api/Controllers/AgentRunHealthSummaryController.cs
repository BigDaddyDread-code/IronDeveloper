using IronDev.Core.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/agents/runs")]
public sealed class AgentRunHealthSummaryController : ControllerBase
{
    private static readonly HashSet<string> SummaryQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectReferenceId",
        "agentRunId",
        "correlationId",
        "workflowRunId",
        "workflowStepId",
        "agentId",
        "agentKind",
        "fromUtc",
        "toUtc",
        "take",
        "includeGateSignals",
        "includeApprovalSignals",
        "includePolicySignals",
        "includeDogfoodSignals"
    };

    private static readonly HashSet<string> DetailQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectReferenceId",
        "correlationId",
        "workflowRunId",
        "workflowStepId",
        "agentId",
        "agentKind",
        "fromUtc",
        "toUtc",
        "take",
        "includeGateSignals",
        "includeApprovalSignals",
        "includePolicySignals",
        "includeDogfoodSignals"
    };

    private readonly IAgentRunHealthSummaryService _summaryService;

    public AgentRunHealthSummaryController(IAgentRunHealthSummaryService summaryService)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    [HttpGet("health-summary")]
    public async Task<ActionResult<AgentRunHealthSummaryApiEnvelope<AgentRunHealthSummary>>> GetSummary(
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string agentRunId = "",
        [FromQuery] string correlationId = "",
        [FromQuery] string workflowRunId = "",
        [FromQuery] string workflowStepId = "",
        [FromQuery] string agentId = "",
        [FromQuery] string agentKind = "",
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int take = AgentRunHealthSummaryValidator.DefaultTake,
        [FromQuery] bool includeGateSignals = true,
        [FromQuery] bool includeApprovalSignals = true,
        [FromQuery] bool includePolicySignals = true,
        [FromQuery] bool includeDogfoodSignals = true,
        CancellationToken cancellationToken = default)
    {
        var unsupported = UnsupportedQueryKeys(SummaryQueryKeys);
        if (unsupported.Count > 0)
            return BadRequest(Envelope("validation_error", null, errors: unsupported.Select(UnsupportedFilter).ToArray()));

        return await ExecuteAsync(new AgentRunHealthSummaryRequest
        {
            ProjectReferenceId = projectReferenceId,
            AgentRunId = agentRunId,
            CorrelationId = correlationId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            AgentId = agentId,
            AgentKind = agentKind,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Take = take,
            IncludeGateSignals = includeGateSignals,
            IncludeApprovalSignals = includeApprovalSignals,
            IncludePolicySignals = includePolicySignals,
            IncludeDogfoodSignals = includeDogfoodSignals
        }, cancellationToken);
    }

    [HttpGet("{agentRunId}/health-summary")]
    public async Task<ActionResult<AgentRunHealthSummaryApiEnvelope<AgentRunHealthSummary>>> GetSummaryForAgentRun(
        string agentRunId,
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string correlationId = "",
        [FromQuery] string workflowRunId = "",
        [FromQuery] string workflowStepId = "",
        [FromQuery] string agentId = "",
        [FromQuery] string agentKind = "",
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int take = AgentRunHealthSummaryValidator.DefaultTake,
        [FromQuery] bool includeGateSignals = true,
        [FromQuery] bool includeApprovalSignals = true,
        [FromQuery] bool includePolicySignals = true,
        [FromQuery] bool includeDogfoodSignals = true,
        CancellationToken cancellationToken = default)
    {
        var unsupported = UnsupportedQueryKeys(DetailQueryKeys);
        if (unsupported.Count > 0)
            return BadRequest(Envelope("validation_error", null, runId: agentRunId, errors: unsupported.Select(UnsupportedFilter).ToArray()));

        return await ExecuteAsync(new AgentRunHealthSummaryRequest
        {
            ProjectReferenceId = projectReferenceId,
            AgentRunId = agentRunId,
            CorrelationId = correlationId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            AgentId = agentId,
            AgentKind = agentKind,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Take = take,
            IncludeGateSignals = includeGateSignals,
            IncludeApprovalSignals = includeApprovalSignals,
            IncludePolicySignals = includePolicySignals,
            IncludeDogfoodSignals = includeDogfoodSignals
        }, cancellationToken);
    }

    private async Task<ActionResult<AgentRunHealthSummaryApiEnvelope<AgentRunHealthSummary>>> ExecuteAsync(
        AgentRunHealthSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _summaryService.GetSummaryAsync(request, cancellationToken);
        var status = ToApiStatus(response.Status);
        var errors = response.Issues.Select(ToError).ToArray();
        var envelope = Envelope(status, response.Summary, request.AgentRunId, response.BoundaryWarnings, errors);

        return response.Status is AgentRunHealthSummaryStatus.InvalidRequest
            ? BadRequest(envelope)
            : Ok(envelope);
    }

    private IReadOnlyList<string> UnsupportedQueryKeys(IReadOnlySet<string> allowed) =>
        Request.Query.Keys.Where(key => !allowed.Contains(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    private static AgentRunHealthSummaryApiEnvelope<AgentRunHealthSummary> Envelope(
        string status,
        AgentRunHealthSummary? data,
        string runId = "",
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<AgentRunHealthSummaryApiError>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            RunId = AgentRunHealthSummaryValidator.SafeText(runId),
            Boundary = new AgentRunHealthSummaryBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = false,
            Warnings = warnings ?? AgentRunHealthSummaryBoundaries.Warnings,
            Errors = errors ?? []
        };

    private static string ToApiStatus(AgentRunHealthSummaryStatus status) =>
        status switch
        {
            AgentRunHealthSummaryStatus.InvalidRequest => "validation_error",
            AgentRunHealthSummaryStatus.NoAgentRunEvidenceFound => "no_agent_run_evidence_found",
            AgentRunHealthSummaryStatus.SummaryAvailable => "summary_available",
            _ => "unknown"
        };

    private static AgentRunHealthSummaryApiError ToError(AgentRunHealthSummaryIssue issue) =>
        new()
        {
            Category = issue.Kind is AgentRunHealthSummaryIssueKind.TraceExplorerError ? "trace_error" : "validation_error",
            Code = ToApiCode(issue.Kind),
            Message = AgentRunHealthSummaryValidator.SafeText(issue.Message),
            Field = AgentRunHealthSummaryValidator.SafeText(issue.Field)
        };

    private static AgentRunHealthSummaryApiError UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_filter",
            Code = "AGENT_RUN_HEALTH_UNSUPPORTED_FILTER",
            Message = $"Unsupported filter: {AgentRunHealthSummaryValidator.SafeText(filter)}."
        };

    private static string ToApiCode(AgentRunHealthSummaryIssueKind kind) =>
        kind switch
        {
            AgentRunHealthSummaryIssueKind.MissingSelector => "AGENT_RUN_HEALTH_MISSING_SELECTOR",
            AgentRunHealthSummaryIssueKind.InvalidProjectReferenceId => "AGENT_RUN_HEALTH_INVALID_PROJECT_REFERENCE_ID",
            AgentRunHealthSummaryIssueKind.InvalidCorrelationId => "AGENT_RUN_HEALTH_INVALID_CORRELATION_ID",
            AgentRunHealthSummaryIssueKind.InvalidDateRange => "AGENT_RUN_HEALTH_INVALID_DATE_RANGE",
            AgentRunHealthSummaryIssueKind.InvalidTake => "AGENT_RUN_HEALTH_INVALID_TAKE",
            AgentRunHealthSummaryIssueKind.UnsafeQueryText => "AGENT_RUN_HEALTH_UNSAFE_QUERY_TEXT",
            AgentRunHealthSummaryIssueKind.TraceExplorerError => "AGENT_RUN_HEALTH_TRACE_EXPLORER_ERROR",
            _ => "AGENT_RUN_HEALTH_UNKNOWN"
        };
}

public sealed record AgentRunHealthSummaryApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string RunId { get; init; } = string.Empty;
    public required AgentRunHealthSummaryBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<AgentRunHealthSummaryApiError> Errors { get; init; } = [];
}

public sealed record AgentRunHealthSummaryApiError
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

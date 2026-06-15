using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/governance/traces")]
public sealed class GovernanceTraceExplorerController : ControllerBase
{
    private readonly IGovernanceTraceExplorerService _traceExplorer;

    public GovernanceTraceExplorerController(IGovernanceTraceExplorerService traceExplorer)
    {
        _traceExplorer = traceExplorer ?? throw new ArgumentNullException(nameof(traceExplorer));
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string workflowRunId = "",
        [FromQuery] string workflowStepId = "",
        [FromQuery] string correlationId = "",
        [FromQuery] string causationId = "",
        [FromQuery] string subjectReferenceId = "",
        [FromQuery] string eventKind = "",
        [FromQuery] string sourceComponent = "",
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int take = GovernanceTraceExplorerValidator.DefaultTake,
        [FromQuery] bool includeRelated = false,
        CancellationToken cancellationToken = default)
    {
        var response = await _traceExplorer.SearchAsync(new GovernanceTraceQuery
        {
            ProjectReferenceId = projectReferenceId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            CorrelationId = correlationId,
            CausationId = causationId,
            SubjectReferenceId = subjectReferenceId,
            EventKind = eventKind,
            SourceComponent = sourceComponent,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Take = take,
            IncludeRelated = includeRelated
        }, cancellationToken);

        return ToActionResult(response);
    }

    [HttpGet("{traceId}")]
    public async Task<IActionResult> GetByTraceId(string traceId, CancellationToken cancellationToken = default)
    {
        var response = await _traceExplorer.GetByTraceIdAsync(traceId, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("by-correlation/{correlationId}")]
    public async Task<IActionResult> GetByCorrelationId(
        string correlationId,
        [FromQuery] string projectReferenceId = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _traceExplorer.GetByCorrelationIdAsync(correlationId, projectReferenceId, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("by-workflow-run/{workflowRunId}")]
    public async Task<IActionResult> GetByWorkflowRunId(
        string workflowRunId,
        [FromQuery] string projectReferenceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _traceExplorer.GetByWorkflowRunIdAsync(workflowRunId, projectReferenceId, cancellationToken);
        return ToActionResult(response);
    }

    private IActionResult ToActionResult(GovernanceTraceListResponse response)
    {
        var body = Envelope(ToWireStatus(response.Status), response, response.Issues, response.BoundaryWarnings);
        return response.Status is GovernanceTraceExplorerStatus.InvalidRequest
            ? BadRequest(body)
            : Ok(body);
    }

    private IActionResult ToActionResult(GovernanceTraceDetailResponse response)
    {
        var body = Envelope(ToWireStatus(response.Status), response, response.Issues, response.BoundaryWarnings);
        return response.Status is GovernanceTraceExplorerStatus.InvalidRequest
            ? BadRequest(body)
            : response.Status is GovernanceTraceExplorerStatus.NoTraceFound
                ? NotFound(body)
                : Ok(body);
    }

    private static object Envelope<TData>(
        string status,
        TData data,
        IReadOnlyList<GovernanceTraceExplorerIssue> issues,
        IReadOnlyList<string> warnings) =>
        new
        {
            status,
            mutationOccurred = false,
            boundary = Boundary(),
            warnings,
            errors = issues.Select(ToError).ToArray(),
            data
        };

    private static object Boundary() =>
        new
        {
            readOnlyTrace = true,
            traceabilityIsAuthority = false,
            traceOutputIsApproval = false,
            traceOutputIsPolicySatisfaction = false,
            traceOutputIsWorkflowTransition = false,
            traceOutputIsToolInvocation = false,
            traceOutputIsAgentDispatch = false,
            traceOutputIsModelExecution = false,
            traceOutputIsMemoryPromotion = false,
            traceOutputIsSourceApply = false,
            traceOutputIsPatchApply = false,
            createsGovernanceEvent = false,
            updatesGovernanceEvent = false,
            deletesGovernanceEvent = false,
            replaysGovernance = false,
            canApprove = false,
            canReject = false,
            canSatisfyPolicy = false,
            canTransitionWorkflow = false,
            canInvokeTool = false,
            canDispatchAgent = false,
            canCallModel = false,
            canPromoteMemory = false,
            canActivateRetrieval = false,
            canApplySource = false,
            canApplyPatch = false,
            exposesRawPayloadJson = false,
            exposesPrivateReasoning = false
        };

    private static object ToError(GovernanceTraceExplorerIssue issue) =>
        new
        {
            code = ToWireIssue(issue.Kind),
            field = issue.Field,
            message = issue.Message
        };

    private static string ToWireStatus(GovernanceTraceExplorerStatus status) =>
        status switch
        {
            GovernanceTraceExplorerStatus.InvalidRequest => "validation_error",
            GovernanceTraceExplorerStatus.NoTraceFound => "not_found",
            GovernanceTraceExplorerStatus.TraceFound => "trace_found",
            GovernanceTraceExplorerStatus.TraceListReturned => "trace_list_returned",
            _ => "unknown"
        };

    private static string ToWireIssue(GovernanceTraceExplorerIssueKind kind) =>
        kind switch
        {
            GovernanceTraceExplorerIssueKind.MissingTraceId => "missing_trace_id",
            GovernanceTraceExplorerIssueKind.MissingCorrelationId => "missing_correlation_id",
            GovernanceTraceExplorerIssueKind.MissingWorkflowRunId => "missing_workflow_run_id",
            GovernanceTraceExplorerIssueKind.InvalidDateRange => "invalid_date_range",
            GovernanceTraceExplorerIssueKind.UnsafeQueryText => "unsafe_query_text",
            GovernanceTraceExplorerIssueKind.MissingProjectReferenceId => "missing_project_reference_id",
            GovernanceTraceExplorerIssueKind.InvalidProjectReferenceId => "invalid_project_reference_id",
            GovernanceTraceExplorerIssueKind.InvalidTraceId => "invalid_trace_id",
            GovernanceTraceExplorerIssueKind.InvalidCorrelationId => "invalid_correlation_id",
            GovernanceTraceExplorerIssueKind.InvalidCausationId => "invalid_causation_id",
            GovernanceTraceExplorerIssueKind.InvalidTake => "invalid_take",
            _ => "unknown"
        };
}

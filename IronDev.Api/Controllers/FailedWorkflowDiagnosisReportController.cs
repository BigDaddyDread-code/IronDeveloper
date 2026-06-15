using IronDev.Core.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/workflow/failures")]
public sealed class FailedWorkflowDiagnosisReportController : ControllerBase
{
    private readonly IFailedWorkflowDiagnosisReportService _diagnosisReports;

    public FailedWorkflowDiagnosisReportController(IFailedWorkflowDiagnosisReportService diagnosisReports)
    {
        _diagnosisReports = diagnosisReports ?? throw new ArgumentNullException(nameof(diagnosisReports));
    }

    [HttpGet("{workflowRunId}/diagnosis-report")]
    public async Task<IActionResult> GetDiagnosisReport(
        string workflowRunId,
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string workflowStepId = "",
        [FromQuery] string correlationId = "",
        [FromQuery] bool includeTraceTimeline = false,
        [FromQuery] bool includeRecommendations = false,
        [FromQuery] int takeTraceItems = FailedWorkflowDiagnosisReportValidator.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var response = await _diagnosisReports.GetReportAsync(new FailedWorkflowDiagnosisReportRequest
        {
            WorkflowRunId = workflowRunId,
            ProjectReferenceId = projectReferenceId,
            WorkflowStepId = workflowStepId,
            CorrelationId = correlationId,
            IncludeTraceTimeline = includeTraceTimeline,
            IncludeRecommendations = includeRecommendations,
            TakeTraceItems = takeTraceItems
        }, cancellationToken);

        var body = Envelope(ToWireStatus(response.Status), response.Report, response.Issues, response.BoundaryWarnings);
        return response.Status is FailedWorkflowDiagnosisReportStatus.InvalidRequest
            ? BadRequest(body)
            : Ok(body);
    }

    private static object Envelope(
        string status,
        FailedWorkflowDiagnosisReport? report,
        IReadOnlyList<FailedWorkflowDiagnosisReportIssue> issues,
        IReadOnlyList<string> warnings) =>
        new
        {
            status,
            mutationOccurred = false,
            boundary = Boundary(),
            warnings,
            errors = issues.Select(ToError).ToArray(),
            data = new
            {
                report
            }
        };

    private static object Boundary() =>
        new
        {
            readOnlyReport = true,
            diagnosisIsRootCauseProof = false,
            reportIsRepair = false,
            reportIsWorkflowRetry = false,
            reportIsWorkflowResume = false,
            reportIsWorkflowTransition = false,
            reportIsTicketCreation = false,
            reportIsApproval = false,
            reportIsPolicySatisfaction = false,
            reportIsExecutionPermission = false,
            reportIsGovernanceDecision = false,
            toolInvoked = false,
            agentDispatched = false,
            modelCalled = false,
            promptBuilt = false,
            memoryPromoted = false,
            retrievalActivated = false,
            sourceApplied = false,
            patchApplied = false,
            createsGovernanceEvent = false,
            updatesGovernanceEvent = false,
            deletesGovernanceEvent = false,
            exposesRawPayloadJson = false,
            exposesPrivateReasoning = false
        };

    private static object ToError(FailedWorkflowDiagnosisReportIssue issue) =>
        new
        {
            code = ToWireIssue(issue.Kind),
            field = issue.Field,
            message = issue.Message
        };

    private static string ToWireStatus(FailedWorkflowDiagnosisReportStatus status) =>
        status switch
        {
            FailedWorkflowDiagnosisReportStatus.InvalidRequest => "validation_error",
            FailedWorkflowDiagnosisReportStatus.NoWorkflowEvidenceFound => "no_workflow_evidence_found",
            FailedWorkflowDiagnosisReportStatus.NoFailureEvidenceFound => "no_failure_evidence_found",
            FailedWorkflowDiagnosisReportStatus.ReportAvailable => "report_available",
            _ => "unknown"
        };

    private static string ToWireIssue(FailedWorkflowDiagnosisIssueKind kind) =>
        kind switch
        {
            FailedWorkflowDiagnosisIssueKind.MissingWorkflowRunId => "missing_workflow_run_id",
            FailedWorkflowDiagnosisIssueKind.MissingTraceSelector => "missing_trace_selector",
            FailedWorkflowDiagnosisIssueKind.InvalidProjectReferenceId => "invalid_project_reference_id",
            FailedWorkflowDiagnosisIssueKind.InvalidCorrelationId => "invalid_correlation_id",
            FailedWorkflowDiagnosisIssueKind.InvalidTakeTraceItems => "invalid_take_trace_items",
            FailedWorkflowDiagnosisIssueKind.UnsafeQueryText => "unsafe_query_text",
            _ => "unknown"
        };
}

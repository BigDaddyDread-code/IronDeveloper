using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/governance/correlation-reports")]
public sealed class ApprovalGateDogfoodCorrelationReportController : ControllerBase
{
    private readonly IApprovalGateDogfoodCorrelationReportService _reports;

    public ApprovalGateDogfoodCorrelationReportController(IApprovalGateDogfoodCorrelationReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    [HttpGet("approval-gate-dogfood")]
    public async Task<IActionResult> GetApprovalGateDogfoodReport(
        [FromQuery] string projectReferenceId = "",
        [FromQuery] string workflowRunId = "",
        [FromQuery] string workflowStepId = "",
        [FromQuery] string correlationId = "",
        [FromQuery] string causationId = "",
        [FromQuery] string approvalReferenceId = "",
        [FromQuery] string toolRequestId = "",
        [FromQuery] string toolGateDecisionId = "",
        [FromQuery] string dogfoodReceiptId = "",
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int take = ApprovalGateDogfoodCorrelationReportValidator.DefaultTake,
        [FromQuery] bool includeTraceReferences = true,
        [FromQuery] bool includeMissingEvidence = true,
        [FromQuery] bool includeRecommendations = true,
        CancellationToken cancellationToken = default)
    {
        var response = await _reports.GetReportAsync(new ApprovalGateDogfoodCorrelationReportRequest
        {
            ProjectReferenceId = projectReferenceId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            CorrelationId = correlationId,
            CausationId = causationId,
            ApprovalReferenceId = approvalReferenceId,
            ToolRequestId = toolRequestId,
            ToolGateDecisionId = toolGateDecisionId,
            DogfoodReceiptId = dogfoodReceiptId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Take = take,
            IncludeTraceReferences = includeTraceReferences,
            IncludeMissingEvidence = includeMissingEvidence,
            IncludeRecommendations = includeRecommendations
        }, cancellationToken);

        var body = Envelope(ToWireStatus(response.Status), response.Report, response.Issues, response.BoundaryWarnings);
        return response.Status is ApprovalGateDogfoodCorrelationReportStatus.InvalidRequest
            ? BadRequest(body)
            : Ok(body);
    }

    private static object Envelope(
        string status,
        ApprovalGateDogfoodCorrelationReport? report,
        IReadOnlyList<ApprovalGateDogfoodCorrelationReportIssue> issues,
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
            correlationIsApproval = false,
            correlationIsPolicySatisfaction = false,
            dogfoodReceiptIsReleaseApproval = false,
            toolGateEvidenceIsToolExecution = false,
            reportStatusIsGovernanceStatus = false,
            conflictSignalIsVerdict = false,
            recommendationIsExecution = false,
            reportIsWorkflowTransition = false,
            canApprove = false,
            canReject = false,
            canSatisfyPolicy = false,
            canOpenGate = false,
            canInvokeTool = false,
            canMarkDogfoodPassed = false,
            canApproveRelease = false,
            canTransitionWorkflow = false,
            canDispatchAgent = false,
            canCallModel = false,
            canBuildPrompt = false,
            canCreateTicket = false,
            canPromoteMemory = false,
            canActivateRetrieval = false,
            canApplySource = false,
            canApplyPatch = false,
            createsGovernanceEvent = false,
            createsApprovalDecision = false,
            createsPolicyDecision = false,
            createsDogfoodReceipt = false,
            exposesRawPayloadJson = false,
            exposesPrivateReasoning = false
        };

    private static object ToError(ApprovalGateDogfoodCorrelationReportIssue issue) =>
        new
        {
            code = ToWireIssue(issue.Kind),
            field = issue.Field,
            message = issue.Message
        };

    private static string ToWireStatus(ApprovalGateDogfoodCorrelationReportStatus status) =>
        status switch
        {
            ApprovalGateDogfoodCorrelationReportStatus.InvalidRequest => "validation_error",
            ApprovalGateDogfoodCorrelationReportStatus.NoEvidenceFound => "no_evidence_found",
            ApprovalGateDogfoodCorrelationReportStatus.EvidenceIncomplete => "evidence_incomplete",
            ApprovalGateDogfoodCorrelationReportStatus.ReportAvailable => "report_available",
            _ => "unknown"
        };

    private static string ToWireIssue(ApprovalGateDogfoodCorrelationReportIssueKind kind) =>
        kind switch
        {
            ApprovalGateDogfoodCorrelationReportIssueKind.MissingSelector => "missing_selector",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidProjectReferenceId => "invalid_project_reference_id",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidWorkflowRunId => "invalid_workflow_run_id",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidWorkflowStepId => "invalid_workflow_step_id",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidCorrelationId => "invalid_correlation_id",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidCausationId => "invalid_causation_id",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidDateRange => "invalid_date_range",
            ApprovalGateDogfoodCorrelationReportIssueKind.InvalidTake => "invalid_take",
            ApprovalGateDogfoodCorrelationReportIssueKind.UnsafeQueryText => "unsafe_query_text",
            _ => "unknown"
        };
}

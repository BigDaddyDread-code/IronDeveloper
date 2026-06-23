using IronDev.Core.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/workflow/apply-preview")]
public sealed class ApplyPreviewController : ControllerBase
{
    private readonly IApplyPreviewService _applyPreviewService;

    public ApplyPreviewController(IApplyPreviewService applyPreviewService)
    {
        _applyPreviewService = applyPreviewService ?? throw new ArgumentNullException(nameof(applyPreviewService));
    }

    [HttpGet("{workflowRunId}/{workflowStepId}")]
    public async Task<IActionResult> Get(
        string workflowRunId,
        string workflowStepId,
        [FromQuery] string? controlledApplyPlanReferenceId = null,
        [FromQuery] int takeDryRuns = 10,
        [FromQuery] bool includeDryRunSummaries = true,
        CancellationToken cancellationToken = default)
    {
        var preview = await _applyPreviewService.GetPreviewAsync(new ApplyPreviewRequest
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ControlledApplyPlanReferenceId = controlledApplyPlanReferenceId ?? string.Empty,
            TakeDryRuns = takeDryRuns,
            IncludeDryRunSummaries = includeDryRunSummaries
        }, cancellationToken);

        var body = new
        {
            status = preview.Status is ApplyPreviewStatus.InvalidRequest ? "validation_error" : "succeeded",
            mutationOccurred = false,
            previewStatus = ToWireStatus(preview.Status),
            boundary = Boundary(),
            warnings = Warnings(preview.Status),
            errors = preview.Issues.Select(issue => new
            {
                code = ToWireIssue(issue.Kind),
                field = issue.Field,
                message = issue.Message
            }).ToArray(),
            data = preview
        };

        return preview.Status is ApplyPreviewStatus.InvalidRequest
            ? BadRequest(body)
            : Ok(body);
    }

    private static object Boundary() =>
        new
        {
            readOnlyInspection = true,
            previewOnly = true,
            previewIsSourceApply = false,
            previewIsPatchApply = false,
            previewIsDryRunExecution = false,
            dryRunReceiptIsExecution = false,
            endpointAccessIsExecutionPermission = false,
            apiResponseStatusIsGovernance = false,
            auditEvidenceIsApproval = false,
            sourceApplied = false,
            patchApplied = false,
            dryRunExecuted = false,
            approvalSatisfied = false,
            policySatisfied = false,
            workflowTransitioned = false,
            memoryPromoted = false,
            retrievalActivated = false,
            agentDispatched = false,
            modelOutputIsAuthority = false,
            humanReviewRequiredForSourceApply = true,
            humanReviewRequiredForMemoryPromotion = true
        };

    private static IReadOnlyList<string> Warnings(ApplyPreviewStatus status)
    {
        var warnings = new List<string>
        {
            "Apply preview is read-only inspection evidence.",
            "Apply preview is not source apply.",
            "Apply preview is not patch apply.",
            "Apply preview is not dry-run execution.",
            "Dry-run receipt summaries are not execution permission.",
            "Approval and policy satisfaction remain separate."
        };

        if (status is ApplyPreviewStatus.MissingPreviewEvidence)
            warnings.Add("No matching apply dry-run receipt summary was found.");

        return warnings;
    }

    private static string ToWireStatus(ApplyPreviewStatus status) =>
        status switch
        {
            ApplyPreviewStatus.InvalidRequest => "invalid_request",
            ApplyPreviewStatus.MissingPreviewEvidence => "missing_preview_evidence",
            ApplyPreviewStatus.PreviewAvailable => "preview_available",
            _ => "unknown"
        };

    private static string ToWireIssue(ApplyPreviewIssueKind kind) =>
        kind switch
        {
            ApplyPreviewIssueKind.MissingWorkflowRunId => "missing_workflow_run_id",
            ApplyPreviewIssueKind.MissingWorkflowStepId => "missing_workflow_step_id",
            ApplyPreviewIssueKind.UnsafeRequestText => "unsafe_request_text",
            ApplyPreviewIssueKind.MissingDryRunEvidence => "missing_dry_run_evidence",
            _ => "unknown"
        };
}

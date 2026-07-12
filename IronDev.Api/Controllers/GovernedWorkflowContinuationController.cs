using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/workflow-continuation")]
public sealed class GovernedWorkflowContinuationController : ControllerBase
{
    private readonly IGovernedWorkflowContinuationService _service;

    public GovernedWorkflowContinuationController(IGovernedWorkflowContinuationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpPost("governed")]
    public async Task<ActionResult<GovernedWorkflowContinuationApiEnvelope>> ContinueGoverned(
        [FromRoute] Guid projectId,
        [FromBody] GovernedWorkflowContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProjectId != projectId)
        {
            return BadRequest(Envelope(
                "rejected",
                null,
                [new GovernedWorkflowContinuationApiError("ProjectMismatch", "projectId", "Route projectId must match request projectId.")],
                refused: true));
        }

        var result = await _service.ContinueAsync(request, cancellationToken).ConfigureAwait(false);
        var envelope = Envelope(
            result.Status,
            SanitizeResult(result),
            result.Issues.Select(issue => new GovernedWorkflowContinuationApiError(issue.Code, issue.Field, issue.Message)).ToArray(),
            result.Warnings,
            refused: !result.Succeeded);

        if (result.Succeeded)
            return Ok(envelope);

        if (result.WorkflowStateMutated)
            return StatusCode(StatusCodes.Status500InternalServerError, envelope);

        return BadRequest(envelope);
    }

    private GovernedWorkflowContinuationApiEnvelope Envelope(
        string status,
        GovernedWorkflowContinuationApiResult? data,
        IReadOnlyList<GovernedWorkflowContinuationApiError>? errors = null,
        IReadOnlyList<string>? warnings = null,
        bool refused = false)
    {
        var normalizedErrors = errors ?? [];
        return new(
            status,
            data,
            normalizedErrors,
            warnings ?? GovernedWorkflowContinuationBoundaryText.Warnings,
            new GovernedWorkflowContinuationApiBoundary(
                WorkflowContinuationIsReleaseReadiness: false,
                WorkflowContinuationApprovesRelease: false,
                WorkflowContinuationExecutesSourceApply: false,
                WorkflowContinuationExecutesRollback: false,
                WorkflowContinuationSatisfiesPolicy: false,
                WorkflowContinuationCallsAgentsModelsToolsGitMemoryOrRetrieval: false,
                HumanReviewRequiredForReleaseReadinessAndApproval: true,
                Boundary: GovernedWorkflowContinuationBoundaryText.Boundary))
        {
            Refusal = refused ? ContinuationRefusal(status, normalizedErrors) : null
        };
    }

    private GovernedRefusalEnvelope ContinuationRefusal(
        string status,
        IReadOnlyList<GovernedWorkflowContinuationApiError> errors) =>
        GovernedRefusal.Create(
            errors.FirstOrDefault()?.Code ?? status,
            errors.FirstOrDefault()?.Message ?? "Workflow continuation was refused.",
            HttpContext.TraceIdentifier,
            blockedReasons: errors.Select(error => error.Message),
            missingEvidence: errors.Where(error => error.Code.Contains("Missing", StringComparison.OrdinalIgnoreCase)).Select(error => error.Message),
            nextSafeActions: ["Resolve the blocked reasons, then submit a new continuation request."],
            forbiddenActions: ["Continue workflow state", "Apply source changes", "Approve release"]);

    private static GovernedWorkflowContinuationApiResult SanitizeResult(GovernedWorkflowContinuationResult result) =>
        new(
            result.Status,
            result.Succeeded,
            result.WorkflowStateMutated,
            result.StepCompleted,
            result.NextStepStarted,
            result.ReleaseReadinessInferred,
            result.ReleaseApproved,
            result.SourceApplyExecuted,
            result.RollbackExecuted,
            result.WorkflowTransitionRecord is null
                ? null
                : new GovernedWorkflowContinuationApiTransitionRecord(
                    result.WorkflowTransitionRecord.WorkflowTransitionRecordId,
                    Safe(result.WorkflowTransitionRecord.WorkflowTransitionRecordHash),
                    Safe(result.WorkflowTransitionRecord.WorkflowRunId),
                    Safe(result.WorkflowTransitionRecord.WorkflowStepId),
                    Safe(result.WorkflowTransitionRecord.TransitionKind),
                    result.WorkflowTransitionRecord.WorkflowContinuationGateEvaluationId,
                    Safe(result.WorkflowTransitionRecord.WorkflowContinuationGateEvaluationHash),
                    result.WorkflowTransitionRecord.SourceApplyRequestId,
                    Safe(result.WorkflowTransitionRecord.SourceApplyRequestHash),
                    result.WorkflowTransitionRecord.SourceApplyReceiptId,
                    Safe(result.WorkflowTransitionRecord.SourceApplyReceiptHash),
                    result.WorkflowTransitionRecord.RollbackExecutionReceiptId,
                    Safe(result.WorkflowTransitionRecord.RollbackExecutionReceiptHash),
                    result.WorkflowTransitionRecord.WorkflowStateMutated,
                    result.WorkflowTransitionRecord.StepCompleted,
                    result.WorkflowTransitionRecord.NextStepStarted,
                    result.WorkflowTransitionRecord.ReleaseReadinessInferred,
                    result.WorkflowTransitionRecord.ReleaseApproved,
                    result.WorkflowTransitionRecord.SourceApplyExecuted,
                    result.WorkflowTransitionRecord.RollbackExecuted,
                    result.WorkflowTransitionRecord.TransitionedAtUtc));

    private static string? Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var lower = value.ToLowerInvariant();
        var unsafeMarkers = new[]
        {
            "rawprompt", "raw prompt", "rawcompletion", "raw completion", "rawtooloutput", "raw tool output",
            "chain-of-thought", "chain of thought", "chainofthought", "private reasoning", "hidden reasoning", "scratchpad",
            "entirepatch", "entire patch", "patchpayload", "patch payload"
        };

        return unsafeMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal))
            ? "[redacted]"
            : value.Trim();
    }
}

public sealed record GovernedWorkflowContinuationApiEnvelope(
    string Status,
    GovernedWorkflowContinuationApiResult? Data,
    IReadOnlyList<GovernedWorkflowContinuationApiError> Errors,
    IReadOnlyList<string> Warnings,
    GovernedWorkflowContinuationApiBoundary Boundary)
{
    public GovernedRefusalEnvelope? Refusal { get; init; }
}

public sealed record GovernedWorkflowContinuationApiResult(
    string Status,
    bool Succeeded,
    bool WorkflowStateMutated,
    bool StepCompleted,
    bool NextStepStarted,
    bool ReleaseReadinessInferred,
    bool ReleaseApproved,
    bool SourceApplyExecuted,
    bool RollbackExecuted,
    GovernedWorkflowContinuationApiTransitionRecord? WorkflowTransitionRecord);

public sealed record GovernedWorkflowContinuationApiTransitionRecord(
    Guid WorkflowTransitionRecordId,
    string? WorkflowTransitionRecordHash,
    string? WorkflowRunId,
    string? WorkflowStepId,
    string? TransitionKind,
    Guid WorkflowContinuationGateEvaluationId,
    string? WorkflowContinuationGateEvaluationHash,
    Guid SourceApplyRequestId,
    string? SourceApplyRequestHash,
    Guid SourceApplyReceiptId,
    string? SourceApplyReceiptHash,
    Guid? RollbackExecutionReceiptId,
    string? RollbackExecutionReceiptHash,
    bool WorkflowStateMutated,
    bool StepCompleted,
    bool NextStepStarted,
    bool ReleaseReadinessInferred,
    bool ReleaseApproved,
    bool SourceApplyExecuted,
    bool RollbackExecuted,
    DateTimeOffset TransitionedAtUtc);

public sealed record GovernedWorkflowContinuationApiError(string Code, string Field, string Message);

public sealed record GovernedWorkflowContinuationApiBoundary(
    bool WorkflowContinuationIsReleaseReadiness,
    bool WorkflowContinuationApprovesRelease,
    bool WorkflowContinuationExecutesSourceApply,
    bool WorkflowContinuationExecutesRollback,
    bool WorkflowContinuationSatisfiesPolicy,
    bool WorkflowContinuationCallsAgentsModelsToolsGitMemoryOrRetrieval,
    bool HumanReviewRequiredForReleaseReadinessAndApproval,
    string Boundary);

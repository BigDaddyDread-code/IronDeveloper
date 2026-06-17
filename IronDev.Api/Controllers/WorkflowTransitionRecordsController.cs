using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/workflow-transition-records")]
public sealed class WorkflowTransitionRecordsController : ControllerBase
{
    private const int MaxRouteValueLength = 512;
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "continue workflow",
        "continued workflow",
        "workflow continued",
        "advance workflow",
        "complete step",
        "start next step",
        "release ready",
        "release readiness",
        "release approved",
        "approve release",
        "source applied",
        "apply source",
        "rollback executed",
        "rollback cleanup",
        "memory promoted",
        "retrieval activated"
    ];

    private readonly IWorkflowTransitionRecordQueryService _query;

    public WorkflowTransitionRecordsController(IWorkflowTransitionRecordQueryService query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    [HttpGet("{workflowTransitionRecordId:guid}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid workflowTransitionRecordId,
        CancellationToken cancellationToken)
    {
        var record = await _query.GetAsync(projectId, workflowTransitionRecordId, cancellationToken);
        if (record is null)
        {
            return NotFound(Envelope<WorkflowTransitionRecordReadModel>(
                "not_found",
                null,
                errors: [Error("workflowTransitionRecordId", "Workflow transition record was not found for this project.", "not_found")]));
        }

        return Ok(Envelope("found", record));
    }

    [HttpGet("by-hash/{workflowTransitionRecordHash}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordReadModel>>> GetByRecordHash(
        [FromRoute] Guid projectId,
        [FromRoute] string workflowTransitionRecordHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateHashLookup(workflowTransitionRecordHash, nameof(workflowTransitionRecordHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<WorkflowTransitionRecordReadModel>("validation_error", null, errors: errors));
        }

        var record = await _query.GetByRecordHashAsync(projectId, workflowTransitionRecordHash, cancellationToken);
        if (record is null)
        {
            return NotFound(Envelope<WorkflowTransitionRecordReadModel>(
                "not_found",
                null,
                errors: [Error("workflowTransitionRecordHash", "Workflow transition record hash was not found for this project.", "not_found")]));
        }

        return Ok(Envelope("found", record));
    }

    [HttpGet("by-workflow-run/{workflowRunId}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordListReadModel>>> ListByWorkflowRun(
        [FromRoute] Guid projectId,
        [FromRoute] string workflowRunId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateLookupText(workflowRunId, nameof(workflowRunId))
            .Concat(ValidateTake(take))
            .ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<WorkflowTransitionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByWorkflowRunAsync(projectId, workflowRunId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-workflow-step/{workflowRunId}/{workflowStepId}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordListReadModel>>> ListByWorkflowStep(
        [FromRoute] Guid projectId,
        [FromRoute] string workflowRunId,
        [FromRoute] string workflowStepId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateLookupText(workflowRunId, nameof(workflowRunId))
            .Concat(ValidateLookupText(workflowStepId, nameof(workflowStepId)))
            .Concat(ValidateTake(take))
            .ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<WorkflowTransitionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByWorkflowStepAsync(projectId, workflowRunId, workflowStepId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-continuation-gate/{workflowContinuationGateEvaluationId:guid}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordListReadModel>>> ListByContinuationGateEvaluation(
        [FromRoute] Guid projectId,
        [FromRoute] Guid workflowContinuationGateEvaluationId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateTake(take).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<WorkflowTransitionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByContinuationGateEvaluationAsync(projectId, workflowContinuationGateEvaluationId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-source-apply-receipt/{sourceApplyReceiptId:guid}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordListReadModel>>> ListBySourceApplyReceipt(
        [FromRoute] Guid projectId,
        [FromRoute] Guid sourceApplyReceiptId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateTake(take).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<WorkflowTransitionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListBySourceApplyReceiptAsync(projectId, sourceApplyReceiptId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-rollback-execution-receipt/{rollbackExecutionReceiptId:guid}")]
    public async Task<ActionResult<WorkflowTransitionRecordApiEnvelope<WorkflowTransitionRecordListReadModel>>> ListByRollbackExecutionReceipt(
        [FromRoute] Guid projectId,
        [FromRoute] Guid rollbackExecutionReceiptId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateTake(take).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<WorkflowTransitionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByRollbackExecutionReceiptAsync(projectId, rollbackExecutionReceiptId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    private static IEnumerable<WorkflowTransitionRecordApiErrorDto> ValidateHashLookup(string? value, string field)
    {
        foreach (var error in ValidateLookupText(value, field))
        {
            yield return error;
        }

        if (!string.IsNullOrWhiteSpace(value) && !value.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            yield return Error(field, "Hash lookup value must use sha256 prefix.");
        }
    }

    private static IEnumerable<WorkflowTransitionRecordApiErrorDto> ValidateLookupText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return Error(field, "Value is required.");
            yield break;
        }

        if (value.Length > MaxRouteValueLength)
        {
            yield return Error(field, "Value is too long.", "content_too_large");
        }

        if (ContainsAny(value, PrivateMaterialMarkers))
        {
            yield return Error(field, "Workflow transition record read API does not accept private, raw, prompt, scratchpad, chain-of-thought, or secret-like lookup material.");
        }

        if (ContainsAny(value, AuthorityClaimMarkers))
        {
            yield return Error(field, "Workflow transition record read API does not accept workflow continuation, workflow mutation, release, source apply, rollback, memory, or retrieval authority claims.");
        }
    }

    private static IEnumerable<WorkflowTransitionRecordApiErrorDto> ValidateTake(int take)
    {
        if (take <= 0 || take > MaxTake)
        {
            yield return Error("take", "Take must be between 1 and 500.");
        }
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static WorkflowTransitionRecordApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        IReadOnlyList<WorkflowTransitionRecordApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            Boundary = new WorkflowTransitionRecordReadBoundary(),
            MutationOccurredInThisApi = false,
            WorkflowContinuationExecutedByThisApi = false,
            ReleaseReadinessInferredByThisApi = false,
            ReleaseApprovedByThisApi = false,
            HumanReviewRequired = true,
            Warnings = WorkflowTransitionRecordReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static WorkflowTransitionRecordApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record WorkflowTransitionRecordApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public required WorkflowTransitionRecordReadBoundary Boundary { get; init; }
    public bool MutationOccurredInThisApi { get; init; }
    public bool WorkflowContinuationExecutedByThisApi { get; init; }
    public bool ReleaseReadinessInferredByThisApi { get; init; }
    public bool ReleaseApprovedByThisApi { get; init; }
    public bool HumanReviewRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<WorkflowTransitionRecordApiErrorDto> Errors { get; init; } = [];
}

public sealed record WorkflowTransitionRecordApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

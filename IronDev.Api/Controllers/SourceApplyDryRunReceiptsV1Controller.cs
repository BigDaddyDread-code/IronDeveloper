using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/source-apply-dry-run-receipts")]
public sealed class SourceApplyDryRunReceiptsV1Controller : ControllerBase
{
    private const int MaxRouteValueLength = 512;

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
        "dry-run performed",
        "dry run performed",
        "source applied",
        "source apply succeeded",
        "source mutated",
        "files written",
        "patch applied",
        "rollback executed",
        "workflow continued",
        "approval satisfied",
        "policy satisfied",
        "release approved",
        "release ready",
        "can apply source"
    ];

    private readonly ISourceApplyDryRunReceiptQueryService _query;

    public SourceApplyDryRunReceiptsV1Controller(ISourceApplyDryRunReceiptQueryService query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    [HttpGet("{sourceApplyDryRunReceiptId:guid}")]
    public async Task<ActionResult<SourceApplyDryRunReceiptApiEnvelope<SourceApplyDryRunReceiptReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid sourceApplyDryRunReceiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await _query.GetAsync(projectId, sourceApplyDryRunReceiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound(DataEnvelope<SourceApplyDryRunReceiptReadModel>(
                "not_found",
                null,
                sourceApplyDryRunReceiptId,
                errors: [Error("sourceApplyDryRunReceiptId", "Source-apply dry-run receipt was not found for this project.", "not_found")]));
        }

        return Ok(DataEnvelope("found", receipt, sourceApplyDryRunReceiptId));
    }

    [HttpGet("by-hash/{sourceApplyDryRunReceiptHash}")]
    public async Task<ActionResult<SourceApplyDryRunReceiptApiEnvelope<SourceApplyDryRunReceiptReadModel>>> GetByReceiptHash(
        [FromRoute] Guid projectId,
        [FromRoute] string sourceApplyDryRunReceiptHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(sourceApplyDryRunReceiptHash, nameof(sourceApplyDryRunReceiptHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(DataEnvelope<SourceApplyDryRunReceiptReadModel>("validation_error", null, errors: errors));
        }

        var receipt = await _query.GetByReceiptHashAsync(projectId, sourceApplyDryRunReceiptHash, cancellationToken);
        if (receipt is null)
        {
            return NotFound(DataEnvelope<SourceApplyDryRunReceiptReadModel>(
                "not_found",
                null,
                errors: [Error("sourceApplyDryRunReceiptHash", "Source-apply dry-run receipt hash was not found for this project.", "not_found")]));
        }

        return Ok(DataEnvelope("found", receipt, receipt.SourceApplyDryRunReceiptId));
    }

    [HttpGet("by-source-apply-request/{sourceApplyRequestId:guid}")]
    public async Task<ActionResult<SourceApplyDryRunReceiptApiEnvelope<IReadOnlyList<SourceApplyDryRunReceiptReadModel>>>> ListBySourceApplyRequest(
        [FromRoute] Guid projectId,
        [FromRoute] Guid sourceApplyRequestId,
        CancellationToken cancellationToken)
    {
        var receipts = await _query.ListBySourceApplyRequestAsync(projectId, sourceApplyRequestId, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    [HttpGet("by-source-apply-gate/{sourceApplyGateEvaluationId:guid}")]
    public async Task<ActionResult<SourceApplyDryRunReceiptApiEnvelope<IReadOnlyList<SourceApplyDryRunReceiptReadModel>>>> ListBySourceApplyGateEvaluation(
        [FromRoute] Guid projectId,
        [FromRoute] Guid sourceApplyGateEvaluationId,
        CancellationToken cancellationToken)
    {
        var receipts = await _query.ListBySourceApplyGateEvaluationAsync(projectId, sourceApplyGateEvaluationId, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    [HttpGet("by-patch-artifact/{patchArtifactId:guid}")]
    public async Task<ActionResult<SourceApplyDryRunReceiptApiEnvelope<IReadOnlyList<SourceApplyDryRunReceiptReadModel>>>> ListByPatchArtifact(
        [FromRoute] Guid projectId,
        [FromRoute] Guid patchArtifactId,
        CancellationToken cancellationToken)
    {
        var receipts = await _query.ListByPatchArtifactAsync(projectId, patchArtifactId, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    [HttpGet("by-rollback-support/{rollbackSupportReceiptId:guid}")]
    public async Task<ActionResult<SourceApplyDryRunReceiptApiEnvelope<IReadOnlyList<SourceApplyDryRunReceiptReadModel>>>> ListByRollbackSupportReceipt(
        [FromRoute] Guid projectId,
        [FromRoute] Guid rollbackSupportReceiptId,
        CancellationToken cancellationToken)
    {
        var receipts = await _query.ListByRollbackSupportReceiptAsync(projectId, rollbackSupportReceiptId, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    private static IReadOnlyList<SourceApplyDryRunReceiptApiErrorDto> ValidateLookupText(string? value, string field)
    {
        var errors = new List<SourceApplyDryRunReceiptApiErrorDto>();

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(Error(field, "Value is required."));
            return errors;
        }

        if (value.Length > MaxRouteValueLength)
        {
            errors.Add(Error(field, "Value is too long.", "content_too_large"));
        }

        if (ContainsAny(value, PrivateMaterialMarkers))
        {
            errors.Add(Error(field, "Source Apply Dry-run Receipt Read API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, secret-like, or private reasoning lookup material."));
        }

        if (ContainsAny(value, AuthorityClaimMarkers))
        {
            errors.Add(Error(field, "Source Apply Dry-run Receipt Read API v1 does not accept dry-run execution, source apply, file mutation, patch application, workflow continuation, approval satisfaction, policy satisfaction, rollback execution, or release approval claims."));
        }

        return errors;
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static SourceApplyDryRunReceiptApiEnvelope<TData> DataEnvelope<TData>(
        string status,
        TData? data,
        Guid? sourceApplyDryRunReceiptId = null,
        IReadOnlyList<SourceApplyDryRunReceiptApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            SourceApplyDryRunReceiptId = sourceApplyDryRunReceiptId,
            Boundary = new SourceApplyDryRunReceiptReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = SourceApplyDryRunReceiptReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static SourceApplyDryRunReceiptApiEnvelope<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListEnvelope(
        string status,
        IReadOnlyList<SourceApplyDryRunReceiptReadModel>? items,
        IReadOnlyList<SourceApplyDryRunReceiptApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Items = items ?? [],
            Boundary = new SourceApplyDryRunReceiptReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = SourceApplyDryRunReceiptReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static SourceApplyDryRunReceiptApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record SourceApplyDryRunReceiptApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public IReadOnlyList<SourceApplyDryRunReceiptReadModel> Items { get; init; } = [];
    public Guid? SourceApplyDryRunReceiptId { get; init; }
    public required SourceApplyDryRunReceiptReadBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<SourceApplyDryRunReceiptApiErrorDto> Errors { get; init; } = [];
}

public sealed record SourceApplyDryRunReceiptApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

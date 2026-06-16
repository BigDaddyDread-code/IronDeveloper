using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/rollback-support-receipts")]
public sealed class RollbackSupportReceiptsV1Controller : ControllerBase
{
    private const int MaxRouteValueLength = 512;

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
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
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "rollback executed",
        "rollback succeeded",
        "source applied",
        "applies source",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready",
        "can apply source"
    ];

    private readonly IRollbackSupportReceiptQueryService _query;

    public RollbackSupportReceiptsV1Controller(IRollbackSupportReceiptQueryService query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    [HttpGet("{rollbackSupportReceiptId:guid}")]
    public async Task<ActionResult<RollbackSupportReceiptApiEnvelope<RollbackSupportReceiptReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid rollbackSupportReceiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await _query.GetAsync(projectId, rollbackSupportReceiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound(DataEnvelope<RollbackSupportReceiptReadModel>(
                "not_found",
                null,
                rollbackSupportReceiptId,
                errors: [Error("rollbackSupportReceiptId", "Rollback support receipt was not found for this project.", "not_found")]));
        }

        return Ok(DataEnvelope("found", receipt, rollbackSupportReceiptId));
    }

    [HttpGet("by-hash/{rollbackSupportReceiptHash}")]
    public async Task<ActionResult<RollbackSupportReceiptApiEnvelope<RollbackSupportReceiptReadModel>>> GetByReceiptHash(
        [FromRoute] Guid projectId,
        [FromRoute] string rollbackSupportReceiptHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(rollbackSupportReceiptHash, nameof(rollbackSupportReceiptHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(DataEnvelope<RollbackSupportReceiptReadModel>("validation_error", null, errors: errors));
        }

        var receipt = await _query.GetByReceiptHashAsync(projectId, rollbackSupportReceiptHash, cancellationToken);
        if (receipt is null)
        {
            return NotFound(DataEnvelope<RollbackSupportReceiptReadModel>(
                "not_found",
                null,
                errors: [Error("rollbackSupportReceiptHash", "Rollback support receipt hash was not found for this project.", "not_found")]));
        }

        return Ok(DataEnvelope("found", receipt, receipt.RollbackSupportReceiptId));
    }

    [HttpGet("by-patch-artifact/{patchArtifactId:guid}")]
    public async Task<ActionResult<RollbackSupportReceiptApiEnvelope<IReadOnlyList<RollbackSupportReceiptReadModel>>>> ListByPatchArtifact(
        [FromRoute] Guid projectId,
        [FromRoute] Guid patchArtifactId,
        CancellationToken cancellationToken)
    {
        var receipts = await _query.ListByPatchArtifactAsync(projectId, patchArtifactId, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    [HttpGet("by-patch-hash/{patchHash}")]
    public async Task<ActionResult<RollbackSupportReceiptApiEnvelope<IReadOnlyList<RollbackSupportReceiptReadModel>>>> ListByPatchHash(
        [FromRoute] Guid projectId,
        [FromRoute] string patchHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(patchHash, nameof(patchHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var receipts = await _query.ListByPatchHashAsync(projectId, patchHash, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    [HttpGet("by-rollback-plan/{rollbackPlanId:guid}")]
    public async Task<ActionResult<RollbackSupportReceiptApiEnvelope<IReadOnlyList<RollbackSupportReceiptReadModel>>>> ListByRollbackPlan(
        [FromRoute] Guid projectId,
        [FromRoute] Guid rollbackPlanId,
        CancellationToken cancellationToken)
    {
        var receipts = await _query.ListByRollbackPlanAsync(projectId, rollbackPlanId, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    [HttpGet("by-source-baseline/{sourceBaselineHash}")]
    public async Task<ActionResult<RollbackSupportReceiptApiEnvelope<IReadOnlyList<RollbackSupportReceiptReadModel>>>> ListBySourceBaselineHash(
        [FromRoute] Guid projectId,
        [FromRoute] string sourceBaselineHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(sourceBaselineHash, nameof(sourceBaselineHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var receipts = await _query.ListBySourceBaselineHashAsync(projectId, sourceBaselineHash, cancellationToken);
        return Ok(ListEnvelope("found", receipts));
    }

    private static IReadOnlyList<RollbackSupportReceiptApiErrorDto> ValidateLookupText(string? value, string field)
    {
        var errors = new List<RollbackSupportReceiptApiErrorDto>();

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
            errors.Add(Error(field, "Rollback Support Receipt Read API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, secret-like, or private reasoning lookup material."));
        }

        if (ContainsAny(value, AuthorityClaimMarkers))
        {
            errors.Add(Error(field, "Rollback Support Receipt Read API v1 does not accept rollback execution, source apply, workflow continuation, or release approval claims."));
        }

        return errors;
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static RollbackSupportReceiptApiEnvelope<TData> DataEnvelope<TData>(
        string status,
        TData? data,
        Guid? rollbackSupportReceiptId = null,
        IReadOnlyList<RollbackSupportReceiptApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            Boundary = new RollbackSupportReceiptReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = RollbackSupportReceiptReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static RollbackSupportReceiptApiEnvelope<IReadOnlyList<RollbackSupportReceiptReadModel>> ListEnvelope(
        string status,
        IReadOnlyList<RollbackSupportReceiptReadModel>? items,
        IReadOnlyList<RollbackSupportReceiptApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Items = items ?? [],
            Boundary = new RollbackSupportReceiptReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = RollbackSupportReceiptReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static RollbackSupportReceiptApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record RollbackSupportReceiptApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public IReadOnlyList<RollbackSupportReceiptReadModel> Items { get; init; } = [];
    public Guid? RollbackSupportReceiptId { get; init; }
    public required RollbackSupportReceiptReadBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<RollbackSupportReceiptApiErrorDto> Errors { get; init; } = [];
}

public sealed record RollbackSupportReceiptApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/v1/projects/{projectId:guid}/patch-artifacts")]
public sealed class PatchArtifactsV1Controller : ControllerBase
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
        "source applied",
        "applies source",
        "rollback executed",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready"
    ];

    private readonly IPatchArtifactQueryService _query;

    public PatchArtifactsV1Controller(IPatchArtifactQueryService query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    [HttpGet("{patchArtifactId:guid}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<PatchArtifactReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid patchArtifactId,
        CancellationToken cancellationToken)
    {
        var artifact = await _query.GetAsync(projectId, patchArtifactId, cancellationToken);
        if (artifact is null)
        {
            return NotFound(DataEnvelope<PatchArtifactReadModel>(
                "not_found",
                null,
                patchArtifactId,
                errors: [Error("patchArtifactId", "Patch artifact was not found for this project.", "not_found")]));
        }

        return Ok(DataEnvelope("found", artifact, patchArtifactId));
    }

    [HttpGet("by-dry-run-receipt-hash/{dryRunReceiptHash}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>>>> ListByDryRunReceiptHash(
        [FromRoute] Guid projectId,
        [FromRoute] string dryRunReceiptHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(dryRunReceiptHash, nameof(dryRunReceiptHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var artifacts = await _query.ListByDryRunReceiptHashAsync(projectId, dryRunReceiptHash, cancellationToken);
        return Ok(ListEnvelope("found", artifacts));
    }

    [HttpGet("by-dry-run-audit-hash/{dryRunAuditHash}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>>>> ListByDryRunAuditHash(
        [FromRoute] Guid projectId,
        [FromRoute] string dryRunAuditHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(dryRunAuditHash, nameof(dryRunAuditHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var artifacts = await _query.ListByDryRunAuditHashAsync(projectId, dryRunAuditHash, cancellationToken);
        return Ok(ListEnvelope("found", artifacts));
    }

    [HttpGet("by-controlled-dry-run-request/{controlledDryRunRequestId:guid}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>>>> ListByControlledDryRunRequest(
        [FromRoute] Guid projectId,
        [FromRoute] Guid controlledDryRunRequestId,
        CancellationToken cancellationToken)
    {
        var artifacts = await _query.ListByControlledDryRunRequestAsync(projectId, controlledDryRunRequestId, cancellationToken);
        return Ok(ListEnvelope("found", artifacts));
    }

    [HttpGet("by-subject/{subjectKind}/{subjectId}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>>>> ListBySubject(
        [FromRoute] Guid projectId,
        [FromRoute] string subjectKind,
        [FromRoute] string subjectId,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(subjectKind, nameof(subjectKind))
            .Concat(ValidateLookupText(subjectId, nameof(subjectId)))
            .ToArray();

        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var artifacts = await _query.ListBySubjectAsync(projectId, subjectKind, subjectId, cancellationToken);
        return Ok(ListEnvelope("found", artifacts));
    }

    [HttpGet("by-patch-hash/{patchHash}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>>>> ListByPatchHash(
        [FromRoute] Guid projectId,
        [FromRoute] string patchHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(patchHash, nameof(patchHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var artifacts = await _query.ListByPatchHashAsync(projectId, patchHash, cancellationToken);
        return Ok(ListEnvelope("found", artifacts));
    }

    [HttpGet("by-source-baseline-hash/{sourceBaselineHash}")]
    public async Task<ActionResult<PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>>>> ListBySourceBaselineHash(
        [FromRoute] Guid projectId,
        [FromRoute] string sourceBaselineHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(sourceBaselineHash, nameof(sourceBaselineHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(ListEnvelope("validation_error", null, errors));
        }

        var artifacts = await _query.ListBySourceBaselineHashAsync(projectId, sourceBaselineHash, cancellationToken);
        return Ok(ListEnvelope("found", artifacts));
    }

    private static IReadOnlyList<PatchArtifactApiErrorDto> ValidateLookupText(string? value, string field)
    {
        var errors = new List<PatchArtifactApiErrorDto>();

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
            errors.Add(Error(field, "Patch Artifact Read API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, secret-like, or private reasoning lookup material."));
        }

        if (ContainsAny(value, AuthorityClaimMarkers))
        {
            errors.Add(Error(field, "Patch Artifact Read API v1 does not accept source apply, rollback, workflow continuation, or release approval claims."));
        }

        return errors;
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static PatchArtifactApiEnvelope<TData> DataEnvelope<TData>(
        string status,
        TData? data,
        Guid? patchArtifactId = null,
        IReadOnlyList<PatchArtifactApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            PatchArtifactId = patchArtifactId,
            Boundary = new PatchArtifactReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = PatchArtifactReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static PatchArtifactApiEnvelope<IReadOnlyList<PatchArtifactReadModel>> ListEnvelope(
        string status,
        IReadOnlyList<PatchArtifactReadModel>? items,
        IReadOnlyList<PatchArtifactApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Items = items ?? [],
            Boundary = new PatchArtifactReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = PatchArtifactReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static PatchArtifactApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record PatchArtifactApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public IReadOnlyList<PatchArtifactReadModel> Items { get; init; } = [];
    public Guid? PatchArtifactId { get; init; }
    public required PatchArtifactReadBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<PatchArtifactApiErrorDto> Errors { get; init; } = [];
}

public sealed record PatchArtifactApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

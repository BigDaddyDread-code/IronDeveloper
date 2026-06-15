using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/accepted-approvals")]
public sealed class AcceptedApprovalsV1Controller : ControllerBase
{
    private const int MaxIdLength = 256;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "hidden reasoning",
        "hidden deliberation",
        "private reasoning",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "entire patch",
        "entirepatch"
    ];

    private static readonly string[] SensitiveMarkers =
    [
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer "
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "approved for execution",
        "policy cleared",
        "policy override",
        "execution permitted",
        "tool executed",
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "release approved",
        "release ready",
        "ready to ship",
        "workflow continued",
        "dry-run executed",
        "patch artifact created"
    ];

    private readonly IAcceptedApprovalQueryService _query;

    public AcceptedApprovalsV1Controller(IAcceptedApprovalQueryService query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    [HttpGet("{acceptedApprovalId:guid}")]
    public async Task<ActionResult<AcceptedApprovalApiEnvelope<AcceptedApprovalReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid acceptedApprovalId,
        CancellationToken cancellationToken)
    {
        var record = await _query.GetAsync(projectId, acceptedApprovalId, cancellationToken);
        if (record is null)
        {
            return NotFound(Envelope<AcceptedApprovalReadModel>(
                "not_found",
                null,
                acceptedApprovalId,
                errors: [Error("acceptedApprovalId", "Accepted approval was not found for this project.", "not_found")]));
        }

        return Ok(Envelope("found", record, acceptedApprovalId));
    }

    [HttpGet("by-target/{approvalTargetKind}/{approvalTargetId}")]
    public async Task<ActionResult<AcceptedApprovalApiEnvelope<IReadOnlyList<AcceptedApprovalReadModel>>>> ListByTarget(
        [FromRoute] Guid projectId,
        [FromRoute] string approvalTargetKind,
        [FromRoute] string approvalTargetId,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(approvalTargetKind, nameof(approvalTargetKind))
            .Concat(ValidateLookupText(approvalTargetId, nameof(approvalTargetId)))
            .ToArray();

        if (errors.Length > 0)
        {
            return BadRequest(Envelope<IReadOnlyList<AcceptedApprovalReadModel>>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByTargetAsync(projectId, approvalTargetKind, approvalTargetId, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-correlation/{correlationId}")]
    public async Task<ActionResult<AcceptedApprovalApiEnvelope<IReadOnlyList<AcceptedApprovalReadModel>>>> ListByCorrelation(
        [FromRoute] Guid projectId,
        [FromRoute] string correlationId,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(correlationId, nameof(correlationId)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<IReadOnlyList<AcceptedApprovalReadModel>>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByProjectAndCorrelationAsync(projectId, correlationId, cancellationToken);
        return Ok(Envelope("found", records));
    }

    private static IReadOnlyList<AcceptedApprovalApiErrorDto> ValidateLookupText(string? value, string field)
    {
        var errors = new List<AcceptedApprovalApiErrorDto>();

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(Error(field, "Value is required."));
            return errors;
        }

        if (value.Length > MaxIdLength)
        {
            errors.Add(Error(field, "Value is too long.", "content_too_large"));
        }

        if (!IsSafeId(value))
        {
            errors.Add(Error(field, "Value contains unsupported characters."));
        }

        if (ContainsAny([value], PrivateReasoningMarkers))
        {
            errors.Add(Error(field, "Accepted Approval Read API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning."));
        }

        if (ContainsAny([value], SensitiveMarkers))
        {
            errors.Add(Error(field, "Accepted Approval Read API v1 does not accept secret-like lookup material."));
        }

        if (ContainsAny([value], AuthorityMarkers))
        {
            errors.Add(Error(field, "Accepted Approval Read API v1 does not accept execution, source apply, release approval, policy satisfaction, workflow continuation, or memory promotion claims."));
        }

        return errors;
    }

    private static bool IsSafeId(string value) =>
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static bool ContainsAny(IEnumerable<string?> values, IEnumerable<string> markers) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static AcceptedApprovalApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        Guid? acceptedApprovalId = null,
        IReadOnlyList<AcceptedApprovalApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            AcceptedApprovalId = acceptedApprovalId,
            Boundary = new AcceptedApprovalReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = AcceptedApprovalReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static AcceptedApprovalApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record AcceptedApprovalApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public Guid? AcceptedApprovalId { get; init; }
    public required AcceptedApprovalReadBoundary Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<AcceptedApprovalApiErrorDto> Errors { get; init; } = [];
}

public sealed record AcceptedApprovalApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

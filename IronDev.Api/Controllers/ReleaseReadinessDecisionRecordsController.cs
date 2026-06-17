using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/release-readiness-decision-records")]
public sealed class ReleaseReadinessDecisionRecordsController : ControllerBase
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
        "approval granted",
        "release approved",
        "approve release",
        "deployment approved",
        "merge approved",
        "release executed",
        "execute release",
        "source applied",
        "apply source",
        "rollback executed",
        "continue workflow",
        "workflow continued",
        string.Concat("git ", "committed"),
        string.Concat("git ", "pushed"),
        "pull request created",
        "memory promoted",
        "retrieval activated"
    ];

    private readonly IReleaseReadinessDecisionRecordQueryService _query;

    public ReleaseReadinessDecisionRecordsController(IReleaseReadinessDecisionRecordQueryService query) =>
        _query = query ?? throw new ArgumentNullException(nameof(query));

    [HttpGet("{releaseReadinessDecisionRecordId:guid}")]
    public async Task<ActionResult<ReleaseReadinessDecisionRecordApiEnvelope<ReleaseReadinessDecisionRecordReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid releaseReadinessDecisionRecordId,
        CancellationToken cancellationToken)
    {
        var record = await _query.GetAsync(projectId, releaseReadinessDecisionRecordId, cancellationToken);
        if (record is null)
        {
            return NotFound(Envelope<ReleaseReadinessDecisionRecordReadModel>(
                "not_found",
                null,
                errors: [Error("releaseReadinessDecisionRecordId", "Release readiness decision record was not found for this project.", "not_found")]));
        }

        return Ok(Envelope("found", record));
    }

    [HttpGet("by-hash/{releaseReadinessDecisionRecordHash}")]
    public async Task<ActionResult<ReleaseReadinessDecisionRecordApiEnvelope<ReleaseReadinessDecisionRecordReadModel>>> GetByRecordHash(
        [FromRoute] Guid projectId,
        [FromRoute] string releaseReadinessDecisionRecordHash,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRawSha256HashLookup(releaseReadinessDecisionRecordHash, nameof(releaseReadinessDecisionRecordHash)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<ReleaseReadinessDecisionRecordReadModel>("validation_error", null, errors: errors));
        }

        var record = await _query.GetByRecordHashAsync(projectId, releaseReadinessDecisionRecordHash, cancellationToken);
        if (record is null)
        {
            return NotFound(Envelope<ReleaseReadinessDecisionRecordReadModel>(
                "not_found",
                null,
                errors: [Error("releaseReadinessDecisionRecordHash", "Release readiness decision record hash was not found for this project.", "not_found")]));
        }

        return Ok(Envelope("found", record));
    }

    [HttpGet("by-report/{releaseReadinessReportId:guid}")]
    public async Task<ActionResult<ReleaseReadinessDecisionRecordApiEnvelope<ReleaseReadinessDecisionRecordListReadModel>>> ListByReleaseReadinessReport(
        [FromRoute] Guid projectId,
        [FromRoute] Guid releaseReadinessReportId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateTake(take).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<ReleaseReadinessDecisionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByReleaseReadinessReportAsync(projectId, releaseReadinessReportId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-workflow-run/{workflowRunId}")]
    public async Task<ActionResult<ReleaseReadinessDecisionRecordApiEnvelope<ReleaseReadinessDecisionRecordListReadModel>>> ListByWorkflowRun(
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
            return BadRequest(Envelope<ReleaseReadinessDecisionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByWorkflowRunAsync(projectId, workflowRunId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-subject/{subjectKind}/{subjectId}")]
    public async Task<ActionResult<ReleaseReadinessDecisionRecordApiEnvelope<ReleaseReadinessDecisionRecordListReadModel>>> ListBySubject(
        [FromRoute] Guid projectId,
        [FromRoute] string subjectKind,
        [FromRoute] string subjectId,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateLookupText(subjectKind, nameof(subjectKind))
            .Concat(ValidateLookupText(subjectId, nameof(subjectId)))
            .Concat(ValidateTake(take))
            .ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<ReleaseReadinessDecisionRecordListReadModel>("validation_error", null, errors: errors));
        }

        var records = await _query.ListBySubjectAsync(projectId, subjectKind, subjectId, take, cancellationToken);
        return Ok(Envelope("found", records));
    }

    private static IEnumerable<ReleaseReadinessDecisionRecordApiErrorDto> ValidateRawSha256HashLookup(string? value, string field)
    {
        foreach (var error in ValidateLookupText(value, field))
        {
            yield return error;
        }

        if (!string.IsNullOrWhiteSpace(value) && !IsRawSha256Hex(value.Trim()))
        {
            yield return Error(field, "Hash lookup value must be a raw 64-character hexadecimal SHA-256 hash without a prefix.");
        }
    }

    private static IEnumerable<ReleaseReadinessDecisionRecordApiErrorDto> ValidateLookupText(string? value, string field)
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
            yield return Error(field, "Release readiness decision record read API does not accept private, raw, prompt, scratchpad, chain-of-thought, or secret-like lookup material.");
        }

        if (ContainsAny(value, AuthorityClaimMarkers))
        {
            yield return Error(field, "Release readiness decision record read API does not accept release approval, deployment approval, merge approval, release execution, source apply, rollback, workflow, git, memory, or retrieval authority claims.");
        }
    }

    private static IEnumerable<ReleaseReadinessDecisionRecordApiErrorDto> ValidateTake(int take)
    {
        if (take <= 0 || take > MaxTake)
        {
            yield return Error("take", "Take must be between 1 and 500.");
        }
    }

    private static bool IsRawSha256Hex(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static ReleaseReadinessDecisionRecordApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        IReadOnlyList<ReleaseReadinessDecisionRecordApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            Boundary = new ReleaseReadinessDecisionRecordReadBoundary(),
            MutationOccurredInThisApi = false,
            ReleaseReadinessGateRanByThisApi = false,
            ReleaseApprovedByThisApi = false,
            DeploymentApprovedByThisApi = false,
            MergeApprovedByThisApi = false,
            ReleaseExecutedByThisApi = false,
            SourceApplyExecutedByThisApi = false,
            RollbackExecutedByThisApi = false,
            WorkflowContinuedByThisApi = false,
            GitOperationExecutedByThisApi = false,
            HumanReviewRequired = true,
            Warnings = ReleaseReadinessDecisionRecordReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static ReleaseReadinessDecisionRecordApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record ReleaseReadinessDecisionRecordApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public required ReleaseReadinessDecisionRecordReadBoundary Boundary { get; init; }
    public bool MutationOccurredInThisApi { get; init; }
    public bool ReleaseReadinessGateRanByThisApi { get; init; }
    public bool ReleaseApprovedByThisApi { get; init; }
    public bool DeploymentApprovedByThisApi { get; init; }
    public bool MergeApprovedByThisApi { get; init; }
    public bool ReleaseExecutedByThisApi { get; init; }
    public bool SourceApplyExecutedByThisApi { get; init; }
    public bool RollbackExecutedByThisApi { get; init; }
    public bool WorkflowContinuedByThisApi { get; init; }
    public bool GitOperationExecutedByThisApi { get; init; }
    public bool HumanReviewRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ReleaseReadinessDecisionRecordApiErrorDto> Errors { get; init; } = [];
}

public sealed record ReleaseReadinessDecisionRecordApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

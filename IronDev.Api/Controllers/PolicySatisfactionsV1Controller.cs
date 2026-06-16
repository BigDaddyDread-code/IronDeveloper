using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/policy-satisfactions")]
public sealed class PolicySatisfactionsV1Controller : ControllerBase
{
    private const int MaxIdLength = 256;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        "developer prompt"
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
        "apply source",
        "source applied",
        "release ready",
        "release approved",
        "workflow continued",
        "dry-run executed",
        "patch artifact created",
        "policy override"
    ];

    private readonly IPolicySatisfactionQueryService _query;
    private readonly IPolicySatisfactionCreateService _create;

    public PolicySatisfactionsV1Controller(
        IPolicySatisfactionQueryService query,
        IPolicySatisfactionCreateService create)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    [HttpGet("{policySatisfactionId:guid}")]
    public async Task<ActionResult<PolicySatisfactionApiEnvelope<PolicySatisfactionReadModel>>> Get(
        [FromRoute] Guid projectId,
        [FromRoute] Guid policySatisfactionId,
        CancellationToken cancellationToken)
    {
        var record = await _query.GetAsync(projectId, policySatisfactionId, cancellationToken);
        if (record is null)
        {
            return NotFound(Envelope<PolicySatisfactionReadModel>(
                "not_found",
                null,
                policySatisfactionId,
                errors: [Error("policySatisfactionId", "Policy satisfaction was not found for this project.", "not_found")]));
        }

        return Ok(Envelope("found", record, policySatisfactionId));
    }

    [HttpGet("by-subject/{subjectKind}/{subjectId}")]
    public async Task<ActionResult<PolicySatisfactionApiEnvelope<IReadOnlyList<PolicySatisfactionReadModel>>>> ListBySubject(
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
            return BadRequest(Envelope<IReadOnlyList<PolicySatisfactionReadModel>>("validation_error", null, errors: errors));
        }

        var records = await _query.ListBySubjectAsync(projectId, subjectKind, subjectId, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-accepted-approval/{acceptedApprovalId:guid}")]
    public async Task<ActionResult<PolicySatisfactionApiEnvelope<IReadOnlyList<PolicySatisfactionReadModel>>>> ListByAcceptedApproval(
        [FromRoute] Guid projectId,
        [FromRoute] Guid acceptedApprovalId,
        CancellationToken cancellationToken)
    {
        var records = await _query.ListByAcceptedApprovalAsync(projectId, acceptedApprovalId, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpGet("by-correlation/{correlationId}")]
    public async Task<ActionResult<PolicySatisfactionApiEnvelope<IReadOnlyList<PolicySatisfactionReadModel>>>> ListByCorrelation(
        [FromRoute] Guid projectId,
        [FromRoute] string correlationId,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLookupText(correlationId, nameof(correlationId)).ToArray();
        if (errors.Length > 0)
        {
            return BadRequest(Envelope<IReadOnlyList<PolicySatisfactionReadModel>>("validation_error", null, errors: errors));
        }

        var records = await _query.ListByProjectAndCorrelationAsync(projectId, correlationId, cancellationToken);
        return Ok(Envelope("found", records));
    }

    [HttpPost]
    public async Task<ActionResult<PolicySatisfactionApiEnvelope<PolicySatisfactionReadModel>>> Create(
        [FromRoute] Guid projectId,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (body.ValueKind is not JsonValueKind.Object)
        {
            return BadRequest(CreateEnvelope<PolicySatisfactionReadModel>(
                "validation_error",
                null,
                errors: [Error("request", "Policy satisfaction create request body is required.")]));
        }

        var forbiddenFields = FindForbiddenClientFields(body).ToArray();
        if (forbiddenFields.Length > 0)
        {
            return BadRequest(CreateEnvelope<PolicySatisfactionReadModel>(
                "validation_error",
                null,
                errors: forbiddenFields
                    .Select(field => Error(field, "Field is server-owned and must not be supplied by the client.", "server_owned_field"))
                    .ToArray()));
        }

        PolicySatisfactionCreateRequest? request;
        try
        {
            request = body.Deserialize<PolicySatisfactionCreateRequest>(JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(CreateEnvelope<PolicySatisfactionReadModel>(
                "validation_error",
                null,
                errors: [Error("request", "Policy satisfaction create request JSON is invalid.")]));
        }

        var result = await _create.CreateAsync(projectId, request, User, cancellationToken);
        if (!result.IsSuccess)
        {
            var envelope = CreateEnvelope<PolicySatisfactionReadModel>(
                "validation_error",
                null,
                errors: result.Issues.Select(ToError).ToArray());

            return result.IsConflict ? Conflict(envelope) : BadRequest(envelope);
        }

        var policySatisfaction = result.PolicySatisfaction!;
        return CreatedAtAction(
            nameof(Get),
            new { projectId, policySatisfactionId = policySatisfaction.PolicySatisfactionId },
            CreateEnvelope("created", policySatisfaction, policySatisfaction.PolicySatisfactionId));
    }

    private static IReadOnlyList<PolicySatisfactionApiErrorDto> ValidateLookupText(string? value, string field)
    {
        var errors = new List<PolicySatisfactionApiErrorDto>();

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
            errors.Add(Error(field, "Policy Satisfaction Read API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning."));
        }

        if (ContainsAny([value], SensitiveMarkers))
        {
            errors.Add(Error(field, "Policy Satisfaction Read API v1 does not accept secret-like lookup material."));
        }

        if (ContainsAny([value], AuthorityMarkers))
        {
            errors.Add(Error(field, "Policy Satisfaction Read API v1 does not accept source apply, release approval, policy override, dry-run execution, patch artifact creation, or workflow continuation claims."));
        }

        return errors;
    }

    private static bool IsSafeId(string value) =>
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static bool ContainsAny(IEnumerable<string?> values, IEnumerable<string> markers) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> FindForbiddenClientFields(JsonElement body)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "policySatisfactionId",
            "projectId",
            "satisfiedAtUtc",
            "createdAtUtc",
            "isExpired",
            "isPolicySatisfied",
            "canApplySource",
            "canRunDryRun",
            "canCreatePatchArtifact",
            "canContinueWorkflow",
            "canApproveRelease",
            "releaseReady",
            "mutationOccurred"
        };

        foreach (var property in body.EnumerateObject())
        {
            if (forbidden.Contains(property.Name))
            {
                yield return property.Name;
            }
        }
    }

    private static PolicySatisfactionApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        Guid? policySatisfactionId = null,
        IReadOnlyList<PolicySatisfactionApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            PolicySatisfactionId = policySatisfactionId,
            Boundary = new PolicySatisfactionReadBoundary(),
            MutationOccurred = false,
            HumanApprovalRequired = true,
            Warnings = PolicySatisfactionReadBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static PolicySatisfactionApiEnvelope<TData> CreateEnvelope<TData>(
        string status,
        TData? data,
        Guid? policySatisfactionId = null,
        IReadOnlyList<PolicySatisfactionApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            PolicySatisfactionId = policySatisfactionId,
            Boundary = new PolicySatisfactionCreateBoundary(),
            MutationOccurred = data is not null,
            HumanApprovalRequired = true,
            Warnings = PolicySatisfactionCreateBoundaryText.Warnings,
            Errors = errors ?? []
        };

    private static PolicySatisfactionApiErrorDto ToError(PolicySatisfactionCreateIssue issue) =>
        Error(issue.Field, issue.Message, issue.Code);

    private static PolicySatisfactionApiErrorDto Error(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Field = field,
            Message = message
        };
}

public sealed record PolicySatisfactionApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public Guid? PolicySatisfactionId { get; init; }
    public required object Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<PolicySatisfactionApiErrorDto> Errors { get; init; } = [];
}

public sealed record PolicySatisfactionApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}
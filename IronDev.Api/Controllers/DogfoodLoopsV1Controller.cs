using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/dogfood-loops")]
public sealed class DogfoodLoopsV1Controller : ControllerBase
{
    private const int MaxSummaryLength = 800;
    private const int MaxGoalLength = 1_500;
    private const int MaxTextLength = 2_000;
    private const int MaxReferences = 50;
    private const int MaxIdLength = 200;
    private const string RedactedPrivateReasoning = "[redacted: sensitive dogfood-loop text]";

    private static readonly HashSet<string> DetailQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectId"
    };

    private static readonly string[] PrivateReasoningMarkers =
    [
        "chain-of-thought",
        "chain of thought",
        "hidden reasoning",
        "hidden deliberation",
        "private reasoning",
        "raw prompt",
        "raw completion",
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
        "release approved",
        "release approval",
        "ready to ship",
        "ship it",
        "approved for release",
        "approval granted",
        "approved for execution",
        "human approved",
        "policy cleared",
        "policy override",
        "authority granted",
        "authoritative for action",
        "execution permitted",
        "tool executed",
        "tool ran",
        "executed true",
        "gate executed",
        "audit approved",
        "approval source audit",
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "collective memory written",
        "vector authority",
        "model authority",
        "authority model",
        "autonomous workflow",
        "workflow completed",
        "create pull request",
        "submit github review"
    ];

    private readonly IDogfoodLoopApiStore _store;

    public DogfoodLoopsV1Controller(IDogfoodLoopApiStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [HttpPost]
    public ActionResult<DogfoodLoopApiEnvelope<DogfoodLoopCreateResponseDto>> Create(
        [FromBody] DogfoodLoopCreateRequestDto request)
    {
        var validation = ValidateCreateRequest(request);
        if (validation.Count > 0)
            return BadRequest(Envelope<DogfoodLoopCreateResponseDto>("validation_error", null, errors: validation));

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<DogfoodLoopCreateResponseDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var receipt = BuildReceipt(request, tenantId.ToString());
        var stored = _store.Save(receipt);
        var response = ToCreateResponseDto(receipt);

        return Ok(Envelope(
            stored.Created ? "receipt_created" : "receipt_found",
            response,
            dogfoodLoopId: receipt.DogfoodLoopId,
            runId: receipt.RunId,
            receiptId: receipt.ReceiptId,
            evidenceId: receipt.EvidenceId,
            mutationOccurred: stored.Created,
            humanApprovalRequired: true,
            warnings: SafeWarnings(receipt)));
    }

    [HttpGet("{dogfoodLoopId}")]
    public ActionResult<DogfoodLoopApiEnvelope<DogfoodLoopReceiptDto>> Get(
        [FromRoute] string dogfoodLoopId,
        [FromQuery] int projectId)
    {
        var validation = ValidateGetRequest(projectId, dogfoodLoopId);
        if (validation.Count > 0)
            return BadRequest(Envelope<DogfoodLoopReceiptDto>("validation_error", null, errors: validation));

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<DogfoodLoopReceiptDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var receipt = _store.Get(tenantId.ToString(), projectId.ToString(), dogfoodLoopId);
        if (receipt is null)
        {
            return NotFound(Envelope<DogfoodLoopReceiptDto>(
                "not_found",
                null,
                dogfoodLoopId: SanitiseText(dogfoodLoopId),
                errors: [ValidationError(nameof(dogfoodLoopId), "Dogfood loop receipt was not found for this project.", "not_found")]));
        }

        var detail = ToReceiptDto(receipt);
        return Ok(Envelope(
            "receipt_found",
            detail,
            dogfoodLoopId: receipt.DogfoodLoopId,
            runId: receipt.RunId,
            receiptId: receipt.ReceiptId,
            evidenceId: receipt.EvidenceId,
            mutationOccurred: false,
            humanApprovalRequired: true,
            warnings: SafeWarnings(receipt)));
    }

    private IReadOnlyList<DogfoodLoopErrorDto> ValidateCreateRequest(DogfoodLoopCreateRequestDto request)
    {
        var errors = new List<DogfoodLoopErrorDto>();

        if (request.ProjectId <= 0)
            errors.Add(ValidationError(nameof(request.ProjectId), "ProjectId is required."));

        if (string.IsNullOrWhiteSpace(request.Summary))
            errors.Add(ValidationError(nameof(request.Summary), "Summary is required."));

        if (string.IsNullOrWhiteSpace(request.Goal))
            errors.Add(ValidationError(nameof(request.Goal), "Goal is required."));

        if (!string.IsNullOrWhiteSpace(request.Summary) && request.Summary.Length > MaxSummaryLength)
            errors.Add(ValidationError(nameof(request.Summary), "Summary is too long.", "content_too_large"));

        if (!string.IsNullOrWhiteSpace(request.Goal) && request.Goal.Length > MaxGoalLength)
            errors.Add(ValidationError(nameof(request.Goal), "Goal is too long.", "content_too_large"));

        ValidateTextList(errors, request.Observations, nameof(request.Observations));
        ValidateTextList(errors, request.BlockedReasons, nameof(request.BlockedReasons));
        ValidateIdList(errors, request.AgentRunIds, nameof(request.AgentRunIds));
        ValidateIdList(errors, request.CriticReviewRunIds, nameof(request.CriticReviewRunIds));
        ValidateIdList(errors, request.MemoryImprovementRunIds, nameof(request.MemoryImprovementRunIds));
        ValidateIdList(errors, request.ToolRequestIds, nameof(request.ToolRequestIds));
        ValidateIdList(errors, request.ToolGateDecisionIds, nameof(request.ToolGateDecisionIds));

        if (request.EvidenceRefs.Count > MaxReferences)
            errors.Add(ValidationError(nameof(request.EvidenceRefs), "Too many evidence references.", "content_too_large"));

        foreach (var evidence in request.EvidenceRefs)
        {
            if (string.IsNullOrWhiteSpace(evidence.RefId))
                errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence RefId is required."));

            if (!IsSafeId(evidence.RefId))
                errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence RefId contains unsupported characters."));

            if (evidence.RefId.Length > MaxIdLength)
                errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence RefId is too long.", "content_too_large"));

            if (evidence.Summary?.Length > MaxTextLength)
                errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence summary is too long.", "content_too_large"));
        }

        if (request.ExtraProperties is not null)
        {
            foreach (var field in request.ExtraProperties.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                errors.Add(UnsupportedField(field));
        }

        var textValues = EnumerateRequestText(request);

        if (ContainsAny(textValues, PrivateReasoningMarkers))
            errors.Add(ValidationError("request", "Dogfood Loop API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning."));

        if (ContainsAny(textValues, SensitiveMarkers))
            errors.Add(ValidationError("request", "Dogfood Loop API v1 does not accept secret-like request material."));

        if (ContainsAny(textValues, AuthorityMarkers))
            errors.Add(ValidationError("request", "Dogfood Loop API v1 does not accept release approval, execution, source apply, memory promotion, audit approval, model authority, vector authority, or hidden workflow claims."));

        return errors;
    }

    private IReadOnlyList<DogfoodLoopErrorDto> ValidateGetRequest(int projectId, string dogfoodLoopId)
    {
        var errors = new List<DogfoodLoopErrorDto>();

        if (projectId <= 0)
            errors.Add(ValidationError(nameof(projectId), "ProjectId is required."));

        if (string.IsNullOrWhiteSpace(dogfoodLoopId))
            errors.Add(ValidationError(nameof(dogfoodLoopId), "DogfoodLoopId is required."));

        if (dogfoodLoopId?.Length > MaxIdLength)
            errors.Add(ValidationError(nameof(dogfoodLoopId), "DogfoodLoopId is too long.", "content_too_large"));

        if (!IsSafeId(dogfoodLoopId))
            errors.Add(ValidationError(nameof(dogfoodLoopId), "DogfoodLoopId contains unsupported characters."));

        foreach (var key in Request.Query.Keys)
        {
            if (!DetailQueryKeys.Contains(key))
                errors.Add(UnsupportedFilter(key));
        }

        if (ContainsAny([dogfoodLoopId], PrivateReasoningMarkers))
            errors.Add(ValidationError(nameof(dogfoodLoopId), "DogfoodLoopId must not contain hidden reasoning markers."));

        if (ContainsAny([dogfoodLoopId], SensitiveMarkers))
            errors.Add(ValidationError(nameof(dogfoodLoopId), "DogfoodLoopId must not contain secret-like material."));

        return errors;
    }

    private DogfoodLoopApiStoredReceipt BuildReceipt(DogfoodLoopCreateRequestDto request, string tenantId)
    {
        var loopId = BuildDogfoodLoopId(request);
        var evidenceRefs = BuildEvidenceRefs(request).ToArray();
        var containsNonDurable = evidenceRefs.Any(reference => !reference.Durable);

        return new DogfoodLoopApiStoredReceipt
        {
            TenantId = tenantId,
            ProjectId = request.ProjectId.ToString(),
            DogfoodLoopId = loopId,
            RunId = $"dogfood-loop-api-v1-{loopId}",
            ReceiptId = $"receipt-{loopId}",
            EvidenceId = $"evidence-{loopId}",
            Summary = SanitiseText(request.Summary),
            Goal = SanitiseText(request.Goal),
            Observations = request.Observations.Select(value => SanitiseText(value)).ToArray(),
            BlockedReasons = request.BlockedReasons.Select(value => SanitiseText(value)).ToArray(),
            ReferencedAgentRuns = request.AgentRunIds.Select(id => Reference("agent_run", id, durable: false, "Caller-supplied agent run reference; not verified by Dogfood Loop API v1.")).ToArray(),
            ReferencedCriticReviews = request.CriticReviewRunIds.Select(id => Reference("critic_review", id, durable: false, "Caller-supplied critic review reference; not verified by Dogfood Loop API v1.")).ToArray(),
            ReferencedMemoryImprovements = request.MemoryImprovementRunIds.Select(id => Reference("memory_improvement", id, durable: false, "Caller-supplied memory-improvement reference; not verified by Dogfood Loop API v1.")).ToArray(),
            ReferencedToolRequests = request.ToolRequestIds.Select(id => Reference("tool_request_preview", id, durable: false, "PR61 tool request API records are non-durable API-local inspection cache entries.")).ToArray(),
            ReferencedGateDecisions = request.ToolGateDecisionIds.Select(id => Reference("tool_gate_decision", id, durable: true, "PR75 tool gate API records are durable SQL-backed gate decision evidence.")).ToArray(),
            EvidenceRefs = evidenceRefs,
            Durable = false,
            ContainsNonDurableReferences = containsNonDurable,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = CurrentUserId(),
            CorrelationId = SanitiseText(request.CorrelationId),
            ContainsRawPrivateReasoning = false,
            Warnings = BoundaryWarnings()
                .Concat(containsNonDurable ? ["Dogfood Loop API v1 contains non-durable caller-supplied/API-local references that are not durable release evidence."] : [])
                .ToArray()
        };
    }

    private static IEnumerable<DogfoodLoopReferenceDto> BuildEvidenceRefs(DogfoodLoopCreateRequestDto request)
    {
        foreach (var evidence in request.EvidenceRefs)
        {
            yield return new DogfoodLoopReferenceDto
            {
                RefType = SanitiseText(evidence.RefType),
                RefId = SanitiseText(evidence.RefId),
                Summary = SanitiseText(evidence.Summary),
                Durable = false,
                BackendRecorded = false,
                Source = "caller_supplied"
            };
        }
    }

    private static DogfoodLoopCreateResponseDto ToCreateResponseDto(DogfoodLoopApiStoredReceipt receipt) =>
        new()
        {
            DogfoodLoopId = receipt.DogfoodLoopId,
            RunId = receipt.RunId,
            ReceiptId = receipt.ReceiptId,
            EvidenceId = receipt.EvidenceId,
            ReceiptOnly = true,
            Durable = receipt.Durable,
            ContainsNonDurableReferences = receipt.ContainsNonDurableReferences,
            Summary = SanitiseText(receipt.Summary, receipt.ContainsRawPrivateReasoning),
            Goal = SanitiseText(receipt.Goal, receipt.ContainsRawPrivateReasoning),
            DurabilityWarnings = DurabilityWarnings(receipt),
            KnownLimitations = KnownLimitations(),
            Warnings = SafeWarnings(receipt)
        };

    private static DogfoodLoopReceiptDto ToReceiptDto(DogfoodLoopApiStoredReceipt receipt)
    {
        var forceRedact = receipt.ContainsRawPrivateReasoning;
        return new DogfoodLoopReceiptDto
        {
            DogfoodLoopId = receipt.DogfoodLoopId,
            RunId = receipt.RunId,
            ReceiptId = receipt.ReceiptId,
            EvidenceId = receipt.EvidenceId,
            ProjectId = receipt.ProjectId,
            Summary = SanitiseText(receipt.Summary, forceRedact),
            Goal = SanitiseText(receipt.Goal, forceRedact),
            Observations = receipt.Observations.Select(value => SanitiseText(value, forceRedact)).ToArray(),
            BlockedReasons = receipt.BlockedReasons.Select(value => SanitiseText(value, forceRedact)).ToArray(),
            ReferencedAgentRuns = SanitiseReferences(receipt.ReferencedAgentRuns, forceRedact),
            ReferencedCriticReviews = SanitiseReferences(receipt.ReferencedCriticReviews, forceRedact),
            ReferencedMemoryImprovements = SanitiseReferences(receipt.ReferencedMemoryImprovements, forceRedact),
            ReferencedToolRequests = SanitiseReferences(receipt.ReferencedToolRequests, forceRedact),
            ReferencedGateDecisions = SanitiseReferences(receipt.ReferencedGateDecisions, forceRedact),
            EvidenceRefs = SanitiseReferences(receipt.EvidenceRefs, forceRedact),
            Durable = receipt.Durable,
            ContainsNonDurableReferences = receipt.ContainsNonDurableReferences,
            DurabilityWarnings = DurabilityWarnings(receipt),
            KnownLimitations = KnownLimitations(),
            CreatedAtUtc = receipt.CreatedAtUtc,
            Warnings = SafeWarnings(receipt)
        };
    }

    private static IReadOnlyList<DogfoodLoopReferenceDto> SanitiseReferences(
        IEnumerable<DogfoodLoopReferenceDto> references,
        bool forceRedact) =>
        references.Select(reference => new DogfoodLoopReferenceDto
        {
            RefType = SanitiseText(reference.RefType, forceRedact),
            RefId = SanitiseText(reference.RefId, forceRedact),
            Summary = SanitiseText(reference.Summary, forceRedact),
            Durable = reference.Durable,
            BackendRecorded = reference.BackendRecorded,
            Source = SanitiseText(reference.Source, forceRedact)
        }).ToArray();

    private static DogfoodLoopReferenceDto Reference(string refType, string refId, bool durable, string summary) =>
        new()
        {
            RefType = refType,
            RefId = SanitiseText(refId),
            Summary = summary,
            Durable = durable,
            BackendRecorded = false,
            Source = "caller_supplied"
        };

    private static string BuildDogfoodLoopId(DogfoodLoopCreateRequestDto request)
    {
        var seed = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;

        return $"dogfood-loop-{request.ProjectId}-{SanitiseId(seed)}";
    }

    private int CurrentTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return int.TryParse(claim, out var tenantId) ? tenantId : 0;
    }

    private string CurrentUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? User.FindFirst(ClaimTypes.Email)?.Value
        ?? "unknown-user";

    private static IEnumerable<string?> EnumerateRequestText(DogfoodLoopCreateRequestDto request)
    {
        yield return request.Summary;
        yield return request.Goal;
        yield return request.CorrelationId;

        foreach (var value in request.Observations)
            yield return value;

        foreach (var value in request.BlockedReasons)
            yield return value;

        foreach (var value in request.AgentRunIds)
            yield return value;

        foreach (var value in request.CriticReviewRunIds)
            yield return value;

        foreach (var value in request.MemoryImprovementRunIds)
            yield return value;

        foreach (var value in request.ToolRequestIds)
            yield return value;

        foreach (var value in request.ToolGateDecisionIds)
            yield return value;

        foreach (var evidence in request.EvidenceRefs)
        {
            yield return evidence.RefType;
            yield return evidence.RefId;
            yield return evidence.Summary;
            yield return evidence.Source;
        }
    }

    private static void ValidateTextList(List<DogfoodLoopErrorDto> errors, IReadOnlyList<string> values, string field)
    {
        if (values.Count > MaxReferences)
            errors.Add(ValidationError(field, "Too many values.", "content_too_large"));

        foreach (var value in values)
        {
            if (value.Length > MaxTextLength)
                errors.Add(ValidationError(field, "Text value is too long.", "content_too_large"));
        }
    }

    private static void ValidateIdList(List<DogfoodLoopErrorDto> errors, IReadOnlyList<string> values, string field)
    {
        if (values.Count > MaxReferences)
            errors.Add(ValidationError(field, "Too many references.", "content_too_large"));

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add(ValidationError(field, "Reference id must not be empty."));
            else if (!IsSafeId(value))
                errors.Add(ValidationError(field, "Reference id contains unsupported characters."));
            else if (value.Length > MaxIdLength)
                errors.Add(ValidationError(field, "Reference id is too long.", "content_too_large"));
        }
    }

    private static bool ContainsAny(IEnumerable<string?> values, IEnumerable<string> markers) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static string SanitiseText(string? value, bool forceRedact = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (forceRedact || PrivateReasoningMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return RedactedPrivateReasoning;

        return value.Trim();
    }

    private static string SanitiseId(string value)
    {
        var safe = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Select(character => character == '.' ? '-' : char.ToLowerInvariant(character))
            .ToArray());

        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private static bool IsSafeId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxIdLength &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static IReadOnlyList<string> SafeWarnings(DogfoodLoopApiStoredReceipt receipt) =>
        BoundaryWarnings()
            .Concat(receipt.Warnings.Select(value => SanitiseText(value, receipt.ContainsRawPrivateReasoning)))
            .Concat(DurabilityWarnings(receipt))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Dogfood Loop API v1 creates or inspects dogfood loop receipts only.",
        "Dogfood receipt is not release approval.",
        "Dogfood loop is not autonomous workflow.",
        "Dogfood receipt is not tool execution, source apply, memory promotion, governance authority, or release readiness.",
        "This API uses non-durable API-local receipt data and does not provide SQL source-of-truth dogfood receipts.",
        "PR61/74 tool request and PR62/75 gate decision references are durable SQL-backed records; dogfood loop receipts remain non-durable API-local records until a durable dogfood store exists.",
        "Human review remains required for source apply and memory promotion."
    ];

    private static IReadOnlyList<string> DurabilityWarnings(DogfoodLoopApiStoredReceipt receipt)
    {
        var warnings = new List<string>
        {
            "Dogfood Loop API v1 receipt storage is non-durable, API-local, not SQL-backed, not durable evidence, and not release approval."
        };

        if (receipt.ContainsNonDurableReferences)
            warnings.Add("Receipt includes non-durable or caller-supplied references that must not be treated as durable backend evidence.");

        return warnings;
    }

    private static IReadOnlyList<string> KnownLimitations() =>
    [
        "No SQL source-of-truth dogfood receipt store exists in this PR.",
        "No autonomous dogfood workflow runner is introduced.",
        "No tool execution, source apply, memory promotion, release approval, or governance authority is introduced."
    ];

    private static DogfoodLoopApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string dogfoodLoopId = "",
        string runId = "",
        string receiptId = "",
        string evidenceId = "",
        bool mutationOccurred = false,
        bool humanApprovalRequired = true,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<DogfoodLoopErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            DogfoodLoopId = dogfoodLoopId,
            RunId = runId,
            ReceiptId = receiptId,
            EvidenceId = evidenceId,
            Boundary = BoundaryStatus(),
            MutationOccurred = mutationOccurred,
            HumanApprovalRequired = humanApprovalRequired,
            Warnings = warnings ?? BoundaryWarnings(),
            Errors = errors ?? []
        };

    private static DogfoodLoopBoundaryStatusDto BoundaryStatus() =>
        new()
        {
            DogfoodReceiptIsReleaseApproval = false,
            DogfoodLoopIsAutonomousWorkflow = false,
            ToolExecuted = false,
            RequestApproved = false,
            GateExecuted = false,
            GateIsExecutor = false,
            SourceApplied = false,
            MemoryPromoted = false,
            CollectiveMemoryWritten = false,
            VectorAuthorityWritten = false,
            AuditIsApproval = false,
            ModelOutputIsAuthority = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            Durable = false,
            ContainsNonDurableReferences = true,
            HumanReviewRequiredForSourceApply = true,
            HumanReviewRequiredForMemoryPromotion = true
        };

    private static DogfoodLoopErrorDto ValidationError(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Message = SanitiseText(message),
            Field = field
        };

    private static DogfoodLoopErrorDto UnsupportedField(string field) =>
        new()
        {
            Category = "unsupported_field",
            Code = "unsupported_field",
            Message = $"Unsupported field '{field}'. Dogfood Loop API v1 does not accept extra approval, release, execution, apply, promotion, governance, or workflow fields.",
            Field = field
        };

    private static DogfoodLoopErrorDto UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_field",
            Code = "unsupported_field",
            Message = $"Unsupported query parameter '{filter}'.",
            Field = filter
        };
}

public interface IDogfoodLoopApiStore
{
    DogfoodLoopApiStoreSaveResult Save(DogfoodLoopApiStoredReceipt receipt);

    DogfoodLoopApiStoredReceipt? Get(string tenantId, string projectId, string dogfoodLoopId);

    int Count();
}

public sealed class InMemoryDogfoodLoopApiStore : IDogfoodLoopApiStore
{
    private readonly ConcurrentDictionary<string, DogfoodLoopApiStoredReceipt> _receipts = new(StringComparer.Ordinal);

    public DogfoodLoopApiStoreSaveResult Save(DogfoodLoopApiStoredReceipt receipt)
    {
        var key = Key(receipt.TenantId, receipt.ProjectId, receipt.DogfoodLoopId);
        var created = _receipts.TryAdd(key, receipt);
        return new DogfoodLoopApiStoreSaveResult { Created = created };
    }

    public DogfoodLoopApiStoredReceipt? Get(string tenantId, string projectId, string dogfoodLoopId)
    {
        _receipts.TryGetValue(Key(tenantId, projectId, dogfoodLoopId), out var receipt);
        return receipt;
    }

    public int Count() => _receipts.Count;

    private static string Key(string tenantId, string projectId, string dogfoodLoopId) =>
        $"{tenantId}::{projectId}::{dogfoodLoopId}";
}

public sealed record DogfoodLoopApiStoreSaveResult
{
    public bool Created { get; init; }
}

public sealed record DogfoodLoopApiStoredReceipt
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string DogfoodLoopId { get; init; }
    public required string RunId { get; init; }
    public required string ReceiptId { get; init; }
    public required string EvidenceId { get; init; }
    public required string Summary { get; init; }
    public required string Goal { get; init; }
    public IReadOnlyList<string> Observations { get; init; } = [];
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedAgentRuns { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedCriticReviews { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedMemoryImprovements { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedToolRequests { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedGateDecisions { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> EvidenceRefs { get; init; } = [];
    public bool Durable { get; init; }
    public bool ContainsNonDurableReferences { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string CreatedByUserId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public bool ContainsRawPrivateReasoning { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DogfoodLoopCreateRequestDto
{
    public int ProjectId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public IReadOnlyList<string> AgentRunIds { get; init; } = [];
    public IReadOnlyList<string> CriticReviewRunIds { get; init; } = [];
    public IReadOnlyList<string> MemoryImprovementRunIds { get; init; } = [];
    public IReadOnlyList<string> ToolRequestIds { get; init; } = [];
    public IReadOnlyList<string> ToolGateDecisionIds { get; init; } = [];
    public IReadOnlyList<DogfoodLoopEvidenceReferenceDto> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> Observations { get; init; } = [];
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
    public string? CorrelationId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraProperties { get; init; }
}

public sealed record DogfoodLoopEvidenceReferenceDto
{
    public string RefType { get; init; } = string.Empty;
    public string RefId { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public sealed record DogfoodLoopApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string DogfoodLoopId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string ReceiptId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required DogfoodLoopBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<DogfoodLoopErrorDto> Errors { get; init; } = [];
}

public sealed record DogfoodLoopBoundaryStatusDto
{
    public bool DogfoodReceiptIsReleaseApproval { get; init; }
    public bool DogfoodLoopIsAutonomousWorkflow { get; init; }
    public bool ToolExecuted { get; init; }
    public bool RequestApproved { get; init; }
    public bool GateExecuted { get; init; }
    public bool GateIsExecutor { get; init; }
    public bool SourceApplied { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool CollectiveMemoryWritten { get; init; }
    public bool VectorAuthorityWritten { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool Durable { get; init; }
    public bool ContainsNonDurableReferences { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
}

public sealed record DogfoodLoopCreateResponseDto
{
    public required string DogfoodLoopId { get; init; }
    public required string RunId { get; init; }
    public required string ReceiptId { get; init; }
    public required string EvidenceId { get; init; }
    public bool ReceiptOnly { get; init; }
    public bool Durable { get; init; }
    public bool ContainsNonDurableReferences { get; init; }
    public required string Summary { get; init; }
    public required string Goal { get; init; }
    public IReadOnlyList<string> DurabilityWarnings { get; init; } = [];
    public IReadOnlyList<string> KnownLimitations { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DogfoodLoopReceiptDto
{
    public required string DogfoodLoopId { get; init; }
    public required string RunId { get; init; }
    public required string ReceiptId { get; init; }
    public required string EvidenceId { get; init; }
    public required string ProjectId { get; init; }
    public required string Summary { get; init; }
    public required string Goal { get; init; }
    public IReadOnlyList<string> Observations { get; init; } = [];
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedAgentRuns { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedCriticReviews { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedMemoryImprovements { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedToolRequests { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> ReferencedGateDecisions { get; init; } = [];
    public IReadOnlyList<DogfoodLoopReferenceDto> EvidenceRefs { get; init; } = [];
    public bool Durable { get; init; }
    public bool ContainsNonDurableReferences { get; init; }
    public IReadOnlyList<string> DurabilityWarnings { get; init; } = [];
    public IReadOnlyList<string> KnownLimitations { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DogfoodLoopReferenceDto
{
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public required string Summary { get; init; }
    public bool Durable { get; init; }
    public bool BackendRecorded { get; init; }
    public required string Source { get; init; }
}

public sealed record DogfoodLoopErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

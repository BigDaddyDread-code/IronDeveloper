using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tool-gates/evaluations")]
public sealed class ToolGatesV1Controller : ControllerBase
{
    private const int MaxReasonLength = 4_000;
    private const int MaxEvidenceRefs = 50;
    private const int MaxIdLength = 200;
    private const string RedactedPrivateReasoning = "[redacted: sensitive tool-gate text]";

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
        "model authority",
        "authority model",
        "create pull request",
        "submit github review"
    ];

    private readonly IToolRequestApiStore _requestStore;
    private readonly IToolGateApiStore _gateStore;
    private readonly IAgentToolExecutionGate _gate;

    public ToolGatesV1Controller(
        IToolRequestApiStore requestStore,
        IToolGateApiStore gateStore,
        IAgentToolExecutionGate gate)
    {
        _requestStore = requestStore ?? throw new ArgumentNullException(nameof(requestStore));
        _gateStore = gateStore ?? throw new ArgumentNullException(nameof(gateStore));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    [HttpPost]
    public ActionResult<ToolGateApiEnvelope<ToolGateEvaluationResponseDto>> Evaluate(
        [FromBody] ToolGateEvaluationRequestDto request)
    {
        var validation = ValidateEvaluateRequest(request);
        if (validation.Count > 0)
            return BadRequest(Envelope<ToolGateEvaluationResponseDto>("validation_error", null, errors: validation));

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<ToolGateEvaluationResponseDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var toolRequestRecord = _requestStore.Get(tenantId.ToString(), request.ProjectId.ToString(), request.ToolRequestId);
        if (toolRequestRecord is null)
        {
            return NotFound(Envelope<ToolGateEvaluationResponseDto>(
                "not_found",
                null,
                toolRequestId: SanitiseText(request.ToolRequestId),
                errors: [ValidationError(nameof(request.ToolRequestId), "Tool request was not found for this project.", "missing_tool_request")]));
        }

        var evaluatedAt = DateTimeOffset.UtcNow;
        var gateResult = _gate.Evaluate(new AgentToolExecutionGateRequest
        {
            ToolRequest = toolRequestRecord.ToolRequest,
            PolicyContext = BuildPolicyContext(toolRequestRecord.ToolRequest),
            ApprovalContext = new AgentToolExecutionGateApprovalContext(),
            MemoryContext = BuildMemoryContext(toolRequestRecord.ToolRequest),
            EvaluatedAtUtc = evaluatedAt
        });

        if (!gateResult.Succeeded || gateResult.Decision is null)
        {
            return BadRequest(Envelope<ToolGateEvaluationResponseDto>(
                "backend_contract_exception",
                null,
                toolRequestId: toolRequestRecord.ToolRequest.ToolRequestId,
                runId: toolRequestRecord.ToolRequest.Scope.RunId ?? string.Empty,
                errors: gateResult.Issues.Select(ToError).ToArray()));
        }

        var preview = new ToolGateApiStoredDecision
        {
            TenantId = tenantId.ToString(),
            ProjectId = request.ProjectId.ToString(),
            ToolRequestRecord = toolRequestRecord,
            Decision = gateResult.Decision,
            CallerEvidenceRefs = request.EvidenceRefs.Select(value => SanitiseText(value)).ToArray(),
            Reason = SanitiseText(request.Reason),
            CorrelationId = SanitiseText(request.CorrelationId),
            CreatedAtUtc = evaluatedAt,
            ContainsRawPrivateReasoning = toolRequestRecord.ContainsRawPrivateReasoning,
            Warnings = BoundaryWarnings()
        };

        var save = _gateStore.Save(preview);
        var response = ToResponseDto(preview);

        return Ok(Envelope(
            "succeeded",
            response,
            toolRequestId: preview.Decision.ToolRequestId,
            gateDecisionId: preview.Decision.GateDecisionId,
            runId: preview.ToolRequestRecord.ToolRequest.Scope.RunId ?? string.Empty,
            evidenceId: response.EvidenceId,
            mutationOccurred: save.Created,
            humanApprovalRequired: response.RequiresHumanApproval,
            warnings: SafeWarnings(preview)));
    }

    [HttpGet("{gateDecisionId}")]
    public ActionResult<ToolGateApiEnvelope<ToolGateDecisionDetailDto>> Get(
        [FromRoute] string gateDecisionId,
        [FromQuery] int projectId)
    {
        var validation = ValidateGetRequest(projectId, gateDecisionId);
        if (validation.Count > 0)
            return BadRequest(Envelope<ToolGateDecisionDetailDto>("validation_error", null, errors: validation));

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<ToolGateDecisionDetailDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var preview = _gateStore.Get(tenantId.ToString(), projectId.ToString(), gateDecisionId);
        if (preview is null)
        {
            return NotFound(Envelope<ToolGateDecisionDetailDto>(
                "not_found",
                null,
                gateDecisionId: SanitiseText(gateDecisionId),
                errors: [ValidationError(nameof(gateDecisionId), "Gate decision preview was not found.", "not_found")]));
        }

        var detail = ToDetailDto(preview);
        return Ok(Envelope(
            "succeeded",
            detail,
            toolRequestId: preview.Decision.ToolRequestId,
            gateDecisionId: preview.Decision.GateDecisionId,
            runId: preview.ToolRequestRecord.ToolRequest.Scope.RunId ?? string.Empty,
            evidenceId: detail.EvidenceId,
            mutationOccurred: false,
            humanApprovalRequired: detail.RequiresHumanApproval,
            warnings: SafeWarnings(preview)));
    }

    private IReadOnlyList<ToolGateApiErrorDto> ValidateEvaluateRequest(ToolGateEvaluationRequestDto request)
    {
        var errors = new List<ToolGateApiErrorDto>();

        if (request.ProjectId <= 0)
            errors.Add(ValidationError(nameof(request.ProjectId), "ProjectId is required."));

        if (string.IsNullOrWhiteSpace(request.ToolRequestId))
            errors.Add(ValidationError(nameof(request.ToolRequestId), "ToolRequestId is required."));

        if (request.ToolRequestId?.Length > MaxIdLength)
            errors.Add(ValidationError(nameof(request.ToolRequestId), "ToolRequestId is too long.", "content_too_large"));

        if (!IsSafeId(request.ToolRequestId))
            errors.Add(ValidationError(nameof(request.ToolRequestId), "ToolRequestId contains unsupported characters."));

        if (request.EvidenceRefs.Count > MaxEvidenceRefs)
            errors.Add(ValidationError(nameof(request.EvidenceRefs), "Too many evidence references.", "content_too_large"));

        if (request.EvidenceRefs.Any(value => value.Length > MaxIdLength))
            errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence reference is too long.", "content_too_large"));

        if (request.Reason?.Length > MaxReasonLength)
            errors.Add(ValidationError(nameof(request.Reason), "Reason is too long.", "content_too_large"));

        if (request.ExtraProperties is not null)
        {
            foreach (var field in request.ExtraProperties.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                errors.Add(UnsupportedField(field));
        }

        IEnumerable<string?> values = [request.ToolRequestId, request.CorrelationId, request.Reason];
        values = values.Concat(request.EvidenceRefs);

        if (ContainsAny(values, PrivateReasoningMarkers))
            errors.Add(ValidationError("request", "Tool Gate API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning."));

        if (ContainsAny(values, SensitiveMarkers))
            errors.Add(ValidationError("request", "Tool Gate API v1 does not accept secret-like request material."));

        if (ContainsAny(values, AuthorityMarkers))
            errors.Add(ValidationError("request", "Tool Gate API v1 does not accept approval, execution, source apply, memory promotion, audit approval, model authority, or hidden workflow claims."));

        return errors;
    }

    private IReadOnlyList<ToolGateApiErrorDto> ValidateGetRequest(int projectId, string gateDecisionId)
    {
        var errors = new List<ToolGateApiErrorDto>();

        if (projectId <= 0)
            errors.Add(ValidationError(nameof(projectId), "ProjectId is required."));

        if (string.IsNullOrWhiteSpace(gateDecisionId))
            errors.Add(ValidationError(nameof(gateDecisionId), "GateDecisionId is required."));

        if (gateDecisionId?.Length > MaxIdLength)
            errors.Add(ValidationError(nameof(gateDecisionId), "GateDecisionId is too long.", "content_too_large"));

        if (!IsSafeId(gateDecisionId))
            errors.Add(ValidationError(nameof(gateDecisionId), "GateDecisionId contains unsupported characters."));

        foreach (var key in Request.Query.Keys)
        {
            if (!DetailQueryKeys.Contains(key))
                errors.Add(UnsupportedFilter(key));
        }

        if (ContainsAny([gateDecisionId], PrivateReasoningMarkers))
            errors.Add(ValidationError(nameof(gateDecisionId), "GateDecisionId must not contain hidden reasoning markers."));

        if (ContainsAny([gateDecisionId], SensitiveMarkers))
            errors.Add(ValidationError(nameof(gateDecisionId), "GateDecisionId must not contain secret-like material."));

        return errors;
    }

    private static AgentToolExecutionGatePolicyContext BuildPolicyContext(AgentToolRequest request) =>
        new()
        {
            PolicyKnown = request.PolicySnapshot.PolicyKnown,
            AllowsToolRequest = request.PolicySnapshot.AllowsToolRequest,
            AllowsToolExecution = request.PolicySnapshot.AllowsToolExecution,
            AllowsSourceMutation = request.PolicySnapshot.AllowsSourceMutation,
            AllowsExternalEffects = request.PolicySnapshot.AllowsExternalEffects,
            AllowsGitHubSubmission = request.PolicySnapshot.AllowsGitHubSubmission,
            AllowsBuildExecution = request.RequestType == AgentToolRequestType.BuildExecutionRequest && request.PolicySnapshot.AllowsToolExecution,
            AllowsTestExecution = request.RequestType == AgentToolRequestType.TestExecutionRequest && request.PolicySnapshot.AllowsToolExecution,
            AllowsPatchProposal = request.RequestType == AgentToolRequestType.PatchProposalRequest && request.PolicySnapshot.AllowsToolRequest,
            PolicyRefs = request.PolicySnapshot.PolicyRefs
        };

    private static AgentToolExecutionGateMemoryContext BuildMemoryContext(AgentToolRequest request)
    {
        var memoryRefs = request.Inputs
            .Where(input => IsMemoryRefType(input.RefType))
            .Select(input => input.RefId)
            .Concat(request.Evidence.Where(evidence => IsMemoryRefType(evidence.RefType)).Select(evidence => evidence.RefId))
            .Select(value => SanitiseText(value))
            .ToArray();

        return new AgentToolExecutionGateMemoryContext
        {
            RequestReferencesMemory = memoryRefs.Length > 0 || request.ApprovalRequirement.RequiresMemoryGovernance,
            HasMemoryGovernanceDecision = false,
            MemoryGovernanceAllowsUse = false,
            MemoryGovernanceWarnsOnly = false,
            MemoryGovernanceBlocksUse = false,
            MemoryRefs = memoryRefs
        };
    }

    private static ToolGateEvaluationResponseDto ToResponseDto(ToolGateApiStoredDecision preview)
    {
        var decision = preview.Decision;
        return new ToolGateEvaluationResponseDto
        {
            GateDecisionId = decision.GateDecisionId,
            ToolRequestId = decision.ToolRequestId,
            Decision = MapDecision(decision.Decision),
            ToolKind = decision.ToolKind.ToString(),
            RequestKind = decision.RequestType.ToString(),
            RiskLevel = decision.RiskLevel.ToString(),
            EvidenceId = EvidenceIdFor(decision.GateDecisionId),
            Reasons = decision.Reasons.Select(ToReasonDto).ToArray(),
            BlockedReasons = decision.Issues.Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)).Select(ToReasonDto).ToArray(),
            RequiredApprovals = RequiredApprovals(preview).ToArray(),
            RequiredEvidence = RequiredEvidence(preview).ToArray(),
            RequiresHumanApproval = RequiresHumanApproval(preview),
            RequiresPolicyApproval = RequiresPolicyApproval(preview),
            RequiresDryRun = RequiresDryRun(preview),
            RequiresGovernanceGate = RequiresGovernanceGate(preview),
            RequiresSeparateExecutor = decision.RequiresExecutor,
            Durable = false,
            RequestDurable = true,
            GateDecisionDurable = false,
            EvaluatedAtUtc = decision.EvaluatedAtUtc,
            Warnings = SafeWarnings(preview)
        };
    }

    private static ToolGateDecisionDetailDto ToDetailDto(ToolGateApiStoredDecision preview)
    {
        var response = ToResponseDto(preview);
        return new ToolGateDecisionDetailDto
        {
            GateDecisionId = response.GateDecisionId,
            ToolRequestId = response.ToolRequestId,
            ProjectId = preview.ProjectId,
            Decision = response.Decision,
            ToolKind = response.ToolKind,
            RequestKind = response.RequestKind,
            RiskLevel = response.RiskLevel,
            EvidenceId = response.EvidenceId,
            Reasons = response.Reasons,
            BlockedReasons = response.BlockedReasons,
            RequiredApprovals = response.RequiredApprovals,
            RequiredEvidence = response.RequiredEvidence,
            RequiresHumanApproval = response.RequiresHumanApproval,
            RequiresPolicyApproval = response.RequiresPolicyApproval,
            RequiresDryRun = response.RequiresDryRun,
            RequiresGovernanceGate = response.RequiresGovernanceGate,
            RequiresSeparateExecutor = response.RequiresSeparateExecutor,
            Durable = false,
            RequestDurable = true,
            GateDecisionDurable = false,
            EvaluatedAtUtc = response.EvaluatedAtUtc,
            Warnings = response.Warnings
        };
    }

    private static ToolGateDecisionReasonDto ToReasonDto(AgentToolExecutionGateReason reason) =>
        new()
        {
            Code = SanitiseText(reason.Code),
            Severity = SanitiseText(reason.Severity),
            Message = SanitiseText(reason.Message),
            EvidenceRefs = reason.EvidenceRefs.Select(value => SanitiseText(value)).ToArray()
        };

    private static ToolGateDecisionReasonDto ToReasonDto(AgentToolExecutionGateIssue issue) =>
        new()
        {
            Code = SanitiseText(issue.Code),
            Severity = SanitiseText(issue.Severity),
            Message = SanitiseText(issue.Message),
            EvidenceRefs = []
        };

    private static IEnumerable<ToolGateRequiredApprovalDto> RequiredApprovals(ToolGateApiStoredDecision preview)
    {
        var request = preview.ToolRequestRecord.ToolRequest;
        var codes = preview.Decision.Issues.Select(issue => issue.Code).ToArray();

        if (request.ApprovalRequirement.RequiresHumanApproval || codes.Contains(AgentToolExecutionGate.ToolGateHumanApprovalRequired, StringComparer.Ordinal))
            yield return RequiredApproval("human", "Human approval remains required before any future executor.");

        if (request.ApprovalRequirement.RequiresPolicyApproval || codes.Contains(AgentToolExecutionGate.ToolGatePolicyApprovalRequired, StringComparer.Ordinal))
            yield return RequiredApproval("policy", "Policy approval remains required before any future executor.");

        if (request.ApprovalRequirement.RequiresGovernanceGate || codes.Contains(AgentToolExecutionGate.ToolGateGovernanceApprovalRequired, StringComparer.Ordinal))
            yield return RequiredApproval("governance_gate", "Governance gate approval remains required before any future executor.");

        if (request.ApprovalRequirement.RequiresMemoryGovernance || codes.Contains(AgentToolExecutionGate.ToolGateMemoryGovernanceRequired, StringComparer.Ordinal))
            yield return RequiredApproval("memory_governance", "Memory governance remains required before memory-backed evidence can be used.");
    }

    private static IEnumerable<string> RequiredEvidence(ToolGateApiStoredDecision preview)
    {
        if (RequiresDryRun(preview))
            yield return "dry_run_evidence";

        foreach (var evidenceRef in preview.CallerEvidenceRefs)
            yield return SanitiseText(evidenceRef, preview.ContainsRawPrivateReasoning);
    }

    private static ToolGateRequiredApprovalDto RequiredApproval(string approvalType, string reason) =>
        new()
        {
            ApprovalType = approvalType,
            Reason = reason,
            Satisfied = false
        };

    private static bool RequiresHumanApproval(ToolGateApiStoredDecision preview) =>
        preview.ToolRequestRecord.ToolRequest.ApprovalRequirement.RequiresHumanApproval ||
        preview.Decision.Issues.Any(issue => string.Equals(issue.Code, AgentToolExecutionGate.ToolGateHumanApprovalRequired, StringComparison.Ordinal));

    private static bool RequiresPolicyApproval(ToolGateApiStoredDecision preview) =>
        preview.ToolRequestRecord.ToolRequest.ApprovalRequirement.RequiresPolicyApproval ||
        preview.Decision.Issues.Any(issue => string.Equals(issue.Code, AgentToolExecutionGate.ToolGatePolicyApprovalRequired, StringComparison.Ordinal));

    private static bool RequiresDryRun(ToolGateApiStoredDecision preview) =>
        preview.ToolRequestRecord.ToolRequest.ApprovalRequirement.RequiresDryRunFirst ||
        preview.Decision.Issues.Any(issue => string.Equals(issue.Code, AgentToolExecutionGate.ToolGateDryRunRequired, StringComparison.Ordinal));

    private static bool RequiresGovernanceGate(ToolGateApiStoredDecision preview) =>
        preview.ToolRequestRecord.ToolRequest.ApprovalRequirement.RequiresGovernanceGate ||
        preview.Decision.Issues.Any(issue => string.Equals(issue.Code, AgentToolExecutionGate.ToolGateGovernanceApprovalRequired, StringComparison.Ordinal));

    private static string MapDecision(AgentToolExecutionGateDecisionType decision) =>
        decision switch
        {
            AgentToolExecutionGateDecisionType.Allowed => "allowed_by_gate",
            AgentToolExecutionGateDecisionType.RequiresApproval => "requires_approval",
            AgentToolExecutionGateDecisionType.Blocked => "blocked_by_gate",
            _ => "unsupported"
        };

    private static string EvidenceIdFor(string gateDecisionId) =>
        $"evidence-{SanitiseId(gateDecisionId)}";

    private int CurrentTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return int.TryParse(claim, out var tenantId) ? tenantId : 0;
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

    private static bool IsMemoryRefType(string refType) =>
        refType.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
        refType.Contains("MemoryInfluence", StringComparison.OrdinalIgnoreCase) ||
        refType.Contains("CollectiveMemory", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SafeWarnings(ToolGateApiStoredDecision preview) =>
        BoundaryWarnings()
            .Concat(preview.Warnings.Select(value => SanitiseText(value, preview.ContainsRawPrivateReasoning)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Tool Gate API v1 evaluates or inspects gate decisions only.",
        "Gate is not executor; gate decision is not approval; gate pass is not human approval.",
        "Tool Gate API v1 uses a non-durable API-local preview cache; SQL-backed durable gate decision storage is not provided by this endpoint.",
        "Tool requests read by this endpoint are durable SQL-backed request records; gate decisions remain non-durable API-local previews until a durable gate store exists.",
        "A separate executor path is required before any requested tool can run.",
        "Human review remains required for source apply and memory promotion."
    ];

    private static ToolGateApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string toolRequestId = "",
        string gateDecisionId = "",
        string runId = "",
        string evidenceId = "",
        bool mutationOccurred = false,
        bool humanApprovalRequired = false,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<ToolGateApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            ToolRequestId = toolRequestId,
            GateDecisionId = gateDecisionId,
            RunId = runId,
            EvidenceId = evidenceId,
            Boundary = BoundaryStatus(),
            MutationOccurred = mutationOccurred,
            HumanApprovalRequired = humanApprovalRequired,
            Warnings = warnings ?? BoundaryWarnings(),
            Errors = errors ?? []
        };

    private static ToolGateBoundaryStatusDto BoundaryStatus() =>
        new()
        {
            GateIsExecutor = false,
            GateDecisionIsApproval = false,
            GatePassIsHumanApproval = false,
            ToolRequestIsExecutionPermission = false,
            ToolExecuted = false,
            RequestApproved = false,
            AuditIsApproval = false,
            SourceApplied = false,
            MemoryPromoted = false,
            ModelOutputIsAuthority = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            Durable = false,
            RequestDurable = true,
            GateDecisionDurable = false,
            HumanReviewRequiredForSourceApply = true,
            HumanReviewRequiredForMemoryPromotion = true
        };

    private static ToolGateApiErrorDto ValidationError(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Message = SanitiseText(message),
            Field = field
        };

    private static ToolGateApiErrorDto UnsupportedField(string field) =>
        new()
        {
            Category = "unsupported_field",
            Code = "unsupported_field",
            Message = $"Unsupported field '{field}'. Tool Gate API v1 does not accept extra authority, approval, execution, apply, promotion, or workflow fields.",
            Field = field
        };

    private static ToolGateApiErrorDto UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_field",
            Code = "unsupported_field",
            Message = $"Unsupported query parameter '{filter}'.",
            Field = filter
        };

    private static ToolGateApiErrorDto ToError(AgentToolExecutionGateIssue issue) =>
        new()
        {
            Category = "backend_contract_exception",
            Code = SanitiseText(issue.Code),
            Message = SanitiseText(issue.Message),
            Field = SanitiseText(issue.Field)
        };
}

public interface IToolGateApiStore
{
    ToolGateApiStoreSaveResult Save(ToolGateApiStoredDecision decision);

    ToolGateApiStoredDecision? Get(string tenantId, string projectId, string gateDecisionId);

    int Count();
}

public sealed class InMemoryToolGateApiStore : IToolGateApiStore
{
    private readonly ConcurrentDictionary<string, ToolGateApiStoredDecision> _decisions = new(StringComparer.Ordinal);

    public ToolGateApiStoreSaveResult Save(ToolGateApiStoredDecision decision)
    {
        var key = Key(decision.TenantId, decision.ProjectId, decision.Decision.GateDecisionId);
        var created = _decisions.TryAdd(key, decision);
        return new ToolGateApiStoreSaveResult { Created = created };
    }

    public ToolGateApiStoredDecision? Get(string tenantId, string projectId, string gateDecisionId)
    {
        _decisions.TryGetValue(Key(tenantId, projectId, gateDecisionId), out var decision);
        return decision;
    }

    public int Count() => _decisions.Count;

    private static string Key(string tenantId, string projectId, string gateDecisionId) =>
        $"{tenantId}::{projectId}::{gateDecisionId}";
}

public sealed record ToolGateApiStoreSaveResult
{
    public bool Created { get; init; }
}

public sealed record ToolGateApiStoredDecision
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required ToolRequestApiStoredRecord ToolRequestRecord { get; init; }
    public required AgentToolExecutionGateDecision Decision { get; init; }
    public IReadOnlyList<string> CallerEvidenceRefs { get; init; } = [];
    public string Reason { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ToolGateEvaluationRequestDto
{
    public int ProjectId { get; init; }
    public string ToolRequestId { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? CorrelationId { get; init; }
    public string? Reason { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraProperties { get; init; }
}

public sealed record ToolGateApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string ToolRequestId { get; init; } = string.Empty;
    public string GateDecisionId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required ToolGateBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ToolGateApiErrorDto> Errors { get; init; } = [];
}

public sealed record ToolGateBoundaryStatusDto
{
    public bool GateIsExecutor { get; init; }
    public bool GateDecisionIsApproval { get; init; }
    public bool GatePassIsHumanApproval { get; init; }
    public bool ToolRequestIsExecutionPermission { get; init; }
    public bool ToolExecuted { get; init; }
    public bool RequestApproved { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool SourceApplied { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool Durable { get; init; }
    public bool RequestDurable { get; init; }
    public bool GateDecisionDurable { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
}

public sealed record ToolGateEvaluationResponseDto
{
    public required string GateDecisionId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string Decision { get; init; }
    public required string ToolKind { get; init; }
    public required string RequestKind { get; init; }
    public required string RiskLevel { get; init; }
    public required string EvidenceId { get; init; }
    public IReadOnlyList<ToolGateDecisionReasonDto> Reasons { get; init; } = [];
    public IReadOnlyList<ToolGateDecisionReasonDto> BlockedReasons { get; init; } = [];
    public IReadOnlyList<ToolGateRequiredApprovalDto> RequiredApprovals { get; init; } = [];
    public IReadOnlyList<string> RequiredEvidence { get; init; } = [];
    public bool RequiresHumanApproval { get; init; }
    public bool RequiresPolicyApproval { get; init; }
    public bool RequiresDryRun { get; init; }
    public bool RequiresGovernanceGate { get; init; }
    public bool RequiresSeparateExecutor { get; init; }
    public bool Durable { get; init; }
    public bool RequestDurable { get; init; }
    public bool GateDecisionDurable { get; init; }
    public DateTimeOffset EvaluatedAtUtc { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ToolGateDecisionDetailDto
{
    public required string GateDecisionId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string ProjectId { get; init; }
    public required string Decision { get; init; }
    public required string ToolKind { get; init; }
    public required string RequestKind { get; init; }
    public required string RiskLevel { get; init; }
    public required string EvidenceId { get; init; }
    public IReadOnlyList<ToolGateDecisionReasonDto> Reasons { get; init; } = [];
    public IReadOnlyList<ToolGateDecisionReasonDto> BlockedReasons { get; init; } = [];
    public IReadOnlyList<ToolGateRequiredApprovalDto> RequiredApprovals { get; init; } = [];
    public IReadOnlyList<string> RequiredEvidence { get; init; } = [];
    public bool RequiresHumanApproval { get; init; }
    public bool RequiresPolicyApproval { get; init; }
    public bool RequiresDryRun { get; init; }
    public bool RequiresGovernanceGate { get; init; }
    public bool RequiresSeparateExecutor { get; init; }
    public bool Durable { get; init; }
    public bool RequestDurable { get; init; }
    public bool GateDecisionDurable { get; init; }
    public DateTimeOffset EvaluatedAtUtc { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ToolGateDecisionReasonDto
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record ToolGateRequiredApprovalDto
{
    public required string ApprovalType { get; init; }
    public required string Reason { get; init; }
    public bool Satisfied { get; init; }
}

public sealed record ToolGateApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

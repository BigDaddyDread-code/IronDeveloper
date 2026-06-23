using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("SensitiveApiPolicy")]
[Route("api/v1/tool-requests")]
public sealed class ToolRequestsV1Controller : ControllerBase
{
    private const int MaxSummaryLength = 500;
    private const int MaxReasonLength = 4_000;
    private const int MaxPayloadLength = 12_000;
    private const int MaxEvidenceRefs = 50;
    private const string RedactedPrivateReasoning = "[redacted: sensitive tool-request text]";

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

    private readonly IToolRequestApiStore _store;
    private readonly AgentToolRequestValidator _validator;

    public ToolRequestsV1Controller(IToolRequestApiStore store, AgentToolRequestValidator validator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    [HttpPost]
    public ActionResult<ToolRequestApiEnvelope<ToolRequestCreateResponseDto>> Create(
        [FromBody] ToolRequestCreateRequestDto request)
    {
        var validation = ValidateCreateRequest(request);
        if (validation.Count > 0)
            return BadRequest(Envelope<ToolRequestCreateResponseDto>("validation_error", null, errors: validation));

        if (!TryParseToolKind(request.RequestedTool, out var toolKind))
        {
            return BadRequest(Envelope<ToolRequestCreateResponseDto>(
                "unsupported_tool",
                null,
                errors: [ValidationError(nameof(request.RequestedTool), "Unsupported requested tool.", "unsupported_tool")]));
        }

        if (!TryParseRequestType(request.RequestKind, out var requestType))
        {
            return BadRequest(Envelope<ToolRequestCreateResponseDto>(
                "unsupported_request_kind",
                null,
                errors: [ValidationError(nameof(request.RequestKind), "Unsupported request kind.", "unsupported_request_kind")]));
        }

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<ToolRequestCreateResponseDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var toolRequest = BuildToolRequest(request, tenantId.ToString(), toolKind, requestType);
        var contractValidation = _validator.Validate(toolRequest);
        if (!contractValidation.IsValid)
        {
            return BadRequest(Envelope<ToolRequestCreateResponseDto>(
                "validation_error",
                null,
                toolRequestId: toolRequest.ToolRequestId,
                runId: toolRequest.Scope.RunId ?? string.Empty,
                evidenceId: toolRequest.Evidence.FirstOrDefault()?.EvidenceId ?? string.Empty,
                errors: ToErrors(contractValidation.Issues)));
        }

        var record = new ToolRequestApiStoredRecord
        {
            ToolRequest = toolRequest,
            PayloadJson = request.Payload.GetRawText(),
            PayloadSummary = BuildPayloadSummary(request.Payload),
            RequestedByUserId = CurrentUserId(),
            CreatedAtUtc = toolRequest.RequestedAtUtc,
            Warnings = BoundaryWarnings()
        };

        var stored = _store.Save(record);
        var data = ToResponseDto(record);

        return Ok(Envelope(
            stored.Created ? "succeeded" : "already_exists",
            data,
            toolRequestId: toolRequest.ToolRequestId,
            runId: toolRequest.Scope.RunId ?? string.Empty,
            evidenceId: toolRequest.Evidence.FirstOrDefault()?.EvidenceId ?? string.Empty,
            mutationOccurred: stored.Created,
            humanApprovalRequired: toolRequest.ApprovalRequirement.RequiresHumanApproval,
            warnings: BoundaryWarnings()));
    }

    [HttpGet("{toolRequestId}")]
    public ActionResult<ToolRequestApiEnvelope<ToolRequestDetailDto>> Get(
        [FromQuery] int projectId,
        string toolRequestId)
    {
        var validation = ValidateGetRequest(projectId, toolRequestId);
        if (validation.Count > 0)
        {
            return BadRequest(Envelope<ToolRequestDetailDto>(
                "validation_error",
                null,
                toolRequestId: toolRequestId,
                errors: validation));
        }

        var tenantId = CurrentTenantId();
        var record = _store.Get(tenantId.ToString(), projectId.ToString(), toolRequestId);
        if (record is null)
        {
            return NotFound(Envelope<ToolRequestDetailDto>(
                "not_found",
                null,
                toolRequestId: toolRequestId,
                errors: [ValidationError(nameof(toolRequestId), "Tool request was not found for this project.", "not_found")]));
        }

        var detail = ToDetailDto(record);
        return Ok(Envelope(
            "succeeded",
            detail,
            toolRequestId: record.ToolRequest.ToolRequestId,
            runId: record.ToolRequest.Scope.RunId ?? string.Empty,
            evidenceId: record.ToolRequest.Evidence.FirstOrDefault()?.EvidenceId ?? string.Empty,
            mutationOccurred: false,
            humanApprovalRequired: record.ToolRequest.ApprovalRequirement.RequiresHumanApproval,
            warnings: SafeWarnings(record)));
    }

    private IReadOnlyList<ToolRequestApiErrorDto> ValidateCreateRequest(ToolRequestCreateRequestDto request)
    {
        var errors = new List<ToolRequestApiErrorDto>();

        if (request.ProjectId <= 0)
            errors.Add(ValidationError(nameof(request.ProjectId), "Project id is required."));

        if (string.IsNullOrWhiteSpace(request.RequestedTool))
            errors.Add(ValidationError(nameof(request.RequestedTool), "Requested tool is required."));

        if (string.IsNullOrWhiteSpace(request.RequestKind))
            errors.Add(ValidationError(nameof(request.RequestKind), "Request kind is required."));

        if (string.IsNullOrWhiteSpace(request.Summary))
            errors.Add(ValidationError(nameof(request.Summary), "Summary is required."));

        if (request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            errors.Add(ValidationError(nameof(request.Payload), "Payload is required."));

        if (!string.IsNullOrWhiteSpace(request.Summary) && request.Summary.Length > MaxSummaryLength)
            errors.Add(ValidationError(nameof(request.Summary), $"Summary must be {MaxSummaryLength} characters or fewer.", "content_too_large"));

        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length > MaxReasonLength)
            errors.Add(ValidationError(nameof(request.Reason), $"Reason must be {MaxReasonLength} characters or fewer.", "content_too_large"));

        var payloadText = request.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? string.Empty
            : request.Payload.GetRawText();

        if (payloadText.Length > MaxPayloadLength)
            errors.Add(ValidationError(nameof(request.Payload), $"Payload must be {MaxPayloadLength} characters or fewer.", "content_too_large"));

        if (request.EvidenceRefs.Count > MaxEvidenceRefs)
            errors.Add(ValidationError(nameof(request.EvidenceRefs), $"Evidence refs must contain {MaxEvidenceRefs} items or fewer."));

        if (request.ExtraProperties is { Count: > 0 })
        {
            foreach (var field in request.ExtraProperties.Keys)
                errors.Add(UnsupportedField(field));
        }

        var textValues = new List<string?>
        {
            request.RequestedTool,
            request.RequestKind,
            request.Summary,
            payloadText,
            request.Reason,
            request.CorrelationId,
            request.RequestedByAgentRunId
        };
        textValues.AddRange(request.EvidenceRefs);

        if (ContainsAny(textValues, PrivateReasoningMarkers))
            errors.Add(ValidationError("request", "Tool Request API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning."));

        if (ContainsAny(textValues, SensitiveMarkers))
            errors.Add(ValidationError("request", "Tool Request API v1 does not accept secret-bearing request material."));

        if (ContainsAny(textValues, AuthorityMarkers))
            errors.Add(ValidationError("request", "Tool Request API v1 does not accept approval, execution, source apply, memory promotion, gate execution, audit approval, model authority, or external submission claims."));

        return errors;
    }

    private IReadOnlyList<ToolRequestApiErrorDto> ValidateGetRequest(int projectId, string toolRequestId)
    {
        var errors = new List<ToolRequestApiErrorDto>();

        if (projectId <= 0)
            errors.Add(ValidationError(nameof(projectId), "Project id is required."));

        if (string.IsNullOrWhiteSpace(toolRequestId))
            errors.Add(ValidationError(nameof(toolRequestId), "Tool request id is required."));

        foreach (var key in Request.Query.Keys)
        {
            if (!DetailQueryKeys.Contains(key))
                errors.Add(UnsupportedFilter(key));
        }

        return errors;
    }

    private AgentToolRequest BuildToolRequest(
        ToolRequestCreateRequestDto request,
        string tenantId,
        AgentToolKind toolKind,
        AgentToolRequestType requestType)
    {
        var toolRequestId = BuildToolRequestId(request, toolKind);
        var actor = ActorFor(toolKind);
        var evidenceRefs = request.EvidenceRefs.Count == 0
            ? [$"tool-request-api:{request.RequestedTool}:{request.CorrelationId ?? toolRequestId}"]
            : request.EvidenceRefs.Select(value => SanitiseText(value)).ToArray();

        return new AgentToolRequest
        {
            ToolRequestId = toolRequestId,
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = requestType,
            ToolKind = toolKind,
            RiskLevel = RiskFor(requestType),
            Scope = new AgentToolRequestScope
            {
                TenantId = tenantId,
                ProjectId = request.ProjectId.ToString(),
                CampaignId = $"tool-request-api-v1-project-{request.ProjectId}",
                RunId = $"tool-request-api-v1-{toolRequestId}",
                AgentRunId = string.IsNullOrWhiteSpace(request.RequestedByAgentRunId)
                    ? $"tool-request-api-v1-agent-run-{toolRequestId}"
                    : SanitiseId(request.RequestedByAgentRunId),
                CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                    ? toolRequestId
                    : SanitiseId(request.CorrelationId)
            },
            Actor = actor,
            Purpose = SanitiseText(request.Summary),
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = $"input-{toolRequestId}-001",
                    RefType = "ToolRequestPayload",
                    RefId = $"payload-{toolRequestId}",
                    Source = "ToolRequestApiV1",
                    Summary = BuildPayloadSummary(request.Payload),
                    EvidenceRefs = evidenceRefs,
                    IsAuthoritativeForAction = false,
                    ContainsRawPrivateReasoning = false,
                    ContainsSecret = false,
                    IsSanitised = true
                }
            ],
            Evidence = evidenceRefs.Select((evidenceRef, index) => new AgentToolRequestEvidence
            {
                EvidenceId = $"evidence-{toolRequestId}-{index + 1:000}",
                RefType = "CallerEvidence",
                RefId = evidenceRef,
                Summary = $"Caller evidence reference for request {toolRequestId}.",
                SupportsNeedForTool = index == 0,
                IsAuthorityGrant = false,
                ContainsRawPrivateReasoning = false,
                ContainsSecret = false
            }).ToArray(),
            ApprovalRequirement = ApprovalFor(requestType),
            PolicySnapshot = new AgentToolRequestPolicySnapshot
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsToolExecution = false,
                AllowsSourceMutation = false,
                AllowsExternalEffects = false,
                AllowsGitHubSubmission = false,
                PolicyRefs = ["api-surface:tool-request-v1", "pr56:backend-contract-freeze"]
            },
            RequestedAtUtc = DateTimeOffset.UtcNow,
            ContainsRawPrivateReasoning = false,
            ClaimsApproval = false,
            ClaimsExecutionPermission = false,
            ContainsExecutionResult = false,
            IsExecutableWithoutGate = false
        };
    }

    private static ToolRequestCreateResponseDto ToResponseDto(ToolRequestApiStoredRecord record) =>
        new()
        {
            ToolRequestId = record.ToolRequest.ToolRequestId,
            Status = record.ToolRequest.Status.ToString(),
            RequestedTool = record.ToolRequest.ToolKind.ToString(),
            RequestKind = record.ToolRequest.RequestType.ToString(),
            RiskLevel = record.ToolRequest.RiskLevel.ToString(),
            RunId = record.ToolRequest.Scope.RunId ?? string.Empty,
            EvidenceId = record.ToolRequest.Evidence.FirstOrDefault()?.EvidenceId ?? string.Empty,
            RequestedByAgentRunId = record.ToolRequest.Scope.AgentRunId ?? string.Empty,
            PayloadSummary = SanitiseText(record.PayloadSummary),
            EvidenceRefs = record.ToolRequest.Evidence.Select(evidence => SanitiseText(evidence.RefId)).ToArray(),
            RequestOnly = true,
            RequiresGovernanceGate = record.ToolRequest.ApprovalRequirement.RequiresGovernanceGate,
            RequiresHumanApproval = record.ToolRequest.ApprovalRequirement.RequiresHumanApproval,
            RequiresPolicyApproval = record.ToolRequest.ApprovalRequirement.RequiresPolicyApproval,
            RequiresDryRunFirst = record.ToolRequest.ApprovalRequirement.RequiresDryRunFirst,
            RequiresMemoryGovernance = record.ToolRequest.ApprovalRequirement.RequiresMemoryGovernance,
            Warnings = SafeWarnings(record)
        };

    private static ToolRequestDetailDto ToDetailDto(ToolRequestApiStoredRecord record)
    {
        var forceRedact = record.ContainsRawPrivateReasoning ||
                          record.ToolRequest.ContainsRawPrivateReasoning ||
                          record.ToolRequest.Inputs.Any(input => input.ContainsRawPrivateReasoning) ||
                          record.ToolRequest.Evidence.Any(evidence => evidence.ContainsRawPrivateReasoning);

        return new ToolRequestDetailDto
        {
            ToolRequestId = record.ToolRequest.ToolRequestId,
            ProjectId = record.ToolRequest.Scope.ProjectId,
            Status = record.ToolRequest.Status.ToString(),
            RequestedTool = record.ToolRequest.ToolKind.ToString(),
            RequestKind = record.ToolRequest.RequestType.ToString(),
            RiskLevel = record.ToolRequest.RiskLevel.ToString(),
            RunId = record.ToolRequest.Scope.RunId ?? string.Empty,
            RequestedByAgentRunId = record.ToolRequest.Scope.AgentRunId ?? string.Empty,
            Purpose = SanitiseText(record.ToolRequest.Purpose, forceRedact),
            PayloadSummary = SanitiseText(record.PayloadSummary, forceRedact),
            Inputs = record.ToolRequest.Inputs.Select(input => new ToolRequestReferenceDto
            {
                RefType = SanitiseText(input.RefType),
                RefId = SanitiseText(input.RefId),
                Summary = SanitiseText(input.Summary, forceRedact),
                EvidenceRefs = input.EvidenceRefs.Select(value => SanitiseText(value, forceRedact)).ToArray()
            }).ToArray(),
            Evidence = record.ToolRequest.Evidence.Select(evidence => new ToolRequestEvidenceReferenceDto
            {
                EvidenceId = SanitiseText(evidence.EvidenceId),
                RefType = SanitiseText(evidence.RefType),
                RefId = SanitiseText(evidence.RefId, forceRedact),
                Summary = SanitiseText(evidence.Summary, forceRedact),
                SupportsNeedForTool = evidence.SupportsNeedForTool,
                IsAuthorityGrant = false
            }).ToArray(),
            RequestOnly = true,
            ClaimsApproval = false,
            ClaimsExecutionPermission = false,
            ContainsExecutionResult = false,
            IsExecutableWithoutGate = false,
            RequiresGovernanceGate = record.ToolRequest.ApprovalRequirement.RequiresGovernanceGate,
            RequiresHumanApproval = record.ToolRequest.ApprovalRequirement.RequiresHumanApproval,
            RequiresPolicyApproval = record.ToolRequest.ApprovalRequirement.RequiresPolicyApproval,
            RequiresDryRunFirst = record.ToolRequest.ApprovalRequirement.RequiresDryRunFirst,
            RequiresMemoryGovernance = record.ToolRequest.ApprovalRequirement.RequiresMemoryGovernance,
            CreatedAtUtc = record.CreatedAtUtc,
            Warnings = SafeWarnings(record, forceRedact)
        };
    }


    private static IReadOnlyList<string> SafeWarnings(ToolRequestApiStoredRecord record, bool forceRedact = false) =>
        BoundaryWarnings()
            .Concat(record.Warnings.Select(value => SanitiseText(value, forceRedact || record.ContainsRawPrivateReasoning)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    private static AgentToolRequestActor ActorFor(AgentToolKind toolKind)
    {
        var definition = toolKind switch
        {
            AgentToolKind.BuildRun or AgentToolKind.TestRun => AgentDefinitionCatalog.TestingAgent,
            AgentToolKind.PatchProposal or AgentToolKind.SourceApply => AgentDefinitionCatalog.ImplementationAgent,
            _ => AgentDefinitionCatalog.ReportingAgent
        };

        return new AgentToolRequestActor
        {
            AgentId = definition.AgentId,
            AgentName = definition.Name,
            AgentKind = definition.Kind,
            ExecutionMode = definition.ExecutionMode,
            DeclaredCapabilities = definition.Capabilities?.ToArray() ?? [],
            ForbiddenCapabilities = definition.ForbiddenCapabilities?.ToArray() ?? []
        };
    }

    private static AgentToolRiskLevel RiskFor(AgentToolRequestType requestType) =>
        requestType switch
        {
            AgentToolRequestType.SourceMutationRequest or AgentToolRequestType.ExternalEffectRequest => AgentToolRiskLevel.Critical,
            AgentToolRequestType.BuildExecutionRequest or AgentToolRequestType.TestExecutionRequest => AgentToolRiskLevel.Medium,
            AgentToolRequestType.PatchProposalRequest => AgentToolRiskLevel.Medium,
            AgentToolRequestType.AnalyseOnly => AgentToolRiskLevel.Low,
            _ => AgentToolRiskLevel.Low
        };

    private static AgentToolRequestApprovalRequirement ApprovalFor(AgentToolRequestType requestType) =>
        requestType switch
        {
            AgentToolRequestType.SourceMutationRequest => new AgentToolRequestApprovalRequirement
            {
                RequiresHumanApproval = true,
                RequiresGovernanceGate = true,
                RequiresPolicyApproval = true,
                RequiresDryRunFirst = true,
                Reason = "Source mutation requests require human approval, governance gate, policy approval, and dry-run evidence. This API does not grant any of them."
            },
            AgentToolRequestType.ExternalEffectRequest => new AgentToolRequestApprovalRequirement
            {
                RequiresHumanApproval = true,
                RequiresGovernanceGate = true,
                RequiresPolicyApproval = true,
                Reason = "External effect requests require human approval, governance gate, and policy approval. This API does not grant any of them."
            },
            AgentToolRequestType.BuildExecutionRequest or AgentToolRequestType.TestExecutionRequest => new AgentToolRequestApprovalRequirement
            {
                RequiresGovernanceGate = true,
                Reason = "Build and test requests require a separate governance gate. This API does not execute the gate."
            },
            _ => new AgentToolRequestApprovalRequirement
            {
                Reason = "Request-only API creation grants no execution permission."
            }
        };

    private static bool TryParseToolKind(string value, out AgentToolKind toolKind)
    {
        var token = NormaliseToken(value);
        toolKind = token switch
        {
            "codestandardsanalysepatch" or "codestandardsanalyzepatch" or "codestandards" => AgentToolKind.CodeStandardsAnalysePatch,
            "workspacediff" => AgentToolKind.WorkspaceDiff,
            "buildrun" or "dotnetbuild" => AgentToolKind.BuildRun,
            "testrun" or "dotnettest" => AgentToolKind.TestRun,
            "patchproposal" => AgentToolKind.PatchProposal,
            "sourceapply" => AgentToolKind.SourceApply,
            "gitstatus" => AgentToolKind.GitStatus,
            "gitdiff" => AgentToolKind.GitDiff,
            "externalhttpcall" => AgentToolKind.ExternalHttpCall,
            "githubreviewsubmission" => AgentToolKind.GitHubReviewSubmission,
            _ => AgentToolKind.Unknown
        };

        return toolKind != AgentToolKind.Unknown;
    }

    private static bool TryParseRequestType(string value, out AgentToolRequestType requestType)
    {
        var token = NormaliseToken(value);
        requestType = token switch
        {
            "analyseonly" or "analyzeonly" => AgentToolRequestType.AnalyseOnly,
            "readonlyinspection" or "readonly" or "inspection" => AgentToolRequestType.ReadOnlyInspection,
            "testexecutionrequest" or "testrequest" => AgentToolRequestType.TestExecutionRequest,
            "buildexecutionrequest" or "buildrequest" => AgentToolRequestType.BuildExecutionRequest,
            "patchproposalrequest" or "patchproposal" => AgentToolRequestType.PatchProposalRequest,
            "sourcemutationrequest" or "sourcemutation" or "sourceapplyrequest" => AgentToolRequestType.SourceMutationRequest,
            "externaleffectrequest" or "externaleffect" => AgentToolRequestType.ExternalEffectRequest,
            _ => 0
        };

        return Enum.IsDefined(requestType) && requestType != 0;
    }

    private static string BuildToolRequestId(ToolRequestCreateRequestDto request, AgentToolKind toolKind)
    {
        var seed = string.Join(
            "-",
            request.ProjectId.ToString(),
            toolKind.ToString(),
            string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId);

        return $"tool-request-{SanitiseId(seed)}";
    }

    private static string BuildPayloadSummary(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return string.Empty;

        var raw = payload.GetRawText();
        if (raw.Length <= 500)
            return SanitiseText(raw);

        return SanitiseText(raw[..500]);
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

    private static string NormaliseToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static ToolRequestApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string toolRequestId = "",
        string runId = "",
        string evidenceId = "",
        bool mutationOccurred = false,
        bool humanApprovalRequired = false,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<ToolRequestApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            ToolRequestId = toolRequestId,
            RunId = runId,
            EvidenceId = evidenceId,
            Boundary = BoundaryStatus(),
            MutationOccurred = mutationOccurred,
            HumanApprovalRequired = humanApprovalRequired,
            Warnings = warnings ?? [],
            Errors = errors ?? []
        };

    private static ToolRequestBoundaryStatusDto BoundaryStatus() =>
        new()
        {
            ToolRequestIsExecutionPermission = false,
            Durable = true,
            ToolExecuted = false,
            RequestApproved = false,
            AuditIsApproval = false,
            GateIsExecutor = false,
            SourceApplied = false,
            MemoryPromoted = false,
            ModelOutputIsAuthority = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            HumanReviewRequiredForSourceApply = true,
            HumanReviewRequiredForMemoryPromotion = true
        };

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Tool Request API v1 creates or inspects tool request evidence only.",
        "Tool Request API v1 persists durable SQL-backed tool request records.",
        "Tool request is not approval, execution permission, tool execution, source apply, memory promotion, or governance.",
        "A separate gate/executor path is required before any requested tool can run.",
        "Human review remains required for source apply and memory promotion."
    ];

    private static ToolRequestApiErrorDto ValidationError(string field, string message, string code = "validation_error") =>
        new()
        {
            Category = code,
            Code = code,
            Message = SanitiseText(message),
            Field = field
        };

    private static ToolRequestApiErrorDto UnsupportedField(string field) =>
        new()
        {
            Category = "unsupported_field",
            Code = "unsupported_field",
            Message = $"Unsupported field '{field}'. Tool Request API v1 does not accept extra authority, approval, execution, apply, promotion, or workflow fields.",
            Field = field
        };

    private static ToolRequestApiErrorDto UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_field",
            Code = "unsupported_field",
            Message = $"Unsupported query parameter '{filter}'.",
            Field = filter
        };

    private static IReadOnlyList<ToolRequestApiErrorDto> ToErrors(IReadOnlyList<AgentToolRequestValidationIssue> issues) =>
        issues.Select(issue => new ToolRequestApiErrorDto
        {
            Category = "backend_contract_exception",
            Code = issue.Code,
            Message = SanitiseText(issue.Message),
            Field = issue.Field
        }).ToArray();
}

public interface IToolRequestApiStore
{
    ToolRequestApiStoreSaveResult Save(ToolRequestApiStoredRecord record);

    ToolRequestApiStoredRecord? Get(string tenantId, string projectId, string toolRequestId);

    int Count();
}

public sealed record ToolRequestApiStoreSaveResult
{
    public bool Created { get; init; }
}

public sealed record ToolRequestApiStoredRecord
{
    public required AgentToolRequest ToolRequest { get; init; }
    public required string PayloadJson { get; init; }
    public required string PayloadSummary { get; init; }
    public string RequestedByUserId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ToolRequestCreateRequestDto
{
    public int ProjectId { get; init; }
    public string RequestedTool { get; init; } = string.Empty;
    public string RequestKind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? CorrelationId { get; init; }
    public string? Reason { get; init; }
    public string? RequestedByAgentRunId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraProperties { get; init; }
}

public sealed record ToolRequestApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string ToolRequestId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required ToolRequestBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ToolRequestApiErrorDto> Errors { get; init; } = [];
}

public sealed record ToolRequestBoundaryStatusDto
{
    public bool ToolRequestIsExecutionPermission { get; init; }
    public bool Durable { get; init; }
    public bool ToolExecuted { get; init; }
    public bool RequestApproved { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool GateIsExecutor { get; init; }
    public bool SourceApplied { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
}

public sealed record ToolRequestCreateResponseDto
{
    public required string ToolRequestId { get; init; }
    public required string Status { get; init; }
    public required string RequestedTool { get; init; }
    public required string RequestKind { get; init; }
    public required string RiskLevel { get; init; }
    public required string RunId { get; init; }
    public required string EvidenceId { get; init; }
    public required string RequestedByAgentRunId { get; init; }
    public required string PayloadSummary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool RequestOnly { get; init; }
    public bool RequiresGovernanceGate { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public bool RequiresPolicyApproval { get; init; }
    public bool RequiresDryRunFirst { get; init; }
    public bool RequiresMemoryGovernance { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ToolRequestDetailDto
{
    public required string ToolRequestId { get; init; }
    public required string ProjectId { get; init; }
    public required string Status { get; init; }
    public required string RequestedTool { get; init; }
    public required string RequestKind { get; init; }
    public required string RiskLevel { get; init; }
    public required string RunId { get; init; }
    public required string RequestedByAgentRunId { get; init; }
    public required string Purpose { get; init; }
    public required string PayloadSummary { get; init; }
    public IReadOnlyList<ToolRequestReferenceDto> Inputs { get; init; } = [];
    public IReadOnlyList<ToolRequestEvidenceReferenceDto> Evidence { get; init; } = [];
    public bool RequestOnly { get; init; }
    public bool ClaimsApproval { get; init; }
    public bool ClaimsExecutionPermission { get; init; }
    public bool ContainsExecutionResult { get; init; }
    public bool IsExecutableWithoutGate { get; init; }
    public bool RequiresGovernanceGate { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public bool RequiresPolicyApproval { get; init; }
    public bool RequiresDryRunFirst { get; init; }
    public bool RequiresMemoryGovernance { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ToolRequestReferenceDto
{
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record ToolRequestEvidenceReferenceDto
{
    public required string EvidenceId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public required string Summary { get; init; }
    public bool SupportsNeedForTool { get; init; }
    public bool IsAuthorityGrant { get; init; }
}

public sealed record ToolRequestApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/manual-memory-improvements")]
public sealed class ManualMemoryImprovementsV1Controller : ControllerBase
{
    private const int MaxSummaryLength = 500;
    private const int MaxContentLength = 12_000;
    private const int MaxContextLength = 4_000;
    private const int MaxEvidenceRefs = 50;
    private const string DefaultSpecialisationId = "builtin.memory.repeated-manual-correction-detector";
    private const string RedactedPrivateReasoning = "[redacted: sensitive memory-improvement audit text]";

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
        "safe means approved",
        "memory safe approved",
        "memory promoted",
        "promote memory",
        "promoted memory",
        "accepted memory",
        "accept memory",
        "save to memory",
        "saveToMemory",
        "collectiveMemoryId",
        "collective memory written",
        "create collective memory",
        "vector authority",
        "weaviate authority",
        "write vector",
        "write index",
        "source applied",
        "apply patch",
        "tool executed",
        "submit github review",
        "create pull request"
    ];

    private readonly IStoredManualMemoryImprovementAgentService _memoryService;
    private readonly IAgentRunAuditQueryService _queryService;

    public ManualMemoryImprovementsV1Controller(
        IStoredManualMemoryImprovementAgentService memoryService,
        IAgentRunAuditQueryService queryService)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [HttpPost]
    public ActionResult<ManualMemoryImprovementApiEnvelope<ManualMemoryImprovementResponseDto>> Create(
        [FromBody] ManualMemoryImprovementRequestDto request)
    {
        var validation = ValidateCreateRequest(request);
        if (validation.Count > 0)
            return BadRequest(Envelope<ManualMemoryImprovementResponseDto>("validation_error", null, errors: validation));

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<ManualMemoryImprovementResponseDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var manualRequest = BuildManualRequest(request, tenantId.ToString(), CurrentUserId());
        var result = _memoryService.ExecuteAndStore(
            manualRequest,
            new ManualAgentExecutionSpecialisationSelection
            {
                SpecialisationId = DefaultSpecialisationId,
                RequestedByUserId = manualRequest.RequestedByUserId,
                Reason = "Create manual memory-improvement proposal-only evidence through API v1. This selection grants no approval, promotion, governance, execution, source apply, vector authority, or collective-memory authority."
            },
            DateTimeOffset.UtcNow);

        if (result.Status == StoredManualAgentExecutionStatus.Conflict)
        {
            return Conflict(Envelope(
                "conflict",
                ToResponseDto(result, mutationOccurred: false),
                runId: result.AgentRunId,
                proposalId: result.Output?.ProposalDrafts.FirstOrDefault()?.ProposalDraftId ?? string.Empty,
                evidenceId: result.AuditEnvelope?.Outputs.FirstOrDefault()?.EvidenceRefs.FirstOrDefault() ?? string.Empty,
                mutationOccurred: false,
                errors: ToErrors(result.Issues)));
        }

        if (result.Status is not (StoredManualAgentExecutionStatus.Stored or StoredManualAgentExecutionStatus.AlreadyStored))
        {
            return BadRequest(Envelope(
                "rejected",
                ToResponseDto(result, mutationOccurred: false),
                runId: result.AgentRunId,
                proposalId: result.Output?.ProposalDrafts.FirstOrDefault()?.ProposalDraftId ?? string.Empty,
                evidenceId: result.AuditEnvelope?.Outputs.FirstOrDefault()?.EvidenceRefs.FirstOrDefault() ?? string.Empty,
                mutationOccurred: false,
                errors: ToErrors(result.Issues)));
        }

        var mutationOccurred = result.Status == StoredManualAgentExecutionStatus.Stored;
        var data = ToResponseDto(result, mutationOccurred);
        return Ok(Envelope(
            result.Status == StoredManualAgentExecutionStatus.AlreadyStored ? "already_exists" : "succeeded",
            data,
            runId: result.AgentRunId,
            proposalId: data.ProposalIds.FirstOrDefault() ?? string.Empty,
            evidenceId: data.EvidenceRefs.FirstOrDefault() ?? string.Empty,
            mutationOccurred: mutationOccurred,
            warnings: BoundaryWarnings().Concat(data.Warnings).ToArray()));
    }

    [HttpGet("{agentRunId}")]
    public ActionResult<ManualMemoryImprovementApiEnvelope<ManualMemoryImprovementDetailDto>> Get(
        [FromQuery] int projectId,
        string agentRunId)
    {
        var validation = ValidateGetRequest(projectId, agentRunId);
        if (validation.Count > 0)
        {
            return BadRequest(Envelope<ManualMemoryImprovementDetailDto>(
                "validation_error",
                null,
                runId: agentRunId,
                errors: validation));
        }

        var response = _queryService.GetAgentRun(projectId.ToString(), agentRunId);
        if (IsNotFound(response.Issues) || response.Run is null || !IsManualMemoryImprovementRun(response.Run))
        {
            return NotFound(Envelope<ManualMemoryImprovementDetailDto>(
                "not_found",
                null,
                runId: agentRunId,
                errors: IsNotFound(response.Issues)
                    ? ToErrors(response.Issues)
                    : [ValidationError("agentRunId", "Manual memory-improvement result was not found for this project.")]));
        }

        if (HasError(response.Issues))
        {
            return BadRequest(Envelope<ManualMemoryImprovementDetailDto>(
                "validation_error",
                null,
                runId: agentRunId,
                errors: ToErrors(response.Issues)));
        }

        var data = ToDetailDto(response);
        return Ok(Envelope(
            "succeeded",
            data,
            runId: agentRunId,
            proposalId: data.ProposalIds.FirstOrDefault() ?? string.Empty,
            evidenceId: data.EvidenceRefs.FirstOrDefault() ?? string.Empty,
            mutationOccurred: false,
            warnings: BoundaryWarnings().Concat(data.Warnings).ToArray()));
    }

    private IReadOnlyList<ManualMemoryImprovementApiErrorDto> ValidateCreateRequest(ManualMemoryImprovementRequestDto request)
    {
        var errors = new List<ManualMemoryImprovementApiErrorDto>();

        if (request.ProjectId <= 0)
            errors.Add(ValidationError(nameof(request.ProjectId), "Project id is required."));

        if (string.IsNullOrWhiteSpace(request.SourceType))
            errors.Add(ValidationError(nameof(request.SourceType), "Source type is required."));

        if (string.IsNullOrWhiteSpace(request.SourceId))
            errors.Add(ValidationError(nameof(request.SourceId), "Source id is required."));

        if (string.IsNullOrWhiteSpace(request.Summary))
            errors.Add(ValidationError(nameof(request.Summary), "Summary is required."));

        if (string.IsNullOrWhiteSpace(request.Content))
            errors.Add(ValidationError(nameof(request.Content), "Content is required."));

        if (!string.IsNullOrWhiteSpace(request.Summary) && request.Summary.Length > MaxSummaryLength)
            errors.Add(ValidationError(nameof(request.Summary), $"Summary must be {MaxSummaryLength} characters or fewer."));

        if (!string.IsNullOrWhiteSpace(request.Content) && request.Content.Length > MaxContentLength)
            errors.Add(ValidationError(nameof(request.Content), $"Content must be {MaxContentLength} characters or fewer."));

        if (!string.IsNullOrWhiteSpace(request.Context) && request.Context.Length > MaxContextLength)
            errors.Add(ValidationError(nameof(request.Context), $"Context must be {MaxContextLength} characters or fewer."));

        if (!string.IsNullOrWhiteSpace(request.CandidateType) &&
            !Enum.TryParse<MemoryImprovementPatternType>(request.CandidateType, ignoreCase: true, out _))
        {
            errors.Add(ValidationError(nameof(request.CandidateType), "Candidate type must be a supported memory-improvement pattern type."));
        }

        if (request.EvidenceRefs.Count > MaxEvidenceRefs)
            errors.Add(ValidationError(nameof(request.EvidenceRefs), $"Evidence references are limited to {MaxEvidenceRefs}."));

        if (request.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence references cannot be blank."));

        var textValues = new[]
        {
            request.SourceType,
            request.SourceId,
            request.Summary,
            request.Content,
            request.Context,
            request.CandidateType,
            request.CorrelationId
        }.Concat(request.EvidenceRefs);

        if (ContainsAny(textValues, PrivateReasoningMarkers))
            errors.Add(ValidationError("request", "Manual Memory Improvement API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, or private reasoning."));

        if (ContainsAny(textValues, AuthorityMarkers))
            errors.Add(ValidationError("request", "Manual Memory Improvement API v1 does not accept approval, promotion, accepted-memory, CollectiveMemory, vector authority, execution, source apply, tool execution, or external submission claims."));

        foreach (var property in request.ExtraProperties?.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase) ?? Enumerable.Empty<string>())
        {
            errors.Add(UnsupportedField(property));
        }

        return errors;
    }

    private IReadOnlyList<ManualMemoryImprovementApiErrorDto> ValidateGetRequest(int projectId, string agentRunId)
    {
        var errors = new List<ManualMemoryImprovementApiErrorDto>();
        var unsupported = Request.Query.Keys
            .Where(key => !DetailQueryKeys.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        errors.AddRange(unsupported.Select(UnsupportedFilter));

        if (projectId <= 0)
            errors.Add(ValidationError("projectId", "Project id is required."));

        if (string.IsNullOrWhiteSpace(agentRunId))
            errors.Add(ValidationError("agentRunId", "Agent run id is required."));

        return errors;
    }

    private static ManualMemoryImprovementDetectionRequest BuildManualRequest(
        ManualMemoryImprovementRequestDto request,
        string tenantId,
        string requestedByUserId)
    {
        var detectionRequestId = BuildDetectionRequestId(request);
        var evidenceRefs = request.EvidenceRefs.Count == 0
            ? [$"manual-memory-improvement-api:{request.SourceType}:{request.SourceId}"]
            : request.EvidenceRefs.Select(value => SanitiseText(value)).ToArray();
        var patternType = ParsePatternType(request.CandidateType);

        return new ManualMemoryImprovementDetectionRequest
        {
            DetectionRequestId = detectionRequestId,
            TenantId = tenantId,
            ProjectId = request.ProjectId.ToString(),
            CampaignId = $"manual-memory-improvement-api-v1-project-{request.ProjectId}",
            RunId = request.CorrelationId is { Length: > 0 }
                ? SanitiseId(request.CorrelationId)
                : $"manual-memory-improvement-api-v1-{detectionRequestId}",
            RequestedByUserId = requestedByUserId,
            CorrelationId = SanitiseText(request.CorrelationId ?? string.Empty),
            RequestSummary = SanitiseText(request.Summary),
            Inputs =
            [
                new ManualMemoryImprovementInputRef
                {
                    InputRefId = $"input-{detectionRequestId}-001",
                    RefType = SanitiseText(request.SourceType),
                    RefId = SanitiseText(request.SourceId),
                    Source = "ManualMemoryImprovementApiV1",
                    Summary = BuildInputSummary(request),
                    EvidenceRefs = evidenceRefs,
                    ContainsRawPrivateReasoning = false,
                    IsAuthoritativeForAction = false
                }
            ],
            PatternDrafts =
            [
                new ManualMemoryImprovementPatternDraft
                {
                    PatternType = patternType,
                    Summary = SanitiseText(request.Content),
                    Confidence = 0.75m,
                    EvidenceRefs = evidenceRefs,
                    RelatedMemoryIds = [],
                    RelatedProposalIds = [],
                    IsDuplicateCandidate = patternType == MemoryImprovementPatternType.DuplicateProposalPattern,
                    RequiresHumanReview = true
                }
            ],
            ProposalDrafts =
            [
                new ManualMemoryImprovementProposalDraftInput
                {
                    Title = SanitiseText(request.Summary),
                    Summary = SanitiseText(request.Content),
                    Rationale = string.IsNullOrWhiteSpace(request.Context)
                        ? "Manual memory-improvement API output is proposal-only evidence. Human review remains required before any separate persistence or promotion path."
                        : SanitiseText(request.Context),
                    SourcePatternIndex = 0,
                    EvidenceRefs = evidenceRefs,
                    IsProposalOnly = true,
                    CreatesCollectiveMemory = false,
                    PromotesMemory = false,
                    RequiresHumanReview = true
                }
            ],
            NoProposalReason = null
        };
    }

    private static ManualMemoryImprovementResponseDto ToResponseDto(
        StoredManualAgentExecutionResult<MemoryImprovementDetectionResult> result,
        bool mutationOccurred)
    {
        var output = result.Output;
        var evidenceRefs = output?.Findings.SelectMany(finding => finding.EvidenceRefs)
            .Concat(output.ProposalDrafts.SelectMany(proposal => proposal.EvidenceRefs))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray()
            ?? result.AuditEnvelope?.Outputs.SelectMany(outputRef => outputRef.EvidenceRefs).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            ?? [];

        return new ManualMemoryImprovementResponseDto
        {
            AgentRunId = result.AgentRunId,
            DetectionResultId = output?.DetectionResultId ?? string.Empty,
            Status = result.Status.ToString(),
            DetectedAtUtc = output?.DetectedAt,
            PatternCount = output?.Findings.Count ?? 0,
            ProposalCount = output?.ProposalDrafts.Count ?? 0,
            ProposalIds = output?.ProposalDrafts.Select(proposal => SanitiseText(proposal.ProposalDraftId)).ToArray() ?? [],
            Patterns = output?.Findings.Select(ToPatternDto).ToArray() ?? [],
            Proposals = output?.ProposalDrafts.Select(ToProposalDto).ToArray() ?? [],
            EvidenceRefs = evidenceRefs,
            MutationOccurred = mutationOccurred,
            ProposalOnly = true,
            RequiresHumanReview = true,
            Warnings = output?.Warnings.Select(value => SanitiseText(value)).ToArray() ?? []
        };
    }

    private static ManualMemoryImprovementDetailDto ToDetailDto(AgentRunDetailResponseDto response)
    {
        var run = response.Run!;
        var safety = run.SafetySummary;
        var forceRedact = safety.ContainsRawPrivateReasoning;
        var detectionOutput = run.Outputs.FirstOrDefault(output => string.Equals(output.RefType, "MemoryImprovementDetectionResult", StringComparison.OrdinalIgnoreCase));
        var proposalOutputs = run.Outputs.Where(output => string.Equals(output.RefType, "MemoryImprovementProposalDraft", StringComparison.OrdinalIgnoreCase)).ToArray();
        var evidenceRefs = run.Inputs.SelectMany(input => input.EvidenceRefs)
            .Concat(run.Outputs.SelectMany(outputRef => outputRef.EvidenceRefs))
            .Concat(run.ThoughtLedger.SelectMany(thought => thought.EvidenceRefs))
            .Concat(run.BoundaryDecisions.SelectMany(boundary => boundary.EvidenceRefs))
            .Select(value => SanitiseText(value, forceRedact))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new ManualMemoryImprovementDetailDto
        {
            ProjectId = response.ProjectId,
            AgentRunId = response.AgentRunId,
            DetectionResultId = SanitiseText(detectionOutput?.RefId ?? string.Empty, forceRedact),
            ProposalIds = proposalOutputs.Select(output => SanitiseText(output.RefId, forceRedact || output.ContainsRawPrivateReasoning)).ToArray(),
            Status = run.Run.Status.ToString(),
            AgentId = SanitiseText(run.Run.AgentId, forceRedact),
            AgentName = SanitiseText(run.Run.AgentName, forceRedact),
            RequestSummary = SanitiseText(run.Run.RequestSummary, forceRedact),
            Purpose = SanitiseText(run.Run.Purpose, forceRedact),
            CreatedAtUtc = run.Run.CreatedAtUtc,
            CompletedAtUtc = run.Run.CompletedAtUtc,
            InputSummaries = run.Inputs.Select(input => SanitiseText(input.Summary, forceRedact || input.ContainsRawPrivateReasoning)).ToArray(),
            OutputSummaries = run.Outputs.Select(output => SanitiseText(output.Summary, forceRedact || output.ContainsRawPrivateReasoning)).ToArray(),
            ThoughtLedgerSummaries = run.ThoughtLedger.Select(thought => SanitiseText(thought.Summary, forceRedact || thought.ContainsRawPrivateReasoning)).ToArray(),
            EvidenceRefs = evidenceRefs,
            ProposalOnlyOutput = detectionOutput?.IsProposalOnly == true && proposalOutputs.All(output => output.IsProposalOnly),
            CreatesAuthority = run.Outputs.Any(output => output.CreatesAuthority),
            CreatesRuntimeAction = run.Outputs.Any(output => output.CreatesRuntimeAction),
            BoundaryBlocks = run.SafetySummary.HasBoundaryBlock || run.SafetySummary.HasBlockedCapabilityAttempt,
            SafetySummary = run.SafetySummary with
            {
                Warnings = run.SafetySummary.Warnings.Select(warning => SanitiseText(warning, forceRedact)).ToArray()
            },
            ProposalOnly = true,
            RequiresHumanReview = true,
            Warnings = run.SafetySummary.Warnings.Select(warning => SanitiseText(warning, forceRedact)).ToArray()
        };
    }

    private static ManualMemoryImprovementPatternDto ToPatternDto(MemoryImprovementPatternFinding finding) =>
        new()
        {
            PatternFindingId = SanitiseText(finding.PatternFindingId),
            PatternType = finding.PatternType.ToString(),
            Summary = SanitiseText(finding.Summary),
            Confidence = finding.Confidence,
            EvidenceRefs = finding.EvidenceRefs.Select(value => SanitiseText(value)).ToArray(),
            RelatedMemoryIds = finding.RelatedMemoryIds.Select(value => SanitiseText(value)).ToArray(),
            RelatedProposalIds = finding.RelatedProposalIds.Select(value => SanitiseText(value)).ToArray(),
            IsDuplicateCandidate = finding.IsDuplicateCandidate,
            RequiresHumanReview = finding.RequiresHumanReview
        };

    private static ManualMemoryImprovementProposalDto ToProposalDto(MemoryImprovementProposalDraft proposal) =>
        new()
        {
            ProposalDraftId = SanitiseText(proposal.ProposalDraftId),
            Title = SanitiseText(proposal.Title),
            Summary = SanitiseText(proposal.Summary),
            Rationale = SanitiseText(proposal.Rationale),
            EvidenceRefs = proposal.EvidenceRefs.Select(value => SanitiseText(value)).ToArray(),
            IsProposalOnly = proposal.IsProposalOnly,
            CreatesCollectiveMemory = proposal.CreatesCollectiveMemory,
            PromotesMemory = proposal.PromotesMemory,
            RequiresHumanReview = proposal.RequiresHumanReview
        };

    private static bool IsManualMemoryImprovementRun(AgentRunDetailDto run) =>
        string.Equals(run.Run.AgentId, AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, StringComparison.Ordinal) &&
        run.Outputs.Any(output => string.Equals(output.RefType, "MemoryImprovementDetectionResult", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(output.RefType, "MemoryImprovementProposalDraft", StringComparison.OrdinalIgnoreCase));

    private static string BuildInputSummary(ManualMemoryImprovementRequestDto request)
    {
        var context = string.IsNullOrWhiteSpace(request.Context)
            ? string.Empty
            : $" Context: {request.Context.Trim()}";

        return SanitiseText($"{request.Summary.Trim()} Content: {request.Content.Trim()}{context}");
    }

    private static string BuildDetectionRequestId(ManualMemoryImprovementRequestDto request)
    {
        var seed = string.Join(
            "-",
            request.ProjectId.ToString(),
            request.SourceType ?? string.Empty,
            request.SourceId ?? string.Empty,
            string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId);

        return SanitiseId(seed);
    }

    private static string SanitiseId(string value)
    {
        var safe = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Select(character => character == '.' ? '-' : char.ToLowerInvariant(character))
            .ToArray());

        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private static MemoryImprovementPatternType ParsePatternType(string? candidateType)
    {
        return Enum.TryParse<MemoryImprovementPatternType>(candidateType, ignoreCase: true, out var patternType)
            ? patternType
            : MemoryImprovementPatternType.RepeatedManualCorrection;
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

    private static ManualMemoryImprovementApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string runId = "",
        string proposalId = "",
        string evidenceId = "",
        bool mutationOccurred = false,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<ManualMemoryImprovementApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            RunId = runId,
            ProposalId = proposalId,
            EvidenceId = evidenceId,
            Boundary = BoundaryStatus(),
            MutationOccurred = mutationOccurred,
            HumanApprovalRequired = true,
            Warnings = warnings ?? [],
            Errors = errors ?? []
        };

    private static ManualMemoryImprovementBoundaryStatusDto BoundaryStatus() =>
        new()
        {
            MemoryImprovementIsPromotion = false,
            MemoryProposalIsPromotion = false,
            MemorySafeIsApproval = false,
            CandidateIsMemory = false,
            RetrievalMatchIsMemoryCandidate = false,
            AuditIsApproval = false,
            SourceApplied = false,
            MemoryPromoted = false,
            CollectiveMemoryWritten = false,
            VectorAuthorityWritten = false,
            ToolExecuted = false,
            ModelOutputIsAuthority = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            HumanReviewRequiredForMemoryPromotion = true,
            HumanReviewRequiredForSourceApply = true
        };

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Manual Memory Improvement API v1 creates or inspects proposal-only memory-improvement evidence only.",
        "Memory-improvement output is not promotion. Memory safety is not approval. Candidate is not memory. Retrieval match is not memory candidate.",
        "Human review remains required before any separate memory promotion or source apply path."
    ];

    private static ManualMemoryImprovementApiErrorDto ValidationError(string field, string message) =>
        new()
        {
            Category = "validation_error",
            Code = "MANUAL_MEMORY_IMPROVEMENT_API_VALIDATION_ERROR",
            Message = SanitiseText(message),
            Field = field
        };

    private static ManualMemoryImprovementApiErrorDto UnsupportedField(string field) =>
        new()
        {
            Category = "unsupported_field",
            Code = "MANUAL_MEMORY_IMPROVEMENT_API_UNSUPPORTED_FIELD",
            Message = $"Unsupported field: {SanitiseText(field)}.",
            Field = SanitiseText(field)
        };

    private static ManualMemoryImprovementApiErrorDto UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_filter",
            Code = "MANUAL_MEMORY_IMPROVEMENT_API_UNSUPPORTED_FILTER",
            Message = $"Unsupported filter: {SanitiseText(filter)}.",
            Field = SanitiseText(filter)
        };

    private static IReadOnlyList<ManualMemoryImprovementApiErrorDto> ToErrors(IReadOnlyList<StoredManualAgentExecutionIssue> issues) =>
        issues.Select(issue => new ManualMemoryImprovementApiErrorDto
        {
            Category = "execution_error",
            Code = issue.Code,
            Message = SanitiseText(issue.Message),
            Field = issue.Field ?? string.Empty
        }).ToArray();

    private static IReadOnlyList<ManualMemoryImprovementApiErrorDto> ToErrors(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Select(issue => new ManualMemoryImprovementApiErrorDto
        {
            Category = string.Equals(issue.Severity, "not_found", StringComparison.OrdinalIgnoreCase)
                ? "not_found"
                : "validation_error",
            Code = issue.Code,
            Message = SanitiseText(issue.Message)
        }).ToArray();

    private static bool HasError(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    private static bool IsNotFound(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Any(issue => string.Equals(issue.Code, AgentRunAuditQueryService.AgentRunNotFound, StringComparison.Ordinal));
}

public sealed record ManualMemoryImprovementRequestDto
{
    public int ProjectId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? Context { get; init; }
    public string? CandidateType { get; init; }
    public string? CorrelationId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraProperties { get; init; }
}

public sealed record ManualMemoryImprovementApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string RunId { get; init; } = string.Empty;
    public string ProposalId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required ManualMemoryImprovementBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ManualMemoryImprovementApiErrorDto> Errors { get; init; } = [];
}

public sealed record ManualMemoryImprovementBoundaryStatusDto
{
    public bool MemoryImprovementIsPromotion { get; init; }
    public bool MemoryProposalIsPromotion { get; init; }
    public bool MemorySafeIsApproval { get; init; }
    public bool CandidateIsMemory { get; init; }
    public bool RetrievalMatchIsMemoryCandidate { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool SourceApplied { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool CollectiveMemoryWritten { get; init; }
    public bool VectorAuthorityWritten { get; init; }
    public bool ToolExecuted { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
}

public sealed record ManualMemoryImprovementResponseDto
{
    public required string AgentRunId { get; init; }
    public required string DetectionResultId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? DetectedAtUtc { get; init; }
    public int PatternCount { get; init; }
    public int ProposalCount { get; init; }
    public IReadOnlyList<string> ProposalIds { get; init; } = [];
    public IReadOnlyList<ManualMemoryImprovementPatternDto> Patterns { get; init; } = [];
    public IReadOnlyList<ManualMemoryImprovementProposalDto> Proposals { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool MutationOccurred { get; init; }
    public bool ProposalOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ManualMemoryImprovementDetailDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required string DetectionResultId { get; init; }
    public IReadOnlyList<string> ProposalIds { get; init; } = [];
    public required string Status { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required string RequestSummary { get; init; }
    public required string Purpose { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public IReadOnlyList<string> InputSummaries { get; init; } = [];
    public IReadOnlyList<string> OutputSummaries { get; init; } = [];
    public IReadOnlyList<string> ThoughtLedgerSummaries { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ProposalOnlyOutput { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public bool BoundaryBlocks { get; init; }
    public required AgentRunSafetySummaryDto SafetySummary { get; init; }
    public bool ProposalOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ManualMemoryImprovementPatternDto
{
    public required string PatternFindingId { get; init; }
    public required string PatternType { get; init; }
    public required string Summary { get; init; }
    public decimal Confidence { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> RelatedMemoryIds { get; init; } = [];
    public IReadOnlyList<string> RelatedProposalIds { get; init; } = [];
    public bool IsDuplicateCandidate { get; init; }
    public bool RequiresHumanReview { get; init; }
}

public sealed record ManualMemoryImprovementProposalDto
{
    public required string ProposalDraftId { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool RequiresHumanReview { get; init; }
}

public sealed record ManualMemoryImprovementApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

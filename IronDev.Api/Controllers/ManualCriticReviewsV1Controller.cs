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
[Route("api/v1/manual-critic/reviews")]
public sealed class ManualCriticReviewsV1Controller : ControllerBase
{
    private const int MaxSummaryLength = 500;
    private const int MaxContentLength = 12_000;
    private const int MaxContextLength = 4_000;
    private const int MaxEvidenceRefs = 50;
    private const string DefaultSpecialisationId = "builtin.critic.code-review";
    private const string RedactedPrivateReasoning = "[redacted: sensitive critic audit text]";

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
        "source applied",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "tool executed",
        "submit github review",
        "create pull request"
    ];

    private readonly IStoredManualIndependentCriticAgentService _criticService;
    private readonly IAgentRunAuditQueryService _queryService;

    public ManualCriticReviewsV1Controller(
        IStoredManualIndependentCriticAgentService criticService,
        IAgentRunAuditQueryService queryService)
    {
        _criticService = criticService ?? throw new ArgumentNullException(nameof(criticService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [HttpPost]
    public ActionResult<ManualCriticApiEnvelope<ManualCriticReviewResponseDto>> Create(
        [FromBody] ManualCriticReviewRequestDto request)
    {
        var validation = ValidateCreateRequest(request);
        if (validation.Count > 0)
            return BadRequest(Envelope<ManualCriticReviewResponseDto>("validation_error", null, errors: validation));

        var tenantId = CurrentTenantId();
        if (tenantId <= 0)
        {
            return BadRequest(Envelope<ManualCriticReviewResponseDto>(
                "validation_error",
                null,
                errors: [ValidationError("tenant", "A selected tenant is required.")]));
        }

        var manualRequest = BuildManualRequest(request, tenantId.ToString(), CurrentUserId());
        var result = _criticService.ExecuteAndStore(
            manualRequest,
            new ManualAgentExecutionSpecialisationSelection
            {
                SpecialisationId = DefaultSpecialisationId,
                RequestedByUserId = manualRequest.RequestedByUserId,
                Reason = "Create manual critic review evidence through API v1. This selection grants no approval, governance, execution, source apply, or memory promotion authority."
            },
            DateTimeOffset.UtcNow);

        if (result.Status == StoredManualAgentExecutionStatus.Conflict)
        {
            return Conflict(Envelope(
                "conflict",
                ToResponseDto(result, mutationOccurred: false),
                runId: result.AgentRunId,
                reviewId: result.Output?.ReviewResultId ?? string.Empty,
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
                reviewId: result.Output?.ReviewResultId ?? string.Empty,
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
            reviewId: data.ReviewId,
            evidenceId: data.EvidenceRefs.FirstOrDefault() ?? string.Empty,
            mutationOccurred: mutationOccurred,
            warnings: BoundaryWarnings().Concat(data.Warnings).ToArray()));
    }

    [HttpGet("{agentRunId}")]
    public ActionResult<ManualCriticApiEnvelope<ManualCriticReviewDetailDto>> Get(
        [FromQuery] int projectId,
        string agentRunId)
    {
        var validation = ValidateGetRequest(projectId, agentRunId);
        if (validation.Count > 0)
        {
            return BadRequest(Envelope<ManualCriticReviewDetailDto>(
                "validation_error",
                null,
                runId: agentRunId,
                errors: validation));
        }

        var response = _queryService.GetAgentRun(projectId.ToString(), agentRunId);
        if (IsNotFound(response.Issues) || response.Run is null || !IsManualCriticRun(response.Run))
        {
            return NotFound(Envelope<ManualCriticReviewDetailDto>(
                "not_found",
                null,
                runId: agentRunId,
                errors: IsNotFound(response.Issues)
                    ? ToErrors(response.Issues)
                    : [ValidationError("agentRunId", "Manual critic review was not found for this project.")]));
        }

        if (HasError(response.Issues))
        {
            return BadRequest(Envelope<ManualCriticReviewDetailDto>(
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
            reviewId: data.ReviewId,
            evidenceId: data.EvidenceRefs.FirstOrDefault() ?? string.Empty,
            mutationOccurred: false,
            warnings: BoundaryWarnings().Concat(data.Warnings).ToArray()));
    }

    private IReadOnlyList<ManualCriticApiErrorDto> ValidateCreateRequest(ManualCriticReviewRequestDto request)
    {
        var errors = new List<ManualCriticApiErrorDto>();

        if (request.ProjectId <= 0)
            errors.Add(ValidationError(nameof(request.ProjectId), "Project id is required."));

        if (!Enum.TryParse<CriticReviewSubjectType>(request.SubjectType, ignoreCase: true, out _))
            errors.Add(ValidationError(nameof(request.SubjectType), "Subject type is required and must be supported."));

        if (string.IsNullOrWhiteSpace(request.SubjectId))
            errors.Add(ValidationError(nameof(request.SubjectId), "Subject id is required."));

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

        if (request.EvidenceRefs.Count > MaxEvidenceRefs)
            errors.Add(ValidationError(nameof(request.EvidenceRefs), $"Evidence references are limited to {MaxEvidenceRefs}."));

        if (request.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            errors.Add(ValidationError(nameof(request.EvidenceRefs), "Evidence references cannot be blank."));

        var textValues = new[]
        {
            request.SubjectType,
            request.SubjectId,
            request.Summary,
            request.Content,
            request.Context,
            request.SeverityHint,
            request.CorrelationId
        }.Concat(request.EvidenceRefs);

        if (ContainsAny(textValues, PrivateReasoningMarkers))
            errors.Add(ValidationError("request", "Manual critic API v1 does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, or private reasoning."));

        if (ContainsAny(textValues, AuthorityMarkers))
            errors.Add(ValidationError("request", "Manual critic API v1 does not accept approval, governance, execution, source apply, memory promotion, tool execution, or external submission claims."));

        foreach (var property in request.ExtraProperties?.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase) ?? Enumerable.Empty<string>())
        {
            errors.Add(UnsupportedField(property));
        }

        return errors;
    }

    private IReadOnlyList<ManualCriticApiErrorDto> ValidateGetRequest(int projectId, string agentRunId)
    {
        var errors = new List<ManualCriticApiErrorDto>();
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

    private static ManualCriticReviewRequest BuildManualRequest(
        ManualCriticReviewRequestDto request,
        string tenantId,
        string requestedByUserId)
    {
        var subjectType = Enum.Parse<CriticReviewSubjectType>(request.SubjectType, ignoreCase: true);
        var reviewRequestId = BuildReviewRequestId(request);
        var evidenceRefs = request.EvidenceRefs.Count == 0
            ? [$"manual-critic-api:{request.SubjectType}:{request.SubjectId}"]
            : request.EvidenceRefs.Select(value => SanitiseText(value)).ToArray();

        return new ManualCriticReviewRequest
        {
            ReviewRequestId = reviewRequestId,
            TenantId = tenantId,
            ProjectId = request.ProjectId.ToString(),
            CampaignId = $"manual-critic-api-v1-project-{request.ProjectId}",
            RunId = request.CorrelationId is { Length: > 0 }
                ? SanitiseId(request.CorrelationId)
                : $"manual-critic-api-v1-{reviewRequestId}",
            SubjectType = subjectType,
            SubjectId = SanitiseText(request.SubjectId),
            RequestedByUserId = requestedByUserId,
            CorrelationId = SanitiseText(request.CorrelationId ?? string.Empty),
            RequestSummary = SanitiseText(request.Summary),
            Inputs =
            [
                new ManualCriticReviewInputRef
                {
                    InputRefId = $"input-{reviewRequestId}-001",
                    RefType = subjectType.ToString(),
                    RefId = SanitiseText(request.SubjectId),
                    Source = "ManualCriticApiV1",
                    Summary = BuildInputSummary(request),
                    EvidenceRefs = evidenceRefs,
                    ContainsRawPrivateReasoning = false,
                    IsAuthoritativeForAction = false
                }
            ],
            FindingDrafts =
            [
                new ManualCriticFindingDraft
                {
                    Severity = ParseSeverity(request.SeverityHint),
                    Title = SanitiseText(request.Summary),
                    Problem = SanitiseText(request.Content),
                    WhyItMatters = string.IsNullOrWhiteSpace(request.Context)
                        ? "Manual critic API output is advisory evidence that must remain separate from approval, governance, source apply, tool execution, and memory promotion."
                        : SanitiseText(request.Context),
                    RequiredFix = "A human reviewer must inspect this critic finding and decide any separate follow-up action.",
                    EvidenceRefs = evidenceRefs,
                    BlocksMerge = false,
                    RequiresHumanReview = true
                }
            ],
            RequestedVerdict = CriticReviewVerdict.RequestChanges
        };
    }

    private static ManualCriticReviewResponseDto ToResponseDto(
        StoredManualAgentExecutionResult<CriticReviewResult> result,
        bool mutationOccurred)
    {
        var output = result.Output;
        var evidenceRefs = output?.Findings.SelectMany(finding => finding.EvidenceRefs).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            ?? result.AuditEnvelope?.Outputs.SelectMany(outputRef => outputRef.EvidenceRefs).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            ?? [];

        return new ManualCriticReviewResponseDto
        {
            AgentRunId = result.AgentRunId,
            ReviewId = output?.ReviewResultId ?? string.Empty,
            ReviewRequestId = output?.ReviewRequestId ?? string.Empty,
            Status = result.Status.ToString(),
            Verdict = output?.Verdict.ToString() ?? string.Empty,
            ReviewedAtUtc = output?.ReviewedAt,
            FindingCount = output?.Findings.Count ?? 0,
            Findings = output?.Findings.Select(ToFindingDto).ToArray() ?? [],
            EvidenceRefs = evidenceRefs,
            MutationOccurred = mutationOccurred,
            AdvisoryOnly = true,
            RequiresHumanReview = true,
            Warnings = output?.Warnings.Select(value => SanitiseText(value)).ToArray() ?? []
        };
    }

    private static ManualCriticReviewDetailDto ToDetailDto(AgentRunDetailResponseDto response)
    {
        var run = response.Run!;
        var safety = run.SafetySummary;
        var forceRedact = safety.ContainsRawPrivateReasoning;
        var output = run.Outputs.FirstOrDefault(outputRef => string.Equals(outputRef.RefType, "CriticReviewResult", StringComparison.OrdinalIgnoreCase));
        var evidenceRefs = run.Inputs.SelectMany(input => input.EvidenceRefs)
            .Concat(run.Outputs.SelectMany(outputRef => outputRef.EvidenceRefs))
            .Concat(run.ThoughtLedger.SelectMany(thought => thought.EvidenceRefs))
            .Concat(run.BoundaryDecisions.SelectMany(boundary => boundary.EvidenceRefs))
            .Select(value => SanitiseText(value, forceRedact))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new ManualCriticReviewDetailDto
        {
            ProjectId = response.ProjectId,
            AgentRunId = response.AgentRunId,
            ReviewId = SanitiseText(output?.RefId ?? string.Empty, forceRedact),
            Status = run.Run.Status.ToString(),
            AgentId = SanitiseText(run.Run.AgentId, forceRedact),
            AgentName = SanitiseText(run.Run.AgentName, forceRedact),
            RequestSummary = SanitiseText(run.Run.RequestSummary, forceRedact),
            Purpose = SanitiseText(run.Run.Purpose, forceRedact),
            CreatedAtUtc = run.Run.CreatedAtUtc,
            CompletedAtUtc = run.Run.CompletedAtUtc,
            InputSummaries = run.Inputs.Select(input => SanitiseText(input.Summary, forceRedact || input.ContainsRawPrivateReasoning)).ToArray(),
            OutputSummaries = run.Outputs.Select(outputRef => SanitiseText(outputRef.Summary, forceRedact || outputRef.ContainsRawPrivateReasoning)).ToArray(),
            ThoughtLedgerSummaries = run.ThoughtLedger.Select(thought => SanitiseText(thought.Summary, forceRedact || thought.ContainsRawPrivateReasoning)).ToArray(),
            EvidenceRefs = evidenceRefs,
            ReviewOnlyOutput = output?.IsReviewOnly == true,
            CreatesAuthority = run.Outputs.Any(outputRef => outputRef.CreatesAuthority),
            CreatesRuntimeAction = run.Outputs.Any(outputRef => outputRef.CreatesRuntimeAction),
            BoundaryBlocks = run.SafetySummary.HasBoundaryBlock || run.SafetySummary.HasBlockedCapabilityAttempt,
            SafetySummary = run.SafetySummary with
            {
                Warnings = run.SafetySummary.Warnings.Select(warning => SanitiseText(warning, forceRedact)).ToArray()
            },
            AdvisoryOnly = true,
            RequiresHumanReview = true,
            Warnings = run.SafetySummary.Warnings.Select(warning => SanitiseText(warning, forceRedact)).ToArray()
        };
    }

    private static ManualCriticFindingDto ToFindingDto(CriticFinding finding) =>
        new()
        {
            FindingId = SanitiseText(finding.FindingId),
            Severity = finding.Severity.ToString(),
            Title = SanitiseText(finding.Title),
            Problem = SanitiseText(finding.Problem),
            WhyItMatters = SanitiseText(finding.WhyItMatters),
            RequiredFix = SanitiseText(finding.RequiredFix),
            EvidenceRefs = finding.EvidenceRefs.Select(value => SanitiseText(value)).ToArray(),
            BlocksMerge = finding.BlocksMerge,
            RequiresHumanReview = finding.RequiresHumanReview
        };

    private static bool IsManualCriticRun(AgentRunDetailDto run) =>
        string.Equals(run.Run.AgentId, AgentDefinitionCatalog.IndependentCriticAgent.AgentId, StringComparison.Ordinal) &&
        run.Outputs.Any(output => string.Equals(output.RefType, "CriticReviewResult", StringComparison.OrdinalIgnoreCase));

    private static string BuildInputSummary(ManualCriticReviewRequestDto request)
    {
        var context = string.IsNullOrWhiteSpace(request.Context)
            ? string.Empty
            : $" Context: {request.Context.Trim()}";

        return SanitiseText($"{request.Summary.Trim()} Content: {request.Content.Trim()}{context}");
    }

    private static string BuildReviewRequestId(ManualCriticReviewRequestDto request)
    {
        var seed = string.Join(
            "-",
            request.ProjectId.ToString(),
            request.SubjectType ?? string.Empty,
            request.SubjectId ?? string.Empty,
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

    private static CriticSeverity ParseSeverity(string? severityHint)
    {
        return Enum.TryParse<CriticSeverity>(severityHint, ignoreCase: true, out var severity)
            ? severity
            : CriticSeverity.Medium;
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

    private static ManualCriticApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string runId = "",
        string reviewId = "",
        string evidenceId = "",
        bool mutationOccurred = false,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<ManualCriticApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            RunId = runId,
            ReviewId = reviewId,
            EvidenceId = evidenceId,
            Boundary = BoundaryStatus(),
            MutationOccurred = mutationOccurred,
            HumanApprovalRequired = true,
            Warnings = warnings ?? [],
            Errors = errors ?? []
        };

    private static ManualCriticBoundaryStatusDto BoundaryStatus() =>
        new()
        {
            CriticIsGovernance = false,
            CriticIsApproval = false,
            AuditIsApproval = false,
            ProposalWasApplied = false,
            SourceApplied = false,
            MemoryPromoted = false,
            ToolExecuted = false,
            ModelOutputIsAuthority = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            HumanReviewRequiredForSourceApply = true,
            HumanReviewRequiredForMemoryPromotion = true
        };

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Manual Critic API v1 creates or inspects critic review evidence only.",
        "Critic review is advisory. It is not governance, approval, source apply, memory promotion, tool execution, or execution permission.",
        "Human review remains required before any separate source apply or memory promotion path."
    ];

    private static ManualCriticApiErrorDto ValidationError(string field, string message) =>
        new()
        {
            Category = "validation_error",
            Code = "MANUAL_CRITIC_API_VALIDATION_ERROR",
            Message = SanitiseText(message),
            Field = field
        };

    private static ManualCriticApiErrorDto UnsupportedField(string field) =>
        new()
        {
            Category = "unsupported_field",
            Code = "MANUAL_CRITIC_API_UNSUPPORTED_FIELD",
            Message = $"Unsupported field: {SanitiseText(field)}.",
            Field = SanitiseText(field)
        };

    private static ManualCriticApiErrorDto UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_filter",
            Code = "MANUAL_CRITIC_API_UNSUPPORTED_FILTER",
            Message = $"Unsupported filter: {SanitiseText(filter)}.",
            Field = SanitiseText(filter)
        };

    private static IReadOnlyList<ManualCriticApiErrorDto> ToErrors(IReadOnlyList<StoredManualAgentExecutionIssue> issues) =>
        issues.Select(issue => new ManualCriticApiErrorDto
        {
            Category = "execution_error",
            Code = issue.Code,
            Message = SanitiseText(issue.Message),
            Field = issue.Field ?? string.Empty
        }).ToArray();

    private static IReadOnlyList<ManualCriticApiErrorDto> ToErrors(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Select(issue => new ManualCriticApiErrorDto
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

public sealed record ManualCriticReviewRequestDto
{
    public int ProjectId { get; init; }
    public string SubjectType { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? Context { get; init; }
    public string? SeverityHint { get; init; }
    public string? CorrelationId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraProperties { get; init; }
}

public sealed record ManualCriticApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string RunId { get; init; } = string.Empty;
    public string ReviewId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required ManualCriticBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ManualCriticApiErrorDto> Errors { get; init; } = [];
}

public sealed record ManualCriticBoundaryStatusDto
{
    public bool CriticIsGovernance { get; init; }
    public bool CriticIsApproval { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool ProposalWasApplied { get; init; }
    public bool SourceApplied { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool ToolExecuted { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
}

public sealed record ManualCriticReviewResponseDto
{
    public required string AgentRunId { get; init; }
    public required string ReviewId { get; init; }
    public required string ReviewRequestId { get; init; }
    public required string Status { get; init; }
    public required string Verdict { get; init; }
    public DateTimeOffset? ReviewedAtUtc { get; init; }
    public int FindingCount { get; init; }
    public IReadOnlyList<ManualCriticFindingDto> Findings { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool MutationOccurred { get; init; }
    public bool AdvisoryOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ManualCriticReviewDetailDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required string ReviewId { get; init; }
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
    public bool ReviewOnlyOutput { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public bool BoundaryBlocks { get; init; }
    public required AgentRunSafetySummaryDto SafetySummary { get; init; }
    public bool AdvisoryOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ManualCriticFindingDto
{
    public required string FindingId { get; init; }
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Problem { get; init; }
    public required string WhyItMatters { get; init; }
    public required string RequiredFix { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool BlocksMerge { get; init; }
    public bool RequiresHumanReview { get; init; }
}

public sealed record ManualCriticApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

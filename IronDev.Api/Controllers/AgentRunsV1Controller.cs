using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/agent-runs")]
public sealed class AgentRunsV1Controller : ControllerBase
{
    private static readonly HashSet<string> ListQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectId",
        "agentId",
        "agentKind",
        "status",
        "triggerType",
        "createdAfterUtc",
        "createdBeforeUtc",
        "runId",
        "correlationId",
        "take",
        "skip"
    };

    private static readonly HashSet<string> DetailQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectId"
    };

    private const string RedactedPrivateReasoning = "[redacted: sensitive audit text]";

    private static readonly string[] PrivateReasoningMarkers =
    [
        "chain-of-thought",
        "hidden reasoning",
        "private reasoning",
        "raw prompt",
        "raw completion",
        "scratchpad",
        "system prompt",
        "developer prompt"
    ];

    private readonly IAgentRunAuditQueryService _queryService;

    public AgentRunsV1Controller(IAgentRunAuditQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [HttpGet]
    public ActionResult<AgentRunApiEnvelope<AgentRunListResponseDto>> List(
        [FromQuery] int projectId,
        [FromQuery] string? agentId = null,
        [FromQuery] AgentKind? agentKind = null,
        [FromQuery] IronDev.Core.Agents.Audit.AgentRunStatus? status = null,
        [FromQuery] AgentRunTriggerType? triggerType = null,
        [FromQuery] DateTimeOffset? createdAfterUtc = null,
        [FromQuery] DateTimeOffset? createdBeforeUtc = null,
        [FromQuery] string? runId = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0)
    {
        var unsupported = UnsupportedQueryKeys(ListQueryKeys);
        if (unsupported.Count > 0)
            return BadRequest(Envelope<AgentRunListResponseDto>("validation_error", null, errors: unsupported.Select(UnsupportedFilter).ToArray()));

        if (projectId <= 0)
            return BadRequest(Envelope<AgentRunListResponseDto>("validation_error", null, errors: [ValidationError("projectId", "Project id is required.")]));

        var response = _queryService.ListAgentRuns(projectId.ToString(), new AgentRunAuditListQuery
        {
            AgentId = agentId,
            AgentKind = agentKind,
            Status = status,
            TriggerType = triggerType,
            FromUtc = createdAfterUtc,
            ToUtc = createdBeforeUtc,
            Take = take,
            Skip = skip
        });

        var data = Sanitise(response with
        {
            Items = response.Items
                .Where(item => string.IsNullOrWhiteSpace(runId) || string.Equals(item.AgentRunId, runId, StringComparison.Ordinal))
                .Where(item => string.IsNullOrWhiteSpace(correlationId) || string.Equals(item.CorrelationId, correlationId, StringComparison.Ordinal))
                .ToArray()
        });

        if (HasError(response.Issues))
            return BadRequest(Envelope("validation_error", data, errors: ToErrors(response.Issues)));

        return Ok(Envelope("succeeded", data, warnings: BoundaryWarnings(data.Items.Select(item => item.HasBoundaryBlocks || item.HasUnsafeAttempt).Any(flag => flag)).ToArray()));
    }

    [HttpGet("{agentRunId}")]
    public ActionResult<AgentRunApiEnvelope<AgentRunDetailResponseDto>> Get([FromQuery] int projectId, string agentRunId)
    {
        var validation = ValidateDetailRequest(projectId, agentRunId);
        if (validation.Count > 0)
            return BadRequest(Envelope<AgentRunDetailResponseDto>("validation_error", null, runId: agentRunId, errors: validation));

        var response = _queryService.GetAgentRun(projectId.ToString(), agentRunId);
        if (IsNotFound(response.Issues))
            return NotFound(Envelope("not_found", response, runId: agentRunId, errors: ToErrors(response.Issues)));

        if (HasError(response.Issues))
            return BadRequest(Envelope("validation_error", response, runId: agentRunId, errors: ToErrors(response.Issues)));

        var safeResponse = Sanitise(response);

        return Ok(Envelope("succeeded", safeResponse, runId: agentRunId, warnings: BoundaryWarnings(safeResponse.Run?.SafetySummary.HasBoundaryBlock == true).ToArray()));
    }

    [HttpGet("{agentRunId}/audit")]
    public ActionResult<AgentRunApiEnvelope<AgentRunAuditSummaryDto>> GetAudit([FromQuery] int projectId, string agentRunId)
    {
        var validation = ValidateDetailRequest(projectId, agentRunId);
        if (validation.Count > 0)
            return BadRequest(Envelope<AgentRunAuditSummaryDto>("validation_error", null, runId: agentRunId, errors: validation));

        var response = _queryService.GetAgentRun(projectId.ToString(), agentRunId);
        if (IsNotFound(response.Issues))
            return NotFound(Envelope<AgentRunAuditSummaryDto>("not_found", null, runId: agentRunId, errors: ToErrors(response.Issues)));

        if (HasError(response.Issues))
            return BadRequest(Envelope<AgentRunAuditSummaryDto>("validation_error", null, runId: agentRunId, errors: ToErrors(response.Issues)));

        var run = response.Run;
        var audit = run is null
            ? null
            : new AgentRunAuditSummaryDto
            {
                ProjectId = response.ProjectId,
                AgentRunId = response.AgentRunId,
                InputCount = run.Inputs.Count,
                OutputCount = run.Outputs.Count,
                ThoughtLedgerCount = run.ThoughtLedger.Count,
                CapabilityUseCount = run.CapabilityUses.Count,
                BoundaryDecisionCount = run.BoundaryDecisions.Count,
                EvidenceReferences = Sanitise(run.Inputs.SelectMany(input => input.EvidenceRefs)
                    .Concat(run.Outputs.SelectMany(output => output.EvidenceRefs))
                    .Concat(run.ThoughtLedger.SelectMany(thought => thought.EvidenceRefs))
                    .Concat(run.BoundaryDecisions.SelectMany(boundary => boundary.EvidenceRefs))
                    .ToArray(), run.SafetySummary.ContainsRawPrivateReasoning)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                SafetySummary = Sanitise(run.SafetySummary),
                BoundaryStatus = BoundaryStatus(run.SafetySummary.HasBoundaryBlock || run.SafetySummary.HasBlockedCapabilityAttempt),
                AuditIsApproval = false,
                EvidenceIsPermission = false
            };

        return Ok(Envelope("succeeded", audit, runId: agentRunId, evidenceId: audit?.EvidenceReferences.FirstOrDefault() ?? string.Empty, warnings: BoundaryWarnings(audit?.SafetySummary.HasBoundaryBlock == true).ToArray()));
    }

    private static AgentRunListResponseDto Sanitise(AgentRunListResponseDto response) =>
        response with
        {
            Items = response.Items.Select(Sanitise).ToArray(),
            Issues = Sanitise(response.Issues)
        };

    private static AgentRunListItemDto Sanitise(AgentRunListItemDto item) =>
        item with
        {
            AgentName = SanitiseText(item.AgentName),
            RequestedByUserId = SanitiseText(item.RequestedByUserId),
            CorrelationId = SanitiseText(item.CorrelationId)
        };

    private static AgentRunDetailResponseDto Sanitise(AgentRunDetailResponseDto response) =>
        response with
        {
            Run = response.Run is null ? null : Sanitise(response.Run),
            Issues = Sanitise(response.Issues)
        };

    private static AgentRunDetailDto Sanitise(AgentRunDetailDto detail) =>
        detail with
        {
            Run = Sanitise(detail.Run),
            AgentDefinition = Sanitise(detail.AgentDefinition),
            Inputs = detail.Inputs.Select(Sanitise).ToArray(),
            Outputs = detail.Outputs.Select(Sanitise).ToArray(),
            CapabilityUses = detail.CapabilityUses.Select(Sanitise).ToArray(),
            BoundaryDecisions = detail.BoundaryDecisions.Select(Sanitise).ToArray(),
            ThoughtLedger = detail.ThoughtLedger.Select(Sanitise).ToArray(),
            Steps = detail.Steps.Select(Sanitise).ToArray(),
            SafetySummary = Sanitise(detail.SafetySummary)
        };

    private static AgentRunRecordDto Sanitise(AgentRunRecordDto run) =>
        run with
        {
            AgentName = SanitiseText(run.AgentName),
            RequestedByUserId = SanitiseText(run.RequestedByUserId),
            RequestedByAgentId = SanitiseText(run.RequestedByAgentId),
            RequestSummary = SanitiseText(run.RequestSummary),
            Purpose = SanitiseText(run.Purpose)
        };

    private static AgentDefinitionSnapshotDto Sanitise(AgentDefinitionSnapshotDto definition) =>
        definition with
        {
            Name = SanitiseText(definition.Name),
            PersonaDisplayName = SanitiseText(definition.PersonaDisplayName),
            Purpose = SanitiseText(definition.Purpose)
        };

    private static AgentRunInputRefDto Sanitise(AgentRunInputRefDto input) =>
        input with
        {
            Source = SanitiseText(input.Source, input.ContainsRawPrivateReasoning),
            Summary = SanitiseText(input.Summary, input.ContainsRawPrivateReasoning),
            EvidenceRefs = Sanitise(input.EvidenceRefs, input.ContainsRawPrivateReasoning)
        };

    private static AgentRunOutputRefDto Sanitise(AgentRunOutputRefDto output) =>
        output with
        {
            Summary = SanitiseText(output.Summary, output.ContainsRawPrivateReasoning),
            EvidenceRefs = Sanitise(output.EvidenceRefs, output.ContainsRawPrivateReasoning)
        };

    private static AgentCapabilityUseDto Sanitise(AgentCapabilityUseDto capability) =>
        capability with
        {
            Summary = SanitiseText(capability.Summary),
            BoundaryDecisionId = SanitiseText(capability.BoundaryDecisionId),
            EvidenceRef = SanitiseText(capability.EvidenceRef)
        };

    private static AgentBoundaryDecisionDto Sanitise(AgentBoundaryDecisionDto boundary) =>
        boundary with
        {
            Reason = SanitiseText(boundary.Reason),
            SourceRefId = SanitiseText(boundary.SourceRefId),
            EvidenceRefs = Sanitise(boundary.EvidenceRefs)
        };

    private static ThoughtLedgerEntryDto Sanitise(ThoughtLedgerEntryDto thought) =>
        thought with
        {
            Summary = SanitiseText(thought.Summary, thought.ContainsRawPrivateReasoning),
            EvidenceRefs = Sanitise(thought.EvidenceRefs, thought.ContainsRawPrivateReasoning),
            Assumptions = Sanitise(thought.Assumptions, thought.ContainsRawPrivateReasoning),
            RejectedAlternatives = Sanitise(thought.RejectedAlternatives, thought.ContainsRawPrivateReasoning),
            Risks = Sanitise(thought.Risks, thought.ContainsRawPrivateReasoning),
            RequiredFollowUps = Sanitise(thought.RequiredFollowUps, thought.ContainsRawPrivateReasoning)
        };

    private static AgentRunStepDto Sanitise(AgentRunStepDto step) =>
        step with
        {
            Summary = SanitiseText(step.Summary, step.ContainsRawPrivateReasoning),
            EvidenceRefs = Sanitise(step.EvidenceRefs, step.ContainsRawPrivateReasoning)
        };

    private static AgentRunSafetySummaryDto Sanitise(AgentRunSafetySummaryDto summary) =>
        summary with
        {
            Warnings = Sanitise(summary.Warnings, summary.ContainsRawPrivateReasoning)
        };

    private static IReadOnlyList<AgentRunAuditQueryIssueDto> Sanitise(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Select(issue => issue with { Message = SanitiseText(issue.Message) }).ToArray();

    private static IReadOnlyList<string> Sanitise(IReadOnlyList<string> values, bool forceRedact = false) =>
        values.Select(value => SanitiseText(value, forceRedact)).ToArray();

    private static string SanitiseText(string value, bool forceRedact = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (forceRedact || PrivateReasoningMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return RedactedPrivateReasoning;

        return value;
    }

    private IReadOnlyList<AgentRunApiErrorDto> ValidateDetailRequest(int projectId, string agentRunId)
    {
        var errors = new List<AgentRunApiErrorDto>();
        var unsupported = UnsupportedQueryKeys(DetailQueryKeys);
        errors.AddRange(unsupported.Select(UnsupportedFilter));

        if (projectId <= 0)
            errors.Add(ValidationError("projectId", "Project id is required."));
        if (string.IsNullOrWhiteSpace(agentRunId))
            errors.Add(ValidationError("agentRunId", "Agent run id is required."));

        return errors;
    }

    private IReadOnlyList<string> UnsupportedQueryKeys(IReadOnlySet<string> allowed) =>
        Request.Query.Keys.Where(key => !allowed.Contains(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    private static AgentRunApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string runId = "",
        string evidenceId = "",
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<AgentRunApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            RunId = runId,
            EvidenceId = evidenceId,
            Boundary = BoundaryStatus(false),
            MutationOccurred = false,
            HumanApprovalRequired = false,
            Warnings = warnings ?? [],
            Errors = errors ?? []
        };

    private static AgentRunBoundaryStatusDto BoundaryStatus(bool hasBoundaryWarnings) =>
        new()
        {
            ReadOnlyInspection = true,
            AuditIsApproval = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            ModelOutputIsAuthority = false,
            HumanReviewRequiredForSourceApply = true,
            HumanReviewRequiredForMemoryPromotion = true,
            HasBoundaryWarnings = hasBoundaryWarnings
        };

    private static IEnumerable<string> BoundaryWarnings(bool hasBoundaryWarnings)
    {
        yield return "Agent run API v1 is inspection-only. It does not start, execute, approve, apply, promote, or govern agent runs.";
        yield return "Audit and evidence are accountability records, not approval or execution permission.";
        if (hasBoundaryWarnings)
            yield return "The inspected run contains boundary warnings or blocked capability evidence.";
    }

    private static AgentRunApiErrorDto UnsupportedFilter(string filter) =>
        new()
        {
            Category = "unsupported_filter",
            Code = "AGENT_RUNS_API_UNSUPPORTED_FILTER",
            Message = $"Unsupported filter: {SanitiseText(filter)}."
        };

    private static AgentRunApiErrorDto ValidationError(string field, string message) =>
        new()
        {
            Category = "validation_error",
            Code = "AGENT_RUNS_API_VALIDATION_ERROR",
            Message = message,
            Field = field
        };

    private static IReadOnlyList<AgentRunApiErrorDto> ToErrors(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Select(issue => new AgentRunApiErrorDto
        {
            Category = string.Equals(issue.Severity, "not_found", StringComparison.OrdinalIgnoreCase) ? "not_found" : "validation_error",
            Code = issue.Code,
            Message = SanitiseText(issue.Message)
        }).ToArray();

    private static bool HasError(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    private static bool IsNotFound(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Any(issue => string.Equals(issue.Code, AgentRunAuditQueryService.AgentRunNotFound, StringComparison.Ordinal));
}

public sealed record AgentRunApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string RunId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required AgentRunBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<AgentRunApiErrorDto> Errors { get; init; } = [];
}

public sealed record AgentRunBoundaryStatusDto
{
    public bool ReadOnlyInspection { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
    public bool HasBoundaryWarnings { get; init; }
}

public sealed record AgentRunApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record AgentRunAuditSummaryDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public int InputCount { get; init; }
    public int OutputCount { get; init; }
    public int ThoughtLedgerCount { get; init; }
    public int CapabilityUseCount { get; init; }
    public int BoundaryDecisionCount { get; init; }
    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
    public required AgentRunSafetySummaryDto SafetySummary { get; init; }
    public required AgentRunBoundaryStatusDto BoundaryStatus { get; init; }
    public bool AuditIsApproval { get; init; }
    public bool EvidenceIsPermission { get; init; }
}


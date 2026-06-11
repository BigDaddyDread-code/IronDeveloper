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

        var data = response with
        {
            Items = response.Items
                .Where(item => string.IsNullOrWhiteSpace(runId) || string.Equals(item.AgentRunId, runId, StringComparison.Ordinal))
                .Where(item => string.IsNullOrWhiteSpace(correlationId) || string.Equals(item.CorrelationId, correlationId, StringComparison.Ordinal))
                .ToArray()
        };

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

        return Ok(Envelope("succeeded", response, runId: agentRunId, warnings: BoundaryWarnings(response.Run?.SafetySummary.HasBoundaryBlock == true).ToArray()));
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
                EvidenceReferences = run.Inputs.SelectMany(input => input.EvidenceRefs)
                    .Concat(run.Outputs.SelectMany(output => output.EvidenceRefs))
                    .Concat(run.ThoughtLedger.SelectMany(thought => thought.EvidenceRefs))
                    .Concat(run.BoundaryDecisions.SelectMany(boundary => boundary.EvidenceRefs))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                SafetySummary = run.SafetySummary,
                BoundaryStatus = BoundaryStatus(run.SafetySummary.HasBoundaryBlock || run.SafetySummary.HasBlockedCapabilityAttempt),
                AuditIsApproval = false,
                EvidenceIsPermission = false
            };

        return Ok(Envelope("succeeded", audit, runId: agentRunId, evidenceId: audit?.EvidenceReferences.FirstOrDefault() ?? string.Empty, warnings: BoundaryWarnings(audit?.SafetySummary.HasBoundaryBlock == true).ToArray()));
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
            Message = $"Unsupported filter: {filter}."
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
            Message = issue.Message
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


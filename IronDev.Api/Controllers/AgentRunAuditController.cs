using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:int}/agent-runs")]
public sealed class AgentRunAuditController : ControllerBase
{
    private readonly IAgentRunAuditQueryService _queryService;

    public AgentRunAuditController(IAgentRunAuditQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [HttpGet]
    public ActionResult<AgentRunListResponseDto> List(
        int projectId,
        [FromQuery] string? agentId = null,
        [FromQuery] AgentKind? agentKind = null,
        [FromQuery] IronDev.Core.Agents.Audit.AgentRunStatus? status = null,
        [FromQuery] AgentRunTriggerType? triggerType = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0)
    {
        var response = _queryService.ListAgentRuns(projectId.ToString(), new AgentRunAuditListQuery
        {
            AgentId = agentId,
            AgentKind = agentKind,
            Status = status,
            TriggerType = triggerType,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Take = take,
            Skip = skip
        });

        return HasError(response.Issues) ? BadRequest(response) : Ok(response);
    }

    [HttpGet("{agentRunId}")]
    public ActionResult<AgentRunDetailResponseDto> Get(int projectId, string agentRunId)
    {
        var response = _queryService.GetAgentRun(projectId.ToString(), agentRunId);
        return ToReadResult(response, response.Issues);
    }

    [HttpGet("{agentRunId}/thought-ledger")]
    public ActionResult<AgentRunThoughtLedgerResponseDto> GetThoughtLedger(int projectId, string agentRunId)
    {
        var response = _queryService.GetThoughtLedger(projectId.ToString(), agentRunId);
        return ToReadResult(response, response.Issues);
    }

    [HttpGet("{agentRunId}/capabilities")]
    public ActionResult<AgentRunCapabilitiesResponseDto> GetCapabilities(int projectId, string agentRunId)
    {
        var response = _queryService.GetCapabilities(projectId.ToString(), agentRunId);
        return ToReadResult(response, response.Issues);
    }

    [HttpGet("{agentRunId}/boundaries")]
    public ActionResult<AgentRunBoundariesResponseDto> GetBoundaries(int projectId, string agentRunId)
    {
        var response = _queryService.GetBoundaryDecisions(projectId.ToString(), agentRunId);
        return ToReadResult(response, response.Issues);
    }

    [HttpGet("{agentRunId}/outputs")]
    public ActionResult<AgentRunOutputsResponseDto> GetOutputs(int projectId, string agentRunId)
    {
        var response = _queryService.GetOutputs(projectId.ToString(), agentRunId);
        return ToReadResult(response, response.Issues);
    }

    [HttpGet("{agentRunId}/inputs")]
    public ActionResult<AgentRunInputsResponseDto> GetInputs(int projectId, string agentRunId)
    {
        var response = _queryService.GetInputs(projectId.ToString(), agentRunId);
        return ToReadResult(response, response.Issues);
    }

    private ActionResult<TResponse> ToReadResult<TResponse>(
        TResponse response,
        IReadOnlyList<AgentRunAuditQueryIssueDto> issues)
    {
        if (issues.Any(issue => string.Equals(issue.Code, AgentRunAuditQueryService.AgentRunNotFound, StringComparison.Ordinal)))
            return NotFound(response);

        if (HasError(issues))
            return BadRequest(response);

        return Ok(response);
    }

    private static bool HasError(IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
}

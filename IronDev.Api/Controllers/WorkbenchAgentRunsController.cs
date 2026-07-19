using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}/agent-runs")]
public sealed class WorkbenchAgentRunsController : ControllerBase
{
    private readonly IWorkbenchAgentRunService _runs;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchAgentRunsController(IWorkbenchAgentRunService runs, ICurrentTenantContext tenant)
    {
        _runs = runs;
        _tenant = tenant;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SubmitWorkbenchAgentRunResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SubmitWorkbenchAgentRunResult>> Submit(
        int projectId,
        SubmitWorkbenchAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var result = await _runs.SubmitAsync(
                new SubmitWorkbenchAgentRunCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ChatSessionId,
                    request.Message),
                cancellationToken);
            return AcceptedAtAction(nameof(Get), new { projectId, agentRunId = result.AgentRunId }, result);
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpGet("{agentRunId:guid}")]
    [ProducesResponseType(typeof(WorkbenchAgentRunSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkbenchAgentRunSnapshot>> Get(
        int projectId,
        Guid agentRunId,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _runs.GetAsync(
                _tenant.TenantId,
                actor.UserId,
                projectId,
                agentRunId,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("{agentRunId:guid}/cancel")]
    [ProducesResponseType(typeof(CancelWorkbenchAgentRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CancelWorkbenchAgentRunResult>> Cancel(
        int projectId,
        Guid agentRunId,
        CancelWorkbenchAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _runs.CancelAsync(
                new CancelWorkbenchAgentRunCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    agentRunId,
                    request.ClientOperationId),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    private CurrentUserContext CurrentActor() =>
        new(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private ActionResult MapFailure(Exception exception) => exception switch
    {
        WorkbenchAgentRunValidationException validation =>
            BadRequest(new { error = "workbench_agent_run_invalid", message = validation.Message }),
        ProjectStartOperationMismatchException mismatch =>
            Conflict(new { error = ProjectStartOperationMismatchException.ErrorCode, message = mismatch.Message }),
        WorkbenchLeaseFenceException fence =>
            Conflict(new { error = WorkbenchLeaseFenceException.ErrorCode, message = fence.Message }),
        WorkbenchProjectNotAccessibleException or WorkbenchAgentRunNotFoundException =>
            NotFound(new { error = "agent_run_not_found", message = "Agent run not found or you no longer have access." }),
        _ => throw exception
    };

    public sealed record SubmitWorkbenchAgentRunRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ChatSessionId,
        string Message);

    public sealed record CancelWorkbenchAgentRunRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId);
}

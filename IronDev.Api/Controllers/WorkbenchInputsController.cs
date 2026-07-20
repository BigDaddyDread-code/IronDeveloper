using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}/inputs")]
public sealed class WorkbenchInputsController : ControllerBase
{
    private readonly IWorkbenchInputService _inputs;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchInputsController(
        IWorkbenchInputService inputs,
        ICurrentTenantContext tenant)
    {
        _inputs = inputs;
        _tenant = tenant;
    }

    [HttpPost]
    [ProducesResponseType(typeof(DispatchWorkbenchInputResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DispatchWorkbenchInputResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DispatchWorkbenchInputResult>> Dispatch(
        int projectId,
        DispatchWorkbenchInputRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var result = await _inputs.DispatchAsync(
                new DispatchWorkbenchInputCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ChatSessionId,
                    request.ComposerText),
                cancellationToken);

            if (result.Kind == WorkbenchInputKinds.CommandRejected)
            {
                return BadRequest(new
                {
                    error = "workbench_command_unknown",
                    message = result.Message,
                    rawCommandToken = result.RawCommandToken
                });
            }

            return result.Kind == WorkbenchInputKinds.AgentRun
                ? StatusCode(StatusCodes.Status202Accepted, result)
                : Ok(result);
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
        WorkbenchInputValidationException or WorkbenchAgentRunValidationException =>
            BadRequest(new { error = "workbench_input_invalid", message = exception.Message }),
        ProjectStartOperationMismatchException mismatch =>
            Conflict(new { error = ProjectStartOperationMismatchException.ErrorCode, message = mismatch.Message }),
        WorkbenchChatSessionBindingException binding =>
            Conflict(new { error = WorkbenchChatSessionBindingException.ErrorCode, message = binding.Message }),
        WorkbenchAgentRunAlreadyActiveException active =>
            Conflict(new
            {
                error = WorkbenchAgentRunAlreadyActiveException.ErrorCode,
                message = active.Message,
                agentRunId = active.AgentRunId
            }),
        WorkbenchAgentRunUnavailableException unavailable =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = WorkbenchAgentRunUnavailableException.ErrorCode,
                message = unavailable.Message,
                failureCategory = unavailable.FailureCategory,
                retryable = false
            }),
        WorkbenchLeaseFenceException fence =>
            Conflict(new { error = WorkbenchLeaseFenceException.ErrorCode, message = fence.Message }),
        WorkbenchProjectNotAccessibleException =>
            NotFound(new
            {
                error = "workbench_input_not_found",
                message = "Project input route not found or you no longer have access."
            }),
        _ => throw exception
    };

    public sealed record DispatchWorkbenchInputRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long? ChatSessionId,
        string ComposerText);
}

using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}")]
public sealed class WorkbenchProjectUnderstandingController : ControllerBase
{
    private readonly IWorkbenchProjectUnderstandingService _understanding;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchProjectUnderstandingController(
        IWorkbenchProjectUnderstandingService understanding,
        ICurrentTenantContext tenant)
    {
        _understanding = understanding;
        _tenant = tenant;
    }

    [HttpGet("understanding")]
    [ProducesResponseType(typeof(ProjectUnderstandingSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectUnderstandingSnapshot>> Get(
        int projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _understanding.GetAsync(
                _tenant.TenantId,
                CurrentActor().UserId,
                projectId,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPut("understanding/facts/{factKey}")]
    [ProducesResponseType(typeof(PutProjectUnderstandingFactResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PutProjectUnderstandingFactResult>> PutFact(
        int projectId,
        string factKey,
        PutProjectUnderstandingFactRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _understanding.PutFactAsync(
                new PutProjectUnderstandingFactCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ExpectedUnderstandingRevision,
                    factKey,
                    request.Action,
                    request.ConflictId,
                    request.Value,
                    request.UserLocked),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("rename-proposals/{proposalId:guid}/accept")]
    [ProducesResponseType(typeof(AcceptProjectRenameProposalResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AcceptProjectRenameProposalResult>> AcceptRename(
        int projectId,
        Guid proposalId,
        AcceptProjectRenameProposalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _understanding.AcceptRenameAsync(
                new AcceptProjectRenameProposalCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    proposalId,
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
        ProjectUnderstandingValidationException validation =>
            BadRequest(new { error = "project_understanding_invalid", message = validation.Message }),
        ProjectStartOperationMismatchException mismatch =>
            Conflict(new { error = ProjectStartOperationMismatchException.ErrorCode, message = mismatch.Message }),
        ProjectUnderstandingRevisionConflictException revision =>
            Conflict(new
            {
                error = ProjectUnderstandingRevisionConflictException.ErrorCode,
                message = revision.Message,
                currentRevision = revision.CurrentRevision
            }),
        ProjectRenameProposalNotPendingException rename =>
            Conflict(new { error = ProjectRenameProposalNotPendingException.ErrorCode, message = rename.Message }),
        ProjectUnderstandingConflictNotOpenException conflict =>
            Conflict(new { error = ProjectUnderstandingConflictNotOpenException.ErrorCode, message = conflict.Message }),
        ProjectRenameProposalStaleException staleRename =>
            Conflict(new { error = ProjectRenameProposalStaleException.ErrorCode, message = staleRename.Message }),
        WorkbenchLeaseFenceException fence =>
            Conflict(new { error = WorkbenchLeaseFenceException.ErrorCode, message = fence.Message }),
        WorkbenchProjectNotAccessibleException =>
            NotFound(new { error = "project_not_found", message = "Project not found or you no longer have access." }),
        _ => throw exception
    };

    public sealed record PutProjectUnderstandingFactRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedUnderstandingRevision,
        string Action,
        Guid? ConflictId,
        string? Value,
        bool? UserLocked);

    public sealed record AcceptProjectRenameProposalRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId);
}

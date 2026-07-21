using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}/ticket-proposal-sets")]
public sealed class WorkbenchTicketProposalsController : ControllerBase
{
    private readonly IWorkbenchTicketProposalService _proposals;
    private readonly IWorkbenchTicketProposalCommitService _commits;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchTicketProposalsController(
        IWorkbenchTicketProposalService proposals,
        IWorkbenchTicketProposalCommitService commits,
        ICurrentTenantContext tenant)
    {
        _proposals = proposals;
        _commits = commits;
        _tenant = tenant;
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(TicketProposalSetReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalSetReadModel>> GetCurrent(
        int projectId,
        [FromQuery] long workbenchSessionId,
        [FromQuery] long leaseEpoch,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await _proposals.GetCurrentAsync(
                _tenant.TenantId,
                CurrentActor().UserId,
                projectId,
                workbenchSessionId,
                leaseEpoch,
                cancellationToken);
            return value is null ? NoContent() : Ok(value);
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpGet("{ticketProposalSetId:guid}")]
    [ProducesResponseType(typeof(TicketProposalSetReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalSetReadModel>> Get(
        int projectId,
        Guid ticketProposalSetId,
        [FromQuery] long workbenchSessionId,
        [FromQuery] long leaseEpoch,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proposals.GetAsync(
                _tenant.TenantId,
                CurrentActor().UserId,
                projectId,
                workbenchSessionId,
                leaseEpoch,
                ticketProposalSetId,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpGet("{ticketProposalSetId:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<TicketProposalSetHistoryEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<TicketProposalSetHistoryEntry>>> GetHistory(
        int projectId,
        Guid ticketProposalSetId,
        [FromQuery] long workbenchSessionId,
        [FromQuery] long leaseEpoch,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proposals.GetHistoryAsync(
                _tenant.TenantId,
                CurrentActor().UserId,
                projectId,
                workbenchSessionId,
                leaseEpoch,
                ticketProposalSetId,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPatch("{ticketProposalSetId:guid}/proposals/{ticketProposalId:guid}")]
    [ProducesResponseType(typeof(TicketProposalSetMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalSetMutationResult>> Edit(
        int projectId,
        Guid ticketProposalSetId,
        Guid ticketProposalId,
        EditTicketProposalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proposals.EditAsync(
                new EditTicketProposalCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    ticketProposalSetId,
                    ticketProposalId,
                    request.ExpectedProposalSetRevision,
                    request.Title,
                    request.Problem,
                    request.ProposedChange,
                    request.AcceptanceCriteria),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("{ticketProposalSetId:guid}/reorder")]
    [ProducesResponseType(typeof(TicketProposalSetMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalSetMutationResult>> Reorder(
        int projectId,
        Guid ticketProposalSetId,
        ReorderTicketProposalsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proposals.ReorderAsync(
                new ReorderTicketProposalsCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    ticketProposalSetId,
                    request.ExpectedProposalSetRevision,
                    request.OrderedProposalIds),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("{ticketProposalSetId:guid}/proposals/{ticketProposalId:guid}/remove")]
    [ProducesResponseType(typeof(TicketProposalSetMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalSetMutationResult>> Remove(
        int projectId,
        Guid ticketProposalSetId,
        Guid ticketProposalId,
        TicketProposalMutationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proposals.RemoveAsync(
                new RemoveTicketProposalCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    ticketProposalSetId,
                    ticketProposalId,
                    request.ExpectedProposalSetRevision),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("{ticketProposalSetId:guid}/issues/{issueId:guid}/resolve")]
    [ProducesResponseType(typeof(TicketProposalSetMutationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalSetMutationResult>> ResolveIssue(
        int projectId,
        Guid ticketProposalSetId,
        Guid issueId,
        ResolveTicketProposalIssueRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proposals.ResolveIssueAsync(
                new ResolveTicketProposalIssueCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    ticketProposalSetId,
                    issueId,
                    request.ExpectedProposalSetRevision,
                    request.Resolution),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("{ticketProposalSetId:guid}/regenerations")]
    [ProducesResponseType(typeof(SubmitWorkbenchAgentRunResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SubmitWorkbenchAgentRunResult>> Regenerate(
        int projectId,
        Guid ticketProposalSetId,
        RegenerateTicketProposalSetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _proposals.RegenerateAsync(
                new RegenerateTicketProposalSetCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ChatSessionId,
                    ticketProposalSetId,
                    request.ExpectedProposalSetRevision,
                    request.Instruction),
                cancellationToken);
            return StatusCode(StatusCodes.Status202Accepted, result);
        }
        catch (Exception exception)
        {
            return MapFailure(exception);
        }
    }

    [HttpPost("{ticketProposalSetId:guid}/commits")]
    [ProducesResponseType(typeof(TicketProposalCommitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketProposalCommitResult>> Commit(
        int projectId,
        Guid ticketProposalSetId,
        CommitTicketProposalSetRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _commits.CommitAsync(
                new CommitTicketProposalSetCommand(
                    _tenant.TenantId,
                    CurrentActor().UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    ticketProposalSetId,
                    request.ExpectedProposalSetRevision),
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
        TicketProposalValidationException validation =>
            BadRequest(new { error = "ticket_proposal_invalid", message = validation.Message }),
        WorkbenchAgentRunValidationException validation =>
            BadRequest(new { error = "ticket_proposal_regeneration_invalid", message = validation.Message }),
        ProjectStartOperationMismatchException mismatch =>
            Conflict(new { error = ProjectStartOperationMismatchException.ErrorCode, message = mismatch.Message }),
        TicketProposalRevisionConflictException revision =>
            Conflict(new
            {
                error = TicketProposalRevisionConflictException.ErrorCode,
                message = revision.Message,
                currentRevision = revision.CurrentRevision
            }),
        TicketProposalIssueNotOpenException issue =>
            Conflict(new { error = TicketProposalIssueNotOpenException.ErrorCode, message = issue.Message }),
        TicketProposalDependencyException dependency =>
            Conflict(new { error = TicketProposalDependencyException.ErrorCode, message = dependency.Message }),
        TicketProposalFinalRemovalException finalRemoval =>
            Conflict(new { error = TicketProposalFinalRemovalException.ErrorCode, message = finalRemoval.Message }),
        TicketProposalBlockingIssuesException blockingIssues =>
            Conflict(new { error = TicketProposalBlockingIssuesException.ErrorCode, message = blockingIssues.Message }),
        TicketProposalAlreadyCommittedException committed =>
            Conflict(new { error = TicketProposalAlreadyCommittedException.ErrorCode, message = committed.Message }),
        TicketProposalSetNotReadyException notReady =>
            Conflict(new { error = TicketProposalSetNotReadyException.ErrorCode, message = notReady.Message }),
        TicketProposalCommitBoundaryException boundary =>
            Conflict(new { error = TicketProposalCommitBoundaryException.ErrorCode, message = boundary.Message }),
        TicketProposalProjectNotShapingException lifecycle =>
            Conflict(new { error = TicketProposalProjectNotShapingException.ErrorCode, message = lifecycle.Message }),
        WorkbenchLeaseFenceException fence =>
            Conflict(new { error = WorkbenchLeaseFenceException.ErrorCode, message = fence.Message }),
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
        WorkbenchProjectNotAccessibleException =>
            NotFound(new
            {
                error = "ticket_proposal_set_not_found",
                message = "Ticket proposal set not found or you no longer have access."
            }),
        _ => throw exception
    };

    public sealed record EditTicketProposalRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedProposalSetRevision,
        string Title,
        string Problem,
        string ProposedChange,
        IReadOnlyList<string> AcceptanceCriteria);

    public sealed record ReorderTicketProposalsRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedProposalSetRevision,
        IReadOnlyList<Guid> OrderedProposalIds);

    public sealed record TicketProposalMutationRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedProposalSetRevision);

    public sealed record ResolveTicketProposalIssueRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedProposalSetRevision,
        string Resolution);

    public sealed record RegenerateTicketProposalSetRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ChatSessionId,
        long ExpectedProposalSetRevision,
        string Instruction);
}

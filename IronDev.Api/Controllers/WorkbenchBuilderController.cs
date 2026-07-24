using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}/builder")]
public sealed class WorkbenchBuilderController : ControllerBase
{
    private readonly IWorkbenchBuilderAuthorizationService _builder;
    private readonly IWorkbenchBuilderPromptPreparationService _promptPreparation;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchBuilderController(
        IWorkbenchBuilderAuthorizationService builder,
        IWorkbenchBuilderPromptPreparationService promptPreparation,
        ICurrentTenantContext tenant)
    {
        _builder = builder;
        _promptPreparation = promptPreparation;
        _tenant = tenant;
    }

    [HttpPost("agent-runs")]
    [ProducesResponseType(typeof(PreparedBuilderAgentRun), StatusCodes.Status201Created)]
    public async Task<ActionResult<PreparedBuilderAgentRun>> PrepareAgentRun(
        int projectId,
        PrepareBuilderAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var result = await _promptPreparation.PrepareAsync(
                new PrepareBuilderAgentRunCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.BuilderExecutionAuthorizationId,
                    request.BuilderWorkPackageCoreId,
                    request.ExpectedCoreSha256),
                cancellationToken);
            return result.IsReplay
                ? Ok(result)
                : StatusCode(StatusCodes.Status201Created, result);
        }
        catch (BuilderPromptPreparationValidationException exception)
        {
            return BadRequest(Error(
                BuilderPromptPreparationValidationException.ErrorCode,
                exception.Message));
        }
        catch (BuilderPromptPreparationOperationMismatchException exception)
        {
            return Conflict(Error(
                BuilderPromptPreparationOperationMismatchException.ErrorCode,
                exception.Message));
        }
        catch (BuilderPromptPreparationConflictException exception)
        {
            return Conflict(new
            {
                error = BuilderPromptPreparationConflictException.ErrorCode,
                reasonCode = exception.ReasonCode,
                message = exception.Message
            });
        }
        catch (BuilderPromptPreparationIntegrityException)
        {
            return UnprocessableEntity(Error(
                BuilderPromptPreparationIntegrityException.ErrorCode,
                "Builder prompt preparation failed integrity verification."));
        }
        catch (WorkbenchLeaseFenceException exception)
        {
            return Conflict(Error(WorkbenchLeaseFenceException.ErrorCode, exception.Message));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(WorkbenchBuilderContext), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkbenchBuilderContext>> GetContext(
        int projectId,
        [FromQuery] long? ticketId,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _builder.GetContextAsync(
                new GetWorkbenchBuilderContextQuery(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    ticketId),
                cancellationToken));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (BuilderAuthorizationValidationException exception)
        {
            return BadRequest(Error(BuilderAuthorizationValidationException.ErrorCode, exception.Message));
        }
        catch (BuilderAuthorizationIntegrityException)
        {
            return UnprocessableEntity(Error(
                BuilderAuthorizationIntegrityException.ErrorCode,
                "The Builder work-package authority failed integrity verification."));
        }
    }

    [HttpPost("work-packages")]
    [ProducesResponseType(typeof(BuilderWorkPackageResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<BuilderWorkPackageResult>> CreateWorkPackage(
        int projectId,
        CreateBuilderWorkPackageRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var result = await _builder.CreateWorkPackageAsync(
                new CreateBuilderWorkPackageCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.TicketIds),
                cancellationToken);
            return result.IsReplay
                ? Ok(result)
                : StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception exception)
        {
            return MapMutationFailure(exception);
        }
    }

    [HttpPost("authorizations")]
    [ProducesResponseType(typeof(BuilderAuthorizationResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<BuilderAuthorizationResult>> GrantAuthorization(
        int projectId,
        GrantBuilderAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var result = await _builder.GrantAsync(
                new GrantBuilderExecutionAuthorizationCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.BuilderWorkPackageCoreId,
                    request.ExpectedCoreHash),
                cancellationToken);
            return result.IsReplay
                ? Ok(result)
                : StatusCode(StatusCodes.Status201Created, result);
        }
        catch (Exception exception)
        {
            return MapMutationFailure(exception);
        }
    }

    [HttpPost("authorizations/{authorizationId:guid}/revocations")]
    [ProducesResponseType(typeof(BuilderAuthorizationRevocationResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BuilderAuthorizationRevocationResult>> RevokeAuthorization(
        int projectId,
        Guid authorizationId,
        RevokeBuilderAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _builder.RevokeAsync(
                new RevokeBuilderExecutionAuthorizationCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    authorizationId),
                cancellationToken));
        }
        catch (Exception exception)
        {
            return MapMutationFailure(exception);
        }
    }

    private ActionResult MapMutationFailure(Exception exception) => exception switch
    {
        BuilderAuthorizationForbiddenException forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            Error(BuilderAuthorizationForbiddenException.ErrorCode, forbidden.Message)),
        WorkbenchProjectNotAccessibleException => ProjectNotFound(),
        WorkbenchLeaseFenceException lease => Conflict(
            Error(WorkbenchLeaseFenceException.ErrorCode, lease.Message)),
        BuilderAuthorizationOperationMismatchException mismatch => Conflict(
            Error(BuilderAuthorizationOperationMismatchException.ErrorCode, mismatch.Message)),
        BuilderAuthorizationNotAllowedException notAllowed => Conflict(new
        {
            error = BuilderAuthorizationNotAllowedException.ErrorCode,
            reasonCode = notAllowed.ReasonCode,
            message = notAllowed.Message
        }),
        BuilderAuthorizationStaleScopeException stale => Conflict(new
        {
            error = BuilderAuthorizationStaleScopeException.ErrorCode,
            reasonCode = stale.ReasonCode,
            message = stale.Message
        }),
        BuilderAuthorizationIntegrityException => UnprocessableEntity(Error(
            BuilderAuthorizationIntegrityException.ErrorCode,
            "The Builder work-package authority failed integrity verification.")),
        BuilderAuthorizationValidationException invalid => BadRequest(
            Error(BuilderAuthorizationValidationException.ErrorCode, invalid.Message)),
        _ => throw exception
    };

    private CurrentUserContext CurrentActor() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private NotFoundObjectResult ProjectNotFound() =>
        NotFound(Error("project_not_found", "Project not found or you no longer have access."));

    private static object Error(string error, string message) => new { error, message };

    public sealed record CreateBuilderWorkPackageRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        IReadOnlyList<long> TicketIds);

    public sealed record GrantBuilderAuthorizationRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        Guid BuilderWorkPackageCoreId,
        string ExpectedCoreHash);

    public sealed record RevokeBuilderAuthorizationRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId);

    public sealed record PrepareBuilderAgentRunRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        Guid BuilderExecutionAuthorizationId,
        Guid BuilderWorkPackageCoreId,
        string ExpectedCoreSha256);
}

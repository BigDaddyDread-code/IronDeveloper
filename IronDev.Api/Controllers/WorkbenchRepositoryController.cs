using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}/repository")]
public sealed class WorkbenchRepositoryController : ControllerBase
{
    private readonly IWorkbenchRepositorySetupService _repositorySetup;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchRepositoryController(
        IWorkbenchRepositorySetupService repositorySetup,
        ICurrentTenantContext tenant)
    {
        _repositorySetup = repositorySetup;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult<RepositorySetupContext>> GetContext(
        int projectId,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _repositorySetup.GetContextAsync(
                new GetRepositorySetupContextQuery(_tenant.TenantId, actor.UserId, projectId),
                cancellationToken));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (RepositorySetupValidationException exception)
        {
            return BadRequest(Error("repository_setup_invalid", exception.Message));
        }
    }

    [HttpPost("setup-plans")]
    public async Task<ActionResult<RepositorySetupPlanPreview>> CreatePlan(
        int projectId,
        CreateRepositorySetupPlanRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _repositorySetup.PreviewAsync(
                new CreateRepositorySetupPlanCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ProfileDefinitionId),
                cancellationToken));
        }
        catch (RepositorySetupUnsupportedProfileException exception)
        {
            return UnprocessableEntity(Error(
                RepositorySetupUnsupportedProfileException.ErrorCode, exception.Message));
        }
        catch (RepositorySetupUnsafePathException exception)
        {
            return UnprocessableEntity(Error(RepositorySetupUnsafePathException.ErrorCode, exception.Message));
        }
        catch (RepositorySetupAlreadyBoundException exception)
        {
            return Conflict(Error(RepositorySetupAlreadyBoundException.ErrorCode, exception.Message));
        }
        catch (WorkbenchLeaseFenceException exception)
        {
            return Conflict(Error(WorkbenchLeaseFenceException.ErrorCode, exception.Message));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (RepositorySetupValidationException exception)
        {
            return BadRequest(Error("repository_setup_invalid", exception.Message));
        }
    }

    [HttpPost("setup-confirmations")]
    public async Task<ActionResult<RepositorySetupConfirmationResult>> Confirm(
        int projectId,
        ConfirmRepositorySetupRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _repositorySetup.ConfirmAsync(
                new ConfirmRepositorySetupCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ExpectedPlanHash),
                cancellationToken));
        }
        catch (RepositorySetupUnsafePathException exception)
        {
            return UnprocessableEntity(Error(RepositorySetupUnsafePathException.ErrorCode, exception.Message));
        }
        catch (RepositorySetupForbiddenException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(RepositorySetupForbiddenException.ErrorCode, exception.Message));
        }
        catch (RepositorySetupAlreadyBoundException exception)
        {
            return Conflict(Error(RepositorySetupAlreadyBoundException.ErrorCode, exception.Message));
        }
        catch (RepositorySetupPlanChangedException exception)
        {
            return Conflict(Error(RepositorySetupPlanChangedException.ErrorCode, exception.Message));
        }
        catch (RepositorySetupPlanNotConfirmableException exception)
        {
            return Conflict(Error(RepositorySetupPlanNotConfirmableException.ErrorCode, exception.Message));
        }
        catch (ProjectStartOperationMismatchException exception)
        {
            return Conflict(Error(ProjectStartOperationMismatchException.ErrorCode, exception.Message));
        }
        catch (WorkbenchLeaseFenceException exception)
        {
            return Conflict(Error(WorkbenchLeaseFenceException.ErrorCode, exception.Message));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (RepositorySetupValidationException exception)
        {
            return BadRequest(Error("repository_setup_invalid", exception.Message));
        }
    }

    public sealed record CreateRepositorySetupPlanRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        string ProfileDefinitionId);

    public sealed record ConfirmRepositorySetupRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        string ExpectedPlanHash);

    private CurrentUserContext CurrentActor() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private NotFoundObjectResult ProjectNotFound() =>
        NotFound(Error("project_not_found", "Project not found or you no longer have access."));

    private static object Error(string error, string message) => new { error, message };
}

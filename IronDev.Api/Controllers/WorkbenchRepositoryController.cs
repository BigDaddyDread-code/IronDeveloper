using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Sandbox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects/{projectId:int}/repository")]
public sealed class WorkbenchRepositoryController : ControllerBase
{
    private readonly IWorkbenchRepositorySetupService _repositorySetup;
    private readonly IWorkbenchRepositoryProvisioningService _repositoryProvisioning;
    private readonly IWorkbenchSandboxQualificationService _sandboxQualification;
    private readonly IWorkbenchRepositoryReadinessService _repositoryReadiness;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchRepositoryController(
        IWorkbenchRepositorySetupService repositorySetup,
        IWorkbenchRepositoryProvisioningService repositoryProvisioning,
        IWorkbenchSandboxQualificationService sandboxQualification,
        IWorkbenchRepositoryReadinessService repositoryReadiness,
        ICurrentTenantContext tenant)
    {
        _repositorySetup = repositorySetup;
        _repositoryProvisioning = repositoryProvisioning;
        _sandboxQualification = sandboxQualification;
        _repositoryReadiness = repositoryReadiness;
        _tenant = tenant;
    }

    [HttpGet("readiness")]
    public async Task<ActionResult<WorkbenchRepositoryReadinessContext>> GetReadiness(
        int projectId,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _repositoryReadiness.GetContextAsync(
                new GetWorkbenchRepositoryReadinessContextQuery(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId),
                cancellationToken));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (RepositoryReadinessValidationException exception)
        {
            return BadRequest(Error("repository_readiness_invalid", exception.Message));
        }
    }

    [HttpPost("readiness-validations")]
    public async Task<ActionResult<RefreshRepositoryReadinessResult>> ValidateTechnicalReadiness(
        int projectId,
        ValidateTechnicalReadinessRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _repositoryReadiness.RefreshAsync(
                new RefreshRepositoryReadinessCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ExpectedRepositoryBindingRevision,
                    request.ExpectedExecutionProfileRevision),
                cancellationToken));
        }
        catch (RepositoryReadinessForbiddenException exception)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                Error(RepositoryReadinessForbiddenException.ErrorCode, exception.Message));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (RepositoryReadinessStaleConfigurationException exception)
        {
            return Conflict(Error(RepositoryReadinessStaleConfigurationException.ErrorCode, exception.Message));
        }
        catch (RepositoryReadinessOperationMismatchException exception)
        {
            return Conflict(Error(RepositoryReadinessOperationMismatchException.ErrorCode, exception.Message));
        }
        catch (WorkbenchLeaseFenceException exception)
        {
            return Conflict(Error(WorkbenchLeaseFenceException.ErrorCode, exception.Message));
        }
        catch (RepositoryReadinessInProgressException exception)
        {
            return Conflict(Error(RepositoryReadinessInProgressException.ErrorCode, exception.Message));
        }
        catch (RepositoryReadinessNotAllowedException exception)
        {
            return Conflict(Error(RepositoryReadinessNotAllowedException.ErrorCode, exception.Message));
        }
        catch (RepositoryReadinessObservationException exception)
        {
            return UnprocessableEntity(new
            {
                error = RepositoryReadinessObservationException.ErrorCode,
                reasonCode = exception.ReasonCode,
                message = exception.Message
            });
        }
        catch (RepositoryReadinessExecutionException exception)
        {
            return UnprocessableEntity(new
            {
                error = RepositoryReadinessExecutionException.ErrorCode,
                reasonCode = exception.ReasonCode,
                message = exception.Message
            });
        }
        catch (RepositoryReadinessIntegrityException)
        {
            return UnprocessableEntity(Error(
                RepositoryReadinessIntegrityException.ErrorCode,
                "Technical-readiness authority or evidence failed integrity validation."));
        }
        catch (RepositoryReadinessValidationException exception)
        {
            return BadRequest(Error("repository_readiness_invalid", exception.Message));
        }
    }

    [HttpGet("sandbox")]
    public async Task<ActionResult<WorkbenchSandboxContext>> GetSandboxContext(
        int projectId,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _sandboxQualification.GetContextAsync(
                new GetWorkbenchSandboxContextQuery(_tenant.TenantId, actor.UserId, projectId),
                cancellationToken));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (SandboxQualificationValidationException exception)
        {
            return BadRequest(Error(SandboxQualificationValidationException.ErrorCode, exception.Message));
        }
    }

    [HttpPost("sandbox-qualifications")]
    public async Task<ActionResult<WorkbenchSandboxQualificationResult>> StartSandboxQualification(
        int projectId,
        StartSandboxQualificationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _sandboxQualification.StartAsync(
                new StartWorkbenchSandboxQualificationCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.ExpectedRepositoryBindingRevision,
                    request.ExpectedExecutionProfileRevision),
                cancellationToken));
        }
        catch (SandboxQualificationForbiddenException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(SandboxQualificationForbiddenException.ErrorCode, exception.Message));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (SandboxQualificationStaleException exception)
        {
            return Conflict(Error(SandboxQualificationStaleException.ErrorCode, exception.Message));
        }
        catch (SandboxQualificationNotAllowedException exception)
        {
            return Conflict(Error(SandboxQualificationNotAllowedException.ErrorCode, exception.Message));
        }
        catch (SandboxQualificationInProgressException exception)
        {
            return Conflict(Error(SandboxQualificationInProgressException.ErrorCode, exception.Message));
        }
        catch (ProjectStartOperationMismatchException exception)
        {
            return Conflict(Error(ProjectStartOperationMismatchException.ErrorCode, exception.Message));
        }
        catch (WorkbenchLeaseFenceException exception)
        {
            return Conflict(Error(WorkbenchLeaseFenceException.ErrorCode, exception.Message));
        }
        catch (SandboxQualificationUnavailableException exception)
        {
            return UnprocessableEntity(new SandboxQualificationUnavailableResponse(
                SandboxQualificationUnavailableException.ErrorCode,
                exception.Capability.ReasonCode,
                exception.Message,
                exception.Capability));
        }
        catch (SandboxQualificationIntegrityException)
        {
            return UnprocessableEntity(Error(
                SandboxQualificationIntegrityException.ErrorCode,
                "The sandbox qualification authority or evidence failed integrity validation."));
        }
        catch (SandboxContractValidationException)
        {
            return UnprocessableEntity(Error(
                SandboxQualificationIntegrityException.ErrorCode,
                "The sandbox qualification authority or evidence failed integrity validation."));
        }
        catch (SandboxQualificationValidationException exception)
        {
            return BadRequest(Error(SandboxQualificationValidationException.ErrorCode, exception.Message));
        }
    }

    [HttpPost("provisionings")]
    public async Task<ActionResult<RepositoryProvisioningResult>> Provision(
        int projectId,
        ProvisionRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            return Ok(await _repositoryProvisioning.ProvisionAsync(
                new ProvisionRepositoryCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.WorkbenchSessionId,
                    request.LeaseEpoch,
                    request.ClientOperationId,
                    request.SetupConfirmationId,
                    request.ExpectedRepositoryBindingRevision,
                    request.ExpectedExecutionProfileRevision),
                cancellationToken));
        }
        catch (RepositoryProvisioningForbiddenException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(RepositoryProvisioningForbiddenException.ErrorCode, exception.Message));
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return ProjectNotFound();
        }
        catch (RepositoryProvisioningStaleException exception)
        {
            return Conflict(Error(RepositoryProvisioningStaleException.ErrorCode, exception.Message));
        }
        catch (RepositoryProvisioningNotAllowedException exception)
        {
            return Conflict(Error(RepositoryProvisioningNotAllowedException.ErrorCode, exception.Message));
        }
        catch (RepositoryProvisioningInProgressException exception)
        {
            return Conflict(Error(RepositoryProvisioningInProgressException.ErrorCode, exception.Message));
        }
        catch (ProjectStartOperationMismatchException exception)
        {
            return Conflict(Error(ProjectStartOperationMismatchException.ErrorCode, exception.Message));
        }
        catch (WorkbenchLeaseFenceException exception)
        {
            return Conflict(Error(WorkbenchLeaseFenceException.ErrorCode, exception.Message));
        }
        catch (RepositoryProvisioningExecutionException exception)
        {
            return UnprocessableEntity(new
            {
                error = RepositoryProvisioningExecutionException.ErrorCode,
                reasonCode = exception.ReasonCode,
                message = exception.Message
            });
        }
        catch (RepositoryProvisioningIntegrityException)
        {
            return UnprocessableEntity(Error(
                RepositoryProvisioningIntegrityException.ErrorCode,
                "The confirmed repository provisioning package failed integrity validation."));
        }
        catch (RepositorySetupValidationException exception)
        {
            return BadRequest(Error("repository_setup_invalid", exception.Message));
        }
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

    public sealed record ProvisionRepositoryRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        Guid SetupConfirmationId,
        long ExpectedRepositoryBindingRevision,
        long ExpectedExecutionProfileRevision);

    public sealed record StartSandboxQualificationRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedRepositoryBindingRevision,
        long ExpectedExecutionProfileRevision);

    public sealed record SandboxQualificationUnavailableResponse(
        string Error,
        string ReasonCode,
        string Message,
        SandboxCapability Capability);

    public sealed record ValidateTechnicalReadinessRequest(
        long WorkbenchSessionId,
        long LeaseEpoch,
        Guid ClientOperationId,
        long ExpectedRepositoryBindingRevision,
        long ExpectedExecutionProfileRevision);

    private CurrentUserContext CurrentActor() => new(
        HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());

    private NotFoundObjectResult ProjectNotFound() =>
        NotFound(Error("project_not_found", "Project not found or you no longer have access."));

    private static object Error(string error, string message) => new { error, message };
}

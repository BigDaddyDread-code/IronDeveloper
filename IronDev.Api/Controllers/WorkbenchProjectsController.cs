using IronDev.Api.Auth;
using IronDev.Core.Auth;
using IronDev.Core.Workbench;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workbench/projects")]
public sealed class WorkbenchProjectsController : ControllerBase
{
    private readonly IWorkbenchProjectEntryService _entry;
    private readonly ICurrentTenantContext _tenant;

    public WorkbenchProjectsController(IWorkbenchProjectEntryService entry, ICurrentTenantContext tenant)
    {
        _entry = entry;
        _tenant = tenant;
    }

    [HttpPost("{projectId:int}/open")]
    public async Task<ActionResult<WorkbenchProjectEntryContext>> Open(
        int projectId,
        OpenWorkbenchProjectRequest request,
        CancellationToken cancellationToken)
    {
        var actor = new CurrentUserContext(HttpContext.RequestServices.GetRequiredService<IHttpContextAccessor>());
        try
        {
            return Ok(await _entry.OpenAsync(
                new OpenWorkbenchProjectCommand(
                    _tenant.TenantId,
                    actor.UserId,
                    projectId,
                    request.ClientOperationId,
                    request.TakeOver),
                cancellationToken));
        }
        catch (ProjectStartValidationException exception)
        {
            return BadRequest(new { error = "workbench_open_invalid", message = exception.Message });
        }
        catch (ProjectStartOperationMismatchException exception)
        {
            return Conflict(new { error = ProjectStartOperationMismatchException.ErrorCode, message = exception.Message });
        }
        catch (WorkbenchLeaseTakeoverRequiredException exception)
        {
            return Conflict(new { error = "workbench_lease_takeover_required", message = exception.Message });
        }
        catch (WorkbenchProjectNotAccessibleException)
        {
            return NotFound(new { error = "project_not_found", message = "Project not found or you no longer have access." });
        }
    }

    public sealed record OpenWorkbenchProjectRequest(Guid ClientOperationId, bool TakeOver = false);
}
